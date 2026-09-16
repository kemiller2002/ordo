---
id: SDE-DISTRIBUTION-AUDIT-001
title: SDE Distribution and Project Bootstrap — Independent Audit
status: accepted
version: 1.0.0
created: 2026-09-02
updated: 2026-09-02
tags: [audit, distribution]
---

# SDE Distribution and Project Bootstrap — Independent Audit

> **HISTORICAL RECORD.** This audit was performed in September 2026 against
> the JavaScript implementation of `@echelon-foundry/sde` that shipped in
> releases 1.0.0 through 1.1.1. That implementation was replaced in 1.2.0 by
> the F# implementation under `src/Sde.Core` and `src/Sde.Cli`, and the
> `distribution/src/` and `distribution/test/` trees this report examines no
> longer exist.
>
> The report is retained because its findings, and the reasoning behind the
> defects it fixed, still describe why the current implementation behaves as
> it does — the F# port preserves every behaviour this audit established, and
> produces a byte-identical execution package. It is **not** a description of
> the current code layout. For how the tool works now, see
> [`docs/development.md`](../docs/development.md); for the command contract,
> see [`docs/cli.md`](../docs/cli.md).

## Executive result

**PASS AFTER REMEDIATION.**

Three confirmed defects were found by direct execution (not by trusting prior
documentation, tests, or agent summaries) and fixed within this audit, per
the audit's own fix policy. After the fixes, the full chain — canonical
source → deterministic build → npm package → `init` → `.sde/` → fresh-agent
usability — was independently re-verified and holds, with one residual,
explicitly-documented limitation (dangling inline prose citations into
non-distributed directories) that was investigated, found real, and
deliberately **not** mechanically fixed because doing so safely was judged
higher-risk than the defect itself warrants (see "Findings," F3).

## Repository state

| | Start of audit | After fixes |
|---|---|---|
| Branch | `claude/ros-bootstrap-init-ao9vu4` | same |
| HEAD | `301cbf16495f0c3a830b3b0d186b4bd4d7fd85c7` | same (fixes are uncommitted at audit-writing time, committed immediately after) |
| Working tree | clean | `distribution/{bin/sde.mjs, src/build.mjs, test/build.test.mjs, test/cli-entrypoint.test.mjs}` modified |
| Node | v22.22.2 | same |
| npm | 10.9.7 | same |
| Package version | 0.1.0 | same |
| Method version | 0.1 | same |
| Manifest schema version | 1 | same |

## Implementation map

- Canonical methodology: `doctrine/*.md`, `method/*.md`, `templates/sde/*.md` (repository root).
- Distribution mapping: `distribution/DISTRIBUTION-MAP.json` (version-controlled, one entry per distributed file, `authored: true` for the one file with no canonical source).
- Builder: `distribution/src/build.mjs`.
- Generated package (gitignored, rebuilt by `npm run build`): `distribution/dist/`.
- CLI: `distribution/bin/sde.mjs` + `distribution/src/commands/{init,status,verify,update}.mjs`.
- Supporting library: `distribution/src/{manifest,hash,paths,fsTree,semver,install}.mjs`.
- Tests: `distribution/test/*.test.mjs` (Node's built-in test runner).
- Package config: `distribution/package.json`.
- ROS: `.ros/`, `ros.json`, `ros` (repo root), `tools/ros_cli.mjs` — untouched (see ROS boundary section).

## Command audit table

| Command | Intended behavior (reconstructed from `distribution/README.md` + source) | Actual behavior (executed) | Pass/Fail |
|---|---|---|---|
| `init` | Install packaged version into `.sde/`; no-op if already installed and unmodified; refuse to overwrite modifications; warn and refuse on a newer-than-packaged install; tell (not act) when an older unmodified install exists | Verified via real CLI subprocess across: fresh install, repeat install (byte-identical mtimes unchanged), unrelated files/directories untouched, `.NET`-style repo, no-`package.json` repo, older-unmodified (reports update-available, does not act), same-version-modified (refuses), newer-installed (refuses) | **PASS** |
| `status` | Report installed/available version, source revision, integrity, modified/missing/unexpected counts; exit 0 clean, non-zero otherwise | Verified: clean install (0/clean), modified file (1 modified, exit 1), missing file, unexpected file, corrupt manifest (`unreadable`, exit 1), VERSION-only edit (detected via the explicit VERSION/manifest agreement check, exit 1) | **PASS** |
| `verify` | Recompute hashes, compare to manifest, exit 0/non-zero, usable in CI, never mutates | Verified: clean (exit 0), modified (exit 1), missing (exit 1), unexpected file (exit 1), malformed JSON manifest (exit 1, no crash), manifest missing required field (exit 1), unsafe manifest path (exit 1, rejected before any filesystem access), symlinked managed file (exit 1, **crashed with a raw stack trace before the fix in this audit; now a clean message — see F1**) | **PASS** (after fix) |
| `update` | Upgrade only when unmodified and older; never downgrade; never overwrite modifications; safe no-op at parity | Verified via real CLI with hand-built older/newer fixtures: older+unmodified upgrades correctly (`v0.0.1 -> v0.1.0`), older+modified refuses and leaves the installation untouched (content byte-compared before/after), same-version+modified refuses, newer-installed refuses with no downgrade, same-version+unmodified is a clean no-op | **PASS** |

The repository's own `distribution/README.md` is a sufficient reconstruction of the intended contract; no documentation/product-defect gap was found in the contract description itself (only in a few specific claims — see "Documentation vs. behavior" below).

## Canonical source authority

**PASS.** `distribution/dist/` is listed in `.gitignore` (`distribution/dist/`) and is not tracked by git (`git ls-files distribution/dist` returns nothing). `git log` shows every distributed canonical source file has exactly one authority (`doctrine/`, `method/`, `templates/sde/`); no independently-edited duplicate of any canonical document exists anywhere in the repository. Body content of every distributed file was byte-compared against its canonical source after the audit's fixes and is identical (only front-matter `related_documents` fields differ, by design — see F3).

## Distribution mapping audit

| Installed path | Canonical source | Exists | Correct | Notes |
|---|---|---|---|---|
| `method/CONSTRUCTION-METHOD.md` | `method/CONSTRUCTION-METHOD-v0.1.md` | yes | yes | |
| `method/CHANGE-CLASSIFICATION.md` | `method/CHANGE-CLASSIFICATION.md` | yes | yes | |
| `method/VERIFICATION-METHOD.md` | `method/VERIFICATION-METHOD.md` | yes | yes | |
| `method/AGENT-EXECUTION-RULES.md` | `method/AGENT-EXECUTION-RULES.md` | yes | yes | |
| `architecture/FOUR-TIER-ARCHITECTURE.md` | `doctrine/FOUR-TIER-ARCHITECTURE.md` | yes | yes | |
| `architecture/BOUNDARY-PRESERVATION.md` | `doctrine/BOUNDARY-PRESERVATION.md` | yes | yes | |
| `reference/GLOSSARY.md` | `doctrine/GLOSSARY.md` | yes | yes | |
| `reference/ENGINEERING-METRICS.md` | `method/ENGINEERING-METRICS.md` | yes | yes | |
| `templates/execution-log.md` | `templates/sde/execution-log.md` | yes | yes | |
| `templates/completion-report.md` | `templates/sde/completion-report.md` | yes | yes | |
| `templates/work-item.md` | (authored, no canonical source) | yes | yes | Explicitly flagged `authored: true` with a documented reason (ROS owns work-item format in the canonical repo); this is honest, not a heuristic fallback |
| `README.md` | (authored template) | yes | yes | |
| `VERSION` | generated | yes | yes | |
| `MANIFEST.json` | generated | yes | yes | |

No heuristic filename matching, no glob-based inclusion, no research files, no stale/duplicate destinations. `build()` fails loudly (throws before writing anything) if a named canonical source is missing — verified directly by injecting a missing-source entry into an isolated copy of the map (`test/build.test.mjs`).

## Installed content audit

Read in full, not merely checked for filename presence. All 13 non-generated managed files are non-empty (23–144 lines), contain no `TODO`/`TBD`/`FIXME`/`{{placeholder}}` markers (checked by `grep`), and are the intended documents (titles/content match their doctrine origin). Templates (`work-item.md`, `execution-log.md`, `completion-report.md`) are usable, minimal, and correctly cross-reference each other's distributed paths.

**Confirmed defect (fixed, see F3):** before this audit's fix, `related_documents` front-matter fields in 7 of 13 files pointed at canonical-repo-only paths (renamed or excluded-from-distribution), making every one of those structured cross-references dangling inside `.sde/`. Fixed by rewriting resolvable entries to their distributed path and dropping unresolvable ones at build time. Verified post-fix: zero dangling `related_documents` entries remain (automated test + independent fresh-agent link-following check).

**Residual, not fixed (see F3):** inline prose citations (not front matter) still reference `doctrine/`, `research/`, `framework/`, and `docs/00-governance/` paths that do not exist in the installed package (≈15+ occurrences across `CONSTRUCTION-METHOD.md`, `AGENT-EXECUTION-RULES.md`, `FOUR-TIER-ARCHITECTURE.md`, `GLOSSARY.md`, `ENGINEERING-METRICS.md`, `execution-log.md`). These read as evidence citations rather than navigable links and did not prevent a fresh agent from correctly beginning work (see "Fresh-agent usability" below), but they are a genuine documentation-completeness gap.

## What is NOT distributed

Verified by full recursive listing of a clean build (13 files + directory structure): no `research/`, no evidence/experiment data, no journals, no REP archive, no migration logs, no ROS internals, no `.git` metadata, no build cache, no `node_modules`. `npm pack` (both `--dry-run` and a real, extracted tarball) confirms the published tarball (32 files, 27.9–29.0 kB) contains only `bin/`, `src/`, `dist/`, `authored/`, `DISTRIBUTION-MAP.json`, `README.md`, `package.json` — no `test/` directory, no research archive, no secrets (tarball scanned for absolute paths, key/token/credential patterns — none found).

## Determinism audit

**PASS.** Built the package three separate times across this audit (twice before any fix, once after) from the same commit; `diff -r` between build outputs was empty every time, including `MANIFEST.json` itself (hash order is a stable `Array.sort()` over `path`, so no unordered-JSON nondeterminism). No timestamps are embedded in any managed file or in the manifest. No absolute paths appear in any output. The one genuine, intentional exception to "same input → same output" is `sourceRevision`, which is explicitly designed to reflect the state of the source tree at build time (see "Source revision audit").

## Manifest audit (independent verification)

Performed with a standalone Python + `hashlib` script — **not** the CLI's own verifier — reading `MANIFEST.json` directly, recomputing SHA-256 for every listed file, and checking for duplicate paths, unsafe paths, missing files, and unexpected files. Result: zero problems, on both the pre-fix and post-fix builds. `VERSION`'s content matched `MANIFEST.json.sdeVersion` exactly. This is independent, first-party confirmation that the CLI's own `verify` command is not silently lying about the state it reports (spot-checked by deliberately corrupting an installation and confirming the CLI's and the independent script's findings agreed in every case tested).

## Package audit

`npm pack --dry-run` and a real `npm pack` (tarball extracted and inspected, then deleted) both confirm: correct package name/version/bin mapping, `files` allowlist correctly excludes `test/`, no research/ROS leakage, no secrets, no giant artifacts, no `node_modules`. `package.json` has `engines.node: >=18`, `type: module`, and license `UNLICENSED` (a real gap to resolve before any actual publish, not evaluated further since publishing was not authorized or attempted).

## Runtime dependency audit

**0 runtime dependencies, 0 development dependencies.** `package.json` declares `"dependencies": {}` and `"devDependencies": {}`; `npm ls` confirms an empty tree; no `node_modules` directory exists or is needed. Everything is built on `node:fs`, `node:path`, `node:crypto`, `node:child_process`, `node:url`, and `node:test`. No dependency justification table is needed because there are no dependencies to justify.

## init/status/verify/update behavioral audit — cases executed

All executed against the real CLI binary (`node distribution/bin/sde.mjs <command>`) in freshly created `mktemp -d` directories, never inside the SDE repository itself:

- Case A (no `.sde/`): succeeds, all files confined to `.sde/`, manifest verifies, version correct.
- Case B (repeat init): no-op, `mtimeMs` of a managed file unchanged, byte-identical.
- Case C (pre-existing unrelated files: `package.json`, `README.md`, `src/index.js`, a random directory): all byte-unchanged after `init`.
- Sentinel file **outside** the temp project root: unchanged across `init`/`status`/`verify`/`update`, all run from inside the project.
- `.sde` pre-existing as a symlink to an external directory: `init` refuses (missing MANIFEST.json inside the symlink target), does not write into the external target, and does not replace the symlink.
- A managed file replaced by a symlink to an external file: `listManagedFiles` refuses to manage it; before this audit's fix this crashed with a raw stack trace (F1); after the fix, `verify`/`update` fail cleanly with exit 1 and the external file is confirmed byte-unchanged.
- `.NET`-style repo (`src/MyApp/MyApp.csproj`, no `package.json`, no Node project at all): `init`/`verify` succeed, `.csproj` untouched.
- Older-version, unmodified fixture → `update`: succeeds, new manifest valid, unrelated files untouched.
- Newer-version fixture (`9.9.9`) → `update` and `init`: both refuse, no downgrade, clear message.
- Corrupt/malformed manifest (invalid JSON; manifest missing `sdeVersion`; manifest entry with a `../../escape.txt` path): all three rejected before any further filesystem action, exit 1, descriptive message naming the exact problem.

## Failure atomicity audit

Code-reasoned (per the mission's own allowance when live fault injection is impractical) plus one live simulation. `installFrom` (`distribution/src/install.mjs`) builds into a sibling temp directory, independently re-verifies the copy against its own manifest, and only then performs two same-filesystem `fs.renameSync` calls (old → backup, new → target), restoring the backup if the second rename throws. A genuinely killed process could in principle land between the two renames, but that window contains no computation — it is as small as a rename-based swap can make it.

**Confirmed, low-severity gap (not fixed — see "Findings," F4):** if an install/update is interrupted before completion, the orphaned `.sde.new-*`/`.sde.old-*` sibling directories are never detected, reported, or cleaned up by any command. Verified directly: manually planting such directories left `status`/`verify`/`update` reporting a fully healthy installation while the orphaned directories sat untouched next to `.sde/`. This is cosmetic clutter, not data loss or corruption — the atomicity guarantee for `.sde/` itself held in every test — and is recorded as an observation for future work rather than fixed now, since adding orphan-detection was judged a scope-expanding feature, not a defect in the documented contract.

## Path-safety and symlink audit

**PASS**, with the crash found and fixed (F1). Malformed/malicious manifest paths (`../../escape.txt`, and by code inspection, absolute paths and `..`-containing paths generally) are rejected by `isSafeRelativePath`/`safeJoin` before any file operation; verified live that no file was ever created outside the project root. A pre-existing `.sde` symlink is refused safely. A managed file replaced by a symlink is refused (never followed for read or write) at the `listManagedFiles` layer; this is intentional defense-in-depth, not an incidental side effect — confirmed by direct code inspection of `distribution/src/fsTree.mjs`.

## Language-neutrality audit

**PASS.** Installed cleanly into: a completely empty directory, a directory with only a `.csproj` (`.NET`-style), a directory with `package.json`/`src/`/random files (Node-style), and (implicitly, since every test repo used) a directory with no ROS installation whatsoever. No command ever inspected or required `package.json`, `src/`, `.NET` project files, GitHub Actions config, or `.ros/`.

## No-runtime-coupling audit

**PASS.** No command reads or writes any file outside `.sde/` in the target project (confirmed by the sentinel-file and unrelated-file tests above). No `package.json` in the target project was ever created or modified by any SDE command. `@echelon-foundry/sde` is invoked as a dev-time/engineering-time tool (`npx`) and is not added as a dependency of the target project by any command.

## ROS independence audit — mandatory table

| Question | Result |
|---|---|
| ROS source modified? | **NO** |
| ROS behavior modified? | **NO** |
| ROS schemas modified? | **NO** |
| ROS package modified? | **NO** |
| SDE CLI imports ROS? | **NO** |
| SDE package depends on ROS? | **NO** |
| Target project must have ROS? | **NO** |

Evidence: `git log --oneline -- ros.json ros tools/ros_cli.mjs .ros/installation.json` shows exactly one commit in the entire repository history touching any of those paths — the original ROS install commit itself (`0378571`) — and none since, across the bootstrap mission, the distribution-build mission, or this audit. `grep` across every `.mjs`/`.json` file in `distribution/` (source and generated output) for ROS references found only: explanatory code comments confirming non-coupling, one prose sentence in `DISTRIBUTION-MAP.json`'s documentation of why `work-item.md` is authored rather than copied from ROS, and prose mentions of ROS inside the distributed `GLOSSARY.md`/`work-item.md` (informational content about ROS's identifier scheme and "use ROS if you have it," not a functional dependency). `package.json` has zero dependencies of any kind. Every behavioral test above ran in repositories with no `.ros/` present at all and succeeded.

## Trust-model review

Determined directly by Challenge C (deliberately editing a managed file's content **and** its manifest hash together, leaving `VERSION` unchanged): `verify` reported full success (`SDE v0.1.0 verified`, exit 0) even though the installed content was completely fabricated. **This is not a bug — it is the actual, current trust level, and it was previously undocumented.**

**Current trust model: Level 1 — local consistency only.** `verify`/`status` prove that the files under `.sde/` agree with the manifest that ships beside them in the same installation. They do **not** prove that the manifest itself is the one `@echelon-foundry/sde@0.1.0` actually published (Level 2 — package consistency, which would require the manifest or its hash to be checked against something outside the locally-editable installation, e.g. a value baked into the installed npm package's own `dist/`), and they do not prove cryptographic release authenticity (Level 3), since there is no signing step anywhere in this pipeline.

In practice this trust level is adequate for the stated purpose (detecting *accidental* local drift), and is explicitly weaker than "proves this content is genuine SDE v0.1.0" — a distinction `distribution/README.md` did not previously state. Recorded here as a documentation gap (see F2); not fixed by adding cryptographic signing, since that would be a new feature far outside "smallest correction to the existing contract."

## Documentation-vs-behavior discrepancy table

| Documentation claim | Implementation evidence | Match? |
|---|---|---|
| "local modifications... are never silently overwritten by `init` or `update`" | Confirmed in every modified-file scenario tested (Challenge A, older+modified, same-version+modified) | Yes |
| "Installation is atomic... a failure mid-install never leaves `.sde/` partially written" | Confirmed by code inspection + orphan-directory simulation; the *target* `.sde/` is indeed never left partial. The claim doesn't mention orphaned temp siblings, which is accurate as literally written but could be read as a stronger guarantee than exists | Yes, as literally written |
| "any installed `.sde/` is traceable to the exact canonical source that produced it" | **False before this audit's fix** when the canonical tree was dirty at build time — the manifest recorded a real, resolvable commit SHA whose committed content did not match what was packaged, with no indication of the mismatch. Fixed (F5): dirty builds now record `<sha>-dirty` | No, until fixed |
| "CI use... 0 = valid, non-zero = invalid" | Confirmed for every scenario tested, including previously-crashing ones (now fixed, F1) | Yes, after fix |
| "Zero npm dependencies (runtime or development)" | Confirmed directly (`npm ls`, `package.json`) | Yes |
| "SDE does not depend on ROS... installer does not modify or require it" | Confirmed (see ROS table) | Yes |
| Implicit claim (never stated outright) that `verify` proves file authenticity | **Overstated by omission** — actual guarantee is Level 1 local consistency only (see Trust-model review) | Documentation gap, not fixed |
| Language-neutral, no framework assumptions | Confirmed across four repo shapes | Yes |

## Test-suite audit

`npm test` (Node's built-in `node --test`, zero test dependencies) from a clean state (`rm -rf dist && npm test`, which reruns `npm run build` via `pretest`): **23 total, 23 passed, 0 failed, 0 skipped**, ~0.5–0.9s. Tests were inspected, not merely trusted: they use real `fs.mkdtempSync` temp directories (never the SDE repo itself), call the real command modules and, for two tests, the actual `bin/sde.mjs` subprocess entry point; they assert exit codes explicitly; `test/version-mismatch.test.mjs` and this audit's manifest script both independently re-derive hashes rather than trusting the CLI. Before this audit, no test exercised a symlinked managed file, a dirty source tree, or related-document dangling links — three genuine test gaps, now closed by four new tests added during remediation (see "Re-audit after fixes").

## Clean-build audit

`rm -rf distribution/dist && node distribution/src/build.mjs` reproduces the full package from committed canonical sources with no manual prerequisite (`npm run build` performs the same command). `npm test` itself triggers a rebuild via `pretest`, so a stale artifact cannot silently pass the suite. No hidden step, environment variable, or manual file placement was required at any point in this audit.

## Git audit

`git status --short` was checked after every build/test/pack/fresh-agent cycle in this audit. Between fixes, the tree returned to exactly its starting state (only intentional edits remained staged/modified); `distribution/dist/` never appeared as untracked clutter (correctly gitignored); a real `npm pack` tarball was created and deleted, leaving no residue. At the time this report was written, the working tree carries exactly the four files this audit's fixes touch — no incidental changes.

## Versioning audit

`sdeVersion` (0.1.0), `methodVersion` (0.1, read from `method/CONSTRUCTION-METHOD-v0.1.md`'s own front matter — single source, not hand-duplicated), and `schemaVersion` (1) are three independent fields in `MANIFEST.json`, confirmed structurally distinct (changing one does not require changing another; verified `buildManifest` never derives one from another). **Observation, not a defect:** no automated test currently exercises "package patch bump, method/schema unchanged" as its own scenario — the version fixtures used in this audit and the existing suite vary `sdeVersion` alone, which exercises the same code path but was not asserted as a named scenario. Recorded as a minor test-coverage gap.

## Source-revision audit

**Confirmed defect, fixed (F5).** Before the fix, `readSourceRevision()` recorded `git rev-parse HEAD` unconditionally; a dirty canonical tree at build time produced a manifest whose `sourceRevision` named a real, resolvable commit SHA that did not actually correspond to the packaged content — verified directly by dirtying a tracked canonical file, rebuilding, and confirming the injected content was packaged under an unqualified, clean-looking SHA. Fixed by checking `git status --porcelain` in `REPO_ROOT` at build time and appending `-dirty` to `sourceRevision` when the tree is not clean; verified in both directions (dirty tree → suffixed, clean tree → unsuffixed) with two new automated tests. Outside git entirely (e.g., installed from a tarball with no `.git` present), `sourceRevision` is `null` — this was already correctly handled before the fix and remains so; it is a limitation of the packaging environment, not something the CLI can improve, and no consuming-project operation requires git.

## Release-immutability audit

Canonical content can change without a package-version bump today — nothing enforces the two moving together, and this was not part of the original mission's scope to add (doing so would require either a version-check gate in CI or in `npm publish`, which is a process/release-engineering control outside this repository's implementation). This is recorded as a **known gap**, not fixed: rebuilding `0.1.0` from a different canonical-source state today produces different bytes with the same declared version, and (until the dirty-tree fix, F5) with no visible indication in the manifest that this had happened for an in-progress, uncommitted change. The fix in F5 mitigates the *uncommitted*-tree case; it does not prevent someone from committing a substantive doctrine change and rebuilding under the same package version number — that remains a human/process discipline requirement (`distribution/README.md`'s "Methodology Immutability" section already states the intended rule; nothing in tooling enforces it).

## Security/secrets audit

No API keys, tokens, credentials, private URLs, absolute user-specific paths, environment dumps, or logs found in the extracted `npm pack` tarball or anywhere under `distribution/`. Checked via `grep` for common credential patterns (`AKIA`, `BEGIN ... PRIVATE KEY`, `secret`, `token=`) and manual path review — none found.

## Unexpected-coupling audit

No installer/runtime code assumes HelixNote, F#, .NET, ROS, GitHub, a specific developer username, a specific local path, a specific branch name, or a specific unrelated commit SHA. (`sourceRevision` is the *release-provenance* commit SHA, which is the intended, documented use of a commit SHA — not an unrelated coupling.) Doctrine content legitimately *mentions* HelixNote/F#/.NET as the evidentiary basis for specific claims (per SDE's own Language Neutrality doctrine, distinguishing semantic conformance from enforcement strength) — this is content, not code coupling, and was judged in-scope/appropriate rather than a defect.

## Findings

| ID | Severity | Classification | Summary | Evidence | Remediation |
|---|---|---|---|---|---|
| F1 | HIGH | confirmed defect | `verify`/`update` crashed with a raw, uncaught Node stack trace (file:// paths and internal call frames exposed) instead of a clean failure when a managed file was replaced by a symlink; exit code was incidentally non-zero but the failure mode violated "error messages identify the problem clearly enough for automation and humans" | Live reproduction: symlinked `method/CONSTRUCTION-METHOD.md` to an external file, ran `verify`/`update` | **Fixed**: `bin/sde.mjs`'s dispatcher now catches any thrown error from a command handler and reports it through the same clean `result()` path used everywhere else. Regression test added (`cli-entrypoint.test.mjs`) |
| F2 | MEDIUM | documentation gap | The manifest's trust model (Level 1, local consistency only — a file+hash pair edited together in the installed manifest is indistinguishable from a genuine install) was never stated; `distribution/README.md`'s integrity language could be read as a stronger guarantee | Challenge C: edited a managed file and its manifest hash together, `verify` reported full success | **Not fixed** (documentation-only gap identified; recommended follow-up, not made in this audit to avoid expanding this report's own scope into rewriting shipped prose beyond what the fix policy allows for a distribution-mechanism audit) |
| F3 | MEDIUM | confirmed defect (partially fixed) | `related_documents` front-matter fields in 7 of 13 distributed files pointed at canonical-only paths (renamed or excluded from distribution), making every one dangling inside `.sde/`; inline prose citations (≈15+ occurrences) have the same problem and remain unfixed | Independent fresh-agent link-following audit (before and after fix); automated test | **Front matter fixed**: `build.mjs` now rewrites resolvable `related_documents` entries to their distributed destination and drops unresolvable ones, verified byte-identical elsewhere in the file. **Prose citations intentionally not fixed** — see rationale in "Installed content audit" |
| F4 | LOW | design limitation | Orphaned `.sde.new-*`/`.sde.old-*` sibling directories from an interrupted install/update are never detected or cleaned up by any command; `status`/`verify` report a fully healthy installation while ignoring them | Manually planted orphan directories; `status`/`verify`/`update` all ignored them | **Not fixed** — recorded as an observation; adding detection would be a scope-expanding feature, not a defect against the documented contract |
| F5 | MEDIUM-HIGH | confirmed defect | A dirty canonical source tree at build time produced a `sourceRevision` naming a real, resolvable, clean-looking commit SHA whose actual committed content did not match what was packaged — directly contradicting `distribution/README.md`'s traceability claim | Dirtied a tracked canonical file, rebuilt, observed the injected content packaged under an unqualified SHA | **Fixed**: `readSourceRevision()` now appends `-dirty` when `git status --porcelain` is non-empty. Two new automated tests (dirty and clean cases) |
| F6 | OBSERVATION | test gap | No test names "package version bumps, method/schema versions unchanged" as its own scenario, though the underlying independence is structurally guaranteed | Code inspection of `buildManifest` | Not fixed — recommend a named test in future work |
| F7 | OBSERVATION | design limitation | Nothing prevents rebuilding an already-published package version from different (but committed) canonical content; only the *uncommitted*-tree case is now caught (F5) | Reasoning from `build.mjs`; not independently re-executed against a real second commit | Not fixed — process/release-engineering control, not an implementation defect |

## Re-audit after fixes

Repeated per the mission's requirement, after F1/F3/F5 were fixed:

- Clean build: `rm -rf dist && node src/build.mjs` — succeeds, 13 managed files.
- Full test suite: **23/23 passing** (19 pre-existing + 4 new regression tests for F1, F3, F5).
- `npm pack --dry-run`: 32 files, no leakage, matches pre-fix file count and shape.
- `init`/`status`/`verify`/`update`: re-run in fresh temp repos, all pass as documented above.
- Modified-file challenge: still correctly detected.
- Path-safety challenge: still correctly rejected.
- Symlink challenge (F1's exact reproduction): now fails cleanly, no stack trace, external file confirmed untouched.
- Dirty-tree challenge (F5's exact reproduction): now correctly suffixes `-dirty`; clean-tree rebuild correctly omits it.
- `related_documents` challenge (F3): zero dangling front-matter entries, confirmed by both an automated test and a second independent fresh-agent link-following pass.
- ROS independence: re-checked against the post-fix diff (`bin/sde.mjs`, `src/build.mjs`, two test files) — no ROS reference of any kind introduced.
- Fresh-agent check: repeated in a brand-new temp repository with the post-fix package; verdict unchanged at **YES WITH LIMITATIONS** (see below) — the limitations are the already-documented residual prose-citation gap (F3), not a new problem introduced by the fixes.

## Fresh-agent usability audit

A subagent with no prior context beyond a freshly-`init`'d `.sde/` directory, instructed not to read anything outside that directory, answered all twelve required questions, citing the correct source file for each, rating each **obvious** or **discoverable** (none **ambiguous** or **missing**) except two rated **discoverable**-with-friction because their own cross-references point at files this package deliberately does not install (the naming-collision disambiguation note in `FOUR-TIER-ARCHITECTURE.md`, and the "Definition of Done" pointer in the construction method's stopping rule). It independently found and listed every dangling inline-prose reference (≈19 distinct dead links across 6 files), confirming F3's residual scope precisely.

## Final fresh-agent question

**Could an autonomous engineering agent with no prior conversation history correctly begin a normal software change using only the project requirements and the installed `.sde/` package?**

**YES WITH LIMITATIONS.**

Justification: the five-step entry path in `.sde/README.md` is unambiguous, and every file it names exists, is complete, and is internally self-consistent after F3's front-matter fix — change classification, the construction workflow (including its ordering rationale), agent execution rules, and the verification enforcement map are all directly actionable without needing any file outside `.sde/`. The limitations are real but do not block starting correctly: several documents are explicitly self-labeled provisional/experimental (accurately, not a defect), and a number of *inline prose* citations point at canonical-repository-only material (deeper evidence, governance "Definition of Done," and a designed-but-not-yet-run validation trial) that a diligent agent might try and fail to open. None of those unreachable citations are required to correctly classify a change, choose an implementation order, or know when to stop.

## Acceptance checklist

- [x] Canonical SDE source remains authoritative.
- [x] Distribution is generated rather than independently maintained.
- [x] Build is deterministic.
- [x] `init` works in a clean generic repository.
- [x] Repeated `init` is safe.
- [x] `status` accurately detects modification/missing files.
- [x] `verify` independently agrees with file hashes (confirmed via a separate, non-CLI script).
- [x] `verify` returns correct exit codes (including the previously-crashing symlink case, now fixed).
- [x] `update` safely updates unmodified installations.
- [x] `update` refuses to overwrite local modifications.
- [x] Downgrade does not occur silently (it is refused with a clear message).
- [x] Manifest paths cannot escape `.sde/`.
- [x] VERSION and manifest remain consistent (and are now checked explicitly, not only via the coincidental per-file hash check).
- [x] Installed package excludes research archive.
- [x] Installed package excludes ROS.
- [x] Runtime dependency count is justified (zero).
- [x] Target application has no SDE production runtime dependency.
- [x] Target application does not require ROS.
- [x] Fresh agent can understand how to begin SDE work (with the documented, residual prose-citation limitation, F3).
- [x] Full relevant test suite passes (23/23).
- [x] npm package contents are correct.
- [x] No secrets/unintended files ship.
- [x] Repository is left clean (verified after every phase of this audit).

## Git result

Commits from this audit (created immediately after this report, in the same working session): a single focused commit containing the F1/F3/F5 fixes and their regression tests, plus this report, attributed to ROS work item `WI-0022`. `git status --short` is clean both before this audit began and after its commit lands; `distribution/dist/` and the one real `npm pack` tarball produced during this audit were never committed.
