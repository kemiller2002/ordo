---
id: DF-SDE-2026-0024
title: Type refusal codes and branching transitions in plan vocabulary 4
status: draft
type: decision-record
created: 2026-10-13
updated: 2026-10-13
tags: [construction, vocabulary, refusal-codes, branching, construct-010]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/refusal-codes-and-branching.md
  - schemas/ordo-construction-plan.vocabulary-4.schema.json
  - src/Ordo.Core/ConstructionPlan.fs
  - src/Ordo.Construction/Emit.fs
  - tests/Ordo.Tests/ConstructionVocabulary4Tests.fs
  - tests/Ordo.Construction.Tests/CodedBranchingConstructionTests.fs
provenance:
  contributions:
    EXE-20261013T210230431Z-892ee45c:
      operations: [created]
      at: 2026-10-13T21:20:17.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record CONSTRUCT-010 phase 2 vocabulary 4: typed refusal codes and branching transitions"
---

# Context

CONSTRUCT-010 phase 2 asks Ordo to generate more of a cohort without
inventing policy. In vocabularies 1 to 3, two common requirement shapes could
not be stated:

- **Typed refusal codes.** For example, "raising an open gate reports
  AlreadyOpen", or the timer source's `TIMER_TOO_SHORT`. Generated code
  reported only `InvalidTransition` or `GuardRejected`, so any code was
  invented in handwritten code, without a trace.
- **Commands whose target depends on what the host observes.** Only single
  targets existed.

# Decision

1. **Add vocabulary 4.** It adds `defineRefusalCode`, `assignRefusalCode`
   (to a refused pair, to the unlisted pairs, or to a guard),
   `defineBranchingTransition` (at least two targets), and a `selection`
   extension kind. Earlier vocabularies keep their canonical forms and
   digests. Their generators are unchanged.
2. **A code goes only to something that refuses.** Assigning one anywhere
   else is a validation error. An unassigned refusal reports `None`. There is
   no default code, because a default would be invented policy.
3. **A selector picks from a generated closed union of the plan's
   targets.** It is a handwritten extension with a mandatory decision
   inventory, and the type system stops it returning another state. An
   undecided point turns its `IMPLEMENT` obligation into `DECIDE`.
4. **Generator 4 is generator 3 for plans that use neither feature.** A test
   compares the F# tiers byte for byte, with only the generator name and
   plan digest neutralized.

# Alternatives

- **Guards that carry codes.** Rejected: one guard can protect several
  pairs, and a pair can be refused without a guard. Code assignment by
  target covers both.
- **A selector that returns a state.** Rejected: it could return a state the
  plan does not allow. The closed union makes that a compile error.
- **Several `defineTransition` operations for one pair.** Rejected: that is
  ambiguous with a duplicate definition. It would also need an ordering or
  priority rule, which would be invented policy.

# Limits

- Default plans do not generate vocabulary 4 yet. Decision records `/3` are
  the next increment.
- The evidence uses a synthetic fixture. No real cohort has been generated
  in vocabulary 4 yet.
