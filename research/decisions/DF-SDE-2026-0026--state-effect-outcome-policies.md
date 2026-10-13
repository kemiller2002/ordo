---
id: DF-SDE-2026-0026
title: State what failed and unknown effect outcomes mean, separately, in plan vocabulary 5
status: draft
type: decision-record
created: 2026-10-13
updated: 2026-10-13
tags: [construction, vocabulary, effects, reconciliation, construct-010]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/effect-outcome-policies.md
  - schemas/ordo-construction-plan.vocabulary-5.schema.json
  - src/Ordo.Core/ConstructionPlan.fs
  - src/Ordo.Construction/Emit.fs
  - tests/Ordo.Tests/ConstructionVocabulary5Tests.fs
  - tests/Ordo.Construction.Tests/EffectPolicyTests.fs
provenance:
  contributions:
    EXE-20261013T223426040Z-917561db:
      operations: [created]
      at: 2026-10-13T22:35:02.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record CONSTRUCT-010 phase 2 increment 2 effect outcome policies"
    EXE-20261013T231252409Z-6e7aa000:
      operations: [modified]
      at: 2026-10-13T23:12:54.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Decision records version 4 state effect policies and persistence"
---

# Context

Vocabulary 2's single `onFailure` treats a definite failure and an unknown
outcome alike. Retrying is safe after a definite failure. After an unknown
outcome it can apply the effect twice, which is the failure mode CONSTRUCT-010
measured for operation idempotency. Reconciliation, compensation and
escalation existed only in host code, unstated and unchecked.

# Decision

1. **Add vocabulary 5.** A port carries no `onFailure`, and each effect has
   exactly one `declareEffectPolicy`, with separate `onFailed` and
   `onUnknown` actions and 0 to 5 retries.
2. **Never retry an unknown outcome.** The vocabulary and the generated
   union cannot express it.
3. **An unknown outcome is reconciled by host code or escalated.** Not
   applied is handled as a definite failure, and still unknown is escalated.
4. **Compensation only with a cited, bound decision.** Compensation that does
   not complete is escalated.
5. **No state for a person's decision.** Generated code chooses no state for
   an escalated or unresolved outcome.

# Alternatives

- **Extend `onFailure` with more values.** Rejected: it keeps the
  failed/unknown conflation.
- **Allow retry on unknown when the effect is idempotent.** Deferred.
  Idempotency scope is increment 4, and until it is stated a retry on unknown
  is a guess.
- **Let host code choose a state on escalation.** Rejected: that moves an
  undecided choice into handwritten code, unclassified.

# Limits

- Default plans state policies through decision records version 4, which
  generate vocabulary 6 (vocabulary 5 plus persistence).
- The evidence is the real timer cohort with test-approved policies, not
  operator-approved ones.
