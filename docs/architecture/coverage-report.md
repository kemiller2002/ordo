# Pre-execution coverage report (`ordo.coverage-report/1`)

Status: **draft / implemented in `Ordo.Core`** (`ConstructionCoverage`).
Tracking: CONSTRUCT-010 phase 7. Builds on
[decision classification](decision-classification.md) and the
[default plan](construction-default-plan.md).

## Purpose

Before an agent runs, the report says:
- how much of a cohort Ordo will generate;
- what is left to handwritten extensions;
- which decisions are still open;
- which construction route the measured evidence favors.

It is derived entirely from the prepared cohort, the default plan, its
obligations and its choice classification; nothing in it is authored. It is
**advisory**: it grants nothing, verifies nothing, and does not decide the
route.

```fsharp
ConstructionCoverage.report      : PreparedCohort -> DefaultPlan -> CoverageReport
ConstructionCoverage.reportWith  : RouteCalibration -> PreparedCohort -> DefaultPlan -> CoverageReport
ConstructionCoverage.recommendWithoutPlan : DefaultPlanRefusal list -> RouteRecommendation   // route B
ConstructionCoverage.encodeCoverageReport / digest                                          // canonical bytes
```

The library is the interface. A host computes the prepared cohort with its
real decision authorization. The `sde` CLI does not take decision authority
from a flag, so it has no `coverage` command.

## Per requirement

Every key in the source manifest gets exactly one row.

| Status | Meaning |
|---|---|
| `fullyGenerated` | generated structure (states, transitions, refusals, invariants, capabilities) realizes it, and no obligation or open choice cites it |
| `partiallyGenerated` | generated structure realizes it, and an extension, an open decision or unsupported content also cites it |
| `needsExtension` | nothing structural is generated for it; an *implement* obligation with all decisions settled covers it |
| `unresolved` | an open decision blocks it, or nothing constructs it and no obligation explains why |
| `unsupported` | approved content the vocabulary cannot express |
| `outsideCohort` | not assigned to this cohort: deferred, rejected, superseded or other work. Never counted as generated |

Ports and extension inventories do not count as generated structure. The
behavior behind them is handwritten.

## Totals

The report gives these totals:
- source and assigned requirements, and the count per status;
- **open decisions**: decision obligations (conflict, undecided pair, effect
  failure, extension decision point, uncovered requirement);
- open and unsupported **choices**, from the classification;
- generated files (the default plan's layout) and generated constructs;
- extension contracts, and an **estimated extension complexity**;
- verification obligations: blueprint verification nodes in required scope,
  plus one cohort-level independent verification.

**Extension complexity points** are a deterministic heuristic, not an effort
estimate. Each extension contract scores:
- 1 for the contract;
- 1 per requirement it realizes;
- 1 per decision point;
- 2 more if it is an effect, for its outcome handling.

The points are uncalibrated against effort and are comparable only between
reports.

## Route recommendation

The routes are the arms CONSTRUCT-008 measured.

| Route | Meaning |
|---|---|
| `C` | mechanical-first: Ordo generates the default plan and structure, and the agent meets the obligations |
| `A+` | conventional implementation with Ordo's trace, classification and obligations as the handoff, and no generated structure |
| `B` | the typed-plan authoring workflow. Recommended only when no default plan can be generated, so the decision records must be authored first |

The rule uses the **structure ratio**:
`generated constructs / (generated constructs + assigned requirements not generated)`.
At or above the calibrated threshold the report recommends `C`; below it, `A+`.
Every recommendation carries:
- `advisory: true`;
- the calibration id;
- a rationale;
- a non-empty uncertainty list. Any open decisions are listed there too,
  because every route must escalate them.

### Calibration `construct-008-pilot/1` (threshold 0.45)

Computed by this report from each slice's frozen ECIR and decision records.
The inputs are in `tests/fixtures/construction/` and are re-derived by tests.

| Slice | Constructs | Not generated | Structure ratio | Requirement share | Measured C vs A |
|---|---|---|---|---|---|
| time-entry timer pilot 01 | 10 | 3 | 0.77 | 0.50 | cheaper by median (−4% primary, −12% cumulative); ranges overlap |
| Summa delivery | 22 | 24 | 0.48 | 0.14 | cheaper, separated (−13.6% primary) |
| Chrona timer | 9 | 14 | 0.39 | 0.22 | dearer by median (+14%); ranges overlap |
| time-entry expanded 01 | 6 | 40 | 0.13 | 0.13 | dearer (+8% primary separated; +33% cumulative) |

**The requirement share does not separate the slices.** Summa has a lower
share than Chrona but the opposite outcome. A share-based rule was drafted
first and rejected on this evidence. The structure ratio does separate them.
The Summa attribution supports reading the ratio as a proxy: there, the
generated state model replaced code and self-tests that A wrote by hand.

### Limits, stated in every report

- **The threshold is fitted, not validated.** It sits between the nearest
  opposite outcomes (0.39 and 0.48), with four points, n = 3 runs per arm and
  one model. Agreement with these four slices is in-sample.
- **Near the threshold the arms overlapped.** Recommendations within 0.10 of
  it say so.
- **Correctness was equal across arms in every slice.** The recommendation is
  about token cost, not correctness.
- **Unsupported choices are flagged.** These are choices decided by omission
  in vocabularies 1 and 2. Generating them would hide decisions; vocabulary 3
  removes them.

A host may supply its own `RouteCalibration`. The report names the
calibration it used.
