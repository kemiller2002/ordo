---
id: DF-SDE-2026-0027
title: Persist workflow instances with stated idempotency scope and stale-write action
status: draft
type: decision-record
created: 2026-10-13
updated: 2026-10-13
tags: [construction, vocabulary, persistence, idempotency, construct-010]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/persisted-instances.md
  - schemas/ordo-construction-plan.vocabulary-6.schema.json
  - src/Ordo.Construction/Emit.fs
  - tests/Ordo.Construction.Tests/PersistenceTests.fs
  - tests/Ordo.Tests/ConstructionVocabulary6Tests.fs
provenance:
  contributions:
    EXE-20261013T224925726Z-c1b416b1:
      operations: [created]
      at: 2026-10-13T22:50:01.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record CONSTRUCT-010 phase 2 increments 3 and 4 persisted instances"
    EXE-20261013T231252409Z-6e7aa000:
      operations: [modified]
      at: 2026-10-13T23:12:55.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Decision records version 4 state effect policies and persistence"
---

# Context

CONSTRUCT-010 traced a first-pass idempotency failure to one cause: the
"already landed?" lookup consulted session memory only, so a retry after
reload wrote twice. The repositories surveyed disagree on idempotency scope
and on what a stale revision means. The lead and the pattern session asked
for persisted aggregates, idempotency and reconciliation as one persistence
protocol.

# Decision

1. **Add vocabulary 6**, with one `declarePersistence` stating idempotency
   scope (durable, session or unresolved) and the stale-write action (refuse,
   escalate or unresolved). Codes may target `notFound` and `staleRevision`.
2. **Generate the protocol, not the policy.** `execute` loads, deduplicates,
   decides through Tier 2, and saves with a revision compare-and-swap.
3. **Durable scope stores applied operation ids with the instance.** An
   unknown save is settled by reading back: recorded means executed;
   unchanged means not saved; moved means stale. It is never assumed failed
   and never written again.
4. **Session scope deduplicates in a caller-held set and escalates an unknown
   save.** An unresolved scope generates no deduplication and records the
   open question.
5. **Rebasing and silently doing nothing are not expressible.**
6. **Persistence with transition effects is refused.** It would be a dual
   write.

# Alternatives

- **Default to durable scope.** Rejected. It would be invented policy; the
  scope must be decided.
- **Retry an unknown save.** Rejected. It can apply twice, which is the
  measured defect.
- **Allow persistence with effects now.** Deferred until an outbox or an
  equivalent single write is designed.

# Limits

- The evidence is a synthetic fixture.
- Decision records version 4 state persistence and effect policies; an
  absent choice stays unresolved. They generate vocabulary 6 default plans.
