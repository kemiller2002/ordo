# Construction plan consensus

Status: **draft / implemented in `Ordo.Core`** (`ConstructionPlanConsensus`).
Contract owner: Ordo. Decision:
[`DF-SDE-2026-0021`](../../research/decisions/DF-SDE-2026-0021--combine-independent-plans-without-a-winner.md).
Builds on: [construction plan v1](construction-plan-v1.md).
Evidence: time-entry timer pilot 01 (research-documents PR #7, tracking
research-documents#6).

## Purpose

A construction plan takes architectural choices away from the agent that
writes it. One agent still decides which structure to propose and which
questions to raise. In the time-entry timer pilot, three independent agents
produced plans for the same cohort:
- their structural operations were identical (1 workflow, 3 states,
  5 transitions and 2 ports: 11 operations once compared canonically);
- their `declareUnresolved` questions differed (2, 3 and 4 of them);
- only one of the three asked what a failed Stop effect means.

Combining independent plans deterministically surfaces every question that at
least one planner noticed. It also turns any structural disagreement into an
explicit open question, so no single agent's choice wins. The guiding question
is why any single agent should be able to make that decision at all.

## API

```fsharp
ConstructionPlanConsensus.combine :
    PreparedCohort -> PlanDraft list -> Result<ConsensusResult, ConsensusRefusal list>
```

Pure F#. No I/O, clock, randomness or configuration.

### Inputs

`combine` refuses unless all of these hold:
- there are at least two drafts (`TooFewDrafts`);
- every draft validates on its own with `ConstructionPlan.validate prepared`.
  A draft that does not is refused as `InvalidDraft(position, violations)`,
  where `position` is its 1-based input position. It is never dropped
  silently;
- all drafts share one `schemaVersion` (`MixedSchemaVersions`) and one
  `vocabularyVersion` (`MixedVocabularyVersions`).

Because every draft is validated against the same prepared cohort, every
draft has the same binding.

### Draft numbering

Drafts are numbered from 1 in **canonical order**: sorted by each draft's own
validated plan digest. The disagreement prose, the report and the digest use
these numbers, so none of them depends on input order. The report lists each
number with its draft digest, so a caller can map an input to its number.
Identical drafts have identical digests and are interchangeable.

## Semantics

Operations are compared in canonical form, the same form
`ConstructionPlan.canonicalOperations` returns: normalized names, guards and
invariant scopes as sorted, distinct sets. Trace is ignored for identity.
Structural operations are grouped by their derived label (`operationLabel`),
such as `state:Running` or `transition:Running/Stop`. A validated draft has at
most one operation per label.

| Situation | Result |
|---|---|
| Every draft has the label, with identical operations | **Agreed.** Kept once, with trace set to the union of the drafts' traces |
| Some drafts have the label and some do not | **Structural disagreement.** Dropped. Becomes one `declareUnresolved` record |
| Every draft has the label, with different operations (a conflicting transition target, a different state role, other guards, another invariant scope) | **Structural disagreement.** Every variant is dropped and becomes one record for the label |
| Vocabulary 2: every draft has the `transitionEffect` port, differing only in `onFailure` | **Effect-disposition disagreement.** The port is kept with `onFailure: unresolved`, and the record is linked to it with `effect` |

### Disagreement records

A disagreement record is a `declareUnresolved` that the consensus writes:
- **subject:** `Disagreement` followed by the label's words, for example
  `Disagreement transition Running Stop` (normalized
  `DisagreementTransitionRunningStop`). A label too long for a name becomes
  `Disagreement <kind> H<16 hex digits of SHA-256(label)>`;
- **question:** names each distinct proposal and the drafts that made it, then
  the drafts that proposed nothing for the label, by number only. For
  example: "Draft 2 proposed defineTransition Restart from Paused to Running.
  Drafts 1 and 3 proposed nothing for transition:Paused/Restart. Consensus
  does not choose between independent drafts; a person must decide.";
- **trace:** the union of the proposing drafts' traces. A requirement that
  only a dropped operation covered stays covered, as `UnresolvedOnly`.

These records are the only operations the consensus writes. Every other
structural operation in the result is one that every draft proposed. The only
change it makes to them is the trace union and, for a disputed effect port,
setting the disposition to `unresolved`.

### Unresolved questions

The result keeps the union of every draft's `declareUnresolved` questions.
- **Deduplication.** Two questions are merged only when they are exact
  duplicates: the same normalized subject, the same `effect` and the same
  question text, compared ordinally. A merged question keeps the union of
  traces and lists every draft that raised it. Differently worded questions
  are never merged.
- **Shared subjects.** Questions with different wording but the same subject
  each get the subject `<subject> draft <n>`, where `n` is the lowest draft
  number that raised that question. A draft cannot raise two questions under
  one subject, so the suffixes are distinct. The question text is unchanged.
- **Effect links (vocabulary 2).** A question keeps its `effect` link only
  when the consensus plan keeps that effect `unresolved` and exactly one
  distinct question is linked to it. Otherwise:
  - if the drafts disputed the disposition, the effect-disposition record
    carries the link;
  - if every draft left the effect `unresolved` but linked it to differently
    worded questions, an **effect-question disagreement** record
    (`Disagreement effect question <command>`) carries the single link;
  - if the port itself was dropped, there is nothing to link to.

  In all three cases the drafts' questions are kept, unlinked. A link is
  never chosen from among them.

### Validity: conservative refusal

The combined draft must pass `ConstructionPlan.validate prepared` like any
other plan. If it does not, `combine` returns
`CombinedPlanInvalid(violations, disagreements)` and nothing else. It does not
drop more operations, invent a replacement or pick a variant to make the plan
valid. The disagreements go to a person.

This is what happens when dropping a disputed operation breaks the structure:
- **Conflicting `Start` target.** The initial state's only transition is
  dropped, so the other states become unreachable (`UnreachableState`).
- **Differing state role.** The state is dropped, but transitions every draft
  agreed on still name it (`UnknownState`). A state-role disagreement
  therefore always refuses, unless no agreed operation references the state.
- **Differing workflow name.** No workflow remains (`MissingWorkflow`).

`CombinedPlanInvalid` does not depend on input order: its violations are
sorted and its disagreements use canonical draft numbers. Only
`InvalidDraft` names an input position, because a draft that does not
validate has no digest to number it by.

### Report: `ConsensusResult`

| Member | Contents |
|---|---|
| `Plan` | the combined `ValidatedPlan` |
| `Digest` | its digest (`ConstructionPlan.digest`) |
| `Drafts` | each draft's number and its own validated digest |
| `Agreed` | labels of the agreed structural operations, sorted |
| `Disagreements` | each disagreement's kind, label, subject, proposals (variant and drafts), proposing drafts and non-proposing drafts |
| `Unresolved` | each kept question's subject, original subject, effect link, text and the drafts that raised it |

`ConsensusResult` has a private representation. Only `combine` constructs
one, so a report cannot be fabricated around a plan.

## Determinism

The result, including its digest, does not depend on the order of the input
drafts. Drafts are numbered by digest, labels are processed in sorted order,
and every list in the report is sorted. The tests run every permutation of
three drafts, for both the pilot plans and a set of drafts with
disagreements, and compare whole results. A refused set is permuted too.

**Idempotence.** The consensus of identical drafts is that plan, with the same
digest. The consensus of drafts that differ only in trace is the plan with the
traces unioned.

## No majority vote

Majority voting is explicitly rejected. If two of three drafts propose an
operation and one does not, the operation is not in the plan. It becomes a
disagreement, the same as when only one draft proposes it. Two agreeing
agents can share a bias, for example the same model, prompt or reading of
an ambiguous source. Their agreement is not evidence that the dissent is
wrong, and the pilot shows that the rarest observation, raised by one draft
in three, was the one that mattered. A test asserts this
(`two agreeing drafts do not outvote one dissent`).

## No new escape hatches

- The consensus adds nothing to the vocabulary. It writes no code string,
  path, package, shell command or validation-policy change. Its only output
  is a plan in the existing vocabulary, which `validate` checks in full.
- It never writes a structural operation that no draft proposed. The only
  operations it writes are the `declareUnresolved` disagreement records.
- It never decides an effect disposition. A disputed disposition becomes
  `unresolved`, which settles nothing.
- It binds exactly the drafts' cohort and adds no decision.

## Authority

A consensus result is not authority, approval or verification, just as a
validated plan is not. A consensus without disagreements is still just a
plan: agreement between agents is not an external decision. Hosts pin the
plan digest, recheck approvals and control every effect, exactly as for a
plan from a single agent.

## Pilot result

The three pilot plans, B1, B2 and B3, are copied verbatim as fixtures under
`tests/fixtures/construction/time-entry-timer-pilot-01/`. They bind the timer
cohort: manifest `sha256:03e29edd…5a01`, canonical blueprint
`sha256:28dd3964…c4cb`. Their consensus:
- 11 agreed structural operations;
- 0 disagreements;
- all 9 unresolved questions kept, including B2's "Stop completion failure";
- a valid plan, digest
  `sha256:bac1177f3f61cebc14555d85ba2988118dc3c7f987590dd4689fffd6c7a12349`.

The draft digests match the ones the pilot's structural verification
reported (results.md).

## Not covered

- There is no CLI command. The `sde` CLI has no construction-plan command
  family, and a flag that approves decisions would be a new authority surface.
- The consensus does not judge whether drafts are independent. It assumes the
  host ran them independently; identical drafts simply agree.
- Agreement on a question's subject is not detected unless the wording is
  identical. Two questions about the same issue in different words are both
  kept, which is the intended, conservative outcome.

## Evidence

- `tests/Ordo.Tests/ConstructionPlanConsensusTests.fs`: the pilot,
  synthetic disagreements, refusals, permutations and idempotence.
- `tests/Ordo.Tests/ConstructionPlanAdversarialTests.fs`: `validate` and
  `ConsensusResult.Plan` are the only public producers of a `ValidatedPlan`,
  and `combine` is the only producer of a `ConsensusResult`.
