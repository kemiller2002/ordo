# Ordo default construction plan (`ordo.default-plan/1`)

Status: **draft / implemented in `Ordo.Core`** (`ConstructionDecisions`,
`ConstructionDefaultPlan`, `ConstructionLayout`). Tracking: CONSTRUCT-008,
mechanical-first construction. Builds on the
[construction plan](construction-plan-v1.md) and the
[construction engine](construction-engine-v1.md).

## Why

An ECIR blueprint records decision and interface nodes with prose
justifications only. In pilot 01 (time-entry timer) every agent re-derived the
same five transitions and two ports by hand, and hand-copied binding digests
into its plan (finding F1). Mechanical-first construction moves that work to
Ordo. Ordo generates the plan from approved inputs; an agent only meets the
obligations Ordo could not discharge.

## Inputs

```fsharp
generateDefaultPlan :
    PreparedCohort -> ApprovedDecisionRecords -> ConstructionVocabulary
    -> Result<DefaultPlan, DefaultPlanRefusal list>
```

- `PreparedCohort`: from `ConstructionIr.prepareCohort` (manifest, blueprint,
  cohort, externally authorized decisions).
- `ApprovedDecisionRecords`: an `ordo.construction-decisions/1` set, read by
  `ConstructionDecisions.readDecisionRecords`. An operator authors and
  approves it once. A host binds `ConstructionDecisions.digest` into the
  signed grant.
- `ConstructionVocabulary`: `Version1` or `Version2`
  (`ConstructionVocabulary.ofVersion` refuses any other version).

Nothing else is read. No caller supplies the plan's binding: Ordo computes it
from the prepared cohort.

## Wire: `ordo.construction-decisions/1`

```text
{ schemaVersion, blueprintDigest, cohortId,
  records: [ { nodeId, content } ] }
content = { kind: "stateModel", workflow, states: [ { name, role } ],
            transitions: [ { command, from, to, requirementKeys[] } ] }
        | { kind: "portContract",
            ports: [ { port: "currentTime", requirementKeys[] }
                   | { port: "transitionEffect", command, onFailure, requirementKeys[] } ] }
```

- `role` is `initial`, `ordinary` or `terminal`.
- `onFailure` is required on an effect. It is `remain`, `proceed`,
  `unresolved`, or `null`. `null` means NOT DECIDED.
- The reader is strict. It refuses unknown or duplicate members, an unknown
  `kind`, `port`, `role` or disposition, and blank names.
- `encodeDecisionRecords` is canonical: sorted keys, sorted lists, distinct
  sorted requirement keys. `digest` is the SHA-256 of those bytes. A file
  written with exactly those bytes hashes to the digest.

Example: [`tests/fixtures/construction/time-entry-timer/construction-decisions.json`](../../tests/fixtures/construction/time-entry-timer/construction-decisions.json).

## What is generated

Only what the records state:

- one workflow;
- its states and transitions, from the `stateModel` record;
- its ports, from `portContract` records.

Traces cite the record's node and the requirement keys the record cites.
A state's trace is the union of the keys of the transitions that touch it.

Nothing else is ever generated. The generator emits no guard, invariant,
capability or decided effect disposition unless a record states it. The
vocabulary has no file, package or project operation, so the generator cannot
emit one either.

The generated draft must pass `ConstructionPlan.validate`. If it does not,
the generator refuses with `GeneratedPlanInvalid` and the plan violations. It
never patches a plan.

## Obligations: `ordo.construction-obligations/1`

```text
{ schemaVersion, cohortId, planDigest,
  obligations: [ { id, kind, requirementKeys[], nodeIds[], question,
                   extensionSurface[], planSubject? } ] }
```

`id` is `OBL-<cohort>-<kind>-<12 hex>`, taken over the obligation's own
content. The same content gives the same id. `extensionSurface` names
contracts as `ConstructionLayout` names them, e.g. `ITimerPorts.Perform/Stop`.
`planSubject` names the plan's `declareUnresolved` that carries the
obligation, when the plan can carry it. `readObligations` is strict, and
`encodeObligations` is canonical.

| Kind | When | In the plan |
|---|---|---|
| `undecidedEffectFailure` | an effect's `onFailure` is `null` or `unresolved` | port `onFailure: unresolved` plus a `declareUnresolved` linked by `effect` (vocabulary 1: unlinked) |
| `extensionBehavior` | an assigned requirement is cited only by ports | no extra operation; the port covers the key |
| `uncoveredRequirement` | an assigned requirement no record cites | a `declareUnresolved` citing the requirement and its in-scope owners |
| `conflict` | every blueprint conflict node inside the cohort's required scope, and a conflict node outside it that the blueprint relates to the cohort by a shared requirement, a dependency edge or a shared source document | none: citing it would violate validation |
| `unsupportedVocabulary` | vocabulary 1 with a decided `remain`/`proceed` disposition | the port has no disposition |

## Refusals (fail closed)

A refusal means no plan is generated, not even a partial one.

- **Envelope:** `UnsupportedDecisionSchema`, `DecisionBlueprintMismatch`,
  `DecisionCohortMismatch`, `UnsupportedVocabularyVersion`.
- **Nodes:**
  - `UnknownRecordNode`, `DuplicateRecord`;
  - `RecordForUnapprovedNode`: the node is not in the cohort's approved
    required scope;
  - `RecordKindNotAllowed`: `stateModel` needs a decision node, and
    `portContract` needs an interface or decision node;
  - `MissingDecisionContent`: a required decision has no record;
  - `MissingStateModel`, `MultipleStateModels`.
- **Content:**
  - `InvalidRecordName`, `MissingRequirementCitation`;
  - `RequirementNotOwnedByNode`, `RequirementOutsideCohort`;
  - `DuplicateRecordedState`, `MissingRecordedInitialState`,
    `MultipleRecordedInitialStates`, `UnconnectedRecordedState`;
  - `TransitionUnknownState`, `DuplicateRecordedTransition`,
    `ConflictingRecordedTransitions`;
  - `DuplicateRecordedPort`, `EffectForUnknownCommand`;
  - `DecidedDispositionWithoutDecision`: `remain` or `proceed` was recorded
    on a node that is not an approved decision.
- **Defense in depth:** `GeneratedPlanInvalid`.

## Result and identity

`DefaultPlan` can be constructed only by the generator. It has these members:

- `Draft`: the canonical `ordo.construction-plan/1` draft.
- `ValidatedPlan` and `PlanDigest`, the existing canonical plan digest.
- `Obligations` and `ObligationSet`.
- `GeneratedLayout`: the paths `Ordo.Construction` will emit,
  from `ConstructionLayout`, the one naming scheme the engine itself uses.
- `Provenance`:
  - generator `ordo.default-plan` at version `ordo.default-plan/3` (`/3`: an in-cohort conflict question carries the node justification; `/2`: in-cohort conflicts became obligations; `/1`: only out-of-cohort ones);
  - the vocabulary version and cohort;
  - the manifest, canonical blueprint, decision records, plan and
    obligations digests;
  - `DefaultPlanDigest`.
- `Digest`.

`encodeDefaultPlan` is the canonical `ordo.default-plan/1` document. It holds
the provenance (without its own digest), the plan, the obligations and the
layout. `Digest` is the SHA-256 of exactly those bytes. Identical inputs give
byte-identical output.

A default plan is not authority. A host still pins the digests, checks the
grant and controls every write.

## Evidence

- `tests/Ordo.Tests/ConstructionDefaultPlanTests.fs` covers:
  - the timer fixture, with pinned digests and obligation ids;
  - determinism;
  - coverage;
  - unapproved and contradictory records;
  - conflicts;
  - vocabulary;
  - strict wire formats;
  - provenance.
- `tests/Ordo.Construction.Tests/DefaultPlanConstructionTests.fs` checks:
  - the layout equals the engine's artifact paths and write plan;
  - the generated F# equals the compiled goldens, and pilot 01's B1 output,
    apart from the header line.
