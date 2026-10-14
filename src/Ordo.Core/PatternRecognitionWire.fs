/// Canonical wire forms for pattern recognition: the recognition report
/// (`ordo.pattern-recognition/1`), the delegation packet
/// (`ordo.delegation-contract/1`), the registry pin, and the strict reader
/// for an agent's completion (`ordo.delegation-completion/1`). Equal meaning
/// gives equal bytes; identity is the SHA-256 of those bytes.
module Ordo.Core.PatternRecognitionWire

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.StrictJson
open Ordo.Core.PatternRegistry
open Ordo.Core.PatternRecognition

let private sha256 (text: string) =
    text |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
    |> fun hex -> "sha256:" + hex.ToLowerInvariant()

let private jStrings (values: string list) = JArray(values |> List.map JString)
let private jRef (r: PatternRef) = JObject [ "id", JString r.Id; "version", JInt(int64 r.Version) ]
let private jRefs refs = JArray(refs |> List.map jRef)
let private jOption f value = value |> Option.map f |> Option.defaultValue JNull

let private jGap gap =
    match gap with
    | EffectFailureUndecided(node, command) ->
        JObject [ "kind", JString "effectFailureUndecided"; "recordNodeId", JString node; "command", JString command ]
    | DecisionNotAuthorized id -> JObject [ "kind", JString "decisionNotAuthorized"; "decisionId", JString id ]
    | PatternSelectionRequired patterns -> JObject [ "kind", JString "patternSelectionRequired"; "patterns", jRefs patterns ]
    | ConflictUnresolved id -> JObject [ "kind", JString "conflictUnresolved"; "conflictId", JString id ]

let private jBlocker blocker =
    match blocker with
    | InvalidSource violations -> JObject [ "kind", JString "invalidSource"; "violations", jStrings violations ]
    | DispositionUnresolved reason -> JObject [ "kind", JString "dispositionUnresolved"; "reason", JString reason ]
    | UnusableContent(pattern, defects) -> JObject [ "kind", JString "unusableContent"; "pattern", jRef pattern; "defects", jStrings defects ]
    | AwaitingPrerequisites keys -> JObject [ "kind", JString "awaitingPrerequisites"; "requirementKeys", jStrings keys ]
    | GenerationRefused refusals -> JObject [ "kind", JString "generationRefused"; "refusals", jStrings refusals ]

let outcomeName outcome =
    match outcome with
    | Constructible _ -> "constructible"
    | DecisionRequired _ -> "decisionRequired"
    | Delegate _ -> "delegate"
    | Blocked _ -> "blocked"

let private jOutcome outcome =
    match outcome with
    | Constructible(patterns, delegated) ->
        JObject [ "kind", JString "constructible"; "patterns", jRefs patterns; "delegatedPrerequisites", jStrings delegated ]
    | DecisionRequired(gaps, generated) ->
        JObject [ "kind", JString "decisionRequired"; "gaps", JArray(List.map jGap gaps); "generatedBy", jRefs generated ]
    | Delegate(interfaces, generated) ->
        JObject [ "kind", JString "delegate"; "interfaces", jStrings interfaces; "generatedBy", jRefs generated ]
    | Blocked blockers -> JObject [ "kind", JString "blocked"; "blockers", JArray(List.map jBlocker blockers) ]

let private jClaim (c: PatternClaim) =
    JObject [ "pattern", jRef c.Pattern
              "kind",
              (match c.Kind with
               | ClaimKind.Constructs -> JObject [ "kind", JString "constructs" ]
               | ClaimKind.InterfaceOnly s -> JObject [ "kind", JString "interfaceOnly"; "surfaces", jStrings s ])
              "evidence", jStrings c.Evidence
              "gaps", JArray(List.map jGap c.Gaps)
              "defects", jStrings c.Defects ]

let private jNode (n: NodeReference) =
    JObject [ "nodeId", JString n.NodeId; "justification", jOption JString n.Justification; "resolves", jStrings n.Resolves ]

let private jSource (s: SourceReference) =
    JObject [ "key", JString s.Key; "originalId", JString s.OriginalId; "document", JString s.Document
              "location", JString s.Location; "revision", JString s.Revision; "contentDigest", JString s.ContentDigest ]

let packetJson (p: DelegationPacket) =
    JObject [ "schemaVersion", JString DelegationSchemaVersion
              "id", JString p.Id
              "cohortId", JString p.CohortId
              "requirementKeys", jStrings p.RequirementKeys
              "sources", JArray(List.map jSource p.Sources)
              "approvedDecisions", JArray(List.map jNode p.ApprovedDecisions)
              "interfaces", jStrings p.Interfaces
              "contracts", JArray(List.map jNode p.Contracts)
              "invariants", JArray(List.map jNode p.Invariants)
              "verificationObligations", JArray(List.map jNode p.VerificationObligations)
              "generatedBy", jRefs p.GeneratedBy
              "allowedPaths", jStrings p.AllowedPaths
              "forbiddenPaths", jStrings p.ForbiddenPaths
              "architectureConstraints", jStrings p.ArchitectureConstraints
              "openQuestions", JArray(p.OpenQuestions |> List.map (fun (subject, q) -> JObject [ "subject", JString subject; "question", JString q ]))
              "escalationRequired", jStrings p.EscalationRequired
              "completionSchema", JString CompletionSchemaVersion ]

let encodePacket packet = packetJson packet |> renderCanonical
let packetDigest packet = encodePacket packet |> sha256

let pinJson (pin: RegistryPin) =
    JObject [ "registryVersion", JString pin.RegistryVersion
              "patterns", JArray(pin.Patterns |> List.map (fun (r, d) -> JObject [ "id", JString r.Id; "version", JInt(int64 r.Version); "contractDigest", JString d ])) ]

let encodePin pin = pinJson pin |> renderCanonical

let private jMeasurements (m: Measurements) =
    let n (v: int) = JInt(int64 v)
    JObject [ "assignedRequirements", n m.AssignedRequirements; "evaluated", n m.Evaluated; "excluded", n m.Excluded
              "constructible", n m.Constructible; "decisionRequired", n m.DecisionRequired; "delegated", n m.Delegated
              "blocked", n m.Blocked; "partiallyGenerated", n m.PartiallyGenerated; "patternsSelected", n m.PatternsSelected
              "claimEvaluations", n m.ClaimEvaluations; "delegationPackets", n m.DelegationPackets; "delegationBytes", n m.DelegationBytes ]

let reportJson (r: RecognitionReport) =
    JObject [ "schemaVersion", JString r.SchemaVersion
              "cohortId", JString r.CohortId
              "sourceManifestDigest", JString r.SourceManifestDigest
              "blueprintDigest", JString r.BlueprintDigest
              "decisionRecordsDigest", jOption JString r.DecisionRecordsDigest
              "pin", pinJson r.Pin
              "requirements",
              JArray(r.Requirements |> List.map (fun x ->
                  JObject [ "key", JString x.Key; "originalId", JString x.OriginalId; "outcome", jOutcome x.Outcome
                            "decisionClass", jOption JString (decisionClass x.Outcome)
                            "claims", JArray(List.map jClaim x.Claims) ]))
              "excluded",
              JArray(r.Excluded |> List.map (fun e -> JObject [ "key", JString e.Key; "disposition", JString e.Disposition; "reason", JString e.Reason ]))
              "decisions",
              JArray(r.Decisions |> List.map (fun d ->
                  JObject [ "id", JString d.Id; "gap", jGap d.Gap; "requirementKeys", jStrings d.RequirementKeys; "question", JString d.Question ]))
              "packets", JArray(r.Packets |> List.map (fun p -> JObject [ "id", JString p.Id; "digest", JString(packetDigest p); "requirementKeys", jStrings p.RequirementKeys ]))
              "generationWorthwhile", JBool r.GenerationWorthwhile
              "completeConstruction", JBool r.CompleteConstruction
              "measurements", jMeasurements r.Measurements ]

let encodeReport report = reportJson report |> renderCanonical
let reportDigest report = encodeReport report |> sha256

/// The recognition pass with the canonical packet encoding as its size measure.
let recognize registry pin input = recognizeWith encodePacket registry pin input

let applyGeneration refusals report = applyGenerationWith encodePacket refusals report

// ---------------------------------------------------------------------------
// Readers.
// ---------------------------------------------------------------------------

let private intField path name p =
    result {
        let! raw = field path name p
        match raw with
        | JInt v when v >= 1L && v <= 1_000_000_000L -> return int v
        | _ -> return! fail (path + "." + name) "expected a positive integer"
    }

/// Strict reader for a stored pin.
let readPin (input: string) : Result<RegistryPin, string> =
    result {
        let! json = parse input |> Result.mapError (sprintf "invalid JSON: %A")
        let! p = fields "$" [ "registryVersion"; "patterns" ] [] json
        let! version = textField "$" "registryVersion" p
        let! raw = field "$" "patterns" p
        let! patterns =
            items "$.patterns" (fun path value ->
                result {
                    let! q = fields path [ "id"; "version"; "contractDigest" ] [] value
                    let! id = textField path "id" q
                    let! v = intField path "version" q
                    let! digest = textField path "contractDigest" q
                    return ({ PatternRef.Id = id; Version = v }: PatternRef), digest
                }) raw
        return { RegistryVersion = version; Patterns = patterns }
    }

let private disposition path value =
    result {
        let! p = fields path [ "requirementKey"; "status" ] [ "reason" ] value
        let! key = textField path "requirementKey" p
        let! status = textField path "status" p
        let reason () = match Map.tryFind "reason" p with Some(JString r) -> Ok r | _ -> fail path "reason is required for this status"
        match status with
        | "implemented" ->
            if Map.containsKey "reason" p then return! fail path "implemented takes no reason"
            else return key, CompletionDisposition.Implemented
        | "notImplemented" ->
            let! r = reason ()
            return key, CompletionDisposition.NotImplemented r
        | "escalated" ->
            let! r = reason ()
            return key, CompletionDisposition.Escalated r
        | other -> return! fail (path + ".status") ("unknown status: " + other)
    }

/// Strict reader for an agent's completion. The result is untrusted until
/// `validateCompletion` checks it against the packet it answers.
let readCompletion (input: string) : Result<DelegationCompletion, string> =
    result {
        let! json = parse input |> Result.mapError (sprintf "invalid JSON: %A")
        let! p = fields "$" [ "schemaVersion"; "packetId"; "packetDigest"; "artifacts"; "dispositions"; "verificationEvidence" ] [] json
        let! schema = textField "$" "schemaVersion" p
        let! packetId = textField "$" "packetId" p
        let! digest = textField "$" "packetDigest" p
        let! rawArtifacts = field "$" "artifacts" p
        let! artifacts =
            items "$.artifacts" (fun path value ->
                result {
                    let! q = fields path [ "path"; "contentDigest" ] [] value
                    let! file = textField path "path" q
                    let! d = textField path "contentDigest" q
                    return { Path = file; ContentDigest = d }
                }) rawArtifacts
        let! rawDispositions = field "$" "dispositions" p
        let! dispositions = items "$.dispositions" disposition rawDispositions
        let! rawEvidence = field "$" "verificationEvidence" p
        let! evidence =
            items "$.verificationEvidence" (fun path value ->
                result {
                    let! q = fields path [ "obligationId"; "evidence" ] [] value
                    let! id = textField path "obligationId" q
                    let! e = textField path "evidence" q
                    return id, e
                }) rawEvidence
        return
            { SchemaVersion = schema
              PacketId = packetId
              PacketDigest = digest
              Artifacts = artifacts
              Dispositions = dispositions
              VerificationEvidence = evidence }
    }
