# Ordo next-pass implementation plan

Status: governed implementation plan for GH-18  
Authority: DF-SDE-2026-0007 through DF-SDE-2026-0013  
Baseline: EV-SDE-2026-0011

## Objective

Implement the six accepted ORDO-NEXT requirements without changing the Four-Tier Architecture, without making Ordo depend on ROS, and without silently reinterpreting persisted v1 wire records.

This plan is implementation guidance, not a substitute for the decision records.

## Phase 1 - State identity and view versioning

Implement DF-SDE-2026-0007 first because coverage and later observations need stable state-view identity.

Required behavior:

- introduce explicit state-view schema identity/version;
- fingerprint canonical content together with the view schema identity/version;
- preserve deterministic canonical rendering;
- keep fingerprinting over the entire selected view;
- do not add selective invalidation;
- new live authorization uses only current-version state capture;
- historical v1 state snapshots remain historical and are never silently upgraded.

Required tests:

- field-order changes do not change canonical identity;
- identical view + identical schema/version yields identical fingerprint;
- identical view + different view-schema version yields different state identity;
- relevant-state change changes fingerprint;
- unrelated ambient state outside the selected view has no effect;
- tampered view/fingerprint is refused;
- legacy v1 record cannot be treated as current-version authorization input without recapture.

## Phase 2 - Derived evidence closure

Implement DF-SDE-2026-0009 as a pure utility over supplied evidence.

Required outcomes:

- valid chain;
- valid shared dependency;
- missing input;
- self-cycle;
- multi-node cycle;
- deterministic transitive closure.

Do not generalize the validator to correction/history relationships.

## Phase 3 - Scoped coverage representation spike

Before freezing the public API for DF-SDE-2026-0008, implement or prototype both candidate forms against the Strata and Time Tracking scenarios:

A. first-class `ContextCoverageClaim`;  
B. typed Evidence carrying coverage content.

Compare:

- ability for policy/guards to require a named Complete scope;
- preservation of Partial versus Unknown;
- wire clarity;
- audit/observation clarity;
- API surface and duplication.

Record the representation choice in a DF record before merging the final public type.

No global coverage flag is allowed.

## Phase 4 - Unknown-effect reconciliation semantics

Implement DF-SDE-2026-0010 without adding an effect engine.

Minimum executable addition:

- a closed, explicit reconciliation-obligation semantic for an external operation whose effect outcome is unknown.

If a generic retry-safety value is added, it must describe an externally established property, not infer idempotency inside Ordo.

Required examples/tests:

- Unknown differs from Failed;
- Unknown leaves reconciliation outstanding;
- generic blind retry is not created by Ordo;
- a proven retry-safe external contract may permit a retry request, but later actions that require knowing the actual effect still require reconciliation;
- Tier 1/2 code performs no probe/I/O.

## Phase 5 - Capability trust-boundary clarification

Implement DF-SDE-2026-0011 in:

- `src/Ordo.Core/Capability.fs` comments;
- executable-Ordo architecture documentation;
- public examples where authority is shown.

No security/authentication dependency is added.

## Phase 6 - Negative-knowledge representation decision

Apply DF-SDE-2026-0012 to at least the Strata-style and Time Tracking-style cases.

Determine whether a common wire value is actually needed.

If domain Evidence + scoped coverage carries all required provenance without ambiguity, do not add a new core type.

If a common value is justified, it must preserve:

- target;
- scope;
- method/query;
- observed time;
- state/commit/revision;
- coverage;
- exclusions/errors.

No universal domain result enum is permitted.

## Phase 7 - Observation and wire updates

Any new state-view/coverage facts needed for audit must be emitted in versioned wire records.

Rules:

- new semantic meaning -> new schema version;
- old records remain old;
- unsupported versions are refused, never guessed;
- version 1 records may be decoded for audit/replay where explicitly supported;
- no v1 record is silently promoted into v2 authorization semantics.

Update `docs/architecture/executable-ordo-traceability.md` with exact implementation status.

## Phase 8 - SDE distribution

Add `doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md` to `distribution/DISTRIBUTION-MAP.json`.

Target `@echelon-foundry/sde` version: **1.3.0**.

Do not bump the package until:

- all implementation and migration tests pass;
- package payload includes the new doctrine;
- `sde upgrade --dry-run` shows the expected additive methodology update;
- packed-artifact matrix passes on Linux, Windows and macOS;
- documented quick-start passes from the packed artifact.

## Four-tier acceptance gate

Mechanical architecture tests must continue proving:

- Ordo.Core has no ROS reference;
- Ordo.Core has no provider SDK;
- Tier 1/2 implementation has no external-I/O dependency;
- provider adapters cannot produce TransitionAuthorization;
- provider output cannot mint Capability;
- DecisionOutcome remains unable to mutate state.

If any accepted requirement fails this gate, redesign the implementation rather than relaxing the gate.

## Compatibility acceptance gate

Before release:

- old v1 wire fixtures remain readable for the explicitly supported audit/replay path;
- new writes use the new semantic schema;
- old fingerprints are never recomputed under new view semantics;
- current authorization always uses current state/view schema;
- no immutable history is rewritten.

## Full verification gate

At completion, record and compare to EV-SDE-2026-0011:

- ROS registry check;
- ROS validate;
- Ordo.Complexity.Tests;
- Ordo.Churn.Tests;
- Ordo.Tests;
- Sde.Core.Tests;
- distribution build and pack;
- package matrix: Ubuntu Node 18/20/22, macOS Node 22, Windows Node 22;
- documented packed-artifact quick start;
- any new view/coverage/evidence/effect-specific suites.

The live-provider test remains separate and must be reported as run or skipped, never implied.

## Work decomposition

Implementation should be split into bounded ROS work items rather than making GH-18 one unreviewable code change.

Recommended order:

1. state-view schema/version + wire migration;
2. evidence closure utility;
3. coverage representation spike + decision;
4. coverage implementation;
5. reconciliation obligation;
6. capability documentation;
7. negative-knowledge representation spike/implementation;
8. observation/wire traceability;
9. distribution 1.3.0 and release validation.

## Stop conditions

Stop and return to governance if implementation appears to require:

- a fifth tier;
- Ordo -> ROS dependency;
- Tier 1/2 I/O;
- a graph database;
- generic EvidenceRelation;
- selective fingerprinting;
- a universal effect workflow;
- automatic provider consensus;
- automatic reflection/self-modification.

Those are explicitly outside the accepted next pass.
