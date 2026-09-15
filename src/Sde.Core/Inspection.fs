/// Reading the repository and deciding what state it is in.
///
/// This module is the "inspect" and "determine installation state" halves of
/// the lifecycle pipeline, and it is strictly read-only: nothing here writes,
/// creates, repairs or deletes anything. Every command starts by calling into
/// it, which is what makes `status`, `verify` and `--dry-run` provably
/// non-mutating — they simply never reach the execution module.
module Sde.Core.Inspection

open System.IO

/// A single way an installation can be wrong. Modelled case by case rather
/// than as a string so that `doctor` can explain each one and `verify` can
/// count them without parsing prose.
type InstallationProblem =
    /// The installation's own record could not be read or did not validate.
    | ManifestUnreadable of detail: string
    /// A managed file's content no longer matches the hash recorded for it.
    | ManagedFileModified of path: string * ownership: Ownership.Ownership
    /// A file the manifest declares is not on disk.
    | ManagedFileMissing of path: string
    /// A file exists under the install root that the manifest does not declare.
    | UnexpectedManagedFile of path: string
    /// VERSION and MANIFEST.json disagree about which version is installed.
    | VersionFileMismatch of versionFile: string * manifestVersion: string
    /// The installed version string is not a version this tool can order.
    | UnparsableInstalledVersion of raw: string
    /// `.echelon/sde.json` exists but is not a record this tool can trust.
    | InstallationRecordUnreadable of detail: string
    /// `.echelon/sde.json` claims a different version than `.sde/` contains.
    | InstallationRecordDisagrees of recordVersion: string * manifestVersion: string
    /// A Shared file is present but its content is not valid.
    | SharedFileConflict of path: string * detail: string

let describeProblem =
    function
    | ManifestUnreadable detail -> sprintf "installation record could not be read: %s" detail
    | ManagedFileModified(path, ownership) ->
        sprintf "%s has been modified locally (%s)" path (Ownership.describe ownership)
    | ManagedFileMissing path -> sprintf "%s is declared by the manifest but missing" path
    | UnexpectedManagedFile path -> sprintf "%s is present but not declared by the manifest" path
    | VersionFileMismatch(versionFile, manifestVersion) ->
        sprintf
            "VERSION file (%s) disagrees with MANIFEST.json sdeVersion (%s)"
            (Json.quote versionFile)
            (Json.quote manifestVersion)
    | UnparsableInstalledVersion raw -> sprintf "installed version %s is not a major.minor.patch version" (Json.quote raw)
    | InstallationRecordUnreadable detail -> detail
    | InstallationRecordDisagrees(recordVersion, manifestVersion) ->
        sprintf
            "%s records version %s but %s/%s records %s"
            Ownership.installationRecordName
            (Json.quote recordVersion)
            Ownership.installRootName
            Ownership.manifestName
            (Json.quote manifestVersion)
    | SharedFileConflict(path, detail) -> sprintf "%s is present but not usable: %s" path detail

/// A problem that makes the installation unsafe to act on at all, as opposed
/// to one that merely makes it invalid. Commands refuse to plan changes over
/// a blocking problem rather than guessing what the user meant.
let isBlocking =
    function
    | ManifestUnreadable _
    | UnparsableInstalledVersion _
    | InstallationRecordUnreadable _ -> true
    | ManagedFileModified _
    | ManagedFileMissing _
    | UnexpectedManagedFile _
    | VersionFileMismatch _
    | InstallationRecordDisagrees _
    | SharedFileConflict _ -> false

type InstalledVersion =
    { Version: string
      ConfigurationVersion: int
      MethodVersion: string option
      SourceRevision: string option
      ManagedFileCount: int }

/// What the repository is, as one closed set of possibilities. Every command
/// branches on this and nothing else, so an unhandled state is a compile
/// error rather than a silent fall-through.
type InstallationState =
    | NotInstalled
    /// Installed, intact and at the version this CLI carries.
    | Installed of InstalledVersion
    /// Installed and intact, but behind this CLI's payload or behind the
    /// current configuration version. Carries what it would move to.
    | UpgradeRequired of InstalledVersion * available: string
    /// Installed but ahead of this CLI's payload. Never downgraded.
    | AheadOfCli of InstalledVersion * available: string
    | Invalid of InstalledVersion option * InstallationProblem list

/// Everything a command needs to know about the repository, gathered once.
type Repository =
    { Root: string
      InstallDir: string
      State: InstallationState
      /// Present only when the configuration file exists and is valid; the
      /// conflict is reported as a problem otherwise.
      StructuralConfig: Result<StructuralReview.Config, string>
      /// Other Echelon tools' records found in the shared root. Read purely
      /// so that changes to `.echelon/` can be proven not to disturb them.
      OtherEchelonRecords: string list }

let installDirFor (projectRoot: string) =
    Path.Combine(projectRoot, Ownership.installRootName)

/// Compares the installation against its own manifest. Returns the modified,
/// missing and unexpected sets; never repairs anything.
type IntegrityDiff =
    { Manifest: Manifest.Manifest
      Modified: string list
      Missing: string list
      Unexpected: string list
      VersionFileMismatch: (string * string) option }

/// How a single declared file failed its check, if it failed at all.
type private EntryFault =
    | FaultMissing
    | FaultModified

let diffInstallation (installDir: string) : Result<IntegrityDiff, string> =
    match Manifest.read installDir with
    | Error message -> Error message
    | Ok manifest ->
        let checkEntry (entry: Manifest.ManagedFile) =
            match Paths.safeJoin installDir entry.Path with
            | Error message -> Error message
            | Ok absolutePath ->
                if not (FileSystem.fileExists absolutePath) then
                    Ok(Some(FaultMissing, entry.Path))
                elif FileSystem.sha256File absolutePath <> entry.Sha256 then
                    Ok(Some(FaultModified, entry.Path))
                else
                    Ok None

        let folded =
            manifest.Files
            |> List.fold
                (fun state entry ->
                    match state with
                    | Error _ -> state
                    | Ok acc ->
                        match checkEntry entry with
                        | Error message -> Error message
                        | Ok None -> Ok acc
                        | Ok(Some found) -> Ok(found :: acc))
                (Ok [])

        match folded with
        | Error message -> Error message
        | Ok found ->
            let ordinal a b = System.String.CompareOrdinal(a, b)

            let missing =
                found
                |> List.choose (fun (kind, path) -> if kind = FaultMissing then Some path else None)
                |> List.sortWith ordinal

            let modified =
                found
                |> List.choose (fun (kind, path) -> if kind = FaultModified then Some path else None)
                |> List.sortWith ordinal

            match FileSystem.listManagedFiles installDir with
            | Error message -> Error message
            | Ok onDisk ->
                let declared = manifest.Files |> List.map (fun f -> f.Path) |> Set.ofList

                let unexpected =
                    onDisk
                    |> List.filter (fun path -> path <> Ownership.manifestName && not (declared.Contains path))
                    |> List.sortWith ordinal

                // An independent consistency check, deliberately separate
                // from the per-file hashes above: VERSION's text must agree
                // with MANIFEST.json's sdeVersion. A hand-edit that changed
                // both consistently would pass every hash check and still be
                // an installation lying about its own version.
                let versionMismatch =
                    match Paths.safeJoin installDir Ownership.versionFileName with
                    | Error _ -> None
                    | Ok versionPath ->
                        if FileSystem.fileExists versionPath && not (List.contains Ownership.versionFileName missing) then
                            let content = (FileSystem.readAllText versionPath).Trim()

                            if content <> manifest.SdeVersion then
                                Some(content, manifest.SdeVersion)
                            else
                                None
                        else
                            None

                Ok
                    { Manifest = manifest
                      Modified = modified
                      Missing = missing
                      Unexpected = unexpected
                      VersionFileMismatch = versionMismatch }

let isIntact (diff: IntegrityDiff) =
    diff.Modified.IsEmpty
    && diff.Missing.IsEmpty
    && diff.Unexpected.IsEmpty
    && diff.VersionFileMismatch.IsNone

let private listOtherEchelonRecords (projectRoot: string) =
    let echelonDir = Path.Combine(projectRoot, Ownership.echelonRootName)

    if not (Directory.Exists echelonDir) then
        []
    else
        try
            Directory.GetFiles echelonDir
            |> Array.map Path.GetFileName
            |> Array.filter (fun name -> name <> InstallationRecord.toolName + ".json")
            |> Array.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
            |> List.ofArray
        with _ ->
            []

/// Determines installation state relative to the payload this CLI carries.
/// `availableVersion` is the version of the bundled execution package.
let determineState (projectRoot: string) (availableVersion: string) : InstallationState =
    let installDir = installDirFor projectRoot

    if not (Directory.Exists installDir) then
        NotInstalled
    else

    match diffInstallation installDir with
    | Error message -> Invalid(None, [ ManifestUnreadable message ])
    | Ok diff ->
        let recordRead = InstallationRecord.read projectRoot

        let configurationVersion =
            match recordRead with
            | InstallationRecord.Present record -> record.ConfigurationVersion
            | InstallationRecord.Absent -> InstallationRecord.legacyConfigurationVersion
            | InstallationRecord.Unreadable _ -> InstallationRecord.legacyConfigurationVersion

        let installed =
            { Version = diff.Manifest.SdeVersion
              ConfigurationVersion = configurationVersion
              MethodVersion = diff.Manifest.MethodVersion
              SourceRevision = diff.Manifest.SourceRevision
              ManagedFileCount = diff.Manifest.Files.Length }

        let integrityProblems =
            [ match diff.VersionFileMismatch with
              | Some(onDisk, manifestVersion) -> yield VersionFileMismatch(onDisk, manifestVersion)
              | None -> ()
              for path in diff.Modified -> ManagedFileModified(path, Ownership.classifyManaged path)
              for path in diff.Missing -> ManagedFileMissing path
              for path in diff.Unexpected -> UnexpectedManagedFile path ]

        let recordProblems =
            match recordRead with
            | InstallationRecord.Absent -> []
            | InstallationRecord.Unreadable detail -> [ InstallationRecordUnreadable detail ]
            | InstallationRecord.Present record ->
                if record.InstalledVersion <> diff.Manifest.SdeVersion then
                    [ InstallationRecordDisagrees(record.InstalledVersion, diff.Manifest.SdeVersion) ]
                else
                    []

        let configProblems =
            match StructuralReview.loadConfig projectRoot with
            | Ok _ -> []
            | Error detail -> [ SharedFileConflict(Ownership.structuralConfigName, detail) ]

        let problems = integrityProblems @ recordProblems @ configProblems

        if not problems.IsEmpty then
            Invalid(Some installed, problems)
        else
            match SemVer.compare diff.Manifest.SdeVersion availableVersion with
            | Error _ -> Invalid(Some installed, [ UnparsableInstalledVersion diff.Manifest.SdeVersion ])
            | Ok comparison ->
                if comparison > 0 then AheadOfCli(installed, availableVersion)
                elif comparison < 0 then UpgradeRequired(installed, availableVersion)
                elif configurationVersion < InstallationRecord.currentConfigurationVersion then
                    // Same payload version, but the installation predates the
                    // .echelon record. Still an upgrade: there is a migration
                    // to run even though the version string does not move.
                    UpgradeRequired(installed, availableVersion)
                else
                    Installed installed

/// Gathers the whole repository picture in one read-only pass.
let inspectRepository (projectRoot: string) (availableVersion: string) : Repository =
    { Root = projectRoot
      InstallDir = installDirFor projectRoot
      State = determineState projectRoot availableVersion
      StructuralConfig = StructuralReview.loadConfig projectRoot
      OtherEchelonRecords = listOtherEchelonRecords projectRoot }
