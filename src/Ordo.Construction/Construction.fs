/// The deterministic construction engine (CONSTRUCT-002). It consumes only a
/// `ValidatedPlan` and returns immutable artifact descriptions. It performs
/// no I/O: a host decides whether and how to write them, using `planWrites`
/// to stay inside generated ownership.
module Ordo.Construction.Construction

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.ConstructionPlan
open Ordo.Construction.Naming
module Layout = Ordo.Core.ConstructionLayout
open Ordo.Construction.Artifacts

/// A valid plan the engine cannot place safely. Anything the vocabulary
/// cannot express (data-carrying states, several workflows, computed
/// predicates, target paths or frameworks) is already unrepresentable in a
/// ValidatedPlan, so the engine has nothing to approximate.
type ConstructionRefusal =
    | Naming of NamingRefusal

type Construction =
    { GeneratorVersion: string
      PlanDigest: string
      OwnershipRoot: string
      /// Generated files, in tier order, then the manifest.
      Artifacts: Artifact list
      ExtensionContracts: ExtensionContract list
      Obligations: ConstructionObligation list
      StructuralChecks: StructuralCheck list }

let private sha256 (content: string) =
    "sha256:" + (Encoding.UTF8.GetBytes content |> SHA256.HashData |> Convert.ToHexString).ToLowerInvariant()

let private keysOf (operations: OperationDraft list) =
    operations |> List.collect (fun o -> (operationTrace o).RequirementKeys) |> List.distinct |> List.sort

let private labels (operations: OperationDraft list) = operations |> List.map operationLabel |> List.distinct |> List.sort

let private artifact path tier operations content =
    { Path = path; Tier = tier; Ownership = Generated; Content = content; Digest = sha256 content
      Operations = labels operations; RequirementKeys = keysOf operations }

let private tierName tier =
    match tier with
    | Some SemanticModel -> JString "semantic-model"
    | Some Transition -> JString "transition"
    | Some Orchestration -> JString "orchestration"
    | None -> JNull

let private ownershipName ownership =
    match ownership with
    | Generated -> "generated"
    | Handwritten -> "handwritten"

let private extensionName kind =
    match kind with
    | GuardPredicate -> "guard-predicate"
    | InvariantPredicate -> "invariant-predicate"
    | CurrentTimePort -> "current-time-port"
    | EffectPort -> "effect-port"
    | SelectionFunction -> "selection-function"
    | ReconcilePort -> "reconcile-port"
    | CompensatePort -> "compensate-port"
    | StorePort -> "store-port"
    | DocumentStorePort -> "document-store-port"

let private strings values = JArray(values |> List.map JString)

let private manifestJson (plan: ValidatedPlanView) generator root (artifacts: Artifact list) contracts obligations checks =
    let effectFailure =
        if generator = GeneratorVersion then []
        // Vocabulary 5 states outcome handling in effect policies only.
        elif generator = GeneratorVersion5 || generator = GeneratorVersion6 || generator = GeneratorVersion7 then
            [ "effectPolicies",
              JArray [ for p in plan.Workflow.EffectPolicies ->
                         JObject [ "effect", JString p.Effect.Key
                                   "retries", JInt(int64 p.Retries)
                                   "onFailed", JString(failedActionName p.OnFailed)
                                   "onUnknown", JString(unknownActionName p.OnUnknown) ] ] ]
            @ (match plan.Workflow.Persistence with
               | Some p ->
                   [ "persistence",
                     JObject [ "idempotency", JString(idempotencyName p.Idempotency); "onStale", JString(staleActionName p.OnStale) ] ]
               | None -> [])
            @ (match plan.Workflow.Document with
               | Some d ->
                   [ "document",
                     JObject [ "idempotency", JString(idempotencyName d.Idempotency)
                               "onSaveConflict", JString(staleActionName d.OnSaveConflict)
                               "onReconcileMoved", JString(reconcileMovedName d.OnReconcileMoved) ] ]
               | None -> [])
        else
            [ "effectFailure",
              JArray [ for p in plan.Workflow.Ports do
                         match p.Kind with
                         | TransitionEffectPort(effect, Some disposition) ->
                             JObject [ "effect", JString effect.Key; "onFailure", JString(effectFailureDispositionName disposition) ]
                         | TransitionEffectPort(_, None) | PortKind.CurrentTimePort -> () ] ]
    JObject([
        "generatorVersion", JString generator
        "planDigest", JString plan.Digest
        "binding",
        JObject [ "sourceManifestDigest", JString plan.Binding.SourceManifestDigest
                  "blueprintDigest", JString plan.Binding.BlueprintDigest
                  "cohortId", JString plan.Binding.CohortId
                  "decisionIds", strings plan.Binding.DecisionIds ]
        "notice", JString "Generated structure only. Not authorization to apply, not evidence of behavioral verification."
        "ownershipRoot", JString root
        "artifacts",
        JArray [ for a in artifacts ->
                    JObject [ "path", JString a.Path; "tier", tierName a.Tier; "ownership", JString(ownershipName a.Ownership)
                              "sha256", JString a.Digest; "operations", strings a.Operations; "requirementKeys", strings a.RequirementKeys ] ]
        "extensionContracts",
        JArray [ for c in contracts ->
                    JObject [ "name", JString c.Name; "kind", JString(extensionName c.Kind); "ownership", JString(ownershipName c.Ownership)
                              "operations", strings c.Operations; "requirementKeys", strings c.RequirementKeys ] ]
        "obligations",
        JArray [ for o in obligations ->
                    let kind =
                        match o.Kind with
                        | Implement contract -> [ "kind", JString "implement"; "contract", JString contract ]
                        | ResolveUnresolved(subject, question) -> [ "kind", JString "resolve"; "subject", JString subject; "question", JString question ]
                        | ResolveEffectFailure(effect, subject, question) ->
                            [ "kind", JString "resolve"; "subject", JString subject; "effect", JString effect; "question", JString question ]
                        | VerifyIndependently what -> [ "kind", JString "verify-independently"; "what", JString what ]
                        | ResolveUndecidedTransition(state, command, question) ->
                            [ "kind", JString "resolve-transition"; "state", JString state; "command", JString command; "question", JString question ]
                        | DecideExtension(contract, point, question) ->
                            [ "kind", JString "decide-extension"; "contract", JString contract; "point", JString point; "question", JString question ]
                    JObject([ "id", JString o.Id ] @ kind @ [ "requirementKeys", strings o.RequirementKeys ]) ]
        "structuralChecks",
        JObject [ "independence", JString "derived-from-plan"
                  "pairs",
                  JArray [ for c in checks ->
                              JObject([ "state", JString c.State; "command", JString c.Command
                                        "expected",
                                        (match c.Expected with
                                         | Some target -> JString target
                                         | None when c.Undecided -> JString "undecided"
                                         | None when not (List.isEmpty c.Targets) -> JString "branching"
                                         | None -> JString "invalid-transition") ]
                                       @ (if List.isEmpty c.Targets then [] else [ "targets", strings c.Targets ])) ] ]
        "coverage",
        JObject [ for KeyValue(key, coverage) in plan.Coverage ->
                    key,
                    match coverage with
                    | Constructed _ -> JString "constructed"
                    | UnresolvedOnly _ -> JString "unresolved-only"
                    | WorkflowLevel -> JString "workflow-level" ] ]
        @ effectFailure)
    |> render
    |> fun text -> text + "\n"

let private refusals (shape: Emit.Shape) =
    checkCases "state" shape.States @ checkCases "command" shape.Commands
    @ checkCases "guard" shape.Guards @ checkCases "invariant" shape.Invariants
    @ checkCases "refusal code" shape.Codes
    |> List.map Naming

/// Same plan and same generator version give byte-identical output.
let construct (validated: ValidatedPlan) : Result<Construction, ConstructionRefusal list> =
    let plan = view validated
    let w = plan.Workflow
    let generator = generatorVersionFor plan.Binding.VocabularyVersion
    let shape = Emit.shapeOf generator w
    match refusals shape with
    | _ :: _ as refused -> Error refused
    | [] ->
        let root = ownershipRoot w.Name
        let operations = canonicalOperations validated
        let pick predicate = operations |> List.filter predicate
        let isWorkflow = function DefineWorkflow _ -> true | _ -> false
        let isEffectPort = function DeclarePort { Kind = TransitionEffect _ } -> true | _ -> false
        // Vocabulary 2: an obligation linked to an effect is realized by the
        // orchestration's Unresolved result case. Vocabulary 1 has no links.
        let isEffectObligation = function DeclareUnresolved { Effect = Some _ } -> true | _ -> false
        let modelOps =
            pick (function
                | DefineWorkflow _ | DefineState _ | DefineTransition _ | DefineInvariant _
                | DefineRefusalCode _ | DefineBranchingTransition _ -> true
                | op -> isEffectPort op)
        let transitionOps =
            pick (function
                | DefineWorkflow _ | DefineTransition _ | DefineInvariant _ | BindCapability _
                | DeclarePairRule _ | DefineRefusal _ | DeclareUndecidedTransition _
                | DefineRefusalCode _ | AssignRefusalCode _ | DefineBranchingTransition _ -> true
                | op -> isEffectPort op)
        let orchestrationOps = pick (function DefineWorkflow _ | DeclarePort _ -> true | op -> isEffectObligation op)
        let digest = plan.Digest
        let generated =
            [ artifact (Layout.modelPath w.Name) (Some SemanticModel) modelOps (Emit.semanticModel digest shape)
              artifact (Layout.transitionPath w.Name) (Some Transition) transitionOps (Emit.transition digest shape) ]
            @ (if shape.HasPorts then [ artifact (Layout.orchestrationPath w.Name) (Some Orchestration) orchestrationOps (Emit.orchestration digest shape) ] else [])
            @ (match w.Persistence with
               | Some p ->
                   [ artifact (Layout.persistencePath w.Name) (Some Orchestration)
                         (pick (function DefineWorkflow _ | DeclarePersistence _ | AssignRefusalCode { Target = CodeTarget.NotFound | CodeTarget.StaleRevision } -> true | _ -> false))
                         (Emit.persistence digest shape p) ]
               | None -> [])
            @ (match w.Document with
               | Some d ->
                   [ artifact (Layout.documentPath w.Name) (Some Orchestration)
                         (pick (function
                             | DefineWorkflow _ | DeclareDocument _
                             | AssignRefusalCode { Target = CodeTarget.NotFound | CodeTarget.StaleRevision | CodeTarget.SaveConflict } -> true
                             | _ -> false))
                         (Emit.document digest shape d) ]
               | None -> [])

        let guardOps (guard: SemanticName) =
            pick (function DefineTransition t -> t.Guards |> List.exists (fun g -> g.Name = guard.Key) | _ -> false)
        let contract name kind ops = { Name = name; Kind = kind; Ownership = Handwritten; Operations = labels ops; RequirementKeys = keysOf ops }
        let contracts =
            (shape.Guards |> List.map (fun g -> contract (Layout.guardContract w.Name g) GuardPredicate (guardOps g)))
            @ (shape.Invariants |> List.map (fun i ->
                contract (Layout.invariantContract w.Name i) InvariantPredicate
                    (pick (function DefineInvariant d -> d.Name = i.Key | _ -> false))))
            @ (if shape.HasClock then [ contract (Layout.currentTimeContract w.Name) CurrentTimePort (pick (function DeclarePort { Kind = CurrentTime } -> true | _ -> false)) ] else [])
            @ (shape.Effects |> List.map (fun e ->
                contract (Layout.effectContract w.Name e) EffectPort
                    (pick (function DeclarePort { Kind = TransitionEffect(c, _) } -> c = e.Key | _ -> false))))
            // Vocabulary 5: reconciliation and compensation are host code the
            // plan's policy requires, one contract per effect.
            @ (shape.Policies
               |> List.collect (fun p ->
                   let ops = pick (function DeclareEffectPolicy d -> d.Effect = p.Effect.Key | DeclarePort { Kind = TransitionEffect(c, _) } -> c = p.Effect.Key | _ -> false)
                   [ if p.OnUnknown = UnknownEffectAction.Reconcile then contract (Layout.reconcileContract w.Name p.Effect) ReconcilePort ops
                     if p.OnFailed = FailedEffectAction.Compensate then contract (Layout.compensateContract w.Name p.Effect) CompensatePort ops ]))
            // Vocabulary 6: the host's store, loaded and compare-and-swap saved.
            @ (match w.Persistence with
               | Some _ ->
                   let ops = pick (function DeclarePersistence _ -> true | _ -> false)
                   [ contract (Layout.loadContract w.Name) StorePort ops; contract (Layout.saveContract w.Name) StorePort ops ]
               | None -> [])
            // Vocabulary 7: the host's document store, read and compare-and-swap written.
            @ (match w.Document with
               | Some _ ->
                   let ops = pick (function DeclareDocument _ -> true | _ -> false)
                   [ contract (Layout.readContract w.Name) DocumentStorePort ops; contract (Layout.writeContract w.Name) DocumentStorePort ops ]
               | None -> [])
            @ (shape.Branching |> List.map (fun (from, command, _) ->
                contract (Layout.selectionContract w.Name from command) SelectionFunction
                    (pick (function
                        | DefineBranchingTransition b -> b.Pair.InState = from.Key && b.Pair.OnCommand = command.Key
                        | _ -> false))))

        // Vocabulary 3: an extension whose inventory has an undecided point is
        // not implementable yet. It gets one decide obligation per open point
        // and no implement obligation, so nobody is told to implement a
        // behavior whose rules are not stated.
        let openPoints (contract: ExtensionContract) =
            w.Inventories
            |> List.filter (fun i ->
                let name =
                    match i.Extension with
                    | ExtensionTarget.Guard n -> Layout.guardContract w.Name n
                    | ExtensionTarget.Invariant n -> Layout.invariantContract w.Name n
                    | ExtensionTarget.Effect n -> Layout.effectContract w.Name n
                    | ExtensionTarget.Selection(f, c) -> Layout.selectionContract w.Name f c
                name = contract.Name)
            |> List.collect _.DecisionPoints
            |> List.choose (fun p ->
                match p.Resolution with
                | DecisionPointResolution.Undecided question -> Some(p, question)
                | DecisionPointResolution.Decided -> None)
        let undecidedQuestion (state: SemanticName) (command: SemanticName) =
            w.UndecidedTransitions
            |> List.tryFind (fun u -> u.InState.Key = state.Key && u.OnCommand.Key = command.Key)
            |> Option.map (fun u -> u.Question, u.Trace.RequirementKeys)
            |> Option.defaultWith (fun () ->
                sprintf "The requirements do not decide what command %s does in state %s of workflow %s." command.Key state.Key w.Name.Key,
                (match w.PairRule with Some(_, trace) -> trace.RequirementKeys | None -> []))
        let obligations =
            (contracts
             |> List.collect (fun c ->
                 match openPoints c with
                 | [] -> [ { Id = "IMPLEMENT:" + c.Name; Kind = Implement c.Name; RequirementKeys = c.RequirementKeys } ]
                 | points ->
                     points |> List.map (fun (p, question) ->
                         { Id = sprintf "DECIDE:%s/%s" c.Name p.Point.Key
                           Kind = DecideExtension(c.Name, p.Point.Key, question)
                           RequirementKeys = p.Trace.RequirementKeys })))
            @ (shape.Undecided |> List.map (fun (state, command) ->
                let question, keys = undecidedQuestion state command
                { Id = sprintf "RESOLVE-PAIR:%s/%s" state.Key command.Key
                  Kind = ResolveUndecidedTransition(state.Key, command.Key, question)
                  RequirementKeys = keys }))
            @ (match w.Persistence with
               | Some p ->
                   let keys = p.Trace.RequirementKeys
                   [ if p.Idempotency = IdempotencyScope.Unresolved then
                         { Id = "RESOLVE-PERSISTENCE:idempotency"
                           Kind = ResolveUnresolved("PersistenceIdempotency", sprintf "Whether operation ids of workflow %s are deduplicated durably or for one session only is not decided." w.Name.Key)
                           RequirementKeys = keys }
                     if p.OnStale = StaleAction.Unresolved then
                         { Id = "RESOLVE-PERSISTENCE:onStale"
                           Kind = ResolveUnresolved("PersistenceOnStale", sprintf "What a write against a stale revision of workflow %s means is not decided." w.Name.Key)
                           RequirementKeys = keys } ]
               | None -> [])
            @ (match w.Document with
               | Some d ->
                   let keys = d.Trace.RequirementKeys
                   [ if d.Idempotency = IdempotencyScope.Unresolved then
                         { Id = "RESOLVE-DOCUMENT:idempotency"
                           Kind = ResolveUnresolved("DocumentIdempotency", sprintf "Whether save operation ids of the %s document are deduplicated durably or for one session only is not decided." w.Name.Key)
                           RequirementKeys = keys }
                     if d.OnSaveConflict = StaleAction.Unresolved then
                         { Id = "RESOLVE-DOCUMENT:onSaveConflict"
                           Kind = ResolveUnresolved("DocumentOnSaveConflict", sprintf "What a save of the %s document against a moved version means is not decided." w.Name.Key)
                           RequirementKeys = keys }
                     if d.Idempotency <> IdempotencyScope.Session && d.OnReconcileMoved = ReconcileMovedAction.Unresolved then
                         { Id = "RESOLVE-DOCUMENT:onReconcileMoved"
                           Kind = ResolveUnresolved("DocumentOnReconcileMoved", sprintf "What reconcile reports when the %s document moved without the unknown save's operation is not decided." w.Name.Key)
                           RequirementKeys = keys } ]
               | None -> [])
            @ (w.Unresolved |> List.map (fun u ->
                { Id = "RESOLVE:" + u.Subject.Key
                  Kind =
                    match u.Effect with
                    | Some effect -> ResolveEffectFailure(effect.Key, u.Subject.Key, u.Question)
                    | None -> ResolveUnresolved(u.Subject.Key, u.Question)
                  RequirementKeys = u.Trace.RequirementKeys }))
            @ [ { Id = "VERIFY:" + w.Name.Key
                  Kind = VerifyIndependently "Behavior against the original requirements. Generated structure and its derived checks are not independent acceptance evidence."
                  RequirementKeys = plan.AssignedRequirementKeys |> Set.toList } ]

        let checks =
            [ for state in shape.States do
                for command in shape.Commands ->
                    { State = state.Key
                      Command = command.Key
                      Expected =
                        w.Transitions
                        |> List.tryFind (fun t -> t.From.Key = state.Key && t.Command.Key = command.Key)
                        |> Option.map _.To.Key
                      Undecided = shape.Undecided |> List.exists (fun (u, c) -> u.Key = state.Key && c.Key = command.Key)
                      Targets =
                        shape.Branching
                        |> List.tryFind (fun (f, c, _) -> f.Key = state.Key && c.Key = command.Key)
                        |> Option.map (fun (_, _, targets) -> targets |> List.map _.Key)
                        |> Option.defaultValue [] } ]

        let manifest =
            artifact (Layout.manifestPath w.Name) None operations
                (manifestJson plan generator root generated contracts obligations checks)

        Ok { GeneratorVersion = generator
             PlanDigest = digest
             OwnershipRoot = root
             Artifacts = generated @ [ manifest ]
             ExtensionContracts = contracts
             Obligations = obligations
             StructuralChecks = checks }

// ---- Applying artifacts is a host's job; deciding what is safe to apply is not. ----

type PlannedWrite =
    | Create of path: string
    | Replace of path: string
    | Unchanged of path: string
    /// A generated file the previous construction produced and this one does not.
    | RemoveStale of path: string

type TargetRefusal =
    /// A file exists at a target path and does not carry the generated marker.
    | WouldOverwriteHandwritten of path: string
    /// A handwritten file sits inside the generated ownership root.
    | HandwrittenInGeneratedRoot of path: string
    /// A path outside the ownership root was supplied or produced.
    | OutsideOwnershipRoot of path: string

let private manifestPrefix = "{\n  \"generatorVersion\": \"ordo.construction/"

/// Whether existing content was produced by this generator family.
let isGenerated (content: string) =
    content.StartsWith(GeneratedMarker, StringComparison.Ordinal) || content.StartsWith(manifestPrefix, StringComparison.Ordinal)

let private inside root (path: string) =
    path.StartsWith(root, StringComparison.Ordinal)
    && not (path.Contains "..") && not (path.Contains "\\") && not (path.Contains "//")

/// Pure write planning. `existing` maps relative paths (under the ownership
/// root, plus every target path) to their current content. Handwritten files
/// are never replaced and never removed; the whole plan is refused instead.
let planWrites (existing: Map<string, string>) (construction: Construction) : Result<PlannedWrite list, TargetRefusal list> =
    let root = construction.OwnershipRoot
    let targets = construction.Artifacts |> List.map _.Path |> Set.ofList
    let refusedTargets =
        construction.Artifacts
        |> List.collect (fun a ->
            [ if not (inside root a.Path) then OutsideOwnershipRoot a.Path
              match Map.tryFind a.Path existing with
              | Some content when not (isGenerated content) -> WouldOverwriteHandwritten a.Path
              | _ -> () ])
    let refusedExisting =
        existing
        |> Map.toList
        |> List.choose (fun (path, content) ->
            if inside root path && not (Set.contains path targets) && not (isGenerated content) then Some(HandwrittenInGeneratedRoot path)
            else None)
    match refusedTargets @ refusedExisting with
    | _ :: _ as refused -> Error refused
    | [] ->
        let writes =
            construction.Artifacts
            |> List.map (fun a ->
                match Map.tryFind a.Path existing with
                | None -> Create a.Path
                | Some content when content = a.Content -> Unchanged a.Path
                | Some _ -> Replace a.Path)
        let stale =
            existing
            |> Map.toList
            |> List.choose (fun (path, content) ->
                if inside root path && not (Set.contains path targets) && isGenerated content then Some(RemoveStale path) else None)
        Ok(writes @ stale)
