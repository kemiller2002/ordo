/// The one naming scheme for what construction emits from a validated plan:
/// generated file paths under a per-workflow ownership root, and the names of
/// the handwritten extension contracts the generated code requires.
///
/// It lives in Ordo.Core so that a host (and `ConstructionDefaultPlan`) can
/// state the generated layout of a plan before construction runs, without a
/// second naming scheme. `Ordo.Construction` names its artifacts and
/// extension contracts with these functions; its tests prove the layout and
/// the engine's artifact paths are equal.
module Ordo.Core.ConstructionLayout

open Ordo.Core.ConstructionPlan

/// Every generated path lives under one ownership root per workflow. Paths
/// are relative, forward-slashed and contain only grammar-checked words.
let ownershipRoot (workflow: SemanticName) = "generated/" + workflow.Key + "/"

let modelPath (workflow: SemanticName) = ownershipRoot workflow + workflow.Key + ".Model.g.fs"
let transitionPath (workflow: SemanticName) = ownershipRoot workflow + workflow.Key + ".Transition.g.fs"
let orchestrationPath (workflow: SemanticName) = ownershipRoot workflow + workflow.Key + ".Orchestration.g.fs"
/// Vocabulary 6: the persisted instance's load, deduplicate, decide and save path.
let persistencePath (workflow: SemanticName) = ownershipRoot workflow + workflow.Key + ".Persistence.g.fs"
let manifestPath (workflow: SemanticName) = ownershipRoot workflow + "construction-manifest.json"

let rulesType (workflow: SemanticName) = workflow.Key + "Rules"
let portsType (workflow: SemanticName) = "I" + workflow.Key + "Ports"
/// Vocabulary 6: the store a persisted instance is loaded from and saved to.
let storeType (workflow: SemanticName) = "I" + workflow.Key + "Store"

/// Extension contract names. A handwritten implementation is identified by
/// these names in the construction manifest and in obligations.
let guardContract (workflow: SemanticName) (guard: SemanticName) = sprintf "%s.Guard/%s" (rulesType workflow) guard.Key
let invariantContract (workflow: SemanticName) (invariant: SemanticName) = sprintf "%s.Invariant/%s" (rulesType workflow) invariant.Key
let currentTimeContract (workflow: SemanticName) = portsType workflow + ".CurrentTime"
let effectContract (workflow: SemanticName) (effect: SemanticName) = sprintf "%s.Perform/%s" (portsType workflow) effect.Key
/// Vocabulary 6: loading and compare-and-swap saving of a persisted instance.
let loadContract (workflow: SemanticName) = storeType workflow + ".Load"
let saveContract (workflow: SemanticName) = storeType workflow + ".Save"
/// Vocabulary 5: the host's reconciliation of an effect whose outcome is unknown.
let reconcileContract (workflow: SemanticName) (effect: SemanticName) = sprintf "%s.Reconcile/%s" (portsType workflow) effect.Key
/// Vocabulary 5: the host's compensation of a definitely failed effect.
let compensateContract (workflow: SemanticName) (effect: SemanticName) = sprintf "%s.Compensate/%s" (portsType workflow) effect.Key
/// Vocabulary 4: the handwritten selector that picks one target of a branching transition.
let selectionContract (workflow: SemanticName) (from: SemanticName) (command: SemanticName) =
    sprintf "%s.Select/%s/%s" (rulesType workflow) from.Key command.Key

/// The files construction emits for a validated workflow, in tier order then
/// the construction manifest. The orchestration tier exists only when the
/// workflow declares a port.
let generatedLayout (workflow: Workflow) : string list =
    [ modelPath workflow.Name
      transitionPath workflow.Name
      if not (List.isEmpty workflow.Ports) then orchestrationPath workflow.Name
      if workflow.Persistence.IsSome then persistencePath workflow.Name
      manifestPath workflow.Name ]
