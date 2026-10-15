/// Deterministic pattern recognition with early delegation,
/// `ordo.pattern-recognition/1` (ORDO-PATTERN-001).
///
/// One bounded pass gives every requirement assigned to a cohort exactly one
/// outcome before anything is generated:
///
/// - Constructible: a registered pattern constructs it and every decision it
///   needs is approved.
/// - DecisionRequired: a pattern applies, but a decision is missing (or two
///   patterns compete). Recognition never supplies a default.
/// - Delegate: no pattern constructs its behavior. It goes immediately to an
///   agent as a bounded packet; any generated interface it needs is named.
/// - Blocked: contradictory, invalid or unusable inputs, or a prerequisite
///   that is itself blocked or undecided.
///
/// Requirements whose blueprint disposition is deferred, rejected or
/// superseded are not construction work; they are listed as excluded, so
/// every assigned requirement still appears exactly once.
///
/// The pass costs one claim evaluation per (requirement, selected pattern)
/// and a dependency closure over the validated (acyclic) blueprint. There is
/// no retry and no loop back into recognition. A false positive is worse
/// than a false negative, so every doubt resolves away from Constructible.
/// See docs/architecture/pattern-registry.md.
module Ordo.Core.PatternRecognition

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionDecisions
open Ordo.Core.PatternRegistry

[<Literal>]
let RecognitionSchemaVersion = "ordo.pattern-recognition/1"

[<Literal>]
let DelegationSchemaVersion = "ordo.delegation-contract/1"

[<Literal>]
let CompletionSchemaVersion = "ordo.delegation-completion/1"

// ---------------------------------------------------------------------------
// Types.
// ---------------------------------------------------------------------------

type Blocker =
    /// The manifest and blueprint do not validate; nothing in them is trusted.
    | InvalidSource of violations: string list
    /// The blueprint records the requirement as unresolved.
    | DispositionUnresolved of reason: string
    /// A pattern matched content that it cannot use safely.
    | UnusableContent of pattern: PatternRef * defects: string list
    /// A prerequisite requirement is blocked or awaits a decision.
    | AwaitingPrerequisites of requirementKeys: string list
    /// Recognition said constructible; the generator refused. Not retried.
    | GenerationRefused of refusals: string list

type Outcome =
    | Constructible of patterns: PatternRef list * delegatedPrerequisites: string list
    | DecisionRequired of gaps: DecisionGap list * generatedBy: PatternRef list
    | Delegate of interfaces: string list * generatedBy: PatternRef list
    | Blocked of blockers: Blocker list

type RequirementRecognition =
    { Key: string
      OriginalId: string
      Outcome: Outcome
      /// Every claim made, for audit. Order: by pattern identity.
      Claims: PatternClaim list }

type ExcludedRequirement = { Key: string; Disposition: string; Reason: string }

/// A decision someone must make. Never answered by recognition.
type DecisionObligation =
    { Id: string
      Gap: DecisionGap
      RequirementKeys: string list
      Question: string }

type SourceReference =
    { Key: string; OriginalId: string; Document: string; Location: string; Revision: string; ContentDigest: string }

type NodeReference =
    { NodeId: string
      Justification: string option
      /// Conflicts this approved decision settles (`conflictResolution`).
      Resolves: string list }

/// The bounded handoff for one coherent group of delegated requirements:
/// only what the agent needs to implement and prove them.
type DelegationPacket =
    { Id: string
      CohortId: string
      RequirementKeys: string list
      Sources: SourceReference list
      /// Approved decisions and rationales whose requirements intersect the
      /// packet or which its owners depend on.
      ApprovedDecisions: NodeReference list
      /// Generated interfaces the implementation must satisfy.
      Interfaces: string list
      Contracts: NodeReference list
      Invariants: NodeReference list
      VerificationObligations: NodeReference list
      /// Patterns that generate part of these requirements (structure only).
      GeneratedBy: PatternRef list
      AllowedPaths: string list
      ForbiddenPaths: string list
      ArchitectureConstraints: string list
      /// Open questions the agent must escalate, never answer: (subject, question).
      OpenQuestions: (string * string) list
      /// Requirements the agent may not report as implemented: each must be
      /// escalated (or not implemented), because a decision it needs is open.
      EscalationRequired: string list }

/// Host policy for delegated work. Supplied by the host, never inferred.
type DelegationPolicy =
    { AllowedPaths: string list
      ArchitectureConstraints: string list
      /// When true, a requirement whose only missing decisions are open
      /// conflict questions is also handed to the agent: it implements what
      /// the requirement determines and must escalate each question rather
      /// than answer it. Its outcome stays DecisionRequired.
      EscalateOpenQuestions: bool }

type RecognitionInput =
    { Manifest: SourceManifest
      Blueprint: Blueprint
      CohortId: string
      /// From an external, verified authority; never from the blueprint.
      AuthorizedDecisionIds: Set<string>
      Records: ApprovedDecisionRecords option
      Policy: DelegationPolicy }

type RecognitionError =
    | CohortNotFound of cohortId: string
    | UnsupportedRecordsSchema of actual: string
    | RecordsBoundToOtherBlueprint of bound: string * actual: string
    | RecordsBoundToOtherCohort of bound: string * actual: string
    | DuplicateRecordNode of nodeId: string
    | RegistryPinRefused of PinError list

type Measurements =
    { AssignedRequirements: int
      Evaluated: int
      Excluded: int
      Constructible: int
      DecisionRequired: int
      Delegated: int
      Blocked: int
      /// Delegated or awaiting a decision with part of the structure generated.
      PartiallyGenerated: int
      PatternsSelected: int
      /// Claim function calls: evaluated requirements x selected patterns.
      ClaimEvaluations: int
      DelegationPackets: int
      /// UTF-8 bytes of each packet's canonical encoding, summed.
      DelegationBytes: int }

type RecognitionReport =
    { SchemaVersion: string
      CohortId: string
      SourceManifestDigest: string
      BlueprintDigest: string
      DecisionRecordsDigest: string option
      Pin: RegistryPin
      Requirements: RequirementRecognition list
      Excluded: ExcludedRequirement list
      Decisions: DecisionObligation list
      Packets: DelegationPacket list
      /// True when a generator run would construct anything at all. False is
      /// the early exit: no generation, no host artifacts for the agent.
      GenerationWorthwhile: bool
      /// True only when every assigned requirement is constructible with no
      /// delegated prerequisite, or excluded as rejected or superseded.
      CompleteConstruction: bool
      Measurements: Measurements }

// ---------------------------------------------------------------------------
// Helpers.
// ---------------------------------------------------------------------------

let private sha256 (text: string) =
    text |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
    |> fun hex -> hex.ToLowerInvariant()

let private shortId (parts: string list) = (sha256 (String.concat "\n" parts)).Substring(0, 12)

let nodeKindName kind =
    match kind with
    | Decision -> "decision"
    | Contract -> "contract"
    | Invariant -> "invariant"
    | Interface -> "interface"
    | Cohort -> "cohort"
    | VerificationObligation -> "verificationObligation"
    | Conflict -> "conflict"
    | Deferral -> "deferral"
    | EngineeringRationale -> "engineeringRationale"

let private dispositionName d =
    match d with
    | Modeled -> "modeled", ""
    | Deferred reason -> "deferred", reason
    | Unresolved reason -> "unresolved", reason
    | Rejected reason -> "rejected", reason
    | Superseded(target, reason) -> "superseded", sprintf "%s (by %s)" reason target

let private portKeys port =
    match port with
    | RecordedCurrentTime keys
    | RecordedTransitionEffect(_, _, keys, _) -> keys

let gapLabel gap =
    match gap with
    | EffectFailureUndecided(node, command) -> sprintf "effect-failure:%s/%s" node command
    | DecisionNotAuthorized id -> "authorization:" + id
    | PatternSelectionRequired patterns -> "pattern-selection:" + (patterns |> List.map PatternRef.label |> String.concat ",")
    | ConflictUnresolved id -> "conflict:" + id

let private question (scope: Map<string, ConstructionNode>) gap =
    match gap with
    | ConflictUnresolved id ->
        match Map.tryFind id scope |> Option.bind _.Justification with
        | Some text -> text
        | None -> sprintf "Conflict %s is open and states no question text." id
    | EffectFailureUndecided(node, command) ->
        sprintf "Record %s leaves the failure disposition of the %s effect undecided. When the effect fails or its outcome is unknown, does the workflow remain in the transition's source state or proceed to its target?" node command
    | DecisionNotAuthorized id ->
        sprintf "Decision %s is in this cohort's required scope but is not approved. Approve or revise it." id
    | PatternSelectionRequired patterns ->
        sprintf "Patterns %s would each construct this requirement differently. Which one applies?"
            (patterns |> List.map PatternRef.label |> String.concat ", ")

/// The Phase 4 decision class of an outcome (`ConstructionClassification`,
/// ordo#78): whether the requirement's choices may be generated. Recognition
/// only constructs from an authorized decision record, so `Constructible` is
/// `approved`; any missing or unauthorized decision is `unresolved`; work no
/// pattern constructs is `unsupported`. `Blocked` has no class. `required`
/// and `implied` are never produced here: recognition reads no requirement
/// text and applies no documented rule.
let decisionClass outcome =
    match outcome with
    | Constructible _ -> Some "approved"
    | DecisionRequired _ -> Some "unresolved"
    | Delegate _ -> Some "unsupported"
    | Blocked _ -> None

let private isProblem outcome =
    match outcome with
    | Blocked _
    | DecisionRequired _ -> true
    | Constructible _
    | Delegate _ -> false

/// A gap an agent can carry as an escalation without deciding it: the
/// requirement's determinate part does not depend on generated content.
let private escalatable gap =
    match gap with
    | ConflictUnresolved _ -> true
    | EffectFailureUndecided _
    | DecisionNotAuthorized _
    | PatternSelectionRequired _ -> false

/// DecisionRequired only by open questions, with nothing generated.
let private onlyOpenQuestions outcome =
    match outcome with
    | DecisionRequired(gaps, []) -> List.forall escalatable gaps
    | _ -> false

// ---------------------------------------------------------------------------
// The pass.
// ---------------------------------------------------------------------------

let private recordsErrors (input: RecognitionInput) =
    match input.Records with
    | None -> []
    | Some r ->
        let blueprintDigest = ConstructionIrWire.blueprintDigest input.Blueprint
        [ if not (List.contains r.SchemaVersion [ DecisionsSchemaVersion; DecisionsSchemaVersion2; DecisionsSchemaVersion3 ]) then
              UnsupportedRecordsSchema r.SchemaVersion
          if r.BlueprintDigest <> blueprintDigest then RecordsBoundToOtherBlueprint(r.BlueprintDigest, blueprintDigest)
          if r.CohortId <> input.CohortId then RecordsBoundToOtherCohort(r.CohortId, input.CohortId)
          for id in r.Records |> List.countBy _.NodeId |> List.filter (snd >> (<) 1) |> List.map fst do
              DuplicateRecordNode id ]

/// Base outcome of one requirement from its claims alone.
let private baseOutcome (facts: RequirementFacts) (claims: PatternClaim list) (cohort: CohortFacts) : Outcome =
    let conflicts =
        facts.Owners
        |> List.filter (fun n -> n.Kind = Conflict && not (Map.containsKey n.Id cohort.ResolvedConflicts))
        |> List.map (fun n -> ConflictUnresolved n.Id)
    let unauthorized =
        facts.Owners
        |> List.filter (fun n -> n.Kind = Decision && not (Set.contains n.Id cohort.AuthorizedDecisionIds))
        |> List.map (fun n -> DecisionNotAuthorized n.Id)
    let defective = claims |> List.filter (fun c -> not (List.isEmpty c.Defects))
    let constructing = claims |> List.filter (fun c -> c.Kind = ClaimKind.Constructs) |> List.map _.Pattern
    let competing = constructing |> List.map _.Id |> List.distinct
    let interfaces = claims |> List.collect (fun c -> match c.Kind with ClaimKind.InterfaceOnly s -> s | ClaimKind.Constructs -> [])
    let gaps =
        [ yield! unauthorized
          yield! conflicts
          yield! claims |> List.collect _.Gaps
          if competing.Length > 1 then yield PatternSelectionRequired(constructing |> List.distinct |> List.sort) ]
        |> List.distinct
    match facts.Disposition with
    | Unresolved reason -> Blocked [ DispositionUnresolved reason ]
    | _ when not (List.isEmpty defective) -> Blocked(defective |> List.map (fun c -> UnusableContent(c.Pattern, c.Defects)))
    | _ when not (List.isEmpty gaps) -> DecisionRequired(gaps |> List.sortBy gapLabel, constructing |> List.distinct |> List.sort)
    | _ when not (List.isEmpty interfaces) -> Delegate(interfaces |> List.distinct |> List.sort, constructing |> List.distinct |> List.sort)
    | _ when not (List.isEmpty constructing) -> Constructible(constructing |> List.distinct |> List.sort, [])
    | _ -> Delegate([], [])

/// Requirement keys owned by nodes reachable through DependsOn from the
/// requirement's owners. The blueprint validated, so the graph is acyclic.
let private prerequisites (scope: Map<string, ConstructionNode>) (facts: RequirementFacts) =
    let rec reach (seen: Set<string>) pending =
        match pending with
        | [] -> seen
        | id :: rest when Set.contains id seen -> reach seen rest
        | id :: rest ->
            let next = scope |> Map.tryFind id |> Option.map _.DependsOn |> Option.defaultValue []
            reach (Set.add id seen) (next @ rest)
    let owners = facts.Owners |> List.map _.Id
    reach Set.empty (facts.Owners |> List.collect _.DependsOn)
    |> Set.filter (fun id -> not (List.contains id owners))
    |> Set.toList
    |> List.choose (fun id -> Map.tryFind id scope)
    |> List.filter (fun n -> n.Kind <> Cohort)
    |> List.collect _.RequirementKeys
    |> List.filter ((<>) facts.Key)
    |> List.distinct
    |> List.sort

/// Generated work never rests on a prerequisite that is blocked or
/// undecided. Delegated work may, under `EscalateOpenQuestions`, when every
/// pending prerequisite waits only on open questions: the agent inherits them.
let private finalOutcome (policy: DelegationPolicy) (baseOutcomes: Map<string, Outcome>) (prereqs: string list) (outcome: Outcome) =
    let pending = prereqs |> List.filter (fun k -> baseOutcomes |> Map.tryFind k |> Option.exists isProblem)
    let delegated = prereqs |> List.filter (fun k -> match Map.tryFind k baseOutcomes with Some(Delegate _) -> true | _ -> false)
    let inheritable = pending |> List.forall (fun k -> baseOutcomes |> Map.tryFind k |> Option.exists onlyOpenQuestions)
    match outcome with
    | Constructible _ when not (List.isEmpty pending) -> Blocked [ AwaitingPrerequisites pending ]
    | Delegate _ when not (List.isEmpty pending) && not (policy.EscalateOpenQuestions && inheritable) ->
        Blocked [ AwaitingPrerequisites pending ]
    | DecisionRequired _ when not (List.isEmpty pending) && not (policy.EscalateOpenQuestions && inheritable) && onlyOpenQuestions outcome ->
        Blocked [ AwaitingPrerequisites pending ]
    | Constructible(patterns, _) -> Constructible(patterns, delegated)
    | other -> other

let private sourceRef (s: SourceRequirement) : SourceReference =
    { Key = s.Key; OriginalId = s.OriginalId; Document = s.Document; Location = s.Location; Revision = s.Revision; ContentDigest = s.ContentDigest }

let private nodeRef (n: ConstructionNode) = { NodeId = n.Id; Justification = n.Justification; Resolves = [] }

/// Conflicts settled by an authorized decision in scope. A link counts only
/// when its node is an authorized Decision in scope, the conflict is a
/// Conflict node in scope, and they share a requirement key. Anything else
/// leaves the conflict open: the safe direction.
let private resolvedConflicts (scope: Map<string, ConstructionNode>) (authorized: Set<string>) (records: DecisionRecord list) =
    records
    |> List.collect (fun r ->
        match r.Content, Map.tryFind r.NodeId scope with
        | ConflictResolution c, Some node when node.Kind = Decision && Set.contains r.NodeId authorized ->
            c.Conflicts
            |> List.distinct
            |> List.filter (fun id ->
                match Map.tryFind id scope with
                | Some conflict when conflict.Kind = Conflict ->
                    not (Set.isEmpty (Set.intersect (Set.ofList conflict.RequirementKeys) (Set.ofList c.RequirementKeys)))
                | _ -> false)
            |> List.map (fun id -> id, r.NodeId)
        | _ -> [])
    |> List.groupBy fst
    |> List.map (fun (id, pairs) -> id, pairs |> List.map snd |> List.distinct |> List.sort)
    |> Map.ofList

let private forbiddenPaths (cohort: CohortFacts) =
    cohort.StateModels
    |> List.choose (fun (_, m) -> match SemanticName.create m.Workflow with Ok w -> Some(ConstructionLayout.ownershipRoot w + "**") | Error _ -> None)
    |> List.distinct
    |> List.sort

let private packet
    (input: RecognitionInput)
    (cohort: CohortFacts)
    (sources: Map<string, SourceRequirement>)
    (members: (RequirementFacts * Outcome * DecisionGap list) list)
    : DelegationPacket =
    let keys = members |> List.map (fun (f, _, _) -> f.Key) |> List.sort
    let owners = members |> List.collect (fun (f, _, _) -> f.Owners) |> List.distinctBy _.Id |> List.sortBy _.Id
    let ownGaps outcome = match outcome with DecisionRequired(gaps, _) -> gaps | _ -> []
    let reachable =
        let rec reach (seen: Set<string>) pending =
            match pending with
            | [] -> seen
            | id :: rest when Set.contains id seen -> reach seen rest
            | id :: rest ->
                let next = cohort.ScopeNodes |> Map.tryFind id |> Option.map _.DependsOn |> Option.defaultValue []
                reach (Set.add id seen) (next @ rest)
        reach Set.empty (owners |> List.map _.Id)
    // Conflicts owning a packet requirement that an authorized decision settles.
    let settled =
        owners
        |> List.filter (fun n -> n.Kind = Conflict)
        |> List.collect (fun n -> Map.tryFind n.Id cohort.ResolvedConflicts |> Option.defaultValue [] |> List.map (fun d -> d, n.Id))
    let approved =
        cohort.ScopeNodes
        |> Map.toList
        |> List.map snd
        |> List.filter (fun n ->
            (n.Kind = Decision || n.Kind = EngineeringRationale)
            && Set.contains n.Id cohort.AuthorizedDecisionIds
            && (Set.contains n.Id reachable
                || n.RequirementKeys |> List.exists (fun k -> List.contains k keys)
                || settled |> List.exists (fst >> (=) n.Id)))
        |> List.map (fun n ->
            { nodeRef n with
                Resolves = settled |> List.filter (fst >> (=) n.Id) |> List.map snd |> List.distinct |> List.sort })
    let ofKind kinds = owners |> List.filter (fun n -> List.contains n.Kind kinds) |> List.map nodeRef
    let interfaces, generatedBy =
        members
        |> List.map (fun (_, o, _) -> o)
        |> List.fold (fun (i, g) o ->
            match o with
            | Delegate(s, p) -> i @ s, g @ p
            | _ -> i, g) ([], [])
    { Id = sprintf "DLG-%s-%s" input.CohortId (shortId keys)
      CohortId = input.CohortId
      RequirementKeys = keys
      Sources = keys |> List.choose (fun k -> Map.tryFind k sources) |> List.map sourceRef
      ApprovedDecisions = approved
      Interfaces = interfaces |> List.distinct |> List.sort
      Contracts = ofKind [ Contract; Interface ]
      Invariants = ofKind [ Invariant ]
      VerificationObligations = ofKind [ VerificationObligation ]
      GeneratedBy = generatedBy |> List.distinct |> List.sort
      AllowedPaths = input.Policy.AllowedPaths |> List.distinct |> List.sort
      ForbiddenPaths = forbiddenPaths cohort
      ArchitectureConstraints = input.Policy.ArchitectureConstraints |> List.distinct |> List.sort
      OpenQuestions =
        members
        |> List.collect (fun (_, o, inherited) -> ownGaps o @ inherited)
        |> List.distinct
        |> List.map (fun g -> gapLabel g, question cohort.ScopeNodes g)
        |> List.sort
      EscalationRequired =
        members
        |> List.filter (fun (_, o, inherited) -> not (List.isEmpty (ownGaps o @ inherited)))
        |> List.map (fun (f, _, _) -> f.Key)
        |> List.sort }

/// Coherent delegation groups: requirements connected through a shared
/// contract, interface, invariant, decision or rationale node. Verification
/// obligations and conflicts do not join groups; they travel with each.
/// Deterministic: components are found from the sorted keys.
let private coherentGroups (factsOf: 'a -> RequirementFacts) (members: 'a list) : 'a list list =
    let joins (n: ConstructionNode) =
        match n.Kind with
        | Contract | Interface | Invariant | Decision | EngineeringRationale -> true
        | VerificationObligation | Conflict | Cohort | Deferral -> false
    let byKey = members |> List.map (fun m -> (factsOf m).Key, m) |> Map.ofList
    let nodesOf key = (factsOf byKey[key]).Owners |> List.filter joins |> List.map _.Id |> Set.ofList
    let neighbours key =
        let mine = nodesOf key
        byKey |> Map.toList |> List.map fst
        |> List.filter (fun other -> other <> key && not (Set.isEmpty (Set.intersect mine (nodesOf other))))
    let rec reachable (seen: Set<string>) pending =
        match pending with
        | [] -> seen
        | k :: rest when Set.contains k seen -> reachable seen rest
        | k :: rest -> reachable (Set.add k seen) (neighbours k @ rest)
    let rec split (remaining: string list) acc =
        match remaining with
        | [] -> List.rev acc
        | k :: _ ->
            let c = reachable Set.empty [ k ]
            split (remaining |> List.filter (fun x -> not (Set.contains x c))) (c :: acc)
    split (byKey |> Map.toList |> List.map fst) []
    |> List.map (fun c -> c |> Set.toList |> List.map (fun k -> byKey[k]))

let private emptyMeasurements =
    { AssignedRequirements = 0; Evaluated = 0; Excluded = 0; Constructible = 0; DecisionRequired = 0; Delegated = 0
      Blocked = 0; PartiallyGenerated = 0; PatternsSelected = 0; ClaimEvaluations = 0; DelegationPackets = 0; DelegationBytes = 0 }

let private measure (patterns: int) (packetBytes: DelegationPacket -> int) (requirements: RequirementRecognition list) excluded packets =
    let count f = requirements |> List.filter (fun r -> f r.Outcome) |> List.length
    { emptyMeasurements with
        AssignedRequirements = requirements.Length + List.length excluded
        Evaluated = requirements.Length
        Excluded = List.length excluded
        Constructible = count (function Constructible _ -> true | _ -> false)
        DecisionRequired = count (function DecisionRequired _ -> true | _ -> false)
        Delegated = count (function Delegate _ -> true | _ -> false)
        Blocked = count (function Blocked _ -> true | _ -> false)
        PartiallyGenerated = count (function Delegate(_, g) | DecisionRequired(_, g) -> not (List.isEmpty g) | _ -> false)
        PatternsSelected = patterns
        ClaimEvaluations = requirements.Length * patterns
        DelegationPackets = List.length packets
        DelegationBytes = packets |> List.sumBy packetBytes }

/// The recognition pass. Pure: equal inputs give an equal report, whatever
/// the order of registry definitions, records, nodes or requirements.
/// `encodePacket` measures handoff size; pass the wire encoder.
let recognizeWith
    (encodePacket: DelegationPacket -> string)
    (registry: Registry)
    (pin: RegistryPin option)
    (input: RecognitionInput)
    : Result<RecognitionReport, RecognitionError list> =
    let cohortNode = input.Blueprint.Nodes |> List.tryFind (fun n -> n.Id = input.CohortId && n.Kind = Cohort)
    match cohortNode, recordsErrors input, Registry.select pin registry with
    | None, _, _ -> Error [ CohortNotFound input.CohortId ]
    | _, (_ :: _ as errors), _ -> Error(errors |> List.sortBy (sprintf "%A"))
    | _, _, Error pinErrors -> Error [ RegistryPinRefused pinErrors ]
    | Some cohortNode, [], Ok(definitions, selectedPin) ->
        let packetBytes p = Encoding.UTF8.GetByteCount(encodePacket p)
        let assigned = cohortNode.RequirementKeys |> List.distinct |> List.sort
        let sources = input.Manifest.Requirements |> List.map (fun s -> s.Key, s) |> Map.ofList
        let originalId k = sources |> Map.tryFind k |> Option.map _.OriginalId |> Option.defaultValue k
        let records = input.Records |> Option.map _.Records |> Option.defaultValue [] |> List.sortBy _.NodeId
        let report requirements excluded decisions packets generation complete =
            { SchemaVersion = RecognitionSchemaVersion
              CohortId = input.CohortId
              SourceManifestDigest = input.Manifest.Digest
              BlueprintDigest = ConstructionIrWire.blueprintDigest input.Blueprint
              DecisionRecordsDigest = input.Records |> Option.map ConstructionDecisions.digest
              Pin = selectedPin
              Requirements = requirements
              Excluded = excluded
              Decisions = decisions
              Packets = packets
              GenerationWorthwhile = generation
              CompleteConstruction = complete
              Measurements = measure definitions.Length packetBytes requirements excluded packets }
        match requiredScope input.Manifest input.Blueprint input.CohortId with
        | None ->
            let violations = ConstructionIr.validate input.Manifest input.Blueprint |> List.map (sprintf "%A")
            let blocked =
                assigned
                |> List.map (fun k -> { Key = k; OriginalId = originalId k; Outcome = Blocked [ InvalidSource violations ]; Claims = [] })
            Ok(report blocked [] [] [] false false)
        | Some scopeIds ->
            let scope =
                input.Blueprint.Nodes |> List.filter (fun n -> Set.contains n.Id scopeIds) |> List.map (fun n -> n.Id, n) |> Map.ofList
            let cohort =
                { CohortId = input.CohortId
                  ScopeNodes = scope
                  AuthorizedDecisionIds = input.AuthorizedDecisionIds
                  StateModels = records |> List.choose (fun r -> match r.Content with StateModel m -> Some(r.NodeId, m) | PortContract _ | ConflictResolution _ -> None)
                  ResolvedConflicts = resolvedConflicts scope input.AuthorizedDecisionIds records }
            let dispositions = input.Blueprint.Requirements |> List.map (fun r -> r.Source.Key, r.Disposition) |> Map.ofList
            let factsOf k =
                { Key = k
                  Disposition = dispositions |> Map.tryFind k |> Option.defaultValue Modeled
                  Owners =
                    scope |> Map.toList |> List.map snd
                    |> List.filter (fun n -> n.Kind <> Cohort && List.contains k n.RequirementKeys)
                  Transitions =
                    records
                    |> List.collect (fun r ->
                        match r.Content with
                        | StateModel m -> m.Transitions |> List.filter (fun t -> List.contains k t.RequirementKeys) |> List.map (fun t -> r.NodeId, t)
                        | PortContract _ | ConflictResolution _ -> [])
                  Ports =
                    records
                    |> List.collect (fun r ->
                        match r.Content with
                        | PortContract ports -> ports |> List.filter (portKeys >> List.contains k) |> List.map (fun p -> r.NodeId, p)
                        | StateModel _ | ConflictResolution _ -> []) }
            let inScope = Set.union (Set.ofList assigned) (scope |> Map.toList |> List.collect (snd >> _.RequirementKeys) |> Set.ofList)
            let evaluate k =
                let facts = factsOf k
                let claims = definitions |> List.choose (fun d -> d.Claim cohort facts) |> List.sortBy _.Pattern
                facts, claims, baseOutcome facts claims cohort
            let evaluated = inScope |> Set.toList |> List.map (fun k -> k, evaluate k) |> Map.ofList
            let baseOutcomes = evaluated |> Map.map (fun _ (_, _, o) -> o)
            let isExcluded (facts: RequirementFacts) =
                match facts.Disposition with
                | Deferred _ | Rejected _ | Superseded _ -> true
                | Modeled | Unresolved _ -> false
            let excluded =
                assigned
                |> List.choose (fun k ->
                    let facts, _, _ = evaluated[k]
                    if isExcluded facts then
                        let name, reason = dispositionName facts.Disposition
                        Some { ExcludedRequirement.Key = k; Disposition = name; Reason = reason }
                    else None)
            let recognized =
                assigned
                |> List.choose (fun k ->
                    let facts, claims, outcome = evaluated[k]
                    if isExcluded facts then None
                    else
                        let prereqs = prerequisites scope facts
                        let final = finalOutcome input.Policy baseOutcomes prereqs outcome
                        let inherited =
                            prereqs
                            |> List.choose (fun p -> Map.tryFind p baseOutcomes)
                            |> List.filter onlyOpenQuestions
                            |> List.collect (function DecisionRequired(gaps, _) -> gaps | _ -> [])
                            |> List.distinct
                        Some(facts, inherited, { Key = k; OriginalId = originalId k; Outcome = final; Claims = claims }))
            let requirements = recognized |> List.map (fun (_, _, r) -> r)
            let decisions =
                requirements
                |> List.collect (fun r -> match r.Outcome with DecisionRequired(gaps, _) -> gaps |> List.map (fun g -> g, r.Key) | _ -> [])
                |> List.groupBy fst
                |> List.map (fun (gap, pairs) ->
                    let keys = pairs |> List.map snd |> List.distinct |> List.sort
                    { Id = sprintf "DEC-REQ-%s-%s" input.CohortId (shortId [ gapLabel gap ])
                      Gap = gap
                      RequirementKeys = keys
                      Question = question scope gap })
                |> List.sortBy _.Id
            let packets =
                recognized
                |> List.choose (fun (facts, inherited, r) ->
                    match r.Outcome with
                    | Delegate _ -> Some(facts, r.Outcome, inherited)
                    | DecisionRequired _ when input.Policy.EscalateOpenQuestions && onlyOpenQuestions r.Outcome ->
                        Some(facts, r.Outcome, inherited)
                    | _ -> None)
                |> coherentGroups (fun (facts, _, _) -> facts)
                |> List.map (packet input cohort sources)
                |> List.sortBy _.Id
            let generation =
                requirements
                |> List.exists (fun r ->
                    match r.Outcome with
                    | Constructible _ -> true
                    | Delegate(_, g)
                    | DecisionRequired(_, g) -> not (List.isEmpty g)
                    | Blocked _ -> false)
            let complete =
                requirements |> List.forall (fun r -> match r.Outcome with Constructible(_, []) -> true | _ -> false)
                && excluded |> List.forall (fun e -> e.Disposition <> "deferred")
            Ok(report requirements excluded decisions packets generation complete)

// ---------------------------------------------------------------------------
// After generation. One generator run, never retried by recognition.
// ---------------------------------------------------------------------------

/// Folds the single generator result into the report. A refusal blocks
/// every requirement whose outcome rested on generated content; delegated
/// requirements without generated parts are unaffected and proceed.
/// `encodePacket` re-measures handoff size; pass the wire encoder.
let applyGenerationWith (encodePacket: DelegationPacket -> string) (refusals: string list) (report: RecognitionReport) : RecognitionReport =
    match refusals with
    | [] -> report
    | _ ->
        let relied outcome =
            match outcome with
            | Constructible _ -> true
            | Delegate(_, g)
            | DecisionRequired(_, g) -> not (List.isEmpty g)
            | Blocked _ -> false
        let affected = report.Requirements |> List.filter (_.Outcome >> relied) |> List.map _.Key |> Set.ofList
        let requirements =
            report.Requirements
            |> List.map (fun r -> if Set.contains r.Key affected then { r with Outcome = Blocked [ GenerationRefused refusals ] } else r)
        let packets =
            report.Packets
            |> List.filter (fun p -> p.RequirementKeys |> List.forall (fun k -> not (Set.contains k affected)))
        let packetBytes p = Encoding.UTF8.GetByteCount(encodePacket p)
        { report with
            Requirements = requirements
            Packets = packets
            GenerationWorthwhile = false
            CompleteConstruction = false
            Measurements = measure report.Measurements.PatternsSelected packetBytes requirements report.Excluded packets }

// ---------------------------------------------------------------------------
// Agent completion. Praxis validates a returned delegation before applying
// any artifact from it.
// ---------------------------------------------------------------------------

[<RequireQualifiedAccess>]
type CompletionDisposition =
    | Implemented
    | NotImplemented of reason: string
    | Escalated of question: string

type CompletionArtifact = { Path: string; ContentDigest: string }

type DelegationCompletion =
    { SchemaVersion: string
      PacketId: string
      /// Digest of the packet's canonical encoding the agent was given.
      PacketDigest: string
      Artifacts: CompletionArtifact list
      Dispositions: (string * CompletionDisposition) list
      /// (verification obligation node id, evidence reference)
      VerificationEvidence: (string * string) list }

type CompletionViolation =
    | UnsupportedCompletionSchema of actual: string
    | PacketMismatch of expectedId: string * expectedDigest: string
    | UnsafePath of path: string
    | ArtifactInForbiddenPath of path: string
    | ArtifactOutsideAllowedPaths of path: string
    | DuplicateArtifact of path: string
    | MissingDisposition of requirementKey: string
    | DuplicateDisposition of requirementKey: string
    | UnknownRequirement of requirementKey: string
    | BlankDispositionReason of requirementKey: string
    | MissingVerificationEvidence of obligationId: string
    | UnknownVerificationObligation of obligationId: string
    | ImplementedWithoutArtifacts
    /// The packet requires escalation for this requirement; reporting it
    /// implemented would silently answer an open question.
    | ImplementedDespiteOpenQuestion of requirementKey: string

/// Glob over forward-slashed relative paths: `*` matches within one
/// segment, `**` matches any number of segments.
let globMatch (pattern: string) (path: string) =
    let rec segment (p: string) (s: string) =
        match p, s with
        | "", "" -> true
        | "", _ -> false
        | _ when p.[0] = '*' -> segment (p.Substring 1) s || (s <> "" && segment p (s.Substring 1))
        | _, "" -> false
        | _ -> p.[0] = s.[0] && segment (p.Substring 1) (s.Substring 1)
    let rec walk (ps: string list) (ss: string list) =
        match ps, ss with
        | [], [] -> true
        | [ "**" ], _ -> true
        | "**" :: rest, _ -> walk rest ss || (not (List.isEmpty ss) && walk ps (List.tail ss))
        | p :: prest, s :: srest -> segment p s && walk prest srest
        | _ -> false
    walk (pattern.Split('/') |> List.ofArray) (path.Split('/') |> List.ofArray)

let private unsafePath (path: string) =
    String.IsNullOrWhiteSpace path
    || path.StartsWith "/"
    || path.Contains "\\"
    || path.Contains ":"
    || path.Split('/') |> Array.exists (fun s -> s = ".." || s = "." || s = "")

/// Every check is independent of the agent's own claims about itself.
let validateCompletion (packetDigest: string) (packet: DelegationPacket) (completion: DelegationCompletion) : CompletionViolation list =
    let keys = Set.ofList packet.RequirementKeys
    let dispositionKeys = completion.Dispositions |> List.map fst
    let obligations = packet.VerificationObligations |> List.map _.NodeId |> Set.ofList
    let evidenced = completion.VerificationEvidence |> List.map fst |> Set.ofList
    [ if completion.SchemaVersion <> CompletionSchemaVersion then UnsupportedCompletionSchema completion.SchemaVersion
      if completion.PacketId <> packet.Id || completion.PacketDigest <> packetDigest then PacketMismatch(packet.Id, packetDigest)
      for a in completion.Artifacts do
          if unsafePath a.Path then UnsafePath a.Path
          elif packet.ForbiddenPaths |> List.exists (fun g -> globMatch g a.Path) then ArtifactInForbiddenPath a.Path
          elif not (packet.AllowedPaths |> List.exists (fun g -> globMatch g a.Path)) then ArtifactOutsideAllowedPaths a.Path
      for p in completion.Artifacts |> List.countBy _.Path |> List.filter (snd >> (<) 1) |> List.map fst do
          DuplicateArtifact p
      for k in keys do
          if not (List.contains k dispositionKeys) then MissingDisposition k
      for k in dispositionKeys |> List.countBy id |> List.filter (snd >> (<) 1) |> List.map fst do
          DuplicateDisposition k
      for k, d in completion.Dispositions do
          if not (Set.contains k keys) then UnknownRequirement k
          match d with
          | CompletionDisposition.NotImplemented reason
          | CompletionDisposition.Escalated reason when String.IsNullOrWhiteSpace reason -> BlankDispositionReason k
          | _ -> ()
      if completion.Dispositions |> List.exists (snd >> (=) CompletionDisposition.Implemented) && List.isEmpty completion.Artifacts then
          ImplementedWithoutArtifacts
      for k, d in completion.Dispositions do
          if d = CompletionDisposition.Implemented && List.contains k packet.EscalationRequired then
              ImplementedDespiteOpenQuestion k
      for o in obligations do
          let implementedAny = completion.Dispositions |> List.exists (snd >> (=) CompletionDisposition.Implemented)
          if implementedAny && not (Set.contains o evidenced) then MissingVerificationEvidence o
      for o in evidenced do
          if not (Set.contains o obligations) then UnknownVerificationObligation o ]
    |> List.distinct
    |> List.sortBy (sprintf "%A")
