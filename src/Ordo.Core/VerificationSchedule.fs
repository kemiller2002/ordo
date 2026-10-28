/// Deterministic execution schedule over a compiled verification plan.
///
/// A verification plan states, per requirement, the acceptance obligations
/// that must hold, how each is verified, which test (if any) verifies it,
/// what evidence it already has and which other requirements it depends on.
/// Praxis admits the plan (intake, identity, digest, provenance); this module
/// orders the work it implies. It is a pure function of the plan and the
/// current evidence bindings: no clock, no file, no randomness, so the same
/// inputs always give the same schedule.
///
/// Guarantees, each asserted in `tests/Ordo.Tests/VerificationScheduleTests.fs`:
/// - every plan requirement appears in the schedule exactly once, with a
///   rank, and in exactly one independent-work wave or cycle group; the
///   priority heuristic orders work, it never drops it;
/// - dependency cycles are reported as data, never silently broken;
/// - a dependency on a requirement the plan does not contain is reported,
///   never dropped;
/// - evidence whose bindings differ from the current subject is stale and is
///   never counted as satisfying an obligation; evidence is reused across
///   runs only through a requirement's explicit `ReuseBindings`, all of
///   which it must carry and match exactly (DF-SDE-2026-0026).
module Ordo.Core.VerificationSchedule

open System

// ---------------------------------------------------------------------------
// Input: the plan as Ordo needs it.
// ---------------------------------------------------------------------------

/// The test that verifies an obligation, or the plan's explicit statement
/// that none exists yet. A missing test is work, not an omission.
type TestIdentity =
    | Identified of string
    | MissingTest of reason: string

/// Recorded outcome of the last verification run of an obligation.
[<RequireQualifiedAccess>]
type EvidenceOutcome =
    | Pass
    | Fail
    | Blocked
    | NotRun

/// Evidence as the plan records it. `Bindings` are the subject values the
/// evidence was produced against (for example the source commit or the
/// baseline digest), named by the same keys the freshness policy uses.
type RecordedEvidence =
    { Outcome: EvidenceOutcome
      Bindings: Map<string, string> }

type PlanObligation =
    { Id: string
      /// Mandatory acceptance: the requirement cannot be accepted without it.
      Mandatory: bool
      Method: string
      Test: TestIdentity
      /// Every recorded result that applies to this obligation (empty when
      /// it was never run). Several results combine: any stale result makes
      /// the obligation stale, then any failure, block or missing run wins
      /// over a pass.
      Evidence: RecordedEvidence list
      /// False when the obligation's own acceptance contract
      /// (`acceptanceContract.freshness.policy`) forbids reuse: its evidence
      /// is then judged on the requirement's `FreshnessBindings` only. True
      /// inherits the requirement's `ReuseBindings`; an obligation can only
      /// narrow its requirement's reuse, never widen it.
      ReuseAllowed: bool }

type PlanRequirement =
    { Id: string
      Priority: string
      Obligations: PlanObligation list
      DependsOn: string list
      /// Binding keys whose current value evidence must match to be fresh.
      FreshnessBindings: string list
      /// Content-addressed alternative to `FreshnessBindings` (empty: none).
      /// Evidence that carries a value for every one of these keys, each
      /// equal to a current value the caller supplied, is fresh without
      /// matching the other `FreshnessBindings` keys. A key absent on either
      /// side makes the evidence not reusable, so it falls back to
      /// `FreshnessBindings`; a key present on both sides with different
      /// values makes it stale. See DF-SDE-2026-0026.
      ReuseBindings: string list }

type VerificationPlan =
    { Schema: string
      PlanDigest: string
      Requirements: PlanRequirement list }

/// The subject the schedule is computed for: the current value of every
/// binding key (for example `sourceCommit`). Supplied by the caller, never
/// read from the environment, so the schedule stays a pure function.
type ScheduleContext =
    { Current: Map<string, string>
      /// Current values that hold for one requirement only (for example the
      /// digest of the declared inputs of the tests that verify it), keyed
      /// by requirement id. They add to, and override, `Current`.
      BySubject: Map<string, Map<string, string>> }

/// A context with no requirement-scoped values.
let currentOnly (current: Map<string, string>) = { Current = current; BySubject = Map.empty }

/// The context one requirement's evidence is judged against.
let forSubject (context: ScheduleContext) (requirementId: string) =
    match Map.tryFind requirementId context.BySubject with
    | None -> context
    | Some scoped -> { context with Current = Map.fold (fun acc key value -> Map.add key value acc) context.Current scoped }

// ---------------------------------------------------------------------------
// Output.
// ---------------------------------------------------------------------------

[<RequireQualifiedAccess>]
type ObligationState =
    | Satisfied
    | Failing
    | NotRun
    | BlockedRun
    | Stale of mismatched: string list
    | TestMissing of reason: string

[<RequireQualifiedAccess>]
type WorkKind =
    /// Implementation: write the missing test.
    | AuthorTest
    /// Implementation: make a failing obligation pass.
    | FixImplementation
    /// Verification: run an identified test that has no current result.
    | RunVerification
    /// Verification: unblock a run the plan records as blocked.
    | UnblockVerification
    /// Stale evidence: re-run to produce evidence bound to the current subject.
    | RefreshEvidence

type WorkItem =
    { RequirementId: string
      ObligationId: string
      Kind: WorkKind
      Mandatory: bool
      Method: string
      Test: TestIdentity
      Detail: string }

type RequirementPriority =
    { /// P0 obligations of the requirement itself.
      MandatoryObligations: int
      /// Requirements that depend on this one, directly or transitively.
      TransitiveDependents: int
      /// Mandatory obligations of this requirement and every transitive
      /// dependent: how much mandatory acceptance this requirement unblocks.
      MandatoryUnblocked: int }

type ScheduledRequirement =
    { Id: string
      Priority: string
      Rank: int
      Score: RequirementPriority
      DependsOn: string list
      Dependents: string list
      Obligations: (string * ObligationState) list
      /// A mandatory obligation is not satisfied by fresh passing evidence.
      AcceptanceBlocker: bool
      /// Requirement takes part in a dependency cycle.
      InCycle: bool }

type DependencyEdge = { Requirement: string; DependsOn: string }

type StaleEvidence =
    { RequirementId: string
      ObligationId: string
      Mismatched: string list }

type VerificationSchedule =
    { PlanDigest: string
      Requirements: ScheduledRequirement list
      AcceptanceBlockers: string list
      Dependencies: DependencyEdge list
      UnknownDependencies: DependencyEdge list
      Cycles: string list list
      ImplementationTasks: WorkItem list
      RequiredVerification: WorkItem list
      StaleEvidence: StaleEvidence list
      /// Waves of requirements whose prerequisites all lie in earlier waves.
      /// Requirements inside one wave are independent and may run in parallel.
      IndependentWork: string list list
      /// Requirements that cannot be placed in a wave because they sit in or
      /// behind a dependency cycle; reported, never dropped.
      CycleBlocked: string list }

type ScheduleError =
    | DuplicateRequirement of string
    | DuplicateObligation of requirement: string * obligation: string

// ---------------------------------------------------------------------------
// Classification.
// ---------------------------------------------------------------------------

let private ordinal (values: string seq) = values |> Seq.distinct |> Seq.sortWith (fun a b -> String.CompareOrdinal(a, b)) |> List.ofSeq

/// Bindings named by the freshness policy whose evidence value is absent or
/// differs from the current subject. A binding the caller did not supply a
/// current value for cannot be judged and is not reported stale.
let mismatchedBindings (context: ScheduleContext) (freshness: string list) (evidence: RecordedEvidence) =
    freshness
    |> ordinal
    |> List.filter (fun key ->
        match Map.tryFind key context.Current with
        | None -> false
        | Some current -> Map.tryFind key evidence.Bindings <> Some current)

/// Content-addressed reuse: every reuse key is carried by the evidence and
/// has a current value, and they are equal. Strict by construction: a key
/// missing on either side never grants reuse (unlike `mismatchedBindings`,
/// which leaves an unsupplied current value unjudged).
let reusable (context: ScheduleContext) (reuse: string list) (evidence: RecordedEvidence) =
    not reuse.IsEmpty
    && reuse
       |> List.forall (fun key ->
           match Map.tryFind key context.Current, Map.tryFind key evidence.Bindings with
           | Some current, Some recorded -> not (String.IsNullOrEmpty current) && String.Equals(current, recorded, StringComparison.Ordinal)
           | _ -> false)

/// Bindings that make one result stale under a requirement's freshness
/// policy: none when the evidence is reusable; otherwise every
/// `freshness` mismatch plus every reuse key the evidence and the subject
/// both carry with different values (evidence that contradicts its subject
/// is never fresh, whatever run it came from).
let staleBindings (context: ScheduleContext) (freshness: string list) (reuse: string list) (evidence: RecordedEvidence) =
    if reusable context reuse evidence then
        []
    else
        let contradicted =
            reuse
            |> List.filter (fun key ->
                match Map.tryFind key context.Current, Map.tryFind key evidence.Bindings with
                | Some current, Some recorded -> current <> recorded
                | _ -> false)

        ordinal (mismatchedBindings context freshness evidence @ contradicted)

/// Classifies an obligation under a freshness policy with a content-addressed
/// reuse alternative (`reuse`, empty for none).
let classifyObligationWithReuse (context: ScheduleContext) (freshness: string list) (reuse: string list) (obligation: PlanObligation) : ObligationState =
    match obligation.Test, obligation.Evidence with
    | MissingTest reason, _ -> ObligationState.TestMissing reason
    | Identified _, [] -> ObligationState.NotRun
    | Identified _, results ->
        let judged = results |> List.filter (fun e -> e.Outcome <> EvidenceOutcome.NotRun)
        let mismatched = judged |> List.collect (staleBindings context freshness reuse) |> ordinal
        let has outcome = judged |> List.exists (fun e -> e.Outcome = outcome)

        if not mismatched.IsEmpty then ObligationState.Stale mismatched
        elif has EvidenceOutcome.Fail then ObligationState.Failing
        elif has EvidenceOutcome.Blocked then ObligationState.BlockedRun
        elif judged.Length < results.Length || judged.IsEmpty then ObligationState.NotRun
        else ObligationState.Satisfied

/// Classifies an obligation under a freshness policy with no reuse path.
let classifyObligation (context: ScheduleContext) (freshness: string list) (obligation: PlanObligation) : ObligationState =
    classifyObligationWithReuse context freshness [] obligation

let private workFor (requirement: PlanRequirement) (obligation: PlanObligation) state =
    let item kind detail =
        Some
            { RequirementId = requirement.Id
              ObligationId = obligation.Id
              Kind = kind
              Mandatory = obligation.Mandatory
              Method = obligation.Method
              Test = obligation.Test
              Detail = detail }

    match state with
    | ObligationState.Satisfied -> None
    | ObligationState.TestMissing reason -> item WorkKind.AuthorTest reason
    | ObligationState.Failing -> item WorkKind.FixImplementation "fresh evidence records a failure"
    | ObligationState.NotRun -> item WorkKind.RunVerification "no current result"
    | ObligationState.BlockedRun -> item WorkKind.UnblockVerification "fresh evidence records the run as blocked"
    | ObligationState.Stale mismatched -> item WorkKind.RefreshEvidence ("evidence not bound to current " + String.concat ", " mismatched)

// ---------------------------------------------------------------------------
// Graph.
// ---------------------------------------------------------------------------

/// Strongly connected components with more than one member, or with a
/// self-edge: the dependency cycles. Tarjan's algorithm over ordinal-sorted
/// nodes and edges, so the result is deterministic; each cycle is sorted and
/// the list of cycles is sorted by its first member.
let findCycles (nodes: string list) (edges: Map<string, string list>) : string list list =
    let successors node = Map.tryFind node edges |> Option.defaultValue [] |> ordinal
    let mutable index = 0
    let indices = Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal)
    let lowlink = Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal)
    let onStack = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
    let stack = Collections.Generic.Stack<string>()
    let components = Collections.Generic.List<string list>()

    let rec strongConnect node =
        indices[node] <- index
        lowlink[node] <- index
        index <- index + 1
        stack.Push node
        onStack.Add node |> ignore

        for next in successors node do
            if not (indices.ContainsKey next) then
                strongConnect next
                lowlink[node] <- min lowlink[node] lowlink[next]
            elif onStack.Contains next then
                lowlink[node] <- min lowlink[node] indices[next]

        if lowlink[node] = indices[node] then
            let rec pop acc =
                let member' = stack.Pop()
                onStack.Remove member' |> ignore
                if member' = node then member' :: acc else pop (member' :: acc)

            components.Add(pop [])

    for node in ordinal nodes do
        if not (indices.ContainsKey node) then strongConnect node

    components
    |> Seq.filter (fun members ->
        match members with
        | [ single ] -> successors single |> List.contains single
        | _ -> true)
    |> Seq.map ordinal
    |> Seq.sortWith (fun a b -> String.CompareOrdinal(List.head a, List.head b))
    |> List.ofSeq

/// Every node reachable from `start` along `edges`, excluding `start`
/// unless a cycle leads back to it.
let private reachable (edges: Map<string, string list>) (start: string) =
    let rec walk visited frontier =
        match frontier with
        | [] -> visited
        | node :: rest ->
            let next =
                Map.tryFind node edges
                |> Option.defaultValue []
                |> List.filter (fun n -> not (Set.contains n visited))

            walk (Set.union visited (Set.ofList next)) (next @ rest)

    walk Set.empty [ start ]

/// Kahn layering over the known dependency edges. Nodes never released
/// (members of, or behind, a cycle) are returned separately.
let private waves (nodes: string list) (prerequisites: Map<string, string list>) =
    let rec layer placed remaining acc =
        let ready =
            remaining
            |> List.filter (fun node ->
                Map.tryFind node prerequisites
                |> Option.defaultValue []
                |> List.forall (fun p -> Set.contains p placed))

        match ready with
        | [] -> List.rev acc, remaining
        | _ ->
            let readySet = Set.ofList ready
            layer (Set.union placed readySet) (remaining |> List.filter (fun n -> not (Set.contains n readySet))) (ready :: acc)

    layer Set.empty nodes []

// ---------------------------------------------------------------------------
// Validation and scheduling.
// ---------------------------------------------------------------------------

let validate (plan: VerificationPlan) : ScheduleError list =
    let duplicates values = values |> List.countBy id |> List.filter (fun (_, n) -> n > 1) |> List.map fst |> ordinal

    [ yield! plan.Requirements |> List.map (fun r -> r.Id) |> duplicates |> List.map DuplicateRequirement
      for requirement in plan.Requirements |> List.sortWith (fun a b -> String.CompareOrdinal(a.Id, b.Id)) do
          yield!
              requirement.Obligations
              |> List.map (fun o -> o.Id)
              |> duplicates
              |> List.map (fun o -> DuplicateObligation(requirement.Id, o)) ]

/// Orders the plan's work. Ranking: most mandatory acceptance unblocked
/// first (P0 obligations of the requirement and of every transitive
/// dependent), then most transitive dependents, then stable id (ordinal).
/// The ranking only orders; every requirement keeps its row.
let schedule (context: ScheduleContext) (plan: VerificationPlan) : Result<VerificationSchedule, ScheduleError list> =
    match validate plan with
    | _ :: _ as errors -> Error errors
    | [] ->
        let requirements = plan.Requirements |> List.sortWith (fun a b -> String.CompareOrdinal(a.Id, b.Id))
        let ids = requirements |> List.map (fun r -> r.Id) |> Set.ofList

        let allEdges =
            requirements
            |> List.collect (fun r -> r.DependsOn |> ordinal |> List.map (fun d -> { Requirement = r.Id; DependsOn = d }))

        let known, unknown = allEdges |> List.partition (fun e -> Set.contains e.DependsOn ids)

        let prerequisites =
            known |> List.groupBy (fun e -> e.Requirement) |> List.map (fun (k, es) -> k, es |> List.map (fun e -> e.DependsOn)) |> Map.ofList

        let dependentsOf =
            known |> List.groupBy (fun e -> e.DependsOn) |> List.map (fun (k, es) -> k, es |> List.map (fun e -> e.Requirement) |> ordinal) |> Map.ofList

        let nodes = requirements |> List.map (fun r -> r.Id)
        let cycles = findCycles nodes prerequisites
        let inCycle = cycles |> List.concat |> Set.ofList
        let mandatoryCount = requirements |> List.map (fun r -> r.Id, r.Obligations |> List.filter (fun o -> o.Mandatory) |> List.length) |> Map.ofList

        let states =
            requirements
            |> List.map (fun r ->
                r.Id,
                r.Obligations
                |> List.map (fun o ->
                    let reuse = if o.ReuseAllowed then r.ReuseBindings else []
                    o, classifyObligationWithReuse (forSubject context r.Id) r.FreshnessBindings reuse o))
            |> Map.ofList

        let score id =
            let dependents = reachable dependentsOf id |> Set.remove id

            { MandatoryObligations = mandatoryCount[id]
              TransitiveDependents = dependents.Count
              MandatoryUnblocked = mandatoryCount[id] + (dependents |> Seq.sumBy (fun d -> mandatoryCount[d])) }

        let scores = nodes |> List.map (fun id -> id, score id) |> Map.ofList

        let rankOrder =
            nodes
            |> List.sortWith (fun a b ->
                let sa, sb = scores[a], scores[b]

                match compare sb.MandatoryUnblocked sa.MandatoryUnblocked with
                | 0 ->
                    match compare sb.TransitiveDependents sa.TransitiveDependents with
                    | 0 -> String.CompareOrdinal(a, b)
                    | c -> c
                | c -> c)

        let rankOf = rankOrder |> List.mapi (fun i id -> id, i + 1) |> Map.ofList
        let byRank (ids: string list) = ids |> List.sortBy (fun id -> rankOf[id])

        let isBlocker id =
            states[id] |> List.exists (fun (o, s) -> o.Mandatory && s <> ObligationState.Satisfied)

        let rows =
            requirements
            |> List.map (fun r ->
                { Id = r.Id
                  Priority = r.Priority
                  Rank = rankOf[r.Id]
                  Score = scores[r.Id]
                  DependsOn = Map.tryFind r.Id prerequisites |> Option.defaultValue [] |> ordinal
                  Dependents = Map.tryFind r.Id dependentsOf |> Option.defaultValue []
                  Obligations = states[r.Id] |> List.map (fun (o, s) -> o.Id, s) |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
                  AcceptanceBlocker = isBlocker r.Id
                  InCycle = Set.contains r.Id inCycle })
            |> List.sortBy (fun row -> row.Rank)

        let work =
            rankOrder
            |> List.collect (fun id ->
                let requirement = requirements |> List.find (fun r -> r.Id = id)

                states[id]
                |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a.Id, b.Id))
                |> List.choose (fun (o, s) -> workFor requirement o s))

        let ofKinds kinds = work |> List.filter (fun w -> List.contains w.Kind kinds)

        let stale =
            rankOrder
            |> List.collect (fun id ->
                states[id]
                |> List.choose (fun (o, s) ->
                    match s with
                    | ObligationState.Stale mismatched -> Some { RequirementId = id; ObligationId = o.Id; Mismatched = mismatched }
                    | _ -> None)
                |> List.sortWith (fun a b -> String.CompareOrdinal(a.ObligationId, b.ObligationId)))

        let placed, unplaced = waves nodes prerequisites

        Ok
            { PlanDigest = plan.PlanDigest
              Requirements = rows
              AcceptanceBlockers = nodes |> List.filter isBlocker |> byRank
              Dependencies = known
              UnknownDependencies = unknown
              Cycles = cycles
              ImplementationTasks = ofKinds [ WorkKind.AuthorTest; WorkKind.FixImplementation ]
              RequiredVerification = work
              StaleEvidence = stale
              IndependentWork = placed |> List.map byRank
              CycleBlocked = unplaced |> byRank }
