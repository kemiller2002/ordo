# @echelon-foundry/sde

Echelon Foundry State-Directed Engineering repository initialization,
verification, diagnostics, and upgrade tooling.

It installs a small, versioned, agent-readable **SDE execution package**
(`.sde/`) into any software repository — the subset of State-Directed
Engineering doctrine and method a project needs to *apply* SDE — and then
keeps that installation honest: it can tell you what is installed, prove the
installation is intact, explain what is wrong when it is not, and move you to
a newer release without destroying local work.

## Quick start

```bash
# Bring this repository into a valid installed state
npx @echelon-foundry/sde init

# Confirm what is installed
npx @echelon-foundry/sde status

# Validate the installation
npx @echelon-foundry/sde verify
```

That is the whole first run. `init` is safe to run again at any time.

## Commands

| Command | What it does | Writes? |
|---|---|---|
| `init` | Bring the repository into a valid installed state | yes, on first run |
| `status` | Report installed version, integrity and available upgrade | no |
| `verify` | Validate that the capability is correctly installed | no |
| `upgrade` | Move an existing installation to this release's version | yes |
| `doctor` | Diagnose problems and explain how to fix them | no |

```bash
npx @echelon-foundry/sde init
npx @echelon-foundry/sde status
npx @echelon-foundry/sde verify
npx @echelon-foundry/sde upgrade
npx @echelon-foundry/sde doctor

npx @echelon-foundry/sde --help
npx @echelon-foundry/sde --version
```

`sde update` is accepted as a **legacy alias** for `sde upgrade`, so scripts
written against releases 1.0.0–1.1.1 keep working.

If you install the package rather than using `npx`, the executable is named
`sde`:

```bash
npm install --save-dev @echelon-foundry/sde
npx sde init
```

Full command reference:
[docs/cli.md](https://github.com/kemiller2002/ordo/blob/main/docs/cli.md).

## Options

| Option | Applies to | Meaning |
|---|---|---|
| `--help`, `-h` | all | Show help. `sde <command> --help` for one command. |
| `--version`, `-V` | all | Print the package version. |
| `--json` | all | Emit one JSON document on stdout and nothing else. |
| `--verbose`, `-v` | all | Include additional detail (full file lists, planned changes). |
| `--dry-run` | `init`, `upgrade` | Calculate and report the full plan; change nothing. |
| `--check` | `init`, `upgrade` | As `--dry-run`, but exit `5` if any change is required. |
| `--strict` | `verify` | Treat review signals as failures. |

## Supported platforms

The package ships a self-contained executable for each platform, so a
consuming repository needs **no .NET runtime** and no global install.

| Platform | Support |
|---|---|
| Windows x64 | native executable |
| Windows ARM64 | runs the x64 executable under Windows emulation |
| Linux x64 (glibc) | native executable |
| Linux x64 (musl, e.g. Alpine) | native executable |
| Linux ARM64 | native executable |
| macOS x64 | native executable |
| macOS ARM64 | native executable |

Any other platform fails immediately with exit code `3` and a message naming
what is supported. Nothing is downloaded at install time or at run time.

**Prerequisites:** Node.js 18 or newer, which `npx` already requires. Nothing
else.

## What gets installed

```
.sde/
├── README.md               orientation for humans and agents
├── VERSION                 the installed SDE package version
├── MANIFEST.json           version, source revision, and a sha256 per file
├── method/                 CONSTRUCTION-METHOD, CHANGE-CLASSIFICATION,
│                           NAVIGATION-AND-CONTEXT, FEATURE-MANIFESTS,
│                           VERIFICATION-METHOD, AGENT-EXECUTION-RULES
├── architecture/           FOUR-TIER-ARCHITECTURE, BOUNDARY-PRESERVATION,
│                           STRUCTURAL-LOCALITY
├── reference/              GLOSSARY, ENGINEERING-METRICS
└── templates/              work-item, execution-log, completion-report,
                            repository-semantic-map, feature-manifest

.echelon/
└── sde.json                the installation record (see below)
```

Deliberately **not** installed: `research/`, evidence records, experiment
reports, research journals and internal methodology-maintenance material. The
canonical repository remains the source for provenance; this package exists to
apply SDE, not to prove it.

## Installation manifest

`.echelon/` is the shared root every Echelon Foundry tool writes its own
record into. SDE owns exactly one file there and never touches another tool's:

```json
{
  "schemaVersion": 1,
  "tool": "sde",
  "package": "@echelon-foundry/sde",
  "installedVersion": "1.2.0",
  "configurationVersion": 2,
  "installRoot": ".sde",
  "managedArtifacts": [".sde", ".echelon/sde.json"]
}
```

It contains no timestamps, no absolute paths, no machine identifiers and no
credentials, so it is reproducible on any machine and safe to commit.

`.sde/MANIFEST.json` is the separate, per-file integrity record: a SHA-256 for
every installed file, plus the canonical git revision the package was built
from. Both are regenerated from the installed package; neither is hand-edited.

## File ownership

Every path this tool knows about carries an ownership classification, and that
classification — not the fact that `init` created the file — decides whether a
later `upgrade` may replace it.

| Class | Meaning | Example |
|---|---|---|
| **tool-owned** | Controlled by the tool. Replaced on upgrade, but only when the installed copy is unmodified; a local edit blocks the upgrade instead. | `.sde/method/*.md` |
| **generated** | Derived from authoritative inputs and safe to regenerate. | `.sde/MANIFEST.json`, `.sde/VERSION`, `.echelon/sde.json` |
| **user-owned** | Controlled by your repository. Never created, modified or deleted by this tool. | everything outside `.sde/` and `.echelon/sde.json` |
| **shared** | The tool defines the schema, your repository owns the values. Read and validated, never rewritten. | `sde.config.json` |

A **shared** file that is present but invalid is a conflict this tool reports
rather than resolves: `verify` fails, `doctor` explains which field is wrong,
and `init` refuses instead of overwriting values you chose.

## `init` semantics

`init` means *bring this repository into a valid installed state*. It does not
mean *copy some files*.

It is **safe, repeatable and idempotent**. Running it a second time over an
intact, current installation makes no changes at all — no file is opened for
writing, so content and modification times are untouched. This is verified by
an automated test that snapshots the repository before and after.

| Existing `.sde/` state | `init` behavior |
|---|---|
| none | installs, writes the installation record |
| current version, intact | reports "already installed and verified"; writes nothing |
| current version, missing only the installation record | writes the record; changes nothing else |
| current version, locally modified | **refuses**, names the changed files |
| older version, intact | reports that an upgrade is available; does **not** upgrade |
| older version, locally modified | **refuses**, names the changed files |
| newer than this release | reports it; refuses to downgrade |
| unreadable installation record | **refuses**; never acts on an installation it cannot verify |

`init` will never create, modify or delete `sde.config.json`, never touch
another tool's file in `.echelon/`, and never modify anything outside `.sde/`
and `.echelon/sde.json`.

Installation is staged: the new package is copied into a temporary sibling
directory and independently re-verified against its own manifest *before*
anything under `.sde/` is replaced, so a failure mid-install never leaves
`.sde/` partially written.

## `status`

Read-only. Reports the package name, this CLI's version, the installed
version, the configuration version, the method version, the canonical source
revision recorded at build time, integrity, and whether an upgrade is
available.

```
$ npx @echelon-foundry/sde status
SDE

Package:               @echelon-foundry/sde
CLI version:           1.2.0
Installed version:     1.2.0
Configuration:         version 2
Method version:        0.2
Source revision:       7844e6ae81f06dc34568ac54a4b55eded2d04bab
Managed artifacts:     18 files
Installation status:   installed
Verification:          passed
Upgrade:               none available
```

Exits `0` when installed and intact, non-zero otherwise.

## `verify`

Read-only, and designed for CI. It recomputes a SHA-256 for every file the
manifest declares, detects declared files that are missing and undeclared
files that are present, checks that `VERSION` and `MANIFEST.json` agree, then
runs the portable `SDE-STRUCT-001` source-size review.

By default structural findings are **review signals** and do not change a
successful exit code: file size is a signal, not proof of nonconformance.

`verify --strict` has one meaning: everything the default mode treats as a
review signal becomes a failure. Concretely, structural findings fail, and an
installation that has not adopted the current configuration version fails.

The default warning bands are 500 lines (review), 1,000 (strong review), above
2,000 (justification normally expected) and above 4,000 (normally a
conformance concern). Common dependency, build and generated-output
directories are skipped. Override with a project-owned `sde.config.json`:

```json
{
  "structuralReview": {
    "extensions": [".fs", ".ts", ".tsx"],
    "excludePaths": ["src/generated"],
    "thresholds": {
      "reviewAt": 500,
      "strongReviewAt": 1000,
      "justificationAbove": 2000,
      "conformanceConcernAbove": 4000
    }
  }
}
```

Set `structuralReview.enabled` to `false` only with a project-level reason.
Invalid configuration fails verification, because the requested check cannot
be trusted.

## `upgrade`

Upgrading is a sequence of explicit version and configuration transitions,
each with its own preconditions — never "delete the old files and copy the new
ones". Every precondition is checked **before anything is written**, so a
migration that cannot run stops the upgrade before it starts rather than half
way through.

Guarantees, each covered by a test:

- a locally modified installation is **never** overwritten; `upgrade` refuses
  and names the files that changed;
- an installation newer than this release is **never** downgraded;
- files your repository owns, including `sde.config.json` and any other
  Echelon tool's record in `.echelon/`, are never touched;
- a failure reports exactly which changes had already been applied.

Preview without changing anything:

```bash
npx @echelon-foundry/sde upgrade --dry-run
npx @echelon-foundry/sde upgrade --dry-run --json
```

Supported upgrade paths and migration behaviour:
[docs/upgrading.md](https://github.com/kemiller2002/ordo/blob/main/docs/upgrading.md).

## `doctor`

Where `verify` answers *whether* the installation is valid, `doctor` explains
*why* it is not and what to do about it. Findings are graded `ERROR`,
`WARNING` or `INFORMATION`; only an `ERROR` fails the command, so `doctor` can
run in CI on a healthy repository.

```
$ npx @echelon-foundry/sde doctor
SDE doctor

ERROR SDE-DOCTOR-003  .sde/reference/GLOSSARY.md is declared by the manifest but is not present.
  Cause:  The file was deleted after installation, or the installation was interrupted.
  Remedy: Remove .sde/ and run `sde init` to reinstall the execution package.

1 error(s), 0 warning(s).
```

Each finding carries a stable code (`SDE-DOCTOR-nnn`) to branch on, a cause
and, for every error, a remedy.

## CI usage

```yaml
- run: npx @echelon-foundry/sde verify --strict
```

`verify` exits `0` when valid and non-zero when not, and never modifies the
repository, so it is safe to run on any branch.

To fail a build when the installation has drifted from what this release would
install, without writing anything:

```yaml
- run: npx @echelon-foundry/sde init --check
```

`--check` exits `5` when changes are required and `0` when nothing is needed,
which distinguishes drift from a broken installation (`1`).

## Agent and non-interactive usage

Every command is non-interactive. Nothing prompts, nothing waits for input,
and no command asks for confirmation — a destructive action is refused
outright rather than confirmed, so there is no prompt to hang on.

For programmatic use:

```bash
npx @echelon-foundry/sde status --json
npx @echelon-foundry/sde verify --json
npx @echelon-foundry/sde doctor --json
npx @echelon-foundry/sde init --dry-run --json
npx @echelon-foundry/sde upgrade --dry-run --json
```

With `--json`, stdout is exactly one JSON document and nothing else — no
decorative text, no progress output, and nothing on stderr. Every document
carries `schemaVersion`, a `command`, and an `exitCode` field equal to the
process exit code. Fields may be added without a schema bump; consumers should
ignore unknown fields.

Combine `--dry-run --json` to obtain a full plan an agent can inspect before
deciding to act:

```json
{
  "schemaVersion": 1,
  "command": "upgrade",
  "tool": "sde",
  "package": "@echelon-foundry/sde",
  "payloadVersion": "1.2.0",
  "dryRun": true,
  "check": false,
  "stateBefore": "upgrade-required",
  "outcome": "planned",
  "changed": false,
  "changes": [
    {
      "change": "run-migration",
      "message": "run migration 0001-echelon-installation-record (configuration 1 -> 2): adopt the shared .echelon/ installation record",
      "id": "0001-echelon-installation-record",
      "fromConfiguration": 1,
      "toConfiguration": 2,
      "description": "adopt the shared .echelon/ installation record"
    },
    {
      "change": "write-installation-record",
      "message": "write .echelon/sde.json recording v1.2.0 at configuration version 2",
      "version": "1.2.0",
      "configurationVersion": 2
    }
  ],
  "resultingVersion": null,
  "refusal": null,
  "failure": null,
  "exitCode": 0
}
```

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Verification failed, installation invalid, or the operation was refused |
| `2` | Arguments were not understood |
| `3` | No packaged executable for this platform |
| `4` | The packaged executable could not be started |
| `5` | `--check` found that changes are required |

Codes `0`, `1` and `2` keep exactly the meanings releases 1.0.0–1.1.1 gave
them. `3` and `4` are produced by the Node launcher only. `5` is produced only
by `--check`.

## Compatibility

This release reimplements the tool in F# without changing what consumers
depend on:

- the package name, the `sde` executable name and the command set are
  unchanged, and `update` still works as an alias for `upgrade`;
- `.sde/MANIFEST.json` remains schema version 1, byte-for-byte compatible —
  the built execution package is identical to what the previous
  implementation produced from the same sources;
- `sde.config.json`, its defaults, its validation rules and the
  `SDE-STRUCT-001` bands are unchanged;
- exit codes `0`, `1` and `2` are unchanged;
- installations from 1.0.0–1.1.1 continue to pass `verify` with no action, and
  are migrated in place, additively, when you choose to run `upgrade`;
- `bin/sde.mjs` remains a working entry point for callers that invoke it by
  path.

The one behavioural change: the tool now runs as a native executable rather
than as JavaScript, so platforms outside the supported list fail with exit
code `3` instead of running. See
[docs/upgrading.md](https://github.com/kemiller2002/ordo/blob/main/docs/upgrading.md)
for the full compatibility boundary.

## Version pinning

A project's `.sde/` installation is pinned to whatever version `init` or the
last `upgrade` installed. There is no background or automatic update of any
kind — no network polling, no telemetry, no downloads at run time. The
methodology only changes when someone deliberately runs `upgrade`.

Three version concepts are tracked independently:

- **package version** (`1.2.0`) — this CLI's own release, reported by
  `--version` and recorded as `sdeVersion`;
- **method version** (`MANIFEST.json` `methodVersion`, e.g. `0.2`) — the
  construction method's own version, read from its canonical document at build
  time. A release that fixes a tooling bug bumps the package version without
  bumping the method version;
- **configuration version** (`.echelon/sde.json` `configurationVersion`) — the
  shape of the installation itself, moved only by a migration.

A released package version is immutable: if canonical methodology changes
materially, that requires a new package version and a new build, not a silent
regeneration of an already-published version's contents.

## How this relates to ROS

| | ROS | SDE (`@echelon-foundry/sde`) |
|---|---|---|
| Manages | work items, research-artifact lifecycle | an engineering method to apply to changes |
| Lives in a project as | `.ros/` | `.sde/` |
| Required by the other? | No | No |

**SDE does not depend on ROS**, and this package never reads, writes or
imports anything under `.ros/`. A project may use `.sde/` with no `.ros/` at
all, or the reverse.

## Development, testing and releasing

This package is built from the
[`state-directed-engineering`](https://github.com/kemiller2002/ordo)
repository, where the lifecycle is implemented in F# under `src/Sde.Core` and
`src/Sde.Cli`. Node is only the launcher.

- [docs/development.md](https://github.com/kemiller2002/ordo/blob/main/docs/development.md)
  — building and testing from a checkout
- [docs/releasing.md](https://github.com/kemiller2002/ordo/blob/main/docs/releasing.md)
  — the release and publish process
- [docs/installation.md](https://github.com/kemiller2002/ordo/blob/main/docs/installation.md)
  — detailed installation behaviour
- [docs/cli.md](https://github.com/kemiller2002/ordo/blob/main/docs/cli.md)
  — full command reference
- [docs/upgrading.md](https://github.com/kemiller2002/ordo/blob/main/docs/upgrading.md)
  — migration behaviour and compatibility policy

`DISTRIBUTION-MAP.json`, shipped with the package, is the machine-readable
mapping from canonical source documents to installed files.

## Troubleshooting

**`Unsupported platform` / exit code 3.** The package ships executables for
the platforms listed above. There is no fallback, because a wrong-architecture
binary failing at run time is worse than a clear refusal.

**`This @echelon-foundry/sde installation is missing its <rid> executable`
/ exit code 4.** The installed package is incomplete — usually a partial or
interrupted `npm install`. Reinstall the package.

**`init` refuses with "local modifications".** Something under `.sde/` was
edited. Run `sde doctor` to see exactly which files and why it matters, then
restore them from version control. `.sde/` is not a place for project-specific
content; record an intentional deviation outside it.

**`verify` fails after a merge.** A merge conflict marker left inside a `.sde/`
file changes its hash. `sde doctor` names the file; restoring it from the
installed version resolves it.

**An installation newer than the CLI.** Run `npx @echelon-foundry/sde@latest`
rather than a pinned older version. Nothing is ever downgraded.

## Runtime footprint

Zero npm dependencies. After `.sde/` is installed, a consuming project's
runtime does not depend on this package at all: it is an engineering-time
input, not a production dependency, and is normally invoked through `npx`
rather than added to a project's own `package.json`.
