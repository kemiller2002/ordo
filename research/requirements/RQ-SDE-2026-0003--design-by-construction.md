---
id: RQ-SDE-2026-0003
title: Preserve Design by Construction contracts in construction
status: draft
version: 1.0.0
owners: [ordo-maintainers]
created: 2026-10-18
updated: 2026-10-18
priority: critical
related_documents:
  - RQ-SDE-2026-0002
tags: [design-by-construction, construction-ir, contracts, accessibility]
derived_from: [INPUT-a385c22ff220f848]
provenance:
  contributions:
    EXE-20261018T124842421Z-f3f0c468:
      operations: [created, modified]
      at: 2026-10-18T12:53:00.964Z
      last: 2026-10-18T12:54:02.877Z
      actor:
        kind: agent
        id: openai/codex
        provider: openai
        model: unknown
        runtime: codex
      reason: "Canonical ingestion of claimed requirement source"
---

# Preserve Design by Construction contracts in construction

This draft canonically ingests the Design by Construction source for Ordo.
Ingestion is not implementation or approval.

## Objective and repository scope

Prevent known invalid UI and print designs structurally rather than relying
primarily on post-hoc visual inspection and testing. Ordo MUST represent and
preserve design-contract versions, modes, invariants, overrides, diagnostics,
and evidence obligations through requirements, decisions, cohorts, and
Construction IR without manufacturing design approval.

## Shared requirements

1. Use a versioned machine-readable design contract with explicit compatibility rules.
2. Standard mode constrains semantic layout, alignment, overflow, typography, color, accessibility, motion, and action semantics.
3. Extended mode permits documented semantic overrides; custom mode requires a reason and additional validation evidence.
4. Preserve ordinary HTML/CSS and accessible web semantics; a proprietary designer format MUST NOT be mandatory for consumers.
5. Reject structurally invalid compositions before code generation with actionable diagnostics.
6. Preserve backward compatibility or provide a documented, versioned migration path.
7. Require keyboard- and Playwright-operable controls whose commands have observable meaningful behavior.
8. Treat runtime and visual testing as evidence for properties contracts cannot guarantee.

## First end-to-end slice

Represent a customer form with two fields, email validation, a real save
command, and responsive layout. Contracts MUST prevent misaligned standard
fields, unsupported layout overrides, missing action bindings, and invalid
parent-child compositions. Evidence MUST cover narrow/mobile and desktop
viewports and reduced motion.

## Expressiveness and flexibility

- Represent stack, grid, split, dock, flow, canvas, overlay, page, and repeater models where applicable without requiring irrelevant renderers to implement all models.
- Preserve context-specific invariants: aligned standard forms, deliberate canvas positioning/overlap, and print pagination/safe areas.
- Support nested composition, container-responsive adaptation, density, themes, and white labeling without mandating a universal arrangement, navigation pattern, or column count.
- Represent declarative binding, computed values, validation, conditional visibility, state-dependent commands, asynchronous feedback, appropriate direct manipulation, keyboard navigation, and machine-operable interactions.
- Preserve documented extension points for specialized content without bypassing semantic or safety contracts.
- Distinguish hard structural constraints from subjective design guidance and preserve an auditable extension path for legitimate unsupported designs.

## Reference-layout certification

- Preserve obligations for at least 20 cross-repository references, including dashboard, complex form, mobile wizard, ledger, calendar, kanban, workflow editor, marketing page, article, resume, menu, business card, and journal publication.
- Record required capabilities, rendering modes, accessibility/machine-operability evidence, and custom overrides.
- Treat 100% documented representability, 100% defined-invalid rejection, and fewer than 10% custom exceptions as proposed targets requiring validation.
- Keep agent effort, escape use, regressions, and visual quality as separate measures; taste remains reviewable guidance.

## Acceptance evidence

- Versioned schema and supported/unsupported examples.
- Deterministic diagnostics for invalid fixtures.
- Correct mobile and desktop rendering for valid fixtures.
- Keyboard, screen-reader, and Playwright evidence.
- Regression/migration evidence with escape defects and agent correction effort.
- Repository-local work and provenance linked to this requirement.

## Dependencies

Contract owners MUST agree on layout taxonomy and compatibility before
enforcement. Coordinate Forma first, then Forma Studio and Ordo, Praxis,
Visual Engineering, and Folio. Visual Engineering owns certification
methodology. Ordo and Praxis MUST preserve the contract in generated work.
Implementation MUST proceed in narrow validated slices and MUST NOT be claimed
complete until evidence is committed.
