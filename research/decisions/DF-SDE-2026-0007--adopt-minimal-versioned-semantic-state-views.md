---
id: DF-SDE-2026-0007
title: Adopt minimal, versioned, semantically complete state views for Ordo decisions
status: accepted
type: decision-record
created: 2026-09-20
updated: 2026-09-20
tags: [architecture, ordo, gh-18, next-pass, state-identity]
supersedes: []
superseded_by: []
related_documents:
  - research/packages/RP-SDE-2026-0004--ordo-next-pass-requirements.md
  - research/evidence/EV-SDE-2026-0011--ordo-next-pass-pre-upgrade-baseline.md
  - doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md
  - doctrine/FOUR-TIER-ARCHITECTURE.md
---

# DF-SDE-2026-0007

## Context

Executable Ordo already fingerprints a caller-selected `StateSnapshot.View` rather than an ambient application object graph. Repository-backed validation showed that relevance is transition-specific. Time Tracking Application scenarios distinguish an unchanged reference that later became inactive from a newly selected inactive reference, and distinguish irrelevant state movement from an intervening overlap that makes Restore illegal.

The open governance questions were what "semantically complete" means, how the view is serialized and versioned, and how historical decisions are treated if the domain later discovers that its view omitted a relevant fact.

## Decision

A state view is **semantically complete for a contract/version and action** when it contains every authoritative state fact whose different value could change any of:

- the legal choice space;
- evidence admissibility or evidence-requirement satisfaction;
- a scoped coverage requirement;
- capability applicability;
- obligation satisfaction or blocking status;
- policy verdict;
- transition legality.

The domain/application owns construction of that view. Ordo does not infer relevance.

Ordo fingerprints the **entire selected view**. No generic selective/dependency-aware fingerprint mechanism is introduced.

Canonical rendering remains deliberate and deterministic. The next executable version MUST bind state identity to an explicit view schema identity/version as well as canonical content. A semantic view-version change must not be able to masquerade as the same state identity merely because its JSON happens to render the same bytes.

If later evidence proves a historical view was insufficient:

1. the old fingerprint remains an honest identity for the view that was actually used;
2. the omission is recorded as a defect/limitation of that contract/view version;
3. the view and affected contract are versioned forward;
4. historical records are never silently reinterpreted;
5. affected historical resolutions are identified where practical;
6. any new action is evaluated against the corrected current view, not grandfathered by the old fingerprint.

## Rejected alternatives

- Fingerprint the entire application state.
- Add a generic dependency graph to the gate.
- Selectively ignore changed fields inside Ordo.
- Recompute old fingerprints under a new view definition.

## Four-tier placement

View construction and state identity are Tier 1/2 pure semantics. They perform no I/O. Tier 3/4 may acquire the raw facts, but the semantic projection is owned below the effect boundary.

## Consequences

The implementation pass must add explicit view schema/version semantics and tests without weakening the existing stale-decision gate. This is a versioned contract change, not a reinterpretation of the existing fingerprint.
