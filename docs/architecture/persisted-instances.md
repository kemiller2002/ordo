# Persisted instances (vocabulary 6)

Status: **draft / implemented in `Ordo.Core` and `Ordo.Construction`**
(plan vocabulary `ordo.construction-vocabulary/6`, generator
`ordo.construction/6`). Tracking: CONSTRUCT-010 phase 2, increments 3
(persisted aggregates) and 4 (idempotency), built as one protocol. Decision:
[`DF-SDE-2026-0027`](../../research/decisions/DF-SDE-2026-0027--persist-instances-with-stated-idempotency.md).

## The gap

The measured idempotency defect had a single root cause. The check "has this
operation already landed?" consulted session memory only, so a retry after
reload wrote a second time. The runs that passed consulted a durable record
of applied operations.

The repositories surveyed also disagree on two points:
- **Idempotency scope:** durable keys, a request header, or in-memory sets.
- **What a stale revision means:** refuse, rebase, or silently do nothing.

So both choices must be stated decisions, never generator defaults.

## Vocabulary 6

Vocabulary 6 is vocabulary 5 plus at most one:

```text
declarePersistence { idempotency: durable | session | unresolved,
                     onStale:     refuse  | escalate | unresolved, trace }
```

It also adds two `assignRefusalCode` targets:
- `notFound`: a missing instance;
- `staleRevision`: a refused stale write.

Schema:
[`schemas/ordo-construction-plan.vocabulary-6.schema.json`](../../schemas/ordo-construction-plan.vocabulary-6.schema.json).

Validation refuses:

| Violation | Cause |
|---|---|
| `DuplicateDefinition "persistence"` | a second persistence declaration |
| `PersistenceWithEffects` | persistence together with transition effects. The instance and the effect would be two unrelated writes; an outbox comes later |
| `CodeOnNonRefusal` | a `staleRevision` code unless `onStale` is `refuse`, or a `notFound` code without persistence |
| `NotInVocabulary` | any of these in vocabularies 1 to 5 |

**Choices that cannot be expressed.** Rebasing over another writer's change
and silently doing nothing on a stale write have no vocabulary. Retrying an
unknown save is not a generated path.

**Classification.** `persistence:idempotency` and `persistence:onStale` are
separate choices. Each is `unresolved` when open, and is otherwise classified
by its trace. An open choice keeps the plan from being `complete`, and
construction adds `RESOLVE-PERSISTENCE:…`.

## Generated code: `<W>.Persistence.g.fs` (Tier 3)

The file is generated only when the plan declares persistence. Other
workflows keep their exact layout. It contains these types:

- `<W>Id`;
- `<W>OperationId`, absent when the scope is unresolved;
- `<W>Record`: `{ Id; Revision; State }`, plus `Applied: Set<OperationId>`
  in durable scope only;
- `<W>Loaded`: `Found | Missing | LoadUnknown`;
- `<W>Saved`: `Saved | Stale current | SaveFailed | SaveUnknown`;
- the host port `I<W>Store`, with `Load` and a compare-and-swap
  `Save(expected, record)`.

`<W>Persistence.execute` loads the instance, deduplicates, decides through
Tier 2, and then saves:

| Scope | Deduplication | Unknown save |
|---|---|---|
| `durable` | `Applied` on the stored record, so it survives reload and restart | **settled by reading the instance back**, never written again. The operation is recorded → `Executed`. The revision is unchanged → `NotSaved`, so the caller may retry. The revision moved without the operation → another write won the compare-and-swap, so this is a stale write. The instance cannot be read → `Escalated` |
| `session` | a caller-held `seen` set, returned updated. It does not survive reload | `Escalated`. Without a durable record, nothing can settle it |
| `unresolved` | none generated | `Unresolved` |

A definite `SaveFailed` is `NotSaved`. A compare-and-swap `Stale` follows
`onStale`:
- `StaleRevision(expected, current)`;
- `Escalated`;
- `Unresolved`.

`executionCode` maps outcomes to the plan's codes: the Tier 2 refusal code,
`notFound`, and `staleRevision`.

Construction emits `IMPLEMENT:I<W>Store.Load` and `IMPLEMENT:I<W>Store.Save`.
The manifest lists `persistence`.

## Evidence

The goldens are the synthetic gate, persisted:
- `StoredGate`: durable scope, with stale writes refused and codes;
- `SessionGate`: session scope, with stale writes escalated.

Both compile under `TreatWarningsAsErrors`.

Behaviour tests use an in-memory compare-and-swap store with scripted faults.
They assert:

- an executed command is saved once, at revision + 1, with the operation
  recorded;
- **a retried operation never writes twice after reload**, which is the
  measured defect;
- an unknown save that landed is settled by reading back, with no second
  write;
- an unknown save that did not land is `NotSaved`, and a retry applies it
  exactly once;
- an unknown save overtaken by another writer is refused as stale and carries
  its code;
- a missing instance carries its code;
- session scope deduplicates within one session only (a new session writes
  again, as that choice means) and escalates an unknown save.

The compile probes fail as expected:
- `Execution.Rebased` does not exist (FS0039);
- a session record has no `Applied` (FS0039).

Mutation checks: six mutants each failed the suite.

- **MP1:** the durable deduplication check is removed.
- **MP2:** an unknown save is assumed not saved.
- **MP3:** a dual write is accepted.
- **MP4:** a stale code is allowed without `refuse`.
- **MP5:** an open scope is classified as decided.
- **MP6:** persistence is accepted in vocabulary 5.

## Default plans (decision records `ordo.construction-decisions/4`)

Version 4 records generate vocabulary 6 default plans, and only those. Every
other pairing is refused with `DecisionSchemaVocabularyMismatch`. Version 4 is
version 3 plus the following:

```text
{ kind: "persistence", idempotency?: durable | session, onStale?: refuse | escalate, requirementKeys[] }
{ kind: "effectPolicies", policies: [ { command, retries: 0..5, onFailed?: remain | proceed | compensate | escalate,
                                        onUnknown?: reconcile | escalate, requirementKeys[] } ] }
codeAssignments target: { kind: "notFound" } | { kind: "staleRevision" }
```

A version 4 `transitionEffect` port carries no `onFailure`; its policy record
says what a failed effect means.

**An absent choice is never defaulted.** It generates `unresolved` and a typed
obligation:
- `undecidedPersistence`, with `planSubject` `persistence:idempotency` or
  `persistence:onStale`;
- `undecidedEffectFailure`, linked to the effect.

`retries` is required.

Each content kind must sit on an approved Decision node and cite keys that
the node owns. A node may hold one state model or port contract plus one of
each other kind.

The generator refuses the records when:
- an effect has no policy record (`MissingEffectPolicyRecord`);
- a policy names no effect (`PolicyForUnknownEffect`);
- an effect has two policy records (`DuplicateEffectPolicyRecord`);
- the set holds more than one persistence record (`MultiplePersistenceRecords`);
- a persisted workflow also has effects (`PersistenceWithEffects`, through
  plan validation).

Evidence: the persisted gate's version 4 records
(`tests/fixtures/construction/gate-persisted-synthetic/construction-decisions-v4.json`)
construct a persistence tier identical to the hand-authored `StoredGate`
golden, apart from the plan digest. The real timer cohort's records, converted
to version 4 in test code with test approvals, generate the Stop effect policy
as `approved`.

Mutation checks: six mutants each failed the suite.

- **MR1:** an absent scope defaults to durable.
- **MR2:** a missing policy record is accepted.
- **MR3:** an absent `onUnknown` raises no obligation.
- **MR4:** version 3 accepts persistence.
- **MR5:** a version 4 port encodes `onFailure`.
- **MR6:** a policy for an unknown effect is accepted.

## Not in this increment

- **Persistence with transition effects:** an outbox or an equivalent single
  write.
- **Field data on the instance:** domain records and field validation. These
  stay extension contracts, as agreed.
