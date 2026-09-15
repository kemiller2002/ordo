/// CLI help text.
///
/// Help is public documentation. Every option listed here exists, every
/// stated side effect is the one the lifecycle actually has, and the wording
/// matches README.md and docs/cli.md. A change to a command's behaviour is
/// not finished until this file and those documents say the same thing.
module Sde.Cli.Help

open Sde.Core

let private commonOptions =
    [ "  --json          Emit a single JSON document on stdout and nothing else."
      "  --verbose       Include additional diagnostic detail."
      "  --help, -h      Show help. Use `sde <command> --help` for one command."
      "  --version, -V   Print the package version and exit." ]

let overview (version: string) =
    [ sprintf "%s %s" Packaging.packageName version
      ""
      "State-Directed Engineering repository initialization, verification,"
      "diagnostics, and upgrade tooling."
      ""
      Args.usage
      ""
      "Commands:"
      sprintf "  init        Bring this repository into a valid installed state (creates %s/)." Ownership.installRootName
      "  status      Report installed version, integrity and available upgrade. Read-only."
      "  verify      Validate that the capability is correctly installed. Read-only."
      "  upgrade     Move an existing installation to this release's version."
      "  doctor      Diagnose problems and explain how to fix them. Read-only."
      ""
      "Options:" ]
    @ commonOptions
    @ [ ""
        "Exit codes:"
        "  0  success"
        "  1  verification failed, installation invalid, or operation refused"
        "  2  arguments were not understood"
        "  3  no packaged executable for this platform"
        "  4  the packaged executable could not be started"
        "  5  --check found that changes are required"
        ""
        "Examples:"
        sprintf "  npx %s init" Packaging.packageName
        sprintf "  npx %s status --json" Packaging.packageName
        sprintf "  npx %s verify --strict" Packaging.packageName
        sprintf "  npx %s upgrade --dry-run" Packaging.packageName ]

let private initHelp =
    [ "sde init"
      ""
      "Bring the current repository into a valid installed state for SDE."
      ""
      "Safe and repeatable. Running it a second time over an intact, current"
      "installation makes no changes at all — no file is opened for writing,"
      "so content and timestamps are untouched."
      ""
      "Side effects:"
      sprintf "  creates %s/ containing the versioned execution package (tool-owned)" Ownership.installRootName
      sprintf "  creates %s recording what is installed (generated)" Ownership.installationRecordName
      ""
      "It will not:"
      sprintf "  overwrite a modified %s/ — it refuses and reports which files changed" Ownership.installRootName
      "  upgrade an older installation — that is `sde upgrade`, a deliberate act"
      "  downgrade an installation newer than this release"
      sprintf "  create, modify or delete %s, which your repository owns" Ownership.structuralConfigName
      sprintf "  touch any other file in %s/, which other Echelon Foundry tools own" Ownership.echelonRootName
      ""
      "Options:"
      "  --dry-run       Calculate and report the full plan; change nothing."
      "  --check         As --dry-run, but exit 5 if any change is required."
      "  --json          Emit the plan or result as JSON."
      ""
      "Examples:"
      "  sde init"
      "  sde init --dry-run --json"
      "  sde init --check          # fails CI when the installation has drifted" ]

let private statusHelp =
    [ "sde status"
      ""
      "Report what is installed. Read-only: it never modifies the repository."
      ""
      "Reports the tool and package name, this CLI's version, the installed"
      "version, the configuration version, integrity, and whether an upgrade"
      "is available."
      ""
      "Options:"
      "  --json          Emit the report as JSON."
      "  --verbose       List every modified, missing and unexpected file."
      ""
      "Exit codes:"
      "  0  installed and intact"
      "  1  not installed, unreadable, or modified"
      ""
      "Examples:"
      "  sde status"
      "  sde status --json" ]

let private verifyHelp =
    [ "sde verify"
      ""
      "Validate that the capability is correctly installed. Read-only."
      ""
      "Recomputes a SHA-256 for every file the manifest declares and compares"
      "it to the recorded hash; detects declared files that are missing and"
      "undeclared files that are present; checks that VERSION and"
      "MANIFEST.json agree; then runs the portable SDE-STRUCT-001 source-size"
      "review."
      ""
      "By default structural findings are review signals and do not change a"
      "successful exit code, because file size is a signal, not proof of"
      "nonconformance."
      ""
      "Options:"
      "  --strict        Treat structural findings as failures, and require the"
      "                  installation to be at the current configuration version."
      "  --json          Emit the report as JSON."
      "  --verbose       List every finding and problem in full."
      ""
      "Exit codes:"
      "  0  valid"
      "  1  invalid, or not installed"
      ""
      "Examples:"
      "  sde verify"
      "  sde verify --strict --json" ]

let private upgradeHelp =
    [ "sde upgrade"
      ""
      "Move an existing installation to the version this release carries."
      ""
      "Upgrading is a sequence of explicit version and configuration"
      "transitions, each with its own preconditions. Every precondition is"
      "checked before anything is written, so a migration that cannot run"
      "stops the upgrade before it starts rather than half way through."
      ""
      "Guarantees:"
      "  a locally modified installation is never overwritten — upgrade refuses"
      "  and names the files that changed"
      "  an installation newer than this release is never downgraded"
      sprintf "  files your repository owns, including %s, are never touched" Ownership.structuralConfigName
      "  a failure reports exactly which changes had already been applied"
      ""
      "Options:"
      "  --dry-run       Calculate and report the full plan; change nothing."
      "  --check         As --dry-run, but exit 5 if any change is required."
      "  --json          Emit the plan or result as JSON."
      ""
      "`sde update` is accepted as a legacy alias for this command."
      ""
      "Examples:"
      "  sde upgrade"
      "  sde upgrade --dry-run --json" ]

let private doctorHelp =
    [ "sde doctor"
      ""
      "Diagnose problems and explain how to fix them. Read-only."
      ""
      "Where `verify` answers whether the installation is valid, `doctor`"
      "explains why it is not and what to do about it. Findings are graded"
      "ERROR, WARNING or INFORMATION; only an ERROR fails the command, so"
      "doctor can be run in CI on a healthy repository."
      ""
      "Each finding carries a stable code (SDE-DOCTOR-nnn) to branch on, a"
      "cause, and a remedy where one exists."
      ""
      "Options:"
      "  --json          Emit all findings as JSON."
      ""
      "Exit codes:"
      "  0  no errors (warnings and information do not fail)"
      "  1  at least one error"
      ""
      "Examples:"
      "  sde doctor"
      "  sde doctor --json" ]

let forCommand (name: string) (version: string) =
    match name with
    | "init" -> initHelp
    | "status" -> statusHelp
    | "verify" -> verifyHelp
    | "upgrade"
    | "update" -> upgradeHelp
    | "doctor" -> doctorHelp
    | _ -> overview version
