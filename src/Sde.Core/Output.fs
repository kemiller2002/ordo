/// The machine-readable interface.
///
/// Every `--json` document this tool can emit is built here from a typed
/// lifecycle result. Nothing else in the codebase writes JSON to stdout, so
/// the schema is one file to review and one file to version.
///
/// SCHEMA STABILITY: each document carries `schemaVersion`. Fields are added
/// without bumping it; a field that is removed or changes meaning requires a
/// bump. Consumers should ignore unknown fields.
module Sde.Core.Output

open Sde.Core.Json
open Sde.Core.Inspection
open Sde.Core.Lifecycle

let schemaVersion = 1

let private optionalString =
    function
    | Some value -> JString value
    | None -> JNull

/// A stable discriminator for installation state, so an agent branches on a
/// token rather than on prose.
let stateToken =
    function
    | NotInstalled -> "not-installed"
    | Installed _ -> "installed"
    | UpgradeRequired _ -> "upgrade-required"
    | AheadOfCli _ -> "ahead-of-cli"
    | Invalid _ -> "invalid"

/// A stable discriminator for each way an installation can be wrong.
let problemToken =
    function
    | ManifestUnreadable _ -> "manifest-unreadable"
    | ManagedFileModified _ -> "managed-file-modified"
    | ManagedFileMissing _ -> "managed-file-missing"
    | UnexpectedManagedFile _ -> "unexpected-managed-file"
    | VersionFileMismatch _ -> "version-file-mismatch"
    | UnparsableInstalledVersion _ -> "unparsable-installed-version"
    | InstallationRecordUnreadable _ -> "installation-record-unreadable"
    | InstallationRecordDisagrees _ -> "installation-record-disagrees"
    | SharedFileConflict _ -> "shared-file-conflict"

let private problemPath =
    function
    | ManagedFileModified(path, _) -> Some path
    | ManagedFileMissing path -> Some path
    | UnexpectedManagedFile path -> Some path
    | SharedFileConflict(path, _) -> Some path
    | ManifestUnreadable _
    | VersionFileMismatch _
    | UnparsableInstalledVersion _
    | InstallationRecordUnreadable _
    | InstallationRecordDisagrees _ -> None

let problemToJson (problem: InstallationProblem) =
    JObject
        [ "code", JString(problemToken problem)
          "path", optionalString (problemPath problem)
          "ownership",
          (match problem with
           | ManagedFileModified(_, ownership) -> JString(Ownership.describe ownership)
           | ManagedFileMissing path
           | UnexpectedManagedFile path -> JString(Ownership.describe (Ownership.classifyManaged path))
           | SharedFileConflict(path, _) -> JString(Ownership.describe (Ownership.classify path))
           | ManifestUnreadable _
           | VersionFileMismatch _
           | UnparsableInstalledVersion _
           | InstallationRecordUnreadable _
           | InstallationRecordDisagrees _ -> JNull)
          "message", JString(describeProblem problem) ]

let private installedToJson (installed: InstalledVersion option) =
    match installed with
    | None -> JNull
    | Some i ->
        JObject
            [ "version", JString i.Version
              "configurationVersion", JInt i.ConfigurationVersion
              "methodVersion", optionalString i.MethodVersion
              "sourceRevision", optionalString i.SourceRevision
              "managedFileCount", JInt i.ManagedFileCount ]

// ---------------------------------------------------------------------------

let statusToJson (report: StatusReport) =
    JObject
        [ "schemaVersion", JInt schemaVersion
          "command", JString "status"
          "tool", JString report.Tool
          "package", JString report.Package
          "cliVersion", JString report.CliVersion
          "payloadVersion", JString report.PayloadVersion
          "methodVersion", optionalString report.MethodVersion
          "state", JString(stateToken report.State)
          "installed", installedToJson report.Installed
          "configuration",
          JObject
              [ "source", JString report.ConfigurationSource
                "valid", JBool report.ConfigurationValid ]
          "verified", JBool report.Verified
          "upgradeAvailable", optionalString report.UpgradeAvailable
          "problems", JArray(report.Problems |> List.map problemToJson)
          "exitCode", JInt(statusExitCode report) ]

let private structuralToJson (report: StructuralReview.Report option) (error: string option) =
    match report, error with
    | _, Some detail -> JObject [ "ran", JBool false; "error", JString detail ]
    | None, None -> JObject [ "ran", JBool false; "error", JNull ]
    | Some report, None ->
        JObject
            [ "ran", JBool true
              "error", JNull
              "enabled", JBool report.Config.Enabled
              "configurationSource", JString report.Config.Source
              "inspectedFiles", JInt report.InspectedFiles
              "findings",
              JArray
                  [ for finding in report.Findings ->
                        JObject
                            [ "code", JString finding.Code
                              "band", JString(StructuralReview.describeBand finding.Band)
                              "path", JString finding.Path
                              "lineCount", JInt finding.LineCount ] ] ]

let verifyToJson (report: VerifyReport) =
    JObject
        [ "schemaVersion", JInt schemaVersion
          "command", JString "verify"
          "tool", JString InstallationRecord.toolName
          "package", JString Packaging.packageName
          "state", JString(stateToken report.State)
          "installedVersion", optionalString report.InstalledVersion
          "managedFileCount", JInt report.ManagedFileCount
          "strict", JBool report.Strict
          "passed", JBool report.Passed
          "problems", JArray(report.Problems |> List.map problemToJson)
          "strictFailures", JArray(report.StrictFailures |> List.map JString)
          "structural", structuralToJson report.Structural report.StructuralError
          "exitCode", JInt(verifyExitCode report) ]

let private changeToJson (change: Planning.PlannedChange) =
    let token, detail =
        match change with
        | Planning.InstallPayload(version, fileCount) ->
            "install-payload", [ "version", JString version; "fileCount", JInt fileCount ]
        | Planning.ReplacePayload(fromVersion, toVersion, fileCount) ->
            "replace-payload",
            [ "fromVersion", JString fromVersion
              "toVersion", JString toVersion
              "fileCount", JInt fileCount ]
        | Planning.WriteInstallationRecord(version, configurationVersion) ->
            "write-installation-record",
            [ "version", JString version
              "configurationVersion", JInt configurationVersion ]
        | Planning.RunMigration(id, fromConfiguration, toConfiguration, description) ->
            "run-migration",
            [ "id", JString id
              "fromConfiguration", JInt fromConfiguration
              "toConfiguration", JInt toConfiguration
              "description", JString description ]

    JObject(
        [ "change", JString token; "message", JString(Planning.describeChange change) ]
        @ detail
    )

let private outcomeToken =
    function
    | AlreadyCurrent _ -> "no-changes-needed"
    | Applied _ -> "applied"
    | ChangeOutcome.Planned _ -> "planned"
    | ChangeOutcome.Refused _ -> "refused"
    | Failed _ -> "failed"

let changeReportToJson (checkMode: bool) (dryRun: bool) (report: ChangeReport) =
    let changes =
        match report.Outcome with
        | Applied(changes, _) -> changes
        | ChangeOutcome.Planned changes -> changes
        | AlreadyCurrent _
        | ChangeOutcome.Refused _
        | Failed _ -> []

    JObject
        [ "schemaVersion", JInt schemaVersion
          "command", JString report.Command
          "tool", JString InstallationRecord.toolName
          "package", JString Packaging.packageName
          "payloadVersion", JString report.PayloadVersion
          "dryRun", JBool dryRun
          "check", JBool checkMode
          "stateBefore", JString(stateToken report.StateBefore)
          "outcome", JString(outcomeToken report.Outcome)
          "changed",
          JBool(
              match report.Outcome with
              | Applied _ -> true
              | AlreadyCurrent _
              | ChangeOutcome.Planned _
              | ChangeOutcome.Refused _
              | Failed _ -> false
          )
          "changes", JArray(changes |> List.map changeToJson)
          "resultingVersion",
          (match report.Outcome with
           | Applied(_, version) -> JString version
           | AlreadyCurrent _
           | ChangeOutcome.Planned _
           | ChangeOutcome.Refused _
           | Failed _ -> JNull)
          "refusal",
          (match report.Outcome with
           | ChangeOutcome.Refused refusal ->
               JObject
                   [ "message", JString(Planning.describeRefusal refusal)
                     "problems",
                     JArray(
                         match refusal with
                         | Planning.LocalModifications(_, problems) -> problems |> List.map problemToJson
                         | Planning.NotADirectory _
                         | Planning.NotWritable _
                         | Planning.UnreadableInstallation _
                         | Planning.WouldDowngrade _
                         | Planning.NotInstalledYet
                         | Planning.MigrationPrecondition _
                         | Planning.SharedConflict _ -> []
                     ) ]
           | AlreadyCurrent _
           | Applied _
           | ChangeOutcome.Planned _
           | Failed _ -> JNull)
          "failure",
          (match report.Outcome with
           | Failed failure -> JString(Execution.describeFailure failure)
           | AlreadyCurrent _
           | Applied _
           | ChangeOutcome.Planned _
           | ChangeOutcome.Refused _ -> JNull)
          "exitCode", JInt(changeExitCode checkMode report) ]

let doctorToJson (report: DoctorReport) =
    JObject
        [ "schemaVersion", JInt schemaVersion
          "command", JString "doctor"
          "tool", JString InstallationRecord.toolName
          "package", JString Packaging.packageName
          "payloadVersion", JString report.PayloadVersion
          "state", JString(stateToken report.State)
          "diagnoses",
          JArray
              [ for diagnosis in report.Diagnoses ->
                    JObject
                        [ "severity", JString(Diagnostics.describeSeverity diagnosis.Severity)
                          "code", JString diagnosis.Code
                          "summary", JString diagnosis.Summary
                          "cause", JString diagnosis.Cause
                          "remedy", optionalString diagnosis.Remedy ] ]
          "errors", JInt(report.Diagnoses |> List.filter (fun d -> d.Severity = Diagnostics.Severity.Error) |> List.length)
          "warnings", JInt(report.Diagnoses |> List.filter (fun d -> d.Severity = Diagnostics.Severity.Warning) |> List.length)
          "exitCode", JInt(doctorExitCode report) ]

/// The document emitted when a command cannot even start, so that `--json`
/// consumers always receive JSON on stdout rather than prose.
let errorToJson (command: string) (message: string) (exitCode: int) =
    JObject
        [ "schemaVersion", JInt schemaVersion
          "command", JString command
          "tool", JString InstallationRecord.toolName
          "package", JString Packaging.packageName
          "outcome", JString "error"
          "message", JString message
          "exitCode", JInt exitCode ]
