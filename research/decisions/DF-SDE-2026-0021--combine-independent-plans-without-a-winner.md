---
id: DF-SDE-2026-0021
title: Combine independent construction plans without choosing a winner
status: draft
type: decision-record
created: 2026-10-12
updated: 2026-10-12
tags: [construction, consensus, plans, unresolved, plan-consensus]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/construction-plan-consensus.md
  - docs/architecture/construction-plan-v1.md
  - research/decisions/DF-SDE-2026-0018--constrain-construction-to-a-typed-vocabulary.md
  - research/decisions/DF-SDE-2026-0020--require-a-declared-effect-failure-disposition.md
  - src/Ordo.Core/ConstructionPlanConsensus.fs
  - tests/Ordo.Tests/ConstructionPlanConsensusTests.fs
  - tests/fixtures/construction/time-entry-timer-pilot-01/README.md
provenance:
  contributions:
    EXE-20261012T212905057Z-9b1bcf75:
      operations: [created]
      at: 2026-10-12T21:45:37.815Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record the construction-plan consensus decision (WI-0044)"
      evidence: [tests/Ordo.Tests/ConstructionPlanConsensusTests.fs]
---

# Context

A construction plan (DF-SDE-2026-0018) limits what an agent can express. One
agent still decides which structure to propose and which questions to raise.

The pre-registered time-entry timer pilot
(`kemiller2002/research-documents`,
`research/construction-by-design/time-entry-timer-01/results.md`,
research-documents PR #7, tracking research-documents#6) ran three
independent construction agents on the same cohort. Their plans, B1, B2 and
B3:
- had identical structural operations: 1 workflow, 3 states, 5 transitions
  and 2 ports, which is 11 operations once compared canonically;
- differed only in their `declareUnresolved` questions, 2, 3 and 4 of them;
- included one runtime question, what a failed Stop effect means, that only
  B2 raised.

Any single plan would have lost at least one question that another planner
noticed. Had the plans disagreed structurally, any single plan would also have
settled that disagreement silently. The question this record answers is why
any single agent should be able to make that decision at all.

# Decision

1. **`ConstructionPlanConsensus.combine`** in `Ordo.Core` combines two or
   more drafts for one prepared cohort. It is pure and deterministic. Each
   draft must validate on its own, and all must share one schema and
   vocabulary version. A draft that does not validate is refused by position
   and never dropped.
2. **Unanimity, not majority.** A structural operation is kept only when every
   draft proposed it identically, compared canonically with trace ignored. Its
   trace becomes the union of the drafts' traces. An operation that some
   drafts proposed and others did not, and every variant of a label the
   drafts proposed differently (a conflicting transition target or a
   differing state role), is not chosen. It becomes one `declareUnresolved`
   disagreement record. The record names each proposal and its drafts by
   number, and carries the union of the proposing drafts' traces.
3. **Every question is kept.** The result keeps the union of all drafts'
   unresolved questions. Only exact duplicates are merged: the same
   normalized subject, effect and question text. Differently worded questions
   that share a subject are disambiguated by draft number, never merged.
4. **A disputed effect disposition is not decided.** Under vocabulary 2, a
   `transitionEffect` port that every draft declared with different
   `onFailure` values is kept as `unresolved`. It is linked to the
   disagreement record by DF-SDE-2026-0020's `effect` rule. When every draft
   left an effect unresolved but linked differently worded questions, all of
   those questions are kept unlinked and one disagreement record carries the
   link.
5. **Invalid after dropping means refused.** The combined plan must pass
   `ConstructionPlan.validate`. If dropping a disputed operation leaves it
   invalid, for example an unreachable state or a transition into a dropped
   state, `combine` returns `CombinedPlanInvalid` with the violations and the
   disagreement list. It does not patch the plan.
6. **Order independence.** Drafts are numbered by their own validated digest,
   so the plan, its digest, the prose and the report do not depend on input
   order.
7. **No authority.** A consensus result is a validated plan, not authority,
   approval or verification. A consensus without disagreements is still just a
   plan. `ConsensusResult` has a private representation, so only `combine`
   can produce one.

# Alternatives

- **Majority vote.** Rejected explicitly. Two agreeing agents can share a
  bias: the same model, prompt, context or misreading of an ambiguous source.
  Their agreement is not evidence against the dissent. In the pilot, the
  question that mattered most was raised by one agent in three, and a vote
  would treat such a minority as noise. Under this decision, one dissent is
  enough to keep a construct out of the plan and in front of a person.
- **Pick a representative draft**, for example the one with the most
  questions. Rejected. This is a winner by another name, and it loses the
  questions only the other drafts raised.
- **Union of structure.** Rejected. It would build constructs that some
  planners did not propose. A transition one agent added is a decision the
  others did not make.
- **Repair an invalid combined plan**, for example by dropping states that
  became unreachable. Rejected. Each repair is a structural decision the
  consensus would be making on its own.
- **Merge similar questions.** Rejected. Deciding that two differently worded
  questions are the same question is itself a judgment. Exact duplicates only.
- **A `plan consensus` CLI command.** Deferred. The `sde` CLI has no
  construction-plan command family, and a command-line flag that approves
  decisions would be a new authority surface (`Sde.Cli.Ecir` already refuses
  to grant approval by flag). The library and its tests are the contract.

# Consequences

- A host can run several independent planners and pass their drafts to
  `combine`. Structure that all of them agree on is constructed, and anything
  else becomes an open obligation, through the existing `RESOLVE:` path.
- Disagreement records are ordinary `declareUnresolved` operations. They need
  no vocabulary change, and generators and hosts treat them like any other
  open obligation.
- Agreement can only remove constructs, never add them. With more planners,
  more questions surface and more disputed structure is held back. That cost
  is intended.
- `Ordo.Core` gains a public module. No existing type changes shape, and no
  version is bumped by this change.

# Evidence

- Pilot consensus: `tests/Ordo.Tests/ConstructionPlanConsensusTests.fs`
  combines the verbatim B1, B2 and B3 plans
  (`tests/fixtures/construction/time-entry-timer-pilot-01/`, file hashes
  pinned). It finds 11 agreed operations, 0 disagreements and all 9
  questions, including B2's "Stop completion failure". The digest is pinned:
  `sha256:bac1177f3f61cebc14555d85ba2988118dc3c7f987590dd4689fffd6c7a12349`.
  The per-draft digests equal those in the pilot's results.md.
- Synthetic disagreements cover:
  - an extra transition;
  - two drafts against one, with no majority;
  - a conflicting target;
  - a conflict that leaves states unreachable (refused);
  - a differing state role (refused);
  - `remain` against `unresolved`, and `remain` against `proceed`;
  - differently linked questions;
  - differently worded questions under one subject;
  - an invalid draft, a single draft, and mixed vocabulary and schema
    versions (all refused).
- Every permutation of three drafts gives an identical result, for the pilot
  and for a disagreeing set. A refused set gives an identical refusal.
- **Mutation check, run during development.** Replacing unanimity with a
  strict majority made `two agreeing drafts do not outvote one dissent` fail.

# Known limits and open questions

- The consensus cannot tell whether drafts were produced independently.
  Copies of one draft agree with each other trivially.
- Disagreement prose is bounded by the 1000-character question limit. With
  many drafts and long names, a record can exceed it. The combined plan is
  then refused (`QuestionTooLong`) rather than truncated.
- Two questions about the same issue in different words are both kept.
  Telling them apart is left to the person resolving them.
