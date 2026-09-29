---
id: SDE-METHOD-EXEC-REQ-001
title: Execution Governance Requirements
status: draft
version: 0.2.0
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

Tracked by GitHub issues #38 and #40.

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


## First-class execution envelope

- **ORD-EXEC-070** A bounded execution MUST be representable as a first-class
  domain object rather than being inferred from a chat session, branch name,
  process, or provider-specific run.
- **ORD-EXEC-071** An execution envelope MUST identify at minimum the execution,
  governed work item or mission, actor, execution role, baseline revision,
  start time, effective capabilities, mutation boundary, evaluation boundary,
  and any human-only transitions relevant to that execution.
- **ORD-EXEC-072** Workspace identity MAY be attached to an execution envelope,
  but workspace identity MUST remain distinct from execution identity because
  non-filesystem and remote executions may exist.
- **ORD-EXEC-073** Execution identity MUST remain stable across provider
  telemetry updates, process restarts, or resumptions of the same execution.
- **ORD-EXEC-074** A new competing attempt at the same work MUST receive a new
  execution identity even when it uses the same actor, provider, role, or work
  item.
- **ORD-EXEC-075** Provider, model, runtime, and human/automation identity MUST
  be attributes of an execution actor or host observation, not the semantic
  definition of the execution role.
- **ORD-EXEC-076** The semantic execution contract MUST remain provider
  independent so a human, Claude, Codex, Gemini, CI runner, or future provider
  can perform the same role when they possess the required capabilities.

## Role authority model

- **ORD-EXEC-080** Ordo MUST define role authority through capabilities and
  prohibitions, not through prose-only role names.
- **ORD-EXEC-081** The default specification role MUST be able to clarify and
  elaborate already-authorized scope but MUST NOT create new governing promises
  or approve its own specification without an explicit additional capability.
- **ORD-EXEC-082** The default implementation role MUST be able to change the
  authorized implementation and implementation-facing tests but MUST NOT alter
  accepted requirements, acceptance policy, or its evaluator.
- **ORD-EXEC-083** The default verification role MUST be able to invoke the
  identified evaluator, observe outcomes, and record evidence but MUST NOT
  repair the candidate or evaluator within the same verification execution.
- **ORD-EXEC-084** The default review role MUST be able to inspect evidence,
  accept an allowed review transition, reject, or request rework according to
  policy but MUST NOT silently become an implementation execution.
- **ORD-EXEC-085** The default integration role MUST be able to combine already
  authorized candidate changes and resolve declared integration conflicts but
  MUST NOT retroactively alter the acceptance criteria under which those
  candidates were judged.
- **ORD-EXEC-086** Projects MAY strengthen or narrow default role capabilities,
  but widening a role MUST be an explicit governed policy decision.
- **ORD-EXEC-087** Role changes during active work MUST be represented as a
  legal transition or a new execution, not as an unrecorded change in prompt
  instructions.

## Evaluation authority closure and identity

- **ORD-EXEC-090** Evaluation authority MUST be modeled as the effective closure
  of all inputs that can change an acceptance verdict, not merely the root gate
  command or test file.
- **ORD-EXEC-091** The evaluation-authority closure MAY include gate code,
  policy, configuration, schemas, test-selection logic, fixtures, generated
  evaluator inputs, referenced constraints, and other effective-current
  dependencies.
- **ORD-EXEC-092** A host MUST be able to assign an immutable identity or
  content-derived fingerprint to the effective evaluator used for a candidate.
- **ORD-EXEC-093** Acceptance evidence MUST identify the evaluator identity or
  fingerprint against which the candidate was judged.
- **ORD-EXEC-094** If the effective evaluation authority changes between the
  declared baseline and the attempted verdict, the outcome MUST NOT be reported
  as pass or fail for the original evaluation context.
- **ORD-EXEC-095** An evaluation-authority change MUST produce an explicit
  invalid, stale, or changed-evaluator state that requires re-baselining or a
  new verification execution.
- **ORD-EXEC-096** A human-readable explanation that an evaluator change was
  harmless MUST NOT substitute for re-evaluation when the effective evaluator
  identity changed.

## Typed receipt semantics

- **ORD-EXEC-100** Expected receipts MUST be typed semantic postconditions
  rather than unstructured success strings.
- **ORD-EXEC-101** The receipt model MUST be able to express at least existence
  or identity of an artifact, command/capability success, expected state,
  produced artifact contract, satisfied verification, observed transition, and
  composite postconditions.
- **ORD-EXEC-102** Expected receipt, observed receipt, and receipt comparison
  result MUST remain separate concepts.
- **ORD-EXEC-103** Receipt comparison MUST support at least `match`,
  `mismatch`, and `indeterminate`.
- **ORD-EXEC-104** `indeterminate` MUST represent insufficient evidence or an
  unknown effect and MUST NOT be collapsed into either success or failure.
- **ORD-EXEC-105** A receipt comparison result MUST carry or reference the
  evidence needed to explain why the result was reached.
- **ORD-EXEC-106** A composite expected receipt MUST preserve the result of each
  constituent check so a partial match cannot be represented as complete
  satisfaction.
- **ORD-EXEC-107** A narrative statement by the executing actor MAY accompany a
  receipt but MUST remain distinct from machine-observed or independently
  observed evidence.

## Step state, resume, and reconciliation

- **ORD-EXEC-110** A multi-step execution MUST be able to preserve durable
  per-step state independently of the conversational session that performed
  each step.
- **ORD-EXEC-111** A completed step with a matching receipt SHOULD be reusable
  on resume when its dependencies and authority identities remain valid.
- **ORD-EXEC-112** A step whose effect is indeterminate MUST require
  reconciliation before a retry when retry could duplicate or corrupt an
  external or repository effect.
- **ORD-EXEC-113** Reconciliation MUST determine whether the prior attempted
  effect occurred, did not occur, or remains unknown; it MUST NOT infer
  non-occurrence solely from a missing success response.
- **ORD-EXEC-114** Resumption MUST preserve earlier receipts and observations
  append-only rather than rewriting them into the final outcome.
- **ORD-EXEC-115** The legal next action after resumption MUST be derived from
  current state, preserved receipts, unresolved obligations, unknown effects,
  and effective authority, not from the prior agent's narrative handoff.

## Semantic mutation boundaries and scope expansion

- **ORD-EXEC-120** Mutation boundaries SHOULD be expressible in semantic terms
  such as features, responsibility clusters, authorities, or capabilities and
  MUST NOT require file paths to be the only unit of authorization.
- **ORD-EXEC-121** A host MAY project a semantic mutation boundary into physical
  paths, repositories, packages, resources, or commands for enforcement and
  observation.
- **ORD-EXEC-122** The physical projection MUST remain traceable to the semantic
  authority that justified it.
- **ORD-EXEC-123** A detected mutation outside the effective boundary MUST
  create an explicit unresolved scope effect or obligation.
- **ORD-EXEC-124** An actor's explanation for an out-of-bound mutation MUST NOT
  itself widen the authorized boundary.
- **ORD-EXEC-125** Legitimate scope expansion MUST occur through an explicit
  legal transition or separately authorized execution that records the new
  boundary and its justification before dependent work is accepted.
- **ORD-EXEC-126** Evaluation-authority artifacts MUST remain outside an
  implementation execution's writable boundary even when they are physically
  colocated with implementation files.

## Actor authorization policy

- **ORD-EXEC-130** Legal transitions MUST be able to express actor-kind
  authorization independently from execution role, including human, agent,
  and automation authorization where applicable.
- **ORD-EXEC-131** A transition MAY be human-required, automation-allowed,
  agent-allowed, or restricted by a more specific capability policy.
- **ORD-EXEC-132** Human-required MUST mean that an agent cannot satisfy the
  authorization merely by reporting or simulating human approval.
- **ORD-EXEC-133** Different projects MAY apply different authorization policy
  to the same semantic state machine without changing the meaning of the
  states themselves.
- **ORD-EXEC-134** Authorization evidence MUST record the actor that actually
  exercised the transition capability.

## Cross-system execution contract

- **ORD-EXEC-140** Ordo owns the semantic meaning of execution state,
  capability, obligation, unknown effect, receipt outcome, and legal
  transition; it MUST NOT depend on Praxis, Conditor, a UI, or a model provider
  to define those meanings.
- **ORD-EXEC-141** A host such as Praxis owns execution orchestration and MAY
  project Ordo semantics into workspaces, processes, APIs, and user interfaces,
  but MUST NOT invent conflicting transition semantics.
- **ORD-EXEC-142** A bootstrapper such as Conditor MAY establish the host and
  initialize governed state but MUST NOT become the runtime authority for work
  transitions after handoff.
- **ORD-EXEC-143** A presentation system MUST remain a projection of the
  authoritative execution model and MUST NOT become an independent workflow
  database or policy engine.
- **ORD-EXEC-144** The architecture MUST permit roles, receipts, evaluator
  identity, and legal-action computation to be used without any particular UI.
