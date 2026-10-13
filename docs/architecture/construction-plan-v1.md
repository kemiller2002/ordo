# Ordo construction plan v1

Status: **draft / implemented in `Ordo.Core`**. Contract owner: Ordo. Tracking:
[ordo#64](https://github.com/kemiller2002/ordo/issues/64) (CONSTRUCT-001).
Consumer: [ordo#65](https://github.com/kemiller2002/ordo/issues/65) (`Ordo.Construction`).
Decision: [`DF-SDE-2026-0018`](../../research/decisions/DF-SDE-2026-0018--constrain-construction-to-a-typed-vocabulary.md).
Vocabulary 2 (effect-failure disposition): [`DF-SDE-2026-0020`](../../research/decisions/DF-SDE-2026-0020--require-a-declared-effect-failure-disposition.md).
Combining independent plans: [construction plan consensus](construction-plan-consensus.md),
decision [`DF-SDE-2026-0021`](../../research/decisions/DF-SDE-2026-0021--combine-independent-plans-without-a-winner.md).
Generating the plan from approved decision records instead of an agent:
[default construction plan](construction-default-plan.md) (CONSTRUCT-008).

## Purpose

A coding agent that may create any file, write any expression, add any
package or reshape a project has to make, and can get wrong, every
architectural choice on every change. A construction plan takes those choices
away. The agent states domain intent for one prepared ECIR cohort, using a closed
set of operations, and nothing else is expressible. Choosing names, placement,
dependencies and output shapes is the construction engine's job (#65), not the
plan's.

The plan extends `ConstructionIr` rather than restating it. It does not keep a
second requirement registry, does not re-decide readiness, and does not mint
authority.

## Wire: `ordo.construction-plan/1`, vocabularies `ordo.construction-vocabulary/1` and `/2`

Schemas (structural only):
- vocabulary 1: [`schemas/ordo-construction-plan.v1.schema.json`](../../schemas/ordo-construction-plan.v1.schema.json);
- vocabulary 2: [`schemas/ordo-construction-plan.vocabulary-2.schema.json`](../../schemas/ordo-construction-plan.vocabulary-2.schema.json).

The plan envelope (`schemaVersion`) is the same for both. Vocabulary 2 is
vocabulary 1 plus the effect-failure disposition described
[below](#vocabulary-2-effect-failure-disposition). Reader: `ConstructionPlanWire.readPlan`, which is strict.
Unknown or duplicate members are refused, as are blank names and unknown
`op`/`port`/`predicate`/`role` values. Each refusal names the offending value
and its JSON path.

```text
{ schemaVersion, vocabularyVersion,
  binding:   { sourceManifestDigest, blueprintDigest, cohortId, decisionIds[] },
  operations: [ op ... ] }
```

| Operation | Operands | What it may express |
|---|---|---|
| `defineWorkflow` | name | the one finite-state workflow of this plan (v1: exactly one) |
| `defineState` | name, role `initial`/`ordinary`/`terminal` | a domain state; exactly one initial |
| `defineTransition` | command, from, to, guards[] | the command is legal from `from` and leads to `to`; commands are derived from transitions |
| `defineInvariant` | name, predicate, holdsIn[] | a named property that must hold in the listed states |
| `declarePort` | `currentTime`, or `transitionEffect` + command (+ `onFailure` in vocabulary 2) | an orchestration need; the engine picks the signature |
| `bindCapability` | command, capability | a semantic capability (`Ordo.Core.Capability`) required to issue the command |
| `declareUnresolved` | subject, question (+ optional `effect` in vocabulary 2) | behavior the plan must not guess; becomes an open obligation |

Every operation carries `trace: { requirementKeys[], nodeIds[] }`.

**Predicates.** Version 1 has a single predicate kind, `extension`: a named
contract that handwritten code implements. Expression, operator and code
strings have no representation at all.

**Names.** A name is made of ASCII letter and digit words, joined by single
spaces, hyphens or underscores, starts with a letter, and is at most 64
characters long. Its identity is the words in PascalCase, so `no timer`,
`no-timer` and `NoTimer` are the same name. The grammar admits no quote, dot,
slash, parenthesis or operator, so a name cannot carry code or a path.

**What cannot be said.** The vocabulary has no operation, member or value that
could express any of these:
- a file, path or directory;
- a shell or process command;
- a package or dependency;
- a project, framework or architecture setting;
- code or expression text;
- a change to validation policy or authority;
- an approval or "validated" marker.

The reader refuses each attempt as an unsupported operation or an unknown
member. The compiler refuses each attempt made in F#.

## Vocabulary 2: effect-failure disposition

**Why.** In vocabulary 1 the generated orchestration reports a `Failed` or
`Unknown` effect outcome but does not say what it means for the workflow. In
the time-entry timer pilot (`construction-by-design/time-entry-timer-01`,
finding F2, research-documents PR #7), two of the three construction-arm
agents decided it silently in handwritten session code: they discarded the
timer, because `state <- decision.Next` ran whatever the Stop outcome was. Two
of the three baseline agents chose the opposite. Only one run of six surfaced
the question. Vocabulary 2 removes that decision from session code: the plan
must state it, and the generated orchestration applies it.

**`onFailure` (required on every `transitionEffect` port).**

| Value | Meaning | Generated result on `Failed` or `Unknown` |
|---|---|---|
| `remain` | the workflow stays in the transition's source state | `Remained(..., failure, state = source)` |
| `proceed` | the transition's target state stands and the failure is reported | `Proceeded(..., failure, state = target)` |
| `unresolved` | the plan does not decide; generated code may pick neither outcome | `Unresolved(..., failure)` with no state; the host must handle it |

```json
{ "op": "declarePort", "port": "transitionEffect", "command": "Stop",
  "onFailure": "unresolved", "trace": { ... } }
{ "op": "declareUnresolved", "subject": "Stop completion refused",
  "effect": "Stop", "question": "...", "trace": { ... } }
```

**Linking an unresolved disposition.** An `unresolved` disposition must be
linked to exactly one `declareUnresolved` whose `effect` member names the
effect's command. The link is a checked semantic name compared by its
normalized form, not by matching prose in `subject` or `question`. Rules:
- an `unresolved` port with no linked obligation is refused
  (`UnresolvedEffectWithoutObligation`); an obligation about the same thing
  that does not carry `effect` is not a link;
- a link must name a declared `transitionEffect` port (`UnknownEffect`),
  even when the command itself exists;
- a link to an effect whose disposition is `remain` or `proceed` is refused
  (`EffectAlreadyDecided`): a decided effect has no open question;
- two links to one effect are refused (`DuplicateEffectObligation`).

The linked obligation becomes the construction obligation
`RESOLVE:<subject>` with kind `ResolveEffectFailure(effect, subject,
question)`.

**Only an effect port can carry a disposition.** In F#, `onFailure` is a
field of the `TransitionEffect` case, so `CurrentTime` has nowhere to put one
(a compile probe asserts FS0003). On the wire, `onFailure` on a
`currentTime` port is refused as `incorrect fields for port currentTime`.
`EffectFailureDisposition` is a closed union with no free-text or custom
case.

**Vocabulary gating.** The reader admits `onFailure` and `effect` only in a
vocabulary 2 plan. In vocabulary 1 they are unknown members. `validate`
refuses a draft built in F# that carries either under vocabulary 1
(`NotInVocabulary(label, member, vocabulary)`), and refuses a vocabulary 2
effect port without a disposition (`MissingEffectDisposition`). A vocabulary
1 plan is never read as vocabulary 2: relabelling one is refused until every
effect declares its disposition.

**A decided disposition needs an authorized decision.** `remain` and
`proceed` settle what a failed effect means for the workflow. As with
`bindCapability`, the agent's word is not enough: the port's trace must cite
a decision node that the prepared cohort requires, and therefore one that was
authorized externally before the cohort could be prepared. Otherwise the plan
is refused with `EffectDispositionWithoutDecision(label)`. A plan fails this
check when it cites no node, only non-decision nodes, or a decision outside
the cohort; the last also fails `UnauthorizedNodeReference`. Without such a
decision, the only legal disposition is `unresolved`, which settles nothing and
becomes an open obligation. So an agent can no longer settle effect-failure
semantics on its own.

## Semantic validation: `ConstructionPlan.validate prepared draft`

The input is a `PreparedCohort` from `ConstructionIr.prepareCohort`, so source
conservation, transitive readiness and external decision authorization have
already been established. They cannot be skipped. Validation then checks:

1. **Binding.**
   - Schema and vocabulary versions are known.
   - The prepared manifest's digest is recomputed from its content
     (`UnverifiedSourceManifest`). A manifest that carries a reviewed digest but
     altered requirements is refused, even when the blueprint agrees with it.
   - The source manifest digest equals the prepared manifest.
   - The blueprint digest equals `blueprintDigest` of the prepared blueprint. It is recomputed, never trusted.
   - The cohort ID equals the prepared cohort.
   - The bound decision IDs equal exactly the cohort's required decisions plus its required standalone rationales.
   - Violations: `StaleSourceManifest`, `StaleBlueprint`, `CohortMismatch`, `UnboundDecision`, `UnauthorizedDecision`.
2. **Trace.**
   - Every operation names at least one requirement and one node.
   - Every requirement is one the cohort *implements* (assigned). A prerequisite requirement is not implementation scope.
   - Every node is in the prepared required scope.
   - The cohort node is never cited (`CohortNodeCited`). It is the delivery
     container linked to every assigned requirement, so citing it would justify
     anything.
   - Every anchored node links at least one of the operation's requirements.
   - A `bindCapability` must cite a decision node the cohort requires
     (`CapabilityWithoutDecision`). A capability requirement is authorization
     policy, so the agent's word is not enough; the decision must have been
     authorized externally before the cohort could be prepared.
   - Under the same rule, a `transitionEffect` port whose disposition is
     `remain` or `proceed` must cite a required decision node
     (`EffectDispositionWithoutDecision`). `unresolved` needs none.
   - Every assigned requirement is covered by at least one operation, so a requirement cannot silently vanish between ECIR and construction.
3. **Names and identifiers.**
   - Names follow the grammar above.
   - Capability IDs are ASCII words joined by `.`, `:`, `-` or `_`, start with a
     letter and are at most 128 characters long.
   - Unresolved questions are at most 1000 characters.
4. **Structure.** Validation refuses:
   - a missing workflow, or more than one;
   - a missing initial state, or more than one;
   - duplicate definitions, compared by normalized name;
   - unknown states or commands;
   - conflicting transitions (same source and command, different targets);
   - duplicate transitions;
   - transitions out of a terminal state;
   - unreachable states, found by breadth-first search over an adjacency map, so
     validation of a hostile 20,000-state chain stays within the test's 30-second bound;
   - non-terminal dead ends; a self-loop alone does not leave a state;
   - duplicate guards on one transition, compared by normalized name. They are
     refused, not merged;
   - invariants with an empty scope;
   - blank unresolved questions;
   - in vocabulary 2, a missing disposition, an `unresolved` disposition
     without its linked obligation, a link to an unknown or already decided
     effect, and two links to one effect (see above). In vocabulary 1, a
     disposition or link at all (`NotInVocabulary`).

Failures are `PlanViolation` values that name the operation by its derived
label, for example `transition:Running/Pause`. The agent never assigns
operation IDs or ownership. An operation owns exactly the construct it
defines, so two operations that define the same construct collide.

**Coverage is reported, not just counted.** `ValidatedPlanView.Coverage`
classifies each assigned requirement in one of three ways:
- `Constructed`: states, transitions, invariants, ports or capabilities define it;
- `UnresolvedOnly`: only open obligations mention it;
- `WorkflowLevel`: only the workflow declaration mentions it, as a
  whole-workflow constraint such as "the timer is not durable".

Only `Constructed` coverage is construction. None of the three is evidence of
implementation.

## Validated plan, identity and authority

`ValidatedPlan` has a private constructor. `ConstructionPlan.validate` is the
only way to obtain one. Ordinary code cannot construct one directly, cannot pass
a draft or a view in its place, and cannot add a `Validated = true` marker,
because the type has no such field. Compile-negative probes in
`tests/Ordo.Tests/ConstructionPlanTests.fs` run the real F# compiler against
the built assembly and assert the specific error code for each attempt.

`ConstructionPlan.digest` is the SHA-256 of `canonicalJson`:
- names are normalized;
- states are ordered initial first, then by name;
- transitions are ordered by (from, command);
- every other list is sorted and deduplicated;
- object keys are sorted;
- in vocabulary 2, `onFailure` and the `effect` link are part of the
  canonical form. A vocabulary 1 plan has neither member, so its canonical
  JSON and digest are byte-identical to what earlier releases produced.

Reordering operations, trace lists or decision IDs, or respelling a name,
leaves the digest unchanged. Any semantic change alters it, including a
change of disposition or of vocabulary. The time-entry fixture's vocabulary 1
digest and its three vocabulary 2 variants' digests are pinned in the tests. Changing canonicalization requires a
schema or vocabulary version change, not an updated constant.

`ConstructionPlan.isBoundTo prepared plan` tells a host that holds both values
whether the plan was formed against exactly that cohort.

A validated plan proves only semantic agreement with one exact prepared
cohort. The same holds for a plan combined from several drafts by
[`ConstructionPlanConsensus`](construction-plan-consensus.md). It is **not** authenticated authority, not host permission, not
evidence of implementation and not evidence of verification. Hosts (Praxis
#242) pin the plan digest, recheck approvals and control every effect.

## Vocabulary 3: classified choices

`ordo.construction-vocabulary/3` adds a mandatory pair rule, explicit
refusals, undecided pairs and a mandatory decision inventory for every
extension. With these, every semantic choice is classified as required,
approved, implied, unresolved or unsupported, and only the first three
generate. See [decision classification](decision-classification.md).

`ordo.construction-vocabulary/4` adds typed refusal codes and branching
transitions, whose target a handwritten selector picks from a generated
closed union. See [refusal codes and branching](refusal-codes-and-branching.md).

`ordo.construction-vocabulary/5` replaces an effect port's `onFailure` with a
mandatory effect policy that treats definite failures (bounded retry, remain,
proceed, approved compensation, escalate) and unknown outcomes (reconcile,
escalate; never retried) separately. See
[effect outcome policies](effect-outcome-policies.md).

`ordo.construction-vocabulary/6` adds a persisted instance: a stated
idempotency scope (durable, session or unresolved) and stale-write action
(refuse, escalate or unresolved). See [persisted instances](persisted-instances.md).

## Versioning

An unknown `schemaVersion` or `vocabularyVersion` is refused, never upgraded
in place. A future vocabulary that adds an operation, predicate or member gets
a new version, as vocabulary 2 did for `onFailure` and `effect`. Plans in an
older version stay readable only while that reader is kept. A change to
canonical encoding is a breaking change.

| Vocabulary | Accepted by | Canonical form |
|---|---|---|
| `ordo.construction-vocabulary/1` | Ordo 1.6.0 and later | unchanged since Ordo 1.6.0 |
| `ordo.construction-vocabulary/2` | Ordo 1.7.0 and later | adds `onFailure` and `effect` |

`ConstructionPlan.VocabularyVersion` keeps its original value (vocabulary 1),
so code that authors plans with it keeps producing vocabulary 1 plans with
unchanged meaning. New plans use `VocabularyVersion2`.
`SupportedVocabularyVersions` lists what the reader accepts.

## Evidence

- Runtime negatives: `tests/Ordo.Tests/ConstructionPlanAdversarialTests.fs`.
- Real fixture: `tests/fixtures/construction/time-entry-timer/`, taken from
  `kemiller2002/time-entry-state-machine@73a63f1` requirement text. Its
  vocabulary 2 variants are `plan.v2-remain.json`, `plan.v2-proceed.json`
  and `plan.v2-unresolved.json`. The decided variants bind test-only
  blueprints (`blueprint.v2-remain.json`, `blueprint.v2-proceed.json`) that
  add the decision node `DEC-TIMER-STOP-FAILURE`.
- Vocabulary 2 negatives: section 9 of
  `tests/Ordo.Tests/ConstructionPlanAdversarialTests.fs`.
- Compile negatives: `tests/Ordo.Tests/ConstructionPlanTests.fs`.
- Consensus over independent plans, including the time-entry pilot's
  agent-produced plans B1, B2 and B3
  (`tests/fixtures/construction/time-entry-timer-pilot-01/`):
  `tests/Ordo.Tests/ConstructionPlanConsensusTests.fs`. See
  [construction plan consensus](construction-plan-consensus.md).
- Default plans generated by Ordo from `ordo.construction-decisions/1`
  records, with typed `ordo.construction-obligations/1` obligations:
  `tests/Ordo.Tests/ConstructionDefaultPlanTests.fs`. See
  [default construction plan](construction-default-plan.md).
