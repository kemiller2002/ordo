# EX-SDE-2026-0001 — raw run observations

**These are observations, not results.** Nothing here has been adjudicated
against the experiment's acceptance criteria in a way that settles the
hypothesis, and no figure here may be cited as an outcome of
EX-SDE-2026-0001. The experiment is incomplete and one of its acceptance
criteria is under review (see *Open methodological question* below).

They are committed because they were produced in an ephemeral container and
cost real money to obtain. Losing them and re-running would be worse than
recording them plainly with their status attached.

## What was measured

Each run is one condition in its own session and container. Cost is the
difference between two usage readings taken by the orchestrator from
outside the run: a **start marker** after setup idles, and a **close
marker** after the mission's final turn. Setup lands entirely before the
start marker.

| Run | Condition | Mission cost | Worker epoch | Completed + pushed | Status |
|---|---|---|---|---|---|
| A1 | baseline | $19.94 | 1 | yes | verified |
| A2 | baseline | $15.92 | 1 | yes | verified |
| A3 | baseline | $19.49 | 2 | yes | VOID — contested |
| B1 | hardened | $12.66 | 1 | yes | verified |
| B2 | hardened | $12.00 | 2 | yes | VOID — contested |
| B2r | hardened | $14.09 | 2 | **no** | VOID — uncontested |

Exact token-class figures for every run are in `run-manifest.json` (this directory). Token
classes are recorded separately and never summed.

## Independent verification

Every run marked *verified* was re-checked by running the fixed
verification sequence against its committed diff, rather than accepting the
agent's own report. That check was not ceremonial:

- **Both agents' self-reports were wrong.** B1 reported "96/99 tests pass";
  A1 reported "27/62 tests pass". Neither matches measurement.
- **One near-miss in the other direction.** B1's API suite first failed to
  compile with `FS0039`, in a file B1 never touched. That was a stale local
  `obj/` directory, not a defect in the run. After a clean restore: 36
  passed, 3 failed — identical to the start commit. It would have been
  recorded as "the hardened run broke the API suite" had the check stopped
  at the first result.

No verified run regressed any test. Each added tests: the domain suite went
from 149 at both start commits to 157 (A1), 160 (A2) and 158 (B1). The three
failing API tests are pre-existing Testcontainers tests that cannot reach a
Docker daemon; they fail identically at both start commits and in every run.

## Why three runs are void

**B2r is void and the reason is not in dispute.** It was blocked by a
permission classifier on `git commit` with 15 files staged, and never
committed or pushed. It fails the acceptance criterion requiring a completed
mission verified against a committed diff, independently of anything below.
Its $14.09 is the cost of an unfinished run and is not comparable to any
other figure here.

**A3 and B2 are void only under a contested reading**, and that reading is
the open question.

## Open methodological question

The acceptance criteria require a run to have completed "without a
rate-limit interruption or resume", checked against the session's event
record. Two problems emerged in execution:

1. **The event record is not reachable.** No `list_events` tool is available
   in the orchestrating session, so the absence of an interruption cannot be
   established directly. Only `worker_epoch` is visible.
2. **`worker_epoch` increments look routine, and do not imply the defect the
   criterion guards against.** Three of six mission turns showed an
   increment. Experiment 3's defect was that telemetry *began counting*
   after most of the work had already happened. That cannot occur here:
   usage is server-side and cumulative per session, the start marker is
   taken before the mission, and B2 — a run with `worker_epoch: 2` —
   completed a full implementation (15 files, 1,002 insertions, pushed) at a
   cost sitting beside B1's verified figure.

So the criterion as written may disqualify runs for a benign infrastructure
event rather than for the failure it was written to exclude. That is a
defect in the criterion, not in the runs.

**It has not been amended.** The experiment record says it is frozen once
the first run starts, and loosening a pre-registered criterion mid-flight to
recover data is the failure this whole record exists to prevent. The
question is recorded here, unresolved, and any amendment must be made
explicitly, with its reasoning, and dated — never silently.

One fact belongs next to that question, because it cuts against the
convenient answer: **A3, a contested run, is a baseline run at $19.49.**
Admitting the contested runs adds a high figure to the baseline condition
and a low one to the hardened condition. Whatever the amendment decides, it
is not a change that quietly favours the hypothesis.

## What cannot be concluded from this file

- No effect size. No experiment here was powered for one.
- No direction. The pre-registered criteria require consistent direction
  across the full set with separation exceeding within-condition spread, and
  the set is incomplete. The baseline runs alone span $15.92 to $19.94.
- Nothing about persistence correctness. No Docker daemon was available, so
  no run could verify its persistence work against a live database — the
  mechanism that has repeatedly caught the semantic no-op in this evidence
  base after everything else passed. A run that finishes cheaper may have
  finished wronger, and nothing here rules that out.
