# SDE 1.3.0 -> ROS upgrade handoff

Status: release candidate for GH-25  
Date: 2026-09-20  
Source repository: `kemiller2002/state-directed-engineering`

## Purpose

This document is the handoff for upgrading ROS and other consuming repositories from `@echelon-foundry/sde` 1.2.0 to 1.3.0 after the 1.3.0 release is published.

SDE 1.3.0 is an additive methodology release. It does not change the Four-Tier Architecture, introduce a fifth tier, or make SDE depend on ROS.

## What changes in 1.3.0

The installed SDE execution package gains:

`.sde/architecture/DECISION-AND-EVIDENCE-SEMANTICS.md`

Its canonical source is:

`doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md`

The doctrine defines the accepted next-pass semantics for:

- minimal semantically complete state views;
- explicit state-view schema/version identity;
- scoped context coverage;
- derived-evidence closure;
- unknown external-effect reconciliation;
- capability as host-supplied semantic authority;
- scoped negative knowledge.

The distribution map remains the source of truth for canonical-source -> installed-file mapping.

## Compatibility

Package version and executable Ordo wire versions are independent.

- SDE package: 1.3.0.
- SDE installation configuration version: remains 2.
- SDE MANIFEST schema: remains 1.
- Ordo state-snapshot wire: current writes remain schema v2.
- Ordo resolution-observation wire: new writes are schema v2.

No SDE configuration migration is introduced by 1.3.0. The 1.2.0 -> 1.3.0 operation is a payload replacement with the existing configuration record retained at configuration version 2.

No immutable Ordo history is rewritten. Historical state-snapshot v1 records remain legacy audit/replay records and cannot authorize new work without current-version state recapture.

## ROS upgrade procedure

After 1.3.0 is published, in the ROS repository:

```bash
npx @echelon-foundry/sde@1.3.0 status
npx @echelon-foundry/sde@1.3.0 upgrade --dry-run
npx @echelon-foundry/sde@1.3.0 upgrade
npx @echelon-foundry/sde@1.3.0 verify --strict
```

The dry run should report an upgrade from 1.2.0 to 1.3.0 and must not modify the repository.

After the upgrade, verify that:

```text
.sde/architecture/DECISION-AND-EVIDENCE-SEMANTICS.md
```

exists and that `.echelon/sde.json` still reports configuration version 2.

## ROS integration implications

ROS should consume the new doctrine as methodology authority rather than duplicating it.

Where ROS coordinates executable Ordo work, it should preserve these distinctions:

- an agent-selected state view is domain/contract-defined and Ordo fingerprints all of it;
- coverage is scoped and explicit, never inferred globally;
- Partial and Unknown remain distinct;
- Derived evidence must have closed acyclic provenance;
- Unknown external effects remain unresolved work even when an external contract proves retry safety;
- Capability means semantic authority supplied by the host, not authentication;
- negative search evidence supports absence only under matching Complete coverage;
- agent evidence is not itself authority to perform an otherwise illegal transition.

ROS must not import executable Ordo into a way that reverses the Tier 4 -> 3 -> 2 -> 1 dependency direction.

## Observation compatibility

`ResolutionObservation` now emits wire schema v2 because the audit shape gained semantic facts:

- `stateViewSchema`: the domain-defined view schema id/version used by the decision;
- `coverage`: the exact scoped coverage claims supplied to the execution.

Coverage is emitted even when incomplete coverage prevents the provider call. This allows an observer to distinguish "provider was not called because coverage was Partial/Unknown" from "no coverage fact was recorded."

The observation schema change does not alter SDE package compatibility and does not reinterpret existing observation records.

## Release verification required before ROS upgrades

Do not upgrade ROS until the 1.3.0 release has all of these green:

- ROS registry check and validation;
- Ordo complexity tests;
- Ordo churn tests;
- Ordo tests;
- Sde.Core tests;
- distribution build and pack;
- packed artifact on Ubuntu Node 18, 20 and 22;
- packed artifact on macOS Node 22;
- packed artifact on Windows Node 22;
- documented packed-artifact quick start;
- release-specific 1.2.0 -> 1.3.0 additive upgrade test.

The live provider test is independent and must be reported as run or skipped. Normal CI does not imply a live-provider result.

## Expected follow-up

Once the published 1.3.0 artifact is confirmed, upgrade ROS using its normal ROS work process, run ROS validation against the upgraded `.sde/` payload, and treat any resulting obligations as upgrade work rather than weakening SDE/Ordo rules.
