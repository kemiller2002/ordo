/// Strict ECIR/1 JSON reader and canonical content-addressed artifact codec.
/// Ordo owns semantics; a parsed model never grants execution authority.
module Ordo.Core.ConstructionIrWire

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.ConstructionIr

type private ResultBuilder() =
    member _.Bind(value, f) = Result.bind f value
    member _.Return(value) = Ok value
    member _.ReturnFrom(value) = value

let private result = ResultBuilder()
let private fail path message = Error(sprintf "%s: %s" path message)

let private fields path required optional value =
    match value with
    | JObject properties ->
        let names = properties |> List.map fst
        let dup = names |> List.countBy id |> List.tryFind (fun (_, count) -> count <> 1)
        let unknown = names |> List.tryFind (fun name -> not (List.contains name (required @ optional)))
        let missing = required |> List.tryFind (fun name -> not (List.contains name names))
        match dup, unknown, missing with
        | Some(name, _), _, _ -> fail path ("duplicate JSON member " + name)
        | _, Some name, _ -> fail path ("unknown JSON member " + name)
        | _, _, Some name -> fail path ("missing JSON member " + name)
        | _ -> Ok(Map.ofList properties)
    | _ -> fail path "expected object"

let private field path name (properties: Map<string, JsonValue>) =
    match Map.tryFind name properties with
    | Some value -> Ok value
    | None -> fail path ("missing " + name)

let private asText path value =
    match value with
    | JString str when not (String.IsNullOrWhiteSpace str) -> Ok str
    | _ -> fail path "expected nonblank string"

let private textField path key properties =
    result {
        let! raw = field path key properties
        return! asText (path + "." + key) raw
    }

let private strings path value =
    match value with
    | JArray items ->
        items |> List.mapi (fun index item -> asText (sprintf "%s[%d]" path index) item)
        |> List.fold (fun state next ->
            result { let! prior = state
                     let! value = next
                     return value :: prior }) (Ok [])
        |> Result.map List.rev
    | _ -> fail path "expected array of strings"

let private items path decoder value =
    match value with
    | JArray values ->
        values
        |> List.mapi (fun index v -> decoder (sprintf "%s[%d]" path index) v)
        |> List.fold (fun state next ->
            result { let! prior = state
                     let! item = next
                     return item :: prior }) (Ok [])
        |> Result.map List.rev
    | _ -> fail path "expected array"

let private source path value =
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

let private disposition path value =
    result {
        let! p = fields path [ "kind" ] [ "reason"; "targetKey" ] value
        let! kind = textField path "kind" p
        let reason () = textField path "reason" p
        let only expected =
            if Set.ofSeq (Map.keys p) = Set.ofList expected then Ok ()
            else fail path ("incorrect fields for disposition " + kind)
        match kind with
        | "modeled" ->
            let! () = only [ "kind" ]
            return Modeled
        | "deferred" | "unresolved" | "rejected" ->
            let! () = only [ "kind"; "reason" ]
            let! explanation = reason ()
            return match kind with
                   | "deferred" -> Deferred explanation
                   | "unresolved" -> Unresolved explanation
                   | _ -> Rejected explanation
        | "superseded" ->
            let! () = only [ "kind"; "reason"; "targetKey" ]
            let! explanation = reason ()
            let! target = textField path "targetKey" p
            return Superseded(target, explanation)
        | _ -> return! fail path ("unknown disposition: " + kind)
    }

let private requirement path value =
    result {
        let! p = fields path [ "source"; "disposition"; "nodeIds" ] [] value
        let! sourceValue = field path "source" p
        let! source = source (path + ".source") sourceValue
        let! dispositionValue = field path "disposition" p
        let! state = disposition (path + ".disposition") dispositionValue
        let! refs = field path "nodeIds" p
        let! ids = strings (path + ".nodeIds") refs
        return { Source = source; Disposition = state; NodeIds = ids }
    }

let private nodeKind path name =
    match name with
    | "decision" -> Ok Decision
    | "contract" -> Ok Contract
    | "invariant" -> Ok Invariant
    | "interface" -> Ok Interface
    | "cohort" -> Ok Cohort
    | "verificationObligation" -> Ok VerificationObligation
    | "conflict" -> Ok Conflict
    | "deferral" -> Ok Deferral
    | "engineeringRationale" -> Ok EngineeringRationale
    | _ -> fail path ("unknown node kind: " + name)

let private node path value =
    result {
        let! p = fields path [ "id"; "kind"; "requirementKeys"; "dependsOn" ] [ "justification" ] value
        let! id = textField path "id" p
        let! kindText = textField path "kind" p
        let! kind = nodeKind (path + ".kind") kindText
        let! keysValue = field path "requirementKeys" p
        let! keys = strings (path + ".requirementKeys") keysValue
        let! dependenciesValue = field path "dependsOn" p
        let! dependencies = strings (path + ".dependsOn") dependenciesValue
        let! justification =
            match Map.tryFind "justification" p with
            | None -> Ok None
            | Some raw -> asText (path + ".justification") raw |> Result.map Some
        return { Id = id; Kind = kind; RequirementKeys = keys; DependsOn = dependencies; Justification = justification }
    }

let private parseJson input =
    Ordo.Core.Json.parse input |> Result.mapError (fun error -> sprintf "invalid JSON: %A" error)

let readManifest input : Result<SourceManifest, string> =
    result {
        let! json = parseJson input
        let! p = fields "$" [ "digest"; "requirements" ] [] json
        let! digest = textField "$" "digest" p
        let! raw = field "$" "requirements" p
        let! requirements = items "$.requirements" source raw
        return { Digest = digest; Requirements = requirements }
    }

let readBlueprint input : Result<Blueprint, string> =
    result {
        let! json = parseJson input
        let! p = fields "$" [ "schemaVersion"; "sourceManifestDigest"; "requirements"; "nodes" ] [] json
        let! version = textField "$" "schemaVersion" p
        if version <> SchemaVersion then
            return! fail "$.schemaVersion" ("unsupported " + version)
        let! digest = textField "$" "sourceManifestDigest" p
        let! rs = field "$" "requirements" p
        let! requirements = items "$.requirements" requirement rs
        let! ns = field "$" "nodes" p
        let! nodes = items "$.nodes" node ns
        return { SchemaVersion = version; SourceManifestDigest = digest; Requirements = requirements; Nodes = nodes }
    }

let private jSource (s: SourceRequirement) =
    JObject [ "key", JString s.Key
              "originalId", JString s.OriginalId
              "document", JString s.Document
              "location", JString s.Location
              "revision", JString s.Revision
              "contentDigest", JString s.ContentDigest ]

let private jDisposition state =
    match state with
    | Modeled -> JObject [ "kind", JString "modeled" ]
    | Deferred why -> JObject [ "kind", JString "deferred"; "reason", JString why ]
    | Unresolved why -> JObject [ "kind", JString "unresolved"; "reason", JString why ]
    | Rejected why -> JObject [ "kind", JString "rejected"; "reason", JString why ]
    | Superseded(target, why) ->
        JObject [ "kind", JString "superseded"; "reason", JString why; "targetKey", JString target ]

let private kindName kind =
    match kind with
    | Decision -> "decision"
    | Contract -> "contract"
    | Invariant -> "invariant"
    | Interface -> "interface"
    | Cohort -> "cohort"
    | VerificationObligation -> "verificationObligation"
    | Conflict -> "conflict"
    | Deferral -> "deferral"
    | EngineeringRationale -> "engineeringRationale"

let private jStrings values = JArray(List.map JString values)

let private jBlueprint (blueprint: Blueprint) =
    JObject [
        "schemaVersion", JString blueprint.SchemaVersion
        "sourceManifestDigest", JString blueprint.SourceManifestDigest
        "requirements", JArray [
            for r in blueprint.Requirements do
                JObject [ "source", jSource r.Source
                          "disposition", jDisposition r.Disposition
                          "nodeIds", jStrings r.NodeIds ] ]
        "nodes", JArray [
            for node in blueprint.Nodes do
                JObject ([ "id", JString node.Id
                           "kind", JString(kindName node.Kind)
                           "requirementKeys", jStrings node.RequirementKeys
                           "dependsOn", jStrings node.DependsOn ]
                         @ (node.Justification |> Option.map (fun why -> [ "justification", JString why ])
                            |> Option.defaultValue [])) ] ]

/// Canonical means stable object-member order, plus normalized sets for the
/// artifact's identity references. Input order in an array does not silently
/// affect the content-addressed construction contract.
let canonicalBlueprint (b: Blueprint) =
    let sorted =
        { b with
            Requirements =
                b.Requirements |> List.map (fun r -> { r with NodeIds = List.sort r.NodeIds })
                |> List.sortBy (fun r -> r.Source.Key)
            Nodes =
                b.Nodes |> List.map (fun n ->
                    { n with RequirementKeys = List.sort n.RequirementKeys; DependsOn = List.sort n.DependsOn })
                |> List.sortBy (fun n -> n.Id) }
    sorted |> jBlueprint |> renderCanonical

let private sha256 (value: string) =
    value |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
    |> fun hex -> "sha256:" + hex.ToLowerInvariant()

let blueprintDigest blueprint = canonicalBlueprint blueprint |> sha256

/// The digest is independent of JSON whitespace, property order, array order,
/// and delimiter collisions. Each source field is UTF-8 byte-length framed.
let manifestDigest (requirements: SourceRequirement list) =
    let builder = StringBuilder("ecir-source-manifest/1\n")
    let frame (value: string) =
        let size = Encoding.UTF8.GetByteCount value
        builder.Append(size).Append(':').Append(value) |> ignore
    requirements
    |> List.sortBy (fun source -> source.Key)
    |> List.iter (fun source ->
        [ source.Key; source.OriginalId; source.Document; source.Location
          source.Revision; source.ContentDigest ] |> List.iter frame)
    sha256 (builder.ToString())

let verifyManifestDigest (manifest: SourceManifest) : Result<unit, string> =
    if manifest.Digest = manifestDigest manifest.Requirements then Ok ()
    else Error "source manifest digest does not match canonical source identity/content"

let encodeManifest (manifest: SourceManifest) =
    JObject [
        "digest", JString manifest.Digest
        "requirements", JArray(manifest.Requirements |> List.map jSource) ]
    |> render

let encodeBlueprint blueprint = jBlueprint blueprint |> render

let validatePinned (manifest: SourceManifest) (blueprint: Blueprint) =
    match verifyManifestDigest manifest with
    | Error problem -> Error [ problem ]
    | Ok () ->
        match validate manifest blueprint with
        | [] -> Ok (blueprintDigest blueprint)
        | errors -> Error(errors |> List.map (sprintf "%A"))

let validatePinnedCohort manifest blueprint cohort approvals =
    match validatePinned manifest blueprint with
    | Error problems -> Error problems
    | Ok digest ->
        match validateCohort manifest blueprint cohort approvals with
        | [] -> Ok digest
        | issues -> Error(issues |> List.map (sprintf "%A"))
