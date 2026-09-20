---
id: JR-SDE-2026-0005
title: Govern ORDO-NEXT-01 through ORDO-NEXT-06
research_area: state-directed-engineering
author_agent: ChatGPT
created: 2026-09-20
related_mission:
related_package: RP-SDE-2026-0004
evidence_ids:
  - EV-SDE-2026-0011
hypothesis_ids: []
theory_ids: []
tags: [ordo, gh-18, governance, requirements, four-tier]
---

# Research Journal Entry

## Objective

Complete the requirement-by-requirement governance pass for the six production candidates in RP-SDE-2026-0004 without implementing behavioral code.

## Starting state

GH-18 was active under ROS and a clean pre-upgrade executable/distribution baseline was recorded in EV-SDE-2026-0011.

## Actions taken

Each requirement was reviewed against:

- current executable Ordo boundaries;
- Four-Tier Architecture;
- the repository-backed validation corpus;
- the tightened constraints requested before implementation.

Six independent decision records were created: DF-SDE-2026-0007 through DF-SDE-2026-0012.

A new authoritative doctrine surface, `doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md`, consolidates the accepted semantics for normal SDE/Ordo use without requiring normal agents to read the research archive.

The Four-Tier doctrine is strengthened in this pass to state explicitly that Ordo does not introduce Tier 1/2 I/O and ROS is not Tier 5.

## Dispositions

| Requirement | Disposition |
|---|---|
| ORDO-NEXT-01 minimal state view | accept with explicit semantic-completeness definition, view schema/version identity, and historical insufficiency handling |
| ORDO-NEXT-02 scoped coverage | accept semantics; defer exact core representation to implementation comparison |
| ORDO-NEXT-03 evidence closure | accept and scope acyclicity specifically to Derived-from relationships |
| ORDO-NEXT-04 unknown effects | accept with explicit reconciliation obligation and proven-idempotency exception |
| ORDO-NEXT-05 capability terminology | accept; current behavior already conforms, documentation must become explicit |
| ORDO-NEXT-06 negative knowledge | accept discipline; no universal enum required |

ORDO-RESEARCH-01 assumptions remains a research prototype. ORDO-RESEARCH-02 hypothetical snapshots remains deferred.

## Important refinements

### State views

Fingerprinting remains full over the selected view. Selective fingerprinting is not introduced.

The future fingerprint identity must incorporate explicit view schema/version semantics. Old fingerprints are never silently reinterpreted if the view definition changes.

### Coverage

Partial means known incompleteness. Unknown means completeness has not been established. They are not interchangeable.

### Evidence cycles

Only derivation topology is governed as a DAG here. Correction/history topology is not accidentally constrained by the evidence validator.

### Unknown effects

Blind retry is prohibited unless the actual external contract proves retry safety. Even safe retry does not eliminate reconciliation when later legality depends on knowing the effect outcome.

### Negative knowledge

Reusable "not found" records require scope, method/query, time, state identity, target, coverage, exclusions and errors.

## Attempts to falsify

For each candidate, the governance pass asked whether the observed cases could remain entirely application-specific.

Evidence closure and unknown-effect safety recurred across sufficiently different applications to justify shared semantics.

Coverage and negative knowledge also recur, but their exact executable representation is not yet sufficiently proven to freeze. Their semantic requirements are therefore accepted while implementation shape remains open.

## Decisions and rationale

See DF-SDE-2026-0007 through DF-SDE-2026-0012.

## Failures and dead ends

No requirement required weakening the Four-Tier Architecture.

No generic EvidenceRelation, graph store, selective invalidation engine, consensus layer, assumption engine, or effect workflow was required.

## Confidence changes

Confidence increased that the next pass can remain small and additive. The main implementation risk is now contract/version migration, not architecture replacement.

## Files changed

- six DF decision records;
- `doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md`;
- Four-Tier, SDE, glossary and executable-Ordo documentation;
- RP-SDE-2026-0004 governance status;
- decision navigation view.

## Highest-value next step

Create the implementation work item(s) under ROS with acceptance tests mapped directly to DF-SDE-2026-0007 through DF-SDE-2026-0012.

The implementation should start with state-view identity/versioning and evidence closure, then run the coverage representation spike before freezing its public type.
