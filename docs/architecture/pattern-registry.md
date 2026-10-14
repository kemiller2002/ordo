# Pattern registry and recognition (ORDO-PATTERN-001)

Status: implemented, unreleased. Work item WI-0045. Research context:
`kemiller2002/research-documents` `research/pattern-construction/`.

## Why

CONSTRUCT-008 found mechanical-first construction cost 8–34% *more* than
agent coding on slices where the vocabulary covered few requirements:
6 of 46 on the expanded time-entry slice and 4 of 18 on Chrona. The agent paid to
read host artifacts and a handoff for generated code that did little of the
work. The default-plan generator is also all-or-nothing: one refused record
refuses the whole cohort.

This change puts a cheap, deterministic **recognition pass** in front of
generation. It answers, per requirement, "can Ordo construct this from
approved structured content, and if not, why not?" It answers before
anything is generated. Unsupported work goes to an agent immediately, as a
bounded packet, and generation is skipped entirely when it would construct
nothing.

## Pieces

| Module | Wire schema | Role |
|---|---|---|
| `PatternRegistry` | `ordo.pattern-registry/1` | Versioned pattern contracts, selection, pins |
| `PatternRecognition` | `ordo.pattern-recognition/1` | The pass, outcomes, packets, completion check |
| `PatternRecognitionWire` | `ordo.delegation-contract/1`, `ordo.delegation-completion/1` | Canonical encodings, digests, strict readers |
| `sde ecir recognize` | — | Report-only CLI; never grants authority |

## Pattern contract

Every pattern version declares:
- a stable id and version;
- its inventory family;
- required inputs and required decisions;
- preconditions, postconditions and invariants;
- the generator it runs through;
- independent verification obligations, stated over the approved content;
- unsupported variations.

The contract's canonical JSON digest is what a pin binds. The claim function
is code, so its digest cannot see changes to it. The rule is: **any change to
what a pattern claims or generates is a new version.**

Initial library. All three wrap `ordo.default-plan/3`; no new generator was added.

| Pattern | Claims | Kind |
|---|---|---|
| `ordo.state-machine@1` | requirements cited by `stateModel` transitions on an approved decision node | constructs |
| `ordo.transition-effect-port@1` | requirements cited by a `transitionEffect` port | interface only; the effect body is handwritten |
| `ordo.current-time-port@1` | requirements cited by a `currentTime` port | interface only |

Further patterns are admitted only on the evidence in
`research/pattern-construction/pattern-ranking.md`. A pattern that raises total
cost without a correctness or governance benefit is removed, not kept for coverage.

## Recognition outcomes

Every requirement assigned to the cohort gets exactly one outcome.
Requirements whose blueprint disposition is deferred, rejected or superseded
are listed as **excluded**, so every key still appears exactly once.

| Outcome | When | Action |
|---|---|---|
| `Constructible(patterns, delegatedPrerequisites)` | one pattern id constructs it, no gap, no defect, no undecided prerequisite | generate; verification obligations stay open |
| `DecisionRequired(gaps, generatedBy)` | undecided effect failure, unapproved decision, open conflict, or two patterns competing | typed decision obligation; never a default |
| `Delegate(interfaces, generatedBy)` | no pattern constructs the behavior | bounded packet to an agent, immediately |
| `Blocked(blockers)` | invalid source, blueprint `unresolved`, unusable matched content, a blocked or undecided prerequisite, or a generator refusal | stop that requirement; report the blocker |

Each requirement also carries `decisionClass`, the Phase 4 class from
`ConstructionClassification` (ordo#78):
- `Constructible` → `approved`;
- `DecisionRequired` → `unresolved`;
- `Delegate` → `unsupported`;
- `Blocked` → none.

Recognition never emits `required` or `implied`, because it reads no
requirement text and applies no documented rule. The ledger keeps its own
classes; recognition does not restate them.

Precedence when several apply: `Blocked`, then `DecisionRequired`, then
`Delegate`, then `Constructible`. A doubt never resolves toward
`Constructible`. A false positive generates wrong behavior; a false negative
only delegates work that was safe.

Two rules go beyond the default-plan coverage rule:
- **A requirement with both a generated transition and a handwritten port
  body is `Delegate`, not `Constructible`.** Its structure is generated
  (`generatedBy` is non-empty, and it counts as partially generated). This
  matches CONSTRUCT-010's rule: "generated only when its behaviour is in
  generated code."
- **An open conflict node is `DecisionRequired(ConflictUnresolved)`.** The
  benchmark blueprints use conflict nodes for open questions ("O-4 (open)… not
  stated"), not for contradictions.
- **A conflict settled by an authorized decision is not open.** A
  `conflictResolution` record (ordo#81) settles a conflict for recognition
  only when all of these hold:
  - its node is an authorized Decision in the cohort's scope;
  - the conflict is a Conflict node in scope;
  - the two share a requirement key.

  Any other link leaves the conflict open. Every packet that hands over a
  requirement owned by a settled conflict lists the deciding node in
  `approvedDecisions`, with `resolves: [conflictIds]`. Praxis suppresses the
  question only on that field.

### Dependencies

Requirement A depends on requirement B when one of A's owner nodes reaches,
through `DependsOn`, a node that owns B.
- Generated work never rests on a prerequisite that is `Blocked` or
  `DecisionRequired`; the dependent becomes `Blocked(AwaitingPrerequisites)`.
- A `Delegate` prerequisite leaves generated work `Constructible`. The
  delegated keys are named, and `CompleteConstruction` is false.
- Delegated work behaves the same way, with one exception: host policy
  `EscalateOpenQuestions` lets the agent inherit open conflict questions.

### Open questions under policy

With `EscalateOpenQuestions = true`, the packet also carries any requirement whose only gaps are
open conflicts and which has no generated part. Its outcome stays
`DecisionRequired`. The packet lists:
- the questions, using each conflict node's own text;
- `escalationRequired` keys, which the completion check refuses to accept as
  `implemented`.

The agent implements what the requirement determines and escalates the rest.
It can never answer the question silently. The policy is the host's to set.

## Bounded and deterministic

- One claim evaluation per (requirement, selected pattern):
  `measurements.claimEvaluations = evaluated × patternsSelected`.
- One dependency closure over the validated, acyclic node graph.
- No retry and no re-entry. The single generator run is folded back by
  `applyGeneration`. A refusal blocks only the requirements that relied on
  generated content; delegated work continues.
- The report is a pure function of its inputs. The order of definitions,
  records, nodes and requirements does not matter (tested).
- On the four frozen benchmark cohorts the pass takes 3–20 ms after JIT warm-up.

## Delegation packet (`ordo.delegation-contract/1`)

A packet holds one coherent group: requirements connected through a shared
contract, interface, invariant, decision or rationale node. It carries:
- pinned source references (key, document, location, revision, digest);
- the approved decisions and rationales that intersect the group or that its
  owners depend on (the fix for OPT-15);
- generated interfaces to satisfy;
- contracts and invariants, with their justification text;
- verification obligations;
- the patterns that generated part of the work;
- host-supplied allowed paths and architecture constraints;
- forbidden paths, which are the generated ownership roots;
- open questions and the keys that require escalation.

It carries no plan, no generated source and no unrelated requirements.

## Completion (`ordo.delegation-completion/1`)

`validateCompletion` is pure. Praxis runs it before applying any artifact.
It refuses a completion when:
- the packet id or digest is wrong;
- a path is unsafe (absolute, `..`, backslash or colon), forbidden, or outside
  the allowed globs;
- an artifact is duplicated;
- a disposition is missing, duplicated, unknown or has a blank reason;
- the agent reports `implemented` with no artifacts;
- verification evidence for an obligation is missing when anything is
  implemented, or the evidence names an unknown obligation;
- an escalation-required requirement is reported as implemented.

## Pins

`RegistryPin = { registryVersion; [(patternRef, contractDigest)] }` is
recorded in every report.
- Recognition under a pin selects exactly those versions with unchanged
  contracts.
- An unknown version gives `UnknownPatternVersion`.
- An edited contract under the same version gives `PatternContractChanged`.
- A newer version, or a new pattern, is not selected under an old pin.

Without a pin, the latest version of every id is selected and the new pin is
reported. Under one approved pin, a registry update can only fail the
recognition; it cannot change its result.

## Acceptance tests

All in `tests/Ordo.Construction.Tests/PatternRecognitionTests.fs`:
1. Identical inputs produce identical results.
2. Registry ordering does not change selection.
3. Ambiguous overlapping patterns are detected.
4. Unsupported patterns delegate immediately.
5. Missing decisions cannot become silent defaults.
6. Requirement provenance remains complete.
7. Pattern versions are pinned.
8. Unknown versions fail safely.
9. Registry updates cannot silently change previously approved construction.

Adversarial cases:
- similar syntax with different semantics (a state model recorded on a
  non-decision node);
- a decided disposition recorded on an interface node;
- undecided and delegated prerequisites;
- open conflicts, strict and escalated;
- an invalid source;
- records bound to another blueprint;
- agreement with the generator's obligations;
- a generator refusal;
- forbidden, escaping and incomplete completions;
- an implemented escalation.

## Not done here

- No new generator. Domain records, idempotency and persistence patterns
  wait on the ranking and on CONSTRUCT-010 Phase 2 ownership.
- Praxis does not consume this until an Ordo.Core release carries it.
  Praxis pins `EchelonFoundry.Ordo.Core` by version.
