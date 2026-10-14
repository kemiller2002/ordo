# Refusal codes and branching transitions (vocabulary 4)

Status: **draft / implemented in `Ordo.Core` and `Ordo.Construction`**
(plan vocabulary `ordo.construction-vocabulary/4`, generator
`ordo.construction/4`). Tracking: CONSTRUCT-010 phase 2, increment 1.
Decision:
[`DF-SDE-2026-0024`](../../research/decisions/DF-SDE-2026-0024--type-refusal-codes-and-branching-transitions.md).
Builds on [decision classification](decision-classification.md).

## What it adds

Vocabulary 3 could say *that* a command is refused, but not *which* refusal
a caller sees. Every refusal reached the host as `InvalidTransition` or
`GuardRejected`. Any code a product needs, such as `TIMER_TOO_SHORT`, had to
be invented in handwritten code, outside the plan and its trace. Vocabulary
3 could also give each transition only one target, so a command whose
outcome depends on something the host observes could not be modelled.

Vocabulary 4 is vocabulary 3 plus three operations and one extension kind.
Schema:
[`schemas/ordo-construction-plan.vocabulary-4.schema.json`](../../schemas/ordo-construction-plan.vocabulary-4.schema.json).

| Operation | Rule |
|---|---|
| `defineRefusalCode { code }` | names one typed refusal code, on the authority of its trace |
| `assignRefusalCode { target, code }` | gives a defined code to one target: a refused `pair`, the `unlisted` pairs, or a `guard` |
| `defineBranchingTransition { command, from, targets[] }` | a legal pair with at least two distinct targets. A handwritten selector picks one |
| `declareExtensionInventory { extension: { kind: "selection", from, command } }` | the decision inventory for a branching transition's selector. Required, like every other extension |

## Validation

Validation refuses each of the following:

- **`UnknownRefusalCode`:** an assignment names a code no `defineRefusalCode`
  defines.
- **`CodeOnNonRefusal`:** the target does not refuse. A `pair` target must be
  an explicit refusal, or an unlisted pair that a terminal state or a
  `refused` pair rule refuses. An `unlisted` target needs a `refused` pair
  rule. A `guard` target must be a guard some transition uses.
- **`DuplicateDefinition`:** two codes for one target.
- **`BranchingNeedsTwoTargets`:** fewer than two distinct targets, compared by
  normalized name.
- **`UnknownState`:** a target that is not a defined state.
- **`TransitionFromTerminal`:** a branching transition from a terminal state.
- **`ConflictingPairDisposition`:** a branching pair that is also a
  transition, a refusal or an undecided pair.
- **`MissingExtensionInventory`:** a selector with no inventory.
- **`NotInVocabulary`:** any of the above in vocabulary 1, 2 or 3. The strict
  reader refuses them too.

Every target of a branching transition counts for reachability. Codes do not
count as generated structure in coverage, because they label refusals that
are already structure. A branching transition does count.

## Classification

- **A branching pair** is classified like any transition: approved when its
  trace cites a bound decision, otherwise required.
- **Its selector** is `extension:selection/F/C`, with decision points like
  any other extension. An undecided point keeps the plan from being
  `complete`.
- **Each code assignment** is a choice of its own,
  `refusalCode:{pair/F/C | unlisted | guard/G}` (kind `refusalCode`),
  classified by its trace. It is never implied: a code is a product decision
  that the trace must justify.

## Generated code (`ordo.construction/4`)

For a plan that uses neither codes nor branching, the F# tiers are
byte-identical to `ordo.construction/3`, apart from the generator name and
plan digest. A test proves this on the real open timer plan. Generators 1 to
3 are unchanged.

**Tier 1** adds:
- `<W>RefusalCode`, a closed union of the defined codes;
- for each branching transition, `<W>Branch_<From>_<Command>`, a closed union
  of its targets.

**Tier 2** adds:
- **`Rules.Select_<From>_<Command>: unit -> <W>Branch_<From>_<Command>`.** The
  selector can return only a listed target; returning a `<W>State` fails with
  FS0001, and omitting the field fails with FS0764.
- **`branching`**: every branching transition as (from, command, targets).
- **`next rules state command`.** It is exhaustive over every pair. It calls
  the selector only for its own pair and maps the union case to the state.
  `decide` uses it in place of `target`.
- **`pairCode`, `guardCode` and `refusalCode`.** These exhaustive functions
  give the code the plan assigns to a refusal, or `None` where the plan
  assigns none. There is no catch-all: an unassigned refusal reports `None`,
  never a default code.

**Construction** adds:
- the extension contract `<W>Rules.Select/<From>/<Command>`
  (`selection-function`);
- an `IMPLEMENT` obligation for it, or one `DECIDE` per undecided point
  instead;
- structural checks that label the pair `"expected": "branching"` and list
  its `targets`.

A refusal code named like an F# reserved case (for example `None`) is refused
with a typed `ReservedName("refusal code", …)`. It is never mangled.

## Evidence

The `Gate` golden is generated from a synthetic plan in
`tests/fixtures/construction/gate-synthetic`. Its sources are test input, not
product requirements. Raise from Closed branches to Open or Jammed, and the
refusals carry `AlreadyOpen`, `Obstructed` and `NotAccepted`. The golden
compiles under `TreatWarningsAsErrors`. Behavior tests drive every pair. In
the compile-negative probes, a selector cannot return an unlisted state or
be omitted.

Mutation checks: six mutants each failed the suite.

- **MV1:** code validation is disabled.
- **MV2:** branching targets are dropped from reachability.
- **MV3:** the selector's choice is ignored.
- **MV4:** the unlisted code is not applied.
- **MV5:** classification ignores branching.
- **MV6:** the reader admits vocabulary 4 operations in vocabulary 3.

## Default plans (decision records `ordo.construction-decisions/3`)

Version 3 records generate vocabulary 4 default plans, and only those. Every
other pairing is refused with `DecisionSchemaVocabularyMismatch`. A version 3
`stateModel` is version 2 plus three optional lists:

```text
refusalCodes:    [ { code, requirementKeys[] } ]
codeAssignments: [ { target: { kind: "pair", from, command } | { kind: "unlisted" },
                     code, requirementKeys[] } ]
branching:       [ { command, from, targets[], requirementKeys[],
                     decisionPoints: [ { point, decided, question?, requirementKeys[] } ] } ]
```

Version 2 records cannot carry these lists; the strict reader refuses them.

Every cited key must be owned by the record's node and assigned to the
cohort. The citations are `code:X`, `codeFor:…`, `branching:F/C` and
`branching:F/C/point:P`.

The state model refuses each of the following:

- a branching transition that names an unknown state (`TransitionUnknownState`);
- a branching pair that is also a single-target transition
  (`ConflictingRecordedTransitions`);
- a code given to something that does not refuse (`CodeOnNonRefusal`, via
  plan validation).

Code targets are limited to refused pairs and the unlisted pairs, because
default plans have no guards.

Generation emits:

- the codes and their assignments;
- each branching transition;
- the selector's decision inventory;
- one obligation per selector:
  - **no undecided point:** an `extensionBehavior` (*implement*) obligation on
    `<W>Rules.Select/<From>/<Command>`;
  - **an undecided point:** one `undecidedExtensionDecision` per open point,
    and no implement obligation.

Anything the records do not construct is still an `uncoveredRequirement`
obligation. In the gate fixture, that is the guard-only requirement.

Example:
[`tests/fixtures/construction/gate-synthetic/construction-decisions-v3.json`](../../tests/fixtures/construction/gate-synthetic/construction-decisions-v3.json).

**No real cohort has version 3 records yet.** Approved records are authored
by the approver, so Ordo cannot write them. The real slices therefore still
generate in the vocabulary of their approved records. Mutation checks: six
mutants each failed the suite.

- **MD2:** a selector with an open point is still handed off as *implement*.
- **MD3:** the branching/transition conflict check is removed.
- **MD4:** version 2 records accept version 3 members.
- **MD5:** coverage ignores selectors.
- **MD6:** code assignments are dropped.
- **MD7:** unknown branching targets are accepted.

## Wire text (vocabulary 7)

A code's plan name is a semantic name, which drops separators:
`ENTRY_NOT_RECORDED` becomes the key `ENTRYNOTRECORDED`. A product whose
diagnostics promise a stable external text can therefore not recover it from
the name. In vocabulary 7, `defineRefusalCode` may carry that text verbatim:

```text
defineRefusalCode { code, wireText?: [A-Za-z0-9_.:-]{1,64}, trace }
```

Validation refuses:

| Violation | Cause |
|---|---|
| `InvalidWireText` | text outside the grammar |
| `PartialWireText` | some codes state wire text and others do not; it is all or none |
| `DuplicateWireText` | two codes share one text |
| `NotInVocabulary` | `wireText` in vocabularies 4 to 6 (the reader refuses the member too) |

When the plan states wire text, the transition tier emits
`wireText : <W>RefusalCode -> string`, one literal per code. When it states
none, no function is emitted, and a plan without wire text encodes exactly
as before.

Evidence: the `GateYard` golden states wire text for all six codes, and a
behaviour test reads it back. A compile probe shows `SessionYard`, which
states none, has no `wireText`. Six mutants each failed the suite:

- **W1:** partial wire text is accepted.
- **W2:** duplicate wire text is accepted.
- **W3:** the grammar check is removed.
- **W4:** the reader drops wire text.
- **W5:** vocabulary 6 accepts wire text.
- **W6:** the emitter writes the code name instead of its text.

## Not yet supported

- **Data-carrying refusals.** A code names a reason. It carries no payload,
  because states and commands carry no data in any vocabulary.
- **Guards in default plans.** Decision records cannot state a guard, so
  guard codes are reachable only from a hand-authored plan.
