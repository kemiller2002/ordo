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
///
/// Three vocabularies are read. Vocabulary 2 differs from vocabulary 1 only in
/// that every transition effect must declare what a failed or unknown effect
/// outcome means for the workflow (`EffectFailureDisposition`). Vocabulary 3
/// adds explicit classification of choices (CONSTRUCT-010): what an unlisted
/// (state, command) pair means, explicit refusals and undecided pairs, and a
/// mandatory decision inventory for every extension. A plan in an earlier
/// vocabulary keeps its exact semantics, canonical form and digest.
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
let VocabularyVersion1 = "ordo.construction-vocabulary/1"

/// Adds the mandatory effect-failure disposition (`onFailure`) to every
/// transition effect port, and the `effect` link from an unresolved
/// obligation to the effect whose failure it leaves open.
[<Literal>]
let VocabularyVersion2 = "ordo.construction-vocabulary/2"

/// Adds `declarePairRule` (mandatory), `defineRefusal`,
/// `declareUndecidedTransition` and `declareExtensionInventory` (mandatory
/// for every guard, invariant and transition effect). A pair the
/// requirements leave undecided is reported as undecided, never refused by
/// omission, and an extension cannot be handed off for implementation
/// without stating which of its decision points are still open.
[<Literal>]
let VocabularyVersion3 = "ordo.construction-vocabulary/3"

/// Vocabulary 3 plus typed refusal codes (`defineRefusalCode`,
/// `assignRefusalCode`) and branching transitions
/// (`defineBranchingTransition`): one command from one state may lead to one
/// of several declared targets, chosen by a handwritten selector over a
/// generated closed union of exactly those targets. Every branching
/// transition needs a selection inventory. CONSTRUCT-010 phase 2, increment 1.
[<Literal>]
let VocabularyVersion4 = "ordo.construction-vocabulary/4"

/// The first vocabulary, under its original name. Code that authors plans
/// with this constant keeps producing vocabulary 1 plans with unchanged
/// meaning; a vocabulary is never upgraded in place.
[<Literal>]
let VocabularyVersion = VocabularyVersion1

/// Every vocabulary this reader and validator accept. Anything else is
/// refused, never upgraded.
let SupportedVocabularyVersions = [ VocabularyVersion1; VocabularyVersion2; VocabularyVersion3; VocabularyVersion4 ]

/// Vocabularies 3 and later classify every choice (pair rule, inventories).
let classifiesChoices (vocabulary: string) = vocabulary = VocabularyVersion3 || vocabulary = VocabularyVersion4

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

/// What a `Failed` or `Unknown` outcome of a transition effect means for the
/// workflow (vocabulary 2). The plan states it, so neither generated nor
/// handwritten code has to guess it.
[<RequireQualifiedAccess>]
type EffectFailureDisposition =
    /// The workflow stays in the transition's source state.
    | Remain
    /// The transition's target state stands; the failure is reported.
    | Proceed
    /// Not decided: generated code picks neither outcome and the host must
    /// handle the failure explicitly. Requires a linked `declareUnresolved`.
    | Unresolved

/// The orchestration ports a workflow may require. The engine chooses their
/// signatures and placement; the plan only states that one is needed.
type PortKindDraft =
    /// The current instant, read once per handled command.
    | CurrentTime
    /// An outbound effect requested after the named command is accepted.
    /// `onFailure` is required in vocabulary 2 and absent in vocabulary 1.
    /// Only an effect port can carry a disposition: `CurrentTime` has no
    /// field for one.
    | TransitionEffect of command: string * onFailure: EffectFailureDisposition option

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
      /// Vocabulary 2: the command whose effect failure this obligation
      /// leaves open. Required for, and only allowed with, an effect whose
      /// disposition is `unresolved`. Absent in vocabulary 1.
      Effect: string option
      Question: string
      Trace: TraceDraft }

/// Vocabulary 3: what a (state, command) pair means when no transition,
/// refusal or undecided declaration names it.
[<RequireQualifiedAccess>]
type UnlistedPairs =
    /// A documented rule, cited by the trace, refuses every unlisted pair.
    | Refused
    /// The requirements do not decide unlisted pairs. Generated code reports
    /// them as undecided and never refuses them by omission.
    | Undecided

type PairRuleDraft = { Unlisted: UnlistedPairs; Trace: TraceDraft }

/// One (state, command) pair, named without a target.
type PairDraft = { InState: string; OnCommand: string }

/// Vocabulary 3: the pair is refused, on the authority of its trace.
type RefusalDraft = { Pair: PairDraft; Trace: TraceDraft }

/// Vocabulary 3: the requirements leave the pair undecided. The question
/// becomes an open obligation; generated code reports the pair as undecided.
type UndecidedTransitionDraft =
    { Pair: PairDraft
      Question: string
      Trace: TraceDraft }

/// The handwritten extension a decision inventory describes.
[<RequireQualifiedAccess>]
type ExtensionRef =
    | Guard of name: string
    | Invariant of name: string
    | Effect of command: string
    /// Vocabulary 4: the handwritten selector of a branching transition.
    | Selection of from: string * command: string

/// Whether one behavioral choice inside an extension is settled.
[<RequireQualifiedAccess>]
type DecisionPointResolution =
    /// Settled by the point's trace: requirement text or an approved decision.
    | Decided
    /// Not settled. The extension cannot be implemented until it is decided.
    | Undecided of question: string

type DecisionPointDraft =
    { Point: string
      Resolution: DecisionPointResolution
      Trace: TraceDraft }

/// Vocabulary 3: every behavioral choice an extension's implementation must
/// make. An empty list asserts that the extension's trace fixes its behavior
/// completely. The inventory is mandatory, so an extension can never be
/// handed off as "implement" with its open choices left unstated.
type ExtensionInventoryDraft =
    { Extension: ExtensionRef
      DecisionPoints: DecisionPointDraft list
      Trace: TraceDraft }

/// Vocabulary 4: a typed refusal code, named in domain words.
type RefusalCodeDraft = { Code: string; Trace: TraceDraft }

/// What a refusal code is assigned to.
[<RequireQualifiedAccess>]
type CodeTarget =
    /// An explicitly refused pair, or one refused by the pair rule.
    | Pair of PairDraft
    /// Every pair the pair rule refuses that has no code of its own.
    | Unlisted
    /// A guard's rejection.
    | Guard of name: string

type CodeAssignmentDraft =
    { Target: CodeTarget
      Code: string
      Trace: TraceDraft }

/// Vocabulary 4: one command from one state leads to one of several targets.
type BranchingTransitionDraft =
    { Pair: PairDraft
      Targets: string list
      Trace: TraceDraft }

type OperationDraft =
    | DefineWorkflow of WorkflowDraft
    | DefineState of StateDraft
    | DefineTransition of TransitionDraft
    | DefineInvariant of InvariantDraft
    | DeclarePort of PortDraft
    | BindCapability of CapabilityDraft
    | DeclareUnresolved of UnresolvedDraft
    | DeclarePairRule of PairRuleDraft
    | DefineRefusal of RefusalDraft
    | DeclareUndecidedTransition of UndecidedTransitionDraft
    | DeclareExtensionInventory of ExtensionInventoryDraft
    | DefineRefusalCode of RefusalCodeDraft
    | AssignRefusalCode of CodeAssignmentDraft
    | DefineBranchingTransition of BranchingTransitionDraft

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
    /// `onFailure` is `Some` exactly when the plan is vocabulary 2.
    | TransitionEffectPort of command: SemanticName * onFailure: EffectFailureDisposition option

type Port = { Kind: PortKind; Trace: Trace }

type CapabilityBinding =
    { Command: SemanticName
      Capability: CapabilityId
      Trace: Trace }

type UnresolvedObligation =
    { Subject: SemanticName
      /// The effect whose failure this obligation leaves open (vocabulary 2).
      Effect: SemanticName option
      Question: string
      Trace: Trace }

type PairDeclaration =
    { InState: SemanticName
      OnCommand: SemanticName
      Trace: Trace }

type UndecidedTransition =
    { InState: SemanticName
      OnCommand: SemanticName
      Question: string
      Trace: Trace }

[<RequireQualifiedAccess>]
type ExtensionTarget =
    | Guard of SemanticName
    | Invariant of SemanticName
    | Effect of SemanticName
    | Selection of from: SemanticName * command: SemanticName

[<RequireQualifiedAccess>]
type CodeSubject =
    | Pair of from: SemanticName * command: SemanticName
    | Unlisted
    | Guard of SemanticName

type CodeAssignment =
    { Target: CodeSubject
      Code: SemanticName
      Trace: Trace }

type BranchingTransition =
    { InState: SemanticName
      OnCommand: SemanticName
      /// Distinct, in canonical order.
      Targets: SemanticName list
      Trace: Trace }

type DecisionPoint =
    { Point: SemanticName
      Resolution: DecisionPointResolution
      Trace: Trace }

type ExtensionInventory =
    { Extension: ExtensionTarget
      DecisionPoints: DecisionPoint list
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
      Unresolved: UnresolvedObligation list
      /// Vocabulary 3 only: the rule for unlisted pairs. `None` in earlier
      /// vocabularies, whose generated code refuses unlisted pairs by omission.
      PairRule: (UnlistedPairs * Trace) option
      Refusals: PairDeclaration list
      UndecidedTransitions: UndecidedTransition list
      Inventories: ExtensionInventory list
      /// Vocabulary 4 only; empty otherwise.
      RefusalCodes: (SemanticName * Trace) list
      CodeAssignments: CodeAssignment list
      BranchingTransitions: BranchingTransition list }

type PlanBinding =
    { SchemaVersion: string
      VocabularyVersion: string
      SourceManifestDigest: string
      BlueprintDigest: string
      CohortId: string
      DecisionIds: string list }

/// How an assigned requirement is reflected in the plan. Coverage is a
/// claim about the plan, not evidence of implementation. A requirement that
/// only an unresolved obligation or the workflow declaration mentions is
/// reported as such, so it cannot pass as constructed.
type RequirementCoverage =
    /// Defined by states, transitions, invariants, ports or capabilities.
    | Constructed of operations: string list
    /// Only open obligations mention it: the behavior is explicitly undecided.
    | UnresolvedOnly of operations: string list
    /// Only the workflow declaration mentions it (a whole-workflow constraint
    /// with no construct of its own).
    | WorkflowLevel

type ValidatedPlanView =
    { Binding: PlanBinding
      Workflow: Workflow
      /// Requirements this cohort implements, as prepared. Prerequisite scope
      /// is not implementation scope.
      AssignedRequirementKeys: Set<string>
      Coverage: Map<string, RequirementCoverage>
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
    | DecisionOutOfScope of decisionId: string
    /// The prepared manifest's digest does not match its own content.
    | UnverifiedSourceManifest of reason: string
    | InvalidName of operation: string * raw: string * reason: NameError
    | InvalidCapability of operation: string * raw: string
    /// A capability requirement is authorization policy; it must cite an
    /// externally authorized decision node rather than the agent's say-so.
    | CapabilityWithoutDecision of operation: string
    /// The cohort node is the delivery container, linked to every assigned
    /// requirement; citing it would justify anything.
    | CohortNodeCited of operation: string * nodeId: string
    | QuestionTooLong of operation: string * length: int
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
    /// A member the plan's vocabulary does not define, for example
    /// `onFailure` in a vocabulary 1 plan.
    | NotInVocabulary of operation: string * memberName: string * vocabulary: string
    /// Vocabulary 2: a transition effect port without a failure disposition.
    | MissingEffectDisposition of operation: string
    /// An `unresolved` disposition with no `declareUnresolved` naming the
    /// effect, so the open question would not become an obligation.
    | UnresolvedEffectWithoutObligation of operation: string
    /// A `declareUnresolved` names an effect that no transition effect port
    /// declares.
    | UnknownEffect of operation: string * effect: string
    /// A `declareUnresolved` names an effect whose disposition is already
    /// decided (`remain` or `proceed`).
    | EffectAlreadyDecided of operation: string * effect: string
    /// Several `declareUnresolved` operations name the same effect.
    | DuplicateEffectObligation of effect: string * operations: string list
    /// A decided disposition (`remain` or `proceed`) settles what a failed
    /// effect means for the workflow; like a capability requirement, it must
    /// cite an externally authorized decision node the cohort requires rather
    /// than the agent's say-so. Without one, only `unresolved` is legal.
    | EffectDispositionWithoutDecision of operation: string
    /// Vocabulary 3: no `declarePairRule`, so unlisted pairs would be decided
    /// by omission.
    | MissingPairRule
    /// A pair named by more than one of: a transition, a refusal, an
    /// undecided declaration.
    | ConflictingPairDisposition of from: string * command: string * declarations: string list
    /// An inventory names an extension the plan does not define.
    | UnknownExtension of operation: string * extension: string
    /// Vocabulary 3: a guard, invariant or transition effect with no
    /// decision inventory.
    | MissingExtensionInventory of extension: string
    /// Vocabulary 4: a code assignment names a code no `defineRefusalCode` defines.
    | UnknownRefusalCode of operation: string * code: string
    /// A code assigned to a pair that is not refused, to unlisted pairs when
    /// the pair rule does not refuse them, or to a guard that does not exist.
    | CodeOnNonRefusal of operation: string
    /// A branching transition with fewer than two distinct targets.
    | BranchingNeedsTwoTargets of operation: string

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

/// The wire spelling of a disposition.
let effectFailureDispositionName disposition =
    match disposition with
    | EffectFailureDisposition.Remain -> "remain"
    | EffectFailureDisposition.Proceed -> "proceed"
    | EffectFailureDisposition.Unresolved -> "unresolved"

let private roleName role =
    match role with
    | Initial -> "initial"
    | Ordinary -> "ordinary"
    | Terminal -> "terminal"

let private unlistedName unlisted =
    match unlisted with
    | UnlistedPairs.Refused -> "refused"
    | UnlistedPairs.Undecided -> "undecided"

let private jExtension extension =
    match extension with
    | ExtensionRef.Guard name -> JObject [ "kind", JString "guard"; "name", JString name ]
    | ExtensionRef.Invariant name -> JObject [ "kind", JString "invariant"; "name", JString name ]
    | ExtensionRef.Effect command -> JObject [ "kind", JString "effect"; "name", JString command ]
    | ExtensionRef.Selection(from, command) -> JObject [ "kind", JString "selection"; "from", JString from; "command", JString command ]

let private jPoint (point: DecisionPointDraft) =
    match point.Resolution with
    | DecisionPointResolution.Decided ->
        JObject [ "point", JString point.Point; "decided", JBool true; "trace", jTrace point.Trace ]
    | DecisionPointResolution.Undecided question ->
        JObject [ "point", JString point.Point; "decided", JBool false; "question", JString question; "trace", jTrace point.Trace ]

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
            | TransitionEffect(command, onFailure) ->
                [ "port", JString "transitionEffect"; "command", JString command ]
                @ (onFailure |> Option.map (fun d -> "onFailure", JString(effectFailureDispositionName d)) |> Option.toList)
        JObject([ "op", JString "declarePort" ] @ kind @ [ "trace", jTrace p.Trace ])
    | BindCapability c ->
        JObject [ "op", JString "bindCapability"; "command", JString c.Command; "capability", JString c.Capability; "trace", jTrace c.Trace ]
    | DeclareUnresolved u ->
        JObject(
            [ "op", JString "declareUnresolved"; "subject", JString u.Subject ]
            @ (u.Effect |> Option.map (fun effect -> "effect", JString effect) |> Option.toList)
            @ [ "question", JString u.Question; "trace", jTrace u.Trace ])
    | DeclarePairRule r ->
        JObject [ "op", JString "declarePairRule"; "unlisted", JString(unlistedName r.Unlisted); "trace", jTrace r.Trace ]
    | DefineRefusal r ->
        JObject [ "op", JString "defineRefusal"; "command", JString r.Pair.OnCommand; "from", JString r.Pair.InState; "trace", jTrace r.Trace ]
    | DeclareUndecidedTransition u ->
        JObject [ "op", JString "declareUndecidedTransition"
                  "command", JString u.Pair.OnCommand
                  "from", JString u.Pair.InState
                  "question", JString u.Question
                  "trace", jTrace u.Trace ]
    | DeclareExtensionInventory i ->
        JObject [ "op", JString "declareExtensionInventory"
                  "extension", jExtension i.Extension
                  "decisionPoints", JArray(i.DecisionPoints |> List.map jPoint)
                  "trace", jTrace i.Trace ]
    | DefineRefusalCode c -> JObject [ "op", JString "defineRefusalCode"; "code", JString c.Code; "trace", jTrace c.Trace ]
    | AssignRefusalCode a ->
        let target =
            match a.Target with
            | CodeTarget.Pair pair -> JObject [ "kind", JString "pair"; "from", JString pair.InState; "command", JString pair.OnCommand ]
            | CodeTarget.Unlisted -> JObject [ "kind", JString "unlisted" ]
            | CodeTarget.Guard name -> JObject [ "kind", JString "guard"; "name", JString name ]
        JObject [ "op", JString "assignRefusalCode"; "target", target; "code", JString a.Code; "trace", jTrace a.Trace ]
    | DefineBranchingTransition b ->
        JObject [ "op", JString "defineBranchingTransition"
                  "command", JString b.Pair.OnCommand
                  "from", JString b.Pair.InState
                  "targets", jStrings b.Targets
                  "trace", jTrace b.Trace ]

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
                  { Kind = (match p.Kind with CurrentTimePort -> CurrentTime | TransitionEffectPort(c, d) -> TransitionEffect(key c, d))
                    Trace = traceDraft p.Trace }
          for c in w.Capabilities ->
              BindCapability { Command = key c.Command; Capability = CapabilityId.value c.Capability; Trace = traceDraft c.Trace }
          for u in w.Unresolved ->
              DeclareUnresolved { Subject = key u.Subject; Effect = u.Effect |> Option.map key; Question = u.Question; Trace = traceDraft u.Trace }
          yield! w.PairRule |> Option.map (fun (unlisted, trace) -> DeclarePairRule { Unlisted = unlisted; Trace = traceDraft trace }) |> Option.toList
          for r in w.Refusals ->
              DefineRefusal { Pair = { InState = key r.InState; OnCommand = key r.OnCommand }; Trace = traceDraft r.Trace }
          for u in w.UndecidedTransitions ->
              DeclareUndecidedTransition
                  { Pair = { InState = key u.InState; OnCommand = key u.OnCommand }; Question = u.Question; Trace = traceDraft u.Trace }
          for i in w.Inventories ->
              DeclareExtensionInventory
                  { Extension =
                      (match i.Extension with
                       | ExtensionTarget.Guard n -> ExtensionRef.Guard(key n)
                       | ExtensionTarget.Invariant n -> ExtensionRef.Invariant(key n)
                       | ExtensionTarget.Effect n -> ExtensionRef.Effect(key n)
                       | ExtensionTarget.Selection(f, c) -> ExtensionRef.Selection(key f, key c))
                    DecisionPoints =
                      i.DecisionPoints |> List.map (fun p -> { DecisionPointDraft.Point = key p.Point; Resolution = p.Resolution; Trace = traceDraft p.Trace })
                    Trace = traceDraft i.Trace }
          for code, trace in w.RefusalCodes -> DefineRefusalCode { Code = key code; Trace = traceDraft trace }
          for a in w.CodeAssignments ->
              AssignRefusalCode
                  { Target =
                      (match a.Target with
                       | CodeSubject.Pair(f, c) -> CodeTarget.Pair { InState = key f; OnCommand = key c }
                       | CodeSubject.Unlisted -> CodeTarget.Unlisted
                       | CodeSubject.Guard g -> CodeTarget.Guard(key g))
                    Code = key a.Code
                    Trace = traceDraft a.Trace }
          for b in w.BranchingTransitions ->
              DefineBranchingTransition
                  { Pair = { InState = key b.InState; OnCommand = key b.OnCommand }
                    Targets = b.Targets |> List.map key
                    Trace = traceDraft b.Trace } ] }

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
    | DeclarePort { Kind = TransitionEffect(command, _) } -> "port:transitionEffect/" + norm command
    | BindCapability c -> sprintf "capability:%s/%s" (norm c.Command) c.Capability
    | DeclareUnresolved u -> "unresolved:" + norm u.Subject
    | DeclarePairRule _ -> "pairRule"
    | DefineRefusal r -> sprintf "refusal:%s/%s" (norm r.Pair.InState) (norm r.Pair.OnCommand)
    | DeclareUndecidedTransition u -> sprintf "undecided:%s/%s" (norm u.Pair.InState) (norm u.Pair.OnCommand)
    | DeclareExtensionInventory i ->
        match i.Extension with
        | ExtensionRef.Guard n -> "inventory:guard/" + norm n
        | ExtensionRef.Invariant n -> "inventory:invariant/" + norm n
        | ExtensionRef.Effect n -> "inventory:effect/" + norm n
        | ExtensionRef.Selection(f, c) -> sprintf "inventory:selection/%s/%s" (norm f) (norm c)
    | DefineRefusalCode c -> "code:" + norm c.Code
    | AssignRefusalCode a ->
        match a.Target with
        | CodeTarget.Pair pair -> sprintf "codeFor:pair/%s/%s" (norm pair.InState) (norm pair.OnCommand)
        | CodeTarget.Unlisted -> "codeFor:unlisted"
        | CodeTarget.Guard g -> "codeFor:guard/" + norm g
    | DefineBranchingTransition b -> sprintf "branching:%s/%s" (norm b.Pair.InState) (norm b.Pair.OnCommand)

let private traceOf operation : TraceDraft =
    match operation with
    | DefineWorkflow w -> w.Trace
    | DefineState s -> s.Trace
    | DefineTransition t -> t.Trace
    | DefineInvariant i -> i.Trace
    | DeclarePort p -> p.Trace
    | BindCapability c -> c.Trace
    | DeclareUnresolved u -> u.Trace
    | DeclarePairRule r -> r.Trace
    | DefineRefusal r -> r.Trace
    | DeclareUndecidedTransition u -> u.Trace
    | DeclareExtensionInventory i -> i.Trace
    | DefineRefusalCode c -> c.Trace
    | AssignRefusalCode a -> a.Trace
    | DefineBranchingTransition b -> b.Trace

/// Every trace an operation carries, labelled: its own and, for an
/// inventory, one per decision point. Each is checked like an operation's.
let private tracesOf operation =
    let label = operationLabel operation
    (label, traceOf operation)
    :: (match operation with
        | DeclareExtensionInventory i ->
            i.DecisionPoints |> List.map (fun p -> sprintf "%s/point:%s" label p.Point, p.Trace)
        | _ -> [])

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
    | DeclarePort { Kind = TransitionEffect(command, _) } -> [ command ]
    | BindCapability c -> [ c.Command ]
    | DeclareUnresolved u -> u.Subject :: Option.toList u.Effect
    | DeclarePairRule _ -> []
    | DefineRefusal r -> [ r.Pair.InState; r.Pair.OnCommand ]
    | DeclareUndecidedTransition u -> [ u.Pair.InState; u.Pair.OnCommand ]
    | DeclareExtensionInventory i ->
        (match i.Extension with
         | ExtensionRef.Guard n | ExtensionRef.Invariant n | ExtensionRef.Effect n -> [ n ]
         | ExtensionRef.Selection(f, c) -> [ f; c ])
        @ (i.DecisionPoints |> List.map _.Point)
    | DefineRefusalCode c -> [ c.Code ]
    | AssignRefusalCode a ->
        a.Code
        :: (match a.Target with
            | CodeTarget.Pair pair -> [ pair.InState; pair.OnCommand ]
            | CodeTarget.Unlisted -> []
            | CodeTarget.Guard g -> [ g ])
    | DefineBranchingTransition b -> b.Pair.InState :: b.Pair.OnCommand :: b.Targets

let private bindingViolations (view: PreparedCohortView) (preparedBlueprintDigest: string) (plan: PlanDraft) =
    let binding = plan.Binding
    let required = Set.union view.RequiredDecisionIds view.RequiredRationaleIds
    let bound = Set.ofList binding.DecisionIds
    [ if plan.SchemaVersion <> PlanSchemaVersion then UnsupportedPlanSchema plan.SchemaVersion
      if not (List.contains plan.VocabularyVersion SupportedVocabularyVersions) then UnsupportedVocabulary plan.VocabularyVersion
      if binding.SourceManifestDigest <> view.Manifest.Digest then
          StaleSourceManifest(binding.SourceManifestDigest, view.Manifest.Digest)
      if binding.BlueprintDigest <> preparedBlueprintDigest then
          StaleBlueprint(binding.BlueprintDigest, preparedBlueprintDigest)
      if binding.CohortId <> view.CohortId then CohortMismatch(binding.CohortId, view.CohortId)
      yield! Set.difference required bound |> Seq.map UnboundDecision
      yield! Set.difference bound required |> Seq.map DecisionOutOfScope ]

let private traceViolations (view: PreparedCohortView) (operations: OperationDraft list) =
    let nodes = view.Blueprint.Nodes |> List.map (fun n -> n.Id, n) |> Map.ofList
    let checkTrace label (trace: TraceDraft) =
        [ if List.isEmpty trace.RequirementKeys then MissingSourceTrace label
          if List.isEmpty trace.NodeIds then MissingNodeTrace label
          for key in List.distinct trace.RequirementKeys do
              if not (Set.contains key view.AssignedRequirementKeys) then UnassignedRequirement(label, key)
          for id in List.distinct trace.NodeIds do
              if not (Set.contains id view.RequiredNodeIds) then UnauthorizedNodeReference(label, id)
              elif nodes[id].Kind = Cohort then CohortNodeCited(label, id)
              else
                  let node = nodes[id]
                  let anchored = not (List.isEmpty node.RequirementKeys)
                  if anchored && not (node.RequirementKeys |> List.exists (fun k -> List.contains k trace.RequirementKeys)) then
                      InconsistentTrace(label, id) ]
    let perOperation =
        operations
        |> List.collect (fun operation ->
            let label = operationLabel operation
            let trace = traceOf operation
            let citesRequiredDecision = trace.NodeIds |> List.exists (fun id -> Set.contains id view.RequiredDecisionIds)
            (tracesOf operation |> List.collect (fun (l, t) -> checkTrace l t))
            @ [ match operation with
                | BindCapability _ when not citesRequiredDecision -> CapabilityWithoutDecision label
                | DeclarePort { Kind = TransitionEffect(_, Some(EffectFailureDisposition.Remain | EffectFailureDisposition.Proceed)) }
                    when not citesRequiredDecision -> EffectDispositionWithoutDecision label
                | _ -> () ])
    let covered = operations |> List.collect (fun o -> (traceOf o).RequirementKeys) |> Set.ofList
    perOperation @ (Set.difference view.AssignedRequirementKeys covered |> Seq.map UncoveredRequirement |> Seq.toList)

[<Literal>]
let MaxQuestionLength = 1000

/// Capability identifiers in a plan are dotted/colon-separated ASCII words,
/// narrower than `CapabilityId` itself, so no generated literal can carry
/// quotes, whitespace or operators.
let private isCapabilityText (raw: string) =
    not (String.IsNullOrEmpty raw)
    && raw.Length <= 128
    && Char.IsAsciiLetter raw[0]
    && raw |> Seq.forall (fun ch -> Char.IsAsciiLetterOrDigit ch || ch = '.' || ch = ':' || ch = '-' || ch = '_')
    && (CapabilityId.create raw |> Result.isOk)

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
            | BindCapability c when not (isCapabilityText c.Capability) -> [ InvalidCapability(label, c.Capability) ]
            | DeclareUnresolved u when u.Question.Length > MaxQuestionLength -> [ QuestionTooLong(label, u.Question.Length) ]
            | DeclareUndecidedTransition u when u.Question.Length > MaxQuestionLength -> [ QuestionTooLong(label, u.Question.Length) ]
            | DeclareExtensionInventory i ->
                i.DecisionPoints
                |> List.choose (fun p ->
                    match p.Resolution with
                    | DecisionPointResolution.Undecided q when q.Length > MaxQuestionLength ->
                        Some(QuestionTooLong(sprintf "%s/point:%s" label p.Point, q.Length))
                    | _ -> None)
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
    let pairCommands =
        operations
        |> List.choose (function
            | DefineRefusal r -> Some r.Pair.OnCommand
            | DeclareUndecidedTransition u -> Some u.Pair.OnCommand
            | DefineBranchingTransition b -> Some b.Pair.OnCommand
            | _ -> None)
    let commandKeys = (transitions |> List.map _.Command) @ pairCommands |> List.map (fun c -> (name c).Key) |> Set.ofList
    let roles = states |> List.map (fun s -> (name s.Name).Key, s.Role) |> Map.ofList
    let roleOf key = Map.tryFind key roles
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
              | DefineRefusal { Pair = pair } | DeclareUndecidedTransition { Pair = pair } ->
                  if not (Set.contains (name pair.InState).Key stateKeys) then UnknownState(operationLabel operation, (name pair.InState).Key)
              | DefineBranchingTransition b ->
                  for raw in b.Pair.InState :: b.Targets do
                      if not (Set.contains (name raw).Key stateKeys) then UnknownState(operationLabel operation, (name raw).Key)
              | _ -> () ]

    let unknownCommands =
        operations
        |> List.choose (fun operation ->
            match operation with
            | DeclarePort { Kind = TransitionEffect(command, _) } | BindCapability { Command = command } ->
                if Set.contains (name command).Key commandKeys then None
                else Some(UnknownCommand(operationLabel operation, (name command).Key))
            | _ -> None)

    let fromTerminal =
        (transitions
         |> List.filter (fun t -> roleOf (name t.From).Key = Some Terminal)
         |> List.map (DefineTransition >> operationLabel >> TransitionFromTerminal))
        // A terminal state decides every command: it leaves nothing undecided.
        @ (operations
           |> List.choose (function
               | DeclareUndecidedTransition u as operation when roleOf (name u.Pair.InState).Key = Some Terminal ->
                   Some(TransitionFromTerminal(operationLabel operation))
               | DefineBranchingTransition b as operation when roleOf (name b.Pair.InState).Key = Some Terminal ->
                   Some(TransitionFromTerminal(operationLabel operation))
               | _ -> None))

    // Reachability follows every possible target of a branching transition.
    let branchingEdges =
        operations
        |> List.collect (function
            | DefineBranchingTransition b -> b.Targets |> List.map (fun t -> (name b.Pair.InState).Key, (name b.Pair.OnCommand).Key, (name t).Key)
            | _ -> [])
    let successors =
        edges @ branchingEdges
        |> List.groupBy (fun (from, _, _) -> from)
        |> List.map (fun (from, group) -> from, group |> List.map (fun (_, _, target) -> target) |> Set.ofList)
        |> Map.ofList
    let next key = Map.tryFind key successors |> Option.defaultValue Set.empty
    let rec reach (seen: Set<string>) (frontier: string list) =
        match frontier with
        | [] -> seen
        | key :: rest ->
            let fresh = Set.difference (next key) seen
            reach (Set.union seen fresh) (Set.toList fresh @ rest)
    let reachable = reach (Set.ofList initials) initials
    let graph =
        [ for s in states do
              let key = (name s.Name).Key
              if not (List.isEmpty initials) && not (Set.contains key reachable) then UnreachableState key
              // A self-loop alone does not leave the state.
              if s.Role <> Terminal && Set.isEmpty (Set.remove key (next key)) then DeadEndState key ]

    let duplicateGuards =
        transitions
        |> List.collect (fun t ->
            t.Guards
            |> List.countBy (fun g -> (name g.Name).Key)
            |> List.filter (fun (_, count) -> count > 1)
            |> List.map (fun (guard, _) -> DuplicateDefinition(sprintf "%s/guard:%s" (operationLabel (DefineTransition t)) guard)))

    let unresolvedQuestions =
        operations
        |> List.choose (fun operation ->
            match operation with
            | DeclareUnresolved u when String.IsNullOrWhiteSpace u.Question -> Some(BlankUnresolvedQuestion(operationLabel operation))
            | DeclareUndecidedTransition u when String.IsNullOrWhiteSpace u.Question -> Some(BlankUnresolvedQuestion(operationLabel operation))
            | _ -> None)

    [ match workflows with
      | [] -> MissingWorkflow
      | [ _ ] -> ()
      | _ -> MultipleWorkflows
      match initials with
      | [] -> MissingInitialState
      | [ _ ] -> ()
      | many -> MultipleInitialStates(List.sort many) ]
    @ duplicates @ duplicateGuards @ transitionConflicts @ unknownStates @ unknownCommands @ fromTerminal @ graph @ unresolvedQuestions

/// Members a plan's vocabulary does not define. Vocabulary 1 has no failure
/// disposition and no effect link; a vocabulary 1 plan that carries one is
/// refused, never read as vocabulary 2.
let private vocabularyViolations (plan: PlanDraft) =
    let version = plan.VocabularyVersion
    let notIn operation member' = NotInVocabulary(operationLabel operation, member', version)
    plan.Operations
    |> List.collect (fun operation ->
        match operation with
        | DeclarePort { Kind = TransitionEffect(_, Some _) } when version = VocabularyVersion1 -> [ notIn operation "onFailure" ]
        | DeclareUnresolved { Effect = Some _ } when version = VocabularyVersion1 -> [ notIn operation "effect" ]
        | DeclarePairRule _ when not (classifiesChoices version) -> [ notIn operation "declarePairRule" ]
        | DefineRefusal _ when not (classifiesChoices version) -> [ notIn operation "defineRefusal" ]
        | DeclareUndecidedTransition _ when not (classifiesChoices version) -> [ notIn operation "declareUndecidedTransition" ]
        | DeclareExtensionInventory _ when not (classifiesChoices version) -> [ notIn operation "declareExtensionInventory" ]
        | DeclareExtensionInventory { Extension = ExtensionRef.Selection _ } when version <> VocabularyVersion4 -> [ notIn operation "selection" ]
        | DefineRefusalCode _ when version <> VocabularyVersion4 -> [ notIn operation "defineRefusalCode" ]
        | AssignRefusalCode _ when version <> VocabularyVersion4 -> [ notIn operation "assignRefusalCode" ]
        | DefineBranchingTransition _ when version <> VocabularyVersion4 -> [ notIn operation "defineBranchingTransition" ]
        | _ -> [])

/// Vocabulary 3: every choice is stated. Exactly one pair rule; a pair is
/// named by at most one of a transition, a refusal and an undecided
/// declaration; every guard, invariant and transition effect has exactly one
/// decision inventory, naming an extension the plan defines, whose points
/// are distinct and whose undecided points ask a question.
let private classificationViolations (vocabulary: string) (operations: OperationDraft list) =
    if not (classifiesChoices vocabulary) then []
    else
        let rules = operations |> List.filter (function DeclarePairRule _ -> true | _ -> false)
        let declarations =
            operations
            |> List.choose (fun operation ->
                match operation with
                | DefineTransition t -> Some(((name t.From).Key, (name t.Command).Key), "transition")
                | DefineRefusal r -> Some(((name r.Pair.InState).Key, (name r.Pair.OnCommand).Key), "refusal")
                | DeclareUndecidedTransition u -> Some(((name u.Pair.InState).Key, (name u.Pair.OnCommand).Key), "undecided")
                | DefineBranchingTransition b -> Some(((name b.Pair.InState).Key, (name b.Pair.OnCommand).Key), "branching")
                | _ -> None)
        let pairConflicts =
            declarations
            |> List.groupBy fst
            |> List.choose (fun ((from, command), group) ->
                match group |> List.map snd |> List.distinct |> List.sort with
                | [ _ ] -> None
                | kinds -> Some(ConflictingPairDisposition(from, command, kinds)))
        let extensionLabel extension =
            match extension with
            | ExtensionRef.Guard n -> "guard/" + (name n).Key
            | ExtensionRef.Invariant n -> "invariant/" + (name n).Key
            | ExtensionRef.Effect n -> "effect/" + (name n).Key
            | ExtensionRef.Selection(f, c) -> sprintf "selection/%s/%s" (name f).Key (name c).Key
        let defined =
            operations
            |> List.collect (fun operation ->
                match operation with
                | DefineTransition t -> t.Guards |> List.map (fun g -> extensionLabel (ExtensionRef.Guard g.Name))
                | DefineInvariant i -> [ extensionLabel (ExtensionRef.Invariant i.Name) ]
                | DeclarePort { Kind = TransitionEffect(command, _) } -> [ extensionLabel (ExtensionRef.Effect command) ]
                | DefineBranchingTransition b -> [ extensionLabel (ExtensionRef.Selection(b.Pair.InState, b.Pair.OnCommand)) ]
                | _ -> [])
            |> Set.ofList
        let inventories =
            operations |> List.choose (function DeclareExtensionInventory i as op -> Some(op, i) | _ -> None)
        let inventoried = inventories |> List.map (fun (_, i) -> extensionLabel i.Extension) |> Set.ofList
        let inventoryViolations =
            inventories
            |> List.collect (fun (operation, i) ->
                let label = operationLabel operation
                [ if not (Set.contains (extensionLabel i.Extension) defined) then UnknownExtension(label, extensionLabel i.Extension)
                  yield!
                      i.DecisionPoints
                      |> List.countBy (fun p -> (name p.Point).Key)
                      |> List.filter (fun (_, n) -> n > 1)
                      |> List.map (fun (point, _) -> DuplicateDefinition(sprintf "%s/point:%s" label point))
                  for p in i.DecisionPoints do
                      match p.Resolution with
                      | DecisionPointResolution.Undecided q when String.IsNullOrWhiteSpace q ->
                          BlankUnresolvedQuestion(sprintf "%s/point:%s" label (name p.Point).Key)
                      | _ -> () ])
        [ if List.isEmpty rules then MissingPairRule ]
        @ pairConflicts
        @ (Set.difference defined inventoried |> Seq.map MissingExtensionInventory |> Seq.toList)
        @ inventoryViolations

/// Vocabulary 4: refusal codes are defined before they are assigned; a code
/// goes only to something that refuses; a branching transition has at least
/// two distinct targets.
let private codeViolations (vocabulary: string) (operations: OperationDraft list) =
    if vocabulary <> VocabularyVersion4 then []
    else
        let key raw = (name raw).Key
        let codes = operations |> List.choose (function DefineRefusalCode c -> Some(key c.Code) | _ -> None) |> Set.ofList
        let states = operations |> List.choose (function DefineState s -> Some(key s.Name, s.Role) | _ -> None) |> Map.ofList
        let rule = operations |> List.tryPick (function DeclarePairRule r -> Some r.Unlisted | _ -> None)
        let named from command =
            operations
            |> List.exists (fun operation ->
                match operation with
                | DefineTransition t -> key t.From = from && key t.Command = command
                | DefineBranchingTransition { Pair = p } | DeclareUndecidedTransition { Pair = p } ->
                    key p.InState = from && key p.OnCommand = command
                | _ -> false)
        let explicitlyRefused from command =
            operations |> List.exists (function DefineRefusal r -> key r.Pair.InState = from && key r.Pair.OnCommand = command | _ -> false)
        let guards =
            operations |> List.collect (function DefineTransition t -> t.Guards |> List.map (fun g -> key g.Name) | _ -> []) |> Set.ofList
        let assignments =
            operations
            |> List.choose (function
                | AssignRefusalCode a as op ->
                    let label = operationLabel op
                    let refuses =
                        match a.Target with
                        | CodeTarget.Pair pair ->
                            let from, command = key pair.InState, key pair.OnCommand
                            explicitlyRefused from command
                            || (not (named from command)
                                && (Map.tryFind from states = Some Terminal || rule = Some UnlistedPairs.Refused))
                        | CodeTarget.Unlisted -> rule = Some UnlistedPairs.Refused
                        | CodeTarget.Guard g -> Set.contains (key g) guards
                    Some [ if not (Set.contains (key a.Code) codes) then UnknownRefusalCode(label, key a.Code)
                           if not refuses then CodeOnNonRefusal label ]
                | DefineBranchingTransition b as op ->
                    if (b.Targets |> List.map key |> List.distinct |> List.length) < 2 then Some [ BranchingNeedsTwoTargets(operationLabel op) ]
                    else None
                | _ -> None)
            |> List.concat
        assignments

/// Vocabulary 2: every transition effect declares its failure disposition,
/// and an `unresolved` disposition is linked, by effect, to exactly one
/// `declareUnresolved`, so the open question becomes an obligation. A link
/// is valid only to an effect port whose disposition is `unresolved`.
let private effectDispositionViolations (vocabulary: string) (operations: OperationDraft list) =
    if vocabulary = VocabularyVersion1 then []
    else
        let ports =
            operations
            |> List.choose (fun operation ->
                match operation with
                | DeclarePort { Kind = TransitionEffect(command, disposition) } -> Some((name command).Key, operationLabel operation, disposition)
                | _ -> None)
        let links =
            operations
            |> List.choose (fun operation ->
                match operation with
                | DeclareUnresolved { Effect = Some effect } -> Some((name effect).Key, operationLabel operation)
                | _ -> None)
        let isLinked key = links |> List.exists (fun (effect, _) -> effect = key)
        let dispositions =
            [ for key, label, disposition in ports do
                  match disposition with
                  | None -> MissingEffectDisposition label
                  | Some EffectFailureDisposition.Unresolved when not (isLinked key) -> UnresolvedEffectWithoutObligation label
                  | Some _ -> () ]
        let decided disposition =
            match disposition with
            | Some EffectFailureDisposition.Remain | Some EffectFailureDisposition.Proceed -> true
            | Some EffectFailureDisposition.Unresolved | None -> false
        let badLinks =
            [ for key, label in links do
                  match ports |> List.filter (fun (port, _, _) -> port = key) with
                  | [] -> UnknownEffect(label, key)
                  | matching when matching |> List.exists (fun (_, _, disposition) -> decided disposition) -> EffectAlreadyDecided(label, key)
                  | _ -> () ]
        let duplicateLinks =
            links
            |> List.groupBy fst
            |> List.filter (fun (_, group) -> List.length group > 1)
            |> List.map (fun (key, group) -> DuplicateEffectObligation(key, group |> List.map snd |> List.sort))
        dispositions @ badLinks @ duplicateLinks

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
                         |> List.sortBy (fun g -> g.Name.Key)
                       Trace = normalizeTrace t.Trace }
            | _ -> None)
        |> List.sortBy (fun t -> t.From.Key, t.Command.Key)
    { Name = name workflow.Name
      Trace = normalizeTrace workflow.Trace
      States = states
      Commands =
        (transitions |> List.map _.Command)
        @ (operations
           |> List.choose (function
               | DefineRefusal r -> Some(name r.Pair.OnCommand)
               | DeclareUndecidedTransition u -> Some(name u.Pair.OnCommand)
               | DefineBranchingTransition b -> Some(name b.Pair.OnCommand)
               | _ -> None))
        |> List.distinctBy _.Key
        |> List.sortBy _.Key
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
                Some { Kind = (match p.Kind with CurrentTime -> CurrentTimePort | TransitionEffect(c, d) -> TransitionEffectPort(name c, d))
                       Trace = normalizeTrace p.Trace }
            | _ -> None)
        |> List.sortBy (fun p -> match p.Kind with CurrentTimePort -> "" | TransitionEffectPort(c, _) -> c.Key)
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
        |> List.choose (function
            | DeclareUnresolved u ->
                Some { Subject = name u.Subject; Effect = u.Effect |> Option.map name; Question = u.Question; Trace = normalizeTrace u.Trace }
            | _ -> None)
        |> byKey _.Subject
      PairRule = operations |> List.tryPick (function DeclarePairRule r -> Some(r.Unlisted, normalizeTrace r.Trace) | _ -> None)
      Refusals =
        operations
        |> List.choose (function
            | DefineRefusal r -> Some { PairDeclaration.InState = name r.Pair.InState; OnCommand = name r.Pair.OnCommand; Trace = normalizeTrace r.Trace }
            | _ -> None)
        |> List.sortBy (fun r -> r.InState.Key, r.OnCommand.Key)
      UndecidedTransitions =
        operations
        |> List.choose (function
            | DeclareUndecidedTransition u ->
                Some { UndecidedTransition.InState = name u.Pair.InState; OnCommand = name u.Pair.OnCommand
                       Question = u.Question; Trace = normalizeTrace u.Trace }
            | _ -> None)
        |> List.sortBy (fun u -> u.InState.Key, u.OnCommand.Key)
      Inventories =
        operations
        |> List.choose (function
            | DeclareExtensionInventory i ->
                Some { Extension =
                         (match i.Extension with
                          | ExtensionRef.Guard n -> ExtensionTarget.Guard(name n)
                          | ExtensionRef.Invariant n -> ExtensionTarget.Invariant(name n)
                          | ExtensionRef.Effect n -> ExtensionTarget.Effect(name n)
                          | ExtensionRef.Selection(f, c) -> ExtensionTarget.Selection(name f, name c))
                       DecisionPoints =
                         i.DecisionPoints
                         |> List.map (fun p -> { Point = name p.Point; Resolution = p.Resolution; Trace = normalizeTrace p.Trace })
                         |> List.sortBy (fun p -> p.Point.Key)
                       Trace = normalizeTrace i.Trace }
            | _ -> None)
        |> List.sortBy (fun i ->
            match i.Extension with
            | ExtensionTarget.Guard n -> 0, n.Key
            | ExtensionTarget.Invariant n -> 1, n.Key
            | ExtensionTarget.Effect n -> 2, n.Key
            | ExtensionTarget.Selection(f, c) -> 3, f.Key + "/" + c.Key)
      RefusalCodes =
        operations
        |> List.choose (function DefineRefusalCode c -> Some(name c.Code, normalizeTrace c.Trace) | _ -> None)
        |> List.sortBy (fun (n, _) -> n.Key)
      CodeAssignments =
        operations
        |> List.choose (function
            | AssignRefusalCode a ->
                Some { CodeAssignment.Target =
                         (match a.Target with
                          | CodeTarget.Pair pair -> CodeSubject.Pair(name pair.InState, name pair.OnCommand)
                          | CodeTarget.Unlisted -> CodeSubject.Unlisted
                          | CodeTarget.Guard g -> CodeSubject.Guard(name g))
                       Code = name a.Code
                       Trace = normalizeTrace a.Trace }
            | _ -> None)
        |> List.sortBy (fun a ->
            match a.Target with
            | CodeSubject.Pair(f, c) -> 0, f.Key + "/" + c.Key
            | CodeSubject.Unlisted -> 1, ""
            | CodeSubject.Guard g -> 2, g.Key)
      BranchingTransitions =
        operations
        |> List.choose (function
            | DefineBranchingTransition b ->
                Some { BranchingTransition.InState = name b.Pair.InState
                       OnCommand = name b.Pair.OnCommand
                       Targets = b.Targets |> List.map name |> List.distinctBy _.Key |> List.sortBy _.Key
                       Trace = normalizeTrace b.Trace }
            | _ -> None)
        |> List.sortBy (fun b -> b.InState.Key, b.OnCommand.Key) }

let private coverage (assigned: Set<string>) (operations: OperationDraft list) =
    let tracing key =
        operations |> List.filter (fun o -> List.contains key (traceOf o).RequirementKeys)
    let labels ops = ops |> List.map operationLabel |> List.distinct |> List.sort
    assigned
    |> Seq.map (fun key ->
        let ops = tracing key
        let undecided operation =
            match operation with
            | DeclareUnresolved _ | DeclareUndecidedTransition _ -> true
            | DeclareExtensionInventory i ->
                i.DecisionPoints |> List.exists (fun p -> p.Resolution <> DecisionPointResolution.Decided)
            | _ -> false
        // A pair rule or a fully decided inventory states how something is
        // decided; it constructs nothing by itself.
        let nonConstructing operation =
            match operation with
            | DefineWorkflow _ | DeclarePairRule _ | DeclareExtensionInventory _ | DefineRefusalCode _ | AssignRefusalCode _ -> true
            | _ -> undecided operation
        let constructs = ops |> List.filter (nonConstructing >> not)
        let unresolved = ops |> List.filter undecided
        key,
        match constructs, unresolved with
        | _ :: _, _ -> Constructed(labels constructs)
        | [], _ :: _ -> UnresolvedOnly(labels unresolved)
        | [], [] -> WorkflowLevel)
    |> Map.ofSeq

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
    let manifestCheck =
        match ConstructionIrWire.verifyManifestDigest view.Manifest with
        | Error reason -> [ UnverifiedSourceManifest reason ]
        | Ok () -> []
    let early =
        manifestCheck @ bindingViolations view preparedDigest plan @ vocabularyViolations plan
        @ traceViolations view plan.Operations @ nameViolations plan.Operations
    match early with
    | _ :: _ -> Error(finish early)
    | [] ->
        match
            structureViolations plan.Operations
            @ effectDispositionViolations plan.VocabularyVersion plan.Operations
            @ classificationViolations plan.VocabularyVersion plan.Operations
            @ codeViolations plan.VocabularyVersion plan.Operations
        with
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
                  Coverage = coverage view.AssignedRequirementKeys plan.Operations
                  Digest = digest })

let view (ValidatedPlan view) = view

let digest (ValidatedPlan view) = view.Digest

/// Whether a validated plan was formed against exactly this prepared cohort.
/// Hosts that hold both use this instead of comparing fields by hand.
let isBoundTo (prepared: PreparedCohort) (ValidatedPlan plan) =
    let cohort = preparedCohortView prepared
    plan.Binding.SourceManifestDigest = cohort.Manifest.Digest
    && plan.Binding.BlueprintDigest = ConstructionIrWire.blueprintDigest cohort.Blueprint
    && plan.Binding.CohortId = cohort.CohortId
    && Set.ofList plan.Binding.DecisionIds = Set.union cohort.RequiredDecisionIds cohort.RequiredRationaleIds

/// The plan's operations in canonical form (normalized names, canonical
/// order). Consumers derive labels with `operationLabel` and traces from
/// these, so labels and traces have one authority.
let canonicalOperations (ValidatedPlan plan) = (canonicalDraft plan.Binding plan.Workflow).Operations

/// The trace an operation carries.
let operationTrace (operation: OperationDraft) = traceOf operation
