---
id: DF-SDE-2026-0020
title: Require a declared effect-failure disposition in construction vocabulary 2
status: draft
type: decision-record
created: 2026-10-12
updated: 2026-10-12
tags: [construction, vocabulary, effects, four-tier, construct-vocab-2]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/construction-plan-v1.md
  - docs/architecture/construction-engine-v1.md
  - research/decisions/DF-SDE-2026-0018--constrain-construction-to-a-typed-vocabulary.md
  - research/decisions/DF-SDE-2026-0019--generate-four-tier-workflow-structure-deterministically.md
  - schemas/ordo-construction-plan.vocabulary-2.schema.json
  - src/Ordo.Core/ConstructionPlan.fs
  - src/Ordo.Construction/Emit.fs
  - tests/Ordo.Construction.Tests/EffectFailureTests.fs
provenance:
  contributions:
    EXE-20261012T184706791Z-71fb8dee:
      operations: [created]
      at: 2026-10-12T19:03:39.294Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record the vocabulary 2 effect-failure disposition decision (CONSTRUCT-VOCAB-2)"
      evidence: [tests/Ordo.Construction.Tests/EffectFailureTests.fs]
---

# Context

Vocabulary 1 (DF-SDE-2026-0018) lets a plan declare a transition effect, and
the generated orchestration (DF-SDE-2026-0019) asks the host to perform it and
reports `Completed`, `Failed` or `Unknown`. On purpose, it does not decide
what a failed or unknown outcome means for the workflow. Its result type says
so: "it does not decide what a failed or unknown effect means for the
workflow".

A pre-registered pilot ran the time-entry timer through construction
(`kemiller2002/research-documents`, `research/construction-by-design/time-entry-timer-01/results.md`,
finding F2, research-documents PR #7; tracking research-documents#6 and
time-entry-state-machine#4):
- in the construction arm, two of three agents decided it silently in
  handwritten session code. They discarded the timer, because
  `state <- decision.Next` ran whatever the Stop outcome was;
- in the baseline arm, two of three agents chose the opposite and kept the
  timer active;
- one run of six surfaced the question.

So the agent could make this decision, untracked, in code no reviewer was
looking at for it. The question this record answers is why the agent has that
ability at all.

# Decision

1. **A new vocabulary, `ordo.construction-vocabulary/2`.** Vocabulary 1 is
   unchanged and still read. Its canonical JSON, digests and generated output
   are byte-identical. Unknown versions are refused, never upgraded.
2. **Every `transitionEffect` port declares `onFailure`.** The value is one of:
   - `remain`: a `Failed` or `Unknown` outcome leaves the workflow in the
     source state;
   - `proceed`: the target state stands and the failure is reported;
   - `unresolved`: generated code picks neither outcome.

   The value is a closed union, `EffectFailureDisposition`. It is a field of
   the `TransitionEffect` case only, so no other port can carry one. It has
   no free-text, custom or handler case.
3. **An `unresolved` disposition is linked to an obligation by a checked
   name.** `declareUnresolved` gains an optional `effect` member, the command
   whose effect it concerns. It is grammar-checked and compared in normalized
   form, never matched against prose. An `unresolved` effect needs exactly
   one linked obligation. A link must name a declared effect whose
   disposition is `unresolved`. The linked obligation becomes
   `RESOLVE:<subject>` with kind `ResolveEffectFailure`.
4. **Precise refusals.** `PlanViolation` gains `NotInVocabulary`,
   `MissingEffectDisposition`, `UnresolvedEffectWithoutObligation`,
   `UnknownEffect`, `EffectAlreadyDecided` and `DuplicateEffectObligation`.
   Each names the operation by its derived label. The wire reader refuses an
   unknown `onFailure` value by name, a missing one under vocabulary 2, and
   either new member under vocabulary 1.
5. **The generator applies the disposition.** A vocabulary 2 plan selects
   `ordo.construction/2`. Tiers 1 and 2 keep their shape. Tier 3 finds the
   first effect that did not complete and applies the declared disposition
   through typed result cases:
   - `Remained`, which carries the source state;
   - `Proceeded`, which carries the target state and the failure;
   - `Unresolved`, which carries no state.

   `Accepted` and `Refused` also carry the post-command `state`, so a host
   never computes it from `decision.Next`. `Unknown` stays distinct from
   `Failed` in `EffectFailure`. A host cannot read a remained outcome as an
   acceptance: the compiler refuses building `Accepted` with a failure, and
   reading a state from `Unresolved` (FS0019).

# Alternatives

- **Keep reporting outcomes and document the question.** Rejected. The pilot
  shows that documentation did not stop the silent choice: the generated
  comment was already there.
- **A fixed default, such as always `remain`.** Rejected. A default is the
  same silent decision, moved into the generator. The baseline arm shows that
  reasonable agents disagree on the answer.
- **Make `onFailure` optional in vocabulary 1.** Rejected. It would change
  what an existing vocabulary 1 plan means and how it is encoded, and the
  documents require a new version for that.
- **Link the obligation by a normalized subject**, for example
  `<Command> effect failure`. Rejected in favor of an explicit `effect`
  member. A naming convention is easy to miss and impossible to tell apart
  from an unrelated subject that happens to match.
- **Callbacks or a handler port for failure.** Rejected. It would reintroduce
  handwritten code in exactly the place this record removes it, and an
  escape hatch the vocabulary deliberately lacks.

# Consequences

- Hosts that pin the generator must accept `ordo.construction/2` for
  vocabulary 2 plans (construction-engine-v1, Praxis #242 requirement 2). A
  `ResolveEffectFailure` obligation becomes a decision request. Until it is
  resolved, the host handles `Unresolved` explicitly.
- `PlanViolation`, `PortKindDraft`, `PortKind`, `UnresolvedDraft`,
  `UnresolvedObligation` and `ObligationKind` change shape or gain cases. F#
  consumers that match them exhaustively must be updated. Ordo releases this
  as 1.7.0, a minor version, because the wire contract adds a version
  without changing an existing one.
- `ConstructionPlan.VocabularyVersion` keeps its vocabulary 1 value, so
  existing authors keep producing vocabulary 1 plans until they opt in.

# Evidence

- Pilot finding F2 (research-documents PR #7). B1 `Completion.fs:57-70` and
  B3 `Completion.fs:60-69` discard the timer whatever the Stop outcome.
- `tests/Ordo.Construction.Tests/EffectFailureTests.fs` runs three compiled
  goldens of the time-entry timer, one per disposition of Stop. A failed or
  unknown Stop under `remain` leaves `Running` and `Paused` in place.
  `proceed` reaches `NoTimer` and reports the failure. `unresolved` picks no
  state and carries a linked `RESOLVE` obligation.
- Section 9 of `tests/Ordo.Tests/ConstructionPlanAdversarialTests.fs` holds
  the validation and wire negatives. Pinned v1 and v2 digests are in
  `tests/Ordo.Tests/ConstructionPlanTests.fs`. Compile probes show that
  `CurrentTime` cannot carry a disposition (FS0003) and that a disposition is
  not free text (FS0001).
- **Mutation check, run during development.** An emitter mutant that made
  `remain` return `decision.Next` was caught by the golden check. With the
  goldens regenerated from the mutant, four `remain` behavior tests failed.

# Known limits and open questions

- A `remain` or `proceed` disposition does not have to cite an externally
  authorized decision node, unlike `bindCapability`. The choice is now
  explicit, digest-pinned and reviewable in the plan, rather than an
  untracked line of session code. Still, the agent that writes the plan can
  make it. Requiring an authorized decision for a decided disposition, and
  `unresolved` otherwise, is the candidate next step. It needs a blueprint
  decision node per effect, and the pilot evidence does not yet show that it
  pays for itself.
- The faithful vocabulary 2 timer plan is `plan.v2-unresolved.json`. The
  `remain` and `proceed` variants are test inputs. Neither resolves the
  time-entry question, and no decision about it is recorded here.
- The disposition covers a single command's effect. Retries, compensation
  and partial completion are not expressible and stay host policy.
