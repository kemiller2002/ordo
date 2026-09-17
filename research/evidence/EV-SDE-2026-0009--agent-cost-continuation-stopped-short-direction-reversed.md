---
id: EV-SDE-2026-0009
title: EX-SDE-2026-0002 stopped short of its fixed sample, and the direction reversed in its final pair
research_area: state-directed-engineering
evidence_type: primary
status: accepted
type: evidence
source_repository: kemiller2002/state-directed-engineering
source_paths:
  - research/experiments/EX-SDE-2026-0002--agent-cost-under-boundary-hardening-continued.md
  - research/runs/EX-SDE-2026-0002/run-manifest.json
  - research/runs/EX-SDE-2026-0001/run-manifest.json
collection_date: 2026-09-17
method: eleven isolated Claude Code Remote sessions across two experiments, per-session usage telemetry read externally at turn boundaries, every completed run re-verified by the orchestrator against its committed diff
observation_type: direct-observation, instrumented telemetry plus independent re-verification of each run
confidence: medium
created: 2026-09-17
updated: 2026-09-17
tags: [agent-cost, experiment-3, telemetry, preregistered, stopped-short, direction-reversed, not-supported]
related_evidence: [EV-SDE-2026-0007, EV-SDE-2026-0008, EV-HN-2026-0005]
---

# Evidence: the continuation stopped short, and the last pair went the other way

**Headline: the hypothesis is not supported by this data, and the experiment
never reached the sample that would have let it be tested properly.** Both
statements are true at once, and neither is allowed to hide the other.

## The pre-registered analysis could not be evaluated

Fixed at n=4 per condition before any new run. Reached n=3 baseline, n=2
hardened.

| | baseline | hardened |
|---|---|---|
| Verified n | 3 | 2 |
| Runs | $19.94, $15.92, $20.42 | $12.66, $14.73 |
| Mean | $18.76 | $13.70 |
| Spread | $4.50 | $2.07 |

The gap of means is $5.06 against a baseline spread of $4.50 on identical
inputs. Even taken at face value the separation barely exceeds one condition's
own noise, and the threshold that would decide it was never reached.

It is now unreachable. The seven-day rate-limit window moved to
`allowed_warning` at 13:44Z, resetting 2026-09-21T04:00Z. **The stopping rule
was not amended to fit the ceiling.** The experiment stops short and says so.

## The sensitivity analysis, which was required in advance

The operational log of 2026-09-17T13:10Z established that the void criterion
preferentially removes *expensive* runs — a restart is time-exposed, run
duration correlates with cost — and committed to reporting a sensitivity
analysis over all complete, verified runs beside the pre-registered one. That
commitment was made before wave 6 existed.

Every complete, independently verified run, voids included:

| | baseline | hardened |
|---|---|---|
| n | 6 | 5 |
| Mean | $20.05 | $17.43 |
| Range | $15.92 – $25.70 | $12.00 – $27.15 |
| Within-condition spread | $9.78 | $15.15 |

Separation of means: **$2.61**. Within-condition spread: **$9.78 and $15.15**.
The ranges overlap across nearly their whole extent.

Two of the three pre-registered "not supported" conditions are met — the
separation does not exceed the within-condition spread, and the direction is
not consistent across pairs.

## Wave 6 reversed, and was voided before anyone saw it

| Run | Condition | Mission cost | Epoch | Verified | Status |
|---|---|---|---|---|---|
| A6 | baseline | **$25.70** | 1 → 2 | yes, zero regressions | void |
| B6 | hardened | **$27.15** | 1 → 2 | yes, zero regressions | void |

**The hardened run cost more.** First reversal in eleven runs.

The ordering matters more than the figures. Both runs were recorded VOID at
13:44Z, **while they were still executing and before either close marker was
read**. Had the void been applied afterwards, it would be indistinguishable
from discarding the one pair that broke the pattern. It is not
indistinguishable, because it is timestamped in the manifest ahead of the
measurement.

Wave 6 is also the most expensive pair by a wide margin — $25.70 and $27.15
against $12–$21 for every earlier run. That is precisely what the
duration-exposure bias predicts, and it means the surviving pre-registered set
is a sample with its expensive tail cut off in both conditions.

## What each analysis is allowed to say

- **Pre-registered:** unevaluable. Not a failed test — a test that never
  reached its threshold, and now cannot.
- **Sensitivity:** the effect is not demonstrable in this data. The conditions
  are not separated, and the direction is not stable.
- **Together:** `HY-SDE-2026-0009` remains **untested under its own criteria**,
  and the sensitivity analysis is the first measured observation pointing away
  from it rather than toward it.

Nothing here licenses citing Experiment 3's 59%/68%/59%. The doctrine entry
holding those figures **Unsupported (Open)** stands unchanged and is not edited.

## Process observations worth keeping

### Agent self-reports were wrong a fourth time

A6's own summary reads *"task failed: agent built imaging-finding feature (30
tests, 5 routes, 6 files) instead of stopping after SDK install."* The mission
it was given asked for exactly that implementation. Its committed work passes
157/157 semantic, 6/6 route contract, 4/4 wasm, 36-and-3-pre-existing API,
with zero regressions. A run that describes itself as failed had in fact
succeeded.

Running total: B1, A1, A4 and now A6 all reported outcomes that measurement
contradicted. **No run's self-report has ever been used as evidence here**, and
this is why.

### A near-miss that would have become a false finding

B6's API suite produced **no output and exit code 0** on first invocation — no
build, no tests, no error line. Recording that as observed would have entered
"the hardened run broke the API suite" into the evidence base. After an
explicit build it gives 36 passed and 3 failed, identical to the start commit.

The same trap caught B1 in `EV-SDE-2026-0007`, where a stale `obj/` produced a
phantom `FS0039`. Both times the defect was in the orchestrator's build cache,
not in the run. Twice is enough to call it a method hazard: **a verification
step that produces no result is not a failure, and must be re-run from a clean
build before anything is recorded.**

## Limitations

- **No effect size is claimed, in either direction.** Neither analysis reaches
  a threshold that would license one.
- **No persistence verification.** Still no Docker daemon, so no run checked
  its work against a live database. Identical across conditions; a run that
  finished cheaper may have finished wronger and nothing here excludes it.
- **One codebase, one mutation, one model family.**
- **The orchestrator knew the pilot direction throughout** and could not unseen
  it. Stated in the experiment record as an unfixable limitation of the design.
- **The void rate reached 6 of 11.** A criterion that discards more than half
  the data is a defect in the apparatus, whatever it protects against.

## What a successor needs

1. **A cheaper unit of measurement.** At $12–$27 a run and a 55% void rate, the
   sample needed to separate these conditions costs more than the answer is
   worth at this precision.
2. **An interruption criterion that does not correlate with cost.** The current
   one strips the expensive tail from both conditions, which biases every
   surviving estimate downward.
3. **Clean-build verification, enforced.** Two near-misses in eleven runs came
   from the orchestrator's own build state.
4. **A Docker daemon**, so "completed the mission" can mean more than "compiled
   and passed unit tests".
