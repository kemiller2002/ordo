/// The engine's naming policy. Plans carry semantic names (grammar-checked
/// ASCII words); the engine alone decides the F# identifiers, namespaces and
/// paths derived from them. A name the policy cannot place safely is refused
/// with a typed reason, never mangled into something the plan did not say.
module Ordo.Construction.Naming

open Ordo.Core.ConstructionPlan

/// Union-case names F# reserves or generates members for. A case named `Tag`
/// collides with the generated `Tag` property; `Is<Case>` names collide with
/// generated `Is<Case>` testers; the others shadow core library cases.
let private reservedCaseNames =
    set [ "Tag"; "Item"; "None"; "Some"; "Ok"; "Error"; "True"; "False"; "Null"; "Unit"; "ValueNone"; "ValueSome" ]

type NamingRefusal =
    /// The name is reserved by F# or by generated members.
    | ReservedName of role: string * name: string
    /// `IsX` would collide with the tester F# generates for a case `X`.
    | TesterCollision of role: string * name: string

let private startsWithTester (key: string) =
    key.Length > 2 && key.StartsWith "Is" && System.Char.IsAsciiLetterUpper key[2]

/// Checks one closed set of union-case names.
let checkCases (role: string) (names: SemanticName list) : NamingRefusal list =
    names
    |> List.collect (fun name ->
        let key = name.Key
        [ if Set.contains key reservedCaseNames then ReservedName(role, key)
          if startsWithTester key then TesterCollision(role, key) ])

/// Type and module names are the workflow name plus a fixed suffix, so they
/// cannot collide with each other or with case names (cases are always
/// accessed qualified).
let stateType (workflow: SemanticName) = workflow.Key + "State"
let commandType (workflow: SemanticName) = workflow.Key + "Command"
let guardType (workflow: SemanticName) = workflow.Key + "Guard"
let invariantType (workflow: SemanticName) = workflow.Key + "Invariant"
let effectType (workflow: SemanticName) = workflow.Key + "Effect"
let refusalType (workflow: SemanticName) = workflow.Key + "Refusal"
let decisionType (workflow: SemanticName) = workflow.Key + "Decision"
let rulesType (workflow: SemanticName) = Ordo.Core.ConstructionLayout.rulesType workflow
let transitionModule (workflow: SemanticName) = workflow.Key + "Transition"
let portsType (workflow: SemanticName) = Ordo.Core.ConstructionLayout.portsType workflow
let effectOutcomeType (workflow: SemanticName) = workflow.Key + "EffectOutcome"
let effectFailureType (workflow: SemanticName) = workflow.Key + "EffectFailure"
let failureDispositionType (workflow: SemanticName) = workflow.Key + "FailureDisposition"
let resultType (workflow: SemanticName) = workflow.Key + "Result"
let orchestrationModule (workflow: SemanticName) = workflow.Key + "Orchestration"
let refusalCodeType (workflow: SemanticName) = workflow.Key + "RefusalCode"
let reconciliationType (workflow: SemanticName) = workflow.Key + "Reconciliation"
let onFailedType (workflow: SemanticName) = workflow.Key + "OnFailed"
let onUnknownType (workflow: SemanticName) = workflow.Key + "OnUnknown"
let idType (workflow: SemanticName) = workflow.Key + "Id"
let operationIdType (workflow: SemanticName) = workflow.Key + "OperationId"
let recordType (workflow: SemanticName) = workflow.Key + "Record"
let loadedType (workflow: SemanticName) = workflow.Key + "Loaded"
let savedType (workflow: SemanticName) = workflow.Key + "Saved"
let executionType (workflow: SemanticName) = workflow.Key + "Execution"
let storeType (workflow: SemanticName) = Ordo.Core.ConstructionLayout.storeType workflow
let persistenceModule (workflow: SemanticName) = workflow.Key + "Persistence"
let entryType (workflow: SemanticName) = workflow.Key + "Entry"
let documentType (workflow: SemanticName) = workflow.Key + "Document"
let documentReadType (workflow: SemanticName) = workflow.Key + "DocumentRead"
let documentWriteType (workflow: SemanticName) = workflow.Key + "DocumentWrite"
let documentStoreType (workflow: SemanticName) = Ordo.Core.ConstructionLayout.documentStoreType workflow
let commandOutcomeType (workflow: SemanticName) = workflow.Key + "CommandOutcome"
let createdType (workflow: SemanticName) = workflow.Key + "Created"
let saveOutcomeType (workflow: SemanticName) = workflow.Key + "SaveOutcome"
let reconciledType (workflow: SemanticName) = workflow.Key + "Reconciled"
let documentModule (workflow: SemanticName) = workflow.Key + "Documents"
let documentSessionType (workflow: SemanticName) = workflow.Key + "DocumentSession"
let writeErrorType (workflow: SemanticName) = workflow.Key + "WriteError"
let documentLoadedType (workflow: SemanticName) = workflow.Key + "DocumentLoaded"

/// Vocabulary 4: per branching transition, the closed union of its targets and
/// the rules field that selects one. Semantic-name keys are ASCII letters and
/// digits only, so the `_` separators keep every (from, command) pair distinct.
let branchType (workflow: SemanticName) (from: SemanticName) (command: SemanticName) =
    sprintf "%sBranch_%s_%s" workflow.Key from.Key command.Key
let selectorField (from: SemanticName) (command: SemanticName) = sprintf "Select_%s_%s" from.Key command.Key

/// Every generated file lives in one namespace per workflow.
let rootNamespace (workflow: SemanticName) = "Generated." + workflow.Key

/// Every generated path lives under one ownership root per workflow
/// (`Ordo.Core.ConstructionLayout`, the single naming scheme for paths).
let ownershipRoot (workflow: SemanticName) = Ordo.Core.ConstructionLayout.ownershipRoot workflow
