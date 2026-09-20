---
id: EV-SDE-2026-0011
title: Pre-upgrade executable Ordo and SDE distribution baseline for GH-18
research_area: state-directed-engineering
evidence_type: primary
source_title: GitHub Actions baseline for executable Ordo and @echelon-foundry/sde
source_author: GitHub Actions
source_uri: https://github.com/kemiller2002/state-directed-engineering/actions/runs/35493473648
source_date: 2026-09-20
retrieved: 2026-09-20
created_by_agent: ChatGPT
confidence: high
supports: []
contradicts: []
related_theories: []
tags: [ordo, baseline, gh-18, tests, distribution, four-tier]
---

# Evidence Record

## Evidence summary

Before implementing `RP-SDE-2026-0004`, the existing executable Ordo and SDE distribution were checked against their current CI baselines.

The current governance branch is based on main commit `ccd363e64928f45af4668876584a4a73c796b5a6`.

Executable Ordo source is unchanged from commit `e82cbe9e6416d460ec1ec5b454874ad3f54a4072`. GitHub's compare endpoint reports that the four commits between those points change only:

- `research/packages/RP-SDE-2026-0004--ordo-next-pass-requirements.md`
- `registries/research-packages.json`

No executable Ordo, SDE Core, method, doctrine, or distribution source changed between the executable baseline and this governance intake.

## Exact claim supported or contradicted

Supported:

> The pre-upgrade executable Ordo and SDE distribution are mechanically green at the baseline used to begin GH-18, subject to the explicitly skipped live-provider test and the limitations below.

This does not support any claim that the new RP-SDE-2026-0004 requirements are correct or that their implementation will improve outcomes.

## Source provenance

### Current ROS / Ordo validation

GitHub Actions run:

- run id: `35493473648`
- workflow: `ROS validation`
- commit: `ccd363e64928f45af4668876584a4a73c796b5a6`
- runner: Ubuntu 24.04 GitHub-hosted runner
- Node: 22
- .NET: 8.0

Observed steps:

- `./ros registry check`: passed
- `./ros validate`: passed
- `tests/Ordo.Complexity.Tests`: 28 passed, 0 failed, 0 skipped
- `tests/Ordo.Churn.Tests`: 9 passed, 0 failed, 0 skipped
- `tests/Ordo.Tests`: 96 passed, 0 failed, 1 skipped, 97 total

The skipped test was:

`Ordo.Tests.LiveAdapterTests.a live provider answers the bounded contract within its choice space`

The repository workflow intentionally skips that test unless live-provider testing is enabled.

### Distribution baseline

GitHub Actions run:

- run id: `35381674083`
- workflow: `SDE distribution`
- commit: `e82cbe9e6416d460ec1ec5b454874ad3f54a4072`
- conclusion: success

Observed evidence:

- `tests/Sde.Core.Tests`: 99 passed, 0 failed, 0 skipped
- build-all-runtimes-and-pack job: passed
- documented quick-start against packed artifact: passed
- packed artifact exercised successfully on:
  - Ubuntu / Node 18
  - Ubuntu / Node 20
  - Ubuntu / Node 22
  - macOS / Node 22
  - Windows / Node 22

Artifact under test was `@echelon-foundry/sde` 1.2.0.

## Relevant excerpt or data

| Surface | Result |
|---|---:|
| ROS registry check | pass |
| ROS validate | pass |
| Ordo complexity tests | 28 / 28 |
| Ordo churn tests | 9 / 9 |
| Ordo core/decision/provider tests | 96 pass, 1 intentional skip / 97 |
| SDE Core tests | 99 / 99 |
| Packed-artifact OS matrix | 5 / 5 jobs pass |
| Documented quick start | pass |

## Interpretation

This provides a clean pre-upgrade line.

If GH-18 later introduces failures, dependency inversions, changed decision semantics, distribution regressions, or four-tier violations, they can be compared against a mechanically green baseline instead of inferred from checked-in test source alone.

It also separates two kinds of evidence:

- repository research inspected checked-in behavior before implementation;
- implementation baseline was actually executed by CI.

## Limitations

- The live Anthropic/provider integration test did not run and is not claimed as baseline evidence.
- The SDE distribution run is attached to executable-Ordo commit `e82cbe9...`, not the later governance-only commit. GitHub compare confirms no executable/method/doctrine/distribution files changed between those commits.
- Passing tests establish regression baseline behavior, not real-world correctness of the new research candidates.
- No TypeSafe/Jev benchmark numbers are reproduced or implied by this baseline.

## Counterevidence

The first two validation attempts after the requirements intake were red for process reasons:

1. the generated research-package registry was stale;
2. the new RP incorrectly used status `candidate`, which is not legal for RP records.

Those defects were corrected before this baseline was accepted. They are evidence that ROS validation is actively enforcing repository governance rather than evidence against executable Ordo semantics.

## Reproduction or verification notes

Current ROS/Ordo baseline commands are encoded in `.github/workflows/ros-validation.yml`.

Distribution/package baseline commands are encoded in `.github/workflows/sde-distribution.yml`.

For implementation completion, GH-18 should re-run the same suites plus any new requirement-specific tests and architecture checks introduced by the governance pass.
