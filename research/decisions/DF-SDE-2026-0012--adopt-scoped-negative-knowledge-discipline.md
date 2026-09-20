---
id: DF-SDE-2026-0012
title: Adopt scoped negative-knowledge discipline and refuse unqualified not-found claims
status: accepted
type: decision-record
created: 2026-09-20
updated: 2026-09-20
tags: [architecture, ordo, gh-18, next-pass, negative-knowledge]
supersedes: []
superseded_by: []
related_documents:
  - research/packages/RP-SDE-2026-0004--ordo-next-pass-requirements.md
  - research/evidence/EV-SDE-2026-0011--ordo-next-pass-pre-upgrade-baseline.md
  - doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md
  - doctrine/FOUR-TIER-ARCHITECTURE.md
---

# DF-SDE-2026-0012

## Context

Strata distinguishes `not-compared`, `not-modelled`, `unverifiable`, and `absent` because they permit different conclusions. Time Tracking Application refuses to interpret an unavailable reference catalog as empty. HelixNote preserves missing unit/date information rather than inventing defaults.

A bare "not found" statement is therefore too weak to reuse safely.

## Decision

SDE/Ordo must preserve the distinction between:

- unavailable;
- not searched / not observed;
- not compared;
- not modelled / unsupported;
- unverifiable / unknown;
- absent.

A reusable negative observation must retain enough provenance to answer:

- **scope**: where was absence being tested?
- **method/query**: how was it searched/observed?
- **time**: when was it observed?
- **state identity**: against which commit/revision/version/state was it observed?
- **coverage**: how complete was the relevant scope?
- **exclusions/errors**: what was inaccessible or intentionally omitted?
- **target**: what was actually searched for?

"Not found" may support **Absent** only when the observation method could detect the target and coverage is Complete for the relevant scope.

`Unknown` and `Unavailable` never become absence by default.

## Representation decision

The discipline is REQUIRED. A universal negative-knowledge discriminated union is not required. Domains may retain richer named states.

The implementation pass should determine whether reusable negative observations need a small common wire record or can remain domain Evidence content plus scoped coverage.

## Four-tier placement

Higher tiers perform searches and observations. Tier 1 records and reasons over the resulting semantic fact. Ordo does not search repositories, databases, or networks to decide whether coverage is complete.

## Consequences

Negative evidence becomes auditable and time/state scoped without forcing every domain into Strata's vocabulary.
