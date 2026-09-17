---
id: EX-SDE-2026-0003
title: Rework caused by arriving requirements — conventional against state-structured, measured mechanically
research_area: sde
status: proposed
created: 2026-09-17
author_agent: claude-code
tests_hypotheses: [HY-SDE-2026-0010]
related_theories: [TH-SDE-2026-0003, TH-SDE-2026-0004, TH-SDE-2026-0005]
inputs:
  - EV-SDE-2026-0008
  - EV-SDE-2026-0010
  - kemiller2002/time-entry-state-machine requirements-reconstruction/
  - tools/Ordo.Churn
outputs:
  - research/evidence/EV-SDE-2026-00NN (on completion)
  - research/runs/EX-SDE-2026-0003/run-manifest.json
---

# Experiment

## Research question

When a new or changed requirement arrives against an existing implementation,
how much of the resulting work lands on code that earlier requirements wrote —
and does a state-structured domain differ from a conventional one?

## Why this experiment and not another cost run

Every measurement this programme has taken so far is of a **state**: decision
points in a finished tree, the cost of one frozen mission, discovery rate on a
single change. None measured requirements arriving **over time**, which is the
condition the methodology is claimed to help with.

The existing rework figure for the state-structured arm, `M-TE-CSSTATE-REWORK`
at **0%**, is the absence of a cause: no later requirement arrived during that
trial. The conventional arm's rework was tracked — 32 earlier-requirement
touches across 19 requirements, 25 of them from the second wave — but only as a
**hand-written table in `effort-experiment/EFFORT-LOG.md`**, in a repository
where the whole experiment landed in **one commit**. Those figures cannot be
re-derived, audited, or checked against the tree. Four agent self-reports were
contradicted by measurement in EX-SDE-2026-0002; unauditable hand-classification
deserves the same suspicion.

This experiment produces the same measure mechanically.

## What is frozen, and why it was not chosen by us

**The wave-2 requirement sequence already existed before this experiment was
conceived.** `requirements-reconstruction/fsharp-business-requirements-beyond-csharp.md`
is scoped by its own README as requirements beyond the baseline, with *"every
requirement already listed in `csharp-business-requirements.md` deliberately
left out"*. Nobody selected it knowing it would be used this way, which removes
the largest threat to this design: a wave 2 chosen, consciously or not, to land
where one condition happens to be strong.

Nine requirements, in document order, **two of them changes to existing
behaviour**:

| # | Requirement | Kind |
|---|---|---|
| 1 | Recording granularity | **Changed** |
| 2 | Only a newly-assigned label needs to be active | **Changed** |
| 3 | Business purpose | New |
| 4 | Restoring a voided entry | New |
| 5 | Merging entries | New |
| 6 | Evidence | New |
| 7 | Daily attestation | New |
| 8 | Recording time with a timer | New |
| 9 | Capabilities added | New |

This order is **fixed and will not be varied**. It is the document's own order.

## Conditions

| | Condition A — conventional | Condition B — state-structured |
|---|---|---|
| Start | `c-sharp/` at `73a63f1ac319b97965d4a9e1607a975ebbe2bd81` | to be built, then frozen — see below |
| Size | 389 lines, 4 domain files + spec project | measured once built |
| Scope | wave 1 only; contains no F-series feature | wave 1 only, same acceptance |

**Condition A's baseline already exists and was not made for this experiment.**
It is verified to contain none of `StartTimer`, `MergedFrom`, `AddEvidence`,
`Attest` or `Restore`.

**Condition B's baseline does not exist and must be built. This is the single
largest threat to validity in this design, and it is handled as follows:**

1. It is built by an agent from `csharp-business-requirements.md` under a
   state-structured instruction — **not hand-authored**, and not tuned by
   anyone who has seen wave 2 applied to either condition.
2. It must pass the **same wave-1 acceptance tests** as condition A before it is
   used. A baseline that implements more or less than condition A confounds the
   comparison from the first line.
3. It is committed, published and frozen **before any wave-2 run begins**, and
   its commit SHA is recorded here.
4. If it cannot be made acceptance-equivalent, the experiment does not run. It
   is not adjusted afterwards to fit.

## Procedure

Each run receives the nine requirements **one at a time, in order, with no
look-ahead**, and commits after each.

- One commit per requirement, tagged with its number, and **no other commits**.
  The commit boundary *is* the measurement, so a run that batches requirements
  or slips in an unnamed commit is **void** — see attribution completeness
  below.
- Telemetry markers are read **per requirement**, not per run. A worker restart
  therefore voids the requirement it hit, not the whole run — the correction to
  EX-SDE-2026-0002's criterion, which voided 55% of runs by making the unit of
  analysis too large.
- The agent may not read the other condition's tree, any finished
  `effort-experiment/` arm, `f-sharp/`, or `EFFORT-LOG.md`. These contain the
  answer.
- The wave-1 acceptance suite must pass after every requirement.

## The measure

`tools/Ordo.Churn`, whose definition is stated in `Churn.fs` and pinned by six
hand-counted tests:

> For a commit C with parent P, every line C removes or replaces sat in P.
> `git blame` on P names the commit that first wrote that line. If that commit
> is the baseline or an earlier requirement, the line is **rework**. Lines C
> adds that replace nothing are **new work**.

Rework is attributed to the requirement that **originally wrote** the line, not
the one that changed it, so "requirement 5 reworked requirement 2" means
requirement 5 changed lines requirement 2 wrote.

### Attribution completeness is a run-validity criterion

Discovered while building the tool, by running it against real history rather
than fixtures:

> A run is **void** unless `unattributed_lines` is **zero** for every
> requirement.

`git blame` names the commit that last wrote a line. A line the tool cannot
trace to the baseline or to a named requirement is not counted as rework — so an
incompletely-named history reports *less* rework than occurred, and reports it as
a clean zero. The first version of the tool did exactly this, returning 0% rework
for a wave that plainly removed seventeen lines.

Two consequences, both binding on the procedure:

1. **Every commit between the baseline and the last requirement must be a named
   requirement.** No hotfix commits, no stray edits, no merges. A run that
   produces an unnamed commit is void.
2. **Lines predating the baseline commit are baseline work.** A starting tree's
   lines were written across the whole history behind it, not by the single
   commit a run starts from, so the tool treats any ancestor of the baseline as
   the baseline. Without this, all rework against the starting tree would
   disappear.

The tool exits non-zero and prints a warning when attribution is incomplete, so
this cannot pass unnoticed.

Primary figure: **rework share** — rework lines over all lines touched — because
requirements differ in size and a raw count rewards whichever happened to be
bigger.

**What this does not capture, stated before any number exists.** A line left
alone is not necessarily untouched work: a requirement can invalidate earlier
code semantically while editing none of it, and an edit can be cosmetic. Line
churn is a proxy for rework, not rework itself. It is used because it is
mechanical and auditable, which the figures it replaces are not.

## Sample size, fixed before any run

**n = 4 runs per condition.** Not "until significant", not "until the direction
is clear". All planned runs execute regardless of interim figures. With nine
requirements per run this yields 36 rework observations per condition, which is
why n=4 is defensible here where it was marginal for a single-figure cost run.

## Falsification criteria

- **Not supported** if the conditions' rework shares span zero, or the direction
  is inconsistent across runs, or the between-condition separation does not
  exceed the within-condition spread.
- **Contradicted** if the state-structured condition reworks consistently more.
- **Supported, scope-limited** only if direction is consistent across all four
  pairs and separation exceeds within-condition spread.

**Secondary, separately falsifiable:** the two **Changed** requirements force
more rework than the seven **New** ones, in both conditions. If this fails, the
measure is probably not capturing what the word rework means, and that finding
is reported whatever the primary result says.

No effect size will be claimed beyond this domain and this requirement sequence.

## Known limitations, recorded before running

- **The requirements are reverse-engineered from implementations**, and
  asymmetrically: the baseline document from the C# implementation, the
  extension document from the F# one. Wave 2 may therefore follow F#-shaped
  seams. The reconstruction README states the documents describe rules rather
  than code structure, which mitigates this and does not remove it. **This
  asymmetry favours the state-structured condition if it bites at all**, and is
  recorded here so that a supported result is read with it in view.
- **Condition B's baseline is made for the experiment**; condition A's is not.
- **One domain, one requirement sequence, one model family.**
- **The orchestrator has seen the conventional arm's hand-kept rework table** and
  cannot unsee it. Stated as an unfixable property of the design.
- **Line churn is a proxy**, as above.

## Results

Pending. Not started.

## Registry updates required

- On completion: `EV-` record, and update `HY-SDE-2026-0010` from untested to
  whatever the data supports.
- If the result is null or reversed, `M-TE-CSSTATE-REWORK`'s presentation on the
  public site must be revisited: it currently shows **0%** in a section headed
  "Requirements-first construction held up", with the disqualifying limitation
  one click away rather than on the page.
