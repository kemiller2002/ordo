/// Producer-side oracle for manifest-free contract admission. The golden
/// corpus in examples/ecir/consumer is what Praxis and Dokimos copy; these
/// tests prove Ordo itself still produces exactly what that corpus promises.
module Ordo.Tests.ConstructibilityAdmissionTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open Xunit
open Ordo.Core.Json
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructibilityContract

let private consumerDir () =
    let rec find (current: DirectoryInfo) remaining =
        let candidate = Path.Combine(current.FullName, "examples", "ecir", "consumer", "expectations.json")
        if File.Exists candidate then Path.Combine(current.FullName, "examples", "ecir", "consumer")
        elif remaining <= 0 then failwith "consumer contract fixtures missing from repository"
        else
            match current.Parent with
            | null -> failwith "consumer contract fixtures missing from repository"
            | parent -> find parent (remaining - 1)
    find (DirectoryInfo AppContext.BaseDirectory) 10

let private read name = File.ReadAllText(Path.Combine(consumerDir (), name))

let private sha256Text (value: string) =
    value |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
    |> fun hex -> "sha256:" + hex.ToLowerInvariant()

let private text name value =
    match value with
    | JObject members ->
        match members |> List.tryFind (fst >> (=) name) with
        | Some(_, JString s) -> Some s
        | _ -> None
    | _ -> None

let private member' name value =
    match value with
    | JObject members -> members |> List.tryFind (fst >> (=) name) |> Option.map snd
    | _ -> None

let private stringList value =
    match value with
    | Some(JArray items) -> items |> List.map (function JString s -> s | other -> failwithf "not a string: %A" other)
    | _ -> failwith "expected string array"

let private cases () =
    match parse (read "expectations.json") |> Result.toOption |> Option.bind (member' "cases") with
    | Some(JArray items) -> items
    | _ -> failwith "expectations.json has no cases"

let private validDigest () =
    match admitContract (read "valid-contract.json") with
    | Ok identity -> identity.ContractDigest
    | Error failure -> failwithf "%A" failure

/// Every array in ecir/1 is set-like and every object is a record, so
/// reversing all of them must never change the canonical bytes.
let rec private reverseEverything value =
    match value with
    | JObject members -> JObject(members |> List.rev |> List.map (fun (k, v) -> k, reverseEverything v))
    | JArray items -> JArray(items |> List.rev |> List.map reverseEverything)
    | other -> other

let rec private rotateEverything value =
    match value with
    | JObject members -> JObject((match members with [] -> [] | head :: rest -> rest @ [ head ]) |> List.map (fun (k, v) -> k, rotateEverything v))
    | JArray items -> JArray((match items with [] -> [] | head :: rest -> rest @ [ head ]) |> List.map rotateEverything)
    | other -> other

[<Fact>]
let ``every golden consumer case is reproduced exactly by the producer`` () =
    let all = cases ()
    Assert.True(List.length all >= 15)
    for case in all do
        let name = text "name" case |> Option.defaultValue "?"
        let contract = text "contract" case |> Option.get |> read
        let outcome = admitPinnedContract (text "expectedDigest" case) contract
        match text "outcome" case, outcome with
        | Some "admitted", Ok identity ->
            let expected = member' "identity" case |> Option.get
            Assert.True(Some identity.ContractSchemaVersion = text "contractSchemaVersion" expected, name)
            Assert.True(Some identity.ContractDigest = text "contractDigest" expected, name)
            Assert.Equal<string list>(stringList (member' "requirementKeys" expected), identity.RequirementKeys)
            Assert.Equal<string list>(stringList (member' "decisionIds" expected), identity.DecisionIds)
            Assert.Equal<string list>(stringList (member' "conflictIds" expected), identity.ConflictIds)
        | Some "rejected", Error failure ->
            let expected =
                match member' "diagnostics" case with
                | Some(JArray items) -> items |> List.map (fun item -> text "code" item |> Option.get, text "subject" item)
                | _ -> failwith name
            Assert.Equal(DiagnosticsSchemaVersion, failure.SchemaVersion)
            Assert.Equal<(string * string option) list>(expected, failure.Diagnostics |> List.map (fun d -> d.Code, d.Subject))
        | expected, actual -> failwithf "%s: expected %A but got %A" name expected actual

[<Fact>]
let ``the published canonical form is byte-identical across member and array order and hashes to the digest`` () =
    let canonicalText = read "valid-contract.canonical.json"
    let canonicalOf json = match readBlueprint json with Ok b -> canonicalBlueprint b | Error e -> failwith e
    Assert.Equal(canonicalText, canonicalOf (read "valid-contract.json"))
    Assert.Equal(canonicalText, canonicalOf (read "reordered-contract.json"))
    Assert.Equal(canonicalText, canonicalOf canonicalText)
    Assert.Equal(validDigest (), sha256Text canonicalText)

    for file in [ "valid-contract.json"; "unicode-ordinal-contract.json" ] do
        let original = read file
        let json = match parse original with Ok value -> value | Error e -> failwithf "%A" e
        for variant in [ reverseEverything json; rotateEverything json; rotateEverything (rotateEverything json) ] do
            Assert.Equal(canonicalOf original, canonicalOf (render variant))
            Assert.Equal(admitContract original |> Result.map encodeContractIdentity,
                         admitContract (render variant) |> Result.map encodeContractIdentity)

[<Fact>]
let ``requirement identifiers are qualified source keys carried verbatim and order independently`` () =
    let identity json = match admitContract json with Ok i -> i | Error e -> failwithf "%A" e
    let current = identity (read "valid-contract.json")
    Assert.Equal<string list>([ "requirements/domain.md#R-001"; "requirements/operations.md#R-001" ], current.RequirementKeys)
    Assert.Equal<string list>([ "DEC-CONFIRM" ], current.DecisionIds)
    Assert.Empty current.ConflictIds
    Assert.Equal(encodeContractIdentity current, encodeContractIdentity (identity (read "reordered-contract.json")))
    Assert.Contains(ContractIdentitySchemaVersion, encodeContractIdentity current)

[<Fact>]
let ``diagnostics are sorted and their envelope is independent of input order`` () =
    let diagnostics json = match admitContract json with Ok _ -> failwith "accepted" | Error failure -> failure
    let broken = read "nonreciprocal-link-contract.json"
    let reordered = match parse broken with Ok value -> render (reverseEverything value) | Error e -> failwithf "%A" e
    let first = diagnostics broken
    let keys = first.Diagnostics |> List.map (fun d -> d.Code, d.Subject, d.Message)
    Assert.Equal<(string * string option * string) list>(List.sort keys, keys)
    Assert.True(keys.Length > 1)
    Assert.Equal(encodeDiagnostics first, encodeDiagnostics (diagnostics reordered))

[<Fact>]
let ``unsupported versions are refused before any other field is interpreted`` () =
    let future = """{"schemaVersion":"ecir/2","somethingNew":{"x":1},"nodes":"not even an array"}"""
    match admitContract future with
    | Ok _ -> failwith "a future version was accepted"
    | Error failure ->
        Assert.Equal<(string * string option) list>([ "ORDO.CONTRACT.UNSUPPORTED_SCHEMA", Some "ecir/2" ],
                                                    failure.Diagnostics |> List.map (fun d -> d.Code, d.Subject))

[<Fact>]
let ``a pinned reference detects that the contract it names has changed`` () =
    let current = match admitContract (read "valid-contract.json") with Ok i -> i | Error e -> failwithf "%A" e
    let previous = match admitContract (read "previous-contract.json") with Ok i -> i | Error e -> failwithf "%A" e
    Assert.NotEqual<string>(previous.ContractDigest, current.ContractDigest)
    Assert.Equal(Ok current, checkReference current.ContractDigest current)
    match checkReference previous.ContractDigest current with
    | Ok _ -> failwith "a stale reference was accepted"
    | Error failure ->
        Assert.Equal<(string * string option) list>(
            [ StaleReferenceCode, Some(previous.ContractDigest + "->" + current.ContractDigest) ],
            failure.Diagnostics |> List.map (fun d -> d.Code, d.Subject))
    for malformed in [ ""; "sha256:"; current.ContractDigest.ToUpperInvariant(); current.ContractDigest.Substring 7 ] do
        match checkReference malformed current with
        | Error failure -> Assert.Equal(InvalidReferenceDigestCode, failure.Diagnostics.Head.Code)
        | Ok _ -> failwithf "malformed pin %s accepted" malformed

[<Fact>]
let ``undecodable escaped surrogates refuse with a diagnostic instead of throwing`` () =
    for text in [ "{\"a\":\"\\ud800\"}"; "{\"\\udc00\":\"x\"}" ] do
        Assert.True(parse text |> Result.isError, text)
    match admitContract (read "lone-surrogate-contract.json") with
    | Ok _ -> failwith "an undecodable contract was accepted"
    | Error failure -> Assert.Equal("ORDO.CONTRACT.INVALID_BLUEPRINT_JSON", failure.Diagnostics.Head.Code)

// ---------------------------------------------------------------------------
// REL-GATE-002: unresolved conflicts and requirement-scoped readiness.
// ---------------------------------------------------------------------------

let private readinessCases () =
    match parse (read "expectations.json") |> Result.toOption |> Option.bind (member' "readinessCases") with
    | Some(JArray items) -> items
    | _ -> failwith "expectations.json has no readinessCases"

let private diagnosticPairs (failure: DiagnosticSet) = failure.Diagnostics |> List.map (fun d -> d.Code, d.Subject)

[<Fact>]
let ``every golden readiness case is reproduced exactly by the producer`` () =
    let all = readinessCases ()
    Assert.True(List.length all >= 10)
    for case in all do
        let name = text "name" case |> Option.defaultValue "?"
        let contract = text "contract" case |> Option.get |> read
        let keys = stringList (member' "requirementKeys" case) |> Set.ofList
        let authorized = stringList (member' "authorizedDecisionIds" case) |> Set.ofList
        match text "outcome" case, assessPinnedReadiness (text "expectedDigest" case) contract keys authorized with
        | Some "ready", Ok ready ->
            let expected = member' "scope" case |> Option.get
            let view = readyScopeView ready
            Assert.True(Some view.Identity.ContractDigest = text "contractDigest" expected, name)
            Assert.True(Some view.Identity.ContractSchemaVersion = text "contractSchemaVersion" expected, name)
            Assert.Equal<string list>(stringList (member' "requirementKeys" expected), view.RequirementKeys)
            Assert.Equal<string list>(stringList (member' "requiredRequirementKeys" expected), view.RequiredRequirementKeys)
            Assert.Equal<string list>(stringList (member' "requiredNodeIds" expected), view.RequiredNodeIds)
            Assert.Equal<string list>(stringList (member' "requiredDecisionIds" expected), view.RequiredDecisionIds)
            Assert.Equal<string list>(stringList (member' "requiredRationaleIds" expected), view.RequiredRationaleIds)
            Assert.Equal<string list>(stringList (member' "authorizedDecisionIds" expected), view.AuthorizedDecisionIds)
        | Some "refused", Error failure ->
            let expected =
                match member' "diagnostics" case with
                | Some(JArray items) -> items |> List.map (fun item -> text "code" item |> Option.get, text "subject" item)
                | _ -> failwith name
            Assert.Equal(DiagnosticsSchemaVersion, failure.SchemaVersion)
            Assert.Equal<(string * string option) list>(expected, diagnosticPairs failure)
        | expected, actual -> failwithf "%s: expected %A but got %A" name expected actual

[<Fact>]
let ``contradictory decisions joined by an open conflict on a modeled requirement are refused at admission`` () =
    match admitContract (read "contradictory-decisions-contract.json") with
    | Ok _ -> failwith "a modeled requirement with an unresolved conflict was admitted"
    | Error failure ->
        Assert.Equal<(string * string option) list>(
            [ UnresolvedConflictCode, Some "requirements/domain.md#R-001->CONFLICT-REOPEN" ], diagnosticPairs failure)
    // Authorizing both contradictory decisions does not resolve the conflict.
    match assessReadiness (read "contradictory-decisions-contract.json")
              (Set.singleton "requirements/domain.md#R-001") (Set.ofList [ "DEC-ALLOW-REOPEN"; "DEC-FORBID-REOPEN" ]) with
    | Ok _ -> failwith "readiness accepted an unresolved conflict"
    | Error failure -> Assert.Equal(UnresolvedConflictCode, failure.Diagnostics.Head.Code)

[<Fact>]
let ``an unresolved requirement is a well-formed contract whose completion is never ready`` () =
    let contract = read "unresolved-requirement-contract.json"
    Assert.True(admitContract contract |> Result.isOk)
    match assessReadiness contract (Set.singleton "requirements/domain.md#R-001") Set.empty with
    | Ok _ -> failwith "an unresolved requirement was ready"
    | Error failure ->
        Assert.Equal<(string * string option) list>(
            [ "ORDO.CONTRACT.BLOCKED_REQUIREMENT", Some "requirements/domain.md#R-001"
              UnresolvedConflictCode, Some "requirements/domain.md#R-001->CONFLICT-CONFIRM" ],
            diagnosticPairs failure)

[<Fact>]
let ``readiness requires external authorization of every decision in scope and nothing else`` () =
    let contract = read "valid-contract.json"
    let domain = Set.singleton "requirements/domain.md#R-001"
    match assessReadiness contract domain Set.empty with
    | Ok _ -> failwith "an unauthorized decision was ready"
    | Error failure ->
        Assert.Equal<(string * string option) list>(
            [ "ORDO.CONTRACT.MISSING_DECISION_AUTHORIZATION", Some "DEC-CONFIRM" ], diagnosticPairs failure)
    match assessReadiness contract domain (Set.singleton "DEC-CONFIRM") with
    | Error failure -> failwithf "%A" failure
    | Ok ready ->
        let view = readyScopeView ready
        Assert.Equal<string list>([ "DEC-CONFIRM" ], view.RequiredDecisionIds)
        Assert.Equal<string list>([ "COHORT-CONFIRM"; "DEC-CONFIRM"; "VERIFY-CONFIRM" ], view.RequiredNodeIds)
        Assert.Contains(ReadinessSchemaVersion, encodeReadyScope ready)
    // The reordered contract yields byte-identical readiness.
    let encode json = assessReadiness json domain (Set.singleton "DEC-CONFIRM") |> Result.map encodeReadyScope
    Assert.Equal(encode contract, encode (read "reordered-contract.json"))
