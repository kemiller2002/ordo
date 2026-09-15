/// Human-readable rendering of lifecycle results.
///
/// Successful, uneventful output stays short: a command that did nothing
/// says so in one line. Detail that only matters when investigating lives
/// behind --verbose. None of this is ever mixed into --json output, which
/// is produced from Sde.Core.Output instead.
module Sde.Cli.Render

open Sde.Core
open Sde.Core.Inspection
open Sde.Core.Lifecycle

let private problemLines (verbose: bool) (problems: InstallationProblem list) =
    if problems.IsEmpty then
        []
    else
        // Without --verbose, long lists of paths are summarised per kind so
        // that a repository with hundreds of modified files does not bury
        // the one line that says what to do next.
        let group label items =
            match items with
            | [] -> []
            | _ when verbose -> [ label + ":" ] @ (items |> List.map (sprintf "  %s"))
            | [ single ] -> [ sprintf "%s: %s" label single ]
            | _ -> [ sprintf "%s: %d files (use --verbose to list them)" label items.Length ]

        let pick chooser = problems |> List.choose chooser

        let modified =
            pick (function
                | ManagedFileModified(path, _) -> Some path
                | _ -> None)

        let missing =
            pick (function
                | ManagedFileMissing path -> Some path
                | _ -> None)

        let unexpected =
            pick (function
                | UnexpectedManagedFile path -> Some path
                | _ -> None)

        let others =
            pick (function
                | ManagedFileModified _
                | ManagedFileMissing _
                | UnexpectedManagedFile _ -> None
                | other -> Some(describeProblem other))

        group "Modified" modified
        @ group "Missing" missing
        @ group "Unexpected" unexpected
        @ (others |> List.map (sprintf "  %s"))

// ---------------------------------------------------------------------------

let status (verbose: bool) (report: StatusReport) =
    let header = [ "SDE"; "" ]

    let body =
        match report.Installed with
        | None ->
            [ sprintf "Package:               %s" report.Package
              sprintf "CLI version:           %s" report.CliVersion
              "Installed version:     (none)"
              "Installation status:   not installed"
              sprintf "Run `%s init` to install." Packaging.executableName ]
        | Some installed ->
            [ sprintf "Package:               %s" report.Package
              sprintf "CLI version:           %s" report.CliVersion
              sprintf "Installed version:     %s" installed.Version
              sprintf "Configuration:         version %d%s"
                  installed.ConfigurationVersion
                  (if report.ConfigurationValid then "" else " (sde.config.json invalid)")
              sprintf "Method version:        %s" (installed.MethodVersion |> Option.defaultValue "unknown")
              sprintf "Source revision:       %s" (installed.SourceRevision |> Option.defaultValue "unknown")
              sprintf "Managed artifacts:     %d files" installed.ManagedFileCount
              sprintf "Installation status:   %s" (Output.stateToken report.State)
              sprintf "Verification:          %s" (if report.Verified then "passed" else "failed") ]

    let upgrade =
        match report.UpgradeAvailable, report.Installed with
        // Same payload version but an older configuration version: saying
        // "1.1.1 available" when 1.1.1 is installed would read as a bug.
        | Some available, Some installed when installed.Version = available ->
            [ sprintf
                  "Upgrade:               configuration %d -> %d pending (run `%s upgrade`)"
                  installed.ConfigurationVersion
                  InstallationRecord.currentConfigurationVersion
                  Packaging.executableName ]
        | Some available, _ ->
            [ sprintf "Upgrade:               %s available (run `%s upgrade`)" available Packaging.executableName ]
        | None, _ ->
            match report.State with
            | AheadOfCli(installed, available) ->
                [ sprintf "Upgrade:               none (installed %s is newer than this CLI's %s)" installed.Version available ]
            // UpgradeRequired always sets UpgradeAvailable, so it is handled
            // by the Some branch above; listed here to keep the match total.
            | UpgradeRequired _
            | NotInstalled
            | Installed _
            | Invalid _ -> [ "Upgrade:               none available" ]

    header @ body @ upgrade @ problemLines verbose report.Problems

// ---------------------------------------------------------------------------

let verify (verbose: bool) (report: VerifyReport) =
    match report.State with
    | NotInstalled -> [ sprintf "No %s/ installation found." Ownership.installRootName ]
    | Invalid(_, problems) ->
        [ "SDE verification failed." ] @ problemLines verbose problems
        @ [ sprintf "Run `%s doctor` to see what each problem means and how to fix it." Packaging.executableName ]
    | Installed _
    | UpgradeRequired _
    | AheadOfCli _ ->
        let version = report.InstalledVersion |> Option.defaultValue "unknown"

        let head =
            if report.Passed then
                [ sprintf "SDE v%s verified." version
                  sprintf "%d managed files verified." report.ManagedFileCount ]
            else
                [ sprintf "SDE v%s installation integrity verified." version ]

        let structural =
            match report.Structural, report.StructuralError with
            | _, Some detail -> [ detail ]
            | None, None -> []
            | Some structural, None ->
                if not structural.Config.Enabled then
                    [ sprintf "Structural review disabled by %s." structural.Config.Source ]
                elif structural.Findings.IsEmpty then
                    [ sprintf
                          "Structural review: %d source files inspected; no %s warnings."
                          structural.InspectedFiles
                          StructuralReview.findingCode ]
                else
                    let shown =
                        if verbose then
                            structural.Findings
                        else
                            structural.Findings |> List.truncate 10

                    let listed =
                        shown
                        |> List.map (fun finding ->
                            sprintf
                                "  %s [%s] %s: %d physical lines"
                                finding.Code
                                (StructuralReview.describeBand finding.Band)
                                finding.Path
                                finding.LineCount)

                    let elided =
                        if shown.Length < structural.Findings.Length then
                            [ sprintf
                                  "  ... and %d more (use --verbose to list them)"
                                  (structural.Findings.Length - shown.Length) ]
                        else
                            []

                    [ sprintf "Structural review %s (%d):" (if report.Strict then "failures" else "warnings") structural.Findings.Length ]
                    @ listed
                    @ elided
                    @ (if report.Strict then
                           []
                       else
                           [ "Structural warnings are review signals, not proof of semantic nonconformance; they do not change this command's success exit code." ])

        let strict =
            if report.StrictFailures.IsEmpty then
                []
            else
                [ "Strict verification failed:" ] @ (report.StrictFailures |> List.map (sprintf "  %s"))

        head @ structural @ strict

// ---------------------------------------------------------------------------

let private changeList (changes: Planning.PlannedChange list) =
    changes |> List.map (fun change -> "  " + Planning.describeChange change)

let change (verbose: bool) (checkMode: bool) (dryRun: bool) (report: ChangeReport) =
    let commandLabel = report.Command

    match report.Outcome with
    | AlreadyCurrent state ->
        match state with
        | Installed installed ->
            [ sprintf "SDE v%s already installed and verified. No changes needed." installed.Version ]
        | UpgradeRequired(installed, available) ->
            [ sprintf "SDE v%s is installed and verified." installed.Version
              sprintf "A newer version (v%s) is available. Run `%s upgrade` to move to it." available Packaging.executableName ]
        | AheadOfCli(installed, available) ->
            [ sprintf "Installed SDE v%s is newer than this CLI's packaged v%s. Nothing to do." installed.Version available ]
        | NotInstalled
        | Invalid _ -> [ sprintf "No changes needed." ]

    | Applied(changes, version) ->
        let headline =
            match changes with
            | Planning.InstallPayload _ :: _ -> sprintf "SDE v%s installed successfully." version
            | _ ->
                match report.StateBefore with
                | UpgradeRequired(installed, _) when installed.Version <> version ->
                    sprintf "SDE upgraded: v%s -> v%s." installed.Version version
                | NotInstalled
                | Installed _
                | UpgradeRequired _
                | AheadOfCli _
                | Invalid _ -> sprintf "SDE v%s installation brought up to date." version

        [ headline ] @ (if verbose then changeList changes else [])

    | ChangeOutcome.Planned changes ->
        let prefix =
            if checkMode then
                sprintf "%s --check: %d change(s) required." commandLabel changes.Length
            else
                sprintf "%s --dry-run: %d change(s) would be made. Nothing was written." commandLabel changes.Length

        [ prefix ] @ changeList changes

    | ChangeOutcome.Refused refusal ->
        let detail =
            match refusal with
            | Planning.LocalModifications(_, problems) -> problemLines verbose problems
            | Planning.NotADirectory _
            | Planning.NotWritable _
            | Planning.UnreadableInstallation _
            | Planning.WouldDowngrade _
            | Planning.NotInstalledYet
            | Planning.MigrationPrecondition _
            | Planning.SharedConflict _ -> []

        let advice =
            match refusal with
            | Planning.LocalModifications _ ->
                [ "Local modifications are never overwritten automatically."
                  sprintf "Run `%s doctor` for what each one means and how to resolve it." Packaging.executableName ]
            | Planning.UnreadableInstallation _ ->
                [ sprintf "Resolve this manually; %s refuses to act on an installation it cannot verify." commandLabel ]
            | Planning.NotADirectory _
            | Planning.NotWritable _
            | Planning.WouldDowngrade _
            | Planning.NotInstalledYet
            | Planning.MigrationPrecondition _
            | Planning.SharedConflict _ -> []

        [ Planning.describeRefusal refusal ] @ detail @ advice

    | Failed failure ->
        [ Execution.describeFailure failure
          sprintf "Run `%s status` to see the repository's current state." Packaging.executableName ]

// ---------------------------------------------------------------------------

let doctor (report: DoctorReport) =
    let lines =
        report.Diagnoses
        |> List.collect (fun diagnosis ->
            [ sprintf "%s %s  %s" (Diagnostics.describeSeverity diagnosis.Severity) diagnosis.Code diagnosis.Summary
              sprintf "  Cause:  %s" diagnosis.Cause ]
            @ (match diagnosis.Remedy with
               | Some remedy -> [ sprintf "  Remedy: %s" remedy ]
               | None -> [])
            @ [ "" ])

    let errors =
        report.Diagnoses |> List.filter (fun d -> d.Severity = Diagnostics.Severity.Error) |> List.length

    let warnings =
        report.Diagnoses |> List.filter (fun d -> d.Severity = Diagnostics.Severity.Warning) |> List.length

    [ "SDE doctor"; "" ] @ lines @ [ sprintf "%d error(s), %d warning(s)." errors warnings ]
