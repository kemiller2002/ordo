# Installing SDE in a repository

How `npx @echelon-foundry/sde init` behaves, in detail. For the command
reference see [cli.md](cli.md); for moving between versions see
[upgrading.md](upgrading.md).

## Prerequisites

Node.js 18 or newer, which `npx` already requires. Nothing else: the package
ships a self-contained executable for each supported platform, so no .NET
runtime and no global install are needed, and nothing is downloaded at install
time or at run time.

Supported platforms are Windows x64 (and ARM64 under emulation), Linux x64
(glibc and musl), Linux ARM64, macOS x64 and macOS ARM64. Any other platform
fails immediately with exit code `3`.

## Recommended: `npx`

```bash
npx @echelon-foundry/sde init
```

This is the canonical interface. It requires no entry in your project's
`package.json` and leaves no dependency behind: SDE is an engineering-time
input, not a production dependency.

## Supported: a development dependency

```bash
npm install --save-dev @echelon-foundry/sde
npx sde init
```

Use this when you want the version pinned in your lockfile, for example so
that every contributor and CI job runs the same release.

## Development-only: from a checkout

Contributors to SDE itself run the CLI from source. See
[development.md](development.md). This is not a supported way to install SDE
into an unrelated project.

## What `init` does, step by step

1. **Detect** the repository: the current working directory.
2. **Inspect** it — read `.sde/`, `.echelon/sde.json` and `sde.config.json` if
   present. Nothing is written during inspection.
3. **Determine the current installation state**: not installed, installed,
   upgrade required, ahead of this CLI, or invalid.
4. **Validate prerequisites**: the target must be a directory this process can
   write to.
5. **Calculate the changes** needed to reach the desired state.
6. **Detect conflicts**: local modifications, an unreadable installation
   record, an invalid shared file.
7. **Install**: stage the execution package into a temporary sibling
   directory, verify the staged copy against its own manifest, then swap it
   into place.
8. **Write the installation record** at `.echelon/sde.json`.
9. **Verify** the result and report it.

Steps 1–6 never write. If step 6 finds a conflict, nothing is written at all.

## What is created

```
.sde/                      the versioned execution package (tool-owned)
.sde/VERSION               the installed version (generated)
.sde/MANIFEST.json         a SHA-256 per installed file (generated)
.echelon/sde.json          the installation record (generated)
```

Nothing else is created, modified or deleted. In particular `init` does not
create `sde.config.json`: the built-in defaults apply when it is absent, and a
file your repository owns is yours to create when you want to change them.

## Repeated runs

`init` is idempotent. Over an intact, current installation it opens no file
for writing, so content and modification times are unchanged. An automated
test snapshots the repository before and after a second `init` and requires
the snapshots to be identical.

The full behaviour matrix is in
[the package README](../distribution/README.md#init-semantics).

## Conflicts

`init` refuses rather than guessing whenever acting could lose work:

| Conflict | Behaviour |
|---|---|
| A managed file was edited locally | Refuses; names the files; exit `1` |
| A declared file is missing | Refuses; names the files; exit `1` |
| An undeclared file is under `.sde/` | Refuses; names the files; exit `1` |
| `.echelon/sde.json` cannot be read | Refuses; never acts on an installation it cannot verify |
| `sde.config.json` is present but invalid | Refuses; reports the invalid field; never rewrites it |
| The installation is newer than this release | Refuses to downgrade |

`sde doctor` explains each of these and what to do about it.

## Failure safety

The execution package is copied into a temporary sibling directory of `.sde/`
and independently re-verified against its own manifest *before* anything under
`.sde/` is replaced. Only then is the directory swapped in with a rename on
the same filesystem. If the rename fails, the previous installation is moved
back.

The consequence: a failure mid-install never leaves `.sde/` partially written,
and never leaves the repository with no `.sde/` at all.

## Committing the result

Commit `.sde/` and `.echelon/sde.json`. They record which methodology version
the repository is operating under, and `verify` needs them to detect drift.
Neither contains timestamps, absolute paths, machine identifiers or
credentials, so they are reproducible on any machine.

## Verifying in CI

```yaml
- run: npx @echelon-foundry/sde verify --strict
```

or, to fail when the installation has drifted from what the current release
would install, without writing anything:

```yaml
- run: npx @echelon-foundry/sde init --check
```

## Uninstalling

Delete `.sde/` and `.echelon/sde.json`. The tool installs nothing else and
leaves nothing behind. If your repository has a `sde.config.json`, that file
is yours; delete it if you no longer want it.
