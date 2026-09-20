---
id: DF-SDE-2026-0015
title: Represent reusable negative search observations as typed Evidence content paired with scoped coverage
status: accepted
type: decision-record
created: 2026-09-20
updated: 2026-09-20
tags: [architecture, ordo, gh-24, negative-knowledge]
supersedes: []
superseded_by: []
related_documents:
  - research/decisions/DF-SDE-2026-0012--adopt-scoped-negative-knowledge-discipline.md
  - research/experiments/EX-SDE-2026-0007--negative-knowledge-representation-spike.md
  - doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md
---

# DF-SDE-2026-0015

## Context

DF-SDE-2026-0012 requires reusable negative knowledge to retain target, scope, method/query, observation time, state/version, coverage, exclusions and errors.

GH-22 already supplies first-class scoped coverage. Existing Evidence supplies identity, source, observed time and epistemic basis. The remaining question is whether the rest should stay arbitrary JSON by convention.

The GH-24 spike used Strata, Time Tracking, and HelixNote to test that boundary.

## Decision

Executable Ordo will add a small typed NegativeObservation payload intended to be carried as ordinary Evidence content.

NegativeObservation means exactly:

> a declared observation/search method was actually executed against a named scope/state and did not find the named target.

It contains:

- target;
- CoverageScope;
- method;
- optional query;
- observed state/commit/revision reference;
- exclusions;
- errors.

It does not duplicate:

- EvidenceId;
- evidence source;
- observed time;
- Direct/Derived/Inferred basis;
- coverage status.

Those remain owned by Evidence and ContextCoverageClaim.

## Absence support

Ordo may validate that a NegativeObservation is eligible to support a domain absence conclusion only when:

1. the coverage claim scope equals the observation scope;
2. the coverage claim cites the EvidenceId carrying the observation;
3. the cited Evidence record actually carries the supplied NegativeObservation content;
4. the coverage status is Complete.

The helper returns structural sufficiency only. It does not create domain truth or authorize a transition.

## Important exclusions

The type must not represent:

- a search that did not run;
- a read/query that failed before establishing its scope;
- not-compared;
- not-modelled;
- unsupported;
- unverifiable;
- a domain field whose source simply omitted a value, such as HelixNote Measurement.UnitMissing.

Those remain distinct domain/evidence states.

## Four-tier placement

Higher tiers perform searches and acquire observations. Ordo.Core contains only the typed value and pure validation. No filesystem, database, GitHub, network, or provider dependency is added.

## Consequences

The representation gives cross-repository negative observations a stable minimum provenance shape without creating a universal negative-knowledge ontology.