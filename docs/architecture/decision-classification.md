# Decision classification (`ordo.choice-classification/1`)

Status: **draft / implemented in `Ordo.Core`** (`ConstructionClassification`,
vocabulary 3 of `ConstructionPlan`, decision records version 2). Tracking:
CONSTRUCT-010 phase 4. Decision:
[`DF-SDE-2026-0022`](../../research/decisions/DF-SDE-2026-0022--classify-every-construction-choice.md).

## The defects this closes

1. **Closed-world refusal.** Vocabularies 1 and 2 generate a transition table
   in which every (state, command) pair the plan does not name is refused.
   When the requirements leave a pair undecided, the generated code still
   decides it: by omission. CONSTRUCT-008 pre-registered this as limitation 1,
   and it materialised in the Chrona slice (Stop while Paused, CHR-GAP-005) and
   the Summa slice (bounce after Delivered).
2. **OPT-14: implement obligations with an undecided rule inside.** The timer
   blueprint makes "stop completion and rounding" an `extensionBehavior`
   obligation that says *implement*. Its tie rule, an elapsed time exactly half
   a minute past a whole minute (D7), is stated nowhere, and arm C resolved it
   silently in 3 of 3 runs.

Both defects let a choice nobody made reach code as if somebody had made it.

## Five classes

Every semantic choice in a validated plan is classified as exactly one of the
following.

| Class | Meaning | Generated as completed construction |
|---|---|---|
| `required` | fixed by the requirement text the choice's trace cites | yes |
| `approved` | fixed by an externally authorized decision (or standalone rationale) the trace cites | yes |
| `implied` | fixed by a documented rule: a cited pair rule, or a terminal state's role | yes |
| `unresolved` | stated as open: an undecided pair, an undecided effect failure, an undecided extension decision point, an open question | **no** |
| `unsupported` | decided by omission, because the vocabulary cannot avoid it. In vocabularies 1 and 2: unlisted pairs, extensions with no inventory, and, in vocabulary 1, effect failure | **no** |

A choice whose trace cites an authorized decision is `approved`. Otherwise it
is `required` by the requirements it cites. Classification is a report about
what the plan claims, checked against what validation enforces. It is not new
authority.

**Subjects** follow a deterministic naming scheme:

- `state:S`, `pair:S/C`, `invariant:I`, `port:currentTime`,
  `port:transitionEffect/C`, `effectFailure:C` and `capability:C/id`;
- `extension:{guard|invariant|effect}/N` and
  `extension:…/N/point:P`;
- `unresolved:Subject`, for open questions not tied to an effect.

## Completeness cannot be faked

`ConstructionClassification.complete : ValidatedPlan -> Result<CompletePlan, ClassifiedChoice list>`
returns `Ok` only when every choice is required, approved or implied.
`CompletePlan` has a private constructor. The test suite's compiler probes
show that ordinary code cannot do any of the following:

- pass a `ValidatedPlan` where a `CompletePlan` is required (FS0001);
- construct a `CompletePlan` directly (FS1093);
- set a `Complete` marker, because none exists (FS1129).

A host or verifier that needs completed construction asks for a
`CompletePlan`. A plan with an open choice cannot satisfy that.

## Vocabulary 3 (`ordo.construction-vocabulary/3`)

Vocabulary 3 is vocabulary 2 plus four operations. Schema:
[`schemas/ordo-construction-plan.vocabulary-3.schema.json`](../../schemas/ordo-construction-plan.vocabulary-3.schema.json).

| Operation | Rule |
|---|---|
| `declarePairRule { unlisted: refused \| undecided }` | exactly one. `refused` must cite the documented rule; `undecided` leaves every unlisted pair outside a terminal state open |
| `defineRefusal { command, from }` | an explicit refusal, on the authority of its trace |
| `declareUndecidedTransition { command, from, question }` | an explicit open pair, even under a refusing rule. Not allowed from a terminal state |
| `declareExtensionInventory { extension, decisionPoints[] }` | exactly one for every guard, invariant and transition effect. Each point is `decided` (with its own trace) or `undecided` (with a question). An empty list asserts that the trace fixes the behavior completely |

Validation refuses the following:

- `MissingPairRule`;
- `ConflictingPairDisposition`: a pair named by two of transition, refusal
  and undecided;
- `MissingExtensionInventory`;
- `UnknownExtension`;
- duplicate decision points, compared by normalized name;
- blank or over-long questions;
- each new operation in vocabulary 1 or 2 (`NotInVocabulary`).

The strict reader refuses each of these in an earlier vocabulary too.
Vocabularies 1 and 2 keep their exact canonical forms and digests.

## Generated code (`ordo.construction/3`)

Tier 2 adds a case `Undecided of state * command` and an exhaustive
`undecided` function. `decide` returns `Error(Refusal.Undecided(...))` for an
open pair, never `InvalidTransition`. The engine also emits these
obligations:

- `RESOLVE-PAIR:S/C` for every open pair;
- `DECIDE:<contract>/<point>` for every undecided decision point;
- **no `IMPLEMENT:<contract>`** for an extension with an undecided point.

Structural checks label open pairs `"expected": "undecided"`. Generator
versions 1 and 2 are unchanged byte for byte.

## Default plans (decision records `ordo.construction-decisions/2`)

Version 2 records generate vocabulary 3 plans, and version 1 records generate
vocabulary 1 and 2 plans. Any other pairing is refused with
`DecisionSchemaVocabularyMismatch`.

A version 2 `stateModel` must include `unlisted`, which is either
`{ disposition: "refused", requirementKeys }` or
`{ disposition: "undecided" }`. It may also list `refusals` and `undecided`
pairs. Every `transitionEffect` must list `decisionPoints`, each one of:

- `{ point, decided: true, requirementKeys }`;
- `{ point, decided: false, question, requirementKeys }`.

Every cited key must be owned by the record's node and assigned to the
cohort.

The obligations gain two kinds:

- **`undecidedTransition`:** one per open pair.
- **`undecidedExtensionDecision`:** one per undecided point. It carries the
  point's keys and the effect's keys.

A requirement realized only through an effect with an undecided point gets no
`extensionBehavior` (implement) obligation. The requirements ledger counts
both new kinds as genuine domain decisions.

## Evidence on the real timer cohort

Generated from `tests/fixtures/construction/time-entry-timer/construction-decisions-v2.json`.

| | vocabulary 2 (decisions/1) | vocabulary 3 (decisions/2) |
|---|---|---|
| Unlisted pairs (7) | `unsupported`: refused by omission | `implied` by REQUIREMENTS.md L367-368 (rejection rule) |
| Stop extension | `unsupported`: no inventory; obligation says *implement* | rounding tie `unresolved`; three other points decided; obligation says *decide* |
| Open choices | 10 | 3: effect failure, rounding tie, uncovered session requirement |
| `complete` | refused | refused, on exactly those 3 |

The `TimerOpen` golden (unlisted pairs undecided) compiles under
`TreatWarningsAsErrors`. All 12 pairs behave as stated: 5 are legal, and 7
return `Undecided`. None returns `InvalidTransition`.

Mutation checks: three mutants each failed the suite.

- **M1:** undecided pairs are emitted as refusals.
- **M2:** an omitted pair is classified as implied.
- **M3:** *implement* is issued despite an open point.
