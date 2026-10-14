---
id: DF-SDE-2026-0029
title: State documents and code wire text in decision records version 5
status: draft
type: decision-record
created: 2026-10-14
updated: 2026-10-14
tags: [construction, decision-records, document, wire-text, construct-010]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/document-persistence.md
  - docs/architecture/refusal-codes-and-branching.md
  - docs/architecture/construction-default-plan.md
  - schemas/ordo-construction-plan.vocabulary-7.schema.json
  - src/Ordo.Core/ConstructionDecisions.fs
  - src/Ordo.Core/ConstructionDefaultPlan.fs
  - tests/Ordo.Tests/ConstructionDecisionsV5Tests.fs
provenance:
  contributions:
    EXE-20261014T082757970Z-ac7e704c:
      operations: [created]
      at: 2026-10-14T08:50:00Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record decision records version 5 and code wire text"
---

# Context

Vocabulary 7 (DF-SDE-2026-0028) could only be reached through hand-authored
plans, because no decision record could state a document. The lead also
found that a refusal code's stable external text (`ENTRY_NOT_RECORDED`) is
lost: a semantic name drops its separators. The expanded slice needs both
before its approved records can generate a plan.

# Decision

1. **Wire text in vocabulary 7.** `defineRefusalCode` may carry `wireText`
   (`[A-Za-z0-9_.:-]{1,64}`). It is stated for every code or for none, and
   it is unique. The transition tier then emits
   `wireText : Code -> string`. Plans without wire text are unchanged byte
   for byte.
2. **Decision records version 5** are version 4 plus:
   - a `document` content kind with optional `idempotency`, `onSaveConflict`
     and `onReconcileMoved`;
   - code assignments to `saveConflict`;
   - optional `wireText` on refusal codes.

   Version 5 pairs only with vocabulary 7.
3. **An absent choice is never defaulted.** It generates `unresolved` and an
   `undecidedPersistence` obligation whose `planSubject` is `document:…`. In
   session scope, reconcile cannot attribute a moved version, so an absent
   `onReconcileMoved` is `escalate`. That value is the only one the
   vocabulary accepts there.
4. **The existing obligation kind is reused.** `undecidedPersistence` covers
   document choices as well. The `planSubject` distinguishes them, so
   consumers of the obligation set gain no new kind.

# Alternatives

- **Keep wire text out of Ordo as a host mapping.** Rejected. The text is a
  requirement-level contract, and a mapping outside the plan is untraced.
- **Allow partial wire text.** Rejected. A code without text would need a
  fallback, which would be invented policy.
- **A new `undecidedDocument` obligation kind.** Rejected. Every consumer
  would have to break for no gain over `planSubject`.

# Limits

- The evidence is a synthetic fixture. The expanded slice's version 5
  records are the approver's to write.
