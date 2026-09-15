/// Applying a validated plan, and only a validated plan.
///
/// This is the single module in the codebase that writes to a consuming
/// repository. Everything else inspects, decides or reports. Keeping the
/// write surface to one file is what makes "status never mutates" and
/// "dry-run never mutates" auditable rather than asserted.
///
/// Failure safety: the payload is staged into a sibling temporary directory,
/// re-verified against its own manifest there, and only then swapped into
/// place with a rename. A failure before the rename leaves the previous
/// installation untouched; a failure during it restores the previous
/// installation from the backup it moved aside.
module Sde.Core.Execution

open System
open System.IO
open Sde.Core.Planning

type ExecutionFailure =
    { Stage: string
      Detail: string
      /// Changes that had already been applied when the failure happened, so
      /// a partial application is always reported rather than silently left
      /// behind.
      Applied: PlannedChange list }

let describeFailure (failure: ExecutionFailure) =
    let applied =
        if List.isEmpty failure.Applied then
            "No change had been applied."
        else
            "Already applied: " + (failure.Applied |> List.map describeChange |> String.concat "; ") + "."

    sprintf "%s failed: %s %s" failure.Stage failure.Detail applied

/// Copies a verified payload directory into the repository's install root.
///
/// The temporary and backup directories are siblings of the final target so
/// that the swap is a same-filesystem rename rather than a cross-device copy
/// that could be interrupted half way.
let installPayload (payloadDir: string) (projectRoot: string) : Result<Manifest.Manifest, string> =
    match Manifest.read payloadDir with
    | Error detail -> Error(sprintf "refusing to install: source package failed manifest check: %s" detail)
    | Ok _ ->

    let targetDir = Inspection.installDirFor projectRoot
    let suffix () = Guid.NewGuid().ToString("N").Substring(0, 12)
    let tempDir = Path.Combine(projectRoot, sprintf ".sde.new-%s" (suffix ()))
    let backupDir = Path.Combine(projectRoot, sprintf ".sde.old-%s" (suffix ()))

    try
        try
            FileSystem.removeTree tempDir

            let copied =
                match FileSystem.listManagedFiles payloadDir with
                | Error detail -> Error detail
                | Ok paths ->
                    paths
                    |> List.fold
                        (fun state relPath ->
                            match state with
                            | Error _ -> state
                            | Ok() ->
                                match Paths.safeJoin payloadDir relPath, Paths.safeJoin tempDir relPath with
                                | Ok source, Ok destination ->
                                    FileSystem.copyFileInto source destination
                                    Ok()
                                | Error detail, _
                                | _, Error detail -> Error detail)
                        (Ok())

            match copied with
            | Error detail -> Error detail
            | Ok() ->

            // Re-verify the staged copy independently. A failure here means
            // the filesystem did not store what was written, which must not
            // be allowed to replace a working installation.
            match Inspection.diffInstallation tempDir with
            | Error detail -> Error(sprintf "install verification failed after copy: %s" detail)
            | Ok diff when not (Inspection.isIntact diff) ->
                Error
                    "install verification failed after copy (this indicates a filesystem or copy problem, not a source-package problem)"
            | Ok _ ->

            let hadPrevious = Directory.Exists targetDir

            if hadPrevious then
                Directory.Move(targetDir, backupDir)

            try
                Directory.Move(tempDir, targetDir)
            with _ ->
                // Put the previous installation back before letting the
                // failure propagate: a repository must never be left with no
                // .sde/ at all because a rename failed.
                if hadPrevious then Directory.Move(backupDir, targetDir)
                reraise ()

            if hadPrevious then
                FileSystem.removeTree backupDir

            match Inspection.diffInstallation targetDir with
            | Error detail -> Error detail
            | Ok diff -> Ok diff.Manifest
        with ex ->
            Error ex.Message
    finally
        FileSystem.removeTree tempDir

/// Applies a plan's changes in order.
let apply (projectRoot: string) (payloadDir: string) (plan: Plan) : Result<unit, ExecutionFailure> =
    let rec go applied remaining =
        match remaining with
        | [] -> Ok()
        | change :: rest ->
            let fail stage detail =
                Error
                    { Stage = stage
                      Detail = detail
                      Applied = List.rev applied }

            match change with
            | InstallPayload _
            | ReplacePayload _ ->
                match installPayload payloadDir projectRoot with
                | Error detail -> fail "Installing the execution package" detail
                | Ok _ -> go (change :: applied) rest

            | RunMigration(id, fromConfiguration, toConfiguration, _) ->
                let installedVersion =
                    match Manifest.read (Inspection.installDirFor projectRoot) with
                    | Ok manifest -> Ok manifest.SdeVersion
                    | Error detail -> Error detail

                match installedVersion with
                | Error detail -> fail (sprintf "Migration %s" id) detail
                | Ok installedVersion ->
                    match Migrations.chain fromConfiguration toConfiguration with
                    | Error(_, detail) -> fail (sprintf "Migration %s" id) detail
                    | Ok migrations ->
                        match Migrations.run projectRoot installedVersion migrations with
                        | Error detail -> fail (sprintf "Migration %s" id) detail
                        | Ok() -> go (change :: applied) rest

            | WriteInstallationRecord(version, _) ->
                try
                    let record = InstallationRecord.create Packaging.packageName version
                    FileSystem.writeFileInto (InstallationRecord.recordPath projectRoot) (InstallationRecord.serialize record)
                    go (change :: applied) rest
                with ex ->
                    fail "Writing the installation record" ex.Message

    go [] plan.Changes
