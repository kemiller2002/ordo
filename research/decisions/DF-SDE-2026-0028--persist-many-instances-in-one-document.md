---
id: DF-SDE-2026-0028
title: Persist many workflow instances in one document with explicit save and reconcile
status: draft
type: decision-record
created: 2026-10-13
updated: 2026-10-13
tags: [construction, vocabulary, persistence, idempotency, document, construct-010]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/document-persistence.md
  - schemas/ordo-construction-plan.vocabulary-7.schema.json
  - src/Ordo.Construction/Emit.fs
  - tests/Ordo.Construction.Tests/DocumentTests.fs
  - tests/Ordo.Tests/ConstructionVocabulary7Tests.fs
provenance:
  contributions:
    EXE-20261013T235024451Z-a617154b:
      operations: [created, modified]
      at: 2026-10-13T23:52:00Z
      last: 2026-10-14T00:20:00Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record CONSTRUCT-010 phase 2 vocabulary 7 document persistence"
---

# Context

Vocabulary 6 persists one instance per command. The lead checked the expanded
CONSTRUCT-010 slice against its frozen contract, which has a different
architecture:

- many entries in one document;
- an explicit save under one operation id, with a version compare-and-swap;
- an explicit reconcile that reports applied, not applied, still unknown or
  conflict;
- no new semantic operation after an unknown outcome until the stored state
  is inspected.

Vocabulary 6 cannot express this, so the slice's persistence requirements
fall outside the vocabulary. Ordo's design follows the contract's semantics,
not its names, and is tested on its own synthetic fixture.

# Decision

1. **Add vocabulary 7.** It has one `declareDocument` that states:
   - the idempotency scope of save operation ids: durable, session or
     unresolved;
   - what a save against a moved document means: refuse, escalate or
     unresolved.

   Codes may target `saveConflict`. With a document, `notFound` means a
   missing entry, and `staleRevision` means an entry revision other than the
   one the caller acted on.
2. **Generate the protocol, not the policy.** `<W>.Document.g.fs` has pure
   functions over a session (stored, working, pending):
   - `load`, `create` and `apply` act in memory;
   - `save` writes once under the caller's operation id;
   - `reconcile` reads only.
3. **Unknown is never collapsed.** A write failure not proven to be without
   effect is `OutcomeUnknown` and marks the save pending. Every change and
   every save is refused (`Unreconciled`) until `reconcile` inspects the
   stored document.
4. **Durable scope reads the store's record of landed operations,** which
   maps each operation to the version it produced. A retry of a landed save
   reports `AlreadySaved` with that version and writes nothing.
5. **A save conflict and a moved version found by reconcile are separate
   choices.** They are `onSaveConflict` and `onReconcileMoved` (conflict,
   escalate or unresolved). A product can decide that conflicts are never
   merged while leaving open what a moved version means at reconcile.
   Session scope cannot attribute a moved version, so it must state
   `escalate`. An unresolved scope leaves the case unresolved.
6. **Merging and overwriting are not expressible.** A document cannot be
   combined with persistence or with transition effects.

# Alternatives

- **Extend vocabulary 6 with a document option.** Rejected. Its generated
  path is one instance per command, so the two protocols would share names
  but not meaning.
- **Settle an unknown save inside `save` by reading back,** as vocabulary 6
  does. Rejected for documents. The contract makes reconcile an explicit,
  caller-visible step, and nothing may happen before it.
- **Make the reconcile-conflict rule fixed.** Rejected. Whether a moved
  version without the operation is a conflict is an open question in the
  slice, so it stays the plan's typed choice.
- **One field for both conflicts.** Rejected after the lead's review. The
  slice decides save conflicts but leaves the reconcile case open, and one
  field would force both to be decided together or left open together.

# Limits

- The evidence is a synthetic fixture.
- No decision records state a document yet, so default plans cannot generate
  vocabulary 7. That needs records version 5.
- Validating the stored document is extension work.
