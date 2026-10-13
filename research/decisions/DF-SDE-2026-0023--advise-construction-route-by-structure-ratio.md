---
id: DF-SDE-2026-0023
title: Report construction coverage before execution and advise the route by structure ratio
status: draft
type: decision-record
created: 2026-10-13
updated: 2026-10-13
tags: [construction, coverage, route, construct-010]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/coverage-report.md
  - src/Ordo.Core/ConstructionCoverage.fs
  - tests/Ordo.Tests/ConstructionCoverageTests.fs
  - tests/fixtures/construction/coverage-calibration/README.md
provenance:
  contributions:
    EXE-20261013T205411288Z-a99db3f1:
      operations: [created]
      at: 2026-10-13T20:58:48.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record CONSTRUCT-010 phase 7 coverage report and route calibration"
---

# Context

CONSTRUCT-008 found that mechanical-first construction (C) is cheaper than
conventional implementation (A) on some slices and dearer on others.
CONSTRUCT-010 phase 7 asks for a report, produced before any agent runs, that
states coverage, open decisions and extension work, and that gives an
advisory route recommendation with its evidence and uncertainty.

# Decision

1. **The report.** `ConstructionCoverage.report` derives
   `ordo.coverage-report/1` from the prepared cohort and the default plan,
   using the plan's obligations and its choice classification:
   - one row per source requirement, with a status from `fullyGenerated`,
     `partiallyGenerated`, `needsExtension`, `unresolved`, `unsupported` and
     `outsideCohort`;
   - totals;
   - extension contracts, with heuristic complexity points;
   - an advisory recommendation.
2. **The rule.** Recommend `C` when the **structure ratio** is at or above
   0.45, and `A+` below it. The ratio is generated constructs divided by
   generated constructs plus the assigned requirements nothing generates.
   Recommend `B` only when no default plan can be generated.
3. **Pinned evidence.** The calibration (`construct-008-pilot/1`) cites the
   four measured slices. Their frozen ECIR inputs are copied into Ordo's test
   fixtures, and a test re-derives each quoted ratio.

# Alternatives

- **The share of requirements generated.** This was the first draft. It was
  rejected on evidence: Summa (share 0.14) was cheaper under C, while Chrona
  (share 0.22) was dearer.
- **No recommendation, only totals.** Rejected: the mission asks for a
  machine-readable route with evidence. A labelled advisory is more useful
  than none, provided its uncertainty is explicit.
- **A CLI command.** Not added. The report needs a prepared cohort, and the
  CLI deliberately takes no decision authority from flags.

# Limits

- The threshold is fitted to four points (n = 3 runs per arm, one model).
  Agreement with them is in-sample.
- The gap between the nearest opposite outcomes is narrow (0.39 to 0.48).
- The recommendation addresses token cost, not correctness, which was equal
  across arms.

A new measurement should either confirm the threshold or replace it under a
new calibration id. It must never be edited in place.
