/// Test fixtures: temporary repositories and historical installations.
module Sde.Tests.Fixtures

open System
open System.IO
open Sde.Core

/// The execution package built into distribution/dist. Tests run against the
/// real payload rather than a synthetic one, so a change to the distribution
/// map is exercised by the whole suite.
let payloadDirectory =
    let rec search (directory: DirectoryInfo option) =
        match directory with
        | None -> None
        | Some directory ->
            let candidate = Path.Combine(directory.FullName, "distribution", Packaging.payloadDirectoryName)

            if File.Exists(Path.Combine(candidate, Ownership.manifestName)) then
                Some candidate
            else
                search (Option.ofObj directory.Parent)

    match search (Some(DirectoryInfo AppContext.BaseDirectory)) with
    | Some directory -> directory
    | None ->
        failwith
            "distribution/dist is not built; run `npm run build` in distribution/ before the test suite (pretest does this automatically)"

let repositoryRoot =
    // distribution/dist -> distribution -> the repository root.
    let parentOf (directory: DirectoryInfo) =
        match Option.ofObj directory.Parent with
        | Some parent -> parent
        | None -> failwithf "%s has no parent directory" directory.FullName

    (DirectoryInfo payloadDirectory |> parentOf |> parentOf).FullName

/// Tests drive the CLI in-process, where the executable is the test host and
/// the "two levels up from the executable" layout of the published package
/// does not apply. SDE_PAYLOAD_DIR is the documented override for exactly
/// this case, so the tests use the same mechanism a developer would.
do Environment.SetEnvironmentVariable("SDE_PAYLOAD_DIR", payloadDirectory)

let payload =
    match Lifecycle.readPayload payloadDirectory with
    | Ok payload -> payload
    | Error detail -> failwith detail

/// The npm package version, read from the authoritative source rather than
/// duplicated, so a test cannot pass against a stale constant.
let packageJsonVersion =
    let path = Path.Combine(repositoryRoot, "distribution", "package.json")

    match Json.parse (File.ReadAllText path) with
    | Ok root ->
        match Json.tryString (Json.tryField "version" root) with
        | Some version -> version
        | None -> failwith "distribution/package.json has no version"
    | Error detail -> failwith detail

let makeTempDir (prefix: string) =
    let path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N").Substring(0, 12))
    Directory.CreateDirectory path |> ignore
    path

let cleanup (paths: string list) =
    for path in paths do
        try
            if Directory.Exists path then Directory.Delete(path, true)
        with _ ->
            ()

/// Builds a self-consistent execution package at an arbitrary version, so
/// upgrade paths can be exercised without needing a second real release.
/// The manifest hashes match the fixture's own files, exactly as a genuine
/// release's would.
let packageAtVersion (version: string) =
    let fixtureDir = makeTempDir "sde-fixture-"

    match FileSystem.listManagedFiles payloadDirectory with
    | Error detail -> failwith detail
    | Ok paths ->
        for relPath in paths do
            FileSystem.copyFileInto (Paths.safeJoinOrFail payloadDirectory relPath) (Paths.safeJoinOrFail fixtureDir relPath)

        FileSystem.writeFileInto (Paths.safeJoinOrFail fixtureDir Ownership.versionFileName) (version + "\n")

        match Manifest.build Packaging.packageName version (Some "0.2") (Some "fixture") fixtureDir with
        | Error detail -> failwith detail
        | Ok manifest ->
            FileSystem.writeFileInto
                (Paths.safeJoinOrFail fixtureDir Ownership.manifestName)
                (Manifest.serialize manifest)

            fixtureDir

let packageAtVersionWithoutManagedFile (version: string) (relativePath: string) =
    let fixture = packageAtVersion version
    let target = Paths.safeJoinOrFail fixture relativePath

    if not (File.Exists target) then
        failwithf "historical fixture does not contain %s" relativePath

    File.Delete target

    match Manifest.build Packaging.packageName version (Some "0.2") (Some "fixture") fixture with
    | Error detail -> failwith detail
    | Ok manifest ->
        FileSystem.writeFileInto
            (Paths.safeJoinOrFail fixture Ownership.manifestName)
            (Manifest.serialize manifest)

        fixture

/// Installs a package fixture into a project, producing an installation that
/// looks exactly like one a release of that version would have left behind:
/// a .sde/ payload and NO .echelon record, which is configuration version 1.
let installHistoricalVersion (version: string) (projectRoot: string) =
    let fixture = packageAtVersion version

    match Execution.installPayload fixture projectRoot with
    | Error detail -> failwith detail
    | Ok _ ->
        cleanup [ fixture ]
        // A release before 1.2.0 wrote no installation record; make sure the
        // fixture does not accidentally have one.
        let recordPath = InstallationRecord.recordPath projectRoot

        if File.Exists recordPath then File.Delete recordPath

/// Captures a comparable snapshot of every file in a repository: relative
/// path plus content hash. Used to prove that a command changed nothing.
let snapshot (root: string) =
    let results = ResizeArray<string * string>()

    let rec walk (directory: string) (prefix: string) =
        for entry in Directory.GetFileSystemEntries directory |> Array.sort do
            let name = Paths.fileName entry
            let relPath = if prefix = "" then name else prefix + "/" + name

            if Directory.Exists entry then
                walk entry relPath
            else
                results.Add(relPath, FileSystem.sha256File entry)

    walk root ""
    results |> List.ofSeq |> List.sortBy fst

/// A snapshot that also captures last-write times, to prove that an
/// idempotent command did not rewrite a file with identical content.
let snapshotWithTimestamps (root: string) =
    let results = ResizeArray<string * string * DateTime>()

    let rec walk (directory: string) (prefix: string) =
        for entry in Directory.GetFileSystemEntries directory |> Array.sort do
            let name = Paths.fileName entry
            let relPath = if prefix = "" then name else prefix + "/" + name

            if Directory.Exists entry then
                walk entry relPath
            else
                results.Add(relPath, FileSystem.sha256File entry, File.GetLastWriteTimeUtc entry)

    walk root ""
    results |> List.ofSeq |> List.sortBy (fun (path, _, _) -> path)
