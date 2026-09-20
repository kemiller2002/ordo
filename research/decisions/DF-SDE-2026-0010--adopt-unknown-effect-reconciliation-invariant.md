---
id: DF-SDE-2026-0010
title: Adopt unknown external-effect outcome as a reconciliation invariant
status: accepted
type: decision-record
created: 2026-09-20
updated: 2026-09-20
tags: [architecture, ordo, gh-18, next-pass, effects, reconciliation]
supersedes: []
superseded_by: []
related_documents:
  - research/packages/RP-SDE-2026-0004--ordo-next-pass-requirements.md
  - research/evidence/EV-SDE-2026-0011--ordo-next-pass-pre-upgrade-baseline.md
  - doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md
  - doctrine/FOUR-TIER-ARCHITECTURE.md
---

# DF-SDE-2026-0010

## Context

Time Entry State Machine, Time Tracking Application, Strata, and HelixNote independently implement the same safety pattern: an external write/deploy may have happened even when the caller did not receive a definitive result. Treating that state as failure and retrying can duplicate or corrupt effects.

## Decision

When a state-changing external effect has been attempted and the system cannot establish whether it occurred:

1. the semantic outcome is **Unknown**, not Failed;
2. an explicit reconciliation obligation remains outstanding;
3. blind retry or blind compensation is prohibited when safety depends on whether the first effect occurred;
4. reconciliation observes the external system before Tier 2 determines the next legal action.

Retry without prior reconciliation is allowed only when the external operation's **actual contract** proves retry safety. A belief that the call "should be idempotent" is insufficient.

Acceptable proof may include a stable idempotency key/operation identity, compare-and-set/create-only semantics, or an externally documented idempotent operation whose repeated execution is semantically equivalent for this use case.

Even when retry is technically idempotent, reconciliation is still required before a later action whose legality depends on knowing what actually occurred.

## Representation

The obligation must be semantically explicit. The executable implementation should prefer a named reconciliation obligation, or equivalently closed semantic representation, over an unstructured `Custom "reconcile"` label for generic Ordo flows.

The exact effect-state machine remains application-owned.

## Four-tier placement

- Tier 2 requests an effect and interprets `Succeeded | Failed | Unknown`.
- Tier 3 coordinates the boundary.
- Tier 4 performs/probes the external effect.
- Tier 1/2 holds the resulting semantic state and obligation.

Tier 1/2 never performs the probe.

## Rejected alternatives

- Automatic retry of every transport failure.
- A universal Ordo effect/workflow engine.
- Treating unknown as failed for operational convenience.

## Consequences

Ordo gains a shared safety invariant without absorbing application-specific storage, merge, compensation, or reconciliation mechanics.
