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

let private strings values = JArray(values |> List.map JString)

let private manifestJson (plan: ValidatedPlanView) root (artifacts: Artifact list) contracts obligations checks =
    JObject [
        "generatorVersion", JString GeneratorVersion
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
                        | VerifyIndependently what -> [ "kind", JString "verify-independently"; "what", JString what ]
                    JObject([ "id", JString o.Id ] @ kind @ [ "requirementKeys", strings o.RequirementKeys ]) ]
        "structuralChecks",
        JObject [ "independence", JString "derived-from-plan"
                  "pairs",
                  JArray [ for c in checks ->
                              JObject [ "state", JString c.State; "command", JString c.Command
                                        "expected", (match c.Expected with Some target -> JString target | None -> JString "invalid-transition") ] ] ]
        "coverage",
        JObject [ for KeyValue(key, coverage) in plan.Coverage ->
                    key,
                    match coverage with
                    | Constructed _ -> JString "constructed"
                    | UnresolvedOnly _ -> JString "unresolved-only"
                    | WorkflowLevel -> JString "workflow-level" ] ]
    |> render
    |> fun text -> text + "\n"

let private refusals (shape: Emit.Shape) =
    checkCases "state" shape.States @ checkCases "command" shape.Commands
    @ checkCases "guard" shape.Guards @ checkCases "invariant" shape.Invariants
    |> List.map Naming

/// Same plan and same generator version give byte-identical output.
let construct (validated: ValidatedPlan) : Result<Construction, ConstructionRefusal list> =
    let plan = view validated
    let w = plan.Workflow
    let shape = Emit.shapeOf w
    match refusals shape with
    | _ :: _ as refused -> Error refused
    | [] ->
        let root = ownershipRoot w.Name
        let operations = canonicalOperations validated
        let pick predicate = operations |> List.filter predicate
        let isWorkflow = function DefineWorkflow _ -> true | _ -> false
        let isEffectPort = function DeclarePort { Kind = TransitionEffect _ } -> true | _ -> false
        let modelOps = pick (function DefineWorkflow _ | DefineState _ | DefineTransition _ | DefineInvariant _ -> true | op -> isEffectPort op)
        let transitionOps = pick (function DefineWorkflow _ | DefineTransition _ | DefineInvariant _ | BindCapability _ -> true | op -> isEffectPort op)
        let orchestrationOps = pick (function DefineWorkflow _ | DeclarePort _ -> true | _ -> false)
        let digest = plan.Digest
        let generated =
            [ artifact (root + w.Name.Key + ".Model.g.fs") (Some SemanticModel) modelOps (Emit.semanticModel digest shape)
              artifact (root + w.Name.Key + ".Transition.g.fs") (Some Transition) transitionOps (Emit.transition digest shape) ]
            @ (if shape.HasPorts then [ artifact (root + w.Name.Key + ".Orchestration.g.fs") (Some Orchestration) orchestrationOps (Emit.orchestration digest shape) ] else [])

        let guardOps (guard: SemanticName) =
            pick (function DefineTransition t -> t.Guards |> List.exists (fun g -> g.Name = guard.Key) | _ -> false)
        let contract name kind ops = { Name = name; Kind = kind; Ownership = Handwritten; Operations = labels ops; RequirementKeys = keysOf ops }
        let contracts =
            (shape.Guards |> List.map (fun g -> contract (sprintf "%s.Guard/%s" (rulesType w.Name) g.Key) GuardPredicate (guardOps g)))
            @ (shape.Invariants |> List.map (fun i ->
                contract (sprintf "%s.Invariant/%s" (rulesType w.Name) i.Key) InvariantPredicate
                    (pick (function DefineInvariant d -> d.Name = i.Key | _ -> false))))
            @ (if shape.HasClock then [ contract (portsType w.Name + ".CurrentTime") CurrentTimePort (pick (function DeclarePort { Kind = CurrentTime } -> true | _ -> false)) ] else [])
            @ (shape.Effects |> List.map (fun e ->
                contract (sprintf "%s.Perform/%s" (portsType w.Name) e.Key) EffectPort
                    (pick (function DeclarePort { Kind = TransitionEffect c } -> c = e.Key | _ -> false))))

        let obligations =
            (contracts |> List.map (fun c -> { Id = "IMPLEMENT:" + c.Name; Kind = Implement c.Name; RequirementKeys = c.RequirementKeys }))
            @ (w.Unresolved |> List.map (fun u ->
                { Id = "RESOLVE:" + u.Subject.Key; Kind = ResolveUnresolved(u.Subject.Key, u.Question); RequirementKeys = u.Trace.RequirementKeys }))
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
                        |> Option.map _.To.Key } ]

        let manifest =
            artifact (root + "construction-manifest.json") None operations
                (manifestJson plan root generated contracts obligations checks)

        Ok { GeneratorVersion = GeneratorVersion
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
