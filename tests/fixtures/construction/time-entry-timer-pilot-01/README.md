# Time-entry timer pilot 01: construction plans B1, B2 and B3

These three files are **agent-produced pilot outputs**, copied byte for byte.
They are evidence of what three independent construction agents actually
wrote. They are not reviewed, approved or authoritative plans, and they are not
Ordo's own fixture plan.

## Provenance

- Repository: `kemiller2002/research-documents`, merged by research-documents
  PR #7 (tracking research-documents#6), commit
  `2edd108eeab4c18639ed4ab45b6d0cc0e8f1cd80`.
- Study: `research/construction-by-design/time-entry-timer-01/`, a
  pre-registered pilot. Each arm-B run wrote `results/<run>/outbox/plan.json`
  through the construction host. See that study's `results.md`.

| File | Source path | SHA-256 of the file |
|---|---|---|
| `B1.plan.json` | `results/B1/outbox/plan.json` | `bf26bd2084968126b9cb5d4b9636aec1842ed0b372d9b8553c264a0bc21ba3a2` |
| `B2.plan.json` | `results/B2/outbox/plan.json` | `0214996d2e6b316199085eb9505cbbe018166ada7f9ba3823b1cf409c6a8ec5a` |
| `B3.plan.json` | `results/B3/outbox/plan.json` | `c9b5c4e9e98dea95de747945f11bd193cbea8c91c76f517c1ba0b5107a682629` |

The tests pin these file hashes, so an edit is caught. Do not edit the files to
make a test pass.

## Binding

All three plans are `ordo.construction-vocabulary/1` and bind:
- source manifest `sha256:03e29eddb6e528baea0707ec6660f18b0495ac8a6424f3c794a53cc2f5925a01`;
- canonical blueprint `sha256:28dd3964f9c49f3492f3c34174ab04d57f4ddcba298276b2a287b4a6f3fdc4cb`;
- cohort `COH-TIMER`, decision `DEC-TIMER-STATES`.

That is the binding of `../time-entry-timer/plan.json`. The tests check it
rather than assume it: each plan validates against the cohort prepared from
`../time-entry-timer/source-manifest.json` and `../time-entry-timer/blueprint.json`.

## What they are used for

`tests/Ordo.Tests/ConstructionPlanConsensusTests.fs` combines them with
`ConstructionPlanConsensus.combine`. The three plans have identical structural
operations and differ only in their `declareUnresolved` questions (2, 3 and 4
of them). Only B2 raised what a failed Stop effect means ("Stop completion
failure"). The consensus keeps all nine questions.
