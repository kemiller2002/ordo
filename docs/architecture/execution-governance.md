---
id: SDE-ARCH-004
title: Execution governance — implementation and traceability
status: draft
version: 0.1.0
created: 2026-09-29
updated: 2026-09-29
related_documents:
  - method/EXECUTION-GOVERNANCE-REQUIREMENTS.md
  - docs/architecture/executable-ordo.md
  - schemas/ordo-execution.v1.schema.json
tags: [architecture, execution, capabilities, receipts, evaluator, traceability]
---

# Execution governance — implementation and traceability

Implements `method/EXECUTION-GOVERNANCE-REQUIREMENTS.md` (`ORD-EXEC-*`) in
`Ordo.Core` under work item GH-44. Everything here is pure F#: no provider,
no host, no UI, no persistence. Praxis, Conditor and any future host consume
the semantics; they do not redefine them (ORD-EXEC-140..144).

## Modules

| Module | Owns |
|---|---|
| `Identifiers.fs` | `ExecutionId`, `StepId`, `WorkspaceId`, `ActorId`, `WorkItemId` — checked identifiers; workspace identity is a separate type from execution identity (ORD-EXEC-072). |
| `ExecutionRole.fs` | `ExecutionRole`, the closed `ExecutionCapability` vocabulary, `RoleAuthority` (grants + prohibitions), default role matrix, governed `Narrow`/`Widen` adjustments. |
| `Evaluator.fs` | `EvaluatorInput` closure members, `EvaluatorIdentity` (sha256 over the canonical closure), `EvaluationOutcome` = `Passed`/`Failed`/`EvaluatorChanged`/`EvaluatorUnavailable`. |
| `Receipt.fs` | `ExpectedReceipt`, `ObservedReceipt` (facts + separate narrative + observation source), `ReceiptResult` = `Match`/`Mismatch`/`Indeterminate`/`Composite`. |
| `MutationBoundary.fs` | Semantic scopes, traceable physical projections, evaluator exclusion, `ScopeEffect`, legal `ScopeExpansion`, `ContainmentStrength` (unknown / semantic-only / host-enforced-with-evidence). |
| `Authorization.fs` | `ActorKind`, `ExecutionActor` (provider/model/runtime as attributes), `ActorRequirement`, `AuthorizationPolicy`, `AuthorizationEvidence`. |
| `Execution.fs` | `ExecutionEnvelope`, `ExecutionState`, append-only `StepLedger` with reconciliation, and `LegalActions.compute` — the one legal-action computation. |
| `ExecutionWire.fs` | `ordo.execution/1` JSON for envelopes, ledgers, receipts and legal actions; decoders for receipts and facts. |

## Semantics that are easy to get wrong

- **Indeterminate is not failure.** No evidence, an unobservable subject, an
  unknown command outcome, or a verdict under a different evaluator all yield
  `Indeterminate`. A self-reported observation can never yield `Match`.
- **Composite receipts keep their constituents.** The summary is `match` only
  when every constituent matches; any mismatch dominates; otherwise
  indeterminate.
- **A changed evaluator is not pass or fail.** `Evaluation.judge` returns
  `EvaluatorChanged` with the per-input diff. There is no parameter for an
  explanation; prose cannot convert it (ORD-EXEC-096). `Evaluation.isCurrent`
  invalidates any verdict recorded under a previous identity.
- **No default role holds evaluator or acceptance authority.** Changing
  evaluation is a separate governed execution (ORD-EXEC-003/051). An envelope
  whose role could modify its own evaluator is refused, and an envelope whose
  boundary would admit an evaluator input is refused.
- **An explanation never widens a boundary.** `ScopeEffect.Explanation` is
  recorded and ignored. Only `MutationBoundary.expand` widens, and it needs a
  justification, an authorizer, traceable projections, and must not reach
  evaluator references.
- **A worktree is not a sandbox.** `Containment.ofMechanism` is always
  `SemanticOnly`; `HostEnforced` requires at least one enforced restriction
  and evidence.
- **Human-required means a human exercised it.** `AuthorizationPolicy.authorize`
  consults only the exercising actor; there is no way to pass "a human
  approved this" on behalf of an agent.
- **Unknown effects are reconciled before retry.** `StepLedger.start` refuses a
  step whose effect is unknown unless reconciliation found it did not occur or
  the host established retry safety from the external contract. A reconciled
  "occurred" is re-observed, not repeated. Entries are append-only.

## Traceability

| Requirements | Implementation | Tests (`tests/Ordo.Tests/ExecutionGovernanceTests.fs`) |
|---|---|---|
| ORD-EXEC-001..004, 126 | `ExecutionEnvelope.create` (evaluator exclusion, `JudgedExecutionHoldsEvaluatorAuthority`), `MutationBoundary.classify` | envelope excludes evaluator closure; mutations classified |
| ORD-EXEC-010..016, 080..087 | `ExecutionRole.fs`, `ExecutionEnvelope.succeed` | default role boundaries; widening governed; no default evaluator authority |
| ORD-EXEC-020/021 | `LegalActions.compute`, `LegalActions.isLegal` | completion legality; human-only transitions |
| ORD-EXEC-030..033, 060/061 | `MutationBoundary.fs`, `Containment` | worktree never a sandbox; scope effects |
| ORD-EXEC-040..044, 100..107 | `Receipt.fs`, `ExecutionWire` | match / mismatch / indeterminate / composite / self-report / narrative |
| ORD-EXEC-050..052, 090..096 | `Evaluator.fs` | unchanged, direct, indirect, generated change; stale verdict; unavailable |
| ORD-EXEC-070..076 | `ExecutionEnvelope`, `ExecutionActor` | provider is an actor attribute; competing attempt identity; successor parent |
| ORD-EXEC-110..115 | `StepLedger` | resume reuse; refusal to retry unknown effect; retry after reconciliation; occurred → observe; retry-safe contract |
| ORD-EXEC-120..125 | `MutationBoundary.expand`, `ScopeEffect` | explanation does not widen; legal expansion |
| ORD-EXEC-130..134 | `Authorization.fs` | human-required; evidence records exercising actor |
| ORD-EXEC-140..144 | `Ordo.Core` purity (architecture tests), `ordo.execution/1` schema | architecture tests unchanged and green |

## What remains for hosts

Ordo defines meaning; it does not observe. A host must supply content digests
for evaluator inputs, observed facts for receipts, the set of mutated
resources, workspace identity and any host-enforcement evidence. Praxis is the
reference host (see Praxis `requirements/EXECUTION-ORCHESTRATION.md`).
