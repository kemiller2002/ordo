---
id: DF-SDE-2026-0014
title: Represent context coverage as first-class scoped claims backed by evidence provenance
status: accepted
type: decision-record
created: 2026-09-20
updated: 2026-09-20
tags: [architecture, ordo, gh-22, coverage]
supersedes: []
superseded_by: []
related_documents:
  - research/decisions/DF-SDE-2026-0008--adopt-scoped-context-coverage-semantics.md
  - research/experiments/EX-SDE-2026-0006--scoped-coverage-representation-spike.md
  - doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md
---

# DF-SDE-2026-0014

## Context

DF-SDE-2026-0008 accepted scoped Complete/Partial/Unknown semantics but deliberately left the executable representation open.

GH-22 compared a first-class claim against coverage encoded only as Evidence using current Strata and Time Tracking Application scenarios.

## Decision

Executable Ordo will use a first-class scoped coverage claim.

A claim contains:

- a domain/contract-defined scope;
- one of Complete, Partial, or Unknown;
- one or more EvidenceIds that provide provenance for how the coverage claim was established.

Coverage is not itself evidence. It is a semantic statement about the completeness of an observation method over a named scope.

A DecisionContract may require Complete coverage for named scopes. A request may carry additional Partial or Unknown claims for other scopes.

When a required scope is missing, Partial, or Unknown, bounded resolution stops before provider execution with an outcome distinct from InsufficientEvidence.

Ordo never infers Complete.

## Why not Evidence-only

Evidence-only representation either leaves Ordo unable to enforce coverage without parsing arbitrary domain content, or requires a special coverage schema/parser inside Evidence, which recreates a first-class type indirectly.

## Domain mapping

Strata may carry independent claims such as relations = Complete and relation_access = Partial. Richer domain states such as NotRequested or Inaccessible remain in Strata evidence; Ordo does not import that taxonomy.

Time Tracking may assert reference-catalog = Unknown after an unreadable pull and reference-catalog = Complete after an authoritative successful observation. Whether a referenced project is active and whether restore overlaps current activities remain ordinary domain state, not coverage.

## Constraints

- no global Complete/Partial/Unknown flag;
- no inference from context size, successful calls, confidence, or absence of errors;
- no coverage score;
- no coverage propagation graph;
- no requirement that every possible scope appear on every request;
- no collapsing Partial into Unknown.

## Four-tier placement

Coverage values are Tier 1 semantics. Higher tiers perform the observations that establish coverage and create Evidence. Tier 1/2 reasons over supplied claims and never performs the observation itself.