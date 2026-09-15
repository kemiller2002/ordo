/// `doctor` — explaining why something is wrong and what would fix it.
///
/// The difference from `verify` is the audience. `verify` answers a yes/no
/// question for CI. `doctor` is read by a human or an agent that has to act,
/// so every diagnosis carries a cause and a remedy, and severity is graded:
/// not every deviation is an error, and calling a warning an error trains
/// people to ignore the output.
module Sde.Core.Diagnostics

open Sde.Core.Inspection

/// Qualified access is required so that `Severity.Error` can never be
/// confused with `Result`'s `Error` at a match site.
[<RequireQualifiedAccess>]
type Severity =
    | Error
    | Warning
    | Information

let describeSeverity =
    function
    | Severity.Error -> "ERROR"
    | Severity.Warning -> "WARNING"
    | Severity.Information -> "INFORMATION"

type Diagnosis =
    { Severity: Severity
      /// Stable machine-readable identifier. Agents branch on this; the
      /// prose is for people and may be reworded between releases.
      Code: string
      Summary: string
      /// Why this is the case, in terms of what was observed.
      Cause: string
      /// What to do about it, or None when there is nothing to do.
      Remedy: string option }

let private diagnosisFor (problem: InstallationProblem) : Diagnosis =
    match problem with
    | ManifestUnreadable detail ->
        { Severity = Severity.Error
          Code = "SDE-DOCTOR-001"
          Summary = sprintf "The %s/ installation record cannot be read." Ownership.installRootName
          Cause = detail
          Remedy =
            Some(
                sprintf
                    "No command will act on an installation it cannot verify. Remove %s/ and run `%s init` to reinstall, or restore the directory from version control."
                    Ownership.installRootName
                    Packaging.executableName
            ) }

    | ManagedFileModified(path, ownership) ->
        { Severity = Severity.Error
          Code = "SDE-DOCTOR-002"
          Summary = sprintf "%s/%s has been modified locally." Ownership.installRootName path
          Cause =
            sprintf
                "Its content no longer matches the SHA-256 recorded at install time. This file is %s: the tool replaces it on upgrade, so a local edit would be lost and is therefore treated as a blocker instead."
                (Ownership.describe ownership)
          Remedy =
            Some(
                sprintf
                    "Restore the file from version control, or remove %s/ and reinstall. Record an intentional deviation outside %s/, which is not a place for project-specific content."
                    Ownership.installRootName
                    Ownership.installRootName
            ) }

    | ManagedFileMissing path ->
        { Severity = Severity.Error
          Code = "SDE-DOCTOR-003"
          Summary = sprintf "%s/%s is declared by the manifest but is not present." Ownership.installRootName path
          Cause = "The file was deleted after installation, or the installation was interrupted."
          Remedy =
            Some(
                sprintf
                    "Remove %s/ and run `%s init` to reinstall the execution package."
                    Ownership.installRootName
                    Packaging.executableName
            ) }

    | UnexpectedManagedFile path ->
        { Severity = Severity.Error
          Code = "SDE-DOCTOR-004"
          Summary = sprintf "%s/%s is present but not declared by the manifest." Ownership.installRootName path
          Cause =
            sprintf
                "%s/ holds only the installed execution package. A file the manifest does not declare means the directory was edited by hand or by another tool."
                Ownership.installRootName
          Remedy =
            Some(
                sprintf
                    "Move project-owned content out of %s/ and delete the file, or reinstall."
                    Ownership.installRootName
            ) }

    | VersionFileMismatch(versionFile, manifestVersion) ->
        { Severity = Severity.Error
          Code = "SDE-DOCTOR-005"
          Summary = "The installation disagrees with itself about which version is installed."
          Cause =
            sprintf
                "%s/VERSION says %s; %s/%s says %s. These are written together and can only differ if they were edited."
                Ownership.installRootName
                (Json.quote versionFile)
                Ownership.installRootName
                Ownership.manifestName
                (Json.quote manifestVersion)
          Remedy =
            Some(
                sprintf
                    "Remove %s/ and run `%s init` to reinstall a self-consistent installation."
                    Ownership.installRootName
                    Packaging.executableName
            ) }

    | UnparsableInstalledVersion raw ->
        { Severity = Severity.Error
          Code = "SDE-DOCTOR-006"
          Summary = sprintf "The installed version %s is not a version this tool can order." (Json.quote raw)
          Cause = "Versions must be major.minor.patch. Upgrade cannot decide whether this is older or newer than the packaged version."
          Remedy = Some(sprintf "Remove %s/ and reinstall." Ownership.installRootName) }

    | InstallationRecordUnreadable detail ->
        { Severity = Severity.Error
          Code = "SDE-DOCTOR-007"
          Summary = sprintf "%s exists but is not a valid installation record." Ownership.installationRecordName
          Cause = detail
          Remedy =
            Some(
                sprintf
                    "Delete %s and run `%s init`, which rewrites it from the installed package. Other files in %s/ belong to other Echelon Foundry tools and must be left alone."
                    Ownership.installationRecordName
                    Packaging.executableName
                    Ownership.echelonRootName
            ) }

    | InstallationRecordDisagrees(recordVersion, manifestVersion) ->
        { Severity = Severity.Warning
          Code = "SDE-DOCTOR-008"
          Summary = sprintf "%s is out of date." Ownership.installationRecordName
          Cause =
            sprintf
                "It records v%s while the installed package is v%s. The package is authoritative; the record is regenerated from it."
                recordVersion
                manifestVersion
          Remedy = Some(sprintf "Run `%s init` to rewrite the record." Packaging.executableName) }

    | SharedFileConflict(path, detail) ->
        { Severity = Severity.Error
          Code = "SDE-DOCTOR-009"
          Summary = sprintf "%s is present but cannot be used." path
          Cause = detail
          Remedy =
            Some(
                sprintf
                    "%s is a shared file: this tool defines its schema, your repository owns its values, and the tool will never rewrite it. Correct the reported field and re-run."
                    path
            ) }

/// Produces the full diagnosis for a repository. Read-only.
let diagnose (repository: Repository) (payloadVersion: string) : Diagnosis list =
    let stateDiagnoses =
        match repository.State with
        | NotInstalled ->
            [ { Severity = Severity.Information
                Code = "SDE-DOCTOR-100"
                Summary = sprintf "No %s/ installation is present in this repository." Ownership.installRootName
                Cause = "The execution package has not been installed here yet."
                Remedy = Some(sprintf "Run `%s init` to install SDE v%s." Packaging.executableName payloadVersion) } ]

        | Installed installed ->
            [ { Severity = Severity.Information
                Code = "SDE-DOCTOR-101"
                Summary = sprintf "SDE v%s is installed and intact." installed.Version
                Cause =
                    sprintf
                        "All %d managed files match the hashes recorded at install time, and the installation is at configuration version %d."
                        installed.ManagedFileCount
                        installed.ConfigurationVersion
                Remedy = None } ]

        | UpgradeRequired(installed, available) ->
            let summary =
                if installed.Version = available then
                    sprintf
                        "SDE v%s is installed but predates the %s/ installation record."
                        installed.Version
                        Ownership.echelonRootName
                else
                    sprintf "SDE v%s is installed; v%s is available." installed.Version available

            [ { Severity = Severity.Information
                Code = "SDE-DOCTOR-102"
                Summary = summary
                Cause =
                    sprintf
                        "The installation is at configuration version %d; this release installs configuration version %d."
                        installed.ConfigurationVersion
                        InstallationRecord.currentConfigurationVersion
                Remedy = Some(sprintf "Run `%s upgrade` when you are ready to move." Packaging.executableName) } ]

        | AheadOfCli(installed, available) ->
            [ { Severity = Severity.Warning
                Code = "SDE-DOCTOR-103"
                Summary =
                    sprintf "The installed SDE v%s is newer than this CLI's packaged v%s." installed.Version available
                Cause = "This CLI carries an older execution package than the one installed here."
                Remedy =
                    Some(
                        sprintf
                            "Use a newer %s release (`npx %s@latest`), or leave the installation as it is. Nothing will be downgraded."
                            Packaging.packageName
                            Packaging.packageName
                    ) } ]

        | Invalid(_, problems) -> problems |> List.map diagnosisFor

    let configDiagnoses =
        match repository.StructuralConfig with
        | Ok config when config.Source = Ownership.structuralConfigName ->
            [ { Severity = Severity.Information
                Code = "SDE-DOCTOR-110"
                Summary = sprintf "Structural review is configured by %s." Ownership.structuralConfigName
                Cause =
                    if config.Enabled then
                        sprintf
                            "Bands: review at %d lines, strong review at %d, justification above %d, conformance concern above %d."
                            config.Thresholds.ReviewAt
                            config.Thresholds.StrongReviewAt
                            config.Thresholds.JustificationAbove
                            config.Thresholds.ConformanceConcernAbove
                    else
                        "Structural review is disabled by this configuration."
                Remedy = None } ]
        | Ok _ -> []
        // A configuration failure is already reported as SDE-DOCTOR-009 via
        // the SharedFileConflict problem, so it is not repeated here.
        | Error _ -> []

    let echelonDiagnoses =
        match repository.OtherEchelonRecords with
        | [] -> []
        | others ->
            [ { Severity = Severity.Information
                Code = "SDE-DOCTOR-120"
                Summary = sprintf "%s/ also holds records for other Echelon Foundry tools." Ownership.echelonRootName
                Cause = sprintf "Found: %s." (String.concat ", " others)
                Remedy = None } ]

    stateDiagnoses @ configDiagnoses @ echelonDiagnoses

let hasErrors (diagnoses: Diagnosis list) =
    diagnoses |> List.exists (fun d -> d.Severity = Severity.Error)

let hasWarnings (diagnoses: Diagnosis list) =
    diagnoses |> List.exists (fun d -> d.Severity = Severity.Warning)
