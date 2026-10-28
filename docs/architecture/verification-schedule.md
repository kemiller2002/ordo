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
| `stale` | evidence bound to a different run or source commit and not reusable under its policy; evidence contradicting its subject (input digest, plan, rehearsal); or bytes not matching the pinned evidence manifest | `refreshEvidence` |
| `satisfied` | fresh PASS | none |

Several results for one requirement combine: any stale result makes the
obligation stale, then failure, block or missing run wins over a pass. Stale
evidence never satisfies an obligation.

A key of the per-run binding the caller supplies no current value for is not
judged; the CLI therefore requires `--source-sha` and `--run-id` together
with `--evidence-manifest`.

### Freshness bindings per policy (DF-SDE-2026-0026)

Bindings are chosen per `freshness.policy`
([DF-SDE-2026-0026](../../research/decisions/DF-SDE-2026-0026--bind-evidence-freshness-per-policy.md)).
Through Ordo 1.18.0 every policy used the per-run binding.

| Policy | Per-run binding (always) | Content-addressed reuse binding |
|---|---|---|
| `FP-LIVE-PER-COMMIT` | `integrity`, `runId`, `sourceSha` | none: never reused |
| `FP-REVIEW-PER-COMMIT` | `integrity`, `runId`, `sourceSha` | none: never reused |
| `FP-DETERMINISTIC-INPUT-DIGEST` | `integrity`, `runId`, `sourceSha` | `integrity`, `inputDigest`, `planDigest`, `rehearsalId`, only when the plan's `freshnessPolicies[]` declares the policy `"reusable": true` |
| any other id | the plan is refused: `$.requirements[i].freshness.policy: unsupported freshness policy …` | n/a |

A result is fresh when it matches the per-run binding, or when it is
reusable: it carries a value for every reuse key, each equal to a current
value. Reuse is strict where the per-run binding is lenient. A reuse key
absent from the evidence, or without a current value, never grants reuse;
the evidence then falls back to the per-run binding. A reuse key that the
evidence and the subject both carry with different values makes the result
stale even within its own run, because it contradicts its subject.

Where the values come from:

- `integrity`, `runId`, `sourceSha`: as before (pinned manifest digest,
  `run_id`, `source_sha`; current values from `--run-id`, `--source-sha`).
- `planDigest`: evidence `plan_digest` (optional, with or without `sha256:`);
  the current value is the plan's own `planIdentity.digest`.
- `inputDigest`: evidence `input_digest` (optional); the current value per
  requirement comes from `--reuse-context`.
- `rehearsalId`: evidence `rehearsal_id` (optional); the current value comes
  from `--reuse-context`.

`--reuse-context FILE` (`ordo.verification-reuse-context/1`) is the only way
to grant reuse:

```json
{ "schema": "ordo.verification-reuse-context/1",
  "rehearsalId": "rehearsal-005",
  "inputDigests": { "VER-008": "sha256:…" } }
```

**Compatibility.** `causa.acceptance-evidence/1` results written in
Rehearsal 5 carry none of `input_digest`, `plan_digest` or `rehearsal_id`,
so they are never reused and schedule exactly as before. Without
`--reuse-context`, a plan with known policies schedules to the same bytes as
in Ordo 1.18.0. The output schema is unchanged
(`ordo.verification-schedule/1`). The one additive change is that
`mismatchedBindings` may now also name `inputDigest`, `planDigest` or
`rehearsalId`. A plan naming a policy outside the three is now refused,
where 1.18.0 scheduled it with the per-run binding.

### Per-obligation acceptance contract (plan revision 2)

Revision 2 of `indy-init.verification-plan/1` gives every obligation an
`acceptanceContract` whose `freshness.policy` is the strictest of the
requirement policy and the obligation's evidence-category policies
(Indy-init `docs/VERIFICATION-PLAN-CONTRACT.md`, item 10). Ordo reads it
(Echelon development speed optimization, Priority Four):

- An obligation whose contract policy permits no reuse (`FP-LIVE-PER-COMMIT`,
  `FP-REVIEW-PER-COMMIT`, or `FP-DETERMINISTIC-INPUT-DIGEST` not declared
  `reusable`) is judged on the per-run binding only, even when its
  requirement allows content-addressed reuse.
- A contract can only narrow its requirement's reuse, never widen it: a
  deterministic contract under a live requirement stays per run.
- An obligation without `acceptanceContract` (revision 1) inherits its
  requirement, as before.
- A present contract with no `freshness.policy`, or an unknown policy, refuses
  the plan with its JSON path
  (`$.requirements[i].obligations[j].acceptanceContract.freshness.policy: …`).

In the Indy-init v2 plan as committed every obligation's contract policy
equals its requirement's (177 deterministic, 97 live, 52 review), so the
schedule of that plan is unchanged; the rule matters as soon as a category
policy is stricter than its requirement's.

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

## Interpretation of plan dependencies (open question)

The plan's consumer contract (Indy-init `docs/VERIFICATION-PLAN-CONTRACT.md`,
item 8) defines `dependencies.requirements` as the requirements whose change
invalidates a requirement's evidence. Ordo orders by them: settling the
prerequisite first means its dependents' evidence is produced once and does
not go stale. The plan does not claim these are build prerequisites; the
genuine task edges live in `dependency-graph.json`, which this schedule does
not read. Whether Ordo should also order by those task edges is an
unresolved question for the plan owner, not decided here.

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
    --expected-digest <hex> [--evidence-manifest FILE --source-sha SHA --run-id ID \
    [--reuse-context FILE]] [--output schedule.json] [--json]
```

Exit 0 scheduled; 1 refused (unreadable plan, digest mismatch, duplicate ids,
unknown freshness policy, unreadable reuse context); 2 usage. Tests:
`tests/Ordo.Tests/VerificationScheduleTests.fs` (including the freshness
negative controls) and `tests/Sde.Core.Tests/VerificationCommandTests.fs`.
