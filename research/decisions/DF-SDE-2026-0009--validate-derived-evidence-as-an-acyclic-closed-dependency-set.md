---
id: DF-SDE-2026-0009
title: Validate derived evidence as an acyclic closed dependency set
status: accepted
type: decision-record
created: 2026-09-20
updated: 2026-09-20
tags: [architecture, ordo, gh-18, next-pass, evidence]
supersedes: []
superseded_by: []
related_documents:
  - research/packages/RP-SDE-2026-0004--ordo-next-pass-requirements.md
  - research/evidence/EV-SDE-2026-0011--ordo-next-pass-pre-upgrade-baseline.md
  - doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md
  - doctrine/FOUR-TIER-ARCHITECTURE.md
---

# DF-SDE-2026-0009

## Context

`EvidenceKind.Derived` already names the input evidence IDs used by a repeatable computation. HelixNote demonstrates that cycles are a real failure class, but not every historical relationship is a derivation relationship. A blanket "all relationships must form a DAG" rule would be false.

## Decision

The executable next pass will validate **Derived-from relationships only** as a directed acyclic dependency relation.

For a caller-supplied closed evidence set, validation must detect:

- missing referenced evidence;
- direct self-dependency;
- multi-node derivation cycles.

It must also be able to compute the transitive dependency closure of selected evidence.

Shared dependencies are legal.

This validator does **not** impose DAG semantics on correction history, provenance references, historical observations, or arbitrary domain relationships. Those topologies keep their own semantics.

Supersession used to determine an effective-current record is a separate lifecycle concern. Where an effective-current projection depends on supersession, that projection must refuse a supersession cycle, but that rule is not part of the evidence-derivation validator.

A later observation may depend on an earlier result when it has a distinct later evidence/resolution identity. Circular self-justification within the same derivation chain is prohibited.

## Rejected alternatives

- General graph infrastructure.
- A universal relationship validator.
- W3C PROV/RDF production machinery.
- Treating temporal feedback as a derivation cycle when it is actually a later observation.

## Four-tier placement

Closure/cycle validation is pure Tier 1/2 logic over supplied values and has no external store.

## Consequences

The implementation should be a small deterministic utility over current evidence IDs, not a storage subsystem.
