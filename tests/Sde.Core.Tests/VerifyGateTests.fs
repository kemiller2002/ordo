/// The split verification gate: `verify --integrity-only`.
///
/// Installation integrity (missing or modified managed artifacts, version
/// and toolchain-pin agreement, an unusable structural configuration, an
/// absent installation) fails closed. SDE-STRUCT-001 findings are review
/// signals: they are reported, typed and counted, but they do not fail the
/// gate. Every failure carries a machine-readable category so automation
/// never has to parse prose to tell the two apart.
module Sde.Tests.VerifyGateTests

open System.IO
open Xunit
open Sde.Core
open Sde.Core.Inspection
open Sde.Core.Lifecycle
open Sde.Tests.Fixtures

let private withProject (body: string -> unit) =
    let project = makeTempDir "sde-gate-"

    try
        body project
    finally
        cleanup [ project ]

/// A tiny threshold configuration so a ten-line file is a structural finding.
let private withStructuralFinding (project: string) =
    File.WriteAllText(
        Path.Combine(project, Ownership.structuralConfigName),
        """{ "structuralReview": { "extensions": [".ts"], "thresholds": { "reviewAt": 3, "strongReviewAt": 4, "justificationAbove": 5, "conformanceConcernAbove": 6 } } }"""
    )

    File.WriteAllText(Path.Combine(project, "big.ts"), String.replicate 10 "line\n")

let private categories (report: VerifyReport) =
    report.Failures |> List.map failureCategory |> List.distinct

let private codes (report: VerifyReport) = report.Failures |> List.map failureCode

let private writePin (project: string) (version: string) =
    let path = Path.Combine(project, Ownership.toolchainManifestName)
    Directory.CreateDirectory(Path.Combine(project, ".echelon")) |> ignore
    File.WriteAllText(path, sprintf "{\n  \"schemaVersion\": 1,\n  \"ordo\": \"%s\"\n}\n" version)

// ---------------------------------------------------------------------------
// Core lifecycle
// ---------------------------------------------------------------------------

[<Fact>]
let ``integrity-only passes over structural findings and reports them as review signals`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        withStructuralFinding project

        let report = verifyIn IntegrityOnly project payload

        Assert.True(report.Passed, sprintf "%A" report.Failures)
        Assert.Equal(ExitCodes.success, verifyExitCode report)
        Assert.Empty report.Failures
        Assert.Contains(report.ReviewSignals, fun finding -> finding.Path = "big.ts" && finding.Code = StructuralReview.findingCode))

[<Fact>]
let ``strict mode still fails the same structural findings and categorises them as structural review`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        withStructuralFinding project

        let report = verifyIn Strict project payload

        Assert.False report.Passed
        Assert.Equal<FailureCategory list>([ StructuralReviewFailure ], categories report)
        Assert.Equal<string list>([ StructuralReview.findingCode ], codes report)
        Assert.Empty report.ReviewSignals)

[<Fact>]
let ``integrity-only fails closed when nothing is installed`` () =
    withProject (fun project ->
        let report = verifyIn IntegrityOnly project payload

        Assert.False report.Passed
        Assert.Equal<FailureCategory list>([ IntegrityFailure ], categories report)
        Assert.Contains("not-installed", codes report))

[<Fact>]
let ``integrity-only fails closed on a modified managed artifact even with structural findings present`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        withStructuralFinding project
        File.AppendAllText(Path.Combine(installDirFor project, "reference", "GLOSSARY.md"), "\nedited\n")

        let report = verifyIn IntegrityOnly project payload

        Assert.False report.Passed
        Assert.Equal<FailureCategory list>([ IntegrityFailure ], categories report)
        Assert.Contains("managed-file-modified", codes report))

[<Fact>]
let ``integrity-only fails closed on a missing managed artifact`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        File.Delete(Path.Combine(installDirFor project, "reference", "GLOSSARY.md"))

        let report = verifyIn IntegrityOnly project payload

        Assert.False report.Passed
        Assert.Contains("managed-file-missing", codes report))

[<Fact>]
let ``integrity-only fails closed when the installation is behind this CLI`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        let newer = { payload with Version = "99.0.0" }

        // The lenient and strict gates accept an intact older installation.
        Assert.True((verifyIn Lenient project newer).Passed)

        let report = verifyIn IntegrityOnly project newer

        Assert.False report.Passed
        Assert.Contains("version-mismatch", codes report))

[<Fact>]
let ``integrity-only fails closed when the installation is ahead of this CLI`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        let older = { payload with Version = "0.0.1" }

        let report = verifyIn IntegrityOnly project older

        Assert.False report.Passed
        Assert.Contains("version-mismatch", codes report))

[<Fact>]
let ``integrity-only fails closed on a toolchain pin that names another release`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        writePin project "0.0.1"

        let report = verifyIn IntegrityOnly project payload

        Assert.False report.Passed
        Assert.Equal<FailureCategory list>([ IntegrityFailure ], categories report)
        Assert.Contains("toolchain-pin-mismatch", codes report))

[<Fact>]
let ``integrity-only fails closed when the toolchain pin is absent`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        File.Delete(Path.Combine(project, Ownership.toolchainManifestName))

        let report = verifyIn IntegrityOnly project payload

        Assert.False report.Passed
        Assert.Contains("toolchain-pin-missing", codes report))

[<Fact>]
let ``integrity-only fails closed on an installation at an older configuration version`` () =
    withProject (fun project ->
        installHistoricalVersion payload.Version project
        writePin project payload.Version

        let report = verifyIn IntegrityOnly project payload

        Assert.False report.Passed
        Assert.Contains("configuration-version-behind", codes report))

[<Fact>]
let ``integrity-only fails closed on an unusable structural configuration`` () =
    withProject (fun project ->
        initialize project payload false |> ignore

        File.WriteAllText(
            Path.Combine(project, Ownership.structuralConfigName),
            """{ "structuralReview": { "thresholds": { "reviewAt": 5000, "strongReviewAt": 10 } } }"""
        )

        let report = verifyIn IntegrityOnly project payload

        Assert.False report.Passed
        Assert.Equal<FailureCategory list>([ IntegrityFailure ], categories report))

[<Fact>]
let ``the boolean verify entry point keeps its released meaning`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        withStructuralFinding project

        Assert.True((verify project payload false).Passed)
        Assert.False((verify project payload true).Passed))

// ---------------------------------------------------------------------------
// CLI and JSON contract
// ---------------------------------------------------------------------------

let private runJson (argv: string list) (project: string) =
    let exitCode, stdout, _ = Sde.Cli.Program.run argv project

    match Json.parse (String.concat "\n" stdout) with
    | Ok document -> exitCode, document
    | Error detail -> failwithf "stdout was not valid JSON: %s" detail

[<Fact>]
let ``verify --integrity-only --json exits zero with structural findings and lists them as review signals`` () =
    withProject (fun project ->
        Sde.Cli.Program.run [ "init" ] project |> ignore
        withStructuralFinding project

        let exitCode, document = runJson [ "verify"; "--integrity-only"; "--json" ] project

        Assert.Equal(ExitCodes.success, exitCode)
        Assert.Equal(Some "integrity-only", Json.tryString (Json.tryField "mode" document))
        Assert.Equal(Some true, Json.tryBool (Json.tryField "passed" document))
        Assert.Equal(Some [], Json.tryArray (Json.tryField "failures" document))

        let signals = Json.tryArray (Json.tryField "reviewSignals" document) |> Option.defaultValue []

        Assert.Contains(
            signals,
            fun signal ->
                Json.tryString (Json.tryField "code" signal) = Some StructuralReview.findingCode
                && Json.tryString (Json.tryField "path" signal) = Some "big.ts"
        ))

[<Fact>]
let ``verify --strict --json categorises every failure`` () =
    withProject (fun project ->
        Sde.Cli.Program.run [ "init" ] project |> ignore
        withStructuralFinding project

        let exitCode, document = runJson [ "verify"; "--strict"; "--json" ] project

        Assert.Equal(ExitCodes.failure, exitCode)
        Assert.Equal(Some "strict", Json.tryString (Json.tryField "mode" document))

        let failures = Json.tryArray (Json.tryField "failures" document) |> Option.defaultValue []
        Assert.NotEmpty failures

        for failure in failures do
            Assert.Equal(Some "structural-review", Json.tryString (Json.tryField "category" failure))
            Assert.Equal(Some StructuralReview.findingCode, Json.tryString (Json.tryField "code" failure)))

[<Fact>]
let ``verify --integrity-only --json reports an integrity failure with its category`` () =
    withProject (fun project ->
        Sde.Cli.Program.run [ "init" ] project |> ignore
        File.Delete(Path.Combine(installDirFor project, "reference", "GLOSSARY.md"))

        let exitCode, document = runJson [ "verify"; "--integrity-only"; "--json" ] project

        Assert.Equal(ExitCodes.failure, exitCode)
        let failures = Json.tryArray (Json.tryField "failures" document) |> Option.defaultValue []

        Assert.Contains(
            failures,
            fun failure ->
                Json.tryString (Json.tryField "category" failure) = Some "integrity"
                && Json.tryString (Json.tryField "code" failure) = Some "managed-file-missing"
        ))

[<Fact>]
let ``--integrity-only and --strict are mutually exclusive`` () =
    match Sde.Cli.Args.parse [ "verify"; "--integrity-only"; "--strict" ] with
    | Sde.Cli.Args.ParseFailed message -> Assert.Contains("--integrity-only", message)
    | other -> failwithf "expected a parse failure, got %A" other

[<Fact>]
let ``--integrity-only is refused on commands other than verify`` () =
    match Sde.Cli.Args.parse [ "status"; "--integrity-only" ] with
    | Sde.Cli.Args.ParseFailed _ -> ()
    | other -> failwithf "expected a parse failure, got %A" other

[<Fact>]
let ``verify help documents --integrity-only`` () =
    withProject (fun project ->
        let _, stdout, _ = Sde.Cli.Program.run [ "verify"; "--help" ] project
        Assert.Contains(stdout, fun line -> line.Contains "--integrity-only"))
