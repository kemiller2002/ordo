# Effect outcome policies (vocabulary 5)

Status: **draft / implemented in `Ordo.Core` and `Ordo.Construction`**
(plan vocabulary `ordo.construction-vocabulary/5`, generator
`ordo.construction/5`). Tracking: CONSTRUCT-010 phase 2, increment 2.
Decision:
[`DF-SDE-2026-0026`](../../research/decisions/DF-SDE-2026-0026--state-effect-outcome-policies.md).

## The gap

Vocabulary 2 gives each transition effect one `onFailure`: `remain`,
`proceed` or `unresolved`. It applies alike to a *definite* failure and to an
*unknown* outcome. That conflates two different facts. A definite failure
did not happen, so trying again is safe. An unknown outcome may have
happened, so trying again can apply it twice. Retry, reconciliation,
compensation and escalation could only be written in host code, where nothing
stated or checked them.

## Vocabulary 5

Vocabulary 5 is vocabulary 4 with one change: a `transitionEffect` port no
longer carries `onFailure`. Instead, every effect has exactly one:

```text
declareEffectPolicy { effect, retries: 0..5,
                      onFailed:  remain | proceed | compensate | escalate | unresolved,
                      onUnknown: reconcile | escalate | unresolved, trace }
```

Schema:
[`schemas/ordo-construction-plan.vocabulary-5.schema.json`](../../schemas/ordo-construction-plan.vocabulary-5.schema.json).

### `onFailed`: a definite failure, once retries are spent

| Action | Meaning |
|---|---|
| `remain` | the workflow stays in the source state |
| `proceed` | the target state stands, and the failure is reported |
| `compensate` | host code undoes the effect, and the workflow stays in the source state. Allowed only when the policy's trace cites a bound decision |
| `escalate` | a person decides; no state is chosen |
| `unresolved` | not decided; needs a linked `declareUnresolved` |

### `onUnknown`: an unknown outcome

| Action | Meaning |
|---|---|
| `reconcile` | host code reports whether the effect landed |
| `escalate` | a person decides; no state is chosen |
| `unresolved` | not decided; needs a linked `declareUnresolved` |

A reconciled outcome is then handled by what host code reports:
- `Applied` counts as completed;
- `NotApplied` is a definite failure, with whatever retries remain;
- `StillUnknown` is escalated.

There is no retry for an unknown outcome. The vocabulary has no word for it,
and the generated union has no case for it: `OnUnknown.Retry` fails with
FS0039.

### Validation

Validation refuses:

- `MissingEffectPolicy`: an effect with no policy;
- `UnknownEffect`: a policy for an undeclared effect;
- `RetriesOutOfRange`: retries outside 0 to 5. The reader also refuses this;
- `CompensationWithoutDecision`;
- `UnresolvedEffectWithoutObligation`: an open policy with no linked question;
- `EffectAlreadyDecided`: a linked question on a decided policy;
- `NotInVocabulary`: an `onFailure` on a port in vocabulary 5, or a policy in
  vocabularies 1 to 4.

Earlier vocabularies keep their canonical forms and digests.

### Classification

`effectFailure:C` is classified as follows:

- `unresolved` when either action is `unresolved`;
- `approved` when the policy's trace cites a bound decision (as compensation
  must);
- `required` otherwise.

## Generated code (`ordo.construction/5`)

Tier 3 settles each effect outcome by the plan's policy:

```text
settle:      Completed            → completed
             Failed, retries left → perform again
             Failed               → failed
             Unknown              → reconcile (if the policy says so), else unknown
reconciled:  Applied → completed | NotApplied → settle as Failed | StillUnknown → unknown
```

Results:
- `Accepted`, `Remained`, `Proceeded` and `Compensated` carry `state`.
- `Escalated` and `Unresolved` carry none. Generated code never chooses a
  state for an outcome a person must decide.
- Compensation that does not complete is escalated.

Each result case exists only if some policy can produce it. The same holds
for the ports:
- `Reconcile` exists only when some policy reconciles;
- `Compensate` exists only when some policy compensates.

A host that omits either fails with FS0366.

**Construction obligations:**
- an `IMPLEMENT:` obligation for `I<W>Ports.Reconcile/<Effect>` and
  `I<W>Ports.Compensate/<Effect>`;
- a `RESOLVE:` obligation for an open policy.

The manifest lists `effectPolicies` instead of `effectFailure`.

## Evidence

The real timer cohort is in vocabulary 5 with two test-approved policies:

- **`TimerPolicy`:** 2 retries, `compensate` on the test-approved
  `DEC-TIMER-STOP-FAILURE`, `reconcile`.
- **`TimerEscalate`:** 0 retries, `escalate`, `unresolved` (linked).

Both goldens compile under `TreatWarningsAsErrors`. Scripted-host tests drive
every path and assert:
- the effect is performed exactly 1 + retries times;
- reconciliation is called once and an unknown outcome is never performed
  again;
- compensation leaves the source state, and failed compensation escalates;
- no state is chosen when escalated or unresolved.

Mutation checks: six mutants each failed the suite.

- **ME1:** an unknown outcome is re-performed.
- **ME2:** compensation is allowed without a decision.
- **ME3:** a missing policy is accepted.
- **ME4:** unbounded retries are accepted.
- **ME5:** classification ignores the policy.
- **ME6:** failed compensation reports `Compensated`.

## Not in this increment

- **Default plans.** Decision records cannot state a policy yet, so
  `generateDefaultPlan` does not produce vocabulary 5.
- **Durable idempotency.** Persisted aggregates, revision compare-and-swap,
  and durable versus session idempotency scope come in increments 3 and 4.
  A retry here is safe only because the outcome is a *definite* failure.
