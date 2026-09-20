---
id: EX-SDE-2026-0007
title: Negative-knowledge representation spike across Strata, Time Tracking, and HelixNote
status: completed
type: experiment-record
created: 2026-09-20
updated: 2026-09-20
tags: [ordo, negative-knowledge, gh-24, strata, time-tracking, helixnote]
related_documents:
  - research/decisions/DF-SDE-2026-0012--adopt-scoped-negative-knowledge-discipline.md
  - research/decisions/DF-SDE-2026-0015--represent-negative-search-observations-as-typed-evidence-content.md
---

# EX-SDE-2026-0007

## Question

After GH-22 introduced first-class scoped coverage, does Ordo still need a reusable negative-observation representation, or is arbitrary domain Evidence content plus coverage sufficient?

Two candidate forms were compared:

A. arbitrary domain Evidence content + ContextCoverageClaim only;
B. a small typed NegativeObservation payload carried inside ordinary Evidence + ContextCoverageClaim.

No new EvidenceKind and no universal domain absence enum are considered.

## Real scenarios

### Strata false clean

EV-STRATA-2026-D3A8 records a correctness failure where foreign-key, check-constraint, and default differences were not compared yet the result rendered as already matches desired state.

This is not a legitimate negative observation. The comparison did not happen. The correct semantic state is not-compared / incomplete coverage, not absent.

### Strata visible but unreadable

EV-STRATA-2026-E7A9 proves catalog visibility does not imply accessibility. A visible object may be structurally known while relation-access coverage is Partial.

A target not found under Partial relation-access coverage cannot support an absence claim.

### Time Tracking reference catalog

Time Tracking refuses to treat a failed reference.json pull as an empty catalog. An unreadable pull establishes Unknown coverage, not absence.

After a successful authoritative catalog observation, a target not found in that complete catalog may support a scoped absence claim.

### HelixNote missing unit

HelixNote Protocol.fs deliberately preserves a lab unit that the source did not state as Measurement.UnitMissing. It never fabricates a default.

This is not the same as a search that failed to find a target. It is a domain fact about a field in a directly observed source. A generic negative-search type must not absorb this state.

## Candidate A: arbitrary Evidence content + coverage

Existing Evidence already carries:

- stable EvidenceId;
- source;
- observed time;
- Direct / Derived / Inferred basis;
- arbitrary JSON content.

GH-22 coverage already carries:

- named scope;
- Complete / Partial / Unknown;
- EvidenceId provenance.

The missing pieces required by DF-SDE-2026-0012 would have to live by convention inside arbitrary Evidence.Content:

- target;
- observation/search method;
- query, where applicable;
- state/commit/revision observed;
- exclusions;
- errors.

Nothing would mechanically guarantee those fields exist or mean the same thing between repositories.

## Candidate B: typed NegativeObservation inside ordinary Evidence

A narrow value records only a completed observation/search that did not find a named target.

It carries:

- target;
- CoverageScope;
- method;
- optional query;
- observed state/commit/revision reference;
- exclusions;
- errors.

Evidence continues to carry source, observed time, identity, and basis.

ContextCoverageClaim continues to carry the completeness assertion and cites the EvidenceId.

A helper may say a negative observation supports absence only when:

- the coverage scope matches the observation scope;
- the coverage claim cites this EvidenceId;
- coverage is Complete.

It does not inspect prose to infer Complete.

## Comparison

| Property | Arbitrary Evidence + coverage | Typed NegativeObservation + coverage |
|---|---|---|
| target is mandatory | convention only | typed |
| method is mandatory | convention only | typed |
| state/commit/revision is mandatory | convention only | typed |
| exclusions/errors have stable shape | convention only | typed |
| observed time | Evidence | Evidence |
| coverage | ContextCoverageClaim | ContextCoverageClaim |
| can mechanically test absence support | weak / content parser required | yes |
| requires new EvidenceKind | no | no |
| forces domain-specific states into Ordo | no | no |
| mistakes Helix UnitMissing for search absence | possible by convention | explicitly out of scope |
| mistakes failed Time Tracking fetch for empty catalog | possible by convention | Complete-coverage check refuses it |

## Result

Promote Candidate B.

The reusable type is deliberately called NegativeObservation, not Absence, because it records what a method failed to find. Absence is a conclusion a domain may draw only when the paired scoped coverage is Complete.

Do not introduce:

- a universal Absent / Unknown / Unavailable / NotCompared / NotModelled union;
- a new EvidenceKind;
- automatic conversion of NotFound into domain absence;
- repository/database searching inside Ordo;
- inference of Complete from a successful method call.

## Boundary

NegativeObservation is Tier 1 data. Tier 3/4 performs the actual search/read/query. The host constructs Evidence and coverage. Tier 1/2 may validate whether those supplied facts are sufficient to support a scoped absence conclusion.