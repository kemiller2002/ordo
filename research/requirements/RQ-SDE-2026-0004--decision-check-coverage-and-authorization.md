---
id: RQ-SDE-2026-0004
title: Decision check coverage and authorization
status: draft
version: 1.0.0
owners: [ordo-maintainers]
created: 2026-10-22
updated: 2026-10-22
priority: medium
related_documents:
  - research/requirements/RQ-SDE-2026-0002--versioned-constructibility-contracts.md
  - docs/architecture/decision-records.md
tags: [reliability, decisions, verification, authorization, provenance]
provenance:
  contributions:
    EXE-20261022T201338695Z-0978072d:
      operations: [created]
      at: 2026-10-22T20:14:32.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Canonical ingestion of claimed requirement source VAC-ORDO-001"
derived_from: [INPUT-da11682255be9272]
---

# Decision check coverage and authorization

This draft canonically ingests VAC-ORDO-001 (approved by the repository
owner). It extends ORD-REL-002 of
[RQ-SDE-2026-0002](RQ-SDE-2026-0002--versioned-constructibility-contracts.md),
which governs decisions referenced by `ecir/1` contracts, to the repository
decision records that implementing agents write during their work, and adds
links from a decision to the independent checks that exercise it. It does not
duplicate ORD-REL-002.

Source finding: VAC-001 (kemiller2002/cohort-based-engineering, branch
`claude/verified-agent-continuity-vfjrrx`). Of 202 blind-adjudicated
decisions, 8 added behaviour no requirement asked for; no deterministic check
caught them; agents self-accepted the records that justified them; and the
prototype asserted `verified` from evidence existence alone.

## Requirements

- **ORD-VAC-001 (VAC-ORDO-001-R1):** A decision record carries `coveredBy`:
  the independent checks that exercise it, typed as acceptance or oracle case
  IDs, rule IDs, or accepted decision IDs.
- **ORD-VAC-002 (VAC-ORDO-001-R2):** Decision status `verified` is computed
  by validation, never declared. It requires a `coveredBy` check with a
  recorded passing result at the decision's revision (for a decision
  reference: the referenced decision is accepted by an authority other than
  its implementing actor). Otherwise the status is `unchecked`.
- **ORD-VAC-003 (VAC-ORDO-001-R3):** A decision record written by the actor
  implementing the affected requirement enters as `proposed`. Only a different
  authorizing actor, or a rule the repository configures, moves it to
  `accepted`. Validation reports an `accepted` record whose only provenance is
  the implementing actor.
- **ORD-VAC-004 (VAC-ORDO-001-R4):** Contract admission (`ecir check`)
  reports a decision that adds behaviour outside every referenced requirement
  key as `scope-extension`, a warning, only where the record declares the
  requirement keys it serves. The `praxis validate` side of R4 belongs to
  Praxis and is a follow-up there.

## Acceptance criteria

Fixtures show: a self-accepted record is reported; an uncovered decision
computes `unchecked`; a covered decision with a passing check computes
`verified`; a decision with no requirement key is reported as
`scope-extension`. Records without the new fields still read and validate.
No change to Tier 1/2 purity (SDE-DOCTRINE-003): the assessment is a pure
function in `Ordo.Core`; file access stays in the CLI.

## Consumers

Praxis (validation of repository decision records) and Dokimos (independent
check results) consume the record schema and its diagnostics.
