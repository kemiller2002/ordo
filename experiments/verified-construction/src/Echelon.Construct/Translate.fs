/// Translation of an admitted handoff into Ordo's existing construction
/// inputs: an ECIR source manifest, an ECIR blueprint, the externally
/// authorized decision set, and an `ordo.construction-vocabulary/4` plan.
///
/// The handoff carries requirements as prose, not as a state model. The
/// mapping from that prose to states and transitions is therefore a fixed,
/// reviewable table for this slice (`invoiceWorkflow`), in which every row
/// names the handoff IDs it rests on. Translation refuses rather than guesses
/// when a row's authority is missing, not accepted, superseded, or when a
/// policy question it relies on is absent or resolved. Ordo then validates
/// the plan against the prepared cohort exactly as it would any other plan.
module Echelon.Construct.Translate

open System
open System.Globalization
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Echelon.Construct.Handoff
open Echelon.Construct.Admission

type TranslationError =
    | MissingAuthority of id: string * needed: string
    | NotAccepted of id: string
    | WrongDecisionKind of id: string * expected: DecisionKind
    | MissingParameter of decisionId: string * parameter: string
    | InvalidParameter of decisionId: string * parameter: string * value: string
    | QuestionOutcomeUnsupported of questionId: string * outcome: PermittedOutcome
    | MissingSourceDigest of id: string
    | SourceDigestMismatch of path: string * declared: string * actual: string
    | SourceUnavailable of path: string
    | CohortNotReady of string list
    | PlanRejected of string list

// ---------------------------------------------------------------------------
// The slice table: what the invoice workflow is made of, and on whose authority.
// ---------------------------------------------------------------------------

[<Literal>]
let WorkflowName = "Invoice"

/// Contract, verification and cohort node IDs that ECIR requires. They are
/// structural containers, not authority, and never appear in the trace.
let ContractNode = "CON-INVOICE-WORKFLOW"
let PersistenceContractNode = "CON-PAYMENT-PERSISTENCE"
let VerificationNode = "VER-INVOICE-WORKFLOW"
let PersistenceVerificationNode = "VER-PAYMENT-PERSISTENCE"
let WorkflowCohort = "COH-INVOICE-WORKFLOW"
let PersistenceCohort = "COH-PAYMENT-PERSISTENCE"

let LifecycleRequirement = "REQ-INV-001"
let PaymentRequirement = "REQ-INV-002"
let PersistenceRequirement = "REQ-INV-003"
let LimitDecision = "DEC-FIN-001"
let BoundaryDecision = "DEC-FIN-002"
let VoidQuestion = "Q-FIN-001"

/// A refusal code: its plan name (domain words), its wire string, and the IDs
/// whose rule it reports. The wire string of the limit code is not chosen
/// here; it is the DEC-FIN-001 `refusalCode` parameter (see `codes`).
type CodeRow =
    { Name: string
      Wire: string
      Requirements: string list
      Nodes: string list }

/// The decided, typed parameters of the decisions this slice constructs from.
type SliceAuthority =
    { Limit: decimal
      LimitText: string
      LimitRefusalCode: string
      PaymentsServiceRole: string
      VoidQuestion: PolicyQuestion }

let codes (authority: SliceAuthority) : CodeRow list =
    [ { Name = "payment limit exceeded"; Wire = authority.LimitRefusalCode; Requirements = [ PaymentRequirement ]; Nodes = [ LimitDecision ] }
      { Name = "policy unresolved Q FIN 001"; Wire = "POLICY_UNRESOLVED_Q_FIN_001"; Requirements = [ LifecycleRequirement ]; Nodes = [ VoidQuestion ] }
      { Name = "exceeds outstanding balance"; Wire = "EXCEEDS_OUTSTANDING_BALANCE"; Requirements = [ PaymentRequirement ]; Nodes = [ ContractNode ] }
      { Name = "non positive amount"; Wire = "NON_POSITIVE_AMOUNT"; Requirements = [ PaymentRequirement ]; Nodes = [ ContractNode ] }
      { Name = "currency mismatch"; Wire = "CURRENCY_MISMATCH"; Requirements = [ PaymentRequirement ]; Nodes = [ ContractNode ] }
      { Name = "invoice closed"; Wire = "INVOICE_CLOSED"; Requirements = [ LifecycleRequirement ]; Nodes = [ ContractNode ] }
      { Name = "command not allowed in state"; Wire = "COMMAND_NOT_ALLOWED_IN_STATE"; Requirements = [ LifecycleRequirement ]; Nodes = [ ContractNode ] } ]

// ---------------------------------------------------------------------------
// Authority lookups. Each refuses instead of guessing.
// ---------------------------------------------------------------------------

let private requirementOf (h: Handoff) id =
    match h.Requirements |> List.tryFind (fun r -> r.Id = id) with
    | None -> Error(MissingAuthority(id, "requirement"))
    | Some r when r.Status <> RequirementStatus.Accepted -> Error(NotAccepted id)
    | Some r -> Ok r

let private decisionOf (h: Handoff) id kind =
    match h.Decisions |> List.tryFind (fun d -> d.Id = id) with
    | None -> Error(MissingAuthority(id, "decision"))
    | Some d when d.Status <> DecisionStatus.Accepted -> Error(NotAccepted id)
    | Some d when d.Kind <> kind -> Error(WrongDecisionKind(id, kind))
    | Some d -> Ok d

let private parameter (d: Decision) name =
    match d.Parameters |> List.tryFind (fst >> (=) name) with
    | Some(_, v) -> Ok v
    | None -> Error(MissingParameter(d.Id, name))

let private positiveDecimal (d: Decision) name (raw: string) =
    match Decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture) with
    | true, v when v > 0m -> Ok v
    | _ -> Error(InvalidParameter(d.Id, name, raw))

let private codeText (d: Decision) name (raw: string) =
    if raw |> Seq.forall (fun c -> (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c = '_') && Char.IsAsciiLetterUpper raw[0]
    then Ok raw
    else Error(InvalidParameter(d.Id, name, raw))

let private collect (results: Result<'a, TranslationError> list) : Result<'a list, TranslationError list> =
    let errors = results |> List.choose (function Error e -> Some e | Ok _ -> None)
    if List.isEmpty errors then Ok(results |> List.choose (function Ok v -> Some v | Error _ -> None)) else Error errors

/// The decided values this slice needs, read only from accepted decisions and
/// the unresolved question. Nothing is defaulted.
let sliceAuthority (admitted: AdmittedHandoff) : Result<SliceAuthority, TranslationError list> =
    let h = handoffOf admitted
    let requirements =
        [ LifecycleRequirement; PaymentRequirement; PersistenceRequirement ] |> List.map (requirementOf h >> Result.map ignore)
    let limit =
        decisionOf h LimitDecision DecisionKind.PolicyLimit
        |> Result.bind (fun d ->
            parameter d "limit"
            |> Result.bind (fun raw -> positiveDecimal d "limit" raw |> Result.map (fun v -> v, raw))
            |> Result.bind (fun (v, raw) -> parameter d "refusalCode" |> Result.bind (codeText d "refusalCode") |> Result.map (fun c -> v, raw, c)))
    let role = decisionOf h BoundaryDecision DecisionKind.Boundary |> Result.bind (fun d -> parameter d "role")
    let question =
        match h.PolicyQuestions |> List.tryFind (fun q -> q.Id = VoidQuestion) with
        | None -> Error(MissingAuthority(VoidQuestion, "unresolved policy question"))
        | Some q when q.PermittedOutcome <> PermittedOutcome.RefuseAndRecord -> Error(QuestionOutcomeUnsupported(q.Id, q.PermittedOutcome))
        | Some q -> Ok q
    match collect requirements, limit, role, question with
    | Ok _, Ok(l, raw, code), Ok r, Ok q ->
        Ok { Limit = l; LimitText = raw; LimitRefusalCode = code; PaymentsServiceRole = r; VoidQuestion = q }
    | reqs, l, r, q ->
        Error(
            (match reqs with Error e -> e | Ok _ -> [])
            @ (match l with Error e -> [ e ] | Ok _ -> [])
            @ (match r with Error e -> [ e ] | Ok _ -> [])
            @ (match q with Error e -> [ e ] | Ok _ -> []))

/// Verifies every requirement and decision source digest the handoff declares
/// against the bytes supplied by the host (path -> "sha256:<hex>"). A source
/// the handoff names but the host could not supply is refused, not skipped.
let verifySources (admitted: AdmittedHandoff) (actual: Map<string, string>) : TranslationError list =
    let h = handoffOf admitted
    (h.Requirements |> List.map _.Source) @ (h.Decisions |> List.map _.Source)
    |> List.choose (fun s -> s.Sha256 |> Option.map (fun d -> s.Path, d))
    |> List.distinct
    |> List.choose (fun (path, declared) ->
        match Map.tryFind path actual with
        | None -> Some(SourceUnavailable path)
        | Some a when a <> declared -> Some(SourceDigestMismatch(path, declared, a))
        | Some _ -> None)

// ---------------------------------------------------------------------------
// ECIR: manifest and blueprint.
// ---------------------------------------------------------------------------

let requirementKey (r: Requirement) = r.Source.Path + "#" + r.Id

let private sourceRequirement (r: Requirement) =
    match r.Source.Sha256 with
    | None -> Error(MissingSourceDigest r.Id)
    | Some digest ->
        Ok { Key = requirementKey r; OriginalId = r.Id; Document = r.Source.Path
             Location = r.Source.Locator |> Option.defaultValue r.Id; Revision = r.Source.Revision
             ContentDigest = digest }

type Ecir =
    { Manifest: SourceManifest
      Blueprint: Blueprint
      /// Decision node IDs that are accepted and human-authorized in the handoff.
      Authorized: Set<string>
      KeyOf: string -> string }

let ecir (admitted: AdmittedHandoff) : Result<Ecir, TranslationError list> =
    let h = handoffOf admitted
    let accepted = h.Requirements |> List.filter (fun r -> r.Status = RequirementStatus.Accepted) |> List.sortBy _.Id
    match accepted |> List.map sourceRequirement |> collect with
    | Error e -> Error e
    | Ok sources ->
        let keyOpt id = accepted |> List.tryFind (fun r -> r.Id = id) |> Option.map requirementKey
        let keyOf id = keyOpt id |> Option.defaultWith (fun () -> invalidOp ("not an accepted requirement: " + id))
        let workflowKeys = [ keyOf LifecycleRequirement; keyOf PaymentRequirement ]
        let persistenceKeys = [ keyOf PersistenceRequirement ]
        // Only accepted decisions become decision nodes. Superseded, proposed
        // and rejected decisions are not authority and are not constructed.
        let decisions =
            h.Decisions
            |> List.filter (fun d -> d.Status = DecisionStatus.Accepted)
            |> List.map (fun d ->
                { Id = d.Id; Kind = Decision
                  RequirementKeys = d.RequirementIds |> List.choose keyOpt
                  DependsOn = []; Justification = Some d.Statement })
        // An unresolved question is a deferral node on the requirements it
        // affects: it is in scope, citable by the plan, and grants nothing.
        let questions =
            h.PolicyQuestions
            |> List.map (fun q ->
                { Id = q.Id; Kind = Deferral
                  RequirementKeys = q.Affects |> List.choose keyOpt
                  DependsOn = []; Justification = Some q.Question })
        let structural =
            [ { Id = ContractNode; Kind = Contract; RequirementKeys = workflowKeys; DependsOn = []; Justification = Some "invoice workflow" }
              { Id = PersistenceContractNode; Kind = Contract; RequirementKeys = persistenceKeys; DependsOn = []; Justification = Some "payment persistence boundary" }
              { Id = VerificationNode; Kind = VerificationObligation; RequirementKeys = workflowKeys; DependsOn = []; Justification = Some "independent verification (Dokimos)" }
              { Id = PersistenceVerificationNode; Kind = VerificationObligation; RequirementKeys = persistenceKeys; DependsOn = []; Justification = Some "compile-negative probes and independent verification" }
              { Id = WorkflowCohort; Kind = Cohort; RequirementKeys = workflowKeys; DependsOn = []; Justification = Some "generated by Ordo.Construction" }
              { Id = PersistenceCohort; Kind = Cohort; RequirementKeys = persistenceKeys; DependsOn = []; Justification = Some "handwritten extension; outside the Ordo vocabulary" } ]
        let nodes = structural @ decisions @ questions |> List.sortBy _.Id
        let records =
            sources
            |> List.map (fun s ->
                { Source = s; Disposition = Modeled
                  NodeIds = nodes |> List.filter (fun n -> List.contains s.Key n.RequirementKeys) |> List.map _.Id })
        let manifest = { Digest = manifestDigest sources; Requirements = sources }
        let blueprint = { SchemaVersion = Ordo.Core.ConstructionIr.SchemaVersion; SourceManifestDigest = manifest.Digest; Requirements = records; Nodes = nodes }
        let authorized =
            h.Decisions
            |> List.filter (fun d -> d.Status = DecisionStatus.Accepted && d.AuthorizedBy |> Option.exists (fun a -> a.Kind = ActorKind.Human))
            |> List.map _.Id
            |> Set.ofList
        Ok { Manifest = manifest; Blueprint = blueprint; Authorized = authorized; KeyOf = keyOf }

// ---------------------------------------------------------------------------
// The plan.
// ---------------------------------------------------------------------------

let plan (admitted: AdmittedHandoff) (authority: SliceAuthority) (ir: Ecir) (prepared: PreparedCohort) : PlanDraft =
    let view = preparedCohortView prepared
    let trace (requirements: string list) (nodes: string list) : TraceDraft =
        { RequirementKeys = requirements |> List.map ir.KeyOf; NodeIds = nodes }
    let lifecycle = trace [ LifecycleRequirement ] [ ContractNode ]
    let lifecycleAndPayment = trace [ LifecycleRequirement; PaymentRequirement ] [ ContractNode ]
    let state name role t = DefineState { Name = name; Role = role; Trace = t }
    let transition command from target = DefineTransition { Command = command; From = from; To = target; Guards = []; Trace = lifecycle }
    let branching from =
        DefineBranchingTransition
            { Pair = { InState = from; OnCommand = "RecordPayment" }; Targets = [ "PartiallyPaid"; "Paid" ]; Trace = lifecycleAndPayment }
    let selection from =
        DeclareExtensionInventory
            { Extension = ExtensionRef.Selection(from, "RecordPayment")
              DecisionPoints =
                [ { Point = "balance reaches zero"; Resolution = DecisionPointResolution.Decided; Trace = trace [ PaymentRequirement ] [ ContractNode ] } ]
              Trace = trace [ PaymentRequirement ] [ ContractNode ] }
    let terminalCodes =
        [ for s in [ "Paid"; "Voided" ] do
            for c in [ "Issue"; "RecordPayment"; "Void" ] ->
                AssignRefusalCode { Target = CodeTarget.Pair { InState = s; OnCommand = c }; Code = "invoice closed"; Trace = lifecycle } ]
    let operations =
        [ DefineWorkflow { Name = WorkflowName; Trace = lifecycle }
          state "Draft" Initial lifecycle
          state "Issued" Ordinary lifecycle
          state "PartiallyPaid" Ordinary lifecycleAndPayment
          state "Paid" Terminal lifecycleAndPayment
          state "Voided" Terminal lifecycle
          transition "Issue" "Draft" "Issued"
          transition "Void" "Draft" "Voided"
          transition "Void" "Issued" "Voided"
          branching "Issued"
          branching "PartiallyPaid"
          selection "Issued"
          selection "PartiallyPaid"
          DeclarePairRule { Unlisted = UnlistedPairs.Refused; Trace = lifecycle }
          DeclareUndecidedTransition
              { Pair = { InState = "PartiallyPaid"; OnCommand = "Void" }
                Question = authority.VoidQuestion.Question
                Trace = trace [ LifecycleRequirement ] [ VoidQuestion ] } ]
        @ (codes authority |> List.map (fun c -> DefineRefusalCode { Code = c.Name; Trace = trace c.Requirements c.Nodes }))
        @ terminalCodes
        @ [ AssignRefusalCode { Target = CodeTarget.Unlisted; Code = "command not allowed in state"; Trace = lifecycle } ]
    { SchemaVersion = PlanSchemaVersion
      VocabularyVersion = VocabularyVersion4
      Binding =
        { SourceManifestDigest = view.Manifest.Digest
          BlueprintDigest = blueprintDigest view.Blueprint
          CohortId = WorkflowCohort
          DecisionIds = view.RequiredDecisionIds |> Set.toList }
      Operations = operations }

/// Prepares the workflow cohort with only the authorized decisions that are in
/// its scope, then validates the plan with Ordo's own validator.
let validatedPlan (admitted: AdmittedHandoff) : Result<SliceAuthority * Ecir * PreparedCohort * ValidatedPlan, TranslationError list> =
    match sliceAuthority admitted, ecir admitted with
    | Error a, Error b -> Error(a @ b)
    | Error a, Ok _ | Ok _, Error a -> Error a
    | Ok authority, Ok ir ->
        let scope = requiredScope ir.Manifest ir.Blueprint WorkflowCohort |> Option.defaultValue Set.empty
        let authorizedInScope = Set.intersect ir.Authorized scope
        match prepareCohort ir.Manifest ir.Blueprint WorkflowCohort authorizedInScope with
        | Error e -> Error [ CohortNotReady(e |> List.map (sprintf "%A")) ]
        | Ok prepared ->
            match validate prepared (plan admitted authority ir prepared) with
            | Error e -> Error [ PlanRejected(e |> List.map (sprintf "%A")) ]
            | Ok validated -> Ok(authority, ir, prepared, validated)
