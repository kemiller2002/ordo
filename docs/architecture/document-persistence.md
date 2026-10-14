# Document persistence (vocabulary 7)

Status: **draft / implemented in `Ordo.Core` and `Ordo.Construction`**
(plan vocabulary `ordo.construction-vocabulary/7`, generator
`ordo.construction/7`). Tracking: CONSTRUCT-010 phase 2. Decision:
[`DF-SDE-2026-0028`](../../research/decisions/DF-SDE-2026-0028--persist-many-instances-in-one-document.md).

## The gap

Vocabulary 6 persists one instance per command: load, decide, save. Some
products keep many instances in one stored document instead:

- commands change the document in memory;
- the document is written only by an explicit save, under one operation id,
  expecting the version last read;
- a save whose outcome is unknown is reported as unknown. It is settled by an
  explicit reconcile that inspects the stored document, and no new change is
  made before that.

That architecture cannot be expressed with one instance per command, so it is
a vocabulary of its own. It is not a variant of vocabulary 6.

## Vocabulary 7

Vocabulary 7 is vocabulary 6 plus at most one:

```text
declareDocument { idempotency:      durable  | session  | unresolved,
                  onSaveConflict:   refuse   | escalate | unresolved,
                  onReconcileMoved: conflict | escalate | unresolved, trace }
```

`onSaveConflict` says what a save against a moved document means.
`onReconcileMoved` says what reconcile reports when the stored document moved
without the unknown save's operation. These are separate choices because a
product can decide the first and leave the second open: whether a moved
version means another writer won, or that the save landed and was then
overwritten, is a question of its own.

Only a durable record can attribute a moved version, so `onReconcileMoved`
takes effect in durable scope. Session scope must state `escalate`.

It also adds the `assignRefusalCode` target `saveConflict`: a save refused
because another writer moved the document. With a document, `notFound` is a
command for an entry the document does not hold, and `staleRevision` is a
command against an entry revision other than the one the caller acted on.

Schema:
[`schemas/ordo-construction-plan.vocabulary-7.schema.json`](../../schemas/ordo-construction-plan.vocabulary-7.schema.json).

Validation refuses:

| Violation | Cause |
|---|---|
| `DuplicateDefinition "document"` | a second document declaration |
| `PersistenceAndDocument` | a document together with `declarePersistence` |
| `PersistenceWithEffects` | a document together with transition effects (a dual write) |
| `CodeOnNonRefusal` | a `saveConflict` code unless `onSaveConflict` is `refuse` |
| `ReconcileMovedWithoutDurableScope` | session scope with `onReconcileMoved` other than `escalate` |
| `NotInVocabulary` | `declareDocument` or a `saveConflict` target in vocabularies 1 to 6 |

**Choices that cannot be expressed.** Merging with another writer's document
and overwriting it have no vocabulary. Retrying an unknown save without
reconciling is not a generated path.

**Classification.** `document:idempotency`, `document:onSaveConflict` and
`document:onReconcileMoved` are separate choices. The last one does not arise
in session scope. Each is `unresolved` when open, and is otherwise classified
by its trace. An open choice keeps the plan from being `complete`, and
construction adds `RESOLVE-DOCUMENT:idempotency`,
`RESOLVE-DOCUMENT:onSaveConflict` or `RESOLVE-DOCUMENT:onReconcileMoved`.

## Generated code: `<W>.Document.g.fs` (Tier 3)

The file is generated only when the plan declares a document. It contains:

- `<W>Id` and `<W>OperationId`, the caller's identities of an entry and of a
  save;
- `<W>Entry`: `{ Id; Revision; State }`, one workflow instance;
- `<W>Document`: `{ Version: int64 option; Entries }`, plus, in durable scope
  only, `Applied: Map<OperationId, int64>`. The store keeps `Applied`: every
  operation that landed, with the version it produced;
- the host port `I<W>DocumentStore`:
  - `Read() -> Found | Missing | ReadFailed`. A failed read is not a missing
    document;
  - `Write(operation, expected: int64 option, document) -> Written version |
    VersionMismatch current | WriteFailed { Reason; ProvenNotApplied }`.
    `expected` is None to create the document;
- `<W>DocumentSession`: `{ Stored; Working; Pending }`, plus `Confirmed` in
  session scope. `Stored` is what was last read or confirmed written,
  `Working` holds changes made in memory, and `Pending` is a save whose
  outcome is unknown.

`<W>Documents` holds pure functions over a session and the store port:

| Function | Reports |
|---|---|
| `load` | `Loaded`; `DocumentMissing`, which starts an empty document that the first save creates; `LoadFailed` |
| `create session id` | `Created`, an entry at the initial state; `AlreadyExists`; `Unreconciled` |
| `apply rules session id expected command` | `Applied(session, decision)`, decided through Tier 2; `Refused`; `EntryNotFound`; `EntryStale(id, expected, current)`; `Unreconciled` |
| `save store session operation` | see below |
| `reconcile store session operation` | see below |

**Save.** A save writes the working copy once, expecting the stored version.
- `Written`: the save reports `Saved(operation, version)`.
- An operation that already landed reports `AlreadySaved(operation, version)`
  with the version it produced, and writes nothing. In durable scope the
  check reads `Applied` on the stored document, so it holds after reload.
  In session scope it reads the session's `Confirmed`.
- `VersionMismatch` follows `onSaveConflict`: `SaveConflict current`,
  `Escalated` or `Unresolved`.
- `WriteFailed`, when proven not applied, reports `NotSaved`: a definite
  failure.
- Any other `WriteFailed` reports `OutcomeUnknown(operation)` and sets
  `Pending`. While a save is pending, `create`, `apply` and `save` all return
  `Unreconciled pending` and change nothing.

**Reconcile.** A reconcile reads the stored document and writes nothing.

| Stored document | durable | session | unresolved |
|---|---|---|---|
| the read fails | `StillUnknown`; stays pending | `StillUnknown` | `StillUnknown` |
| it records the operation | `Applied version`; the session adopts the stored document | `Applied version`, when the session confirmed it | (not expressible) |
| its version is unchanged | `NotApplied`; the caller may save again | `NotApplied` | `NotApplied` |
| its version moved without the operation | per `onReconcileMoved`: `ReconcileConflict current`, `Escalated` or `Unresolved` | `Escalated`: the session cannot tell | `Unresolved` |

The moved-version case is the plan's typed choice, not a fixed rule.

`commandCode` maps a command outcome to the plan's codes: the Tier 2 refusal
code, `notFound` and `staleRevision`. `saveCode` maps `SaveConflict` to the
`saveConflict` code.

Construction emits `IMPLEMENT:I<W>DocumentStore.Read` and
`IMPLEMENT:I<W>DocumentStore.Write`. The manifest lists `document`.

## Evidence

The goldens are synthetic gates kept in one yard document
(`tests/fixtures/construction/gate-document-synthetic`):
- `GateYard`: durable scope; save conflicts refused; codes `NoSuchGate`,
  `StaleEntry` and `YardMoved`;
- `SessionYard`: session scope; save conflicts escalated;
- `OpenYard`: both choices unresolved.

All three compile under `TreatWarningsAsErrors`.

`DocumentTests` uses an in-memory store that records landed operations with
the versions they produced, and scripts write and read faults. It asserts:

- a missing document starts empty, and its first save creates it expecting no
  version;
- commands change only the working copy, and one save writes once;
- a missing entry, a stale entry revision and a refused command each carry
  their codes;
- a save that already landed reports its original version after reload and
  in the same session, and writes nothing;
- an unknown save is reported as unknown and blocks `create`, `apply` and
  `save` until reconciled;
- reconcile reports:
  - `Applied` when the operation landed, after which a retry writes nothing;
  - `NotApplied` when it did not, after which a retry writes exactly once;
  - `ReconcileConflict` when another writer won;
  - `StillUnknown` when the read fails, and the save stays pending;
- a moved version is refused with its code, and a proven failure is
  `NotSaved`;
- session scope deduplicates within one session only and escalates what it
  cannot settle;
- an unresolved scope leaves a moved document unresolved and raises all three
  `RESOLVE-DOCUMENT` obligations;
- a decided save conflict (refused, with its code) generates alongside an open
  reconcile-moved case: reconcile reports `Unresolved`, and only
  `RESOLVE-DOCUMENT:onReconcileMoved` is raised.

The compile probes fail as expected:
- there is no `Merged` or `Overwritten` save outcome (FS0039);
- `OutcomeUnknown` carries no version (FS0001);
- a session document has no `Applied` (FS0039);
- an unresolved scope has no `AlreadySaved` and no reconcile `Applied`
  (FS0039).

**Mutation checks.** Each of these generated-code mutants failed
`DocumentTests`. M2, M9, M10 and M15 drop a pending or deduplication check by
matching on a constant.

- **M1:** every write failure is treated as proven.
- **M2:** `save` ignores a pending save.
- **M3:** reconcile ignores a recorded operation.
- **M4:** a moved document reconciles as `NotApplied`.
- **M5:** a stale entry revision is accepted.
- **M6:** durable deduplication is removed.
- **M7:** a failed read reconciles as `NotApplied`.
- **M8:** a confirmed save is not recorded in the session.
- **M9:** `apply` ignores a pending save.
- **M10:** `create` ignores a pending save.
- **M11:** session scope treats a moved document as `NotApplied`.
- **M12:** a conflict loses its code.
- **M13:** an unknown save is not marked pending.
- **M15:** session deduplication is removed.

M14, which writes against `Working.Version`, is equivalent: no function
changes the working version apart from the stored one.

Each of these plan mutants also failed `ConstructionVocabulary7Tests`:

- **P1:** a `saveConflict` code is allowed without `refuse`.
- **P2:** vocabulary 6 accepts a document.
- **P3:** a document is accepted together with persistence.
- **P4:** a `staleRevision` code is refused with a document.
- **P5:** a requirement traced only by a document whose save-conflict choice
  is open counts as constructed.

These mutants of the split between the two choices each failed:

- **Q1:** the `onReconcileMoved` obligation is dropped.
- **Q2:** an open reconcile-moved case generates a conflict.
- **Q3:** session scope classifies the choice.
- **Q4:** session scope accepts any `onReconcileMoved`.
- **Q5:** the reader maps `conflict` to `escalate`.

## Default plans (decision records `ordo.construction-decisions/5`)

Version 5 records generate vocabulary 7 default plans, and only those. Every
other pairing is refused with `DecisionSchemaVocabularyMismatch`. Version 5 is
version 4 plus:

```text
{ kind: "document", idempotency?: durable | session, onSaveConflict?: refuse | escalate,
  onReconcileMoved?: conflict | escalate, requirementKeys[] }
codeAssignments target: { kind: "saveConflict" }
refusalCodes[]: { code, wireText?, requirementKeys[] }
```

**An absent choice is never defaulted.** It generates `unresolved` and an
`undecidedPersistence` obligation whose `planSubject` names the choice:
`document:idempotency`, `document:onSaveConflict` or
`document:onReconcileMoved`. The obligation's extension surface is
`I<W>DocumentStore`.

There is one case where nothing needs to be recorded. In session scope, the
generated reconcile cannot attribute a moved version and always escalates,
so an absent `onReconcileMoved` becomes `escalate` with no obligation. That
value is the only one vocabulary 7 accepts in session scope. Recording any
other value is refused by plan validation (`ReconcileMovedWithoutDurableScope`).

A document record must sit on an approved Decision node and cite keys that
the node owns. A record set holds at most one document
(`MultipleDocumentRecords`). A document together with a persistence record is
refused by plan validation (`PersistenceAndDocument`).

Evidence:
- **The yard's records.** The version 5 records
  (`tests/fixtures/construction/gate-document-synthetic/construction-decisions-v5.json`)
  construct a document tier byte-identical to the hand-authored `GateYard`
  golden, apart from the plan digest. They also construct the same
  `wireText` literals.
- **Save conflict decided, reconcile case open.** With `onReconcileMoved`
  omitted, the plan refuses save conflicts with their code and raises only
  `document:onReconcileMoved`.

Mutation checks: twelve mutants each failed the suite.

- **R1:** an absent scope defaults to durable.
- **R2:** an absent `onReconcileMoved` defaults to `conflict`.
- **R3:** session scope does not fix reconcile to escalate.
- **R4:** version 4 accepts `document`.
- **R5:** version 4 accepts `wireText`.
- **R6:** the writer drops `wireText`.
- **R7:** version 4 accepts a `saveConflict` target.
- **R8:** an absent `onSaveConflict` raises no obligation.
- **R9:** vocabulary 7 pairs with version 4 records.
- **R10:** two document records are accepted. This mutant survived at first;
  a test now adds a second approved decision node in memory.
- **R11:** the generator drops wire text.
- **R12:** session scope still raises `document:onReconcileMoved`.

## Not in this increment
- **Validating the stored document.** An invalid stored document is a
  host-side load failure for now (`LoadFailed`). Field data and document
  validation stay extension contracts.
- **A document with transition effects.** This needs an outbox or an
  equivalent single write.
