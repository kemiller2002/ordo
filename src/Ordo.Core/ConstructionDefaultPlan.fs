/// Mechanical-first construction (CONSTRUCT-008): Ordo generates the default
/// construction plan for a prepared cohort from approved inputs only, the
/// prepared cohort (manifest, blueprint, cohort), the approved construction
/// decision records and the vocabulary. No agent authors the plan and no
/// caller supplies its binding.
///
/// The generator never invents behavior to make a plan complete. Every
/// operation it emits is justified by an approved decision record whose
/// content cites requirements its node owns. What the inputs leave open
/// becomes a typed obligation (`ordo.construction-obligations/1`); a missing
/// essential decision or a record that contradicts the blueprint is a
/// refusal, never a partial plan. The generated draft must pass
/// `ConstructionPlan.validate` or the generator refuses with its violations.
///
/// A default plan is semantic validity plus a work list, not authority to
/// apply anything. See docs/architecture/construction-default-plan.md.
module Ordo.Core.ConstructionDefaultPlan

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.StrictJson
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionDecisions

[<Literal>]
let GeneratorId = "ordo.default-plan"

/// The generator's own semantic version. Same inputs and same generator
/// version give a byte-identical default plan.
[<Literal>]
let GeneratorVersion = "ordo.default-plan/3"

[<Literal>]
let ObligationsSchemaVersion = "ordo.construction-obligations/1"

[<Literal>]
let DefaultPlanSchemaVersion = "ordo.default-plan/1"

// ---------------------------------------------------------------------------
// Types.
// ---------------------------------------------------------------------------

/// The plan vocabulary to generate in. Vocabulary 2 can state an effect's
/// failure disposition; vocabulary 1 cannot, so a decided disposition under
/// vocabulary 1 becomes an `unsupportedVocabulary` obligation.
[<RequireQualifiedAccess>]
type ConstructionVocabulary =
    | Version1
    | Version2
    /// Classified choices (CONSTRUCT-010). Generated only from
    /// `ordo.construction-decisions/2` records, which state every pair rule
    /// and every effect's decision points.
    | Version3

[<RequireQualifiedAccess>]
type TypedObligationKind =
    /// A requirement is realized only through a generated extension surface
    /// (a port); its behavior is handwritten.
    | ExtensionBehavior
    /// A transition effect's failure disposition is not decided.
    | UndecidedEffectFailure
    /// A blueprint conflict inside the cohort, or outside it when the blueprint relates it to
    /// it. Not resolved by the plan.
    | Conflict
    /// An assigned requirement that no approved record constructs.
    | UncoveredRequirement
    /// Approved content the selected vocabulary cannot express.
    | UnsupportedVocabulary
    /// Vocabulary 3: the records leave what a command does in a state open.
    /// Generated code reports the pair as undecided.
    | UndecidedTransition
    /// Vocabulary 3: a decision point inside an effect's behavior is open.
    /// The effect cannot be implemented until it is decided, so no
    /// `extensionBehavior` (implement) obligation is issued for it.
    | UndecidedExtensionDecision

type TypedObligation =
    { /// `OBL-<cohort>-<kind>-<12 hex>`, from the obligation's content.
      Id: string
      Kind: TypedObligationKind
      RequirementKeys: string list
      NodeIds: string list
      /// A precise statement or question for a person or agent.
      Question: string
      /// Generated extension contracts or ports this obligation touches, as
      /// named by `ConstructionLayout`.
      ExtensionSurface: string list
      /// The `declareUnresolved` subject in the plan that carries this
      /// obligation, when the plan carries it.
      PlanSubject: string option }

/// `ordo.construction-obligations/1`.
type ObligationSet =
    { SchemaVersion: string
      CohortId: string
      PlanDigest: string
      Obligations: TypedObligation list }

type DefaultPlanProvenance =
    { GeneratorId: string
      GeneratorVersion: string
      VocabularyVersion: string
      CohortId: string
      SourceManifestDigest: string
      /// Canonical blueprint digest (`ConstructionIrWire.blueprintDigest`).
      BlueprintDigest: string
      /// Canonical decision record digest (`ConstructionDecisions.digest`).
      DecisionRecordsDigest: string
      PlanDigest: string
      ObligationsDigest: string
      /// SHA-256 of `encodeDefaultPlan`, which carries every field above.
      DefaultPlanDigest: string }

/// Why no default plan was generated. Each case names the record, node,
/// requirement or name at fault.
type DefaultPlanRefusal =
    | UnsupportedVocabularyVersion of actual: string
    | UnsupportedDecisionSchema of actual: string
    /// Vocabulary 3 needs `ordo.construction-decisions/2`; vocabularies 1 and
    /// 2 need `ordo.construction-decisions/1`. Nothing is dropped or invented
    /// to bridge them.
    | DecisionSchemaVocabularyMismatch of schema: string * vocabulary: string
    /// The records were approved against another blueprint.
    | DecisionBlueprintMismatch of bound: string * prepared: string
    | DecisionCohortMismatch of bound: string * prepared: string
    | DuplicateRecord of nodeId: string
    | UnknownRecordNode of nodeId: string
    /// The node exists but is not approved in this cohort's required scope.
    | RecordForUnapprovedNode of nodeId: string
    /// `stateModel` needs a decision node; `portContract` an interface or
    /// decision node.
    | RecordKindNotAllowed of nodeId: string * nodeKind: string * contentKind: string
    /// A decision the cohort requires has no decision record. Fail closed.
    | MissingDecisionContent of decisionId: string
    | MissingStateModel
    | MultipleStateModels of nodeIds: string list
    | InvalidRecordName of nodeId: string * raw: string * reason: NameError
    | MissingRequirementCitation of nodeId: string * subject: string
    | RequirementNotOwnedByNode of nodeId: string * subject: string * requirementKey: string
    | RequirementOutsideCohort of nodeId: string * subject: string * requirementKey: string
    | DuplicateRecordedState of nodeId: string * state: string
    | MissingRecordedInitialState of nodeId: string
    | MultipleRecordedInitialStates of nodeId: string * states: string list
    /// A recorded state no recorded transition enters or leaves.
    | UnconnectedRecordedState of nodeId: string * state: string
    | TransitionUnknownState of nodeId: string * transition: string * state: string
    | DuplicateRecordedTransition of nodeId: string * transition: string
    | ConflictingRecordedTransitions of nodeId: string * from: string * command: string * targets: string list
    | DuplicateRecordedPort of port: string
    | EffectForUnknownCommand of nodeId: string * command: string
    /// A decided disposition (`remain`, `proceed`) recorded on a node that
    /// is not an approved decision.
    | DecidedDispositionWithoutDecision of nodeId: string * command: string
    /// Defense in depth: the generated draft did not validate.
    | GeneratedPlanInvalid of violations: PlanViolation list
    /// A conflict resolution names a node that is not in the blueprint.
    | ResolvesUnknownNode of nodeId: string * conflictId: string
    /// A conflict resolution names a node that is not a conflict.
    | ResolvesNonConflict of nodeId: string * targetId: string * targetKind: string
    /// A conflict resolution shares no requirement key with a conflict it
    /// names, so nothing ties the decision to that conflict.
    | UnrelatedConflictResolution of nodeId: string * conflictId: string

type private DefaultPlanView =
    { Draft: PlanDraft
      Plan: ValidatedPlan
      Obligations: ObligationSet
      GeneratedLayout: string list
      /// Conflicts settled by an authorized decision's `conflictResolution`
      /// record, with the deciding nodes. They get no conflict obligation.
      ResolvedConflicts: (string * string list) list
      Provenance: DefaultPlanProvenance
      Encoded: string }

/// Only `generateDefaultPlan` constructs one. Not authority to apply.
type DefaultPlan =
    private
    | DefaultPlan of DefaultPlanView

    member private this.View = let (DefaultPlan view) = this in view
    /// The ordinary `ordo.construction-plan/1` document in canonical form,
    /// with the binding Ordo computed from the prepared cohort.
    member this.Draft = this.View.Draft
    /// The draft as validated by `ConstructionPlan.validate`.
    member this.ValidatedPlan = this.View.Plan
    member this.PlanDigest = ConstructionPlan.digest this.View.Plan
    member this.Obligations = this.View.Obligations.Obligations
    member this.ObligationSet = this.View.Obligations
    /// The generated file paths construction will emit (`ConstructionLayout`).
    member this.GeneratedLayout = this.View.GeneratedLayout
    /// Conflicts an authorized decision settles, with the deciding nodes.
    member this.ResolvedConflicts = this.View.ResolvedConflicts
    member this.Provenance = this.View.Provenance
    member this.Digest = this.View.Provenance.DefaultPlanDigest

[<RequireQualifiedAccess>]
module ConstructionVocabulary =

    let version vocabulary =
        match vocabulary with
        | ConstructionVocabulary.Version1 -> VocabularyVersion1
        | ConstructionVocabulary.Version2 -> VocabularyVersion2
        | ConstructionVocabulary.Version3 -> VocabularyVersion3

    /// Anything but a supported vocabulary version is refused, never upgraded.
    let ofVersion (raw: string) : Result<ConstructionVocabulary, DefaultPlanRefusal> =
        match raw with
        | VocabularyVersion1 -> Ok ConstructionVocabulary.Version1
        | VocabularyVersion2 -> Ok ConstructionVocabulary.Version2
        | VocabularyVersion3 -> Ok ConstructionVocabulary.Version3
        | other -> Error(UnsupportedVocabularyVersion other)

// ---------------------------------------------------------------------------
// Encoding of obligations.
// ---------------------------------------------------------------------------

let obligationKindName kind =
    match kind with
    | TypedObligationKind.ExtensionBehavior -> "extensionBehavior"
    | TypedObligationKind.UndecidedEffectFailure -> "undecidedEffectFailure"
    | TypedObligationKind.Conflict -> "conflict"
    | TypedObligationKind.UncoveredRequirement -> "uncoveredRequirement"
    | TypedObligationKind.UnsupportedVocabulary -> "unsupportedVocabulary"
    | TypedObligationKind.UndecidedTransition -> "undecidedTransition"
    | TypedObligationKind.UndecidedExtensionDecision -> "undecidedExtensionDecision"

let private jStrings values = JArray(List.map JString values)

let private sha256 (value: string) =
    value |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
    |> fun hex -> "sha256:" + hex.ToLowerInvariant()

let private shortHash length (value: string) = (sha256 value).Substring("sha256:".Length, length)

let private obligationContent (o: TypedObligation) =
    [ "kind", JString(obligationKindName o.Kind)
      "requirementKeys", jStrings o.RequirementKeys
      "nodeIds", jStrings o.NodeIds
      "question", JString o.Question
      "extensionSurface", jStrings o.ExtensionSurface ]
    @ (o.PlanSubject |> Option.map (fun s -> "planSubject", JString s) |> Option.toList)

let private jObligation (o: TypedObligation) = JObject(("id", JString o.Id) :: obligationContent o)

let private obligationsJson (set: ObligationSet) =
    JObject [ "schemaVersion", JString set.SchemaVersion
              "cohortId", JString set.CohortId
              "planDigest", JString set.PlanDigest
              "obligations", JArray(set.Obligations |> List.map jObligation) ]

/// Canonical serialization of `ordo.construction-obligations/1`.
let encodeObligations (set: ObligationSet) = obligationsJson set |> renderCanonical

let private obligationKind path raw =
    match raw with
    | "extensionBehavior" -> Ok TypedObligationKind.ExtensionBehavior
    | "undecidedEffectFailure" -> Ok TypedObligationKind.UndecidedEffectFailure
    | "conflict" -> Ok TypedObligationKind.Conflict
    | "uncoveredRequirement" -> Ok TypedObligationKind.UncoveredRequirement
    | "unsupportedVocabulary" -> Ok TypedObligationKind.UnsupportedVocabulary
    | "undecidedTransition" -> Ok TypedObligationKind.UndecidedTransition
    | "undecidedExtensionDecision" -> Ok TypedObligationKind.UndecidedExtensionDecision
    | other -> fail path ("unsupported obligation kind: " + other)

let private readStrings path name p =
    result {
        let! raw = field path name p
        return! strings (path + "." + name) raw
    }

let private readObligation path value =
    result {
        let! p = fields path [ "id"; "kind"; "requirementKeys"; "nodeIds"; "question"; "extensionSurface" ] [ "planSubject" ] value
        let! id = textField path "id" p
        let! kindText = textField path "kind" p
        let! kind = obligationKind (path + ".kind") kindText
        let! keys = readStrings path "requirementKeys" p
        let! nodes = readStrings path "nodeIds" p
        let! question = textField path "question" p
        let! surface = readStrings path "extensionSurface" p
        let! subject = if Map.containsKey "planSubject" p then textField path "planSubject" p |> Result.map Some else Ok None
        return { Id = id; Kind = kind; RequirementKeys = keys; NodeIds = nodes; Question = question; ExtensionSurface = surface; PlanSubject = subject }
    }

/// Strict reader for `ordo.construction-obligations/1`. The result is a
/// record of what a document says, not proof that Ordo generated it; compare
/// its canonical encoding with a regenerated default plan's.
let readObligations (input: string) : Result<ObligationSet, string> =
    result {
        let! json = parse input |> Result.mapError (sprintf "invalid JSON: %A")
        let! p = fields "$" [ "schemaVersion"; "cohortId"; "planDigest"; "obligations" ] [] json
        let! schema = textField "$" "schemaVersion" p
        let! () = if schema = ObligationsSchemaVersion then Ok() else fail "$.schemaVersion" ("unsupported " + schema)
        let! cohort = textField "$" "cohortId" p
        let! planDigest = textField "$" "planDigest" p
        let! raw = field "$" "obligations" p
        let! obligations = items "$.obligations" readObligation raw
        return { SchemaVersion = schema; CohortId = cohort; PlanDigest = planDigest; Obligations = obligations }
    }

let private provenanceJson (p: DefaultPlanProvenance) =
    JObject [ "generatorId", JString p.GeneratorId
              "generatorVersion", JString p.GeneratorVersion
              "vocabularyVersion", JString p.VocabularyVersion
              "cohortId", JString p.CohortId
              "sourceManifestDigest", JString p.SourceManifestDigest
              "blueprintDigest", JString p.BlueprintDigest
              "decisionRecordsDigest", JString p.DecisionRecordsDigest
              "planDigest", JString p.PlanDigest
              "obligationsDigest", JString p.ObligationsDigest ]

let private defaultPlanJson draft obligations layout (resolved: (string * string list) list) provenance =
    JObject(
        [ "schemaVersion", JString DefaultPlanSchemaVersion
          "provenance", provenanceJson provenance
          "plan", ConstructionPlan.toJson draft
          "obligations", obligationsJson obligations
          "generatedLayout", jStrings layout ]
        // Present only when a record resolves a conflict, so plans without
        // one keep their exact bytes and digests.
        @ (match resolved with
           | [] -> []
           | _ ->
               [ "resolvedConflicts",
                 JArray [ for conflict, decisions in resolved ->
                            JObject [ "conflict", JString conflict; "decisionIds", jStrings decisions ] ] ]))

/// Canonical serialization of a default plan (`ordo.default-plan/1`):
/// provenance (without its own digest), plan, obligations and generated
/// layout. `Digest` is the SHA-256 of exactly these bytes.
let encodeDefaultPlan (plan: DefaultPlan) = let (DefaultPlan view) = plan in view.Encoded

// ---------------------------------------------------------------------------
// Checking the records against the prepared cohort.
// ---------------------------------------------------------------------------

let private kindText kind =
    match kind with
    | Decision -> "decision"
    | Contract -> "contract"
    | Invariant -> "invariant"
    | Interface -> "interface"
    | Cohort -> "cohort"
    | VerificationObligation -> "verificationObligation"
    | NodeKind.Conflict -> "conflict"
    | Deferral -> "deferral"
    | EngineeringRationale -> "engineeringRationale"

let private contentKind content =
    match content with
    | StateModel _ -> "stateModel"
    | PortContract _ -> "portContract"
    | ConflictResolution _ -> "conflictResolution"

let private nameKey raw = SemanticName.create raw |> Result.map SemanticName.key

let private envelopeRefusals (view: PreparedCohortView) (blueprintDigest: string) (vocabulary: ConstructionVocabulary) (records: ApprovedDecisionRecords) =
    let expected =
        match vocabulary with
        | ConstructionVocabulary.Version3 -> DecisionsSchemaVersion2
        | ConstructionVocabulary.Version1 | ConstructionVocabulary.Version2 -> DecisionsSchemaVersion
    [ if records.SchemaVersion <> DecisionsSchemaVersion && records.SchemaVersion <> DecisionsSchemaVersion2 then
          UnsupportedDecisionSchema records.SchemaVersion
      elif records.SchemaVersion <> expected then
          DecisionSchemaVocabularyMismatch(records.SchemaVersion, ConstructionVocabulary.version vocabulary)
      if records.BlueprintDigest <> blueprintDigest then DecisionBlueprintMismatch(records.BlueprintDigest, blueprintDigest)
      if records.CohortId <> view.CohortId then DecisionCohortMismatch(records.CohortId, view.CohortId) ]

let private nodeRefusals (view: PreparedCohortView) (records: ApprovedDecisionRecords) =
    let nodes = view.Blueprint.Nodes |> List.map (fun n -> n.Id, n) |> Map.ofList
    // A node holds at most one structural record and at most one conflict
    // resolution, so a decision can both fix structure and settle conflicts.
    let isResolution r = match r.Content with ConflictResolution _ -> true | StateModel _ | PortContract _ -> false
    let duplicates =
        records.Records |> List.countBy (fun r -> r.NodeId, isResolution r) |> List.filter (fun (_, n) -> n > 1)
        |> List.map (fst >> fst >> DuplicateRecord)
    let perRecord =
        records.Records
        |> List.distinctBy (fun r -> r.NodeId, isResolution r)
        |> List.choose (fun r ->
            match Map.tryFind r.NodeId nodes with
            | None -> Some(UnknownRecordNode r.NodeId)
            | Some node ->
                let approvedDecision = node.Kind = Decision && Set.contains node.Id view.RequiredDecisionIds
                match r.Content, node.Kind with
                | StateModel _, Decision
                | PortContract _, Decision
                | ConflictResolution _, Decision -> if approvedDecision then None else Some(RecordForUnapprovedNode r.NodeId)
                | PortContract _, Interface -> if Set.contains node.Id view.RequiredNodeIds then None else Some(RecordForUnapprovedNode r.NodeId)
                | content, kind -> Some(RecordKindNotAllowed(r.NodeId, kindText kind, contentKind content)))
    let recorded = records.Records |> List.map _.NodeId |> Set.ofList
    let missing = Set.difference view.RequiredDecisionIds recorded |> Seq.map MissingDecisionContent |> Seq.toList
    let models = records.Records |> List.filter (fun r -> match r.Content with StateModel _ -> true | PortContract _ | ConflictResolution _ -> false) |> List.map _.NodeId |> List.distinct |> List.sort
    let modelCount =
        match models with
        | [] -> [ MissingStateModel ]
        | [ _ ] -> []
        | many -> [ MultipleStateModels many ]
    duplicates @ perRecord @ missing @ modelCount

let private recordNames (record: DecisionRecord) =
    match record.Content with
    | StateModel m ->
        m.Workflow :: (m.States |> List.map _.Name) @ (m.Transitions |> List.collect (fun t -> [ t.Command; t.From; t.To ]))
        @ (m.Refusals |> List.collect (fun r -> [ r.Command; r.From ]))
        @ (m.Undecided |> List.collect (fun u -> [ u.Command; u.From ]))
    | PortContract ports ->
        ports
        |> List.collect (function
            | RecordedTransitionEffect(command, _, _, points) ->
                command :: (points |> Option.defaultValue [] |> List.map _.Point)
            | RecordedCurrentTime _ -> [])
    | ConflictResolution _ -> []

let private nameRefusals (records: ApprovedDecisionRecords) =
    records.Records
    |> List.collect (fun r ->
        recordNames r
        |> List.choose (fun raw ->
            match SemanticName.create raw with
            | Error reason -> Some(InvalidRecordName(r.NodeId, raw, reason))
            | Ok _ -> None))

/// Valid names only; called after nameRefusals is empty.
let private key raw =
    match nameKey raw with
    | Ok value -> value
    | Error reason -> invalidOp (sprintf "unchecked name %s: %A" raw reason)

let private transitionLabel (t: RecordedTransition) = sprintf "transition:%s/%s" (key t.From) (key t.Command)

let private portLabel port =
    match port with
    | RecordedCurrentTime _ -> "port:currentTime"
    | RecordedTransitionEffect(command, _, _, _) -> "port:transitionEffect/" + key command

let private citations (record: DecisionRecord) =
    match record.Content with
    | StateModel m ->
        (m.Transitions |> List.map (fun t -> transitionLabel t, t.RequirementKeys))
        @ (m.Refusals |> List.map (fun r -> sprintf "refusal:%s/%s" (key r.From) (key r.Command), r.RequirementKeys))
        @ (m.Undecided |> List.map (fun u -> sprintf "undecided:%s/%s" (key u.From) (key u.Command), u.RequirementKeys))
        @ (match m.Unlisted with
           | Some(RecordedUnlisted.Refused keys) -> [ "pairRule", keys ]
           | Some RecordedUnlisted.Undecided | None -> [])
    | PortContract ports ->
        (ports |> List.map (fun p ->
            portLabel p, (match p with RecordedCurrentTime keys | RecordedTransitionEffect(_, _, keys, _) -> keys)))
        @ (ports
           |> List.collect (function
               | RecordedTransitionEffect(_, _, _, Some points) as p ->
                   points |> List.map (fun point -> sprintf "%s/point:%s" (portLabel p) point.Point, point.RequirementKeys)
               | _ -> []))
    | ConflictResolution c -> [ "resolves:" + String.concat "," (c.Conflicts |> List.distinct |> List.sort), c.RequirementKeys ]

let private citationRefusals (view: PreparedCohortView) (records: ApprovedDecisionRecords) =
    let nodes = view.Blueprint.Nodes |> List.map (fun n -> n.Id, n) |> Map.ofList
    records.Records
    |> List.collect (fun r ->
        let owned = nodes[r.NodeId].RequirementKeys |> Set.ofList
        citations r
        |> List.collect (fun (subject, keys) ->
            [ if List.isEmpty keys then MissingRequirementCitation(r.NodeId, subject)
              for k in List.distinct keys do
                  if not (Set.contains k owned) then RequirementNotOwnedByNode(r.NodeId, subject, k)
                  elif not (Set.contains k view.AssignedRequirementKeys) then RequirementOutsideCohort(r.NodeId, subject, k) ]))

let private stateModelRefusals (nodeId: string) (m: StateModelContent) =
    let stateKeys = m.States |> List.map (fun s -> key s.Name)
    let duplicateStates =
        stateKeys |> List.countBy id |> List.filter (fun (_, n) -> n > 1) |> List.map (fun (s, _) -> DuplicateRecordedState(nodeId, s))
    let initials = m.States |> List.filter (fun s -> s.Role = Initial) |> List.map (fun s -> key s.Name) |> List.sort
    let initialCount =
        match initials with
        | [] -> [ MissingRecordedInitialState nodeId ]
        | [ _ ] -> []
        | many -> [ MultipleRecordedInitialStates(nodeId, many) ]
    let known = Set.ofList stateKeys
    let unknown =
        m.Transitions
        |> List.collect (fun t ->
            [ for raw in [ t.From; t.To ] do
                  if not (Set.contains (key raw) known) then TransitionUnknownState(nodeId, transitionLabel t, key raw) ])
    let edges =
        m.Transitions
        |> List.groupBy (fun t -> key t.From, key t.Command)
        |> List.collect (fun ((from, command), group) ->
            match group |> List.map (fun t -> key t.To) |> List.distinct |> List.sort with
            | [ _ ] when List.length group > 1 -> [ DuplicateRecordedTransition(nodeId, sprintf "transition:%s/%s" from command) ]
            | [ _ ] -> []
            | targets -> [ ConflictingRecordedTransitions(nodeId, from, command, targets) ])
    let touched = m.Transitions |> List.collect (fun t -> [ key t.From; key t.To ]) |> Set.ofList
    let unconnected =
        stateKeys |> List.distinct |> List.filter (fun s -> not (Set.contains s touched)) |> List.map (fun s -> UnconnectedRecordedState(nodeId, s))
    duplicateStates @ initialCount @ unknown @ edges @ unconnected

let private portRefusals (view: PreparedCohortView) (commands: Set<string>) (records: ApprovedDecisionRecords) =
    let ports =
        records.Records
        |> List.collect (fun r -> match r.Content with PortContract ports -> ports |> List.map (fun p -> r.NodeId, p) | StateModel _ | ConflictResolution _ -> [])
    let duplicates =
        ports |> List.countBy (snd >> portLabel) |> List.filter (fun (_, n) -> n > 1) |> List.map (fst >> DuplicateRecordedPort)
    let perPort =
        ports
        |> List.collect (fun (nodeId, port) ->
            match port with
            | RecordedCurrentTime _ -> []
            | RecordedTransitionEffect(command, disposition, _, _) ->
                [ if not (Set.contains (key command) commands) then EffectForUnknownCommand(nodeId, key command)
                  match disposition with
                  | Some(EffectFailureDisposition.Remain | EffectFailureDisposition.Proceed)
                      when not (Set.contains nodeId view.RequiredDecisionIds) ->
                      DecidedDispositionWithoutDecision(nodeId, key command)
                  | _ -> () ])
    duplicates @ perPort

// ---------------------------------------------------------------------------
// Generation.
// ---------------------------------------------------------------------------

let private trace keys nodeIds : TraceDraft =
    { RequirementKeys = keys |> List.distinct |> List.sort; NodeIds = nodeIds |> List.distinct |> List.sort }

let private obligation cohortId kind keys nodeIds question surface subject =
    let draft =
        { Id = ""
          Kind = kind
          RequirementKeys = keys |> List.distinct |> List.sort
          NodeIds = nodeIds |> List.distinct |> List.sort
          Question = question
          ExtensionSurface = surface |> List.distinct |> List.sort
          PlanSubject = subject }
    let content = JObject(obligationContent draft) |> renderCanonical
    { draft with Id = sprintf "OBL-%s-%s-%s" cohortId (obligationKindName kind) (shortHash 12 content) }

let private effectSubject (command: string) =
    let raw = command + " effect failure"
    if raw.Length <= MaxNameLength then raw else "Effect failure " + shortHash 8 command

let private uncoveredSubject (requirementKey: string) = "Uncovered requirement " + shortHash 8 requirementKey

let private isStructural operation =
    match operation with
    | DefineState _ | DefineTransition _ | DefineInvariant _ | BindCapability _ | DefineRefusal _
    | DefineBranchingTransition _ -> true
    | DefineWorkflow _ | DeclarePort _ | DeclareUnresolved _ | DeclarePairRule _
    | DeclareUndecidedTransition _ | DeclareExtensionInventory _ | DefineRefusalCode _ | AssignRefusalCode _ -> false

/// The blueprint's relation between a conflict node outside the cohort and
/// the cohort, as evidence strings. Empty when the blueprint relates them in
/// no way this generator recognizes.
let private conflictRelation (view: PreparedCohortView) (conflict: ConstructionNode) =
    let documentOf =
        view.Manifest.Requirements |> List.map (fun r -> r.Key, r.Document) |> Map.ofList
    let cohortDocuments = view.AssignedRequirementKeys |> Seq.choose (fun k -> Map.tryFind k documentOf) |> Set.ofSeq
    let required = view.Blueprint.Nodes |> List.filter (fun n -> Set.contains n.Id view.RequiredNodeIds)
    [ for k in conflict.RequirementKeys |> List.distinct |> List.sort do
          if Set.contains k view.RequiredRequirementKeys then sprintf "shared requirement %s" k
      for dependency in conflict.DependsOn |> List.distinct |> List.sort do
          if Set.contains dependency view.RequiredNodeIds then sprintf "dependency %s -> %s" conflict.Id dependency
      for node in required do
          if List.contains conflict.Id node.DependsOn then sprintf "dependency %s -> %s" node.Id conflict.Id
      for document in conflict.RequirementKeys |> List.choose (fun k -> Map.tryFind k documentOf) |> List.distinct |> List.sort do
          if Set.contains document cohortDocuments then sprintf "shared source document %s" document ]
    |> List.distinct

/// Each conflict a resolution names must be a blueprint conflict that shares
/// a requirement key with the resolution.
let private resolutionRefusals (view: PreparedCohortView) (records: ApprovedDecisionRecords) =
    let nodes = view.Blueprint.Nodes |> List.map (fun n -> n.Id, n) |> Map.ofList
    records.Records
    |> List.collect (fun r ->
        match r.Content with
        | ConflictResolution c ->
            c.Conflicts
            |> List.distinct
            |> List.choose (fun id ->
                match Map.tryFind id nodes with
                | None -> Some(ResolvesUnknownNode(r.NodeId, id))
                | Some node when node.Kind <> NodeKind.Conflict -> Some(ResolvesNonConflict(r.NodeId, id, kindText node.Kind))
                | Some node when Set.isEmpty (Set.intersect (Set.ofList node.RequirementKeys) (Set.ofList c.RequirementKeys)) ->
                    Some(UnrelatedConflictResolution(r.NodeId, id))
                | Some _ -> None)
        | StateModel _ | PortContract _ -> [])

/// Conflicts settled by checked resolution records. Every record that reaches
/// this point is on an authorized decision of the cohort.
let private resolvedConflicts (records: ApprovedDecisionRecords) =
    records.Records
    |> List.collect (fun r ->
        match r.Content with
        | ConflictResolution c -> c.Conflicts |> List.map (fun id -> id, r.NodeId)
        | StateModel _ | PortContract _ -> [])
    |> List.groupBy fst
    |> List.map (fun (id, pairs) -> id, pairs |> List.map snd |> List.distinct |> List.sort)
    |> List.sortBy fst

let private conflictObligations (view: PreparedCohortView) (resolved: Set<string>) =
    let reasons =
        view.Blueprint.Requirements
        |> List.choose (fun r ->
            match r.Disposition with
            | Unresolved reason -> Some(r.Source.Key, reason)
            | _ -> None)
        |> Map.ofList
    /// A conflict inside the cohort's required scope is always the cohort's
    /// open question; one outside it only when the blueprint relates it.
    let placement (conflict: ConstructionNode) =
        if Set.contains conflict.Id view.RequiredNodeIds then
            let recorded =
                match conflict.Justification with
                | Some text when not (String.IsNullOrWhiteSpace text) -> sprintf " The blueprint records: %s." (text.Trim().TrimEnd('.'))
                | _ -> ""
            Some(sprintf "Conflict %s is inside cohort %s and no approved decision resolves it.%s" conflict.Id view.CohortId recorded)
        else
            match conflictRelation view conflict with
            | [] -> None
            | basis ->
                Some(sprintf "Conflict %s is outside cohort %s and this plan does not resolve it. The blueprint relates it to the cohort by %s."
                        conflict.Id view.CohortId (String.concat "; " basis))
    view.Blueprint.Nodes
    |> List.filter (fun n -> n.Kind = NodeKind.Conflict && not (Set.contains n.Id resolved))
    |> List.sortBy _.Id
    |> List.choose (fun conflict ->
        placement conflict
        |> Option.map (fun statement ->
            let recorded =
                conflict.RequirementKeys |> List.distinct |> List.sort
                |> List.choose (fun k -> Map.tryFind k reasons |> Option.map (sprintf "%s: %s" k))
            let question =
                sprintf "%s%s Which outcome is authoritative for the cohort's behavior?"
                    statement
                    (match recorded with [] -> "" | reasons -> " Recorded as unresolved: " + String.concat "; " reasons + ".")
            obligation view.CohortId TypedObligationKind.Conflict conflict.RequirementKeys [ conflict.Id ] question [] None))

/// Builds the plan and obligations from records already checked against
/// the cohort; refuses only when the generated draft does not validate.
let private generate
    (prepared: PreparedCohort)
    (view: PreparedCohortView)
    (blueprintDigest: string)
    (records: ApprovedDecisionRecords)
    (vocabulary: ConstructionVocabulary)
    (modelNode: string)
    (model: StateModelContent)
    : Result<DefaultPlan, DefaultPlanRefusal list> =
    let vocabularyVersion = ConstructionVocabulary.version vocabulary
    let resolved = resolvedConflicts records
    let workflowKey = key model.Workflow
    let workflowName = match SemanticName.create model.Workflow with Ok n -> n | Error e -> invalidOp (sprintf "%A" e)
    let touching stateKey =
        model.Transitions
        |> List.filter (fun t -> key t.From = stateKey || key t.To = stateKey)
        |> List.collect _.RequirementKeys
    let structure =
        [ yield DefineWorkflow { Name = model.Workflow; Trace = trace (model.Transitions |> List.collect _.RequirementKeys) [ modelNode ] }
          for s in model.States ->
              DefineState { Name = s.Name; Role = s.Role; Trace = trace (touching (key s.Name)) [ modelNode ] }
          for t in model.Transitions ->
              DefineTransition { Command = t.Command; From = t.From; To = t.To; Guards = []; Trace = trace t.RequirementKeys [ modelNode ] } ]

    let ports =
        records.Records
        |> List.sortBy _.NodeId
        |> List.collect (fun r -> match r.Content with PortContract ports -> ports |> List.map (fun p -> r.NodeId, p) | StateModel _ | ConflictResolution _ -> [])
    let surfaceOf port =
        match port with
        | RecordedCurrentTime _ -> ConstructionLayout.currentTimeContract workflowName
        | RecordedTransitionEffect(command, _, _, _) ->
            ConstructionLayout.effectContract workflowName (match SemanticName.create command with Ok n -> n | Error e -> invalidOp (sprintf "%A" e))
    let portOperations =
        ports
        |> List.map (fun (nodeId, port) ->
            match port with
            | RecordedCurrentTime keys -> DeclarePort { Kind = CurrentTime; Trace = trace keys [ nodeId ] }
            | RecordedTransitionEffect(command, disposition, keys, _) ->
                let onFailure =
                    match vocabulary with
                    | ConstructionVocabulary.Version1 -> None
                    | ConstructionVocabulary.Version2 | ConstructionVocabulary.Version3 ->
                        Some(disposition |> Option.defaultValue EffectFailureDisposition.Unresolved)
                DeclarePort { Kind = TransitionEffect(command, onFailure); Trace = trace keys [ nodeId ] })

    // Effect failure: never decided by the generator.
    let effectItems =
        ports
        |> List.choose (fun (nodeId, port) ->
            match port with
            | RecordedCurrentTime _ -> None
            | RecordedTransitionEffect(command, disposition, keys, _) ->
                let commandKey = key command
                match disposition, vocabulary with
                | (None | Some EffectFailureDisposition.Unresolved), _ ->
                    let subject = effectSubject commandKey
                    let question =
                        sprintf "Decision record %s leaves the failure disposition of the %s effect undecided. When the %s effect fails or its outcome is unknown, does workflow %s remain in the transition's source state or proceed to its target state?"
                            nodeId commandKey commandKey workflowKey
                    let effect =
                        match vocabulary with
                        | ConstructionVocabulary.Version2 | ConstructionVocabulary.Version3 -> Some command
                        | ConstructionVocabulary.Version1 -> None
                    Some(Some(DeclareUnresolved { Subject = subject; Effect = effect; Question = question; Trace = trace keys [ nodeId ] }),
                         obligation view.CohortId TypedObligationKind.UndecidedEffectFailure keys [ nodeId ] question [ surfaceOf port ] (Some subject))
                | Some decided, ConstructionVocabulary.Version1 ->
                    let question =
                        sprintf "Decision record %s decides that a failed %s effect means '%s', but %s cannot express an effect-failure disposition, so generated code does not apply it. Generate with %s, or implement the approved disposition in host code."
                            nodeId commandKey (effectFailureDispositionName decided) VocabularyVersion1 VocabularyVersion2
                    Some(None, obligation view.CohortId TypedObligationKind.UnsupportedVocabulary keys [ nodeId ] question [ surfaceOf port ] None)
                | Some _, (ConstructionVocabulary.Version2 | ConstructionVocabulary.Version3) -> None)

    // Vocabulary 3: every choice the records make or leave open is stated.
    let pointKeys (port: RecordedPort) =
        match port with
        | RecordedTransitionEffect(_, _, keys, _) -> keys
        | RecordedCurrentTime keys -> keys
    let openPoints (port: RecordedPort) =
        match port with
        | RecordedTransitionEffect(_, _, _, Some points) -> points |> List.filter _.Question.IsSome
        | RecordedTransitionEffect(_, _, _, None) | RecordedCurrentTime _ -> []
    let classificationOperations, classificationItems =
        match vocabulary with
        | ConstructionVocabulary.Version1 | ConstructionVocabulary.Version2 -> [], []
        | ConstructionVocabulary.Version3 ->
            let modelKeys = model.Transitions |> List.collect _.RequirementKeys
            let rule =
                match model.Unlisted with
                | Some(RecordedUnlisted.Refused keys) -> DeclarePairRule { Unlisted = UnlistedPairs.Refused; Trace = trace keys [ modelNode ] }
                | Some RecordedUnlisted.Undecided | None -> DeclarePairRule { Unlisted = UnlistedPairs.Undecided; Trace = trace modelKeys [ modelNode ] }
            let refusals =
                model.Refusals
                |> List.map (fun r -> DefineRefusal { Pair = { InState = r.From; OnCommand = r.Command }; Trace = trace r.RequirementKeys [ modelNode ] })
            let undecided =
                model.Undecided
                |> List.map (fun u ->
                    DeclareUndecidedTransition { Pair = { InState = u.From; OnCommand = u.Command }; Question = u.Question; Trace = trace u.RequirementKeys [ modelNode ] })
            let inventories =
                ports
                |> List.choose (fun (nodeId, port) ->
                    match port with
                    | RecordedTransitionEffect(command, _, keys, points) ->
                        Some(DeclareExtensionInventory
                                { Extension = ExtensionRef.Effect command
                                  DecisionPoints =
                                    points
                                    |> Option.defaultValue []
                                    |> List.map (fun p ->
                                        { DecisionPointDraft.Point = p.Point
                                          Resolution =
                                            match p.Question with
                                            | None -> DecisionPointResolution.Decided
                                            | Some q -> DecisionPointResolution.Undecided q
                                          Trace = trace p.RequirementKeys [ nodeId ] })
                                  Trace = trace keys [ nodeId ] })
                    | RecordedCurrentTime _ -> None)
            // Undecided pairs: explicit ones, and every unlisted pair outside
            // a terminal state when the rule leaves unlisted pairs undecided.
            let stateKeys = model.States |> List.map (fun st -> key st.Name, st.Role)
            let commandKeys =
                (model.Transitions |> List.map _.Command) @ (model.Refusals |> List.map _.Command) @ (model.Undecided |> List.map _.Command)
                |> List.map key |> List.distinct |> List.sort
            let named from command =
                (model.Transitions |> List.exists (fun t -> key t.From = from && key t.Command = command))
                || (model.Refusals |> List.exists (fun r -> key r.From = from && key r.Command = command))
            let explicitPairs =
                model.Undecided |> List.map (fun u -> (key u.From, key u.Command), u.Question, u.RequirementKeys)
            let rulePairs =
                match model.Unlisted with
                | Some RecordedUnlisted.Undecided ->
                    [ for from, role in stateKeys do
                          for command in commandKeys do
                              let explicit = explicitPairs |> List.exists (fun ((f, c), _, _) -> f = from && c = command)
                              if role <> Terminal && not (named from command) && not explicit then
                                  (from, command),
                                  sprintf "Decision record %s leaves unlisted pairs undecided, so what command %s does in state %s of workflow %s is not decided."
                                      modelNode command from workflowKey,
                                  modelKeys ]
                | Some(RecordedUnlisted.Refused _) | None -> []
            let pairObligations =
                explicitPairs @ rulePairs
                |> List.map (fun ((from, command), question, keys) ->
                    None, obligation view.CohortId TypedObligationKind.UndecidedTransition keys [ modelNode ] question [] None)
            let pointObligations =
                ports
                |> List.collect (fun (nodeId, port) ->
                    openPoints port
                    |> List.map (fun p ->
                        let question =
                            sprintf "Decision record %s leaves decision point %s of the %s effect undecided: %s The effect's extension cannot be implemented until this is decided."
                                nodeId p.Point (match port with RecordedTransitionEffect(c, _, _, _) -> key c | RecordedCurrentTime _ -> "") (Option.defaultValue "" p.Question)
                        None,
                        obligation view.CohortId TypedObligationKind.UndecidedExtensionDecision (p.RequirementKeys @ pointKeys port) [ nodeId ] question [ surfaceOf port ] None))
            (rule :: refusals @ undecided @ inventories), pairObligations @ pointObligations

    // Requirement coverage over what the records construct.
    let constructed = structure @ classificationOperations @ portOperations
    let tracing k = constructed |> List.filter (fun o -> List.contains k (operationTrace o).RequirementKeys)
    let ownersInScope k =
        view.Blueprint.Nodes
        |> List.filter (fun n -> Set.contains n.Id view.RequiredNodeIds && n.Kind <> Cohort && List.contains k n.RequirementKeys)
    let verifiers k = ownersInScope k |> List.filter (fun n -> n.Kind = VerificationObligation) |> List.map _.Id
    let coverageItems =
        view.AssignedRequirementKeys
        |> Set.toList
        |> List.choose (fun k ->
            let ops = tracing k
            let viaPorts = ports |> List.filter (fun (_, p) -> match p with RecordedCurrentTime keys | RecordedTransitionEffect(_, _, keys, _) -> List.contains k keys)
            if ops |> List.exists isStructural then None
            // Vocabulary 3: a requirement reached through an effect with an
            // undecided point is covered by that point's decide obligation;
            // it is never handed off as "implement".
            elif viaPorts |> List.exists (snd >> openPoints >> List.isEmpty >> not) then None
            elif not (List.isEmpty viaPorts) then
                let surface = viaPorts |> List.map (snd >> surfaceOf) |> List.distinct |> List.sort
                let verifiedBy = verifiers k
                let question =
                    sprintf "Requirement %s is realized only through the extension surface %s. Its behavior is not generated: implement it in handwritten extension code against the requirement text.%s"
                        k (String.concat ", " surface)
                        (match verifiedBy with [] -> "" | ids -> " Independent verification: " + String.concat ", " ids + ".")
                Some(None, obligation view.CohortId TypedObligationKind.ExtensionBehavior [ k ] ((viaPorts |> List.map fst) @ verifiedBy) question surface None)
            else
                let subject = uncoveredSubject k
                let owners = ownersInScope k |> List.map _.Id
                let question =
                    sprintf "Requirement %s is assigned to cohort %s, but no approved decision record constructs it. Which approved decision or extension implements it?%s"
                        k view.CohortId
                        (match verifiers k with [] -> "" | ids -> " Independent verification: " + String.concat ", " ids + ".")
                Some(Some(DeclareUnresolved { Subject = subject; Effect = None; Question = question; Trace = trace [ k ] owners }),
                     obligation view.CohortId TypedObligationKind.UncoveredRequirement [ k ] owners question [] (Some subject)))

    let items = effectItems @ classificationItems @ coverageItems
    let draft: PlanDraft =
        { SchemaVersion = PlanSchemaVersion
          VocabularyVersion = vocabularyVersion
          Binding =
            { BindingDraft.SourceManifestDigest = view.Manifest.Digest
              BlueprintDigest = blueprintDigest
              CohortId = view.CohortId
              DecisionIds = Set.union view.RequiredDecisionIds view.RequiredRationaleIds |> Set.toList }
          Operations = constructed @ (items |> List.choose fst) }

    match ConstructionPlan.validate prepared draft with
    | Error violations -> Error [ GeneratedPlanInvalid violations ]
    | Ok validated ->
        let planView = ConstructionPlan.view validated
        let canonical: PlanDraft =
            { draft with
                Binding = { draft.Binding with DecisionIds = planView.Binding.DecisionIds }
                Operations = canonicalOperations validated }
        let obligations: ObligationSet =
            { SchemaVersion = ObligationsSchemaVersion
              CohortId = view.CohortId
              PlanDigest = planView.Digest
              Obligations = (items |> List.map snd) @ conflictObligations view (resolved |> List.map fst |> Set.ofList) |> List.sortBy _.Id }
        let layout = ConstructionLayout.generatedLayout planView.Workflow
        let provenance: DefaultPlanProvenance =
            { GeneratorId = GeneratorId
              GeneratorVersion = GeneratorVersion
              VocabularyVersion = vocabularyVersion
              CohortId = view.CohortId
              SourceManifestDigest = view.Manifest.Digest
              BlueprintDigest = blueprintDigest
              DecisionRecordsDigest = ConstructionDecisions.digest records
              PlanDigest = planView.Digest
              ObligationsDigest = sha256 (encodeObligations obligations)
              DefaultPlanDigest = "" }
        let encoded = defaultPlanJson canonical obligations layout resolved provenance |> renderCanonical
        Ok(DefaultPlan
            { Draft = canonical
              Plan = validated
              Obligations = obligations
              GeneratedLayout = layout
              ResolvedConflicts = resolved
              Provenance = { provenance with DefaultPlanDigest = sha256 encoded }
              Encoded = encoded })

/// Generates the default plan for a prepared cohort from approved decision
/// records only. Pure and deterministic: identical inputs give a
/// byte-identical `encodeDefaultPlan` and digest.
let generateDefaultPlan
    (prepared: PreparedCohort)
    (records: ApprovedDecisionRecords)
    (vocabulary: ConstructionVocabulary)
    : Result<DefaultPlan, DefaultPlanRefusal list> =

    let view = preparedCohortView prepared
    let blueprintDigest = ConstructionIrWire.blueprintDigest view.Blueprint
    let check refusals =
        match refusals with
        | [] -> Ok()
        | _ -> Error(refusals |> List.distinct |> List.sortBy (sprintf "%A"))
    result {
        let! () = check (envelopeRefusals view blueprintDigest vocabulary records @ nodeRefusals view records)
        let! () = check (nameRefusals records @ citationRefusals view records @ resolutionRefusals view records)
        let modelNode, model =
            records.Records |> List.pick (fun r -> match r.Content with StateModel m -> Some(r.NodeId, m) | PortContract _ | ConflictResolution _ -> None)
        let commands = model.Transitions |> List.map (fun t -> key t.Command) |> Set.ofList
        let! () = check (stateModelRefusals modelNode model @ portRefusals view commands records)
        return! generate prepared view blueprintDigest records vocabulary modelNode model
    }
