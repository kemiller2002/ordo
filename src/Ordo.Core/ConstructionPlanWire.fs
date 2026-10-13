/// Strict reader and writer for `ordo.construction-plan/1`. Reading yields an
/// untrusted `PlanDraft`; only `ConstructionPlan.validate` against a prepared
/// cohort can produce a `ValidatedPlan`. An operation, port or predicate the
/// vocabulary does not name is refused by name, never ignored or guessed.
///
/// The members an operation may carry depend on the plan's vocabulary:
/// `onFailure` and `effect` exist only in vocabulary 2, so a vocabulary 1
/// plan that carries one is refused as an unknown member.
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

let private disposition path raw =
    match raw with
    | "remain" -> Ok EffectFailureDisposition.Remain
    | "proceed" -> Ok EffectFailureDisposition.Proceed
    | "unresolved" -> Ok EffectFailureDisposition.Unresolved
    | other -> fail path ("unsupported onFailure: " + other)

let private guard path value =
    result {
        let! p = fields path [ "name"; "predicate" ] [] value
        let! name = textField path "name" p
        let! kind = textField path "predicate" p
        let! decided = predicate (path + ".predicate") kind
        return { GuardDraft.Name = name; Predicate = decided }
    }

let private operation (vocabulary: string) path value =
    // Vocabularies 2 and 3 both require a failure disposition on every effect.
    let second = vocabulary <> VocabularyVersion1
    let notIn kind = fail (path + ".op") (sprintf "operation %s is not in %s" kind vocabulary)
    // Vocabulary 4 keeps every vocabulary 3 operation.
    let onlyInThird kind = if classifiesChoices vocabulary then Ok() else notIn kind
    let onlyInFourth kind = if vocabulary = VocabularyVersion4 then Ok() else notIn kind
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
            let! p = fields path [ "op"; "port"; "trace" ] ([ "command" ] @ (if second then [ "onFailure" ] else [])) value
            let! port = textField path "port" p
            let onFailure () =
                match second, Map.containsKey "onFailure" p with
                | false, _ -> Ok None
                | true, true ->
                    textField path "onFailure" p |> Result.bind (disposition (path + ".onFailure")) |> Result.map Some
                | true, false -> fail path ("missing JSON member onFailure: " + VocabularyVersion2 + " requires a failure disposition for every transitionEffect port")
            let! kindOfPort =
                match port, Map.containsKey "command" p, Map.containsKey "onFailure" p with
                | "currentTime", false, false -> Ok CurrentTime
                | "transitionEffect", true, _ ->
                    result {
                        let! command = textField path "command" p
                        let! decided = onFailure ()
                        return TransitionEffect(command, decided)
                    }
                | ("currentTime" | "transitionEffect"), _, _ -> fail path ("incorrect fields for port " + port)
                | other, _, _ -> fail (path + ".port") ("unsupported port: " + other)
            let! t = traceField path p
            return DeclarePort { Kind = kindOfPort; Trace = t }
        | "bindCapability" ->
            let! p = fields path [ "op"; "command"; "capability"; "trace" ] [] value
            let! command = textField path "command" p
            let! capability = textField path "capability" p
            let! t = traceField path p
            return BindCapability { Command = command; Capability = capability; Trace = t }
        | "declareUnresolved" ->
            let! p = fields path [ "op"; "subject"; "question"; "trace" ] (if second then [ "effect" ] else []) value
            let! subject = textField path "subject" p
            let! effect =
                if Map.containsKey "effect" p then textField path "effect" p |> Result.map Some else Ok None
            let! question = textField path "question" p
            let! t = traceField path p
            return DeclareUnresolved { Subject = subject; Effect = effect; Question = question; Trace = t }
        | "declarePairRule" ->
            let! () = onlyInThird kind
            let! p = fields path [ "op"; "unlisted"; "trace" ] [] value
            let! text = textField path "unlisted" p
            let! unlisted =
                match text with
                | "refused" -> Ok UnlistedPairs.Refused
                | "undecided" -> Ok UnlistedPairs.Undecided
                | other -> fail (path + ".unlisted") ("unsupported unlisted-pair rule: " + other)
            let! t = traceField path p
            return DeclarePairRule { Unlisted = unlisted; Trace = t }
        | "defineRefusal" ->
            let! () = onlyInThird kind
            let! p = fields path [ "op"; "command"; "from"; "trace" ] [] value
            let! command = textField path "command" p
            let! from = textField path "from" p
            let! t = traceField path p
            return DefineRefusal { Pair = { InState = from; OnCommand = command }; Trace = t }
        | "declareUndecidedTransition" ->
            let! () = onlyInThird kind
            let! p = fields path [ "op"; "command"; "from"; "question"; "trace" ] [] value
            let! command = textField path "command" p
            let! from = textField path "from" p
            let! question = textField path "question" p
            let! t = traceField path p
            return DeclareUndecidedTransition { Pair = { InState = from; OnCommand = command }; Question = question; Trace = t }
        | "declareExtensionInventory" ->
            let! () = onlyInThird kind
            let! p = fields path [ "op"; "extension"; "decisionPoints"; "trace" ] [] value
            let! rawExtension = field path "extension" p
            let extensionPath = path + ".extension"
            let! extensionKind =
                match rawExtension with
                | JObject properties ->
                    match properties |> List.filter (fun (n, _) -> n = "kind") with
                    | [ _, JString k ] -> Ok k
                    | _ -> fail extensionPath "expected exactly one string member kind"
                | _ -> fail extensionPath "expected object"
            let named make =
                fields extensionPath [ "kind"; "name" ] [] rawExtension
                |> Result.bind (textField extensionPath "name")
                |> Result.map make
            let! extension =
                match extensionKind with
                | "guard" -> named ExtensionRef.Guard
                | "invariant" -> named ExtensionRef.Invariant
                | "effect" -> named ExtensionRef.Effect
                | "selection" ->
                    result {
                        let! () = if vocabulary = VocabularyVersion4 then Ok() else fail (extensionPath + ".kind") ("extension kind selection is not in " + vocabulary)
                        let! e = fields extensionPath [ "kind"; "from"; "command" ] [] rawExtension
                        let! from = textField extensionPath "from" e
                        let! command = textField extensionPath "command" e
                        return ExtensionRef.Selection(from, command)
                    }
                | other -> fail (extensionPath + ".kind") ("unsupported extension kind: " + other)
            let point pointPath pointValue =
                result {
                    let! decided =
                        match pointValue with
                        | JObject properties ->
                            match properties |> List.filter (fun (n, _) -> n = "decided") with
                            | [ _, JBool b ] -> Ok b
                            | _ -> fail pointPath "expected exactly one boolean member decided"
                        | _ -> fail pointPath "expected object"
                    let! q = fields pointPath ([ "point"; "decided"; "trace" ] @ (if decided then [] else [ "question" ])) [] pointValue
                    let! pointName = textField pointPath "point" q
                    let! resolution =
                        if decided then Ok DecisionPointResolution.Decided
                        else textField pointPath "question" q |> Result.map DecisionPointResolution.Undecided
                    let! t = traceField pointPath q
                    return { DecisionPointDraft.Point = pointName; Resolution = resolution; Trace = t }
                }
            let! rawPoints = field path "decisionPoints" p
            let! points = items (path + ".decisionPoints") point rawPoints
            let! t = traceField path p
            return DeclareExtensionInventory { Extension = extension; DecisionPoints = points; Trace = t }
        | "defineRefusalCode" ->
            let! () = onlyInFourth kind
            let! p = fields path [ "op"; "code"; "trace" ] [] value
            let! code = textField path "code" p
            let! t = traceField path p
            return DefineRefusalCode { Code = code; Trace = t }
        | "assignRefusalCode" ->
            let! () = onlyInFourth kind
            let! p = fields path [ "op"; "target"; "code"; "trace" ] [] value
            let! rawTarget = field path "target" p
            let targetPath = path + ".target"
            let! targetKind =
                match rawTarget with
                | JObject properties ->
                    match properties |> List.filter (fun (n, _) -> n = "kind") with
                    | [ _, JString k ] -> Ok k
                    | _ -> fail targetPath "expected exactly one string member kind"
                | _ -> fail targetPath "expected object"
            let! target =
                match targetKind with
                | "pair" ->
                    fields targetPath [ "kind"; "from"; "command" ] [] rawTarget
                    |> Result.bind (fun q ->
                        result {
                            let! from = textField targetPath "from" q
                            let! command = textField targetPath "command" q
                            return CodeTarget.Pair { InState = from; OnCommand = command }
                        })
                | "unlisted" -> fields targetPath [ "kind" ] [] rawTarget |> Result.map (fun _ -> CodeTarget.Unlisted)
                | "guard" ->
                    fields targetPath [ "kind"; "name" ] [] rawTarget
                    |> Result.bind (textField targetPath "name")
                    |> Result.map CodeTarget.Guard
                | other -> fail (targetPath + ".kind") ("unsupported refusal-code target: " + other)
            let! code = textField path "code" p
            let! t = traceField path p
            return AssignRefusalCode { Target = target; Code = code; Trace = t }
        | "defineBranchingTransition" ->
            let! () = onlyInFourth kind
            let! p = fields path [ "op"; "command"; "from"; "targets"; "trace" ] [] value
            let! command = textField path "command" p
            let! from = textField path "from" p
            let! rawTargets = field path "targets" p
            let! targets = strings (path + ".targets") rawTargets
            let! t = traceField path p
            return DefineBranchingTransition { Pair = { InState = from; OnCommand = command }; Targets = targets; Trace = t }
        | other -> return! fail (path + ".op") ("unsupported operation: " + other)
    }

let readPlan (input: string) : Result<PlanDraft, string> =
    result {
        let! json = parse input |> Result.mapError (sprintf "invalid JSON: %A")
        let! p = fields "$" [ "schemaVersion"; "vocabularyVersion"; "binding"; "operations" ] [] json
        let! schema = textField "$" "schemaVersion" p
        let! () = if schema = PlanSchemaVersion then Ok() else fail "$.schemaVersion" ("unsupported " + schema)
        let! vocabulary = textField "$" "vocabularyVersion" p
        let! () =
            if List.contains vocabulary SupportedVocabularyVersions then Ok()
            else fail "$.vocabularyVersion" ("unsupported " + vocabulary)
        let! rawBinding = field "$" "binding" p
        let! b = fields "$.binding" [ "sourceManifestDigest"; "blueprintDigest"; "cohortId"; "decisionIds" ] [] rawBinding
        let! manifest = textField "$.binding" "sourceManifestDigest" b
        let! blueprint = textField "$.binding" "blueprintDigest" b
        let! cohort = textField "$.binding" "cohortId" b
        let! rawDecisions = field "$.binding" "decisionIds" b
        let! decisions = strings "$.binding.decisionIds" rawDecisions
        let! rawOperations = field "$" "operations" p
        let! operations = items "$.operations" (operation vocabulary) rawOperations
        return
            { SchemaVersion = schema
              VocabularyVersion = vocabulary
              Binding = { SourceManifestDigest = manifest; BlueprintDigest = blueprint; CohortId = cohort; DecisionIds = decisions }
              Operations = operations }
    }

/// Human-ordered rendering of a draft. Identity is `ConstructionPlan.digest`,
/// taken over the canonical form, never over these bytes.
let encodePlan (plan: PlanDraft) = toJson plan |> render
