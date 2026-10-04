# Engineering Quality Guard Proposal

Status: proposed Ordo/SDE method extension
Date: 2026-10-04

## Problem

A vertical slice can be behaviorally correct and green while still accumulating structural debt or unsafe failure behavior. This proposal strengthens SDE without turning it into heavyweight up-front design.

## Proposed work items

### ORDO-QUAL-001 - Boundary allocation before consequential implementation
Priority: high

For semantic, boundary, or cross-cutting changes, require a short responsibility allocation identifying semantic authority, application orchestration, external-effect adapters, persistence ownership, presentation/CLI ownership, and expected dependency direction. Effects in the wrong tier must be justified as temporary debt or moved before completion.

### ORDO-QUAL-002 - Failure-posture declaration
Priority: critical

Any feature that gates work, authorizes action, mutates persistent state, or makes a safety/security/reliability decision SHALL declare behavior for unknown or corrupt evidence: fail closed, fail open, or indeterminate requiring reconciliation/operator action. Silent conversion of corrupted required state to an empty or healthy default is prohibited.

### ORDO-QUAL-003 - Persistent-state quality contract
Priority: critical

Persistent semantic/control state SHALL define schema/version identity, compatibility and migration behavior, atomicity, corruption/truncation handling, concurrent writer behavior, recovery/reconciliation, deletion/expiry semantics, and enough provenance to explain the current state.

### ORDO-QUAL-004 - Vertical-slice promotion gate
Priority: high

A knowingly collapsed slice SHALL be marked as experimental/noncanonical, temporary architecture debt with a captured follow-up, or acceptable final architecture with explicit rationale. An experimental shortcut must not become canonical merely because tests pass.

### ORDO-QUAL-005 - Negative-path verification matrix
Priority: high

For high-risk stateful or boundary work, select applicable checks for invalid input, missing/partial input, stale evidence, corrupted state, dependency unavailable, timeout/interruption, concurrency, version/protocol skew, recovery/rollback, and repeated/idempotent execution. Skipped rows must be explicit.

### ORDO-QUAL-006 - Representative live-boundary proof
Priority: high

When correctness materially depends on an external process, provider protocol, credential store, remote executor, release artifact, or generated installation, require a representative live proof before calling the boundary validated, unless unavailable and the risk is recorded.

### ORDO-QUAL-007 - Self-hosting compatibility rule
Priority: high

A system that uses a released version of itself to interpret current repository state SHALL define compatibility authority independently from source-version equality. Include state/protocol schema version, compatible executor range, immutable release identity, pin-advancement sequence, and incompatible-combination refusal behavior.

### ORDO-QUAL-008 - Responsibility concentration as architecture evidence
Priority: medium

Treat rapidly growing modules, modules hosting several unrelated external effects, repeated cross-tier imports, and high-change/high-complexity hotspots as evidence that structural locality may be failing. No fixed line-count threshold alone is a violation; metrics trigger review and semantic responsibility determines the outcome.

### ORDO-QUAL-009 - Adversarial second pass for control-plane changes
Priority: high

Before completing high-risk control-plane, persistence, installer, security, remote-execution, or compatibility work, perform a second pass intended to falsify the design. Ask what can be corrupted or disappear, which fallback can authorize work accidentally, what happens after restart/reset/version change, which provider fields can disappear, what is stringly typed, and what architecture debt the vertical slice introduced.

### ORDO-QUAL-010 - Known-debt completion rule
Priority: high

Definition of Done SHALL require either no known material design debt introduced by the work, or every known material debt item captured with severity, rationale, owner system, and follow-up reference. This permits deliberate debt but prohibits invisible debt.

### ORDO-QUAL-011 - Quality evidence routing
Priority: medium

Ordo defines engineering obligations rather than duplicating analyzers. Dokimos owns structural/change-quality evidence. Praxis owns execution/completion evidence. Aegis owns unexpected operational-fault representation. Conditor owns installation and compatibility enforcement. Registry/release metadata owns ecosystem compatibility facts where available.

## Canonical documents to update after validation

1. method/CONSTRUCTION-METHOD-v0.2.md: responsibility allocation, adversarial second pass, known-debt stop condition.
2. method/VERIFICATION-METHOD.md: negative-path matrix and live-boundary proof.
3. method/CHANGE-CLASSIFICATION.md: persistence/control-plane/self-hosting compatibility as high-risk classes.
4. method/AGENT-EXECUTION-RULES.md: failure posture and architecture-debt disclosure.
5. docs/00-governance/Engineering-Standards.md: green CI is necessary but not sufficient; corrupted required state must not silently become healthy/default state.

## Guardrail against process bloat

These rules scale with risk. A local pure-function change should not need a design ceremony. A provider adapter plus persistence plus work gating should. Tooling should derive obligations from declared risk metadata so the method does not depend on checklist memory.
