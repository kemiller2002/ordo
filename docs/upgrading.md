# Upgrading and migration

How `npx @echelon-foundry/sde upgrade` moves an installation between versions,
what it guarantees, and where the compatibility boundary lies.

## The model

Upgrading is **not** "delete the old files, copy the new ones". It is a
sequence of explicit transitions, each of which knows its source version, its
destination version, its preconditions, its effect and how it is validated.

```
inspect
   -> determine current state
   -> calculate the transition
   -> validate every precondition
   -> execute in order
   -> verify the result
```

Every precondition in the whole chain is checked **before anything is
written**. A migration that cannot run stops the upgrade before it starts,
rather than half way through.

Two things move independently:

- the **payload version** — which release of the execution package is
  installed (`1.1.1` to `1.2.0`, say);
- the **configuration version** — the shape of the installation itself, moved
  only by a numbered migration.

## Configuration versions

| Version | Shape | Produced by |
|---|---|---|
| 1 | `.sde/` with `MANIFEST.json` and `VERSION`, no `.echelon/` record | releases 1.0.0 – 1.1.1 |
| 2 | the same, plus `.echelon/sde.json` | release 1.2.0 onward |

### Migration `0001-echelon-installation-record` (1 → 2)

**Effect.** Writes `.echelon/sde.json` from the installation already on disk.

**Precondition.** `.echelon/sde.json` is either absent or readable. A record
that exists but cannot be parsed blocks the migration, because overwriting it
would destroy the evidence needed to diagnose it.

**Safety.** Purely additive. It creates one generated file and touches nothing
else, so a repository that has customised anything keeps every byte of it.

Migrations are applied in order: an upgrade from configuration version 1 to a
future version 4 runs 1→2, then 2→3, then 3→4. There are no N-to-M shortcuts,
because a shortcut is a second code path that has to be kept correct as the
ladder grows.

## Supported upgrade paths

| From | Behaviour |
|---|---|
| Not installed | Refused — run `init` first (exit `1`) |
| Any older payload version, intact | Payload replaced, migrations run, record written |
| Current payload version, configuration 1 | Migration only; the payload is not rewritten |
| Current payload version, configuration 2 | No changes needed (exit `0`) |
| Newer than this release | Refused — never downgraded (exit `1`) |
| Locally modified | Refused — never overwritten (exit `1`) |
| Unreadable installation record | Refused (exit `1`) |
| Invalid `sde.config.json` | Refused (exit `1`) |

Each of these is covered by an automated test, and the historical-installation
path is additionally exercised against the real packed npm artifact.

## Guarantees

These are the guarantees the tests prove, and no more:

1. **A locally modified installation is never overwritten.** `upgrade` refuses
   and names every changed file. The repository is byte-identical before and
   after a refused upgrade.
2. **An installation newer than this release is never downgraded.**
3. **User-owned files are never touched.** `sde.config.json`, your source,
   your documentation — nothing outside `.sde/` and `.echelon/sde.json` is
   read for writing.
4. **Other Echelon Foundry tools' records are never touched.** SDE owns
   exactly one file in `.echelon/`.
5. **A failure reports what was already applied.** If a migration fails part
   way through a chain, the error names the failing migration and lists the
   migrations that had already succeeded. Partial application is always
   reported, never silent.
6. **`--dry-run` writes nothing.** Dry-run and `--check` stop after planning
   and never reach the code that writes.

What is **not** guaranteed: a transactional rollback of a partially applied
migration chain. Failure is made detectable and recovery is made obvious, but
a chain that fails in the middle leaves the completed steps in place. Every
migration so far is additive, so this has no data-loss consequence today; a
future destructive migration would need a stronger mechanism before it could
be accepted.

## Previewing an upgrade

```bash
npx @echelon-foundry/sde upgrade --dry-run
npx @echelon-foundry/sde upgrade --dry-run --json
```

Dry run inspects, calculates the complete plan, validates it, reports what
would change, and writes nothing.

For CI, `--check` does the same and exits `5` when any change is required, so
drift is distinguishable from a broken installation (`1`).

## Resolving a refused upgrade

When `upgrade` refuses because of local modifications:

```bash
npx @echelon-foundry/sde doctor
```

`doctor` names each file, says why the modification blocks the upgrade, and
gives the remedy. Normally that is restoring the file from version control.

`.sde/` is not a place for project-specific content. If your project genuinely
needs to deviate from installed SDE guidance, record that decision outside
`.sde/`. A deviation-tracking mechanism inside the tool is future work.

## Compatibility policy

- **Package name, executable name and command set** are stable. A command is
  never removed; a renamed command keeps its old name as an alias
  indefinitely, as `update` does for `upgrade`.
- **`MANIFEST.json` schema version 1** is the installed format. A change to it
  requires a schema bump and a migration.
- **JSON output schemas** carry `schemaVersion`. Fields are added without a
  bump; a removed field or a changed meaning requires one.
- **Exit codes `0`, `1` and `2`** keep the meanings they have had since 1.0.0.
- **`sde.config.json`** is your file. Its schema only grows; a field that
  validates today keeps validating.

## The 1.1.1 → 1.2.0 compatibility boundary

Release 1.2.0 reimplemented the tool in F#. What consumers depend on did not
change:

- the built execution package is **byte-identical** to what the previous
  JavaScript implementation produced from the same sources, verified by
  diffing both builders' output;
- `.sde/MANIFEST.json` is unchanged in schema, field names, field order and
  serialization;
- `sde.config.json`, its defaults, its validation rules and the
  `SDE-STRUCT-001` bands are unchanged;
- `update` still works and does what it always did;
- `bin/sde.mjs` remains a working entry point for callers that invoke it by
  path;
- an installation from 1.0.0–1.1.1 passes `verify` with no action required.

**The one behavioural change.** The tool now runs as a native executable
rather than as JavaScript. On a platform outside the supported list it exits
`3` with a clear message instead of running. The supported list covers
Windows x64 and ARM64, Linux x64 (glibc and musl), Linux ARM64, macOS x64 and
macOS ARM64 — but a platform outside it that previously worked because Node
ran there will now refuse. This is the only way in which 1.2.0 can fail where
1.1.1 succeeded.

**One new behaviour on an existing installation.** Running `init` or `upgrade`
against a 1.0.0–1.1.1 installation now writes `.echelon/sde.json`, which did
not exist before. This is additive, it exits `0`, and it is reported. If you
diff your repository in CI, expect that one new file the first time.

`verify` without `--strict` does **not** fail an installation for being at
configuration version 1, so an existing CI job keeps passing until you choose
to upgrade.
