---
id: DF-SDE-2026-0016
title: Charter boundary amplification as its own workstream and start with evidence-based detection
status: accepted
type: decision-record
created: 2026-10-05
updated: 2026-10-05
tags: [architecture, bca, gh-52, ordo-qual-001, ordo-qual-008, construction-quality]
supersedes: [DF-SDE-2026-0003]
superseded_by: []
related_documents:
  - research/decisions/DF-SDE-2026-0003--defer-boundary-amplification-optimization.md
  - research/hypotheses/HY-SDE-2026-0004--hardening-reduces-boundary-volume.md
  - research/theories/TH-SDE-2026-0004--detection-vs-construction-optimization.md
  - method/QUALITY-GUARDS-PROPOSAL.md
  - docs/architecture/boundary-amplification.md
  - schemas/ordo-boundary-amplification.v1.schema.json
---

# DF-SDE-2026-0016

## Context

DF-SDE-2026-0003 deferred work on Boundary Change Amplification (BCA) *for
the duration of the State Programming → SDE migration*, and required any
later BCA work to open its own work item and evidence trail. The migration
has finished. Executable Ordo now exists (DF-SDE-2026-0006), and
`MutationBoundary` already models a declared write set, the classification
of observed mutations and an authorized `ScopeExpansion`
(justification + authorizer).

HY-SDE-2026-0004 ("hardening reduces total required boundary changes")
stays **rejected**: three HelixNote experiments measured 4.0 boundary files
per semantic decision whatever the hardening. TH-SDE-2026-0004 holds that
Ordo's leverage is in detection rather than in making boundary changes
cheaper.

New evidence arrived from the portfolio. In Praxis, one requirement ("usage
auto-pause") placed provider HTTP, Keychain access, process RPC,
persistence, locking, sleeping and the domain state machine in a single CLI
module. Across seven commits (kemiller2002/praxis `4948e54`, `3475098`,
`65a5e01`, `fcb53b7`, `d732e20`, `f885d16`, `8d67d12`, 2026-10-03..04) the
work repeatedly modified one orchestration file (`PacingCommands.fs`, six
times) while crossing CLI, persistence, provider, domain and rendering. The
work declared only domain and CLI. No check in place at the time flagged the crossing; a later audit did, and
Praxis PR #169 restored the four-tier placement. Issue GH-52 asks Ordo to supersede the
migration-only deferral with dedicated construction-quality work.
ORDO-QUAL-001 (boundary allocation before implementation) and ORDO-QUAL-008
(responsibility concentration as architecture evidence) propose the method
side.

## Decision

1. **The deferral in DF-SDE-2026-0003 is superseded.** Boundary
   amplification is now a chartered workstream under GH-52, with its own
   evidence trail as DF-SDE-2026-0003 required.
2. **The first increment is detection, not reduction.** Ordo.Core gains
   `BoundaryAmplification` (pure domain) and `BoundaryAmplificationWire`
   (versioned contracts). Together they compare a work item's *declared*
   boundaries with the boundaries its change *observably* crossed and
   report:
   - expected, observed and unclassified paths;
   - unexpected crossings and the amplification ratio;
   - signals ORDO-BA-001..010, each with its evidence;
   - a risk level (`Low | Elevated | High`) derived from signal strength;
   - a decomposition recommendation
     (`NoAction | ConsiderSplitAlong | RequireDesignReview`).

   This makes no claim to lower the 4.0 BCA figure, and HY-SDE-2026-0004 is
   not reopened.
3. **Size is not evidence.** No signal is a file or line count. A large
   change inside one boundary produces no signal; a test asserts this.
4. **Unknown is visible, not failed.** Paths no boundary classifies are
   reported as unclassified (ORDO-BA-010, informational) and never dropped.
   They do not raise the risk.
5. **Approvals reuse `ScopeExpansion`.** An approved expansion must carry a
   justification and an authorizer, and it is validated by
   `MutationBoundary.expand`. When it covers every unexpected crossing, the
   recommendation goes down one step. The crossing, the risk and the
   approval itself stay in the output.
6. **The output is advisory.** `ordo boundary assess` exits 0 for any
   completed assessment, High risk included. A consumer such as Praxis or
   CI decides whether to gate. Non-zero exits mean the arguments were not
   understood (2) or an input was invalid (6).
7. **Host placement.** The executable host is the existing `sde`/`ordo`
   CLI (`src/Sde.Cli`), which now references Ordo.Core. The dependency
   points inward only:
   - Ordo.Core stays pure;
   - Ordo.Core references no Sde or ROS assembly (the architecture tests in
     Ordo.Tests still assert this);
   - all file I/O, including the optional import extraction (`--root`),
     lives in the CLI adapter.

   The trimmed single-file publish builds with no trim warnings.
8. **Configuration is repository-owned and versioned.** The boundary map
   (`ordo.boundary-map/1`), expectation (`ordo.boundary-expectation/1`),
   changed files (`ordo.changed-files/1` or plain text) and optional
   policy (`ordo.boundary-policy/1`) are inputs. Thresholds have defaults,
   and the effective policy and its source are echoed in every result.

## Evidence

- `tests/fixtures/boundary-amplification/praxis-usage-pacing` characterizes
  the incident from the commits above. It yields **High** risk (score 10)
  and `RequireDesignReview` splitting along concurrency, persistence,
  process, provider and rendering. With import evidence removed, the paths
  alone still yield High.
- `tests/fixtures/boundary-amplification/approved-cross-cutting` is a
  legitimately cross-cutting change with an authorized expansion. It reports
  every crossing, with full approval coverage and a recommendation
  downgraded from `require-design-review` to `consider-split-along`.
- `tests/Ordo.Tests/BoundaryAmplificationTests.fs` has 41 tests:
  - a positive and a negative case for every signal;
  - validation, the wire contracts and approvals;
  - determinism under input permutation.
- `tests/Sde.Core.Tests/BoundaryCommandTests.fs` has 12 tests covering
  parsing, exit codes, the JSON contract and import extraction.

The fixtures are characterizations, not validation. They show the detector
flags the motivating case and leaves a single-boundary change alone. They do
not show that the thresholds are right across repositories.

## Consequences

- ORDO-QUAL-001 and ORDO-QUAL-008 move from *proposed* to *partially
  implemented (detection)* in `method/QUALITY-GUARDS-PROPOSAL.md`. The
  method text in CONSTRUCTION-METHOD is not yet changed. Canonical method
  rules wait for evidence from real use.
- `context/KNOWN-RISKS.md` keeps its BCA row. Detection does not solve BCA.
- The charter exclusion in `PROJECT-CHARTER.md` was phase-scoped. This
  record ends it.

## Not decided here (remaining debt)

- The structural exception shape GH-52 asks for (owner, prohibited growth,
  age or expiry, revisit trigger) is not implemented. Approvals carry only
  justification and authorizer.
- BCA as defined in the glossary (boundary files per semantic decision) is
  not computed, because Ordo has no semantic-decision count input yet.
- Threshold calibration across Praxis, Dokimos and Ordo is unmeasured.
- Observation uses paths and imports only. Calls through an abstraction, or
  effects reached without an import, are invisible to it.
- Praxis does not consume the contract yet.

## Reversibility

Reversible. The module is additive, the CLI command is new, and the
contracts are versioned (`/1`). Withdrawing them removes a command and two
Ordo.Core modules, and leaves no persisted state behind.

## Status

`accepted`.
