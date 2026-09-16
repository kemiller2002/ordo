# Releasing `@echelon-foundry/sde`

The supported release path is the `Publish @echelon-foundry/sde to npm`
GitHub Actions workflow. A local developer machine is not the authoritative
release process.

## How a release is triggered

`.github/workflows/publish.yml` runs when `distribution/package.json` changes
on a push to the release branch. It compares the local version against what is
on npm and publishes only when they differ, so an unrelated edit to
`package.json` does not cause a re-publish attempt.

## The version bump

`distribution/package.json`'s `version` is the single authoritative version
source. It sets the npm package version, the version the CLI reports, and the
`sdeVersion` recorded in every installed `MANIFEST.json`. Bumping it is the
whole of the release change.

Choose the number against the public surface, not the diff size:

| Change | Bump |
|---|---|
| A command, flag, output field or exit code is removed or changes meaning | major |
| A command, flag or output field is added; methodology content changes | minor |
| A fix with no change to the public surface | patch |

Adding a configuration migration is at least a minor bump, because an
installation's shape changes.

## What must pass before publishing

The workflow publishes only after all of this has succeeded:

1. the execution package builds from canonical sources;
2. the F# test suite passes on Linux, Windows and macOS;
3. every platform executable builds;
4. the packed tarball is exercised on Linux, Windows and macOS —
   `npm run test:package`, which installs the real tarball into a throwaway
   project and drives the installed executable;
5. `npm pack --dry-run` output is recorded in the log for review;
6. the packed-artifact checks are re-run against the exact release build.

The separate `SDE distribution` workflow runs the same validation on every
pull request, and additionally runs the documented quick-start commands
against a globally installed copy of the packed tarball, so a README example
cannot drift from what the package does.

## Authentication

Publishing uses npm's **Trusted Publishing** (OIDC). No `NPM_TOKEN` is stored
in this repository. The workflow's `id-token: write` permission is exchanged
by npm CLI 11.5.1 or newer for a short-lived publish credential, authorized
because the package's Trusted Publisher settings on npmjs.com name this exact
repository and this exact workflow file.

npm requires a package to exist before Trusted Publishing can be configured
for it, so the first publish of a new package name must be done manually
(`npm login` and `npm publish --access public` from a machine with publish
rights). `@echelon-foundry/sde` is past that point.

## Releasing, step by step

1. Land the change, with its tests, on the release branch.
2. Bump `version` in `distribution/package.json`.
3. Check what would be published:
   ```bash
   cd distribution
   npm run build
   npm pack --dry-run
   ```
4. Exercise the real artifact locally before relying on CI:
   ```bash
   npm run test:package
   ```
5. Push the version bump. The workflow validates and publishes.
6. Confirm: `npm view @echelon-foundry/sde version`.

## Reproducing the release build locally

```bash
cd distribution
npm run build             # payload plus all six platform executables
npm run test:package      # pack and exercise the tarball
npm pack                  # produce the .tgz for inspection
tar -tzf echelon-foundry-sde-*.tgz | sort
```

## What is published

Controlled by the `files` allow-list in `distribution/package.json`, never by
a deny-list:

| Path | Why |
|---|---|
| `bin/` | The Node launcher and the legacy entry point |
| `dist/` | The execution package `init` installs |
| `runtimes/` | One self-contained executable per supported platform |
| `DISTRIBUTION-MAP.json` | The canonical source → installed file mapping |
| `README.md` | The npm package page |

There is no `.npmignore`: the allow-list is the single source of truth for
what ships, so there is nothing for a deny-list to contradict.

Deliberately not published: F# sources and project files, the test suite,
`scripts/`, `authored/` (its content is already inside `dist/`), debug
symbols, dependency manifests and build residue. `npm run test:package`
asserts each of these absences, and asserts that nothing resembling a secret
or local configuration is included.

## Package size

The package ships six self-contained executables of roughly 11 MB each, so
the tarball is around 33 MB. This is the cost of requiring no .NET runtime on
a consumer's machine and nothing downloaded at run time.

Splitting the platform executables into per-platform packages behind
`optionalDependencies`, so that a consumer downloads only their own, is
recorded as future work. It would reduce a first `npx` to roughly 12 MB at the
cost of publishing seven packages per release.

## Provenance

Each build records the canonical repository's git commit in the installed
`MANIFEST.json` as `sourceRevision`, so any installed `.sde/` is traceable to
the exact source that produced it. A build from a working tree with
uncommitted changes records the commit with a `-dirty` suffix, so a release
can never claim a resolvable commit whose content differs from the packaged
bytes.

A released package version is immutable. If canonical methodology changes
materially, that requires a new package version and a new build, not a silent
regeneration of an already-published version's contents. This matters for
reproducible engineering trials that cite a specific installed SDE version.
