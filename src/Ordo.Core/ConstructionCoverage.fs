/// The pre-execution coverage report, `ordo.coverage-report/1`
/// (CONSTRUCT-010, phase 7). Before any agent runs, it says how much of a
/// cohort Ordo will generate, what is left to handwritten extensions, which
/// decisions are still open, and which construction route the evidence
/// favors. It is derived from a default plan, its obligations and its choice
/// classification; nothing in it is authored, and it is advisory only. It
/// grants nothing and verifies nothing.
module Ordo.Core.ConstructionCoverage

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionClassification
open Ordo.Core.ConstructionDefaultPlan

[<Literal>]
let CoverageSchemaVersion = "ordo.coverage-report/1"

/// What construction will do for one source requirement.
[<RequireQualifiedAccess>]
type RequirementStatus =
    /// Generated structure realizes it and nothing about it is left open.
    | FullyGenerated
    /// Generated structure realizes part of it; an extension, an open
    /// decision or unsupported content remains.
    | PartiallyGenerated
    /// Nothing structural is generated for it; a handwritten extension whose
    /// decisions are all settled implements it.
    | NeedsExtension
    /// An open decision blocks it: a conflict, an undecided pair, effect
    /// failure or extension decision point, or nothing constructs it.
    | Unresolved
    /// Approved content the vocabulary cannot express.
    | Unsupported
    /// Not assigned to this cohort: deferred, rejected, superseded, or
    /// another cohort's work. Reported, never counted as generated.
    | OutsideCohort

type RequirementCoverageRow =
    { Key: string
      Status: RequirementStatus
      /// Plan operation labels that generate structure for it.
      GeneratedBy: string list
      /// Obligation ids that cite it.
      ObligationIds: string list }

/// A handwritten extension contract and a deterministic size estimate.
type ExtensionEstimate =
    { Contract: string
      RequirementKeys: string list
      DecisionPoints: int
      OpenDecisionPoints: int
      /// Heuristic complexity points: 1 per contract, 1 per requirement it
      /// realizes, 1 per decision point, 2 for an effect (its outcome
      /// handling). Uncalibrated against effort; comparable only between
      /// reports.
      Points: int }

type CoverageTotals =
    { SourceRequirements: int
      AssignedRequirements: int
      FullyGenerated: int
      PartiallyGenerated: int
      NeedsExtension: int
      Unresolved: int
      Unsupported: int
      OutsideCohort: int
      /// Open decision obligations (conflict, undecided pair, effect failure
      /// or extension point, uncovered requirement).
      UnresolvedDecisions: int
      /// Classified choices that are unresolved or unsupported.
      OpenChoices: int
      UnsupportedChoices: int
      GeneratedFiles: int
      GeneratedConstructs: int
      ExtensionContracts: int
      EstimatedExtensionComplexity: int
      /// Blueprint verification obligations in the cohort's required scope,
      /// plus the cohort-level independent verification every construction
      /// carries.
      VerificationObligations: int }

/// The construction routes CONSTRUCT-008 measured.
[<RequireQualifiedAccess>]
type Route =
    /// Mechanical-first: Ordo generates the default plan and structure; the
    /// agent meets the obligations.
    | C
    /// Constrained agent implementation (the operator brief's Mode A+), for
    /// low vocabulary coverage. Not unrestricted agent coding: it still uses
    /// pinned requirements, approved decisions, the complete disposition
    /// ledger, typed implementation obligations, capability-scoped execution,
    /// architecture constraints and independent Dokimos verification.
    | APlus
    /// Agent-authored construction plans, retained for research and approved
    /// decision-record authoring, not the preferred production path.
    /// Recommended only when no default plan can be generated.
    | B

/// One measured slice a calibration was fitted on.
type CalibrationPoint =
    { Slice: string
      /// This report's structure ratio, computed on the slice's frozen inputs.
      StructureRatio: float
      /// The arm measured as cheaper on the primary metric: "C" or "A".
      CheaperArm: string
      /// Whether the arms' ranges were separated rather than overlapping.
      Separated: bool }

/// The evidence a recommendation is computed from. The default value is
/// `measuredCalibration`; a host may supply its own, and the report names it.
type RouteCalibration =
    { Id: string
      /// C is recommended at or above this structure ratio:
      /// generated constructs / (generated constructs + assigned requirements
      /// nothing generates). A proxy for how much handwritten work generation
      /// replaces, not a measured cost.
      StructureRatioThreshold: float
      /// The measured slices the threshold was fitted on, so a later
      /// out-of-sample check can compare against them mechanically.
      FittedOn: CalibrationPoint list
      /// Short statements of the evidence and its limits.
      Evidence: string list }

type RouteRecommendation =
    { Route: Route
      /// Generated (fully or partially) assigned requirements / assigned.
      GeneratedShare: float
      StructureRatio: float
      Calibration: string
      /// The calibration's threshold and fitted points, reported next to the ratio.
      Threshold: float
      FittedOn: CalibrationPoint list
      Rationale: string
      /// Why this recommendation may be wrong. Never empty.
      Uncertainty: string list
      Advisory: bool }

type CoverageReport =
    { CohortId: string
      PlanDigest: string
      DefaultPlanDigest: string
      VocabularyVersion: string
      Rows: RequirementCoverageRow list
      Extensions: ExtensionEstimate list
      Totals: CoverageTotals
      Recommendation: RouteRecommendation }

// ---------------------------------------------------------------------------
// Calibration.
// ---------------------------------------------------------------------------

/// Fitted to the four slices CONSTRUCT-008 measured (n = 3 runs per arm,
/// one model), with this report's own structure ratio computed on each
/// slice's frozen ECIR and decision records. The requirement share does NOT
/// separate them (Summa 0.14 was cheaper, Chrona 0.22 dearer), so it is
/// reported but not used. See docs/architecture/coverage-report.md.
let measuredCalibration =
    { Id = "construct-008-pilot/1"
      StructureRatioThreshold = 0.45
      FittedOn =
        [ { Slice = "time-entry-timer-pilot-01"; StructureRatio = 0.7692; CheaperArm = "C"; Separated = false }
          { Slice = "summa-delivery"; StructureRatio = 0.4783; CheaperArm = "C"; Separated = true }
          { Slice = "chrona-timer"; StructureRatio = 0.3913; CheaperArm = "A"; Separated = false }
          { Slice = "time-entry-expanded-01"; StructureRatio = 0.1304; CheaperArm = "A"; Separated = true } ]
      Evidence =
        [ "time-entry timer pilot 01: ratio 0.77 (10 constructs, 3 not generated); C cheaper than A by median (primary -4%, cumulative -12%), ranges overlapping"
          "Summa delivery (Phase 9): ratio 0.48 (22 constructs, 24 not generated); C cheaper than A, separated on the primary metric (-13.6%)"
          "Chrona timer (Phase 9): ratio 0.39 (9 constructs, 14 not generated); C dearer than A by median (+14%), ranges overlapping"
          "time-entry expanded 01: ratio 0.13 (6 constructs, 40 not generated); C dearer than A (+8% primary, separated; +33% cumulative)"
          "The threshold lies between the nearest opposite outcomes (0.39 and 0.48); four points, n = 3 runs per arm, one model; correctness was equal across arms in every slice" ] }

// ---------------------------------------------------------------------------
// Derivation.
// ---------------------------------------------------------------------------

let private isDecision kind =
    match kind with
    | TypedObligationKind.Conflict
    | TypedObligationKind.UndecidedEffectFailure
    | TypedObligationKind.UncoveredRequirement
    | TypedObligationKind.UndecidedTransition
    | TypedObligationKind.UndecidedExtensionDecision -> true
    | TypedObligationKind.ExtensionBehavior
    | TypedObligationKind.UnsupportedVocabulary -> false

let private isStructural operation =
    match operation with
    | DefineState _ | DefineTransition _ | DefineRefusal _ | DefineInvariant _ | BindCapability _
    | DefineBranchingTransition _ -> true
    | DefineWorkflow _ | DeclarePort _ | DeclareUnresolved _ | DeclarePairRule _
    | DeclareUndecidedTransition _ | DeclareExtensionInventory _ | DefineRefusalCode _ | AssignRefusalCode _
    | DeclareEffectPolicy _ -> false

let private distinctSorted values = values |> List.distinct |> List.sort

let private status
    (operations: OperationDraft list)
    (obligations: TypedObligation list)
    (openChoiceKeys: Set<string>)
    (key: string)
    =
    let generatedBy =
        operations
        |> List.filter (fun o -> isStructural o && List.contains key (operationTrace o).RequirementKeys)
        |> List.map operationLabel
        |> distinctSorted
    let citing = obligations |> List.filter (fun o -> List.contains key o.RequirementKeys)
    let has predicate = citing |> List.exists (fun o -> predicate o.Kind)
    let open' = has isDecision || Set.contains key openChoiceKeys
    let unsupported = has ((=) TypedObligationKind.UnsupportedVocabulary)
    let extension = has ((=) TypedObligationKind.ExtensionBehavior)
    let s =
        match generatedBy with
        | _ :: _ when open' || unsupported || extension -> RequirementStatus.PartiallyGenerated
        | _ :: _ -> RequirementStatus.FullyGenerated
        | [] when open' -> RequirementStatus.Unresolved
        | [] when unsupported -> RequirementStatus.Unsupported
        | [] when extension -> RequirementStatus.NeedsExtension
        // Nothing constructs it and no obligation says why: never "generated".
        | [] -> RequirementStatus.Unresolved
    { Key = key; Status = s; GeneratedBy = generatedBy; ObligationIds = citing |> List.map _.Id |> distinctSorted }

let private extensionEstimates (plan: ValidatedPlanView) (obligations: TypedObligation list) =
    let w = plan.Workflow
    let inventory target =
        w.Inventories |> List.tryFind (fun i -> i.Extension = target)
    let estimate contract target keys isEffect =
        let points = inventory target |> Option.map _.DecisionPoints |> Option.defaultValue []
        let openPoints = points |> List.filter (fun p -> p.Resolution <> DecisionPointResolution.Decided) |> List.length
        let keys = distinctSorted keys
        { Contract = contract
          RequirementKeys = keys
          DecisionPoints = points.Length
          OpenDecisionPoints = openPoints
          Points = 1 + keys.Length + points.Length + (if isEffect then 2 else 0) }
    let guards =
        w.Transitions
        |> List.collect (fun t -> t.Guards |> List.map (fun g -> g.Name, t.Trace.RequirementKeys))
        |> List.groupBy (fun (n, _) -> n.Key)
        |> List.map (fun (_, group) ->
            let name = fst group.Head
            estimate (ConstructionLayout.guardContract w.Name name) (ExtensionTarget.Guard name) (group |> List.collect snd) false)
    let invariants =
        w.Invariants |> List.map (fun i -> estimate (ConstructionLayout.invariantContract w.Name i.Name) (ExtensionTarget.Invariant i.Name) i.Trace.RequirementKeys false)
    let ports =
        w.Ports
        |> List.map (fun p ->
            match p.Kind with
            | CurrentTimePort ->
                { Contract = ConstructionLayout.currentTimeContract w.Name
                  RequirementKeys = distinctSorted p.Trace.RequirementKeys
                  DecisionPoints = 0
                  OpenDecisionPoints = 0
                  Points = 1 }
            | TransitionEffectPort(command, _) ->
                estimate (ConstructionLayout.effectContract w.Name command) (ExtensionTarget.Effect command) p.Trace.RequirementKeys true)
    let selectors =
        w.BranchingTransitions
        |> List.map (fun b ->
            estimate (ConstructionLayout.selectionContract w.Name b.InState b.OnCommand) (ExtensionTarget.Selection(b.InState, b.OnCommand)) b.Trace.RequirementKeys false)
    guards @ invariants @ ports @ selectors |> List.sortBy _.Contract

let private share part whole = if whole = 0 then 0.0 else float part / float whole

let private recommend (calibration: RouteCalibration) (totals: CoverageTotals) =
    let generated = share (totals.FullyGenerated + totals.PartiallyGenerated) totals.AssignedRequirements
    let notGenerated = totals.NeedsExtension + totals.Unresolved + totals.Unsupported
    let ratio = share totals.GeneratedConstructs (totals.GeneratedConstructs + notGenerated)
    let margin = abs (ratio - calibration.StructureRatioThreshold)
    let route = if ratio >= calibration.StructureRatioThreshold then Route.C else Route.APlus
    let rationale =
        match route with
        | Route.C ->
            sprintf "Ordo generates %d constructs against %d assigned requirements it does not generate (structure ratio %.2f, threshold %.2f). In the measured slices at or above this ratio, the generated structure replaced enough handwritten code and self-testing for C to cost less than A."
                totals.GeneratedConstructs notGenerated ratio calibration.StructureRatioThreshold
        | _ ->
            sprintf "Ordo generates %d constructs against %d assigned requirements it does not generate (structure ratio %.2f, threshold %.2f). In the measured slices below this ratio, most work was handwritten anyway and C's handoff overhead outweighed the generated structure, so A with Ordo's handoff is the cheaper starting point."
                totals.GeneratedConstructs notGenerated ratio calibration.StructureRatioThreshold
    let uncertainty =
        [ yield sprintf "Calibration %s is fitted to four slices with n = 3 runs per arm and one model; it is directional, not statistical, and the gap between opposite outcomes is narrow (0.39 to 0.48)." calibration.Id
          if margin < 0.1 then
              yield sprintf "The structure ratio is within %.2f of the threshold; measured slices this close overlapped between arms." margin
          if totals.UnresolvedDecisions > 0 then
              yield sprintf "%d decisions are open. Every route must escalate them; none may be resolved by the implementer." totals.UnresolvedDecisions
          if totals.UnsupportedChoices > 0 then
              yield sprintf "%d choices are decided by omission in this vocabulary (unsupported); generating them would hide those decisions." totals.UnsupportedChoices
          yield "Token cost was the measured outcome; correctness was equal across arms, so this recommendation does not trade correctness." ]
    { Route = route
      GeneratedShare = Math.Round(generated, 4)
      StructureRatio = Math.Round(ratio, 4)
      Calibration = calibration.Id
      Threshold = calibration.StructureRatioThreshold
      FittedOn = calibration.FittedOn
      Rationale = rationale
      Uncertainty = uncertainty
      Advisory = true }

/// The coverage report of a default plan against its prepared cohort.
/// Deterministic: equal inputs give equal reports and equal bytes.
let reportWith (calibration: RouteCalibration) (prepared: PreparedCohort) (plan: DefaultPlan) : CoverageReport =
    let cohort = preparedCohortView prepared
    let planView = ConstructionPlan.view plan.ValidatedPlan
    let classification = classify plan.ValidatedPlan
    let openChoices = openChoices classification
    let openChoiceKeys =
        openChoices
        |> List.filter (fun c -> match c.Class with ChoiceClass.Unresolved _ -> true | _ -> false)
        |> List.collect _.RequirementKeys
        |> Set.ofList
    let operations = canonicalOperations plan.ValidatedPlan
    let assigned = cohort.AssignedRequirementKeys
    let rows =
        cohort.Manifest.Requirements
        |> List.map _.Key
        |> distinctSorted
        |> List.map (fun key ->
            if Set.contains key assigned then status operations plan.Obligations openChoiceKeys key
            else { Key = key; Status = RequirementStatus.OutsideCohort; GeneratedBy = []; ObligationIds = [] })
    let count s = rows |> List.filter (fun r -> r.Status = s) |> List.length
    let extensions = extensionEstimates planView plan.Obligations
    let w = planView.Workflow
    let verification =
        cohort.Blueprint.Nodes
        |> List.filter (fun n -> n.Kind = VerificationObligation && Set.contains n.Id cohort.RequiredNodeIds)
        |> List.length
    let totals =
        { SourceRequirements = rows.Length
          AssignedRequirements = assigned.Count
          FullyGenerated = count RequirementStatus.FullyGenerated
          PartiallyGenerated = count RequirementStatus.PartiallyGenerated
          NeedsExtension = count RequirementStatus.NeedsExtension
          Unresolved = count RequirementStatus.Unresolved
          Unsupported = count RequirementStatus.Unsupported
          OutsideCohort = count RequirementStatus.OutsideCohort
          UnresolvedDecisions = plan.Obligations |> List.filter (_.Kind >> isDecision) |> List.length
          OpenChoices = openChoices.Length
          UnsupportedChoices = openChoices |> List.filter (fun c -> match c.Class with ChoiceClass.Unsupported _ -> true | _ -> false) |> List.length
          GeneratedFiles = plan.GeneratedLayout.Length
          GeneratedConstructs = w.States.Length + w.Transitions.Length + w.BranchingTransitions.Length + w.Refusals.Length + w.Ports.Length
          ExtensionContracts = extensions.Length
          EstimatedExtensionComplexity = extensions |> List.sumBy _.Points
          VerificationObligations = verification + 1 }
    { CohortId = cohort.CohortId
      PlanDigest = plan.PlanDigest
      DefaultPlanDigest = plan.Digest
      VocabularyVersion = planView.Binding.VocabularyVersion
      Rows = rows
      Extensions = extensions
      Totals = totals
      Recommendation = recommend calibration totals }

let report prepared plan = reportWith measuredCalibration prepared plan

/// When no default plan can be generated, only route B can proceed: the
/// decision records must be authored and approved first. The refusals say why.
let recommendWithoutPlan (refusals: DefaultPlanRefusal list) : RouteRecommendation =
    { Route = Route.B
      GeneratedShare = 0.0
      StructureRatio = 0.0
      Calibration = measuredCalibration.Id
      Threshold = measuredCalibration.StructureRatioThreshold
      FittedOn = measuredCalibration.FittedOn
      Rationale =
        sprintf "No default plan can be generated (%d refusals), so nothing can be generated yet. Author and approve construction decision records first; B is the authoring path for them."
            refusals.Length
      Uncertainty =
        [ "Route B was the most expensive arm measured; it is recommended here only because C and A+ have no approved inputs to start from."
          "Once records are approved, recompute this report." ]
      Advisory = true }

// ---------------------------------------------------------------------------
// Canonical encoding.
// ---------------------------------------------------------------------------

let statusName s =
    match s with
    | RequirementStatus.FullyGenerated -> "fullyGenerated"
    | RequirementStatus.PartiallyGenerated -> "partiallyGenerated"
    | RequirementStatus.NeedsExtension -> "needsExtension"
    | RequirementStatus.Unresolved -> "unresolved"
    | RequirementStatus.Unsupported -> "unsupported"
    | RequirementStatus.OutsideCohort -> "outsideCohort"

let routeName route =
    match route with
    | Route.C -> "C"
    | Route.APlus -> "A+"
    | Route.B -> "B"

let private jStrings values = JArray(List.map JString values)
let private jInt (n: int) = JInt(int64 n)

let recommendationJson (r: RouteRecommendation) =
    JObject [ "route", JString(routeName r.Route)
              "advisory", JBool r.Advisory
              "generatedShare", JFloat r.GeneratedShare
              "structureRatio", JFloat r.StructureRatio
              "calibration", JString r.Calibration
              "threshold", JFloat r.Threshold
              "fittedOn",
              JArray [ for p in r.FittedOn ->
                         JObject [ "slice", JString p.Slice
                                   "structureRatio", JFloat p.StructureRatio
                                   "cheaperArm", JString p.CheaperArm
                                   "separated", JBool p.Separated ] ]
              "rationale", JString r.Rationale
              "uncertainty", jStrings r.Uncertainty ]

let toJson (r: CoverageReport) =
    let t = r.Totals
    JObject [ "schemaVersion", JString CoverageSchemaVersion
              "notice", JString "Advisory pre-execution report. Not authorization, not evidence of implementation or verification."
              "cohortId", JString r.CohortId
              "planDigest", JString r.PlanDigest
              "defaultPlanDigest", JString r.DefaultPlanDigest
              "vocabularyVersion", JString r.VocabularyVersion
              "totals",
              JObject [ "sourceRequirements", jInt t.SourceRequirements
                        "assignedRequirements", jInt t.AssignedRequirements
                        "fullyGenerated", jInt t.FullyGenerated
                        "partiallyGenerated", jInt t.PartiallyGenerated
                        "needsExtension", jInt t.NeedsExtension
                        "unresolved", jInt t.Unresolved
                        "unsupported", jInt t.Unsupported
                        "outsideCohort", jInt t.OutsideCohort
                        "unresolvedDecisions", jInt t.UnresolvedDecisions
                        "openChoices", jInt t.OpenChoices
                        "unsupportedChoices", jInt t.UnsupportedChoices
                        "generatedFiles", jInt t.GeneratedFiles
                        "generatedConstructs", jInt t.GeneratedConstructs
                        "extensionContracts", jInt t.ExtensionContracts
                        "estimatedExtensionComplexity", jInt t.EstimatedExtensionComplexity
                        "verificationObligations", jInt t.VerificationObligations ]
              "requirements",
              JArray [ for row in r.Rows ->
                         JObject [ "key", JString row.Key
                                   "status", JString(statusName row.Status)
                                   "generatedBy", jStrings row.GeneratedBy
                                   "obligationIds", jStrings row.ObligationIds ] ]
              "extensions",
              JArray [ for e in r.Extensions ->
                         JObject [ "contract", JString e.Contract
                                   "requirementKeys", jStrings e.RequirementKeys
                                   "decisionPoints", jInt e.DecisionPoints
                                   "openDecisionPoints", jInt e.OpenDecisionPoints
                                   "complexityPoints", jInt e.Points ] ]
              "recommendation", recommendationJson r.Recommendation ]

/// Canonical bytes: sorted keys, rows sorted by requirement key.
let encodeCoverageReport (r: CoverageReport) = toJson r |> renderCanonical

let digest (r: CoverageReport) =
    encodeCoverageReport r |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
    |> fun hex -> "sha256:" + hex.ToLowerInvariant()
