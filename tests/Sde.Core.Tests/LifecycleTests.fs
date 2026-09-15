/// Lifecycle behaviour tests.
///
/// Every behaviour the released JavaScript implementation guaranteed is
/// pinned here, so the F# reimplementation cannot regress a contract
/// consumers already depend on. New behaviour is tested alongside it.
module Sde.Tests.LifecycleTests

open System.IO
open Xunit
open Sde.Core
open Sde.Core.Inspection
open Sde.Core.Lifecycle
open Sde.Tests.Fixtures

let private withProject (body: string -> unit) =
    let project = makeTempDir "sde-test-"

    try
        body project
    finally
        cleanup [ project ]

// ---------------------------------------------------------------------------
// init
// ---------------------------------------------------------------------------

[<Fact>]
let ``clean init installs the expected files and a valid manifest`` () =
    withProject (fun project ->
        let report = initialize project payload false

        match report.Outcome with
        | Applied(_, version) -> Assert.Equal(payload.Version, version)
        | other -> failwithf "expected Applied, got %A" other

        let installDir = installDirFor project
        Assert.True(File.Exists(Path.Combine(installDir, "VERSION")))
        Assert.True(File.Exists(Path.Combine(installDir, "README.md")))
        Assert.True(File.Exists(Path.Combine(installDir, "method", "CONSTRUCTION-METHOD.md")))
        Assert.True(File.Exists(Path.Combine(installDir, "architecture", "FOUR-TIER-ARCHITECTURE.md")))
        Assert.True(File.Exists(Path.Combine(installDir, "reference", "GLOSSARY.md")))
        Assert.True(File.Exists(Path.Combine(installDir, "templates", "work-item.md")))

        match Manifest.read installDir with
        | Error detail -> failwith detail
        | Ok manifest ->
            Assert.Equal(1, manifest.SchemaVersion)
            Assert.True(manifest.Files.Length > 0))

[<Fact>]
let ``init writes the echelon installation record`` () =
    withProject (fun project ->
        initialize project payload false |> ignore

        match InstallationRecord.read project with
        | InstallationRecord.Present record ->
            Assert.Equal(payload.Version, record.InstalledVersion)
            Assert.Equal(InstallationRecord.currentConfigurationVersion, record.ConfigurationVersion)
            Assert.Equal(Packaging.packageName, record.Package)
        | other -> failwithf "expected a readable installation record, got %A" other)

[<Fact>]
let ``running init twice makes no change at all`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        let before = snapshotWithTimestamps project

        let second = initialize project payload false

        match second.Outcome with
        | AlreadyCurrent _ -> ()
        | other -> failwithf "expected AlreadyCurrent, got %A" other

        Assert.Equal<(string * string * System.DateTime) list>(before, snapshotWithTimestamps project))

[<Fact>]
let ``init refuses cleanly when the target directory does not exist`` () =
    withProject (fun parent ->
        let missing = Path.Combine(parent, "does-not-exist")
        let report = initialize missing payload false

        match report.Outcome with
        | ChangeOutcome.Refused(Planning.NotADirectory path) -> Assert.Equal(missing, path)
        | other -> failwithf "expected NotADirectory, got %A" other)

[<Fact>]
let ``init does not touch unrelated files already in the project`` () =
    withProject (fun project ->
        let unrelated = Path.Combine(project, "unrelated.txt")
        File.WriteAllText(unrelated, "keep me")

        initialize project payload false |> ignore

        Assert.Equal("keep me", File.ReadAllText unrelated))

[<Fact>]
let ``init refuses to overwrite a locally modified installation`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        let target = Path.Combine(installDirFor project, "method", "CONSTRUCTION-METHOD.md")
        File.AppendAllText(target, "\nlocal edit\n")
        let contentBefore = File.ReadAllText target

        let report = initialize project payload false

        match report.Outcome with
        | ChangeOutcome.Refused(Planning.LocalModifications(version, problems)) ->
            Assert.Equal(payload.Version, version)

            Assert.Contains(
                problems,
                function
                | ManagedFileModified(path, _) -> path = "method/CONSTRUCTION-METHOD.md"
                | _ -> false
            )
        | other -> failwithf "expected LocalModifications, got %A" other

        Assert.Equal(contentBefore, File.ReadAllText target))

[<Fact>]
let ``init reports but does not perform an upgrade when an older version is installed`` () =
    withProject (fun project ->
        installHistoricalVersion "0.0.1" project

        let report = initialize project payload false

        match report.Outcome with
        | AlreadyCurrent(UpgradeRequired(installed, available)) ->
            Assert.Equal("0.0.1", installed.Version)
            Assert.Equal(payload.Version, available)
        | other -> failwithf "expected AlreadyCurrent(UpgradeRequired ...), got %A" other

        match Manifest.read (installDirFor project) with
        | Ok manifest -> Assert.Equal("0.0.1", manifest.SdeVersion)
        | Error detail -> failwith detail)

[<Fact>]
let ``init dry run makes no changes`` () =
    withProject (fun project ->
        let report = initialize project payload true

        match report.Outcome with
        | ChangeOutcome.Planned changes -> Assert.NotEmpty changes
        | other -> failwithf "expected Planned, got %A" other

        Assert.Empty(Directory.GetFileSystemEntries project))

[<Fact>]
let ``init check exits with the drift code when changes are required`` () =
    withProject (fun project ->
        let report = initialize project payload true
        Assert.Equal(ExitCodes.changesRequired, changeExitCode true report)

        initialize project payload false |> ignore
        let after = initialize project payload true
        Assert.Equal(ExitCodes.success, changeExitCode true after))

// ---------------------------------------------------------------------------
// status
// ---------------------------------------------------------------------------

[<Fact>]
let ``status reports not installed for a fresh repository and never writes`` () =
    withProject (fun project ->
        let report = getStatus project payload

        Assert.Equal(None, report.Installed)
        Assert.Equal(ExitCodes.failure, statusExitCode report)
        Assert.Empty(Directory.GetFileSystemEntries project))

[<Fact>]
let ``status reports the installed version and does not modify the repository`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        let before = snapshotWithTimestamps project

        let report = getStatus project payload

        Assert.Equal(Some payload.Version, report.Installed |> Option.map (fun i -> i.Version))
        Assert.True report.Verified
        Assert.Equal(ExitCodes.success, statusExitCode report)
        Assert.Equal<(string * string * System.DateTime) list>(before, snapshotWithTimestamps project))

[<Fact>]
let ``status reports an available upgrade for an older installation`` () =
    withProject (fun project ->
        installHistoricalVersion "0.0.1" project

        let report = getStatus project payload

        Assert.Equal(Some payload.Version, report.UpgradeAvailable))

// ---------------------------------------------------------------------------
// verify
// ---------------------------------------------------------------------------

[<Fact>]
let ``verify fails when nothing is installed`` () =
    withProject (fun project ->
        let report = verify project payload false
        Assert.False report.Passed
        Assert.Equal(ExitCodes.failure, verifyExitCode report))

[<Fact>]
let ``verify passes on a clean installation and does not mutate it`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        let before = snapshotWithTimestamps project

        let report = verify project payload false

        Assert.True report.Passed
        Assert.Equal(ExitCodes.success, verifyExitCode report)
        Assert.Equal<(string * string * System.DateTime) list>(before, snapshotWithTimestamps project))

[<Fact>]
let ``verify fails and reports a missing managed file`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        File.Delete(Path.Combine(installDirFor project, "reference", "GLOSSARY.md"))

        let report = verify project payload false

        Assert.False report.Passed

        Assert.Contains(
            report.Problems,
            function
            | ManagedFileMissing path -> path = "reference/GLOSSARY.md"
            | _ -> false
        ))

[<Fact>]
let ``verify detects a modified managed file`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        File.AppendAllText(Path.Combine(installDirFor project, "reference", "GLOSSARY.md"), "\nedited\n")

        let report = verify project payload false

        Assert.False report.Passed

        Assert.Contains(
            report.Problems,
            function
            | ManagedFileModified(path, ownership) -> path = "reference/GLOSSARY.md" && ownership = Ownership.ToolOwned
            | _ -> false
        ))

[<Fact>]
let ``verify detects an undeclared file under the install root`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        File.WriteAllText(Path.Combine(installDirFor project, "stray.md"), "not declared")

        let report = verify project payload false

        Assert.False report.Passed

        Assert.Contains(
            report.Problems,
            function
            | UnexpectedManagedFile path -> path = "stray.md"
            | _ -> false
        ))

[<Fact>]
let ``a VERSION file that disagrees with the manifest is detected even when its own hash matches`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        let installDir = installDirFor project
        let versionPath = Path.Combine(installDir, "VERSION")
        let manifestPath = Path.Combine(installDir, Ownership.manifestName)

        let tampered = "9.9.9\n"
        File.WriteAllText(versionPath, tampered)
        let newHash = FileSystem.sha256Bytes (System.Text.Encoding.UTF8.GetBytes tampered)

        // Rewrite the manifest so VERSION's recorded hash matches its new
        // content: the per-file check alone would now pass.
        let text = File.ReadAllText manifestPath
        let original = text.Substring(text.IndexOf "\"VERSION\"")
        let hashStart = original.IndexOf "\"sha256\": \"" + "\"sha256\": \"".Length
        let oldHash = original.Substring(hashStart, 64)
        File.WriteAllText(manifestPath, text.Replace(oldHash, newHash))

        let report = verify project payload false

        Assert.False report.Passed

        Assert.Contains(
            report.Problems,
            function
            | VersionFileMismatch("9.9.9", manifestVersion) -> manifestVersion = payload.Version
            | _ -> false
        )

        Assert.DoesNotContain(
            report.Problems,
            function
            | ManagedFileModified _ -> true
            | _ -> false
        ))

[<Fact>]
let ``strict verify fails an installation that has not adopted the current configuration version`` () =
    withProject (fun project ->
        installHistoricalVersion payload.Version project

        let lenient = verify project payload false
        Assert.True(lenient.Passed, "default verification must keep passing for installations from earlier releases")

        let strict = verify project payload true
        Assert.False strict.Passed
        Assert.NotEmpty strict.StrictFailures)

// ---------------------------------------------------------------------------
// upgrade
// ---------------------------------------------------------------------------

[<Fact>]
let ``upgrade moves an older unmodified installation to the packaged version`` () =
    withProject (fun project ->
        installHistoricalVersion "0.0.1" project

        let report = performUpgrade project payload false

        match report.Outcome with
        | Applied(_, version) -> Assert.Equal(payload.Version, version)
        | other -> failwithf "expected Applied, got %A" other

        match Manifest.read (installDirFor project) with
        | Ok manifest -> Assert.Equal(payload.Version, manifest.SdeVersion)
        | Error detail -> failwith detail)

[<Fact>]
let ``upgrade runs every intermediate configuration migration in order`` () =
    withProject (fun project ->
        installHistoricalVersion "0.0.1" project

        match createUpgradePlan project payload with
        | Planning.Changes plan ->
            let migrations =
                plan.Changes
                |> List.choose (function
                    | Planning.RunMigration(id, fromConfiguration, toConfiguration, _) ->
                        Some(id, fromConfiguration, toConfiguration)
                    | _ -> None)

            Assert.Equal<(string * int * int) list>(
                [ Migrations.recordMigrationId, 1, InstallationRecord.currentConfigurationVersion ],
                migrations
            )
        | other -> failwithf "expected a change plan, got %A" other

        performUpgrade project payload false |> ignore

        match InstallationRecord.read project with
        | InstallationRecord.Present record ->
            Assert.Equal(InstallationRecord.currentConfigurationVersion, record.ConfigurationVersion)
            Assert.Equal(payload.Version, record.InstalledVersion)
        | other -> failwithf "expected a record after upgrade, got %A" other)

[<Fact>]
let ``upgrade refuses to overwrite a locally modified installation and changes nothing`` () =
    withProject (fun project ->
        installHistoricalVersion "0.0.1" project
        let modifiedFile = Path.Combine(installDirFor project, "method", "CONSTRUCTION-METHOD.md")
        File.AppendAllText(modifiedFile, "\nlocal edit\n")
        let before = snapshot project

        let report = performUpgrade project payload false

        match report.Outcome with
        | ChangeOutcome.Refused(Planning.LocalModifications(version, _)) -> Assert.Equal("0.0.1", version)
        | other -> failwithf "expected LocalModifications, got %A" other

        Assert.Equal<(string * string) list>(before, snapshot project))

[<Fact>]
let ``upgrade refuses to downgrade when the installed version is newer`` () =
    withProject (fun project ->
        installHistoricalVersion "9.9.9" project

        let report = performUpgrade project payload false

        match report.Outcome with
        | ChangeOutcome.Refused(Planning.WouldDowngrade(installed, packaged)) ->
            Assert.Equal("9.9.9", installed)
            Assert.Equal(payload.Version, packaged)
        | other -> failwithf "expected WouldDowngrade, got %A" other

        match Manifest.read (installDirFor project) with
        | Ok manifest -> Assert.Equal("9.9.9", manifest.SdeVersion)
        | Error detail -> failwith detail)

[<Fact>]
let ``upgrade on a current installation is a no-op`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        let before = snapshotWithTimestamps project

        let report = performUpgrade project payload false

        match report.Outcome with
        | AlreadyCurrent _ -> ()
        | other -> failwithf "expected AlreadyCurrent, got %A" other

        Assert.Equal<(string * string * System.DateTime) list>(before, snapshotWithTimestamps project))

[<Fact>]
let ``upgrade refuses when nothing is installed`` () =
    withProject (fun project ->
        let report = performUpgrade project payload false

        match report.Outcome with
        | ChangeOutcome.Refused Planning.NotInstalledYet -> ()
        | other -> failwithf "expected NotInstalledYet, got %A" other)

[<Fact>]
let ``a user-owned file survives an upgrade untouched`` () =
    withProject (fun project ->
        installHistoricalVersion "0.0.1" project

        let userConfig = Path.Combine(project, Ownership.structuralConfigName)
        let userConfigContent = """{ "structuralReview": { "thresholds": { "reviewAt": 250 } } }"""
        File.WriteAllText(userConfig, userConfigContent)

        let userSource = Path.Combine(project, "src", "app.ts")
        Directory.CreateDirectory(Paths.directoryName userSource) |> ignore
        File.WriteAllText(userSource, "export const x = 1\n")

        let report = performUpgrade project payload false

        match report.Outcome with
        | Applied _ -> ()
        | other -> failwithf "expected Applied, got %A" other

        Assert.Equal(userConfigContent, File.ReadAllText userConfig)
        Assert.Equal("export const x = 1\n", File.ReadAllText userSource))

[<Fact>]
let ``another Echelon tool's installation record survives an upgrade untouched`` () =
    withProject (fun project ->
        installHistoricalVersion "0.0.1" project

        let otherRecord = Path.Combine(project, Ownership.echelonRootName, "ros.json")
        Directory.CreateDirectory(Paths.directoryName otherRecord) |> ignore
        let otherContent = """{ "tool": "ros", "installedVersion": "1.2.1" }"""
        File.WriteAllText(otherRecord, otherContent)

        performUpgrade project payload false |> ignore

        Assert.Equal(otherContent, File.ReadAllText otherRecord)
        Assert.True(File.Exists(InstallationRecord.recordPath project)))

[<Fact>]
let ``upgrade refuses when a migration precondition fails and writes nothing`` () =
    withProject (fun project ->
        installHistoricalVersion payload.Version project

        // A record that exists but is not readable is exactly the case the
        // migration's precondition guards: overwriting it would destroy the
        // evidence needed to diagnose it.
        let recordPath = InstallationRecord.recordPath project
        Directory.CreateDirectory(Paths.directoryName recordPath) |> ignore
        File.WriteAllText(recordPath, "{ not json")
        let before = snapshot project

        let report = performUpgrade project payload false

        match report.Outcome with
        | ChangeOutcome.Refused(Planning.UnreadableInstallation _) -> ()
        | other -> failwithf "expected a refusal for the unreadable record, got %A" other

        Assert.Equal<(string * string) list>(before, snapshot project))

[<Fact>]
let ``upgrade dry run makes no changes`` () =
    withProject (fun project ->
        installHistoricalVersion "0.0.1" project
        let before = snapshotWithTimestamps project

        let report = performUpgrade project payload true

        match report.Outcome with
        | ChangeOutcome.Planned changes -> Assert.NotEmpty changes
        | other -> failwithf "expected Planned, got %A" other

        Assert.Equal<(string * string * System.DateTime) list>(before, snapshotWithTimestamps project))

// ---------------------------------------------------------------------------
// shared files
// ---------------------------------------------------------------------------

[<Fact>]
let ``an invalid shared configuration file is detected and never rewritten`` () =
    withProject (fun project ->
        initialize project payload false |> ignore

        let configPath = Path.Combine(project, Ownership.structuralConfigName)
        let invalid = """{ "structuralReview": { "thresholds": { "reviewAt": 5000, "strongReviewAt": 10 } } }"""
        File.WriteAllText(configPath, invalid)

        let report = verify project payload false
        Assert.False report.Passed

        let state = getInstallationState project payload

        match state with
        | Invalid(_, problems) ->
            Assert.Contains(
                problems,
                function
                | SharedFileConflict(path, _) -> path = Ownership.structuralConfigName
                | _ -> false
            )
        | other -> failwithf "expected Invalid, got %A" other

        // init must refuse rather than "fixing" a file the repository owns.
        let init = initialize project payload false

        match init.Outcome with
        | ChangeOutcome.Refused(Planning.SharedConflict(path, _)) -> Assert.Equal(Ownership.structuralConfigName, path)
        | other -> failwithf "expected SharedConflict, got %A" other

        Assert.Equal(invalid, File.ReadAllText configPath))

[<Fact>]
let ``a valid shared configuration file overrides the default thresholds`` () =
    withProject (fun project ->
        initialize project payload false |> ignore

        File.WriteAllText(
            Path.Combine(project, Ownership.structuralConfigName),
            """{ "structuralReview": { "extensions": [".ts"], "thresholds": { "reviewAt": 3, "strongReviewAt": 4, "justificationAbove": 5, "conformanceConcernAbove": 6 } } }"""
        )

        File.WriteAllText(Path.Combine(project, "big.ts"), String.replicate 10 "line\n")

        let report = verify project payload false

        Assert.True report.Passed

        match report.Structural with
        | Some structural ->
            Assert.Contains(
                structural.Findings,
                fun finding -> finding.Path = "big.ts" && finding.Band = StructuralReview.ConformanceConcern
            )
        | None -> failwith "expected a structural report")

[<Fact>]
let ``strict verify fails on structural findings that the default mode only warns about`` () =
    withProject (fun project ->
        initialize project payload false |> ignore

        File.WriteAllText(
            Path.Combine(project, Ownership.structuralConfigName),
            """{ "structuralReview": { "extensions": [".ts"], "thresholds": { "reviewAt": 3, "strongReviewAt": 4, "justificationAbove": 5, "conformanceConcernAbove": 6 } } }"""
        )

        File.WriteAllText(Path.Combine(project, "big.ts"), String.replicate 10 "line\n")

        Assert.True((verify project payload false).Passed)
        Assert.False((verify project payload true).Passed))

// ---------------------------------------------------------------------------
// doctor
// ---------------------------------------------------------------------------

[<Fact>]
let ``doctor identifies an intentionally damaged installation and explains it`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        File.Delete(Path.Combine(installDirFor project, "reference", "GLOSSARY.md"))
        File.AppendAllText(Path.Combine(installDirFor project, "method", "CHANGE-CLASSIFICATION.md"), "\nedited\n")

        let report = diagnose project payload

        Assert.Equal(ExitCodes.failure, doctorExitCode report)
        Assert.True(Diagnostics.hasErrors report.Diagnoses)

        Assert.Contains(report.Diagnoses, fun d -> d.Code = "SDE-DOCTOR-003")
        Assert.Contains(report.Diagnoses, fun d -> d.Code = "SDE-DOCTOR-002")

        // Every error must carry a remedy: a diagnosis with no way forward
        // is not a diagnosis.
        for diagnosis in report.Diagnoses do
            if diagnosis.Severity = Diagnostics.Severity.Error then
                Assert.True(diagnosis.Remedy.IsSome, sprintf "%s has no remedy" diagnosis.Code))

[<Fact>]
let ``doctor succeeds on a healthy repository and never writes`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        let before = snapshotWithTimestamps project

        let report = diagnose project payload

        Assert.Equal(ExitCodes.success, doctorExitCode report)
        Assert.False(Diagnostics.hasErrors report.Diagnoses)
        Assert.Equal<(string * string * System.DateTime) list>(before, snapshotWithTimestamps project))

[<Fact>]
let ``doctor explains a missing installation without calling it an error`` () =
    withProject (fun project ->
        let report = diagnose project payload

        Assert.Equal(ExitCodes.success, doctorExitCode report)
        Assert.Contains(report.Diagnoses, fun d -> d.Code = "SDE-DOCTOR-100"))
