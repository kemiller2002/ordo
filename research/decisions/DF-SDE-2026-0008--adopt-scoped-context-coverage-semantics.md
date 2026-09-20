---
id: DF-SDE-2026-0008
title: Adopt scoped context coverage semantics without a global completeness flag
status: accepted
type: decision-record
created: 2026-09-20
updated: 2026-09-20
tags: [architecture, ordo, gh-18, next-pass, coverage]
supersedes: []
superseded_by: []
related_documents:
  - research/packages/RP-SDE-2026-0004--ordo-next-pass-requirements.md
  - research/evidence/EV-SDE-2026-0011--ordo-next-pass-pre-upgrade-baseline.md
  - doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md
  - doctrine/FOUR-TIER-ARCHITECTURE.md
---

# DF-SDE-2026-0008

## Context

Strata demonstrates that one decision can have complete information in one dimension and partial or unknown information in another. Time Tracking Application demonstrates that unavailable authoritative reference context must block processing rather than be treated as an empty catalog.

A single request-level `Complete | Partial | Unknown` value therefore loses material information.

## Decision

Coverage is always asserted **for a contract/domain-defined scope**.

Every reusable coverage claim must carry:

- a stable or contract-local scope identity;
- `Complete | Partial | Unknown`;
- provenance/evidence supporting the assertion.

The status meanings are fixed:

- **Complete**: the declared observation method is sufficient for the named scope and the scope was fully observed at the relevant state/time.
- **Partial**: incompleteness is known. Some of the named scope was observed, but a known part was excluded, inaccessible, unsupported, or otherwise not covered.
- **Unknown**: whether the named scope was covered sufficiently has not been established.

`Unknown` and `Partial` MUST remain distinct.

Coverage dimensions and scopes are defined by the domain/contract, not globally by Ordo. Multiple scopes may apply to one decision.

Ordo MUST NOT infer Complete from context size, confidence, successful provider execution, or lack of errors.

## Representation decision

The semantic requirement is accepted now. The exact executable representation remains intentionally open for the implementation spike.

The spike must compare:

1. a first-class typed coverage claim; and
2. coverage encoded as deliberately typed Evidence.

The smaller representation that preserves policy use, auditability, wire stability, and all three statuses should be chosen and recorded before implementation is finalized.

## Four-tier placement

Coverage is a Tier 1 semantic fact derived from observations acquired by higher tiers. Tier 1/2 reason over the claim; they do not perform the search or introspection that established it.

## Consequences

A policy may require `Complete` for one scope while accepting `Partial` or `Unknown` elsewhere. Missing evidence and incomplete coverage remain different conditions.
