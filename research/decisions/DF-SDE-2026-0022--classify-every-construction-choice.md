---
id: DF-SDE-2026-0022
title: Classify every construction choice; generate only required, approved and implied choices
status: draft
type: decision-record
created: 2026-10-13
updated: 2026-10-13
tags: [construction, classification, construct-010, opt-14]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/decision-classification.md
  - docs/architecture/construction-plan-v1.md
  - docs/architecture/construction-default-plan.md
  - src/Ordo.Core/ConstructionClassification.fs
  - tests/Ordo.Tests/ConstructionClassificationTests.fs
  - tests/Ordo.Construction.Tests/ClassifiedConstructionTests.fs
provenance:
  contributions:
    EXE-20261013T204919043Z-6855fd51:
      operations: [created]
      at: 2026-10-13T20:49:43.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record CONSTRUCT-010 phase 4 decision classification"
---

# Context

CONSTRUCT-008 found two ways an undecided choice reached generated or agent
code as if it had been decided:

1. **Closed-world transitions.** These were pre-registered as limitation 1
   and observed on Chrona and Summa.
2. **Implement obligations with an undecided rule inside.** This is OPT-14:
   the timer's rounding tie, D7, which arm C resolved silently in 3 of 3 runs.

CONSTRUCT-010 phase 4 asks that every semantic choice be classified, that
only defensible choices be generated, and that unresolved decisions be
unrepresentable as completed construction.

# Decision

1. **Five classes.** Every choice of a validated plan is `required`,
   `approved`, `implied`, `unresolved` or `unsupported`
   (`ConstructionClassification.classify`). Only the first three generate.
   `CompletePlan` has a private constructor, and `complete` returns it only
   when nothing is unresolved or unsupported.
2. **Vocabulary 3.** It adds:
   - a mandatory `declarePairRule`: unlisted pairs are either refused by a
     cited rule or undecided;
   - `defineRefusal`;
   - `declareUndecidedTransition`;
   - a mandatory `declareExtensionInventory` for every guard, invariant and
     effect.

   An unlisted pair is therefore never decided by omission, and an extension
   cannot be handed off without stating its open choices.
3. **Generated code** (`ordo.construction/3`) reports an open pair as
   `Undecided`, a distinct outcome that is never `InvalidTransition`. An
   extension with an undecided point gets a decide obligation and no implement
   obligation.
4. **Decision records version 2** state the same things for default plans.
   Records version 1 with vocabulary 3, and records version 2 with
   vocabularies 1 or 2, are refused rather than bridged.
5. **Earlier vocabularies are not changed in place.** Their output, canonical
   forms and digests are unchanged. Classification reports their omissions as
   `unsupported`, so a vocabulary 1 or 2 plan with any unlisted pair is never
   `complete`.

# Alternatives

- **Let the agent decide undecided pairs and report them.** Rejected: that is
  the behavior OPT-14 measured as a regression.
- **Treat unlisted pairs as undecided by default, with no rule.** Rejected:
  the time-entry requirements do state a rejection rule (REQUIREMENTS.md
  L367-368). Ignoring it would manufacture open questions that the text
  answers. The rule must be stated and cited.
- **Detect hidden ambiguity in requirement text automatically.** Rejected:
  this is not mechanically possible. Instead the inventory is mandatory, so
  the operator approving the records must state each decision point. Omission
  is not representable, but a wrongly declared "decided" point is still an
  authoring error that review must catch.
- **Bump the generator version for earlier vocabularies.** Rejected: it would
  break byte identity of every existing default plan and construction with no
  semantic change for them.

# Consequences

- The F# source of `ConstructionDecisions.RecordedTransitionEffect` (a new
  `decisionPoints` element) and of `StateModelContent` (`Unlisted`,
  `Refusals`, `Undecided`) changes. Code that constructs these values directly
  must add the new fields. The JSON wire for decision records version 1 and
  for plan vocabularies 1 and 2 is unchanged.
- Ordo moves to 1.11.0 (minor): new wire versions and union cases, no changed
  existing wire meaning.
- Unresolved and unsupported choices now hold back completion.
  CONSTRUCT-010's coverage report (phase 7) counts them; it does not hide them.

# Evidence

On the real timer cohort, vocabulary 3 reduces open choices from 10 to 3:
- the effect failure disposition;
- the rounding tie;
- the uncovered session requirement.

The 7 rejected pairs become `implied` by the cited rule. The `TimerOpen`
golden returns `Undecided` for all 7 unlisted pairs and `InvalidTransition`
for none. Three mutants of the invariants each failed the suite. See
`docs/architecture/decision-classification.md`.
