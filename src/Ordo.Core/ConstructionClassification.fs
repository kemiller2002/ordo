/// Decision classification (CONSTRUCT-010, phase 4). Every semantic choice a
/// validated plan makes, or leaves open, is classified as exactly one of:
///
/// - required: fixed by the requirement text its trace cites;
/// - approved: fixed by an externally authorized decision its trace cites;
/// - implied: fixed by a documented rule (a cited pair rule, or a terminal
///   state's role);
/// - unresolved: stated as open (an undecided pair, an undecided effect
///   failure, an undecided extension decision point, an open question);
/// - unsupported: decided by omission, which the plan's vocabulary cannot
///   avoid (vocabularies 1 and 2 refuse unlisted pairs and cannot inventory an
///   extension's choices).
///
/// Only the first three may be generated as completed construction. A
/// `CompletePlan` exists only when no choice is unresolved or unsupported, so
/// an open decision cannot be passed off as finished work. Classification is
/// a report about the plan, not authority to apply it.
module Ordo.Core.ConstructionClassification

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.ConstructionPlan

[<Literal>]
let ClassificationSchemaVersion = "ordo.choice-classification/1"

[<RequireQualifiedAccess>]
type ChoiceClass =
    | Required of requirementKeys: string list
    | Approved of decisionIds: string list
    | Implied of rule: string * requirementKeys: string list
    | Unresolved of question: string
    | Unsupported of reason: string

/// What kind of semantic choice a subject is.
[<RequireQualifiedAccess>]
type ChoiceKind =
    | State
    /// What one command does in one state: a transition, a refusal, or open.
    | Pair
    | Invariant
    | Port
    | EffectFailure
    | Capability
    /// The behavior a handwritten extension implements, beyond its decision points.
    | ExtensionBehavior
    /// One choice inside an extension's behavior.
    | ExtensionDecision
    /// An open question the plan states explicitly.
    | OpenQuestion
    /// Which typed code a refusal reports (vocabulary 4).
    | RefusalCode

type ClassifiedChoice =
    { /// Stable, deterministic subject, e.g. `pair:Paused/Stop`.
      Subject: string
      Kind: ChoiceKind
      RequirementKeys: string list
      Class: ChoiceClass }

type Classification =
    { PlanDigest: string
      VocabularyVersion: string
      /// Sorted by subject.
      Choices: ClassifiedChoice list }

/// Only required, approved and implied choices may be generated as
/// completed construction.
let generates (choice: ChoiceClass) =
    match choice with
    | ChoiceClass.Required _ | ChoiceClass.Approved _ | ChoiceClass.Implied _ -> true
    | ChoiceClass.Unresolved _ | ChoiceClass.Unsupported _ -> false

/// The choices that keep a plan from being complete.
let openChoices (classification: Classification) =
    classification.Choices |> List.filter (_.Class >> generates >> not)

let private distinctSorted values = values |> List.distinct |> List.sort

/// Approved when the trace cites an authorized decision or rationale the plan
/// binds; otherwise required by the requirement text the trace cites.
let private basis (decisionIds: Set<string>) (trace: Trace) =
    match trace.NodeIds |> List.filter (fun id -> Set.contains id decisionIds) with
    | [] -> ChoiceClass.Required trace.RequirementKeys
    | decisions -> ChoiceClass.Approved(distinctSorted decisions)

let private choice subject kind (keys: string list) cls =
    { Subject = subject; Kind = kind; RequirementKeys = distinctSorted keys; Class = cls }

let private extensionSubject extension =
    match extension with
    | ExtensionTarget.Guard n -> "extension:guard/" + n.Key
    | ExtensionTarget.Invariant n -> "extension:invariant/" + n.Key
    | ExtensionTarget.Effect n -> "extension:effect/" + n.Key
    | ExtensionTarget.Selection(f, c) -> sprintf "extension:selection/%s/%s" f.Key c.Key

/// Classifies every semantic choice of a validated plan. Deterministic:
/// the same plan gives the same classification.
let classify (plan: ValidatedPlan) : Classification =
    let view = ConstructionPlan.view plan
    let w = view.Workflow
    let version = view.Binding.VocabularyVersion
    let decisions = Set.ofList view.Binding.DecisionIds
    let basisOf = basis decisions

    let states =
        w.States |> List.map (fun s -> choice ("state:" + s.Name.Key) ChoiceKind.State s.Trace.RequirementKeys (basisOf s.Trace))

    let pairs =
        [ for state in w.States do
              for command in w.Commands ->
                  let subject = sprintf "pair:%s/%s" state.Name.Key command.Key
                  let is (s: SemanticName) (c: SemanticName) = s.Key = state.Name.Key && c.Key = command.Key
                  match w.Transitions |> List.tryFind (fun t -> is t.From t.Command) with
                  | Some t -> choice subject ChoiceKind.Pair t.Trace.RequirementKeys (basisOf t.Trace)
                  | None ->
                  match w.BranchingTransitions |> List.tryFind (fun b -> is b.InState b.OnCommand) with
                  | Some b -> choice subject ChoiceKind.Pair b.Trace.RequirementKeys (basisOf b.Trace)
                  | None ->
                      match w.Refusals |> List.tryFind (fun r -> is r.InState r.OnCommand) with
                      | Some r -> choice subject ChoiceKind.Pair r.Trace.RequirementKeys (basisOf r.Trace)
                      | None ->
                          match w.UndecidedTransitions |> List.tryFind (fun u -> is u.InState u.OnCommand) with
                          | Some u -> choice subject ChoiceKind.Pair u.Trace.RequirementKeys (ChoiceClass.Unresolved u.Question)
                          | None when state.Role = Terminal ->
                              choice subject ChoiceKind.Pair state.Trace.RequirementKeys
                                  (ChoiceClass.Implied("terminal state " + state.Name.Key, state.Trace.RequirementKeys))
                          | None ->
                              match w.PairRule with
                              | Some(UnlistedPairs.Refused, trace) ->
                                  choice subject ChoiceKind.Pair trace.RequirementKeys
                                      (ChoiceClass.Implied("pairRule refused", trace.RequirementKeys))
                              | Some(UnlistedPairs.Undecided, trace) ->
                                  choice subject ChoiceKind.Pair trace.RequirementKeys
                                      (ChoiceClass.Unresolved(
                                          sprintf "The requirements do not decide what command %s does in state %s of workflow %s."
                                              command.Key state.Name.Key w.Name.Key))
                              | None ->
                                  choice subject ChoiceKind.Pair [] (ChoiceClass.Unsupported(
                                      sprintf "%s refuses an unlisted pair by omission; no documented rule decides command %s in state %s."
                                          version command.Key state.Name.Key)) ]

    let invariants =
        w.Invariants |> List.map (fun i -> choice ("invariant:" + i.Name.Key) ChoiceKind.Invariant i.Trace.RequirementKeys (basisOf i.Trace))

    let effectQuestion (command: SemanticName) =
        w.Unresolved
        |> List.tryFind (fun u -> u.Effect |> Option.exists (fun e -> e.Key = command.Key))
        |> Option.map _.Question

    let ports =
        w.Ports
        |> List.collect (fun p ->
            match p.Kind with
            | CurrentTimePort -> [ choice "port:currentTime" ChoiceKind.Port p.Trace.RequirementKeys (basisOf p.Trace) ]
            | TransitionEffectPort(command, disposition) ->
                let policy = w.EffectPolicies |> List.tryFind (fun p -> p.Effect.Key = command.Key)
                let failure =
                    match policy, disposition with
                    // Vocabulary 5: the effect policy decides, or leaves open.
                    | Some p, _ when p.OnFailed = FailedEffectAction.Unresolved || p.OnUnknown = UnknownEffectAction.Unresolved ->
                        ChoiceClass.Unresolved(
                            effectQuestion command
                            |> Option.defaultValue (sprintf "The outcome policy of the %s effect is not decided." command.Key))
                    | Some p, _ -> basisOf p.Trace
                    | None, _ ->
                    match disposition with
                    | Some(EffectFailureDisposition.Remain | EffectFailureDisposition.Proceed) -> basisOf p.Trace
                    | Some EffectFailureDisposition.Unresolved ->
                        ChoiceClass.Unresolved(
                            effectQuestion command
                            |> Option.defaultValue (sprintf "The failure disposition of the %s effect is not decided." command.Key))
                    | None ->
                        ChoiceClass.Unsupported(
                            sprintf "%s cannot state what a failed or unknown %s effect means for the workflow." version command.Key)
                [ choice ("port:transitionEffect/" + command.Key) ChoiceKind.Port p.Trace.RequirementKeys (basisOf p.Trace)
                  choice ("effectFailure:" + command.Key) ChoiceKind.EffectFailure p.Trace.RequirementKeys failure ])

    let capabilities =
        w.Capabilities
        |> List.map (fun c ->
            choice (sprintf "capability:%s/%s" c.Command.Key (CapabilityId.value c.Capability)) ChoiceKind.Capability
                c.Trace.RequirementKeys (basisOf c.Trace))

    // Every guard, invariant body and transition effect is handwritten.
    let extensions =
        (w.Transitions |> List.collect (fun t -> t.Guards |> List.map (fun g -> ExtensionTarget.Guard g.Name, t.Trace)))
        @ (w.Invariants |> List.map (fun i -> ExtensionTarget.Invariant i.Name, i.Trace))
        @ (w.Ports |> List.choose (fun p -> match p.Kind with TransitionEffectPort(c, _) -> Some(ExtensionTarget.Effect c, p.Trace) | CurrentTimePort -> None))
        @ (w.BranchingTransitions |> List.map (fun b -> ExtensionTarget.Selection(b.InState, b.OnCommand), b.Trace))
        |> List.distinctBy (fst >> extensionSubject)
    let extensionChoices =
        extensions
        |> List.collect (fun (extension, definingTrace) ->
            let subject = extensionSubject extension
            match w.Inventories |> List.tryFind (fun i -> extensionSubject i.Extension = subject) with
            | Some inventory ->
                choice subject ChoiceKind.ExtensionBehavior inventory.Trace.RequirementKeys (basisOf inventory.Trace)
                :: (inventory.DecisionPoints
                    |> List.map (fun p ->
                        choice (sprintf "%s/point:%s" subject p.Point.Key) ChoiceKind.ExtensionDecision p.Trace.RequirementKeys
                            (match p.Resolution with
                             | DecisionPointResolution.Decided -> basisOf p.Trace
                             | DecisionPointResolution.Undecided question -> ChoiceClass.Unresolved question)))
            | None ->
                [ choice subject ChoiceKind.ExtensionBehavior definingTrace.RequirementKeys
                      (ChoiceClass.Unsupported(
                          sprintf "%s has no decision inventory, so the choices the %s implementation must make are not stated." version subject)) ])

    // Open questions not already counted as an undecided effect failure.
    let questions =
        w.Unresolved
        |> List.filter (fun u -> u.Effect.IsNone)
        |> List.map (fun u -> choice ("unresolved:" + u.Subject.Key) ChoiceKind.OpenQuestion u.Trace.RequirementKeys (ChoiceClass.Unresolved u.Question))

    // Which code a refusal reports is a choice of its own (vocabulary 4).
    let codes =
        w.CodeAssignments
        |> List.map (fun a ->
            let target =
                match a.Target with
                | CodeSubject.Pair(f, c) -> sprintf "pair/%s/%s" f.Key c.Key
                | CodeSubject.Unlisted -> "unlisted"
                | CodeSubject.Guard g -> "guard/" + g.Key
            choice ("refusalCode:" + target) ChoiceKind.RefusalCode a.Trace.RequirementKeys (basisOf a.Trace))

    { PlanDigest = view.Digest
      VocabularyVersion = version
      Choices = states @ pairs @ invariants @ ports @ capabilities @ extensionChoices @ questions @ codes |> List.sortBy _.Subject }

/// A plan with no unresolved or unsupported choice. Only `complete` makes one,
/// so open decisions cannot be represented as completed construction. It is
/// still not authority to apply anything.
type CompletePlan =
    private
    | CompletePlan of ValidatedPlan * Classification

    member this.Plan = let (CompletePlan(plan, _)) = this in plan
    member this.Classification = let (CompletePlan(_, c)) = this in c

/// `Ok` only when every choice is required, approved or implied; otherwise the
/// open choices, so a person can decide them.
let complete (plan: ValidatedPlan) : Result<CompletePlan, ClassifiedChoice list> =
    let classification = classify plan
    match openChoices classification with
    | [] -> Ok(CompletePlan(plan, classification))
    | open' -> Error open'

// ---------------------------------------------------------------------------
// Canonical encoding: `ordo.choice-classification/1`.
// ---------------------------------------------------------------------------

let kindName kind =
    match kind with
    | ChoiceKind.State -> "state"
    | ChoiceKind.Pair -> "pair"
    | ChoiceKind.Invariant -> "invariant"
    | ChoiceKind.Port -> "port"
    | ChoiceKind.EffectFailure -> "effectFailure"
    | ChoiceKind.Capability -> "capability"
    | ChoiceKind.ExtensionBehavior -> "extensionBehavior"
    | ChoiceKind.ExtensionDecision -> "extensionDecision"
    | ChoiceKind.OpenQuestion -> "openQuestion"
    | ChoiceKind.RefusalCode -> "refusalCode"

let className cls =
    match cls with
    | ChoiceClass.Required _ -> "required"
    | ChoiceClass.Approved _ -> "approved"
    | ChoiceClass.Implied _ -> "implied"
    | ChoiceClass.Unresolved _ -> "unresolved"
    | ChoiceClass.Unsupported _ -> "unsupported"

let private jStrings values = JArray(List.map JString values)

let private jChoice (c: ClassifiedChoice) =
    let detail =
        match c.Class with
        | ChoiceClass.Required _ -> []
        | ChoiceClass.Approved ids -> [ "decisionIds", jStrings ids ]
        | ChoiceClass.Implied(rule, _) -> [ "rule", JString rule ]
        | ChoiceClass.Unresolved question -> [ "question", JString question ]
        | ChoiceClass.Unsupported reason -> [ "reason", JString reason ]
    JObject(
        [ "subject", JString c.Subject
          "kind", JString(kindName c.Kind)
          "class", JString(className c.Class)
          "requirementKeys", jStrings c.RequirementKeys ]
        @ detail)

let private classes = [ "required"; "approved"; "implied"; "unresolved"; "unsupported" ]

let toJson (classification: Classification) =
    let counts = classification.Choices |> List.countBy (_.Class >> className) |> Map.ofList
    JObject [ "schemaVersion", JString ClassificationSchemaVersion
              "planDigest", JString classification.PlanDigest
              "vocabularyVersion", JString classification.VocabularyVersion
              "complete", JBool(List.isEmpty (openChoices classification))
              "summary", JObject [ for c in classes -> c, JInt(int64 (Map.tryFind c counts |> Option.defaultValue 0)) ]
              "choices", JArray(classification.Choices |> List.map jChoice) ]

/// Canonical bytes: sorted keys, choices sorted by subject.
let encodeClassification (classification: Classification) = toJson classification |> renderCanonical

let digest (classification: Classification) =
    encodeClassification classification |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
    |> fun hex -> "sha256:" + hex.ToLowerInvariant()
