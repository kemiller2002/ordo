---
id: EV-SDE-2026-0007
title: EX-SDE-2026-0001 stopped short — working cost telemetry, a mis-specified acceptance criterion, and no answer on agent cost
status: accepted
type: evidence
source_repository: kemiller2002/state-directed-engineering
source_paths:
  - research/experiments/EX-SDE-2026-0001--agent-cost-under-boundary-hardening-uninterrupted.md
  - research/runs/EX-SDE-2026-0001/run-manifest.json
  - research/runs/EX-SDE-2026-0001/README.md
collection_date: 2026-09-17
method: direct orchestration of six isolated Claude Code Remote sessions, per-session usage telemetry read externally at turn boundaries, every completed run re-verified against its committed diff
observation_type: direct-observation, instrumented telemetry plus independent re-verification of each run's claimed test results
created: 2026-09-17
updated: 2026-09-17
tags: [agent-cost, experiment-3, method-defect, telemetry, preregistered, inconclusive]
related_evidence: [EV-HN-2026-0005]
---

# Evidence: EX-SDE-2026-0001 stopped short of its threshold

**This record establishes nothing about agent cost.** It is filed because
the experiment produced a usable method result and a set of measurements
that would otherwise be lost, and because stopping short is an outcome worth
recording rather than an absence worth hiding.

## What was asked

Does mechanical boundary hardening reduce the monetary cost an agent incurs
implementing an identical frozen mission? Open since Experiment 3
[EV-HN-2026-0005], whose figures were disqualified for covering a resumed
partial run.

## What was run

Six runs against the HelixNote conditions, each in its own session and
container: Condition A from `4879537` (baseline), Condition B from
`8d2d789` (hardened). Setup and mission were separate turns, with usage read
externally at each idle boundary, so setup cost falls outside the reported
figure.

| Run | Condition | Mission cost | Cache reads | Output tokens | Worker epoch | Completed | Status |
|---|---|---|---|---|---|---|---|
| A1 | baseline | $19.94 | 30.26M | 81,223 | 1 | yes | verified |
| A2 | baseline | $15.92 | 23.90M | 72,803 | 1 | yes | verified |
| B1 | hardened | $12.66 | 17.62M | 63,625 | 1 | yes | verified |
| A3 | baseline | $19.49 | 30.61M | 69,014 | 2 | yes | void, contested |
| B2 | hardened | $12.00 | 17.30M | 55,935 | 2 | yes | void, contested |
| B2r | hardened | $14.09 | 20.50M | 65,828 | 2 | **no** | void, uncontested |

Token classes are reported separately and never summed. Setup costs, all
excluded, ranged $0.25–$0.36.

## Why this does not answer the question

The verified set is **two baseline runs and one hardened run**. The
pre-registered criteria require at least three per condition, consistent
direction, and separation exceeding within-condition spread. With one
hardened run there is no within-condition spread to compare against, so the
test cannot be evaluated at all — it is not that it failed.

The decisive number is not the gap between conditions. It is that the two
verified baseline runs span **$15.92 to $19.94, a $4.02 spread** on
identical inputs — roughly a fifth of their own magnitude. The gap between
conditions across all completed runs is $3.25 to $7.28. **One condition's
run-to-run noise is the same order as the apparent effect**, which is
precisely the situation a three-run threshold exists to resolve and this
experiment did not reach.

Every completed run, contested ones included, placed baseline above
hardened. That is six for six in the same direction, it agrees with
Experiment 3's disqualified figures, and it remains **suggestive and
insufficient**. It is recorded here so a future reader can see what was
observed, not so it can be cited.

`HY-SDE-2026-0009` therefore remains **untested**, and the doctrine entry
holding Experiment 3's cost reduction **Unsupported (Open)** stands
unchanged. Nothing measured here moves it.

## What this evidence does establish

### 1. The measurement apparatus works, and its shape is now known

Three facts about per-session telemetry, verified on a throwaway probe
before any run, and load-bearing for any successor:

- A session **cannot read its own usage**; the orchestrator must read it
  from outside. An earlier draft of the method had each run taking its own
  readings, which would have produced no start markers across all six runs.
- Usage is **frozen while a turn runs** and updates at idle, so readings
  land on turn boundaries only.
- Usage **accumulates across turns**, so the difference between two
  readings is that turn's isolated cost (probe: $0.584559 → $0.759935, every
  token class rising).

This is the structural fix Experiment 3 lacked: two conditions in separate
sessions cannot mix figures.

### 2. The acceptance criterion is mis-specified

The criterion voided a run that did not complete "without a rate-limit
interruption or resume", checked against the session event record. In
execution:

- **The event record is unreachable.** No `list_events` tool exists in the
  orchestrating session, so absence of interruption cannot be established.
  Only `worker_epoch` is observable.
- **Epoch increments are routine and benign here.** They appeared in three
  of six mission turns. They cannot cause the defect the criterion guards
  against, because usage is server-side and cumulative and the start marker
  precedes the mission. B2 carried an increment while completing a full
  implementation (15 files, 1,002 insertions, pushed) at a cost beside B1's.

The criterion therefore disqualifies runs for an infrastructure event rather
than for the failure it was written to exclude. **It was not amended.** The
experiment record freezes at first run, and loosening a pre-registered
criterion mid-flight to recover data is the failure this programme exists to
prevent. The defect is recorded for a successor to fix before running.

Recorded beside it because it cuts against the convenient answer: the
contested runs are **A3 at $19.49 (baseline)** and B2 at $12.00 (hardened).
Admitting them adds a high figure to baseline and a low one to hardened, so
the amendment under consideration is not one that quietly favours the
hypothesis.

### 3. A failure mode the design never anticipated

B2r was **blocked by a permission classifier on `git commit`** with 15 files
staged. It implemented the mission and ran the verification sequence, then
could not commit or push, so it fails the criterion requiring a completed
mission verified against a committed diff. Its $14.09 is the cost of
unfinished work. A successor must either pre-authorise the commit path or
treat an uncommitted tree as a recoverable state rather than a lost run.

### 4. Agent self-reports did not match measurement

Every completed run was re-verified by re-running the fixed sequence against
its committed diff rather than trusting its report. This was not ceremonial:

- B1 reported "96/99 tests pass". Measurement: 158/158 domain, 6/6 route
  contract, 36 passed and 3 pre-existing failures in the API suite.
- A1 reported "27/62 tests pass, 1 pre-existing lab_result failure".
  Measurement: 157/157 domain, zero regressions, the same 3 pre-existing
  Docker failures.

One near-miss ran the other way and is recorded because it nearly became a
false finding: B1's API suite first failed to compile with `FS0039` in a
file B1 never touched. That was a stale local `obj/` directory. After a
clean restore it gave 36 passed and 3 failed, identical to the start commit.
Stopping at the first result would have recorded "the hardened run broke the
API suite".

No verified run regressed any test. Each added tests — the domain suite went
from 149 at both start commits to 157 (A1), 160 (A2) and 158 (B1) — so no
run was cheaper by doing less.

## Limitations

- **No effect size, no direction, no answer on cost.** The set is
  underpowered and the experiment stopped before its threshold.
- **No persistence verification.** No Docker daemon was available, so no run
  could check its persistence work against a live database — the mechanism
  that has repeatedly caught the semantic no-op in this evidence base after
  everything else passed. This was identical across conditions, so it does
  not bias the comparison, but a run that finished cheaper may have finished
  wronger and nothing here excludes that.
- **One codebase, one mutation, one model family.** Every agent came from
  one model family; two runs agreeing may be one blind spot rather than two
  measurements.
- **Deliberate deviation from Experiment 3.** Agents were forbidden from
  reading `experiment-3-predictions.md` and the metrics schema, which sit in
  their trees and which the original agents could plausibly see. Applied
  identically to both conditions, so internal validity is unaffected;
  recorded because it is a difference from the original.
- **Pairs ran concurrently** rather than in counterbalanced sequence. This
  removes the ordering confound rather than balancing it, and is a change
  from the registered procedure, recorded as such.

## What a successor needs

1. Replace the interruption criterion with one that names the defect —
   telemetry that fails to span the mission — and a check that can actually
   be performed with available tools.
2. Pre-authorise the commit and push path.
3. Budget for run-to-run variance of at least $4 on a ~$16–20 mission, which
   sets the sample size honestly rather than assuming three runs suffice.
4. Provide a Docker daemon, so "completed the mission" can mean something
   stronger than "compiled and passed unit tests".
