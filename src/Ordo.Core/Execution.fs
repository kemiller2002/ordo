/// The first-class execution envelope, its durable step ledger, and the one
/// computation of legal next actions (ORD-EXEC-070..076, ORD-EXEC-110..115,
/// ORD-EXEC-020/021, ORD-EXEC-140..144).
///
/// An execution is not a chat session, a branch or a provider run. It is a
/// typed object naming the work it serves, the actor performing it, the role
/// and authority it holds, what it may mutate, what judges it, and where it
/// stands. A host (Praxis) projects it into workspaces and processes; a UI
/// renders `LegalActions.compute`. Neither re-derives the rules.
module Ordo.Core.Execution

open System
open Ordo.Core.Identifiers
open Ordo.Core.ExternalEffect
open Ordo.Core.ExecutionRole
open Ordo.Core.Evaluator
open Ordo.Core.Receipt
open Ordo.Core.MutationBoundary
open Ordo.Core.Authorization

/// Where an execution stands.
type ExecutionState =
    | Active
    | Blocked of reason: string
    /// A step's effect is unknown; nothing that could duplicate it may run.
    | AwaitingReconciliation
    | Completed
    | Failed of reason: string
    | Abandoned of reason: string
    /// The executor disappeared. A successor continues under a new execution
    /// whose parent is this one; this one is never recorded as successful.
    | Interrupted

[<RequireQualifiedAccess>]
module ExecutionState =

    let toWire state =
        match state with
        | Active -> "active"
        | Blocked _ -> "blocked"
        | AwaitingReconciliation -> "awaiting-reconciliation"
        | Completed -> "completed"
        | Failed _ -> "failed"
        | Abandoned _ -> "abandoned"
        | Interrupted -> "interrupted"

    let isTerminal state =
        match state with
        | Completed
        | Failed _
        | Abandoned _
        | Interrupted -> true
        | Active
        | Blocked _
        | AwaitingReconciliation -> false

/// The first-class execution envelope (ORD-EXEC-071).
type ExecutionEnvelope =
    { Id: ExecutionId
      WorkItem: WorkItemId
      Actor: ExecutionActor
      Role: ExecutionRole
      Authority: RoleAuthority
      BaselineRevision: string
      CandidateRevision: string option
      /// Attached, never identical to the execution (ORD-EXEC-072).
      Workspace: WorkspaceId option
      Containment: ContainmentStrength
      Boundary: MutationBoundary
      /// The evaluation boundary. `None` when this execution is not judged
      /// by an identified evaluator.
      Evaluator: EvaluatorIdentity option
      HumanOnlyTransitions: string list
      /// Set when this execution continues an interrupted predecessor.
      Parent: ExecutionId option
      StartedAt: DateTimeOffset
      State: ExecutionState }

type EnvelopeError =
    | EmptyBaselineRevision
    /// ORD-EXEC-001 / ORD-EXEC-126: the declared boundary would let this
    /// execution write its own evaluator.
    | BoundaryAdmitsEvaluator of references: string list
    /// A role that holds evaluator-modification authority cannot also be
    /// judged by that evaluator.
    | JudgedExecutionHoldsEvaluatorAuthority
    | DuplicateExecutionId of ExecutionId
    | ParentIsSelf

[<RequireQualifiedAccess>]
module ExecutionEnvelope =

    /// Build an envelope. The evaluator's closure is always added to the
    /// boundary's excluded evaluator references, and a boundary projection
    /// that would admit any evaluator input is refused rather than trimmed.
    let create
        (id: ExecutionId)
        (workItem: WorkItemId)
        (actor: ExecutionActor)
        (authority: RoleAuthority)
        (baseline: string)
        (boundary: MutationBoundary)
        (evaluator: EvaluatorIdentity option)
        (startedAt: DateTimeOffset)
        : Result<ExecutionEnvelope, EnvelopeError> =
        let evaluatorRefs =
            evaluator |> Option.map (fun e -> e.Inputs |> List.map (fun i -> i.Reference)) |> Option.defaultValue []

        let admitted =
            evaluatorRefs
            |> List.filter (fun r ->
                match MutationBoundary.classify { boundary with EvaluatorReferences = [] } r with
                | WithinBoundary _ -> true
                | OutsideBoundary
                | EvaluatorAuthorityMutation -> false)

        if String.IsNullOrWhiteSpace baseline then
            Error EmptyBaselineRevision
        elif Option.isSome evaluator && RoleAuthority.allows ModifyEvaluationAuthority authority then
            Error JudgedExecutionHoldsEvaluatorAuthority
        elif not (List.isEmpty admitted) then
            Error(BoundaryAdmitsEvaluator admitted)
        else
            Ok
                { Id = id
                  WorkItem = workItem
                  Actor = actor
                  Role = authority.Role
                  Authority = authority
                  BaselineRevision = baseline
                  CandidateRevision = None
                  Workspace = None
                  Containment = ContainmentUnknown
                  Boundary =
                    { boundary with
                        EvaluatorReferences = boundary.EvaluatorReferences @ evaluatorRefs |> List.distinct }
                  Evaluator = evaluator
                  HumanOnlyTransitions = []
                  Parent = None
                  StartedAt = startedAt
                  State = Active }

    let withWorkspace workspace containment (envelope: ExecutionEnvelope) =
        { envelope with
            Workspace = Some workspace
            Containment = containment }

    let withCandidate revision (envelope: ExecutionEnvelope) =
        { envelope with CandidateRevision = Some revision }

    let withHumanOnly transitions (envelope: ExecutionEnvelope) =
        { envelope with HumanOnlyTransitions = transitions }

    /// The capabilities this execution may exercise.
    let capabilities (envelope: ExecutionEnvelope) = RoleAuthority.effective envelope.Authority

    /// A competing attempt at the same work always gets a new identity
    /// (ORD-EXEC-074).
    let requireNewIdentity (existing: ExecutionEnvelope list) (candidate: ExecutionId) =
        if existing |> List.exists (fun e -> e.Id = candidate) then
            Error(DuplicateExecutionId candidate)
        else
            Ok candidate

    /// Continue an interrupted execution. The predecessor is recorded as
    /// interrupted; the successor carries a new identity and a parent link.
    /// A role change is likewise a new execution, never an in-place edit
    /// (ORD-EXEC-087).
    let succeed
        (newId: ExecutionId)
        (actor: ExecutionActor)
        (authority: RoleAuthority)
        (at: DateTimeOffset)
        (predecessor: ExecutionEnvelope)
        : Result<ExecutionEnvelope * ExecutionEnvelope, EnvelopeError> =
        if newId = predecessor.Id then
            Error ParentIsSelf
        else
            create
                newId
                predecessor.WorkItem
                actor
                authority
                predecessor.BaselineRevision
                predecessor.Boundary
                predecessor.Evaluator
                at
            |> Result.map (fun successor ->
                { predecessor with State = Interrupted },
                { successor with
                    Parent = Some predecessor.Id
                    Workspace = predecessor.Workspace
                    Containment = predecessor.Containment
                    CandidateRevision = predecessor.CandidateRevision
                    HumanOnlyTransitions = predecessor.HumanOnlyTransitions })

/// What reconciliation established about a prior attempt (ORD-EXEC-113).
type ReconciliationFinding =
    | EffectOccurred of evidence: string
    | EffectDidNotOccur of evidence: string
    | EffectStillUnknown of reason: string

/// One append-only ledger entry (ORD-EXEC-114).
type StepEntry =
    | AttemptStarted of attempt: int * at: DateTimeOffset
    | AttemptObserved of attempt: int * observed: ObservedReceipt * result: ReceiptResult * at: DateTimeOffset
    | AttemptReconciled of attempt: int * finding: ReconciliationFinding * at: DateTimeOffset

type StepRecord =
    { Id: StepId
      Sequence: int
      Name: string
      DependsOn: StepId list
      Expected: ExpectedReceipt
      /// Whether repeating an unknown attempt is safe by the external
      /// contract. Supplied by the host, never inferred.
      RetrySafety: UnknownRetrySafety
      Entries: StepEntry list }

/// A step's current status, derived from its entries.
type StepStatus =
    | NotStarted
    | Satisfied
    | ReceiptMismatch of ReceiptResult
    /// Started and never observed, or observed as indeterminate: the effect
    /// is unknown.
    | EffectUnknown
    | ReconciledAsNotOccurred
    | ReconciledAsOccurred

/// What may legally happen to a step next (ORD-EXEC-111/112/115).
type StepAction =
    | ExecuteStep
    | ReuseCompletedStep
    | RetryStep
    | ReconcileStep
    /// The effect occurred; observe it again rather than repeating it.
    | ObserveStep
    | AwaitDependencies of StepId list
    | ResolveMismatch

type StepError =
    | StepNotFound of StepId
    | DuplicateStep of StepId
    | StepActionNotLegal of StepId * attempted: string * legal: StepAction
    | NoAttemptInProgress of StepId

[<RequireQualifiedAccess>]
module Step =

    let create id sequence name dependsOn expected retrySafety =
        { Id = id
          Sequence = sequence
          Name = name
          DependsOn = dependsOn
          Expected = expected
          RetrySafety = retrySafety
          Entries = [] }

    let attempts (step: StepRecord) =
        step.Entries
        |> List.sumBy (fun e ->
            match e with
            | AttemptStarted _ -> 1
            | _ -> 0)

    let status (step: StepRecord) : StepStatus =
        match step.Entries |> List.tryLast with
        | None -> NotStarted
        | Some(AttemptStarted _) -> EffectUnknown
        | Some(AttemptObserved(_, _, result, _)) ->
            match Receipt.outcome result with
            | Matched -> Satisfied
            | Mismatched -> ReceiptMismatch result
            | Undetermined -> EffectUnknown
        | Some(AttemptReconciled(_, EffectOccurred _, _)) -> ReconciledAsOccurred
        | Some(AttemptReconciled(_, EffectDidNotOccur _, _)) -> ReconciledAsNotOccurred
        | Some(AttemptReconciled(_, EffectStillUnknown _, _)) -> EffectUnknown

    let private statusToken status =
        match status with
        | NotStarted -> "not-started"
        | Satisfied -> "match"
        | ReceiptMismatch _ -> "mismatch"
        | EffectUnknown -> "indeterminate"
        | ReconciledAsNotOccurred -> "reconciled-not-occurred"
        | ReconciledAsOccurred -> "reconciled-occurred"

    let statusToWire (step: StepRecord) = statusToken (status step)

/// The durable, append-only step ledger of one execution (ORD-EXEC-110).
type StepLedger =
    { Execution: ExecutionId
      Steps: StepRecord list }

[<RequireQualifiedAccess>]
module StepLedger =

    let empty execution = { Execution = execution; Steps = [] }

    let tryFind id (ledger: StepLedger) = ledger.Steps |> List.tryFind (fun s -> s.Id = id)

    let add (step: StepRecord) (ledger: StepLedger) =
        match tryFind step.Id ledger with
        | Some _ -> Error(DuplicateStep step.Id)
        | None -> Ok { ledger with Steps = ledger.Steps @ [ step ] |> List.sortBy (fun s -> s.Sequence) }

    /// The legal next action for one step, derived only from durable state
    /// (ORD-EXEC-115).
    let nextAction (ledger: StepLedger) (step: StepRecord) : StepAction =
        let unsatisfied =
            step.DependsOn
            |> List.filter (fun d ->
                match tryFind d ledger with
                | Some dep -> Step.status dep <> Satisfied
                | None -> true)

        match Step.status step with
        | Satisfied -> ReuseCompletedStep
        | _ when not (List.isEmpty unsatisfied) -> AwaitDependencies unsatisfied
        | NotStarted -> ExecuteStep
        | ReceiptMismatch _ -> ResolveMismatch
        | ReconciledAsNotOccurred -> RetryStep
        | ReconciledAsOccurred -> ObserveStep
        | EffectUnknown ->
            if ExternalEffect.mayRepeatBeforeReconciliation step.RetrySafety then
                RetryStep
            else
                ReconcileStep

    let private update id (f: StepRecord -> Result<StepRecord, StepError>) (ledger: StepLedger) =
        match tryFind id ledger with
        | None -> Error(StepNotFound id)
        | Some step ->
            f step
            |> Result.map (fun updated ->
                { ledger with
                    Steps = ledger.Steps |> List.map (fun s -> if s.Id = id then updated else s) })

    let private append entry (step: StepRecord) = { step with Entries = step.Entries @ [ entry ] }

    /// Start (or retry) a step. Refuses to repeat an attempt whose effect is
    /// unknown unless reconciliation established that it did not occur or
    /// the host established that repeating it is safe (ORD-EXEC-112).
    let start id (at: DateTimeOffset) (ledger: StepLedger) =
        update
            id
            (fun step ->
                match nextAction ledger step with
                | ExecuteStep
                | RetryStep -> Ok(append (AttemptStarted(Step.attempts step + 1, at)) step)
                | legal -> Error(StepActionNotLegal(id, "start", legal)))
            ledger

    /// Record what was observed for the current attempt.
    let observe id (expectedOverride: ExpectedReceipt option) (observed: ObservedReceipt) (at: DateTimeOffset) (ledger: StepLedger) =
        update
            id
            (fun step ->
                let attempt = Step.attempts step
                let observable =
                    match Step.status step with
                    | EffectUnknown -> attempt > 0
                    | ReconciledAsOccurred -> true
                    | _ -> false

                if not observable then
                    Error(NoAttemptInProgress id)
                else
                    let expected = expectedOverride |> Option.defaultValue step.Expected
                    let result = Receipt.compare expected observed
                    Ok(append (AttemptObserved(attempt, observed, result, at)) step))
            ledger

    /// Record a reconciliation finding for an unknown effect (ORD-EXEC-113).
    let reconcile id (finding: ReconciliationFinding) (at: DateTimeOffset) (ledger: StepLedger) =
        update
            id
            (fun step ->
                match Step.status step with
                | EffectUnknown -> Ok(append (AttemptReconciled(Step.attempts step, finding, at)) step)
                | _ -> Error(StepActionNotLegal(id, "reconcile", nextAction ledger step)))
            ledger

    let isComplete (ledger: StepLedger) =
        ledger.Steps |> List.forall (fun s -> Step.status s = Satisfied)

    let hasUnknownEffects (ledger: StepLedger) =
        ledger.Steps |> List.exists (fun s -> Step.status s = EffectUnknown)

/// A transition an operator, agent, CLI, API or UI may request.
type LegalActionKind =
    | StartStep of StepId
    | RetryStepAction of StepId
    | ReconcileStepAction of StepId
    | ObserveStepAction of StepId
    | ResolveScopeEffect of resource: string
    | ExpandScope
    | Checkpoint
    | Block
    | Resume
    | Complete
    | Abandon

[<RequireQualifiedAccess>]
module LegalActionKind =

    let transitionName action =
        match action with
        | StartStep _ -> "step.start"
        | RetryStepAction _ -> "step.retry"
        | ReconcileStepAction _ -> "step.reconcile"
        | ObserveStepAction _ -> "step.observe"
        | ResolveScopeEffect _ -> "scope.resolve"
        | ExpandScope -> "scope.expand"
        | Checkpoint -> "execution.checkpoint"
        | Block -> "execution.block"
        | Resume -> "execution.resume"
        | Complete -> "execution.complete"
        | Abandon -> "execution.abandon"

/// An action with its availability. Unavailable actions carry the reasons,
/// so a presentation layer never has to guess why (ORD-EXEC-020/021).
type LegalAction =
    { Action: LegalActionKind
      Available: bool
      Reasons: string list
      /// Who may exercise it under the effective policy.
      Requirement: ActorRequirement }

/// Everything the legal-action computation may look at.
type ExecutionSnapshot =
    { Envelope: ExecutionEnvelope
      Ledger: StepLedger
      ScopeEffects: ScopeEffect list
      Verification: EvaluationOutcome option
      Policy: AuthorizationPolicy }

[<RequireQualifiedAccess>]
module LegalActions =

    let private evaluate (snapshot: ExecutionSnapshot) (actor: ExecutionActor) (action: LegalActionKind) (blockers: string list) =
        let transition = LegalActionKind.transitionName action

        let policy =
            if List.contains transition snapshot.Envelope.HumanOnlyTransitions then
                AuthorizationPolicy.require transition HumanRequired snapshot.Policy
            else
                snapshot.Policy

        let authorization =
            match AuthorizationPolicy.authorize transition actor snapshot.Envelope.StartedAt policy with
            | Ok _ -> []
            | Error(HumanAuthorizationRequired _) -> [ "requires a human actor" ]
            | Error(ActorKindNotPermitted(_, kind, _)) -> [ sprintf "actor kind %s is not permitted" (ActorKind.toWire kind) ]
            | Error(ActorNotPermitted _) -> [ "actor is not permitted" ]

        let reasons = blockers @ authorization

        { Action = action
          Available = List.isEmpty reasons
          Reasons = reasons
          Requirement = AuthorizationPolicy.requirementFor transition policy }

    /// The one authoritative computation of legal next actions for an actor.
    /// CLI, local API, remote execution and UI all consume this.
    let compute (snapshot: ExecutionSnapshot) (actor: ExecutionActor) : LegalAction list =
        let envelope = snapshot.Envelope
        let ledger = snapshot.Ledger
        let terminal = ExecutionState.isTerminal envelope.State

        let stateBlockers =
            match envelope.State with
            | Active
            | AwaitingReconciliation -> []
            | Blocked reason -> [ "execution is blocked: " + reason ]
            | other -> [ "execution is " + ExecutionState.toWire other ]

        let stepActions =
            ledger.Steps
            |> List.choose (fun step ->
                match StepLedger.nextAction ledger step with
                | ExecuteStep -> Some(StartStep step.Id, [])
                | RetryStep -> Some(RetryStepAction step.Id, [])
                | ReconcileStep -> Some(ReconcileStepAction step.Id, [])
                | ObserveStep -> Some(ObserveStepAction step.Id, [])
                | AwaitDependencies deps ->
                    Some(StartStep step.Id, [ "waiting on " + (deps |> List.map StepId.value |> String.concat ", ") ])
                | ResolveMismatch -> Some(StartStep step.Id, [ "receipt mismatch must be resolved by governed rework" ])
                | ReuseCompletedStep -> None)
            |> List.map (fun (action, blockers) -> evaluate snapshot actor action (stateBlockers @ blockers))

        let unresolvedEffects = snapshot.ScopeEffects

        let scopeActions =
            unresolvedEffects
            |> List.map (fun e -> evaluate snapshot actor (ResolveScopeEffect e.Resource) stateBlockers)

        let expandBlockers =
            if envelope.Authority.Prohibits |> Set.contains ExpandMutationBoundary then
                [ "role " + ExecutionRole.toWire envelope.Role + " may not expand its mutation boundary" ]
            else
                []

        let completionBlockers =
            [ if not (StepLedger.isComplete ledger) then
                  yield "not every step receipt matches"
              if StepLedger.hasUnknownEffects ledger then
                  yield "unknown effects require reconciliation"
              if not (List.isEmpty unresolvedEffects) then
                  yield "out-of-boundary mutations are unresolved"
              match envelope.Evaluator, snapshot.Verification with
              | Some current, Some outcome when not (Evaluation.isCurrent current outcome) ->
                  yield "verification is stale or its evaluator changed"
              | Some _, Some(EvaluationOutcome.Failed(_, reason)) -> yield "verification failed: " + reason
              | _ -> () ]

        let lifecycle =
            [ if not terminal then
                  yield evaluate snapshot actor Checkpoint []
                  yield evaluate snapshot actor ExpandScope (stateBlockers @ expandBlockers)
                  yield evaluate snapshot actor Complete (stateBlockers @ completionBlockers)
                  yield evaluate snapshot actor Abandon []
              match envelope.State with
              | Blocked _ -> yield evaluate snapshot actor Resume []
              | Active -> yield evaluate snapshot actor Block []
              | _ -> () ]

        stepActions @ scopeActions @ lifecycle

    /// Whether a specific action is currently legal for the actor. A
    /// presentation layer submits the action; it never decides this itself.
    let isLegal snapshot actor action =
        compute snapshot actor |> List.exists (fun a -> a.Action = action && a.Available)
