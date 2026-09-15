/// Configuration migrations, expressed as explicit sequential transitions.
///
/// A migration moves an installation from configuration version N to N+1 and
/// nothing else. An upgrade across several versions runs each transition in
/// order — there are no N-to-M shortcuts, because a shortcut is a second
/// code path that has to be kept correct as the ladder grows.
///
/// Each migration declares its preconditions separately from its effect. The
/// whole ladder is checked before any of it runs, so a precondition failure
/// three steps in stops the upgrade before the first step has written
/// anything, rather than leaving the repository half-migrated.
///
/// Configuration versions so far:
///   1  `.sde/` with MANIFEST.json and VERSION, no `.echelon/` record.
///      Every installation produced by releases 1.0.0 to 1.1.1.
///   2  the same, plus `.echelon/sde.json`.
module Sde.Core.Migrations

type Migration =
    { Id: string
      FromConfiguration: int
      ToConfiguration: int
      Description: string
      /// Checked against the repository before any migration runs. Returns
      /// the reason it cannot proceed, or None.
      Precondition: string -> string option
      /// Applies the migration. `installedVersion` is the payload version in
      /// place once the payload step of the upgrade has been applied.
      Apply: string -> string -> Result<unit, string> }

let recordMigrationId = "0001-echelon-installation-record"

let recordMigrationDescription =
    sprintf "adopt the shared %s/ installation record" Ownership.echelonRootName

/// 1 -> 2: write `.echelon/sde.json` from the installation already on disk.
///
/// Purely additive. It creates one Generated file and touches nothing else,
/// so a repository that has customised anything keeps every byte of it. The
/// precondition guards the one case where writing would be wrong: a record
/// that already exists but cannot be read, where overwriting would destroy
/// evidence needed to diagnose it.
let private echelonRecordMigration =
    { Id = recordMigrationId
      FromConfiguration = 1
      ToConfiguration = 2
      Description = recordMigrationDescription
      Precondition =
        fun projectRoot ->
            match InstallationRecord.read projectRoot with
            | InstallationRecord.Absent -> None
            | InstallationRecord.Present _ -> None
            | InstallationRecord.Unreadable detail ->
                Some(
                    sprintf
                        "%s already exists but cannot be read (%s); resolve or remove it before upgrading"
                        Ownership.installationRecordName
                        detail
                )
      Apply =
        fun projectRoot installedVersion ->
            try
                let record = InstallationRecord.create Packaging.packageName installedVersion
                FileSystem.writeFileInto (InstallationRecord.recordPath projectRoot) (InstallationRecord.serialize record)
                Ok()
            with ex ->
                Error(sprintf "could not write %s: %s" Ownership.installationRecordName ex.Message) }

/// Every known migration, ordered by the version it moves from. Adding a
/// configuration version means adding exactly one entry here.
let all = [ echelonRecordMigration ]

/// Builds the ordered chain of migrations from `current` to `target`.
/// Reports the missing link rather than silently skipping a gap.
let chain (current: int) (target: int) : Result<Migration list, string * string> =
    let rec build at acc =
        if at >= target then
            Ok(List.rev acc)
        else
            match all |> List.tryFind (fun m -> m.FromConfiguration = at) with
            | None ->
                Error(
                    sprintf "configuration-%d" at,
                    sprintf "no migration is defined from configuration version %d to %d" at (at + 1)
                )
            | Some migration -> build migration.ToConfiguration (migration :: acc)

    build current []

/// Builds the chain and checks every precondition up front.
let planFor (projectRoot: string) (current: int) (target: int) : Result<Migration list, string * string> =
    match chain current target with
    | Error failure -> Error failure
    | Ok migrations ->
        let failed =
            migrations
            |> List.tryPick (fun migration ->
                migration.Precondition projectRoot
                |> Option.map (fun detail -> migration.Id, detail))

        match failed with
        | Some failure -> Error failure
        | None -> Ok migrations

/// Chain construction without a repository to check against, used while
/// planning before the project root is known to be inspectable.
let plan (current: int) (target: int) : Result<Migration list, string * string> = chain current target

/// Runs the chain in order, stopping at the first failure and reporting
/// exactly which migration failed and which ones had already been applied.
let run (projectRoot: string) (installedVersion: string) (migrations: Migration list) : Result<unit, string> =
    let rec go applied remaining =
        match remaining with
        | [] -> Ok()
        | (migration: Migration) :: rest ->
            match migration.Precondition projectRoot with
            | Some detail -> Error(sprintf "migration %s precondition failed: %s" migration.Id detail)
            | None ->
                match migration.Apply projectRoot installedVersion with
                | Error detail ->
                    let appliedNote =
                        if List.isEmpty applied then
                            "no migration had been applied"
                        else
                            sprintf "already applied: %s" (applied |> List.rev |> String.concat ", ")

                    Error(sprintf "migration %s failed: %s (%s)" migration.Id detail appliedNote)
                | Ok() -> go (migration.Id :: applied) rest

    go [] migrations
