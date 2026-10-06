/// The callable lifecycle API.
///
/// Everything the CLI can do is a function here, taking plain values and
/// returning typed results. Nothing in this module reads argv, writes to a
/// console, or calls exit. That is the point: ROS, an integration assembly,
/// a test or a future service host can drive the same lifecycle without
/// simulating a command line, and the CLI is only ever an adapter over it.
module Sde.Core.Lifecycle

open Sde.Core.Inspection
open Sde.Core.Planning

/// Stable process exit codes.
///
/// COMPATIBILITY: 0, 1 and 2 keep exactly the meanings releases 1.0.0 to
/// 1.1.1 gave them, because CI configurations already branch on them. 1 is
/// deliberately left as the broad failure code rather than being split into
/// finer codes, since a script testing for `== 1` today must keep working.
/// New codes are only used for conditions that could not previously arise.
module ExitCodes =
    /// The command succeeded.
    let success = 0
    /// Verification failed, the installation is invalid, or the operation
    /// was refused. The broad failure code inherited from earlier releases.
    let failure = 1
    /// The arguments were not understood.
    let invalidArguments = 2
    /// The host platform has no packaged executable. Reported by the Node
    /// launcher before the F# CLI starts, so it can never be returned here.
    let unsupportedPlatform = 3
    /// The packaged executable is present but could not be started.
    /// Reported by the Node launcher, never by the F# CLI.
    let prerequisiteFailure = 4
    /// `--check` found that changes would be required. Distinct from
    /// `failure` so CI can tell drift apart from a broken installation.
    let changesRequired = 5
    /// `boundary assess` could not read or interpret an input document. An
    /// assessment that found High risk is still a success (0): gating on
    /// the assessment is the consumer's decision.
    let invalidInput = 6

/// The execution package carried by this CLI.
type Payload =
    { Directory: string
      Version: string
      MethodVersion: string option
      SourceRevision: string option
      FileCount: int }

/// Reads the bundled execution package. A broken payload is a broken
/// release, not a problem with the repository being operated on, and is
/// reported that way.
let readPayload (payloadDir: string) : Result<Payload, string> =
    match Manifest.read payloadDir with
    | Error detail ->
        Error(
            sprintf
                "the bundled SDE execution package at %s is invalid (%s). This indicates a broken %s release, not a problem with the target project."
                payloadDir
                detail
                Packaging.packageName
        )
    | Ok manifest ->
        Ok
            { Directory = payloadDir
              Version = manifest.SdeVersion
              MethodVersion = manifest.MethodVersion
              SourceRevision = manifest.SourceRevision
              FileCount = manifest.Files.Length }

// ---------------------------------------------------------------------------
// Inspection
// ---------------------------------------------------------------------------

let inspectRepository (projectRoot: string) (payload: Payload) : Repository =
    Inspection.inspectRepository projectRoot payload.Version

let getInstallationState (projectRoot: string) (payload: Payload) : InstallationState =
    Inspection.determineState projectRoot payload.Version

// ---------------------------------------------------------------------------
// status
// ---------------------------------------------------------------------------

type StatusReport =
    { Tool: string
      Package: string
      /// The released version of this CLI, which is also the version of the
      /// execution package it carries.
      CliVersion: string
      PayloadVersion: string
      MethodVersion: string option
      Installed: InstalledVersion option
      State: InstallationState
      ConfigurationSource: string
      ConfigurationValid: bool
      Problems: InstallationProblem list
      UpgradeAvailable: string option
      Verified: bool }

/// Read-only by construction: it calls inspection and nothing else.
let getStatus (projectRoot: string) (payload: Payload) : StatusReport =
    let repository = inspectRepository projectRoot payload

    let installed =
        match repository.State with
        | NotInstalled -> None
        | Installed i
        | UpgradeRequired(i, _)
        | AheadOfCli(i, _) -> Some i
        | Invalid(i, _) -> i

    let problems =
        match repository.State with
        | Invalid(_, problems) -> problems
        | NotInstalled
        | Installed _
        | UpgradeRequired _
        | AheadOfCli _ -> []

    let upgradeAvailable =
        match repository.State with
        | UpgradeRequired(_, available) -> Some available
        | NotInstalled
        | Installed _
        | AheadOfCli _
        | Invalid _ -> None

    // An installation that is merely behind, or ahead, is still intact: it
    // verifies, it just is not the version this CLI carries.
    let verified =
        match repository.State with
        | Installed _
        | UpgradeRequired _
        | AheadOfCli _ -> true
        | NotInstalled
        | Invalid _ -> false

    { Tool = InstallationRecord.toolName
      Package = Packaging.packageName
      CliVersion = payload.Version
      PayloadVersion = payload.Version
      MethodVersion = payload.MethodVersion
      Installed = installed
      State = repository.State
      ConfigurationSource =
        match repository.StructuralConfig with
        | Ok config -> config.Source
        | Error _ -> Ownership.structuralConfigName
      ConfigurationValid =
        match repository.StructuralConfig with
        | Ok _ -> true
        | Error _ -> false
      Problems = problems
      UpgradeAvailable = upgradeAvailable
      Verified = verified }

/// Exit 0 only when an installation is present and intact. Unchanged from
/// earlier releases: "not installed" and "modified" are both non-zero.
let statusExitCode (report: StatusReport) =
    if report.Installed.IsSome && report.Verified then
        ExitCodes.success
    else
        ExitCodes.failure

// ---------------------------------------------------------------------------
// verify
// ---------------------------------------------------------------------------

/// Which conditions `verify` treats as failures.
type VerifyMode =
    /// The default: installation integrity fails; structural findings and an
    /// intact installation at another version are review signals.
    | Lenient
    /// Everything `Lenient` treats as a review signal fails: SDE-STRUCT-001
    /// findings, and an installation not at the current configuration version.
    | Strict
    /// The adoption gate. Every installation-integrity condition fails closed,
    /// including version and toolchain-pin agreement; SDE-STRUCT-001 findings
    /// are reported as review signals and never fail.
    | IntegrityOnly

/// What kind of failure a verification failure is. Automation gates on this
/// rather than parsing failure prose.
type FailureCategory =
    /// The installation cannot be trusted: absent, damaged, at the wrong
    /// version, pinned to another release, or configured unusably.
    | IntegrityFailure
    /// A source-size review signal that strict mode promotes to a failure.
    | StructuralReviewFailure

/// One reason a verification did not pass, as a closed set.
type VerifyFailure =
    | InstallationMissing
    /// Invalid, but with no specific problem recorded. Never passes.
    | InstallationUnverifiable
    | InstallationDamaged of InstallationProblem
    | StructuralConfigurationUnusable of detail: string
    | ConfigurationVersionBehind of installed: int * expected: int
    | VersionMismatch of installed: string * cli: string
    | ToolchainPinMissing of expected: string
    | ToolchainPinMismatch of pinned: string * expected: string
    | ToolchainManifestUnrecognised
    | StructuralFindings of count: int

let failureCategory =
    function
    | StructuralFindings _ -> StructuralReviewFailure
    | InstallationMissing
    | InstallationUnverifiable
    | InstallationDamaged _
    | StructuralConfigurationUnusable _
    | ConfigurationVersionBehind _
    | VersionMismatch _
    | ToolchainPinMissing _
    | ToolchainPinMismatch _
    | ToolchainManifestUnrecognised -> IntegrityFailure

/// A stable machine-readable code for each failure.
let failureCode =
    function
    | InstallationMissing -> "not-installed"
    | InstallationUnverifiable -> "installation-invalid"
    | InstallationDamaged problem -> problemToken problem
    | StructuralConfigurationUnusable _ -> "structural-configuration-unusable"
    | ConfigurationVersionBehind _ -> "configuration-version-behind"
    | VersionMismatch _ -> "version-mismatch"
    | ToolchainPinMissing _ -> "toolchain-pin-missing"
    | ToolchainPinMismatch _ -> "toolchain-pin-mismatch"
    | ToolchainManifestUnrecognised -> "toolchain-manifest-unrecognised"
    | StructuralFindings _ -> StructuralReview.findingCode

let describeFailure =
    function
    | InstallationMissing -> sprintf "no %s/ installation found" Ownership.installRootName
    | InstallationUnverifiable -> "the installation is invalid"
    | InstallationDamaged problem -> describeProblem problem
    | StructuralConfigurationUnusable detail -> detail
    | ConfigurationVersionBehind(installed, expected) ->
        sprintf
            "installation is at configuration version %d; this release expects %d (run `%s upgrade`)"
            installed
            expected
            Packaging.executableName
    | VersionMismatch(installed, cli) ->
        sprintf
            "installed SDE v%s does not match this CLI's packaged v%s (run the pinned CLI, or `%s upgrade`)"
            installed
            cli
            Packaging.executableName
    | ToolchainPinMissing expected ->
        sprintf "%s does not pin %s; expected \"%s\"" Ownership.toolchainManifestName ToolchainPin.key expected
    | ToolchainPinMismatch(pinned, expected) ->
        sprintf
            "%s pins %s \"%s\" but the installation is v%s"
            Ownership.toolchainManifestName
            ToolchainPin.key
            pinned
            expected
    | ToolchainManifestUnrecognised ->
        sprintf "%s is not a JSON object; its %s pin cannot be checked" Ownership.toolchainManifestName ToolchainPin.key
    | StructuralFindings count ->
        sprintf "%d SDE-STRUCT-001 finding(s) (strict mode treats structural findings as failures)" count

type VerifyReport =
    { State: InstallationState
      InstalledVersion: string option
      ManagedFileCount: int
      Problems: InstallationProblem list
      Structural: StructuralReview.Report option
      StructuralError: string option
      Mode: VerifyMode
      /// True only in `Strict` mode. Retained for released consumers.
      Strict: bool
      /// The failures only the selected non-default mode produces, as prose.
      /// Retained for released consumers; `Failures` is the typed form.
      StrictFailures: string list
      /// Every reason the verification did not pass, in a stable order.
      Failures: VerifyFailure list
      /// SDE-STRUCT-001 findings reported without failing the verification.
      /// Empty in `Strict` mode, where the same findings are failures.
      ReviewSignals: StructuralReview.Finding list
      Passed: bool }

/// The toolchain pin, compared against the installed release. Pure.
let private pinFailures (version: string) (toolchain: string option) =
    match toolchain with
    | None -> [ ToolchainPinMissing version ]
    | Some text ->
        match Json.parse text with
        | Error _ -> [ ToolchainManifestUnrecognised ]
        | Ok(Json.JObject _ as document) ->
            match Json.tryField ToolchainPin.key document with
            | None -> [ ToolchainPinMissing version ]
            | Some(Json.JString pinned) when pinned = version -> []
            | Some(Json.JString pinned) -> [ ToolchainPinMismatch(pinned, version) ]
            | Some _ -> [ ToolchainManifestUnrecognised ]
        | Ok _ -> [ ToolchainManifestUnrecognised ]

/// Validates that the capability is correctly installed. Never mutates.
///
/// The mode decides only which observed conditions fail; every mode observes
/// the same things. See `VerifyMode`.
let verifyIn (mode: VerifyMode) (projectRoot: string) (payload: Payload) : VerifyReport =
    let repository = inspectRepository projectRoot payload

    let installed =
        match repository.State with
        | NotInstalled -> None
        | Installed i
        | UpgradeRequired(i, _)
        | AheadOfCli(i, _) -> Some i
        | Invalid(i, _) -> i

    let problems =
        match repository.State with
        | NotInstalled -> []
        | Invalid(_, problems) -> problems
        | Installed _
        | UpgradeRequired _
        | AheadOfCli _ -> []

    let integrityOk =
        match repository.State with
        | Installed _
        | UpgradeRequired _
        | AheadOfCli _ -> true
        | NotInstalled
        | Invalid _ -> false

    // The structural review runs only over an installation that is intact.
    // Reporting source-size findings for a repository whose methodology
    // installation is broken would bury the actual failure.
    let structural, structuralError =
        if not integrityOk then
            None, None
        else
            match repository.StructuralConfig with
            | Error detail -> None, Some detail
            | Ok config ->
                match StructuralReview.inspect projectRoot config with
                | Error detail -> None, Some(sprintf "Structural source inspection failed: %s" detail)
                | Ok report -> Some report, None

    let findings =
        structural |> Option.map (fun report -> report.Findings) |> Option.defaultValue []

    // Failures every mode reports: the installation itself is unusable.
    let baseFailures =
        [ match repository.State with
          | NotInstalled -> InstallationMissing
          | Invalid(_, []) -> InstallationUnverifiable
          | Invalid(_, problems) -> yield! problems |> List.map InstallationDamaged
          | Installed _
          | UpgradeRequired _
          | AheadOfCli _ -> ()

          match structuralError with
          | Some detail -> StructuralConfigurationUnusable detail
          | None -> () ]

    let configurationBehind =
        [ match installed with
          | Some i when i.ConfigurationVersion < InstallationRecord.currentConfigurationVersion ->
              ConfigurationVersionBehind(i.ConfigurationVersion, InstallationRecord.currentConfigurationVersion)
          | Some _
          | None -> () ]

    let structuralFailure =
        if findings.IsEmpty then [] else [ StructuralFindings findings.Length ]

    // Version and pin agreement are only meaningful over an intact
    // installation; a damaged one has already failed with its own reasons.
    let agreementFailures =
        match repository.State, installed with
        | (Installed _ | UpgradeRequired _ | AheadOfCli _), Some i ->
            (if i.Version <> payload.Version then [ VersionMismatch(i.Version, payload.Version) ] else [])
            @ pinFailures i.Version repository.Toolchain
        | _ -> []

    let modeFailures =
        match mode with
        | Lenient -> []
        | Strict -> structuralFailure @ configurationBehind
        | IntegrityOnly -> configurationBehind @ agreementFailures

    let failures = baseFailures @ modeFailures

    { State = repository.State
      InstalledVersion = installed |> Option.map (fun i -> i.Version)
      ManagedFileCount = installed |> Option.map (fun i -> i.ManagedFileCount) |> Option.defaultValue 0
      Problems = problems
      Structural = structural
      StructuralError = structuralError
      Mode = mode
      Strict = (mode = Strict)
      StrictFailures = modeFailures |> List.map describeFailure
      Failures = failures
      ReviewSignals =
        match mode with
        | Strict -> []
        | Lenient
        | IntegrityOnly -> findings
      Passed = List.isEmpty failures }

/// The released entry point: `strict` selects `Strict`, otherwise `Lenient`.
let verify (projectRoot: string) (payload: Payload) (strict: bool) : VerifyReport =
    verifyIn (if strict then Strict else Lenient) projectRoot payload

let verifyExitCode (report: VerifyReport) =
    if report.Passed then ExitCodes.success else ExitCodes.failure

// ---------------------------------------------------------------------------
// init and upgrade
// ---------------------------------------------------------------------------

/// What actually happened, as opposed to what was planned.
type ChangeOutcome =
    /// The repository was already in the desired state. No file was opened
    /// for writing, so a second run cannot perturb mtimes or content.
    | AlreadyCurrent of InstallationState
    /// Changes were planned and applied.
    | Applied of PlannedChange list * resultingVersion: string
    /// Changes were planned and deliberately not applied (dry run or check).
    | Planned of PlannedChange list
    | Refused of PlanRefusal
    | Failed of Execution.ExecutionFailure

type ChangeReport =
    { Command: string
      Outcome: ChangeOutcome
      StateBefore: InstallationState
      PayloadVersion: string }

let createInitializationPlan (projectRoot: string) (payload: Payload) : PlanOutcome =
    planInit (inspectRepository projectRoot payload) payload.Version payload.FileCount

let createUpgradePlan (projectRoot: string) (payload: Payload) : PlanOutcome =
    planUpgrade (inspectRepository projectRoot payload) payload.Version payload.FileCount

let private execute (command: string) (projectRoot: string) (payload: Payload) (dryRun: bool) (outcome: PlanOutcome) =
    let stateBefore = getInstallationState projectRoot payload

    let result =
        match outcome with
        | PlanOutcome.Refused refusal -> ChangeOutcome.Refused refusal
        | PlanOutcome.NoChangesNeeded state -> AlreadyCurrent state
        | PlanOutcome.Changes plan ->
            if dryRun then
                Planned plan.Changes
            else
                match Execution.apply projectRoot payload.Directory plan with
                | Error failure -> Failed failure
                | Ok() -> Applied(plan.Changes, payload.Version)

    { Command = command
      Outcome = result
      StateBefore = stateBefore
      PayloadVersion = payload.Version }

/// Brings the repository into a valid installed state. Idempotent: the
/// second and every later run over an intact, current installation returns
/// AlreadyCurrent without writing anything.
let initialize (projectRoot: string) (payload: Payload) (dryRun: bool) : ChangeReport =
    createInitializationPlan projectRoot payload
    |> execute "init" projectRoot payload dryRun

/// Moves an existing installation to this CLI's version and configuration,
/// running each required migration in order.
let performUpgrade (projectRoot: string) (payload: Payload) (dryRun: bool) : ChangeReport =
    createUpgradePlan projectRoot payload
    |> execute "upgrade" projectRoot payload dryRun

/// `--check` asks a different question from `--dry-run`: not "what would you
/// do" but "is anything required". Drift gets its own exit code so CI can
/// distinguish it from a broken installation.
let changeExitCode (checkMode: bool) (report: ChangeReport) =
    match report.Outcome with
    | AlreadyCurrent _ -> ExitCodes.success
    | Applied _ -> ExitCodes.success
    | Planned _ -> if checkMode then ExitCodes.changesRequired else ExitCodes.success
    | ChangeOutcome.Refused _ -> ExitCodes.failure
    | Failed _ -> ExitCodes.failure

// ---------------------------------------------------------------------------
// doctor
// ---------------------------------------------------------------------------

type DoctorReport =
    { Diagnoses: Diagnostics.Diagnosis list
      State: InstallationState
      PayloadVersion: string }

let diagnose (projectRoot: string) (payload: Payload) : DoctorReport =
    let repository = inspectRepository projectRoot payload

    { Diagnoses = Diagnostics.diagnose repository payload.Version
      State = repository.State
      PayloadVersion = payload.Version }

/// Errors fail the command; warnings and information do not. `doctor` is a
/// diagnostic, so it must be runnable on a healthy repository in CI without
/// failing the build for an advisory.
let doctorExitCode (report: DoctorReport) =
    if Diagnostics.hasErrors report.Diagnoses then
        ExitCodes.failure
    else
        ExitCodes.success
