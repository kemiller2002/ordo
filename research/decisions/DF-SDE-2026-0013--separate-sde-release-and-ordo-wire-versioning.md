---
id: DF-SDE-2026-0013
title: Release governed Ordo methodology additively while versioning executable wire semantics independently
status: accepted
type: decision-record
created: 2026-09-20
updated: 2026-09-20
tags: [architecture, ordo, sde, gh-18, distribution, versioning, migration]
supersedes: []
superseded_by: []
related_documents:
  - research/packages/RP-SDE-2026-0004--ordo-next-pass-requirements.md
  - doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md
  - docs/releasing.md
  - distribution/DISTRIBUTION-MAP.json
  - docs/architecture/ordo-next-pass-implementation-plan.md
---

# DF-SDE-2026-0013

## Context

GH-18 changes both methodology content and future executable Ordo semantics, but those surfaces have different compatibility rules.

The published `@echelon-foundry/sde` package is currently 1.2.0. `docs/releasing.md` classifies methodology-content changes as a minor release.

Executable Ordo's persisted/wire records use explicit schema versions. Core `Wire.fs` and `ResolutionObservation` currently write schema version 1 and refuse unknown versions.

State-view schema/version identity changes the meaning of persisted state identity, so it cannot be introduced by silently treating old records as if they had the new semantics.

## Decision

### SDE distribution

The governed methodology change is additive and removes no existing command, flag, output field, or exit-code meaning.

The target SDE distribution release is therefore **1.3.0**, subject to the normal release gate.

The new accepted doctrine `doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md` must be included in the installed SDE execution package so consuming repositories receive the same governance rules that this repository uses.

### Executable Ordo wire compatibility

Executable Ordo wire schemas are versioned independently from the npm methodology package.

New records that rely on view schema/version identity or other new semantic fields MUST use a new wire schema version. Version 1 records must never be reinterpreted as though those fields had existed.

Historical version 1 records should remain readable for audit/replay where practical. A legacy record lacking the new state-view identity semantics MUST NOT authorize a new real-world transition merely because its old fingerprint still parses. A new action must recapture current state under the current view schema/version.

If ResolutionObservation gains coverage or state-view schema identity, its schema version must evolve independently and preserve the same refusal-to-guess rule.

## Migration rule

No in-place rewrite of immutable historical observations is permitted.

Migration is:

1. preserve old records as old records;
2. decode them explicitly as legacy when supported;
3. emit new-version records for new executions;
4. require current-version state capture before new authorization;
5. keep audit/replay provenance showing which schema version produced each record.

## Consequences

The implementation may include source/API changes inside pre-stable executable Ordo, but wire-history semantics remain explicit.

SDE package version and Ordo wire schema version must never be conflated.
