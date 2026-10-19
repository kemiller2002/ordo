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
