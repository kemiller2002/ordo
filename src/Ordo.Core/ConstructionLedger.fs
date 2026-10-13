/// The program-level requirements disposition ledger,
/// `ordo.requirements-ledger/1` (CONSTRUCT-009). It guarantees that no source
/// requirement silently disappears between intake and construction: every
/// key of the pinned source manifest gets exactly one row that says what
/// construction did with it, why, and on whose authority.
///
/// The ledger is derived, not authored. `buildLedger` reads the manifest, the
/// blueprint, the default plans Ordo generated (any subset, possibly none),
/// independent acceptance evidence and explicit operator tags, and computes
/// each row. An explicit disposition claim may only narrow what the facts
/// allow: it is checked, never believed. In particular no input lets an
/// agent mark a genuine domain decision as resolved, or invent a vocabulary
/// limitation, without an approved decision that closes it.
///
/// A ledger is a report, not authority to apply or ship anything. See
/// docs/architecture/construction-default-plan.md ("Requirements ledger").
module Ordo.Core.ConstructionLedger

open System
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionDefaultPlan

[<Literal>]
let LedgerSchemaVersion = "ordo.requirements-ledger/1"

// ---------------------------------------------------------------------------
// Types.
// ---------------------------------------------------------------------------

/// How a requirement's behavior is (or is not) settled. A row carries the
/// set of classes that apply to its aspects, not a single class: the timer
/// Stop requirement is mechanically constructed (its ports) and also waits on
/// a genuine domain decision (what a failed effect means). Case order is the
/// canonical order.
[<RequireQualifiedAccess>]
type ResolutionClass =
    /// The requirement text itself fixes the behavior. Only an explicit
    /// operator tag says so; nothing is inferred.
    | FixedByRequirement
    /// An approved decision (or engineering rationale) node anchors it.
    | AlreadyApproved
    /// A generated default plan constructs it (states, transitions, ports).
    | MechanicallyDetermined
    /// A domain question is open: a conflict, an undecided effect failure, an
    /// assigned requirement nothing constructs, a blueprint `unresolved`
    /// disposition, or a requirement no plan constructs and nothing approves.
    | GenuineDomainDecision
    /// Approved or requirement-fixed content the selected vocabulary cannot
    /// express (including an uncovered requirement tagged `FixedByRequirement`).
    | UnsupportedByVocabulary

/// What happened to a requirement. Every case but `Implemented` carries a
/// mandatory, nonblank rationale.
[<RequireQualifiedAccess>]
type FinalDisposition =
    | Implemented
    | PartiallyImplemented of remaining: string
    | Deferred of rationale: string
    | Unresolved of rationale: string
    | Rejected of rationale: string
    | Superseded of decisionId: string * rationale: string

[<RequireQualifiedAccess>]
type AcceptanceVerdict =
    | Passed
    | Failed

/// Independent acceptance for one requirement: cases authored and run
/// independently of construction. The ledger cannot prove independence; it
/// refuses the one self-certification it can see (citing a blueprint node,
/// such as a verification obligation, as an acceptance case).
type AcceptanceEvidence =
    { CaseIds: string list
      Verdict: AcceptanceVerdict }

/// An explicit disposition someone asks the ledger to record. Checked against
/// the derived facts; it can never close an open question by itself.
type DispositionClaim =
    { RequirementKey: string
      Disposition: FinalDisposition
      /// Approved decisions that close the requirement's open questions. Each
      /// must be a program-level approval in `LedgerTags.ApprovedDecisionIds`
      /// that no supplied default plan binds.
      DecisionIds: string list }

/// Explicit ledger inputs. `FixedByRequirement` and `ApprovedDecisionIds` come
/// from the same external authority as `ConstructionIr.validateCohort`'s
/// authorized decisions, never from an agent. `Claims` may come from anyone
/// and are checked.
type LedgerTags =
    { FixedByRequirement: Set<string>
      ApprovedDecisionIds: Set<string>
      Claims: DispositionClaim list }

type LedgerRow =
    { Requirement: SourceRequirement
      /// The semantic categories of the requirement: the kinds of the
      /// blueprint nodes that anchor it, excluding cohort and verification.
      Categories: NodeKind list
      BlueprintDisposition: Disposition
      /// Nodes the requirement's nodes depend on.
      DependencyNodeIds: string list
      /// Approved decision or rationale nodes that anchor it, plus approved
      /// decisions a claim cites.
      ApprovedDecisionIds: string list
      /// The approved decisions relied on to close open questions or to
      /// reject or supersede the requirement. A subset of
      /// `ApprovedDecisionIds`.
      ClosingDecisionIds: string list
      ConflictNodeIds: string list
      /// The cohort whose supplied default plan assigns the requirement.
      CohortId: string option
      /// `ConstructionPlan.operationLabel`s of plan operations that trace it.
      OperationLabels: string list
      /// Generated files that realize it, when the plan constructs it.
      GeneratedArtifacts: string list
      /// Handwritten extension contracts its obligations name.
      ExtensionSurface: string list
      /// Typed obligations that leave a question open: conflict, undecided
      /// effect failure, uncovered requirement, unsupported vocabulary.
      /// `extensionBehavior` is handwritten work, discharged by acceptance.
      OpenObligationIds: string list
      VerificationNodeIds: string list
      AcceptanceCaseIds: string list
      Acceptance: AcceptanceVerdict option
      Classes: ResolutionClass list
      Disposition: FinalDisposition }

type Ledger =
    { SchemaVersion: string
      SourceManifestDigest: string
      /// Canonical blueprint digest (`ConstructionIrWire.blueprintDigest`).
      BlueprintDigest: string
      /// `DefaultPlan.Digest` of every plan the ledger was derived from.
      DefaultPlanDigests: string list
      Rows: LedgerRow list }

type LedgerViolation =
    | InvalidBlueprint of Violation
    /// A supplied default plan was generated against another manifest or
    /// blueprint.
    | PlanBindingMismatch of cohortId: string
    /// More than one supplied plan assigns the requirement; the ledger does
    /// not choose between them.
    | AmbiguousCohort of requirementKey: string * cohortIds: string list
    /// A manifest requirement has no row.
    | UnaccountedRequirement of requirementKey: string
    | DuplicateRow of requirementKey: string
    /// A row, evidence entry, tag or claim names a key outside the manifest.
    | InventedRequirement of requirementKey: string
    | DuplicateClaim of requirementKey: string
    | UnapprovedDecision of requirementKey: string * decisionId: string
    /// A claim cites a decision a supplied plan already binds. The generator
    /// applied it; what it left open, it cannot close.
    | NonClosingDecision of requirementKey: string * decisionId: string
    | ImplementedWithoutIndependentAcceptance of requirementKey: string
    | ImplementedWithOpenObligation of requirementKey: string * obligationIds: string list
    /// Requirements outside every supplied plan are never (partially)
    /// implemented by construction.
    | ImplementedWithoutCohort of requirementKey: string
    | MissingRationale of requirementKey: string
    /// A requirement rejected or superseded, or a genuine domain decision
    /// marked implemented, without an approved decision that closes it.
    | AgentResolvedDomainDecision of requirementKey: string
    | UnclassifiedRow of requirementKey: string
    /// A ledger document's row differs from the one derived from the inputs.
    | RowMismatch of requirementKey: string
    | LedgerHeaderMismatch of field: string

let noTags =
    { FixedByRequirement = Set.empty
      ApprovedDecisionIds = Set.empty
      Claims = [] }

// ---------------------------------------------------------------------------
// Row rules. These hold for any row, derived or read from a document.
// ---------------------------------------------------------------------------

let private isBlank (value: string) = String.IsNullOrWhiteSpace value

let private sorted values = values |> List.distinct |> List.sort

let private rationaleOf disposition =
    match disposition with
    | FinalDisposition.Implemented -> None
    | FinalDisposition.PartiallyImplemented why
    | FinalDisposition.Deferred why
    | FinalDisposition.Unresolved why
    | FinalDisposition.Rejected why
    | FinalDisposition.Superseded(_, why) -> Some why

/// Decisions the row relies on to settle its open questions, including the
/// decision a supersession names.
let private closing (row: LedgerRow) =
    match row.Disposition with
    | FinalDisposition.Superseded(decision, _) -> decision :: row.ClosingDecisionIds
    | _ -> row.ClosingDecisionIds
    |> List.filter (isBlank >> not)
    |> sorted

/// Passing acceptance with at least one case, none blank and none a
/// verification node of the requirement's own blueprint.
let private independent (verificationNodeIds: string list) (verdict: AcceptanceVerdict option) (caseIds: string list) =
    verdict = Some AcceptanceVerdict.Passed
    && not (List.isEmpty caseIds)
    && caseIds |> List.forall (fun id -> not (isBlank id) && not (List.contains id verificationNodeIds))

let private independentlyAccepted (row: LedgerRow) =
    independent row.VerificationNodeIds row.Acceptance row.AcceptanceCaseIds

/// The ledger's invariants on one row.
let rowViolations (row: LedgerRow) : LedgerViolation list =
    let key = row.Requirement.Key
    let approved = Set.ofList row.ApprovedDecisionIds
    let closedBy = closing row
    let genuine = List.contains ResolutionClass.GenuineDomainDecision row.Classes
    [ if List.isEmpty row.Classes then UnclassifiedRow key
      if rationaleOf row.Disposition |> Option.exists isBlank then MissingRationale key
      match row.Disposition with
      | FinalDisposition.Superseded(decision, _) when isBlank decision -> MissingRationale key
      | _ -> ()
      for decision in closedBy do
          if not (Set.contains decision approved) then UnapprovedDecision(key, decision)
      match row.Disposition with
      | FinalDisposition.Implemented ->
          if not (independentlyAccepted row) then ImplementedWithoutIndependentAcceptance key
          if not (List.isEmpty row.OpenObligationIds) && List.isEmpty closedBy then
              ImplementedWithOpenObligation(key, row.OpenObligationIds)
      | _ -> ()
      match row.Disposition, row.CohortId with
      | (FinalDisposition.Implemented | FinalDisposition.PartiallyImplemented _), None -> ImplementedWithoutCohort key
      | _ -> ()
      // Rejecting or superseding a requirement is itself a domain decision;
      // implementing one that waits on a domain decision settles it.
      match row.Disposition with
      | FinalDisposition.Rejected _
      | FinalDisposition.Superseded _ when List.isEmpty closedBy -> AgentResolvedDomainDecision key
      | FinalDisposition.Implemented when genuine && List.isEmpty closedBy -> AgentResolvedDomainDecision key
      | _ -> () ]

/// Every manifest key has exactly one row and every row a manifest key.
let accountabilityViolations (manifest: SourceManifest) (rows: LedgerRow list) : LedgerViolation list =
    let keys = manifest.Requirements |> List.map _.Key |> Set.ofList
    let rowKeys = rows |> List.map _.Requirement.Key
    [ for key in keys do
          if not (List.contains key rowKeys) then UnaccountedRequirement key
      for key, count in List.countBy id rowKeys do
          if count > 1 then DuplicateRow key
          if not (Set.contains key keys) then InventedRequirement key ]

let private finish violations =
    violations |> List.distinct |> List.sortBy (sprintf "%A")

// ---------------------------------------------------------------------------
// Derivation.
// ---------------------------------------------------------------------------

let private isOpen kind =
    match kind with
    | TypedObligationKind.ExtensionBehavior -> false
    | TypedObligationKind.UndecidedEffectFailure
    | TypedObligationKind.Conflict
    | TypedObligationKind.UncoveredRequirement
    | TypedObligationKind.UnsupportedVocabulary -> true

let private isDomainQuestion kind =
    match kind with
    | TypedObligationKind.UndecidedEffectFailure
    | TypedObligationKind.Conflict
    | TypedObligationKind.UncoveredRequirement -> true
    | TypedObligationKind.ExtensionBehavior
    | TypedObligationKind.UnsupportedVocabulary -> false

let private assigns (plan: DefaultPlan) key =
    Set.contains key (ConstructionPlan.view plan.ValidatedPlan).AssignedRequirementKeys

let private constructs (plan: DefaultPlan) key =
    match Map.tryFind key (ConstructionPlan.view plan.ValidatedPlan).Coverage with
    | Some(Constructed _) -> true
    | _ -> false

let private labelsFor (plan: DefaultPlan) key =
    canonicalOperations plan.ValidatedPlan
    |> List.filter (fun o -> List.contains key (operationTrace o).RequirementKeys)
    |> List.map operationLabel
    |> sorted

let private ids (items: ConstructionNode list) = items |> List.map _.Id |> sorted

/// The row for one manifest requirement, before any claim.
let private deriveRow
    (blueprint: Blueprint)
    (plans: DefaultPlan list)
    (approvedSet: Set<string>)
    (evidence: Map<string, AcceptanceEvidence>)
    (tags: LedgerTags)
    (source: SourceRequirement)
    : LedgerRow =
    let key = source.Key
    let record = blueprint.Requirements |> List.find (fun r -> r.Source.Key = key)
    let nodes = blueprint.Nodes |> List.map (fun n -> n.Id, n) |> Map.ofList
    let linked = record.NodeIds |> List.choose (fun id -> Map.tryFind id nodes)
    let ofKind kind = linked |> List.filter (fun n -> n.Kind = kind)
    let cohortPlan = plans |> List.tryFind (fun p -> assigns p key)
    let obligations =
        plans |> List.collect _.Obligations |> List.filter (fun o -> List.contains key o.RequirementKeys)
        |> List.distinctBy _.Id
    let fixedByRequirement = Set.contains key tags.FixedByRequirement
    // An uncovered requirement whose behavior the operator tags as fixed by the
    // requirement text is a vocabulary gap, not an open domain question:
    // handwritten code may implement it, and independent acceptance closes it.
    let fixedButInexpressible (o: TypedObligation) =
        fixedByRequirement && o.Kind = TypedObligationKind.UncoveredRequirement
    let openObligations =
        obligations |> List.filter (fun o -> isOpen o.Kind && not (fixedButInexpressible o))
    let constructed = cohortPlan |> Option.exists (fun p -> constructs p key)
    let anchoringApproved =
        linked
        |> List.filter (fun n -> (n.Kind = Decision || n.Kind = EngineeringRationale) && Set.contains n.Id approvedSet)
        |> ids
    let acceptance = Map.tryFind key evidence
    let blueprintConflicts = ofKind NodeKind.Conflict |> ids
    let verificationIds = ofKind VerificationObligation |> ids

    let disposition, closedBy =
        let openIds = openObligations |> List.map _.Id |> sorted
        match record.Disposition with
        | Deferred why -> FinalDisposition.Deferred why, []
        | Unresolved why -> FinalDisposition.Unresolved why, []
        | Rejected why ->
            match anchoringApproved with
            | [] ->
                FinalDisposition.Unresolved(
                    sprintf "The blueprint rejects this requirement (%s), but no approved decision or engineering rationale records the rejection." why),
                []
            | approved -> FinalDisposition.Rejected why, approved
        | Superseded(target, why) ->
            match anchoringApproved with
            | [] ->
                FinalDisposition.Unresolved(
                    sprintf "The blueprint supersedes this requirement by %s (%s), but no approved decision records the supersession." target why),
                []
            | decision :: _ as approved -> FinalDisposition.Superseded(decision, sprintf "Superseded by %s: %s" target why), approved
        | Modeled ->
            match cohortPlan with
            | None ->
                let cohorts = ofKind Cohort |> ids
                FinalDisposition.Unresolved(
                    sprintf "No supplied default plan constructs this requirement (blueprint cohorts: %s)."
                        (match cohorts with [] -> "none" | c -> String.concat ", " c)),
                []
            | Some plan ->
                let cohort = (ConstructionPlan.view plan.ValidatedPlan).Binding.CohortId
                if not (List.isEmpty openIds) then
                    let why = sprintf "Open obligations in cohort %s: %s." cohort (String.concat ", " openIds)
                    (if constructed then FinalDisposition.PartiallyImplemented why else FinalDisposition.Unresolved why), []
                else
                    let how =
                        if constructed then "Constructed"
                        else "Handwritten (requirement text fixes it; the vocabulary cannot express it)"
                    match acceptance with
                    | Some a when independent verificationIds (Some a.Verdict) a.CaseIds ->
                        FinalDisposition.Implemented, []
                    | Some a when a.Verdict = AcceptanceVerdict.Failed ->
                        FinalDisposition.PartiallyImplemented(
                            sprintf "%s in cohort %s; independent acceptance failed (%s)." how cohort (String.concat ", " (sorted a.CaseIds))),
                        []
                    | _ ->
                        FinalDisposition.PartiallyImplemented(
                            sprintf "%s in cohort %s; no passing independent acceptance evidence." how cohort),
                        []

    let approvedIds = anchoringApproved
    let genuine =
        obligations |> List.exists (fun o -> isDomainQuestion o.Kind && not (fixedButInexpressible o))
        || (match record.Disposition with Unresolved _ -> true | _ -> false)
        || not (List.isEmpty blueprintConflicts)
        || (not constructed && List.isEmpty approvedIds && not fixedByRequirement)

    { Requirement = source
      Categories =
        linked |> List.map _.Kind
        |> List.filter (fun k -> k <> Cohort && k <> VerificationObligation)
        |> List.distinct |> List.sort
      BlueprintDisposition = record.Disposition
      DependencyNodeIds = linked |> List.collect _.DependsOn |> sorted
      ApprovedDecisionIds = approvedIds
      ClosingDecisionIds = sorted closedBy
      ConflictNodeIds =
        blueprintConflicts
        @ (obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.Conflict) |> List.collect _.NodeIds)
        |> sorted
      CohortId = cohortPlan |> Option.map (fun p -> (ConstructionPlan.view p.ValidatedPlan).Binding.CohortId)
      OperationLabels = cohortPlan |> Option.map (fun p -> labelsFor p key) |> Option.defaultValue []
      GeneratedArtifacts =
        match cohortPlan with
        | Some plan when constructed -> sorted plan.GeneratedLayout
        | _ -> []
      ExtensionSurface = obligations |> List.collect _.ExtensionSurface |> sorted
      OpenObligationIds = openObligations |> List.map _.Id |> sorted
      VerificationNodeIds = verificationIds
      AcceptanceCaseIds = acceptance |> Option.map (fun a -> sorted a.CaseIds) |> Option.defaultValue []
      Acceptance = acceptance |> Option.map _.Verdict
      Classes =
        [ if fixedByRequirement then ResolutionClass.FixedByRequirement
          if not (List.isEmpty approvedIds) then ResolutionClass.AlreadyApproved
          if constructed then ResolutionClass.MechanicallyDetermined
          if genuine then ResolutionClass.GenuineDomainDecision
          if openObligations |> List.exists (fun o -> o.Kind = TypedObligationKind.UnsupportedVocabulary)
             || obligations |> List.exists fixedButInexpressible then
              ResolutionClass.UnsupportedByVocabulary ]
      Disposition = disposition }

/// The decisions a claim cites (including the decision a supersession names).
let private citedBy (claim: DispositionClaim) =
    claim.DecisionIds
    @ (match claim.Disposition with FinalDisposition.Superseded(decision, _) -> [ decision ] | _ -> [])
    |> List.filter (isBlank >> not)
    |> sorted

/// Applies an explicit claim. Only closable decisions the claim cites are
/// added; the row rules then decide whether the claim stands.
let private applyClaim (closable: Set<string>) (claim: DispositionClaim) (row: LedgerRow) =
    let cited = citedBy claim |> List.filter (fun d -> Set.contains d closable)
    let approved = row.ApprovedDecisionIds @ cited |> sorted
    { row with
        Disposition = claim.Disposition
        ApprovedDecisionIds = approved
        ClosingDecisionIds = row.ClosingDecisionIds @ (claim.DecisionIds |> List.filter (fun d -> Set.contains d closable)) |> sorted
        Classes =
            if List.isEmpty approved then row.Classes
            else ResolutionClass.AlreadyApproved :: row.Classes |> List.distinct |> List.sort }

let private inputViolations
    (manifest: SourceManifest)
    (blueprint: Blueprint)
    (plans: DefaultPlan list)
    (planBound: Set<string>)
    (evidence: Map<string, AcceptanceEvidence>)
    (tags: LedgerTags)
    =
    let keys = manifest.Requirements |> List.map _.Key |> Set.ofList
    let blueprintDigest = ConstructionIrWire.blueprintDigest blueprint
    let cohortOf (p: DefaultPlan) = p.Provenance.CohortId
    [ for plan in plans do
          if plan.Provenance.SourceManifestDigest <> manifest.Digest || plan.Provenance.BlueprintDigest <> blueprintDigest then
              PlanBindingMismatch(cohortOf plan)
      for key in keys do
          match plans |> List.filter (fun p -> assigns p key) with
          | _ :: _ :: _ as many -> AmbiguousCohort(key, many |> List.map cohortOf |> List.sort)
          | _ -> ()
      for key in Seq.append (Map.keys evidence) tags.FixedByRequirement do
          if not (Set.contains key keys) then InventedRequirement key
      for claim in tags.Claims do
          if not (Set.contains claim.RequirementKey keys) then InventedRequirement claim.RequirementKey
          for decision in citedBy claim do
              if Set.contains decision planBound then NonClosingDecision(claim.RequirementKey, decision)
              elif not (Set.contains decision tags.ApprovedDecisionIds) then UnapprovedDecision(claim.RequirementKey, decision)
      for key, count in tags.Claims |> List.countBy _.RequirementKey do
          if count > 1 then DuplicateClaim key ]

/// Derives the ledger. Pure and deterministic. `plans` may be empty or cover
/// only some cohorts: a requirement no supplied plan assigns is `Unresolved`
/// (or keeps its blueprint `deferred`, approved `rejected` or approved
/// `superseded` disposition), never omitted.
let buildLedger
    (manifest: SourceManifest)
    (blueprint: Blueprint)
    (plans: DefaultPlan list)
    (evidence: Map<string, AcceptanceEvidence>)
    (tags: LedgerTags)
    : Result<Ledger, LedgerViolation list> =
    match ConstructionIr.validate manifest blueprint with
    | _ :: _ as structural -> Error(structural |> List.map InvalidBlueprint |> finish)
    | [] ->
        let planBound =
            plans
            |> List.collect (fun p -> (ConstructionPlan.view p.ValidatedPlan).Binding.DecisionIds)
            |> Set.ofList
        let approvedSet = Set.union planBound tags.ApprovedDecisionIds
        // A claim can close a question only with a program-level approval.
        // A decision a supplied plan binds was already applied by the
        // generator: whatever it left open, it does not close.
        let closable = Set.difference tags.ApprovedDecisionIds planBound
        match inputViolations manifest blueprint plans planBound evidence tags with
        | _ :: _ as invalid -> Error(finish invalid)
        | [] ->
            let claims = tags.Claims |> List.map (fun c -> c.RequirementKey, c) |> Map.ofList
            let rows =
                manifest.Requirements
                |> List.map (fun source ->
                    let row = deriveRow blueprint plans approvedSet evidence tags source
                    match Map.tryFind source.Key claims with
                    | Some claim -> applyClaim closable claim row
                    | None -> row)
                |> List.sortBy _.Requirement.Key
            match accountabilityViolations manifest rows @ List.collect rowViolations rows with
            | _ :: _ as violations -> Error(finish violations)
            | [] ->
                Ok
                    { SchemaVersion = LedgerSchemaVersion
                      SourceManifestDigest = manifest.Digest
                      BlueprintDigest = ConstructionIrWire.blueprintDigest blueprint
                      DefaultPlanDigests = plans |> List.map _.Digest |> sorted
                      Rows = rows }

/// Checks a ledger document (for example one read with
/// `ConstructionLedgerWire.readLedger`) against the inputs it claims to be
/// derived from. Empty means it is exactly the derived ledger.
let validateLedger
    (manifest: SourceManifest)
    (blueprint: Blueprint)
    (plans: DefaultPlan list)
    (evidence: Map<string, AcceptanceEvidence>)
    (tags: LedgerTags)
    (ledger: Ledger)
    : LedgerViolation list =
    let intrinsic = accountabilityViolations manifest ledger.Rows @ List.collect rowViolations ledger.Rows
    match buildLedger manifest blueprint plans evidence tags with
    | Error violations -> finish (violations @ intrinsic)
    | Ok expected ->
        let header =
            [ if ledger.SchemaVersion <> expected.SchemaVersion then LedgerHeaderMismatch "schemaVersion"
              if ledger.SourceManifestDigest <> expected.SourceManifestDigest then LedgerHeaderMismatch "sourceManifestDigest"
              if ledger.BlueprintDigest <> expected.BlueprintDigest then LedgerHeaderMismatch "blueprintDigest"
              if sorted ledger.DefaultPlanDigests <> expected.DefaultPlanDigests then LedgerHeaderMismatch "defaultPlanDigests" ]
        let mismatched =
            [ for row in expected.Rows do
                  match ledger.Rows |> List.filter (fun r -> r.Requirement.Key = row.Requirement.Key) with
                  | [ actual ] when actual <> row -> RowMismatch row.Requirement.Key
                  | _ -> () ]
        finish (header @ intrinsic @ mismatched)
