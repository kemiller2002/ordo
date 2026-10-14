---
id: DF-SDE-2026-0025
title: Link approved decisions to the blueprint conflicts they settle
status: draft
type: decision-record
created: 2026-10-13
updated: 2026-10-13
tags: [construction, decisions, conflicts, ledger, construct-010]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/construction-default-plan.md
  - src/Ordo.Core/ConstructionDecisions.fs
  - src/Ordo.Core/ConstructionDefaultPlan.fs
  - src/Ordo.Core/ConstructionLedger.fs
  - tests/Ordo.Tests/ConstructionConflictResolutionTests.fs
provenance:
  contributions:
    EXE-20261013T215345974Z-e4c4e8f2:
      operations: [created]
      at: 2026-10-13T21:54:13.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record the typed decision-to-conflict link for CONSTRUCT-010"
---

# Context

A blueprint conflict stays an open obligation unless something says an
approved decision settles it. Before this change, nothing typed could say
that. On the expanded time-entry slice, accepted decisions are recorded only
as prose in node justifications, so their conflicts are escalated as open.

Three consumers need one representation of the link:
- the default plan and the ledger;
- pattern recognition (ordo#77);
- Praxis handoff.

# Decision

1. **Add a `conflictResolution` content kind for decision records:**
   `{ conflicts[], requirementKeys[] }`. It is accepted in decisions `/1` and
   `/2`, and is additive.
2. **Generation checks structure only.** The node must be an authorized
   decision of the cohort. Each conflict must be a blueprint conflict that
   shares a requirement key with the resolution. The keys must be owned by
   the node and assigned to the cohort. Whether the decision answers the
   conflict's question remains the approver's responsibility.
3. **Effect.**
   - A settled conflict emits no conflict obligation.
   - The default plan lists `resolvedConflicts`, only when non-empty.
   - The ledger counts the deciding node as an approval.
   - A settled conflict alone no longer makes a requirement a genuine domain
     decision.
4. **A decision outside the cohort's authorized scope cannot resolve
   anything.** Its record is refused, as for every content kind. This is
   stricter than "changes nothing": an unauthorized claim fails closed.

# Alternatives

- **Infer the link from blueprint prose.** Rejected. It would be invented
  authority.
- **A typed edge in ECIR.** Deferred. Decision records are already
  grant-bound by digest and owned by the approver, and ECIR is frozen for the
  measured slices.
- **Check that the decision answers the conflict's question.** Not possible
  mechanically. A shared requirement key is the minimum tie that can be
  enforced.

# Limits

The frozen expanded slice has no decision node for its accepted idempotency
decisions, so the tests add one as test input. Using the link on that slice
needs a new ECIR revision with typed decision nodes. Separately, conflict O8
asks what a reused operation id does against a changed week. The
durable-scope decision does not obviously answer that question, and the
approver should confirm before linking it.
