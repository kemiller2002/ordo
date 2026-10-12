/// Strict reader and writer for `ordo.construction-plan/1`. Reading yields an
/// untrusted `PlanDraft`; only `ConstructionPlan.validate` against a prepared
/// cohort can produce a `ValidatedPlan`. An operation, port or predicate the
/// vocabulary does not name is refused by name, never ignored or guessed.
module Ordo.Core.ConstructionPlanWire

open Ordo.Core.Json
open Ordo.Core.StrictJson
open Ordo.Core.ConstructionPlan

let private trace path value =
    result {
        let! p = fields path [ "requirementKeys"; "nodeIds" ] [] value
        let! keys = field path "requirementKeys" p
        let! requirementKeys = strings (path + ".requirementKeys") keys
        let! ids = field path "nodeIds" p
        let! nodeIds = strings (path + ".nodeIds") ids
        return { TraceDraft.RequirementKeys = requirementKeys; NodeIds = nodeIds }
    }

let private traceField path p =
    result {
        let! raw = field path "trace" p
        return! trace (path + ".trace") raw
    }

let private predicate path raw =
    match raw with
    | "extension" -> Ok ExtensionPredicate
    | other -> fail path ("unsupported predicate: " + other)

let private role path raw =
    match raw with
    | "initial" -> Ok Initial
    | "ordinary" -> Ok Ordinary
    | "terminal" -> Ok Terminal
    | other -> fail path ("unknown state role: " + other)

let private guard path value =
    result {
        let! p = fields path [ "name"; "predicate" ] [] value
        let! name = textField path "name" p
        let! kind = textField path "predicate" p
        let! decided = predicate (path + ".predicate") kind
        return { GuardDraft.Name = name; Predicate = decided }
    }

let private operation path value =
    result {
        let! kind =
            match value with
            | JObject properties ->
                match properties |> List.filter (fun (name, _) -> name = "op") with
                | [ _, JString op ] -> Ok op
                | _ -> fail path "expected exactly one string member op"
            | _ -> fail path "expected object"
        match kind with
        | "defineWorkflow" ->
            let! p = fields path [ "op"; "name"; "trace" ] [] value
            let! name = textField path "name" p
            let! t = traceField path p
            return DefineWorkflow { Name = name; Trace = t }
        | "defineState" ->
            let! p = fields path [ "op"; "name"; "role"; "trace" ] [] value
            let! name = textField path "name" p
            let! roleText = textField path "role" p
            let! decided = role (path + ".role") roleText
            let! t = traceField path p
            return DefineState { Name = name; Role = decided; Trace = t }
        | "defineTransition" ->
            let! p = fields path [ "op"; "command"; "from"; "to"; "guards"; "trace" ] [] value
            let! command = textField path "command" p
            let! from = textField path "from" p
            let! target = textField path "to" p
            let! rawGuards = field path "guards" p
            let! guards = items (path + ".guards") guard rawGuards
            let! t = traceField path p
            return DefineTransition { Command = command; From = from; To = target; Guards = guards; Trace = t }
        | "defineInvariant" ->
            let! p = fields path [ "op"; "name"; "predicate"; "holdsIn"; "trace" ] [] value
            let! name = textField path "name" p
            let! kindText = textField path "predicate" p
            let! decided = predicate (path + ".predicate") kindText
            let! rawStates = field path "holdsIn" p
            let! holdsIn = strings (path + ".holdsIn") rawStates
            let! t = traceField path p
            return DefineInvariant { Name = name; Predicate = decided; HoldsIn = holdsIn; Trace = t }
        | "declarePort" ->
            let! p = fields path [ "op"; "port"; "trace" ] [ "command" ] value
            let! port = textField path "port" p
            let! kindOfPort =
                match port, Map.containsKey "command" p with
                | "currentTime", false -> Ok CurrentTime
                | "transitionEffect", true -> textField path "command" p |> Result.map TransitionEffect
                | ("currentTime" | "transitionEffect"), _ -> fail path ("incorrect fields for port " + port)
                | other, _ -> fail (path + ".port") ("unsupported port: " + other)
            let! t = traceField path p
            return DeclarePort { Kind = kindOfPort; Trace = t }
        | "bindCapability" ->
            let! p = fields path [ "op"; "command"; "capability"; "trace" ] [] value
            let! command = textField path "command" p
            let! capability = textField path "capability" p
            let! t = traceField path p
            return BindCapability { Command = command; Capability = capability; Trace = t }
        | "declareUnresolved" ->
            let! p = fields path [ "op"; "subject"; "question"; "trace" ] [] value
            let! subject = textField path "subject" p
            let! question = textField path "question" p
            let! t = traceField path p
            return DeclareUnresolved { Subject = subject; Question = question; Trace = t }
        | other -> return! fail (path + ".op") ("unsupported operation: " + other)
    }

let readPlan (input: string) : Result<PlanDraft, string> =
    result {
        let! json = parse input |> Result.mapError (sprintf "invalid JSON: %A")
        let! p = fields "$" [ "schemaVersion"; "vocabularyVersion"; "binding"; "operations" ] [] json
        let! schema = textField "$" "schemaVersion" p
        let! () = if schema = PlanSchemaVersion then Ok() else fail "$.schemaVersion" ("unsupported " + schema)
        let! vocabulary = textField "$" "vocabularyVersion" p
        let! () = if vocabulary = VocabularyVersion then Ok() else fail "$.vocabularyVersion" ("unsupported " + vocabulary)
        let! rawBinding = field "$" "binding" p
        let! b = fields "$.binding" [ "sourceManifestDigest"; "blueprintDigest"; "cohortId"; "decisionIds" ] [] rawBinding
        let! manifest = textField "$.binding" "sourceManifestDigest" b
        let! blueprint = textField "$.binding" "blueprintDigest" b
        let! cohort = textField "$.binding" "cohortId" b
        let! rawDecisions = field "$.binding" "decisionIds" b
        let! decisions = strings "$.binding.decisionIds" rawDecisions
        let! rawOperations = field "$" "operations" p
        let! operations = items "$.operations" operation rawOperations
        return
            { SchemaVersion = schema
              VocabularyVersion = vocabulary
              Binding = { SourceManifestDigest = manifest; BlueprintDigest = blueprint; CohortId = cohort; DecisionIds = decisions }
              Operations = operations }
    }

/// Human-ordered rendering of a draft. Identity is `ConstructionPlan.digest`,
/// taken over the canonical form, never over these bytes.
let encodePlan (plan: PlanDraft) = toJson plan |> render
