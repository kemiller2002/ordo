---
id: EX-SDE-2026-0002
title: Agent monetary cost under mechanical boundary hardening — continuation with a corrected interruption criterion
research_area: sde
status: active
created: 2026-09-17
author_agent: claude-code
tests_hypotheses: [HY-SDE-2026-0009]
related_theories: [TH-SDE-2026-0003, TH-SDE-2026-0004, TH-SDE-2026-0005]
inputs:
  - EV-SDE-2026-0007
  - EV-HN-2026-0005
  - research/experiments/EX-SDE-2026-0001--agent-cost-under-boundary-hardening-uninterrupted.md
  - research/runs/EX-SDE-2026-0001/run-manifest.json
outputs:
  - research/evidence/EV-SDE-2026-00NN (on completion)
  - research/runs/EX-SDE-2026-0002/run-manifest.json
---

# Experiment

## Research question

Unchanged from EX-SDE-2026-0001: does mechanical boundary hardening reduce
the monetary cost an agent incurs implementing an identical frozen mission?

## Why a successor rather than a resumption

EX-SDE-2026-0001 froze at its first run and stopped short of its threshold
with n=2 baseline against n=1 hardened [EV-SDE-2026-0007]. Its acceptance
criterion could not be evaluated with available tools and was deliberately
not amended mid-flight. This record carries the corrections that could not
be made there.

## The stopping rule, fixed before any new run

**This is the most important section of this record.** The continuation was
authorised after a pilot whose direction was already visible. That creates a
specific hazard — continuing while results look favourable and stopping when
they do not — which inflates false positives and would make the final figure
less trustworthy, not more.

Three commitments, made now, before any new data exists:

1. **The sample size is fixed at n=4 per condition.** Not "until it reaches
   significance", not "until the direction is clear".
2. **All planned runs execute regardless of interim figures.** No interim
   analysis decides whether to continue. If the third pair reverses the
   direction, the fourth still runs.
3. **The outcome is reported whatever it is**, including a reversal, a null,
   or a spread too wide to separate the conditions. A reversal here would be
   a more valuable result than a confirmation, because the pilot direction
   and Experiment 3's disqualified figures both point the other way.

The prior runs' direction is already known to the orchestrator and cannot be
unseen. That is a stated limitation of this experiment, not something the
design can fix.

## Pooling the pilot runs

A1 ($19.94), A2 ($15.92) and B1 ($12.66) from EX-SDE-2026-0001 are pooled
into this experiment's dataset. The justification is specific, not
convenience:

- Identical start commits, mission text, model, effort, tool surface and
  verification sequence. Nothing about the conditions changed.
- Each read `worker_epoch: 1` at its close marker. Since the counter starts
  at 1 and does not decrease, a close reading of 1 establishes that no
  restart occurred at any point before the close — which satisfies the
  corrected criterion below, not merely the old one.

**A3, B2 and B2r are not pooled.** A3 had a restart observed mid-mission.
B2's restart timing was never established. B2r never completed. Their
figures stay where they are, as observations in the prior record, and are
not averaged into anything here.

So this experiment needs **2 further baseline runs and 3 further hardened
runs** to reach n=4 per condition.

## Corrected criterion

Replacing the criterion that proved unevaluable:

> A run counts only if `worker_epoch` is **read at the start marker and again
> at the close marker, and is unchanged between them**. A change between
> those two readings voids the run. A change observed at any other time —
> including after the close marker, or after the orchestrator archives the
> session — is not evidence about the run and does not void it.

This names something observable with available tools, and it targets the
actual defect: telemetry that fails to span the mission. It follows directly
from the discovery that archiving A1 incremented its epoch after its run had
finished [EV-SDE-2026-0007 §2].

**Corollary, binding on the orchestrator:** do not archive a session before
taking its final reading.

## The commit-path failure

B2r implemented its mission, ran the verification sequence, and was then
blocked by a permission classifier on `git commit` with 15 files staged —
losing a ~$14 run at the final step. Mitigation: the mission prompt states
that committing and pushing to the run's own branch is an authorised part of
the mission, and instructs one plain retry if a first attempt is refused. A
run that still cannot commit is void, and the failure is recorded as an
environment limitation rather than an agent error.

## Everything else is unchanged

Conditions, start commits (`4879537` baseline, `8d2d789` hardened), mission
text, the two-turn setup/mission protocol with externally-read markers,
concurrent pairing, separate token-class reporting, and independent
re-verification of every run against its committed diff. See
EX-SDE-2026-0001 for the full specification; only the criterion, the commit
path and the stopping rule differ.

## Falsification criteria

Unchanged in substance and restated so this record stands alone:

- **Not supported** if the per-run `cost_usd` differences span zero, or the
  direction is inconsistent across pairs, or the between-condition
  separation does not exceed the within-condition spread.
- **Contradicted** if the hardened condition costs consistently more.
- **Supported, scope-limited** only if direction is consistent across all
  pairs and separation exceeds within-condition spread.

The pilot measured a **$4.02 baseline spread** on identical inputs against a
between-condition gap of $3.25–$7.28. That ratio is why n=4 is the floor and
why "not supported" remains a live outcome even if every run repeats the
pilot direction.

No effect size will be claimed. One codebase, one mutation, one model
family.

## Results

Pending.

## Registry updates required

- On completion: `EV-` record, and update `HY-SDE-2026-0009` from untested
  to whatever the data supports.
- `doctrine/CONTRADICTIONS-AND-DEPRECATED.md`: the Experiment 3 entry stays
  as the permanent record of disqualified figures whatever this finds. It is
  not deleted or edited away.
