---
id: EV-SDE-2026-0008
title: F# decision-point complexity added per run across the agent-cost trials — hardened runs added fewer branch points, measured post hoc and F#-only
research_area: state-directed-engineering
evidence_type: derived
status: accepted
type: evidence
source_repository: kemiller2002/helix-note-application
source_paths:
  - research/runs/EX-SDE-2026-0001/complexity/
  - research/runs/EX-SDE-2026-0002/complexity/
  - tools/Ordo.Complexity/Complexity.fs
collection_date: 2026-09-17
method: token-accurate cyclomatic complexity from the F# compiler service lexer, computed as the delta between each run's committed branch tip and its own condition's start commit
observation_type: derived measurement over committed diffs, computed after the runs finished and not part of any pre-registered analysis
confidence: medium
created: 2026-09-17
updated: 2026-09-17
tags: [complexity, cyclomatic, agent-cost, post-hoc, exploratory, not-preregistered, fsharp-only, partial-coverage]
related_evidence: [EV-SDE-2026-0007]
---

# Evidence: decision-point complexity added by each agent-cost run

## Correction, 2026-09-17: this measures F# only, and the runs are not F# only

**Added after the record was first published, in response to the question
being asked directly.** The first version of this record called its figures
"the complexity each run added." They are not. They are **the complexity each
run added in F#**, and every run also wrote SQL that was never counted.

| Run | Cond | SQL files | SQL lines added | Decision-bearing constructs (crude count) |
|---|---|---|---|---|
| A1 | baseline | 1 | 234 | 9 |
| A2 | baseline | 1 | 230 | 9 |
| A3 | baseline | 1 | 236 | 10 |
| A4 | baseline | 2 | 263 | 11 |
| A5 | baseline | 1 | 235 | 10 |
| B1 | hardened | 2 | 258 | 11 |
| B2 | hardened | 1 | 239 | 9 |
| B4 | hardened | 1 | 238 | 9 |
| B5 | hardened | 1 | 228 | 9 |

This is not inert schema. The added SQL declares triggers, `CREATE OR REPLACE
FUNCTION` bodies, `CHECK` constraints and `COALESCE` defaults — decision logic
that happens to live in the database rather than in the domain project. The
counts above come from matching keywords in the added lines, which is exactly
the technique the F# measure was built to avoid, so they are an indication of
scale and **not** comparable to the F# figures. They are given so the size of
the gap is visible rather than implied.

**What the correction does and does not change:**

- It does **not** change the separation. The uncounted SQL is close to uniform
  across every run — 228 to 263 lines, 9 to 11 constructs — and does not
  differ between conditions. It cannot account for baseline's 126–140 against
  hardened's 82–111.
- It does **not** change the density finding. Recomputed with SQL lines in the
  denominator, baseline runs 10.6–12.5 and hardened 9.4–10.8 branch points per
  100 added lines: still overlapping. That ratio mixes F# branch points over
  F#-plus-SQL lines and is therefore incoherent as a measure; it is shown only
  to demonstrate that the conclusion does not turn on the denominator.
- It **does** mean every per-run total in this record is **incomplete**. None
  of them is "the complexity that run added." Each is a lower bound covering
  one of the two languages the run wrote in.

The wider evidence base is further from F#-only still. The effort experiment
underlying this programme's most-cited figures has three arms, two of them in
C# (`effort-experiment/c-sharp`, `effort-experiment/c-sharp-state`) against
one in F# (`effort-experiment/f-sharp-state`). This tool cannot measure those
arms at all, and no cross-language complexity comparison is made anywhere in
this record.

## This measurement was not pre-registered

It was taken after every run in this table had already finished, on a question
nobody wrote down in advance. That places it outside the falsification
criteria of EX-SDE-2026-0001 and EX-SDE-2026-0002, and it is recorded as a
**description of what the runs produced**, not as a test of anything.

Specifically: it **cannot** move `HY-SDE-2026-0009`, which is about monetary
cost and not about complexity, and it is **not** evidence for or against it.
A post-hoc measure that separates the conditions cleanly is exactly the kind
of result that looks like a finding and is not one, so the separation below is
stated and then left alone.

## What was measured

**F# sources only** — `.fs` and `.fsi`. See the correction above for what that
leaves out.

Cyclomatic complexity in McCabe's decision-counting form — one plus the number
of branch points — computed per file from the F# compiler service's own token
stream. A keyword inside a string literal or a comment therefore cannot be
counted. The tool is `tools/Ordo.Complexity`, its rules are stated in
`Complexity.fs`, and its tests in `tests/Ordo.Complexity.Tests` hand-count
every expected value including the two cases where the measure is known to be
wrong.

Counted as a branch point: `if`, `elif`, `when`, `while`, `while!`, `for`,
`|`, `&&`, `||`. `match` is deliberately not counted, because its clauses
already are through `|`.

**The reported figure is not a total. It is a delta**: the complexity of the
files at each run's committed tip minus the complexity of those same files at
that run's own start commit. Absolute totals across conditions would not be
comparable, because Condition A starts at `4879537` and Condition B at
`8d2d789` — two different trees. A file a run never touched contributes zero
by construction.

## Results

Every run that committed anything, void ones included. Voids in this table are
voids under the *cost* experiment's telemetry criterion; that criterion is
about whether a session restarted, not about whether the code is sound, and
each void run below was independently verified as complete with no
regressions.

| Run | Exp | Cond | Cost status | Branch points added | Lines added | Files | Points per 100 lines | Mission cost |
|---|---|---|---|---|---|---|---|---|
| A1 | 0001 | baseline | verified | 140 | 1,019 | 16 | 13.7 | $19.94 |
| A2 | 0001 | baseline | verified | 131 | 863 | 14 | 15.2 | $15.92 |
| A3 | 0001 | baseline | void | 128 | 791 | 13 | 16.2 | $19.49 |
| A4 | 0002 | baseline | verified | 140 | 983 | 15 | 14.2 | $20.42 |
| A5 | 0002 | baseline | void | 126 | 957 | 16 | 13.2 | $18.83 |
| B1 | 0001 | hardened | verified | 111 | 791 | 14 | 14.0 | $12.66 |
| B2 | 0001 | hardened | void | 102 | 746 | 14 | 13.7 | $12.00 |
| B4 | 0002 | hardened | verified | 82 | 638 | 14 | 12.9 | $14.73 |
| B5 | 0002 | hardened | void | 97 | 667 | 14 | 14.5 | $20.62 |

B2r is absent because it never committed. A6 and B6 were still running when
this was measured.

**The ranges do not overlap.** Baseline added 126–140 branch points across
five runs; hardened added 82–111 across four. Restricted to runs the cost
experiment accepted, it is 131–140 against 82–111. This is a wider separation
than the cost measure produced, where the conditions' ranges overlap.

## Three things that cut against reading too much into it

### The difference is volume, not branch density

Branch points per 100 added lines: baseline 13.2–16.2, hardened 12.9–14.5.
**Those ranges overlap.** The hardened runs did not write less tangled code
per line; they wrote **less code**, at a similar density of decisions. Whether
writing less code for the same fixed mission is a virtue or a shortfall is not
settled by this number.

### It is not the cost axis

B5 is the counterexample and it is decisive. At **$20.62** it is the most
expensive hardened run and the second most expensive run of any condition, and
it added **97** branch points — the second-lowest figure in the table.
Whatever drives the run-to-run cost variance that made the cost experiment
inconclusive, it is not simply how much branching the run wrote.

### The two start trees are nearly the same size

An obvious alternative explanation is that the hardened tree already contained
the complexity, so each run had less left to add. Measured across every `.fs`
and `.fsi` file:

| Start commit | Files | Branch points | Lines |
|---|---|---|---|
| `4879537` baseline | 93 | 4,671 | 35,849 |
| `8d2d789` hardened | 95 | 4,718 | 36,262 |

A 1.0% difference in branch points and 1.2% in lines. The hardening mutation
is small, so "it was already there" is not supported at tree scale. It is not
excluded at the scale of the specific files the mission touches, which this
measurement does not isolate.

## Limitations

- **Not pre-registered**, as stated at the top. Nothing here is confirmatory.
- **`|` over-counts.** It fires on discriminated-union case declarations as
  well as on match clauses, so a file declaring many union cases scores higher
  than its control flow warrants. This applies identically to both conditions,
  so it does not bias the comparison made here; it would bias an absolute
  claim, and no absolute claim is made.
- **`| null` is never counted**, in either sense. A lexer cannot tell
  `status: string | null` — a nullable type annotation with no control flow —
  from `| null -> None`, a real match clause. In the one file where this
  mattered, 23 of 24 disputed tokens were annotations and one was a clause, so
  all are excluded. That loses real null-match clauses; the loss falls on both
  conditions alike. It also makes the figures independent of the compiler
  service version: FCS 43.8.400 and the lexer shipping with the .NET 10 SDK
  disagree on these tokens and agree token-for-token once they are excluded,
  which was verified across all nine runs.
- **"Fewer branch points" may mean "did less".** Every run in the table was
  independently verified against its committed diff with no regressions and
  the full verification sequence passing, but the suites did not end at
  identical sizes (A4 reached 154 semantic tests, B4 152). Completeness was
  checked against a fixed sequence, not proven identical in scope.
- **One codebase, one mutation, one model family.** Same scope limit as every
  other result in this programme.
- **One language out of two.** The SQL each run wrote is uncounted, so every
  figure is a lower bound. See the correction at the top.
- **Void runs are included.** They are included because the void criterion
  concerns session telemetry rather than code, and excluding sound code on a
  telemetry ground would be a selection made after seeing the numbers. The
  table marks them so a reader can drop them; the separation holds either way.

## What would make this worth testing properly

Pre-register it. A successor that states the complexity prediction before
running, fixes its sample size, and measures the files the mission requires
rather than every file a run happened to touch would produce a result this
record cannot.
