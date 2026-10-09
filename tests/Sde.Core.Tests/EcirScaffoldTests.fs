module Sde.Tests.EcirScaffoldTests

open System
open System.IO
open Xunit
open Sde.Cli
open Sde.Tests.Fixtures
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire

let private source id doc =
    { Key = doc + "#" + id
      OriginalId = id
      Document = doc
      Location = "L12"
      Revision = "rev-1"
      ContentDigest = "sha256:" + String('c', 64) }

let private original =
    [ source "R-001" "spec/one.md"
      source "R-001" "spec/two.md" ]

let private manifest =
    { Requirements = original
      Digest = manifestDigest original }

[<Fact>]
let ``ECIR scaffold CLI preserves all source requirements and writes only an immutable unresolved artifact`` () =
    let dir = makeTempDir "sde-ecir-scaffold-"
    try
        let input = Path.Combine(dir, "manifest.json")
        let output = Path.Combine(dir, "draft.json")
        File.WriteAllText(input, encodeManifest manifest)
        let args = [ "ecir"; "scaffold"; "--manifest"; input; "--output"; output; "--json" ]
        let code, stdout, stderr = Program.run args dir

        Assert.Equal(0, code)
        Assert.Empty stderr
        Assert.Single stdout |> ignore
        Assert.Contains("\"executionAuthorized\":false", List.head stdout)
        Assert.True(File.Exists output)

        let read =
            File.ReadAllText(output) |> readBlueprint
            |> function Ok b -> b | Error error -> failwith error
        Assert.Equal(2, read.Requirements.Length)
        Assert.Equal(2, read.Nodes.Length)
        Assert.Empty(validate manifest read)
        Assert.True(read.Requirements |> List.forall (fun r ->
            match r.Disposition with Unresolved _ -> true | _ -> false))
        Assert.Equal(manifest.Digest, read.SourceManifestDigest)

        let initialBytes = File.ReadAllBytes output
        let second, _, errors = Program.run args dir
        Assert.NotEqual(0, second)
        Assert.NotEmpty errors
        Assert.Equal<byte>(initialBytes, File.ReadAllBytes output)
    finally
        cleanup [ dir ]

[<Fact>]
let ``ECIR scaffold CLI rejects falsified input before writing anything`` () =
    let dir = makeTempDir "sde-ecir-reject-"
    try
        let input = Path.Combine(dir, "manifest.json")
        let output = Path.Combine(dir, "draft.json")
        File.WriteAllText(input, (encodeManifest manifest).Replace(manifest.Digest, "sha256:" + String('a', 64)))
        let code, _, errors =
            Program.run [ "ecir"; "scaffold"; "--manifest"; input; "--output"; output ] dir
        Assert.NotEqual(0, code)
        Assert.NotEmpty errors
        Assert.False(File.Exists output)
    finally
        cleanup [ dir ]

[<Fact>]
let ``ECIR scaffold requires both source and output paths`` () =
    let code, _, _ = Program.run [ "ecir"; "scaffold"; "--manifest"; "input.json" ] repositoryRoot
    Assert.Equal(2, code)
