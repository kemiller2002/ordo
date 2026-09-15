/// Tests for the execution-package builder.
module Sde.Tests.PackageBuildTests

open System.IO
open Xunit
open Sde.Core
open Sde.Tests.Fixtures

let private buildInputs (outputDirectory: string) : PackageBuild.BuildInputs =
    { RepositoryRoot = repositoryRoot
      DistributionDirectory = Path.Combine(repositoryRoot, "distribution")
      OutputDirectory = outputDirectory
      PackageName = Packaging.packageName
      PackageVersion = packageJsonVersion }

[<Fact>]
let ``building twice from the same sources produces identical bytes`` () =
    let first = makeTempDir "sde-build-a-"
    let second = makeTempDir "sde-build-b-"

    try
        match PackageBuild.build (buildInputs first), PackageBuild.build (buildInputs second) with
        | Ok a, Ok b ->
            Assert.Equal(Manifest.serialize a.Manifest, Manifest.serialize b.Manifest)

            match FileSystem.listManagedFiles first, FileSystem.listManagedFiles second with
            | Ok pathsA, Ok pathsB ->
                Assert.Equal<string list>(pathsA, pathsB)

                for path in pathsA do
                    Assert.Equal(
                        FileSystem.sha256File (Paths.safeJoinOrFail first path),
                        FileSystem.sha256File (Paths.safeJoinOrFail second path)
                    )
            | Error detail, _
            | _, Error detail -> failwith detail
        | Error detail, _
        | _, Error detail -> failwith detail
    finally
        cleanup [ first; second ]

[<Fact>]
let ``a missing canonical source fails the build loudly`` () =
    let output = makeTempDir "sde-build-missing-"
    let fakeRepository = makeTempDir "sde-fake-repo-"

    try
        // A repository root with the real distribution directory but none of
        // the canonical sources the map names.
        let inputs =
            { buildInputs output with
                RepositoryRoot = fakeRepository }

        match PackageBuild.build inputs with
        | Error detail ->
            Assert.Contains("missing canonical source file(s)", detail)
            // Nothing may have been written before the failure.
            Assert.Empty(Directory.GetFileSystemEntries output)
        | Ok _ -> failwith "expected the build to fail"
    finally
        cleanup [ output; fakeRepository ]

[<Fact>]
let ``the built package validates as a schema version 1 manifest`` () =
    match Manifest.read payloadDirectory with
    | Error detail -> failwith detail
    | Ok manifest ->
        Assert.Equal(1, manifest.SchemaVersion)
        Assert.Equal(Packaging.packageName, manifest.PackageName)
        Assert.Equal(packageJsonVersion, manifest.SdeVersion)
        Assert.True(manifest.MethodVersion.IsSome)
        Assert.DoesNotContain(manifest.Files, fun file -> file.Path = Ownership.manifestName)
        Assert.Contains(manifest.Files, fun file -> file.Path = Ownership.versionFileName)

[<Fact>]
let ``the VERSION file agrees with the manifest`` () =
    let text = File.ReadAllText(Path.Combine(payloadDirectory, Ownership.versionFileName))

    match Manifest.read payloadDirectory with
    | Ok manifest -> Assert.Equal(manifest.SdeVersion, text.Trim())
    | Error detail -> failwith detail

[<Fact>]
let ``related_documents entries are rewritten to distributed destinations and dangling ones dropped`` () =
    let map =
        Map.ofList
            [ "doctrine/FOUR-TIER-ARCHITECTURE.md", "architecture/FOUR-TIER-ARCHITECTURE.md"
              "method/VERIFICATION-METHOD.md", "method/VERIFICATION-METHOD.md" ]

    let source =
        "---\nversion: 0.2.0\nrelated_documents:\n  - doctrine/FOUR-TIER-ARCHITECTURE.md\n  - research/evidence/EV-HN-2026-0001.md\n  - method/VERIFICATION-METHOD.md\n---\n\nBody.\n"

    let rewritten = PackageBuild.rewriteRelatedDocuments source map

    Assert.Contains("  - architecture/FOUR-TIER-ARCHITECTURE.md", rewritten)
    Assert.Contains("  - method/VERIFICATION-METHOD.md", rewritten)
    Assert.DoesNotContain("research/evidence", rewritten)
    Assert.Contains("Body.", rewritten)

[<Fact>]
let ``a document whose related_documents all dangle gets an empty list rather than a broken one`` () =
    let source = "---\nversion: 0.2.0\nrelated_documents:\n  - research/evidence/EV.md\n---\n\nBody.\n"
    let rewritten = PackageBuild.rewriteRelatedDocuments source Map.empty

    Assert.Contains("related_documents: []", rewritten)

[<Fact>]
let ``markdown links to distributed targets are rewritten and others left alone`` () =
    let map = Map.ofList [ "doctrine/GLOSSARY.md", "reference/GLOSSARY.md" ]

    let source = "See [glossary](GLOSSARY.md) and [evidence](../research/EV.md) and [web](https://example.com/x.md)."

    let rewritten =
        PackageBuild.rewriteDistributedMarkdownLinks source "doctrine/STRUCTURAL-LOCALITY.md" "architecture/STRUCTURAL-LOCALITY.md" map

    Assert.Contains("](../reference/GLOSSARY.md)", rewritten)
    Assert.Contains("](../research/EV.md)", rewritten)
    Assert.Contains("](https://example.com/x.md)", rewritten)

[<Fact>]
let ``front matter version extraction reduces to major and minor`` () =
    match PackageBuild.toMajorMinor "0.2.0" with
    | Ok value -> Assert.Equal("0.2", value)
    | Error detail -> failwith detail

    match PackageBuild.toMajorMinor "nonsense" with
    | Error _ -> ()
    | Ok _ -> failwith "expected a failure"

[<Fact>]
let ``the distribution map is loadable and every declared source exists`` () =
    let mapPath = Path.Combine(repositoryRoot, "distribution", PackageBuild.distributionMapFileName)

    match PackageBuild.loadDistributionMap mapPath with
    | Error detail -> failwith detail
    | Ok map ->
        Assert.NotEmpty map.Files

        for entry in map.Files do
            Assert.True(Paths.isSafeRelativePath entry.Destination)

            match entry.Source with
            | Some source when not entry.Authored ->
                Assert.True(
                    File.Exists(Path.Combine(repositoryRoot, Paths.toNativePath source)),
                    sprintf "declared source %s does not exist" source
                )
            | Some _
            | None -> Assert.True(entry.Authored, sprintf "%s has no source and is not authored" entry.Destination)
