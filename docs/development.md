# Developing `@echelon-foundry/sde`

The tool's lifecycle logic is F#. Node is only the launcher.

```
npm / npx
    |
    v
distribution/bin/sde.js          tiny Node bootstrap: pick platform, exec, forward
    |
    v
distribution/runtimes/<rid>/sde  self-contained F# executable
    |
    v
src/Sde.Cli                      argument parsing, help, rendering  (adapter)
    |
    v
src/Sde.Core                     inspection, planning, migration, execution
```

## Prerequisites

- .NET SDK 8.0
- Node.js 18 or newer (22 is what CI uses for the packaging jobs)

## Layout

| Path | Contents |
|---|---|
| `src/Sde.Core/` | The domain: state, ownership, manifest, planning, migrations, execution, diagnostics, JSON output, the package builder |
| `src/Sde.Cli/` | The CLI adapter: argument parsing, help text, human rendering, entry point |
| `src/Sde.PackageBuilder/` | Release-time tool that builds the execution package from canonical sources. Not published. |
| `tests/Sde.Core.Tests/` | The F# test suite, covering both core and CLI |
| `distribution/` | The npm package root |
| `distribution/bin/sde.js` | The Node launcher |
| `distribution/scripts/` | Build orchestration and the packed-artifact test. Not published. |
| `distribution/DISTRIBUTION-MAP.json` | Canonical source → installed file mapping |
| `distribution/authored/` | Content with no canonical source, maintained directly |

In `Sde.Core`, compile order is dependency order: a module can only use what
is listed above it in `Sde.Core.fsproj`, so a cycle is impossible to express.

## Architectural rules

These are the rules the design depends on. Breaking one is a design change,
not a refactor.

1. **JavaScript decides nothing.** `bin/sde.js` may detect the platform,
   locate the executable, forward arguments and stdio, and return the exit
   code. It must not know what files SDE installs, what repository state
   means, whether an installation is valid, what migrations exist, or what any
   exit code other than its own `3` and `4` means.
2. **`Execution.fs` is the only module that writes** to a consuming
   repository. This is what makes `status`, `verify`, `doctor` and `--dry-run`
   provably non-mutating: they never reach it.
3. **The CLI is an adapter.** Every capability is a function in
   `Sde.Core.Lifecycle` taking plain values and returning typed results.
   Nothing in `Sde.Core` reads argv, writes to a console or calls exit. ROS,
   an integration assembly, a test or a future service host can drive the same
   lifecycle without simulating a command line.
4. **Illegal states are unrepresentable where practical.** `InstallationState`,
   `PlannedChange`, `InstallationProblem` and `PlanRefusal` are closed unions,
   and incomplete-match warnings are errors, so adding a case forces every
   decision site to say what it means.
5. **All JSON goes through `Sde.Core.Json` and `Sde.Core.Output`.** Nothing
   concatenates JSON by hand, so the machine interface is one file to review.
6. **No new dependencies** without a clear justification. The tool uses
   FSharp.Core and the .NET base class library, nothing else. JSON is written
   by hand rather than by reflection specifically so that trimming is safe;
   trim warnings are errors.

## Versioning

`distribution/package.json`'s `version` is the single authoritative source.
`Directory.Build.props` reads it at evaluation time and sets the .NET
`Version`, so the npm package version, the version the CLI reports and the
`sdeVersion` recorded in an installed `MANIFEST.json` cannot drift apart. A
release bump is one edit in one file, and a test asserts all three agree.

## Building

```bash
cd distribution

npm run build:payload     # build the execution package into distribution/dist
npm run build:runtimes    # build all six platform executables
npm run build             # both
```

Every supported platform cross-compiles from a single host, so
`npm run build:runtimes` produces all six from Linux, macOS or Windows alike.

To build only what you need while iterating:

```bash
node scripts/publish-runtimes.mjs --only linux-x64
```

## Testing

```bash
cd distribution
npm test                  # builds the payload, then runs the F# suite
```

or directly:

```bash
dotnet test tests/Sde.Core.Tests/Sde.Core.Tests.fsproj
```

The suite covers path safety, manifest compatibility, the full lifecycle,
idempotency, historical upgrade fixtures, damaged installations, shared-file
conflicts, argument parsing, help, exit codes and JSON output.

### Testing the package, not just the source

`dotnet test` is not sufficient evidence that npm distribution works. It says
nothing about whether the tarball contains the right files, whether the bin
mapping resolves, or whether the launcher finds its executable from the
installed layout.

```bash
cd distribution
npm run build             # runtimes must exist first
npm run test:package
```

This packs the package, installs the tarball into a throwaway project, and
drives the installed `sde` executable against clean temporary repositories —
including init, a second init, status, verify, doctor, upgrade, every JSON
mode, every dry-run mode, damaged installations and a historical-version
upgrade.

To exercise a tarball that has already been built rather than packing a new
one, point `SDE_TEST_TARBALL` at it:

```bash
SDE_TEST_TARBALL=../packed-artifact/echelon-foundry-sde-1.2.0.tgz npm run test:package
```

CI uses this so that Linux, Windows and macOS all exercise the one artifact
that would actually be published, instead of each rebuilding its own.

## Running the CLI from a checkout

The published layout puts the executable at `runtimes/<rid>/sde` and the
payload two levels up at `dist/`. From a build output directory that layout
does not hold, so set the payload location explicitly:

```bash
cd distribution
npm run build:payload

export SDE_PAYLOAD_DIR="$PWD/dist"
dotnet run --project ../src/Sde.Cli/Sde.Cli.fsproj -- status
```

`SDE_PAYLOAD_DIR` is a development and test affordance, not part of the
supported interface for consumers.

## Changing what gets installed

`distribution/DISTRIBUTION-MAP.json` is the machine-readable mapping from
canonical documents to installed files. To add a document to the execution
package, add an entry there — do not add a file under `distribution/dist/`,
which is generated and never hand-edited.

The build fails loudly if a canonical source the map names is missing: a
silent skip would ship an incomplete methodology with no indication.

A file with no canonical source is marked `"authored": true` and lives under
`distribution/authored/`, with the reason recorded in the map entry.

## Adding a migration

1. Add a `Migration` value in `src/Sde.Core/Migrations.fs` with its id, source
   and destination configuration version, description, precondition and
   effect.
2. Add it to the `all` list.
3. Bump `InstallationRecord.currentConfigurationVersion`.
4. Add a test that upgrades a fixture at the previous configuration version,
   and a test that the precondition blocks when it should.
5. Document it in [upgrading.md](upgrading.md).

Migrations move one version at a time. Do not add an N-to-M shortcut.

## Changing the CLI surface

Help text is public documentation. A change to a command's behaviour is not
finished until `src/Sde.Cli/Help.fs`, `docs/cli.md`, `distribution/README.md`
and the actual behaviour all agree. A test asserts that every option
documented in help is one the parser accepts.

## Repository governance

This repository runs ROS. Before making a meaningful change:

```bash
./ros work begin WI-XXXX --type task
./ros validate
```

`./ros validate` fails a change with no active or completed work-item
attribution. See `docs/work-protocol.md`.
