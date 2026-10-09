module Ordo.Tests.ConstructionIrFixtureTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire

let private examples () =
    let rec find (current: DirectoryInfo) remaining =
        let candidate = Path.Combine(current.FullName, "examples", "ecir", "source-manifest.json")
        if File.Exists candidate then Path.GetDirectoryName candidate
        elif remaining <= 0 || isNull current.Parent then failwith "ECIR fixtures missing from repository"
        else find current.Parent (remaining - 1)
    find (DirectoryInfo AppContext.BaseDirectory) 10

let private read name = File.ReadAllText(Path.Combine(examples (), name))

let private sha (value: string) =
    Encoding.UTF8.GetBytes value
    |> SHA256.HashData
    |> Convert.ToHexString
    |> fun hex -> "sha256:" + hex.ToLowerInvariant()

let private parseManifest () =
    match readManifest (read "source-manifest.json") with
    | Ok manifest -> manifest
    | Error problem -> failwith problem

let private parseBlueprint name =
    match readBlueprint (read name) with
    | Ok blueprint -> blueprint
    | Error problem -> failwith problem

[<Fact>]
let ``checked-in ECIR example is actually verifiable against its pinned intake`` () =
    let manifest = parseManifest ()
    Assert.Equal(2, manifest.Requirements.Length)
    Assert.Equal("R-001", manifest.Requirements[0].OriginalId)
    Assert.Equal("R-001", manifest.Requirements[1].OriginalId)
    Assert.NotEqual(manifest.Requirements[0].Key, manifest.Requirements[1].Key)
    let blueprint = parseBlueprint "valid-blueprint.json"
    Assert.True(validatePinned manifest blueprint |> Result.isOk)

[<Fact>]
let ``sample content hashes correspond to real original requirement files and positions`` () =
    let manifest = parseManifest ()
    for source in manifest.Requirements do
        let fullPath = Path.Combine(examples (), source.Document)
        let content = File.ReadAllText fullPath
        Assert.Equal(sha content, source.Revision)
        let lines = content.Replace("\r\n", "\n").Split('\n')
        Assert.StartsWith("L", source.Location)
        let position = int (source.Location.Substring 1)
        Assert.Contains(source.OriginalId, lines[position - 1])
        let section = String.Join("\n", lines[(position - 1)..])
        Assert.Equal(sha section, source.ContentDigest)

[<Fact>]
let ``negative example blueprints are JSON-valid but fail Ordo semantic validation`` () =
    let manifest = parseManifest ()
    let omitted = parseBlueprint "invalid-omitted-requirement.json"
    let reversed = parseBlueprint "invalid-nonreciprocal-link.json"
    Assert.True(validatePinned manifest omitted |> Result.isError)
    Assert.True(validatePinned manifest reversed |> Result.isError)
    Assert.Contains(MissingRequirement "requirements/operations.md#R-001", validate manifest omitted)
    Assert.Contains(NonReciprocalLink("requirements/domain.md#R-001", "VERIFY-CONFIRM"), validate manifest reversed)

[<Fact>]
let ``scaffolding the checked-in manifest conserves all source IDs without authorizing execution`` () =
    let manifest = parseManifest ()
    match scaffold manifest with
    | Error problem -> failwithf "%A" problem
    | Ok proposal ->
        Assert.Empty(validate manifest proposal)
        Assert.Equal(manifest.Requirements.Length, proposal.Requirements.Length)
        Assert.True(proposal.Requirements |> List.forall (fun entry ->
            match entry.Disposition with Unresolved _ -> true | _ -> false))
