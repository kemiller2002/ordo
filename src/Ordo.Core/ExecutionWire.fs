/// The wire vocabulary of the execution-governance contract
/// (`ordo.execution/1`).
///
/// Hosts exchange envelopes, receipts and legal actions as JSON rather than
/// by linking Ordo's types, so the shapes here are explicit and independent
/// of F# case names (ORDO-8402). `schemas/ordo-execution.v1.schema.json`
/// documents the same shapes for non-.NET hosts.
module Ordo.Core.ExecutionWire

open System
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.ExecutionRole
open Ordo.Core.Evaluator
open Ordo.Core.Receipt
open Ordo.Core.MutationBoundary
open Ordo.Core.Authorization
open Ordo.Core.Execution

[<Literal>]
let SchemaId = "ordo.execution/1"

let private optString value =
    match value with
    | Some(s: string) -> JString s
    | None -> JNull

let private timestamp (at: DateTimeOffset) = JString(at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))

let rec expectedToJson (expected: ExpectedReceipt) : JsonValue =
    match expected with
    | ArtifactExists r -> JObject [ "kind", JString "artifact-exists"; "reference", JString r ]
    | ArtifactIdentity(r, d) -> JObject [ "kind", JString "artifact-identity"; "reference", JString r; "digest", JString d ]
    | ArtifactAbsent r -> JObject [ "kind", JString "artifact-absent"; "reference", JString r ]
    | CommandSucceeded c -> JObject [ "kind", JString "command-succeeded"; "command", JString c ]
    | StateEquals(k, v) -> JObject [ "kind", JString "state-equals"; "key", JString k; "value", JString v ]
    | ConformsToContract(r, c) ->
        JObject [ "kind", JString "conforms-to-contract"; "reference", JString r; "contract", JString c ]
    | VerificationSatisfied fp -> JObject [ "kind", JString "verification-satisfied"; "evaluator", JString fp ]
    | TransitionObserved t -> JObject [ "kind", JString "transition-observed"; "transition", JString t ]
    | AllOf parts -> JObject [ "kind", JString "all-of"; "receipts", JArray(parts |> List.map expectedToJson) ]

let factToJson (fact: ObservedFact) : JsonValue =
    match fact with
    | ArtifactObserved(r, d) -> JObject [ "kind", JString "artifact-observed"; "reference", JString r; "digest", optString d ]
    | ArtifactNotFound r -> JObject [ "kind", JString "artifact-not-found"; "reference", JString r ]
    | CommandExited(c, code) -> JObject [ "kind", JString "command-exited"; "command", JString c; "exitCode", JInt(int64 code) ]
    | CommandOutcomeUnknown(c, why) ->
        JObject [ "kind", JString "command-outcome-unknown"; "command", JString c; "reason", JString why ]
    | StateObserved(k, v) -> JObject [ "kind", JString "state-observed"; "key", JString k; "value", JString v ]
    | ContractChecked(r, c, ok) ->
        JObject [ "kind", JString "contract-checked"; "reference", JString r; "contract", JString c; "conforms", JBool ok ]
    | VerificationObserved(fp, passed) ->
        JObject [ "kind", JString "verification-observed"; "evaluator", JString fp; "passed", JBool passed ]
    | TransitionRecorded t -> JObject [ "kind", JString "transition-recorded"; "transition", JString t ]
    | Unobservable(s, why) -> JObject [ "kind", JString "unobservable"; "subject", JString s; "reason", JString why ]

let observedToJson (observed: ObservedReceipt) : JsonValue =
    let source, observer =
        match observed.Source with
        | HostObserved o -> "host", o
        | IndependentlyObserved o -> "independent", o
        | SelfReported o -> "self-reported", o

    JObject
        [ "source", JString source
          "observer", JString observer
          "facts", JArray(observed.Facts |> List.map factToJson)
          "narrative", optString observed.Narrative ]

let rec resultToJson (result: ReceiptResult) : JsonValue =
    let outcome = JString(ReceiptOutcome.toWire (Receipt.outcome result))

    match result with
    | Match(e, because) ->
        JObject [ "result", outcome; "expected", expectedToJson e; "evidence", JArray(because |> List.map factToJson) ]
    | Mismatch(e, reason, because)
    | Indeterminate(e, reason, because) ->
        JObject
            [ "result", outcome
              "expected", expectedToJson e
              "reason", JString reason
              "evidence", JArray(because |> List.map factToJson) ]
    | Composite(e, parts) ->
        JObject [ "result", outcome; "expected", expectedToJson e; "constituents", JArray(parts |> List.map resultToJson) ]

let private str path name value =
    requiredMember name value |> Result.bind (asString (path + "." + name))

let rec expectedFromJson (value: JsonValue) : Result<ExpectedReceipt, JsonError> =
    str "$" "kind" value
    |> Result.bind (fun kind ->
        match kind with
        | "artifact-exists" -> str "$" "reference" value |> Result.map ArtifactExists
        | "artifact-absent" -> str "$" "reference" value |> Result.map ArtifactAbsent
        | "artifact-identity" ->
            Result.bind (fun r -> str "$" "digest" value |> Result.map (fun d -> ArtifactIdentity(r, d))) (str "$" "reference" value)
        | "command-succeeded" -> str "$" "command" value |> Result.map CommandSucceeded
        | "state-equals" -> Result.bind (fun k -> str "$" "value" value |> Result.map (fun v -> StateEquals(k, v))) (str "$" "key" value)
        | "conforms-to-contract" ->
            Result.bind
                (fun r -> str "$" "contract" value |> Result.map (fun c -> ConformsToContract(r, c)))
                (str "$" "reference" value)
        | "verification-satisfied" -> str "$" "evaluator" value |> Result.map VerificationSatisfied
        | "transition-observed" -> str "$" "transition" value |> Result.map TransitionObserved
        | "all-of" ->
            requiredMember "receipts" value
            |> Result.bind (asArray "$.receipts")
            |> Result.bind (List.map expectedFromJson >> collect)
            |> Result.map AllOf
        | other -> Error(UnexpectedType("$.kind", "a known receipt kind, got " + other)))

let factFromJson (value: JsonValue) : Result<ObservedFact, JsonError> =
    let optionalString name =
        tryMember name value
        |> Result.bind (fun m ->
            match m with
            | None
            | Some JNull -> Ok None
            | Some v -> asString ("$." + name) v |> Result.map Some)

    let boolean name =
        requiredMember name value
        |> Result.bind (fun v ->
            match v with
            | JBool b -> Ok b
            | _ -> Error(UnexpectedType("$." + name, "boolean")))

    str "$" "kind" value
    |> Result.bind (fun kind ->
        match kind with
        | "artifact-observed" ->
            Result.bind (fun r -> optionalString "digest" |> Result.map (fun d -> ArtifactObserved(r, d))) (str "$" "reference" value)
        | "artifact-not-found" -> str "$" "reference" value |> Result.map ArtifactNotFound
        | "command-exited" ->
            Result.bind
                (fun c -> requiredMember "exitCode" value |> Result.bind (asInt "$.exitCode") |> Result.map (fun code -> CommandExited(c, code)))
                (str "$" "command" value)
        | "command-outcome-unknown" ->
            Result.bind (fun c -> str "$" "reason" value |> Result.map (fun r -> CommandOutcomeUnknown(c, r))) (str "$" "command" value)
        | "state-observed" -> Result.bind (fun k -> str "$" "value" value |> Result.map (fun v -> StateObserved(k, v))) (str "$" "key" value)
        | "contract-checked" ->
            str "$" "reference" value
            |> Result.bind (fun r ->
                str "$" "contract" value
                |> Result.bind (fun c -> boolean "conforms" |> Result.map (fun ok -> ContractChecked(r, c, ok))))
        | "verification-observed" ->
            Result.bind (fun fp -> boolean "passed" |> Result.map (fun p -> VerificationObserved(fp, p))) (str "$" "evaluator" value)
        | "transition-recorded" -> str "$" "transition" value |> Result.map TransitionRecorded
        | "unobservable" -> Result.bind (fun s -> str "$" "reason" value |> Result.map (fun r -> Unobservable(s, r))) (str "$" "subject" value)
        | other -> Error(UnexpectedType("$.kind", "a known fact kind, got " + other)))

let private scopeJson scopes = JArray(scopes |> List.map (SemanticScope.toWire >> JString))

let boundaryToJson (boundary: MutationBoundary) : JsonValue =
    JObject
        [ "scopes", scopeJson boundary.Scopes
          "projections",
          JArray(
              boundary.Projections
              |> List.map (fun p ->
                  JObject [ "scope", JString(SemanticScope.toWire p.Scope); "patterns", JArray(p.Patterns |> List.map JString) ])
          )
          "evaluatorReferences", JArray(boundary.EvaluatorReferences |> List.map JString) ]

let evaluatorToJson (identity: EvaluatorIdentity) : JsonValue =
    JObject
        [ "fingerprint", JString identity.Fingerprint
          "inputs",
          JArray(
              identity.Inputs
              |> List.map (fun i ->
                  JObject
                      [ "kind", JString(EvaluatorInputKind.toWire i.Kind)
                        "reference", JString i.Reference
                        "digest", JString i.Digest ])
          ) ]

let actorToJson (actor: ExecutionActor) : JsonValue =
    JObject
        [ "id", JString(ActorId.value actor.Id)
          "kind", JString(ActorKind.toWire actor.Kind)
          "provider", optString actor.Provider
          "model", optString actor.Model
          "runtime", optString actor.Runtime ]

let envelopeToJson (envelope: ExecutionEnvelope) : JsonValue =
    JObject
        [ "schema", JString SchemaId
          "executionId", JString(ExecutionId.value envelope.Id)
          "workItem", JString(WorkItemId.value envelope.WorkItem)
          "actor", actorToJson envelope.Actor
          "role", JString(ExecutionRole.toWire envelope.Role)
          "capabilities",
          JArray(ExecutionEnvelope.capabilities envelope |> Set.toList |> List.map (ExecutionCapability.toWire >> JString))
          "prohibitions", JArray(envelope.Authority.Prohibits |> Set.toList |> List.map (ExecutionCapability.toWire >> JString))
          "baselineRevision", JString envelope.BaselineRevision
          "candidateRevision", optString envelope.CandidateRevision
          "workspace", optString (envelope.Workspace |> Option.map WorkspaceId.value)
          "containment", JString(Containment.toWire envelope.Containment)
          "securitySandbox", JBool(Containment.isSecuritySandbox envelope.Containment)
          "mutationBoundary", boundaryToJson envelope.Boundary
          "evaluator",
          (match envelope.Evaluator with
           | Some e -> evaluatorToJson e
           | None -> JNull)
          "humanOnlyTransitions", JArray(envelope.HumanOnlyTransitions |> List.map JString)
          "parentExecution", optString (envelope.Parent |> Option.map ExecutionId.value)
          "startedAt", timestamp envelope.StartedAt
          "state", JString(ExecutionState.toWire envelope.State) ]

let private entryToJson (entry: StepEntry) =
    match entry with
    | AttemptStarted(n, at) -> JObject [ "entry", JString "started"; "attempt", JInt(int64 n); "at", timestamp at ]
    | AttemptObserved(n, observed, result, at) ->
        JObject
            [ "entry", JString "observed"
              "attempt", JInt(int64 n)
              "observed", observedToJson observed
              "comparison", resultToJson result
              "at", timestamp at ]
    | AttemptReconciled(n, finding, at) ->
        let token, detail =
            match finding with
            | EffectOccurred e -> "occurred", e
            | EffectDidNotOccur e -> "did-not-occur", e
            | EffectStillUnknown r -> "unknown", r

        JObject
            [ "entry", JString "reconciled"
              "attempt", JInt(int64 n)
              "finding", JString token
              "detail", JString detail
              "at", timestamp at ]

let ledgerToJson (ledger: StepLedger) : JsonValue =
    JObject
        [ "schema", JString SchemaId
          "executionId", JString(ExecutionId.value ledger.Execution)
          "steps",
          JArray(
              ledger.Steps
              |> List.map (fun s ->
                  JObject
                      [ "stepId", JString(StepId.value s.Id)
                        "sequence", JInt(int64 s.Sequence)
                        "name", JString s.Name
                        "dependsOn", JArray(s.DependsOn |> List.map (StepId.value >> JString))
                        "expected", expectedToJson s.Expected
                        "status", JString(Step.statusToWire s)
                        "entries", JArray(s.Entries |> List.map entryToJson) ])
          ) ]

let legalActionsToJson (actions: LegalAction list) : JsonValue =
    JArray(
        actions
        |> List.map (fun a ->
            let target =
                match a.Action with
                | StartStep id
                | RetryStepAction id
                | ReconcileStepAction id
                | ObserveStepAction id -> JString(StepId.value id)
                | ResolveScopeEffect resource -> JString resource
                | ExpandScope
                | Checkpoint
                | Block
                | Resume
                | Complete
                | Abandon -> JNull

            JObject
                [ "transition", JString(LegalActionKind.transitionName a.Action)
                  "target", target
                  "available", JBool a.Available
                  "reasons", JArray(a.Reasons |> List.map JString)
                  "actorRequirement", JString(AuthorizationPolicy.requirementToWire a.Requirement) ])
    )
