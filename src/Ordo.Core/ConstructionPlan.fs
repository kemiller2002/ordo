/// A typed, versioned construction vocabulary over a prepared ECIR cohort
/// (CONSTRUCT-001). An agent expresses domain intent with a closed set of
/// operations; it cannot express a file, a path, a command, a dependency, a
/// code fragment, an architecture choice or a policy change, because the
/// vocabulary has no case that could carry one.
///
/// Validation is semantic, not authorization: a `ValidatedPlan` proves only
/// that the plan agrees with the exact prepared cohort it was checked
/// against. It is never a host credential or permission to cause effects.
/// See docs/architecture/construction-plan-v1.md.
module Ordo.Core.ConstructionPlan

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.ConstructionIr

[<Literal>]
let PlanSchemaVersion = "ordo.construction-plan/1"

[<Literal>]
let VocabularyVersion = "ordo.construction-vocabulary/1"

// ---------------------------------------------------------------------------
// The untrusted draft: everything an agent is able to say.
// ---------------------------------------------------------------------------

/// The ECIR identities an operation claims to implement. Both directions are
/// checked against the prepared cohort; an operation cannot cite a node or
/// requirement outside it.
type TraceDraft =
    { RequirementKeys: string list
      NodeIds: string list }

/// How a guard or invariant is decided. Version 1 has exactly one case: the
/// predicate is a named contract that a handwritten extension implements.
/// There is no expression, operator or code case, so none can be supplied.
type Predicate = | ExtensionPredicate

/// A state's structural role. One union rather than two flags, so a state
/// cannot be both initial and terminal.
type StateRole =
    | Initial
    | Ordinary
    | Terminal

type WorkflowDraft = { Name: string; Trace: TraceDraft }

type StateDraft =
    { Name: string
      Role: StateRole
      Trace: TraceDraft }

type GuardDraft = { Name: string; Predicate: Predicate }

/// A command is legal from one source state and leads to one target. The set
/// of commands is derived from transitions rather than declared separately.
type TransitionDraft =
    { Command: string
      From: string
      To: string
      Guards: GuardDraft list
      Trace: TraceDraft }

type InvariantDraft =
    { Name: string
      Predicate: Predicate
      HoldsIn: string list
      Trace: TraceDraft }

/// The orchestration ports a workflow may require. The engine chooses their
/// signatures and placement; the plan only states that one is needed.
type PortKindDraft =
    /// The current instant, read once per handled command.
    | CurrentTime
    /// An outbound effect requested after the named command is accepted.
    | TransitionEffect of command: string

type PortDraft = { Kind: PortKindDraft; Trace: TraceDraft }

/// A semantic capability required to issue a command (`Ordo.Core.Capability`):
/// an authority prerequisite the host checks, never a credential.
type CapabilityDraft =
    { Command: string
      Capability: string
      Trace: TraceDraft }

/// Behavior the plan cannot or must not decide. It becomes an explicit open
/// obligation instead of guessed implementation. The question is prose for a
/// person; it is never rendered into generated code.
type UnresolvedDraft =
    { Subject: string
      Question: string
      Trace: TraceDraft }

type OperationDraft =
    | DefineWorkflow of WorkflowDraft
    | DefineState of StateDraft
    | DefineTransition of TransitionDraft
    | DefineInvariant of InvariantDraft
    | DeclarePort of PortDraft
    | BindCapability of CapabilityDraft
    | DeclareUnresolved of UnresolvedDraft

/// What the plan claims to have been formed against. Every field is checked
/// against the prepared cohort rather than believed.
type BindingDraft =
    { SourceManifestDigest: string
      BlueprintDigest: string
      CohortId: string
      DecisionIds: string list }

type PlanDraft =
    { SchemaVersion: string
      VocabularyVersion: string
      Binding: BindingDraft
      Operations: OperationDraft list }

// ---------------------------------------------------------------------------
// Semantic names: domain words, never code.
// ---------------------------------------------------------------------------

type NameError =
    | NameBlank
    | NameTooLong of length: int
    | NameMustStartWithLetter
    | NameHasIllegalCharacter of character: char
    | NameHasEmptyWord

[<Literal>]
let MaxNameLength = 64

/// A domain name made of ASCII letter/digit words joined by single spaces,
/// hyphens or underscores. Its identity is the words in PascalCase, so
/// "no timer", "no-timer" and "NoTimer" are one name and cannot be defined
/// twice. The grammar admits no character that could escape into code.
type SemanticName =
    private
    | SemanticName of string

    member this.Key =
        let (SemanticName key) = this
        key

    override this.ToString() = this.Key

[<RequireQualifiedAccess>]
module SemanticName =

    let private isWordCharacter (ch: char) =
        (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')

    let private isSeparator (ch: char) = ch = ' ' || ch = '-' || ch = '_'

    let create (raw: string) : Result<SemanticName, NameError> =
        if String.IsNullOrEmpty raw then Error NameBlank
        elif raw.Length > MaxNameLength then Error(NameTooLong raw.Length)
        elif not (Char.IsAsciiLetter raw[0]) then Error NameMustStartWithLetter
        else
            match raw |> Seq.tryFind (fun ch -> not (isWordCharacter ch || isSeparator ch)) with
            | Some illegal -> Error(NameHasIllegalCharacter illegal)
            | None ->
                let words = raw.Split([| ' '; '-'; '_' |])
                if words |> Array.exists String.IsNullOrEmpty then Error NameHasEmptyWord
                else
                    words
                    |> Array.map (fun word -> string (Char.ToUpperInvariant word[0]) + word.Substring 1)
                    |> String.concat ""
                    |> SemanticName
                    |> Ok

    let key (name: SemanticName) = name.Key

// ---------------------------------------------------------------------------
// The validated model. Readable by anyone; constructible only by `validate`
// as part of a ValidatedPlan.
// ---------------------------------------------------------------------------

/// Sorted, distinct ECIR identities.
type Trace =
    { RequirementKeys: string list
      NodeIds: string list }

type Guard = { Name: SemanticName; Predicate: Predicate }

type State =
    { Name: SemanticName
      Role: StateRole
      Trace: Trace }

type Transition =
    { Command: SemanticName
      From: SemanticName
      To: SemanticName
      Guards: Guard list
      Trace: Trace }

type Invariant =
    { Name: SemanticName
      Predicate: Predicate
      HoldsIn: SemanticName list
      Trace: Trace }

type PortKind =
    | CurrentTimePort
    | TransitionEffectPort of command: SemanticName

type Port = { Kind: PortKind; Trace: Trace }

type CapabilityBinding =
    { Command: SemanticName
      Capability: CapabilityId
      Trace: Trace }

type UnresolvedObligation =
    { Subject: SemanticName
      Question: string
      Trace: Trace }

/// One finite-state workflow. Lists are in canonical order: states with the
/// initial state first then by name; everything else by name.
type Workflow =
    { Name: SemanticName
      Trace: Trace
      States: State list
      Commands: SemanticName list
      Transitions: Transition list
      Invariants: Invariant list
      Ports: Port list
      Capabilities: CapabilityBinding list
      Unresolved: UnresolvedObligation list }

type PlanBinding =
    { SchemaVersion: string
      VocabularyVersion: string
      SourceManifestDigest: string
      BlueprintDigest: string
      CohortId: string
      DecisionIds: string list }

type ValidatedPlanView =
    { Binding: PlanBinding
      Workflow: Workflow
      /// Requirements this cohort implements, as prepared. Prerequisite scope
      /// is not implementation scope.
      AssignedRequirementKeys: Set<string>
      /// Content address of the canonical plan.
      Digest: string }

/// Semantic validity against one exact prepared cohort, and nothing more:
/// not authenticated authority, not host permission, not proof of
/// implementation or verification. No caller can construct one.
type ValidatedPlan = private ValidatedPlan of ValidatedPlanView

/// Why a plan was refused. Every case names the operation or identity at
/// fault, so a refusal is actionable rather than a bare "invalid".
type PlanViolation =
    | UnsupportedPlanSchema of actual: string
    | UnsupportedVocabulary of actual: string
    | StaleSourceManifest of bound: string * prepared: string
    | StaleBlueprint of bound: string * prepared: string
    | CohortMismatch of bound: string * prepared: string
    | UnboundDecision of decisionId: string
    | UnauthorizedDecision of decisionId: string
    | InvalidName of operation: string * raw: string * reason: NameError
    | InvalidCapability of operation: string * raw: string
    | MissingSourceTrace of operation: string
    | MissingNodeTrace of operation: string
    | UnassignedRequirement of operation: string * requirementKey: string
    | UnauthorizedNodeReference of operation: string * nodeId: string
    | InconsistentTrace of operation: string * nodeId: string
    | UncoveredRequirement of requirementKey: string
    | MissingWorkflow
    | MultipleWorkflows
    | DuplicateDefinition of operation: string
    | MissingInitialState
    | MultipleInitialStates of states: string list
    | UnknownState of operation: string * state: string
    | UnknownCommand of operation: string * command: string
    | ConflictingTransition of from: string * command: string * targets: string list
    | TransitionFromTerminal of operation: string
    | UnreachableState of state: string
    | DeadEndState of state: string
    | EmptyInvariantScope of operation: string
    | BlankUnresolvedQuestion of operation: string

// ---------------------------------------------------------------------------
// Encoding. The draft is the wire shape; the canonical plan is the draft of
// a validated workflow with names normalized and every list ordered.
// ---------------------------------------------------------------------------

let private jStrings values = JArray(List.map JString values)

let private jTrace (trace: TraceDraft) =
    JObject [ "requirementKeys", jStrings trace.RequirementKeys; "nodeIds", jStrings trace.NodeIds ]

let private predicateName predicate =
    match predicate with
    | ExtensionPredicate -> "extension"

let private roleName role =
    match role with
    | Initial -> "initial"
    | Ordinary -> "ordinary"
    | Terminal -> "terminal"

let private jOperation operation =
    match operation with
    | DefineWorkflow w -> JObject [ "op", JString "defineWorkflow"; "name", JString w.Name; "trace", jTrace w.Trace ]
    | DefineState s ->
        JObject [ "op", JString "defineState"; "name", JString s.Name; "role", JString(roleName s.Role); "trace", jTrace s.Trace ]
    | DefineTransition t ->
        JObject [ "op", JString "defineTransition"
                  "command", JString t.Command
                  "from", JString t.From
                  "to", JString t.To
                  "guards", JArray [ for g in t.Guards -> JObject [ "name", JString g.Name; "predicate", JString(predicateName g.Predicate) ] ]
                  "trace", jTrace t.Trace ]
    | DefineInvariant i ->
        JObject [ "op", JString "defineInvariant"
                  "name", JString i.Name
                  "predicate", JString(predicateName i.Predicate)
                  "holdsIn", jStrings i.HoldsIn
                  "trace", jTrace i.Trace ]
    | DeclarePort p ->
        let kind =
            match p.Kind with
            | CurrentTime -> [ "port", JString "currentTime" ]
            | TransitionEffect command -> [ "port", JString "transitionEffect"; "command", JString command ]
        JObject([ "op", JString "declarePort" ] @ kind @ [ "trace", jTrace p.Trace ])
    | BindCapability c ->
        JObject [ "op", JString "bindCapability"; "command", JString c.Command; "capability", JString c.Capability; "trace", jTrace c.Trace ]
    | DeclareUnresolved u ->
        JObject [ "op", JString "declareUnresolved"; "subject", JString u.Subject; "question", JString u.Question; "trace", jTrace u.Trace ]

let toJson (plan: PlanDraft) =
    JObject [ "schemaVersion", JString plan.SchemaVersion
              "vocabularyVersion", JString plan.VocabularyVersion
              "binding",
              JObject [ "sourceManifestDigest", JString plan.Binding.SourceManifestDigest
                        "blueprintDigest", JString plan.Binding.BlueprintDigest
                        "cohortId", JString plan.Binding.CohortId
                        "decisionIds", jStrings plan.Binding.DecisionIds ]
              "operations", JArray(plan.Operations |> List.map jOperation) ]

let private sha256 (value: string) =
    value |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
    |> fun hex -> "sha256:" + hex.ToLowerInvariant()

let private traceDraft (trace: Trace) : TraceDraft =
    { RequirementKeys = trace.RequirementKeys; NodeIds = trace.NodeIds }

/// The draft a validated plan is equivalent to, with every name in its
/// normalized form and every list in canonical order.
let private canonicalDraft (binding: PlanBinding) (w: Workflow) : PlanDraft =
    let key (name: SemanticName) = name.Key
    { SchemaVersion = binding.SchemaVersion
      VocabularyVersion = binding.VocabularyVersion
      Binding =
        { SourceManifestDigest = binding.SourceManifestDigest
          BlueprintDigest = binding.BlueprintDigest
          CohortId = binding.CohortId
          DecisionIds = binding.DecisionIds }
      Operations =
        [ yield DefineWorkflow { Name = key w.Name; Trace = traceDraft w.Trace }
          for s in w.States -> DefineState { Name = key s.Name; Role = s.Role; Trace = traceDraft s.Trace }
          for t in w.Transitions ->
              DefineTransition
                  { Command = key t.Command; From = key t.From; To = key t.To
                    Guards = t.Guards |> List.map (fun g -> { Name = key g.Name; Predicate = g.Predicate })
                    Trace = traceDraft t.Trace }
          for i in w.Invariants ->
              DefineInvariant
                  { Name = key i.Name; Predicate = i.Predicate; HoldsIn = i.HoldsIn |> List.map key; Trace = traceDraft i.Trace }
          for p in w.Ports ->
              DeclarePort
                  { Kind = (match p.Kind with CurrentTimePort -> CurrentTime | TransitionEffectPort c -> TransitionEffect(key c))
                    Trace = traceDraft p.Trace }
          for c in w.Capabilities ->
              BindCapability { Command = key c.Command; Capability = CapabilityId.value c.Capability; Trace = traceDraft c.Trace }
          for u in w.Unresolved -> DeclareUnresolved { Subject = key u.Subject; Question = u.Question; Trace = traceDraft u.Trace } ] }

// ---------------------------------------------------------------------------
// Validation.
// ---------------------------------------------------------------------------

/// Derived operation identity: what the operation defines. An agent cannot
/// choose it, so two operations defining the same thing collide.
let operationLabel (operation: OperationDraft) =
    let norm raw = SemanticName.create raw |> Result.map SemanticName.key |> Result.defaultValue raw
    match operation with
    | DefineWorkflow w -> "workflow:" + norm w.Name
    | DefineState s -> "state:" + norm s.Name
    | DefineTransition t -> sprintf "transition:%s/%s" (norm t.From) (norm t.Command)
    | DefineInvariant i -> "invariant:" + norm i.Name
    | DeclarePort { Kind = CurrentTime } -> "port:currentTime"
    | DeclarePort { Kind = TransitionEffect command } -> "port:transitionEffect/" + norm command
    | BindCapability c -> sprintf "capability:%s/%s" (norm c.Command) c.Capability
    | DeclareUnresolved u -> "unresolved:" + norm u.Subject

let private traceOf operation : TraceDraft =
    match operation with
    | DefineWorkflow w -> w.Trace
    | DefineState s -> s.Trace
    | DefineTransition t -> t.Trace
    | DefineInvariant i -> i.Trace
    | DeclarePort p -> p.Trace
    | BindCapability c -> c.Trace
    | DeclareUnresolved u -> u.Trace

let private normalizeTrace (trace: TraceDraft) : Trace =
    { RequirementKeys = trace.RequirementKeys |> List.distinct |> List.sort
      NodeIds = trace.NodeIds |> List.distinct |> List.sort }

/// Every raw name an operation carries, with the role it plays.
let private namesOf operation =
    match operation with
    | DefineWorkflow w -> [ w.Name ]
    | DefineState s -> [ s.Name ]
    | DefineTransition t -> [ t.Command; t.From; t.To ] @ (t.Guards |> List.map _.Name)
    | DefineInvariant i -> i.Name :: i.HoldsIn
    | DeclarePort { Kind = CurrentTime } -> []
    | DeclarePort { Kind = TransitionEffect command } -> [ command ]
    | BindCapability c -> [ c.Command ]
    | DeclareUnresolved u -> [ u.Subject ]

let private bindingViolations (view: PreparedCohortView) (preparedBlueprintDigest: string) (plan: PlanDraft) =
    let binding = plan.Binding
    let required = Set.union view.RequiredDecisionIds view.RequiredRationaleIds
    let bound = Set.ofList binding.DecisionIds
    [ if plan.SchemaVersion <> PlanSchemaVersion then UnsupportedPlanSchema plan.SchemaVersion
      if plan.VocabularyVersion <> VocabularyVersion then UnsupportedVocabulary plan.VocabularyVersion
      if binding.SourceManifestDigest <> view.Manifest.Digest then
          StaleSourceManifest(binding.SourceManifestDigest, view.Manifest.Digest)
      if binding.BlueprintDigest <> preparedBlueprintDigest then
          StaleBlueprint(binding.BlueprintDigest, preparedBlueprintDigest)
      if binding.CohortId <> view.CohortId then CohortMismatch(binding.CohortId, view.CohortId)
      yield! Set.difference required bound |> Seq.map UnboundDecision
      yield! Set.difference bound required |> Seq.map UnauthorizedDecision ]

let private traceViolations (view: PreparedCohortView) (operations: OperationDraft list) =
    let nodes = view.Blueprint.Nodes |> List.map (fun n -> n.Id, n) |> Map.ofList
    let perOperation =
        operations
        |> List.collect (fun operation ->
            let label = operationLabel operation
            let trace = traceOf operation
            [ if List.isEmpty trace.RequirementKeys then MissingSourceTrace label
              if List.isEmpty trace.NodeIds then MissingNodeTrace label
              for key in List.distinct trace.RequirementKeys do
                  if not (Set.contains key view.AssignedRequirementKeys) then UnassignedRequirement(label, key)
              for id in List.distinct trace.NodeIds do
                  if not (Set.contains id view.RequiredNodeIds) then UnauthorizedNodeReference(label, id)
                  else
                      let node = nodes[id]
                      let anchored = not (List.isEmpty node.RequirementKeys)
                      if anchored && not (node.RequirementKeys |> List.exists (fun k -> List.contains k trace.RequirementKeys)) then
                          InconsistentTrace(label, id) ])
    let covered = operations |> List.collect (fun o -> (traceOf o).RequirementKeys) |> Set.ofList
    perOperation @ (Set.difference view.AssignedRequirementKeys covered |> Seq.map UncoveredRequirement |> Seq.toList)

let private nameViolations (operations: OperationDraft list) =
    operations
    |> List.collect (fun operation ->
        let label = operationLabel operation
        let names =
            namesOf operation
            |> List.choose (fun raw ->
                match SemanticName.create raw with
                | Error reason -> Some(InvalidName(label, raw, reason))
                | Ok _ -> None)
        let capability =
            match operation with
            | BindCapability c ->
                match CapabilityId.create c.Capability with
                | Error _ -> [ InvalidCapability(label, c.Capability) ]
                | Ok _ -> []
            | _ -> []
        names @ capability)

/// Valid names only; called after nameViolations is empty.
let private name raw =
    match SemanticName.create raw with
    | Ok value -> value
    | Error reason -> invalidOp (sprintf "unchecked name %s: %A" raw reason)

let private structureViolations (operations: OperationDraft list) =
    let workflows = operations |> List.choose (function DefineWorkflow w -> Some w | _ -> None)
    let states = operations |> List.choose (function DefineState s -> Some s | _ -> None)
    let transitions = operations |> List.choose (function DefineTransition t -> Some t | _ -> None)
    let stateKeys = states |> List.map (fun s -> (name s.Name).Key) |> Set.ofList
    let commandKeys = transitions |> List.map (fun t -> (name t.Command).Key) |> Set.ofList
    let roleOf key = states |> List.tryFind (fun s -> (name s.Name).Key = key) |> Option.map _.Role
    let initials = states |> List.filter (fun s -> s.Role = Initial) |> List.map (fun s -> (name s.Name).Key)

    let duplicates =
        operations
        |> List.countBy operationLabel
        |> List.filter (fun (label, count) -> count > 1 && not (label.StartsWith "transition:"))
        |> List.map (fst >> DuplicateDefinition)

    let edges = transitions |> List.map (fun t -> (name t.From).Key, (name t.Command).Key, (name t.To).Key)
    let transitionConflicts =
        edges
        |> List.groupBy (fun (from, command, _) -> from, command)
        |> List.collect (fun ((from, command), group) ->
            match group |> List.map (fun (_, _, target) -> target) |> List.distinct |> List.sort with
            | [ _ ] when List.length group > 1 -> [ DuplicateDefinition(sprintf "transition:%s/%s" from command) ]
            | [ _ ] -> []
            | targets -> [ ConflictingTransition(from, command, targets) ])

    let unknownStates =
        [ for t in transitions do
              let label = operationLabel (DefineTransition t)
              for raw in [ t.From; t.To ] do
                  if not (Set.contains (name raw).Key stateKeys) then UnknownState(label, (name raw).Key)
          for operation in operations do
              match operation with
              | DefineInvariant i ->
                  if List.isEmpty i.HoldsIn then EmptyInvariantScope(operationLabel operation)
                  for raw in i.HoldsIn do
                      if not (Set.contains (name raw).Key stateKeys) then UnknownState(operationLabel operation, (name raw).Key)
              | _ -> () ]

    let unknownCommands =
        operations
        |> List.choose (fun operation ->
            match operation with
            | DeclarePort { Kind = TransitionEffect command } | BindCapability { Command = command } ->
                if Set.contains (name command).Key commandKeys then None
                else Some(UnknownCommand(operationLabel operation, (name command).Key))
            | _ -> None)

    let fromTerminal =
        transitions
        |> List.filter (fun t -> roleOf (name t.From).Key = Some Terminal)
        |> List.map (DefineTransition >> operationLabel >> TransitionFromTerminal)

    let rec reach (seen: Set<string>) =
        let next =
            edges |> List.filter (fun (from, _, _) -> Set.contains from seen) |> List.map (fun (_, _, target) -> target)
            |> Set.ofList |> Set.union seen
        if next = seen then seen else reach next
    let reachable = reach (Set.ofList initials)
    let graph =
        [ for s in states do
              let key = (name s.Name).Key
              if not (List.isEmpty initials) && not (Set.contains key reachable) then UnreachableState key
              if s.Role <> Terminal && not (edges |> List.exists (fun (from, _, _) -> from = key)) then DeadEndState key ]

    let unresolvedQuestions =
        operations
        |> List.choose (fun operation ->
            match operation with
            | DeclareUnresolved u when String.IsNullOrWhiteSpace u.Question -> Some(BlankUnresolvedQuestion(operationLabel operation))
            | _ -> None)

    [ match workflows with
      | [] -> MissingWorkflow
      | [ _ ] -> ()
      | _ -> MultipleWorkflows
      match initials with
      | [] -> MissingInitialState
      | [ _ ] -> ()
      | many -> MultipleInitialStates(List.sort many) ]
    @ duplicates @ transitionConflicts @ unknownStates @ unknownCommands @ fromTerminal @ graph @ unresolvedQuestions

/// The typed workflow, in canonical order. Called only after every check passed.
let private buildWorkflow (operations: OperationDraft list) : Workflow =
    let byKey (f: 'a -> SemanticName) = List.sortBy (fun item -> (f item).Key)
    let workflow = operations |> List.pick (function DefineWorkflow w -> Some w | _ -> None)
    let states =
        operations
        |> List.choose (function DefineState s -> Some { Name = name s.Name; Role = s.Role; Trace = normalizeTrace s.Trace } | _ -> None)
        |> List.sortBy (fun s -> (if s.Role = Initial then 0 else 1), s.Name.Key)
    let transitions =
        operations
        |> List.choose (function
            | DefineTransition t ->
                Some { Command = name t.Command
                       From = name t.From
                       To = name t.To
                       Guards =
                         t.Guards |> List.map (fun g -> { Name = name g.Name; Predicate = g.Predicate })
                         |> List.distinctBy (fun g -> g.Name.Key) |> List.sortBy (fun g -> g.Name.Key)
                       Trace = normalizeTrace t.Trace }
            | _ -> None)
        |> List.sortBy (fun t -> t.From.Key, t.Command.Key)
    { Name = name workflow.Name
      Trace = normalizeTrace workflow.Trace
      States = states
      Commands = transitions |> List.map _.Command |> List.distinctBy _.Key |> List.sortBy _.Key
      Transitions = transitions
      Invariants =
        operations
        |> List.choose (function
            | DefineInvariant i ->
                Some { Name = name i.Name; Predicate = i.Predicate
                       HoldsIn = i.HoldsIn |> List.map name |> List.distinctBy _.Key |> List.sortBy _.Key
                       Trace = normalizeTrace i.Trace }
            | _ -> None)
        |> byKey _.Name
      Ports =
        operations
        |> List.choose (function
            | DeclarePort p ->
                Some { Kind = (match p.Kind with CurrentTime -> CurrentTimePort | TransitionEffect c -> TransitionEffectPort(name c))
                       Trace = normalizeTrace p.Trace }
            | _ -> None)
        |> List.sortBy (fun p -> match p.Kind with CurrentTimePort -> "" | TransitionEffectPort c -> c.Key)
      Capabilities =
        operations
        |> List.choose (function
            | BindCapability c ->
                CapabilityId.create c.Capability
                |> Result.toOption
                |> Option.map (fun id -> { Command = name c.Command; Capability = id; Trace = normalizeTrace c.Trace })
            | _ -> None)
        |> List.sortBy (fun c -> c.Command.Key, CapabilityId.value c.Capability)
      Unresolved =
        operations
        |> List.choose (function DeclareUnresolved u -> Some { Subject = name u.Subject; Question = u.Question; Trace = normalizeTrace u.Trace } | _ -> None)
        |> byKey _.Subject }

/// The canonical JSON rendering of a validated plan: normalized names,
/// canonical list order, sorted object keys.
let canonicalJson (plan: ValidatedPlan) =
    let (ValidatedPlan view) = plan
    canonicalDraft view.Binding view.Workflow |> toJson |> renderCanonical

/// Checks a plan against the exact cohort it claims to implement. The
/// prepared cohort is required, so readiness (source conservation,
/// transitive blockers, external decision authorization) has already been
/// established by `ConstructionIr.prepareCohort`; this function cannot be
/// used to skip it. The blueprint digest is recomputed, not trusted.
let validate (prepared: PreparedCohort) (plan: PlanDraft) : Result<ValidatedPlan, PlanViolation list> =
    let view = preparedCohortView prepared
    let preparedDigest = ConstructionIrWire.blueprintDigest view.Blueprint
    let finish violations = violations |> List.distinct |> List.sortBy (sprintf "%A")
    let early = bindingViolations view preparedDigest plan @ traceViolations view plan.Operations @ nameViolations plan.Operations
    match early with
    | _ :: _ -> Error(finish early)
    | [] ->
        match structureViolations plan.Operations with
        | _ :: _ as structural -> Error(finish structural)
        | [] ->
            let binding =
                { SchemaVersion = plan.SchemaVersion
                  VocabularyVersion = plan.VocabularyVersion
                  SourceManifestDigest = plan.Binding.SourceManifestDigest
                  BlueprintDigest = plan.Binding.BlueprintDigest
                  CohortId = plan.Binding.CohortId
                  DecisionIds = plan.Binding.DecisionIds |> List.distinct |> List.sort }
            let workflow = buildWorkflow plan.Operations
            let digest = canonicalDraft binding workflow |> toJson |> renderCanonical |> sha256
            Ok(ValidatedPlan
                { Binding = binding
                  Workflow = workflow
                  AssignedRequirementKeys = view.AssignedRequirementKeys
                  Digest = digest })

let view (ValidatedPlan view) = view

let digest (ValidatedPlan view) = view.Digest
