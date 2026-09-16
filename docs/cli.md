# `sde` command reference

The canonical public interface of `@echelon-foundry/sde`.

```
npx @echelon-foundry/sde init
npx @echelon-foundry/sde status
npx @echelon-foundry/sde verify
npx @echelon-foundry/sde upgrade
npx @echelon-foundry/sde doctor

npx @echelon-foundry/sde --help
npx @echelon-foundry/sde --version
```

This document and the CLI's own `--help` output are kept in agreement; a test
asserts that every option documented in help is one the parser accepts.

---

## Global options

| Option | Meaning |
|---|---|
| `--help`, `-h` | Show help. `sde <command> --help` shows one command's help. |
| `--version`, `-V` | Print the package version and exit. |
| `--json` | Emit one JSON document on stdout and nothing else. |
| `--verbose`, `-v` | Include additional detail: full file lists, planned changes. |

Options are accepted before or after the command name: `sde --json status` and
`sde status --json` are equivalent.

Running `sde` with no command is a usage error (exit `2`), not a silent
success — an automation that loses its argument should fail rather than appear
to work.

---

## `init`

Bring the current repository into a valid installed state for SDE.

```
sde init [--dry-run | --check] [--json] [--verbose]
```

**Side effects.** Creates `.sde/` containing the versioned execution package
(tool-owned) and `.echelon/sde.json` recording what is installed (generated).

**It will not.** Overwrite a modified `.sde/`; upgrade an older installation
(that is `upgrade`); downgrade a newer one; create, modify or delete
`sde.config.json`; or touch any other file in `.echelon/`.

**Idempotent.** A second run over an intact, current installation opens no
file for writing.

| Option | Meaning |
|---|---|
| `--dry-run` | Calculate and report the full plan; change nothing. |
| `--check` | As `--dry-run`, but exit `5` if any change is required. |

```bash
sde init
sde init --dry-run --json
sde init --check          # fails CI when the installation has drifted
```

---

## `status`

Report what is installed. **Read-only.**

```
sde status [--json] [--verbose]
```

Reports the tool and package name, this CLI's version, the installed version,
the configuration version, the method version, the canonical source revision
recorded at build time, integrity, and whether an upgrade is available.

| Exit | Meaning |
|---|---|
| `0` | Installed and intact |
| `1` | Not installed, unreadable, or modified |

---

## `verify`

Validate that the capability is correctly installed. **Read-only.**

```
sde verify [--strict] [--json] [--verbose]
```

Checks, in order:

1. every file the manifest declares still hashes to its recorded SHA-256;
2. no declared file is missing;
3. no undeclared file is present under `.sde/`;
4. `VERSION` and `MANIFEST.json` agree about the installed version;
5. `sde.config.json`, if present, is valid;
6. the portable `SDE-STRUCT-001` source-size review.

By default structural findings are review signals and do not change a
successful exit code.

**`--strict`** has one meaning, stated once: everything the default mode
treats as a review signal becomes a failure. Concretely:

- `SDE-STRUCT-001` findings fail;
- an installation still at an older configuration version fails.

| Exit | Meaning |
|---|---|
| `0` | Valid |
| `1` | Invalid, or not installed |

---

## `upgrade`

Move an existing installation to the version this release carries.

```
sde upgrade [--dry-run | --check] [--json] [--verbose]
```

`sde update` is a **legacy alias** for this command, retained because it was
the released name through 1.1.1.

Upgrading is a sequence of explicit transitions, each with preconditions that
are all checked before anything is written. See
[upgrading.md](upgrading.md) for the migration model.

| Option | Meaning |
|---|---|
| `--dry-run` | Calculate and report the full plan; change nothing. |
| `--check` | As `--dry-run`, but exit `5` if any change is required. |

---

## `doctor`

Diagnose problems and explain how to fix them. **Read-only.**

```
sde doctor [--json]
```

Findings are graded `ERROR`, `WARNING` or `INFORMATION`. Only an `ERROR`
fails the command, so `doctor` can be run in CI on a healthy repository.

Each finding carries a stable code, a cause, and — for every error — a remedy.

| Code | Severity | Condition |
|---|---|---|
| `SDE-DOCTOR-001` | ERROR | The installation record cannot be read |
| `SDE-DOCTOR-002` | ERROR | A managed file has been modified locally |
| `SDE-DOCTOR-003` | ERROR | A declared managed file is missing |
| `SDE-DOCTOR-004` | ERROR | An undeclared file is present under `.sde/` |
| `SDE-DOCTOR-005` | ERROR | `VERSION` and `MANIFEST.json` disagree |
| `SDE-DOCTOR-006` | ERROR | The installed version is not orderable |
| `SDE-DOCTOR-007` | ERROR | `.echelon/sde.json` is not a valid record |
| `SDE-DOCTOR-008` | WARNING | `.echelon/sde.json` is out of date |
| `SDE-DOCTOR-009` | ERROR | A shared file is present but unusable |
| `SDE-DOCTOR-100` | INFORMATION | Nothing is installed here |
| `SDE-DOCTOR-101` | INFORMATION | The installation is intact |
| `SDE-DOCTOR-102` | INFORMATION | An upgrade is available |
| `SDE-DOCTOR-103` | WARNING | The installation is newer than this CLI |
| `SDE-DOCTOR-110` | INFORMATION | Structural review is configured by `sde.config.json` |
| `SDE-DOCTOR-120` | INFORMATION | Other Echelon tools have records in `.echelon/` |

Codes are stable across releases; the prose may be reworded.

---

## Exit codes

| Code | Meaning | Produced by |
|---|---|---|
| `0` | Success | any command |
| `1` | Verification failed, installation invalid, or operation refused | any command |
| `2` | Arguments were not understood | argument parsing |
| `3` | No packaged executable for this platform | the Node launcher |
| `4` | The packaged executable could not be started | the Node launcher |
| `5` | `--check` found that changes are required | `--check` only |

`0`, `1` and `2` keep exactly the meanings releases 1.0.0–1.1.1 gave them, so
existing CI configurations that branch on them keep working. `1` is
deliberately left as the broad failure code rather than split into finer
codes. `3` and `4` describe conditions that could not previously arise,
because the tool used to run wherever Node ran.

---

## JSON output

With `--json`, stdout is exactly one JSON document and nothing else. No
decorative text is mixed in and nothing is written to stderr.

Every document carries:

| Field | Meaning |
|---|---|
| `schemaVersion` | The output schema version (currently `1`) |
| `command` | `init`, `status`, `verify`, `upgrade`, `doctor` or `version` |
| `tool` | `sde` |
| `package` | `@echelon-foundry/sde` |
| `exitCode` | Equal to the process exit code |

**Schema stability.** Fields are added without bumping `schemaVersion`; a
field that is removed or changes meaning requires a bump. Consumers should
ignore unknown fields.

### `status --json`

| Field | Type | Meaning |
|---|---|---|
| `cliVersion`, `payloadVersion` | string | This release's version |
| `methodVersion` | string \| null | The construction method's version |
| `state` | string | `not-installed`, `installed`, `upgrade-required`, `ahead-of-cli`, `invalid` |
| `installed` | object \| null | `version`, `configurationVersion`, `methodVersion`, `sourceRevision`, `managedFileCount` |
| `configuration` | object | `source`, `valid` |
| `verified` | boolean | Whether the installation is intact |
| `upgradeAvailable` | string \| null | The version an upgrade would move to |
| `problems` | array | Each with `code`, `path`, `ownership`, `message` |

### `verify --json`

| Field | Type | Meaning |
|---|---|---|
| `state` | string | As above |
| `installedVersion` | string \| null | |
| `managedFileCount` | number | |
| `strict` | boolean | Whether `--strict` was given |
| `passed` | boolean | The verification result |
| `problems` | array | As above |
| `strictFailures` | array of string | Failures that only strict mode produces |
| `structural` | object | `ran`, `error`, `enabled`, `configurationSource`, `inspectedFiles`, `findings` |

Each structural finding has `code`, `band`, `path` and `lineCount`. Bands are
`review`, `strong-review`, `justification-required`, `conformance-concern`.

### `init --json` and `upgrade --json`

| Field | Type | Meaning |
|---|---|---|
| `dryRun`, `check` | boolean | Which mode was requested |
| `stateBefore` | string | Installation state before acting |
| `outcome` | string | `no-changes-needed`, `applied`, `planned`, `refused`, `failed` |
| `changed` | boolean | Whether anything was written |
| `changes` | array | Each with `change`, `message` and change-specific fields |
| `resultingVersion` | string \| null | Set only when `outcome` is `applied` |
| `refusal` | object \| null | `message` and `problems` |
| `failure` | string \| null | Set only when `outcome` is `failed` |

`change` is one of `install-payload`, `replace-payload`,
`write-installation-record`, `run-migration`.

### `doctor --json`

| Field | Type | Meaning |
|---|---|---|
| `diagnoses` | array | Each with `severity`, `code`, `summary`, `cause`, `remedy` |
| `errors`, `warnings` | number | Counts by severity |

---

## Problem codes

Reported in the `problems` array of `status --json` and `verify --json`.

| Code | Meaning |
|---|---|
| `manifest-unreadable` | The installation's own record could not be read |
| `managed-file-modified` | A managed file's content changed after installation |
| `managed-file-missing` | A declared file is not on disk |
| `unexpected-managed-file` | An undeclared file is present under `.sde/` |
| `version-file-mismatch` | `VERSION` and `MANIFEST.json` disagree |
| `unparsable-installed-version` | The installed version cannot be ordered |
| `installation-record-unreadable` | `.echelon/sde.json` is not valid |
| `installation-record-disagrees` | The record's version differs from the payload's |
| `shared-file-conflict` | A shared file is present but not usable |

---

## Environment variables

| Variable | Purpose |
|---|---|
| `SDE_PAYLOAD_DIR` | **Development and tests only.** Overrides where the CLI looks for the execution package it installs. Not part of the supported interface for consumers. |
