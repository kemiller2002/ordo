---
id: SDE-METHOD-EXEC-REQ-001
title: Execution Governance Requirements
status: draft
version: 0.1.0
created: 2026-09-28
updated: 2026-09-28
related_documents:
  - method/AGENT-EXECUTION-RULES.md
  - method/CONSTRUCTION-METHOD-v0.2.md
  - doctrine/STATE-DIRECTED-ENGINEERING.md
  - docs/architecture/executable-ordo.md
  - research/decisions/DF-SDE-2026-0006--introduce-executable-ordo-primitives.md
tags: [requirements, execution, capabilities, verification, evidence]
---

# Execution Governance Requirements

These requirements capture execution-governance gaps identified while comparing
Ordo, Praxis, and Conditor with the Bang workflow on 2026-09-28. They define
semantic requirements for Ordo. They do not require copying Bang's board,
prompts, or implementation.

Tracked by GitHub issue #38.

## Evaluation independence

- **ORD-EXEC-001** An execution being evaluated MUST NOT mutate any artifact
  whose effective value participates in determining acceptance of that same
  execution.
- **ORD-EXEC-002** The evaluator authority set MUST include direct and indirect
  inputs to acceptance, including policy, gate code, configuration, generated
  evaluator inputs, test-selection rules, and other effective-current
  dependencies.
- **ORD-EXEC-003** When evaluation authority must change, that change MUST occur
  in a separate governed execution. Any candidate previously judged under the
  prior authority MUST be explicitly re-baselined or re-evaluated rather than
  silently inheriting the new evaluator.
- **ORD-EXEC-004** Evaluation independence MUST be represented as a semantic
  invariant, not only as a prompt instruction or path-name convention.

## Execution roles and capabilities

- **ORD-EXEC-010** Execution role MUST be represented as capability-bearing
  state rather than as an informal prompt label.
- **ORD-EXEC-011** Ordo MUST be able to represent distinct default authorities
  for specification, implementation, verification, review, integration, and
  future specialized execution roles.
- **ORD-EXEC-012** A specification role MUST NOT create or alter governing
  promises unless a separate capability explicitly authorizes that transition.
- **ORD-EXEC-013** An implementation role MUST NOT silently widen governing
  promises, acceptance criteria, or evaluation policy.
- **ORD-EXEC-014** A verification role MAY execute verification, collect
  evidence, and record a verdict, but MUST NOT modify the candidate or
  evaluation authority it is judging.
- **ORD-EXEC-015** A review role MAY record findings and request governed
  rework, but MUST NOT silently mutate accepted scope or implementation as part
  of the review decision.
- **ORD-EXEC-016** Human-only authorization points MUST be expressible as
  capability and legal-transition policy rather than requiring host-specific
  special cases.

## Legal actions and presentation

- **ORD-EXEC-020** Any UI or host that presents legal next actions MUST derive
  those actions from the same authoritative state, capability, policy, and
  obligation model used by non-UI execution.
- **ORD-EXEC-021** A presentation layer MUST NOT invent, widen, or independently
  interpret legal transitions.

## Mutation boundary and isolation

- **ORD-EXEC-030** Before a bounded mutating execution begins, the system MUST
  be able to declare its expected mutation boundary or authorized write set at
  a useful semantic granularity.
- **ORD-EXEC-031** A mutation outside the declared boundary MUST be represented
  as an unresolved effect, scope violation, or equivalent obligation and MUST
  block completion until explicitly reconciled.
- **ORD-EXEC-032** Git worktrees, branches, process working directories, and
  similar isolation mechanisms MUST NOT be represented as security sandboxes
  unless the host actually enforces the claimed access boundary.
- **ORD-EXEC-033** Semantic capability restrictions and host-enforced security
  restrictions MUST remain distinguishable so evidence does not overstate the
  strength of isolation.

## Receipts and evidence

- **ORD-EXEC-040** A transition or execution step MAY declare an expected
  receipt that describes the postcondition required for legal advancement.
- **ORD-EXEC-041** The observed receipt MUST be recorded as evidence distinct
  from the expected receipt.
- **ORD-EXEC-042** A receipt mismatch MUST prevent automatic advancement and
  MUST preserve enough observed state to diagnose or reconcile the mismatch.
- **ORD-EXEC-043** When stronger machine-observed evidence exists, an agent's
  narrative completion claim MUST NOT substitute for that evidence.
- **ORD-EXEC-044** Receipt evidence SHOULD preserve the execution, step,
  authority/version context, actor, and observed result necessary to determine
  what was actually judged.

## Acceptance direction

A conforming implementation should make it possible for Praxis or another host
to ask Ordo:

1. what role and capabilities an execution holds;
2. what it may mutate;
3. what evaluation authority is independent of that mutation;
4. what legal actions are currently available;
5. what receipt is expected for the next transition; and
6. whether the observed receipt permits advancement or creates an obligation.


## Evaluator availability and repair

- **ORD-EXEC-050** If required evaluation authority, tooling, configuration,
  or evidence machinery is missing, unverifiable, or outside its declared
  identity, the evaluation MUST stop or become blocked rather than silently
  repairing or substituting the evaluator inside the candidate execution.
- **ORD-EXEC-051** Repairing or replacing evaluation machinery MUST be a
  separate governed operation with its own authority and evidence.
- **ORD-EXEC-052** After evaluator repair or replacement, any affected
  candidate MUST be evaluated against the resulting identified evaluator
  before acceptance can be claimed.

## Host enforcement

- **ORD-EXEC-060** Ordo MUST be able to distinguish semantic authorization
  from host-enforced access control and MUST allow a host to attach stronger
  enforcement evidence when filesystem, process, network, credential, or
  system-call restrictions are actually enforced.
- **ORD-EXEC-061** Absence of host-enforcement evidence MUST leave the
  execution's containment strength unknown or semantic-only rather than
  inferring sandboxing from a worktree, prompt, or working directory.
