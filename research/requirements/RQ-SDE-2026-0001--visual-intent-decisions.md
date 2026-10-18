---
id: RQ-SDE-2026-0001
title: Typed visual-intent decisions in construction IR
status: draft
version: 1.0.0
owners: [ordo-maintainers]
created: 2026-10-18
updated: 2026-10-18
priority: critical
related_documents:
  - https://github.com/kemiller2002/ordo/issues/92
tags: [visual-intent, construction-ir, decisions, traceability]
derived_from: [INPUT-d68ba75aa37d75df]
provenance:
  contributions:
    EXE-20261018T124842421Z-f3f0c468:
      operations: [created, modified]
      at: 2026-10-18T12:53:00.522Z
      last: 2026-10-18T12:54:02.508Z
      actor:
        kind: agent
        id: openai/codex
        provider: openai
        model: unknown
        runtime: codex
      reason: "Canonical ingestion of claimed requirement source"
---

# Typed visual-intent decisions in construction IR

This draft canonically records Ordo issue #92. Ingestion is separate from
approval and implementation.

The source scope identifier is ORDO-VIS-001.

## Problem

Adopting Forma does not ensure that an agent selected an appropriate palette
for a product. Design intent has not been an authoritative, reviewable input
before coding. This is a decision-provenance defect, not merely a CSS defect.

## Requirements

- **ORDO-VIS-001.1:** Define a typed, versioned visual-intent decision in Ordo's requirement and Construction IR path for every user-facing application/surface. Record originating requirement IDs, owner, decision status (`approved`, `provisional`, `unknown`, or `not-applicable`), selected Forma release/brand/theme/profile, task-importance hierarchy, semantic color mappings, permitted exceptions, and rationale.
- **ORDO-VIS-001.2:** Preserve traceability from source requirements through approved visual decision, delegated work packages, and implementation verification. Summarization and cohort formation MUST NOT silently drop requirements.
- **ORDO-VIS-001.3:** Treat a missing or unsupported color/brand decision as an explicit unknown or obligation. Never manufacture user approval or reinterpret an agent preference as an approved product choice.
- **ORDO-VIS-001.4:** Construction plans MUST constrain supported token roles and semantic mappings. Coding agents may request changes to the decision contract but MUST NOT silently invent brand colors or tokens.
- **ORDO-VIS-001.5:** Make changes versioned and replayable with a stable schema and explicit approval evidence. Preserve automated execution where valid policy exists; do not block headless builds for purely cosmetic, not-applicable surfaces.

## Acceptance criteria

- Schema and validation round-trip tests cover approved, provisional, missing, contradictory, deprecated, and not-applicable visual decisions.
- A Summa fixture using unsupported `--ef-color-action-primary` is flagged as an unsupported/missing contract, not legitimized by its hex fallback.
- Two applications may share Forma but retain distinct product theme profiles, intent, and provenance.
- Integration references current Ordo ECIR #59, construction operations #64, and product design-system authority without creating a parallel source of truth.

## Scope boundary

Ordo governs decisions and traceability. Forma owns color contracts, Dokimos
verifies code, and Praxis enforces evidence gates. Implementation remains
governed by Ordo [#92](https://github.com/kemiller2002/ordo/issues/92).
