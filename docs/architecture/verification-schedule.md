# Ordo verification schedule (`ordo.verification-schedule/1`)

Status: **draft / implemented in `Ordo.Core`** (`VerificationSchedule`,
`VerificationScheduleWire`, `VerificationPlanWire`) with the read-only CLI
adapter `sde verification schedule` (`src/Sde.Cli/Verification.fs`).
Prepared for Rehearsal 5, where 98 of 108 P0 requirements ended `NOT_RUN`
because no precise test existed and acceptance work was ordered late.

## Boundary

| Responsibility | Owner |
|---|---|
| Producing the compiled plan (`indy-init.verification-plan/1`) | the requirements compiler in Indy-init |
| Intake, identity (digest recomputation), provenance, handoff | Praxis: `praxis inbox claim` then `praxis inbox admit-plan`, writing `praxis.verification-plan-handoff/1` |
| Decomposition, dependency ordering, priority, parallel waves | Ordo: this module |
| Running tests, judging evidence integrity for acceptance | the verifier (Dokimos / `compute-acceptance-verdict`) |

Ordo does not recompute the plan digest; it checks the plan carries the
digest Praxis admitted (`--expected-digest`) and records it as `planDigest`.
No new orchestration framework is introduced: the schedule is a value.

## Function

```fsharp
schedule : ScheduleContext -> VerificationPlan -> Result<VerificationSchedule, ScheduleError list>
```

Pure. No clock, file or randomness; inputs are sorted ordinally before use,
so input order cannot change the output, and the canonical rendering has no
timestamp: the same plan and context render to the same bytes. The schedule
carries `scheduleDigest`, SHA-256 over its own canonical rendering.

Each obligation is classified:

| State | Meaning | Work |
|---|---|---|
| `testMissing` | the plan states no test is bound | `authorTest` (implementation) |
| `failing` | fresh evidence records FAIL | `fixImplementation` (implementation) |
| `notRun` | test bound, no current result | `runVerification` |
| `blocked` | fresh evidence records BLOCKED | `unblockVerification` |
| `stale` | evidence bound to a different run, source commit, or with bytes not matching the pinned evidence manifest | `refreshEvidence` |
| `satisfied` | fresh PASS | none |

Several results for one requirement combine: any stale result makes the
obligation stale, then failure, block or missing run wins over a pass. Stale
evidence never satisfies an obligation.

Every policy in `indy-init.verification-plan/1` binds evidence to the run and
the candidate source commit, so the binding keys are `runId`, `sourceSha` and
`integrity`. A key the caller supplies no current value for is not judged;
the CLI therefore requires `--source-sha` and `--run-id` together with
`--evidence-manifest`.

## Output

- `acceptanceBlockers`: requirements with a mandatory obligation not
  satisfied. Mandatory means P0 and `exclusion.status = in-scope`; an
  approved exclusion keeps its row but is not a blocker.
- `dependencies` and `unknownDependencies`: edges to requirements inside and
  outside the plan. An edge to a requirement the plan does not contain (for
  example a P1 prerequisite) is reported, never dropped.
- `cycles`: strongly connected components (Tarjan) with more than one
  member, or a self-edge. Members and anything behind them are listed in
  `cycleBlocked`; nothing fails silently and nothing disappears.
- `implementationTasks`, `requiredVerification` (every unsatisfied
  obligation), `staleEvidence`.
- `independentWork`: Kahn waves; requirements within one wave have no
  dependency on each other and may run in parallel.
- `requirements`: one row per plan requirement with `rank` and `score`.

## Priority heuristic

1. `mandatoryUnblocked` descending: P0 obligations of the requirement plus
   those of every transitive dependent, i.e. how much mandatory acceptance
   finishing it unblocks;
2. `transitiveDependents` descending;
3. requirement id, ordinal ascending.

The heuristic orders; it never excludes. Tests assert that every plan
requirement has exactly one row, a rank in `1..n` and exactly one place in a
wave or in `cycleBlocked` (including a 120-requirement generated plan with
cycles and unknown dependencies).

## Usage

```bash
sde verification schedule --plan verification-plans/VP-….verification-plan.json \
    --expected-digest <hex> [--evidence-manifest FILE --source-sha SHA --run-id ID] \
    [--output schedule.json] [--json]
```

Exit 0 scheduled, 1 refused (unreadable plan, digest mismatch, duplicate
ids), 2 usage. Tests: `tests/Ordo.Tests/VerificationScheduleTests.fs`.
