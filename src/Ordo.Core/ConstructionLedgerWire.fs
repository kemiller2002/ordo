/// Strict reader and canonical, content-addressed codec for
/// `ordo.requirements-ledger/1`. Reading yields what a document says, not
/// proof that Ordo derived it: check it with `ConstructionLedger.validateLedger`
/// against the inputs it names.
module Ordo.Core.ConstructionLedgerWire

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.StrictJson
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionLedger

// ---------------------------------------------------------------------------
// Names.
// ---------------------------------------------------------------------------

let private kindNames =
    [ Decision, "decision"
      Contract, "contract"
      Invariant, "invariant"
      Interface, "interface"
      Cohort, "cohort"
      VerificationObligation, "verificationObligation"
      NodeKind.Conflict, "conflict"
      Deferral, "deferral"
      EngineeringRationale, "engineeringRationale" ]

let private classNames =
    [ ResolutionClass.FixedByRequirement, "fixedByRequirement"
      ResolutionClass.AlreadyApproved, "alreadyApproved"
      ResolutionClass.MechanicallyDetermined, "mechanicallyDetermined"
      ResolutionClass.GenuineDomainDecision, "genuineDomainDecision"
      ResolutionClass.UnsupportedByVocabulary, "unsupportedByVocabulary" ]

let private verdictNames =
    [ AcceptanceVerdict.Passed, "passed"
      AcceptanceVerdict.Failed, "failed" ]

let private nameOf table value = table |> List.find (fst >> (=) value) |> snd

let private ofName what table path raw =
    match table |> List.tryFind (snd >> (=) raw) with
    | Some(value, _) -> Ok value
    | None -> fail path (sprintf "unsupported %s: %s" what raw)

let resolutionClassName = nameOf classNames

// ---------------------------------------------------------------------------
// Encoding.
// ---------------------------------------------------------------------------

let private jStrings values = JArray(values |> List.sort |> List.map JString)

let private jSource (s: SourceRequirement) =
    JObject [ "key", JString s.Key
              "originalId", JString s.OriginalId
              "document", JString s.Document
              "location", JString s.Location
              "revision", JString s.Revision
              "contentDigest", JString s.ContentDigest ]

let private jBlueprintDisposition disposition =
    match disposition with
    | Modeled -> JObject [ "kind", JString "modeled" ]
    | Deferred why -> JObject [ "kind", JString "deferred"; "reason", JString why ]
    | Unresolved why -> JObject [ "kind", JString "unresolved"; "reason", JString why ]
    | Rejected why -> JObject [ "kind", JString "rejected"; "reason", JString why ]
    | Superseded(target, why) -> JObject [ "kind", JString "superseded"; "targetKey", JString target; "reason", JString why ]

let private jDisposition disposition =
    match disposition with
    | FinalDisposition.Implemented -> JObject [ "kind", JString "implemented" ]
    | FinalDisposition.PartiallyImplemented why -> JObject [ "kind", JString "partiallyImplemented"; "rationale", JString why ]
    | FinalDisposition.Deferred why -> JObject [ "kind", JString "deferred"; "rationale", JString why ]
    | FinalDisposition.Unresolved why -> JObject [ "kind", JString "unresolved"; "rationale", JString why ]
    | FinalDisposition.Rejected why -> JObject [ "kind", JString "rejected"; "rationale", JString why ]
    | FinalDisposition.Superseded(decision, why) ->
        JObject [ "kind", JString "superseded"; "decisionId", JString decision; "rationale", JString why ]

let private jOption render value = value |> Option.map render |> Option.defaultValue JNull

let private jRow (row: LedgerRow) =
    JObject [ "requirement", jSource row.Requirement
              "categories", JArray(row.Categories |> List.sort |> List.map (nameOf kindNames >> JString))
              "blueprintDisposition", jBlueprintDisposition row.BlueprintDisposition
              "dependencyNodeIds", jStrings row.DependencyNodeIds
              "approvedDecisionIds", jStrings row.ApprovedDecisionIds
              "closingDecisionIds", jStrings row.ClosingDecisionIds
              "conflictNodeIds", jStrings row.ConflictNodeIds
              "cohortId", jOption JString row.CohortId
              "operationLabels", jStrings row.OperationLabels
              "generatedArtifacts", jStrings row.GeneratedArtifacts
              "extensionSurface", jStrings row.ExtensionSurface
              "openObligationIds", jStrings row.OpenObligationIds
              "verificationNodeIds", jStrings row.VerificationNodeIds
              "acceptanceCaseIds", jStrings row.AcceptanceCaseIds
              "acceptanceVerdict", jOption (nameOf verdictNames >> JString) row.Acceptance
              "resolutionClasses", JArray(row.Classes |> List.sort |> List.map (nameOf classNames >> JString))
              "disposition", jDisposition row.Disposition ]

/// Canonical serialization: sorted object members, sorted lists, rows in key
/// order. List order carries no meaning, so equal meaning gives equal bytes.
let encodeLedger (ledger: Ledger) =
    JObject [ "schemaVersion", JString ledger.SchemaVersion
              "sourceManifestDigest", JString ledger.SourceManifestDigest
              "blueprintDigest", JString ledger.BlueprintDigest
              "defaultPlanDigests", jStrings ledger.DefaultPlanDigests
              "rows",
              JArray(
                  ledger.Rows
                  |> List.map (fun row -> row.Requirement.Key, jRow row)
                  |> List.sortWith (fun (ka, a) (kb, b) ->
                      match String.CompareOrdinal(ka, kb) with
                      | 0 -> String.CompareOrdinal(renderCanonical a, renderCanonical b)
                      | c -> c)
                  |> List.map snd) ]
    |> renderCanonical

/// Content address of the canonical ledger: SHA-256 of `encodeLedger`.
let digest (ledger: Ledger) =
    encodeLedger ledger |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
    |> fun hex -> "sha256:" + hex.ToLowerInvariant()

// ---------------------------------------------------------------------------
// Reading. Strict: exactly the permitted members, once each.
// ---------------------------------------------------------------------------

let private readStrings path name p =
    result {
        let! raw = field path name p
        return! strings (path + "." + name) raw
    }

let private readSource path value =
    result {
        let! p = fields path [ "key"; "originalId"; "document"; "location"; "revision"; "contentDigest" ] [] value
        let! key = textField path "key" p
        let! originalId = textField path "originalId" p
        let! document = textField path "document" p
        let! location = textField path "location" p
        let! revision = textField path "revision" p
        let! contentDigest = textField path "contentDigest" p
        return { Key = key; OriginalId = originalId; Document = document; Location = location; Revision = revision; ContentDigest = contentDigest }
    }

let private kindOf (value: JsonValue) path =
    match value with
    | JObject properties ->
        match properties |> List.filter (fun (name, _) -> name = "kind") with
        | [ _, JString kind ] -> Ok kind
        | _ -> fail path "expected exactly one string member kind"
    | _ -> fail path "expected object"

let private readBlueprintDisposition path value =
    result {
        let! kind = kindOf value path
        match kind with
        | "modeled" ->
            let! _ = fields path [ "kind" ] [] value
            return Modeled
        | "deferred" | "unresolved" | "rejected" ->
            let! p = fields path [ "kind"; "reason" ] [] value
            let! why = textField path "reason" p
            return
                match kind with
                | "deferred" -> Deferred why
                | "unresolved" -> Unresolved why
                | _ -> Rejected why
        | "superseded" ->
            let! p = fields path [ "kind"; "targetKey"; "reason" ] [] value
            let! target = textField path "targetKey" p
            let! why = textField path "reason" p
            return Superseded(target, why)
        | other -> return! fail (path + ".kind") ("unsupported blueprint disposition: " + other)
    }

let private readDisposition path value =
    result {
        let! kind = kindOf value path
        let rationale () =
            result {
                let! p = fields path [ "kind"; "rationale" ] [] value
                return! textField path "rationale" p
            }
        match kind with
        | "implemented" ->
            let! _ = fields path [ "kind" ] [] value
            return FinalDisposition.Implemented
        | "partiallyImplemented" ->
            let! why = rationale ()
            return FinalDisposition.PartiallyImplemented why
        | "deferred" ->
            let! why = rationale ()
            return FinalDisposition.Deferred why
        | "unresolved" ->
            let! why = rationale ()
            return FinalDisposition.Unresolved why
        | "rejected" ->
            let! why = rationale ()
            return FinalDisposition.Rejected why
        | "superseded" ->
            let! p = fields path [ "kind"; "decisionId"; "rationale" ] [] value
            let! decision = textField path "decisionId" p
            let! why = textField path "rationale" p
            return FinalDisposition.Superseded(decision, why)
        | other -> return! fail (path + ".kind") ("unsupported disposition: " + other)
    }

let private readNames what table path name p =
    result {
        let! raw = readStrings path name p
        return!
            raw
            |> List.mapi (fun i r -> ofName what table (sprintf "%s.%s[%d]" path name i) r)
            |> List.fold (fun state next ->
                result {
                    let! prior = state
                    let! value = next
                    return value :: prior }) (Ok [])
            |> Result.map List.rev
    }

let private readOptional path name decode p =
    result {
        let! raw = field path name p
        match raw with
        | JNull -> return None
        | value ->
            let! text = asText (path + "." + name) value
            let! decoded = decode (path + "." + name) text
            return Some decoded
    }

let private rowMembers =
    [ "requirement"; "categories"; "blueprintDisposition"; "dependencyNodeIds"; "approvedDecisionIds"
      "closingDecisionIds"; "conflictNodeIds"; "cohortId"; "operationLabels"; "generatedArtifacts"
      "extensionSurface"; "openObligationIds"; "verificationNodeIds"; "acceptanceCaseIds"
      "acceptanceVerdict"; "resolutionClasses"; "disposition" ]

let private readRow path value =
    result {
        let! p = fields path rowMembers [] value
        let! rawSource = field path "requirement" p
        let! source = readSource (path + ".requirement") rawSource
        let! categories = readNames "node kind" kindNames path "categories" p
        let! rawBlueprint = field path "blueprintDisposition" p
        let! blueprintDisposition = readBlueprintDisposition (path + ".blueprintDisposition") rawBlueprint
        let! dependencies = readStrings path "dependencyNodeIds" p
        let! approved = readStrings path "approvedDecisionIds" p
        let! closing = readStrings path "closingDecisionIds" p
        let! conflicts = readStrings path "conflictNodeIds" p
        let! cohort = readOptional path "cohortId" (fun _ text -> Ok text) p
        let! labels = readStrings path "operationLabels" p
        let! artifacts = readStrings path "generatedArtifacts" p
        let! surface = readStrings path "extensionSurface" p
        let! open' = readStrings path "openObligationIds" p
        let! verification = readStrings path "verificationNodeIds" p
        let! cases = readStrings path "acceptanceCaseIds" p
        let! verdict = readOptional path "acceptanceVerdict" (ofName "acceptance verdict" verdictNames) p
        let! classes = readNames "resolution class" classNames path "resolutionClasses" p
        let! rawDisposition = field path "disposition" p
        let! disposition = readDisposition (path + ".disposition") rawDisposition
        return
            { Requirement = source
              Categories = categories
              BlueprintDisposition = blueprintDisposition
              DependencyNodeIds = dependencies
              ApprovedDecisionIds = approved
              ClosingDecisionIds = closing
              ConflictNodeIds = conflicts
              CohortId = cohort
              OperationLabels = labels
              GeneratedArtifacts = artifacts
              ExtensionSurface = surface
              OpenObligationIds = open'
              VerificationNodeIds = verification
              AcceptanceCaseIds = cases
              Acceptance = verdict
              Classes = classes
              Disposition = disposition }
    }

/// Strict reader for `ordo.requirements-ledger/1`. Unknown members, schema
/// versions, classes, kinds and dispositions are refused by name.
let readLedger (input: string) : Result<Ledger, string> =
    result {
        let! json = parse input |> Result.mapError (sprintf "invalid JSON: %A")
        let! p = fields "$" [ "schemaVersion"; "sourceManifestDigest"; "blueprintDigest"; "defaultPlanDigests"; "rows" ] [] json
        let! schema = textField "$" "schemaVersion" p
        let! () = if schema = LedgerSchemaVersion then Ok() else fail "$.schemaVersion" ("unsupported " + schema)
        let! manifestDigest = textField "$" "sourceManifestDigest" p
        let! blueprintDigest = textField "$" "blueprintDigest" p
        let! plans = readStrings "$" "defaultPlanDigests" p
        let! rawRows = field "$" "rows" p
        let! rows = items "$.rows" readRow rawRows
        return
            { SchemaVersion = schema
              SourceManifestDigest = manifestDigest
              BlueprintDigest = blueprintDigest
              DefaultPlanDigests = plans
              Rows = rows }
    }
