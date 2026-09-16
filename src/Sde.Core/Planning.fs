/// Turning an inspected state into a validated plan.
///
/// A plan is a value. Producing one touches nothing, which is the whole
/// mechanism behind `--dry-run` and `--check`: those flags do not take a
/// different code path that happens to skip the writes, they simply stop
/// after this module and never call Execution.
///
/// `PlannedChange` deliberately describes changes at the level of intent
/// ("replace the payload", "run this migration") rather than as a flat list
/// of file writes, so that a refusal can explain what was being attempted.
module Sde.Core.Planning

open Sde.Core.Inspection

type PlannedChange =
    /// Write the whole execution package into the install root, replacing
    /// whatever ToolOwned content is there. Only ever planned when the
    /// existing payload is intact or absent.
    | InstallPayload of version: string * fileCount: int
    | ReplacePayload of fromVersion: string * toVersion: string * fileCount: int
    /// Write `.echelon/sde.json`. Generated, so writing it is always safe.
    | WriteInstallationRecord of version: string * configurationVersion: int
    /// Run one numbered configuration migration.
    | RunMigration of id: string * fromConfiguration: int * toConfiguration: int * description: string

let describeChange =
    function
    | InstallPayload(version, fileCount) -> sprintf "install SDE v%s execution package (%d files) into %s/" version fileCount Ownership.installRootName
    | ReplacePayload(fromVersion, toVersion, fileCount) ->
        sprintf "replace %s/ payload v%s with v%s (%d files)" Ownership.installRootName fromVersion toVersion fileCount
    | WriteInstallationRecord(version, configurationVersion) ->
        sprintf "write %s recording v%s at configuration version %d" Ownership.installationRecordName version configurationVersion
    | RunMigration(id, fromConfiguration, toConfiguration, description) ->
        sprintf "run migration %s (configuration %d -> %d): %s" id fromConfiguration toConfiguration description

/// Why a plan cannot be produced or cannot be executed. A refusal is a
/// first-class outcome, not an exception: the user needs to be told exactly
/// what is in the way and what would unblock it.
type PlanRefusal =
    | NotADirectory of path: string
    | NotWritable of path: string
    | LocalModifications of version: string * problems: InstallationProblem list
    | UnreadableInstallation of detail: string
    | WouldDowngrade of installed: string * packaged: string
    | NotInstalledYet
    | MigrationPrecondition of id: string * detail: string
    | SharedConflict of path: string * detail: string

let describeRefusal =
    function
    | NotADirectory path -> sprintf "%s is not a directory." path
    | NotWritable path -> sprintf "%s is not writable." path
    | LocalModifications(version, _) ->
        sprintf "SDE v%s is installed but has local modifications; refusing to overwrite it." version
    | UnreadableInstallation detail -> sprintf "The existing installation record could not be read: %s" detail
    | WouldDowngrade(installed, packaged) ->
        sprintf "Installed SDE v%s is newer than this CLI's packaged v%s. Refusing to downgrade." installed packaged
    | NotInstalledYet -> sprintf "No %s/ installation found. Run `sde init` first." Ownership.installRootName
    | MigrationPrecondition(id, detail) -> sprintf "Migration %s cannot run: %s" id detail
    | SharedConflict(path, detail) -> sprintf "%s is present but not usable: %s" path detail

/// The outcome of planning: either there is nothing to do, a set of changes
/// to make, or a reason not to proceed.
type Plan =
    { Changes: PlannedChange list
      /// Restated on the plan so a dry-run can report what it inspected
      /// without the caller re-reading the repository.
      CurrentState: InstallationState }

type PlanOutcome =
    | NoChangesNeeded of InstallationState
    | Changes of Plan
    | Refused of PlanRefusal

let private problemsOf state =
    match state with
    | Invalid(_, problems) -> problems
    | NotInstalled
    | Installed _
    | UpgradeRequired _
    | AheadOfCli _ -> []

/// Turns an Invalid state into the right refusal. Kept in one place so init
/// and upgrade refuse identically for identical damage.
let private refuseInvalid (installed: InstalledVersion option) (problems: InstallationProblem list) =
    match problems |> List.tryFind isBlocking with
    | Some(ManifestUnreadable detail) -> UnreadableInstallation detail
    | Some(InstallationRecordUnreadable detail) -> UnreadableInstallation detail
    | Some(UnparsableInstalledVersion raw) ->
        UnreadableInstallation(sprintf "installed version %s is not a major.minor.patch version" (Json.quote raw))
    | Some _
    | None ->
        match problems |> List.tryPick (function
                  | SharedFileConflict(path, detail) -> Some(path, detail)
                  | _ -> None) with
        | Some(path, detail) -> SharedConflict(path, detail)
        | None ->
            let version =
                installed |> Option.map (fun i -> i.Version) |> Option.defaultValue "unknown"

            LocalModifications(version, problems)

let private preflight (projectRoot: string) =
    if not (FileSystem.directoryExists projectRoot) then
        Some(NotADirectory projectRoot)
    elif not (FileSystem.isWritableDirectory projectRoot) then
        Some(NotWritable projectRoot)
    else
        None

/// Plans `init`: bring the repository into a valid installed state.
///
/// Idempotency is a property of this function, not of the executor. A second
/// `init` over an intact, current installation returns NoChangesNeeded, so
/// there is no code path on which it could rewrite a file and change its
/// mtime.
let planInit (repository: Repository) (payloadVersion: string) (payloadFileCount: int) : PlanOutcome =
    match preflight repository.Root with
    | Some refusal -> Refused refusal
    | None ->

    match repository.State with
    | NotInstalled ->
        Changes
            { Changes =
                [ InstallPayload(payloadVersion, payloadFileCount)
                  WriteInstallationRecord(payloadVersion, InstallationRecord.currentConfigurationVersion) ]
              CurrentState = repository.State }

    | Installed _ -> NoChangesNeeded repository.State

    // An installation that is merely behind is NOT upgraded by init. That is
    // deliberate and matches the behaviour every released version has had:
    // moving a repository's methodology to a new version is a decision, and
    // `upgrade` is where that decision is expressed. init reports it.
    | UpgradeRequired(installed, available) ->
        if installed.ConfigurationVersion < InstallationRecord.currentConfigurationVersion
           && installed.Version = available then
            // Same payload, missing only the installation record. Writing a
            // Generated file that should already exist is a repair init is
            // allowed to make: nothing user-visible is replaced by it.
            Changes
                { Changes =
                    [ RunMigration(
                          Migrations.recordMigrationId,
                          installed.ConfigurationVersion,
                          InstallationRecord.currentConfigurationVersion,
                          Migrations.recordMigrationDescription
                      ) ]
                  CurrentState = repository.State }
        else
            NoChangesNeeded repository.State

    | AheadOfCli _ -> NoChangesNeeded repository.State

    | Invalid(installed, problems) -> Refused(refuseInvalid installed problems)

/// Plans `upgrade`: move an existing installation to the version and
/// configuration this CLI carries, running each intermediate migration in
/// order. Never plans a downgrade and never plans over local modifications.
let planUpgrade (repository: Repository) (payloadVersion: string) (payloadFileCount: int) : PlanOutcome =
    match preflight repository.Root with
    | Some refusal -> Refused refusal
    | None ->

    match repository.State with
    | NotInstalled -> Refused NotInstalledYet

    | Invalid(installed, problems) -> Refused(refuseInvalid installed problems)

    | AheadOfCli(installed, available) -> Refused(WouldDowngrade(installed.Version, available))

    | Installed _ -> NoChangesNeeded repository.State

    | UpgradeRequired(installed, available) ->
        match Migrations.plan installed.ConfigurationVersion InstallationRecord.currentConfigurationVersion with
        | Error(id, detail) -> Refused(MigrationPrecondition(id, detail))
        | Ok migrations ->
            let payloadChange =
                if installed.Version = available then
                    []
                else
                    [ ReplacePayload(installed.Version, available, payloadFileCount) ]

            let migrationChanges =
                migrations
                |> List.map (fun (m: Migrations.Migration) ->
                    RunMigration(m.Id, m.FromConfiguration, m.ToConfiguration, m.Description))

            Changes
                { Changes =
                    payloadChange
                    @ migrationChanges
                    @ [ WriteInstallationRecord(available, InstallationRecord.currentConfigurationVersion) ]
                  CurrentState = repository.State }
