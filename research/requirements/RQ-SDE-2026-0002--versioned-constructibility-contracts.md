---
id: RQ-SDE-2026-0002
title: Versioned constructibility and decision contracts
status: draft
version: 1.0.0
owners: [ordo-maintainers]
created: 2026-10-18
updated: 2026-10-18
priority: critical
related_documents: []
tags: [reliability, contracts, decisions, compatibility]
derived_from: [INPUT-8a37a8766a122b40]
provenance:
  contributions:
    EXE-20261018T124842421Z-f3f0c468:
      operations: [created, modified]
      at: 2026-10-18T12:53:00.745Z
      last: 2026-10-18T12:54:02.702Z
      actor:
        kind: agent
        id: openai/codex
        provider: openai
        model: unknown
        runtime: codex
      reason: "Canonical ingestion of claimed requirement source"
---

# Versioned constructibility and decision contracts

This draft canonically ingests REL-ORDO-001. Ingestion records the obligation
and is not proof of implementation.

The source requirement family is REL-ORDO-001.

## Requirements

- **ORD-REL-001:** Publish a canonical, deterministic, versioned machine-readable contract for requirements, dependencies, decisions, conflicts, ownership, and expected verification.
- **ORD-REL-002:** Validate that every referenced decision ID exists, has the correct node type and cohort, and satisfies required approval/resolution rules.
- **ORD-REL-003:** Detect unresolved or cyclic dependencies, duplicate requirement IDs, missing ownership, unsupported schema versions, and contradictory decisions before work starts.
- **ORD-REL-004:** Define explicit supported-version negotiation and migration behavior; never silently reinterpret incompatible contracts.
- **ORD-REL-005:** Emit a stable contract digest and traceability links from every requirement to its evidence obligations.
- **ORD-REL-006:** Publish valid, invalid, backward-compatible, and cross-version contract fixtures consumable by Praxis and Dokimos.
- **ORD-REL-007:** Keep validation deterministic and side-effect-free; add no runtime dependency without justification.

## Acceptance criteria

Invalid decision IDs and unsupported versions fail at intake with
machine-readable diagnostics. Identical input yields the same normalized
contract and digest. Consumer compatibility tests pass.

## Consumers

Praxis and Dokimos consume this contract.
