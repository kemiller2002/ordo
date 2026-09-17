# Decision-point complexity, per file, per run

One CSV per run that committed a branch. Each row is a file the run changed;
the columns are that file's branch-point and line counts at the run's own
condition start commit, at the run's committed tip, and the difference.

`TOTAL` carries the added columns only. Base and head totals are deliberately
blank there: summing absolute complexity across two different start trees
would produce a number that looks comparable between conditions and is not.

Produced by `tools/Ordo.Complexity`:

    dotnet run --project tools/Ordo.Complexity -- \
      --delta <helix-note-checkout> <start-commit> <run-branch>

Start commits: `4879537` for Condition A (baseline), `8d2d789` for Condition B
(hardened). The measure, its exclusions and its two known inaccuracies are
documented in `tools/Ordo.Complexity/Complexity.fs` and pinned by
`tests/Ordo.Complexity.Tests`. The reading of these numbers, including what
they do not show, is `research/evidence/EV-SDE-2026-0008--decision-point-complexity-added-per-run.md`.

**These figures were measured after the runs finished and were not
pre-registered.** They are descriptive. They test nothing.

**They cover F# only.** Every run also added 228–263 lines of SQL holding
triggers, function bodies and `CHECK` constraints, none of it counted here.
Each total is a lower bound over one of the two languages the run wrote in.
The omission is near-uniform across runs and conditions; `EV-SDE-2026-0008`
carries the measured detail.
