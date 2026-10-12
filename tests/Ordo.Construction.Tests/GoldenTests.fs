/// Identical validated input and generator version give identical bytes.
module Ordo.Construction.Tests.GoldenTests

open System.IO
open System.Security.Cryptography
open System.Text
open Xunit
open Ordo.Construction.Artifacts
open Ordo.Construction.Construction
open Ordo.Construction.Tests.Fixtures

let private fixtures = [ timer; door ]

[<Fact>]
let ``regeneration reproduces every golden file byte for byte`` () =
    for fixture in fixtures do
        let c = construction fixture
        for artifact in c.Artifacts do
            let golden = File.ReadAllBytes(goldenPath fixture artifact.Path)
            Assert.True((golden = Encoding.UTF8.GetBytes artifact.Content), sprintf "%s/%s differs from its golden" fixture.Name artifact.Path)
        let goldenFiles =
            Directory.GetFiles(goldenPath fixture "generated", "*", SearchOption.AllDirectories)
            |> Array.map (fun p -> Path.GetRelativePath(goldenPath fixture "", p).Replace('\\', '/')) |> Set.ofArray
        Assert.Equal<Set<string>>(goldenFiles, c.Artifacts |> List.map _.Path |> Set.ofList)

[<Fact>]
let ``repeated construction is identical and artifact digests match content`` () =
    for fixture in fixtures do
        let first = construction fixture
        for _ in 1 .. 20 do
            Assert.Equal(first, construction fixture)
        for artifact in first.Artifacts do
            let digest = "sha256:" + (Encoding.UTF8.GetBytes artifact.Content |> SHA256.HashData |> System.Convert.ToHexString).ToLowerInvariant()
            Assert.Equal(digest, artifact.Digest)

[<Fact>]
let ``reordered and respelled plans construct the same bytes`` () =
    let d = draft timer
    let respell =
        d.Operations
        |> List.rev
        |> List.map (fun op ->
            match op with
            | Ordo.Core.ConstructionPlan.DefineState s when s.Name = "No Timer" -> Ordo.Core.ConstructionPlan.DefineState { s with Name = "no-timer" }
            | other -> other)
    let v = Ordo.Core.ConstructionPlan.validate (prepared timer) { d with Operations = respell } |> ok "respelled"
    Assert.Equal(construction timer, construct v |> ok "construct")

[<Fact>]
let ``output uses LF line endings, no BOM and one trailing newline`` () =
    for fixture in fixtures do
        for artifact in (construction fixture).Artifacts do
            Assert.DoesNotContain("\r", artifact.Content)
            Assert.False(artifact.Content.StartsWith "﻿")
            Assert.EndsWith("\n", artifact.Content)
            Assert.False(artifact.Content.EndsWith "\n\n", artifact.Path)

[<Fact>]
let ``every generated source file carries the generated marker and plan digest`` () =
    for fixture in fixtures do
        let c = construction fixture
        for artifact in c.Artifacts |> List.filter (fun a -> a.Tier.IsSome) do
            Assert.StartsWith(GeneratedMarker + "/1 from " + c.PlanDigest + ">", artifact.Content)
