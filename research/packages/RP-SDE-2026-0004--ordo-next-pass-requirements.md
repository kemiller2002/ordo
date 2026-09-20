---
identifier: RP-SDE-2026-0004
title: Ordo next-pass validated requirements intake
research_area: state-directed-engineering
discipline: [software-engineering-methodology, software-architecture, agentic-engineering]
author_agent: ChatGPT
version: 1.0.0
status: accepted
confidence:
  label: high
  rationale: Derived from deep literature review and repository-backed validation across Chrona, Time Entry State Machine, Time Tracking Application, Strata, and HelixNote; the pre-upgrade executable baseline is recorded in EV-SDE-2026-0011 and each production requirement now has an accepted governance decision.
completion:
  state: complete
  estimate: 1.0
priority: high
related_projects: [state-directed-engineering, repository-operating-system]
source_repository: kemiller2002/research-documents
source_commit: 6f69ad93f5f410d19499cce9d49c5e15888b67aa
source_document: research/ordo-foundations/deep-research-program/requirement-candidates/ordo-next-pass.md
supersedes: []
superseded_by: []
tags: [ordo, next-pass, requirements, four-tier, evidence, coverage, unknown-effects]
created: 2026-09-20
updated: 2026-09-20
---

# Governance status

This package is **accepted as the governed requirements intake and provenance record** for GH-18. Acceptance of the package does not mean its original wording became executable code unchanged.

The production requirements were governed independently:

| Requirement | Disposition | Decision |
|---|---|---|
| ORDO-NEXT-01 minimal state view | accepted with versioning/insufficiency tightening | DF-SDE-2026-0007 |
| ORDO-NEXT-02 scoped coverage | accepted semantics; executable representation remains an implementation decision | DF-SDE-2026-0008 |
| ORDO-NEXT-03 evidence closure | accepted, explicitly limited to Derived-from topology | DF-SDE-2026-0009 |
| ORDO-NEXT-04 unknown effects | accepted with explicit reconciliation and proven-idempotency exception | DF-SDE-2026-0010 |
| ORDO-NEXT-05 capability terminology | accepted | DF-SDE-2026-0011 |
| ORDO-NEXT-06 negative knowledge | accepted discipline; no universal enum required | DF-SDE-2026-0012 |
| ORDO-RESEARCH-01 assumptions | remains research/prototype only | deferred |
| ORDO-RESEARCH-02 hypothetical snapshot | remains deferred until a real application requires it | deferred |

The accepted semantic authority is now `doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md` plus the six decision records above. Executable support is a subsequent implementation step.

The original research copy remains in `kemiller2002/research-documents` at commit `6f69ad93f5f410d19499cce9d49c5e15888b67aa` for provenance.

---

# Ordo Next-Pass Requirements

Status: validated next-pass requirement candidates  
Source: `synthesis/ORDO-ROS-NEXT-PASS-DECISION-PACKAGE.md`  
Validation date: 2026-09-19

These requirements are intentionally small. They are the requirements that survived repository-backed validation across Chrona, Time Entry State Machine, Time Tracking Application, Strata, and HelixNote.

They do not authorize implementation by themselves. They are intended to become the input to the next formal Ordo requirements/governance pass.

# Cross-Cutting Architecture Guardrail - Four-Tier Preservation

Every requirement in this document is subordinate to the existing four-tier architecture.

## Dependency invariant

```text
Tier 4 - Browser / Host / Presentation / External Runtime
        v
Tier 3 - Protocol / Engine / Adapters / Effect Execution
        v
Tier 2 - Commands / Decisions / Transitions / Policy
        v
Tier 1 - Domain State / Evidence / Coverage / Obligations / Authority Semantics
```

Dependencies may only point downward.

## Requirements

No implementation of an ORDO-NEXT requirement may:

- invert the Tier 4 -> 3 -> 2 -> 1 dependency direction;
- cause Tier 1 or Tier 2 to perform external I/O;
- cause Tier 1 or Tier 2 to depend on browser, HTTP, database, filesystem, GitHub, provider SDK, persistence adapter, or ROS;
- make ROS a prerequisite for a legal domain transition;
- move application-specific effect execution, reconciliation mechanics, idempotency, conflict resolution, or merge behavior into Ordo.Core;
- make presentation state authoritative;
- permit a provider/adapter to mint authority or bypass Tier 2 rules;
- bypass the pure domain transition path when processing an infrastructure/effect result.

## Valid placement

- state/evidence/coverage/obligation/capability semantics: Tier 1;
- pure guards, decisions, policy, transitions, effect interpretation: Tier 2;
- protocol/adapters/effect requests and host coordination: Tier 3;
- actual browser/network/database/GitHub/filesystem interaction: Tier 4 or host infrastructure;
- ROS observation/calibration/reflection: outside the application tier stack.

## Effect rule

Tier 2 may return an `EffectRequest` as data. Tier 3/4 performs the effect and returns an `EffectResult`. Tier 2 interprets that result and determines the next legal state.

An Unknown result may create a reconciliation obligation and request a probe, but the pure semantic tier must not perform the probe itself.

## Acceptance criterion applying to every requirement

Architecture/boundary tests MUST prove that the implementation introduces no upward dependency or Tier 1/2 I/O dependency.

A requirement that cannot be implemented under this guardrail MUST be revised rather than weakening the four-tier architecture.

# ORDO-NEXT-01 - Minimal semantically complete state view

## Requirement

A DecisionContract or transition evaluation MUST operate against the smallest state view that is semantically sufficient to evaluate that decision.

Ordo MUST fingerprint the entire selected state view.

A caller MUST NOT include unrelated mutable state merely because it is available.

A caller MUST NOT omit state whose change could alter:

- a legal choice;
- evidence admissibility;
- a policy result;
- capability applicability;
- obligation satisfaction;
- transition legality.

## Rationale

Current Ordo already fingerprints a selected view, not an ambient application object graph.

Time Tracking Application executable scenarios demonstrate that relevance is command-specific:

- an existing reference becoming inactive can be irrelevant when the reference is unchanged;
- introducing a newly inactive reference is relevant;
- an intervening overlapping activity is relevant to Restore.

A dependency-aware fingerprint mechanism would duplicate domain knowledge that the domain already possesses.

## Acceptance criteria

- tests show two requests with identical relevant views yield identical fingerprints regardless of unrelated ambient state;
- tests show any relevant field change changes the fingerprint;
- documentation explicitly states that view construction is domain/contract responsibility;
- no selective dependency graph is introduced into `StateFingerprint`;
- Gate continues to compare full fingerprints of selected views.

## Non-requirement

No generic selective invalidation engine.

# ORDO-NEXT-02 - Scoped context coverage semantics

## Requirement

A bounded decision whose safety depends on completeness of search, introspection, or supplied context MUST be able to carry explicit coverage claims.

A coverage claim MUST identify:

- the scope whose completeness is being asserted;
- a status of Complete, Partial, or Unknown;
- provenance/evidence supporting the claim.

Ordo MUST NOT infer Complete from context size, provider confidence, successful execution, or absence of errors.

Multiple coverage claims MAY apply to one request.

## Rationale

Strata demonstrates that completeness is multidimensional. A target can be visible but unreadable, structurally parsed but semantically unresolved, or complete for one relationship class and partial for another.

Time Tracking Application demonstrates that unavailable authoritative reference context must block processing rather than appear empty.

A single global request-level `Complete | Partial | Unknown` value is therefore insufficient.

## Open implementation choice

The first implementation spike MUST compare:

1. a first-class `ContextCoverageClaim` type; and
2. coverage represented as conventional typed Evidence.

Choose the smaller representation that preserves all required distinctions.

## Acceptance criteria

- Complete always names a scope;
- Partial remains distinguishable from Unknown;
- coverage remains distinguishable from missing evidence;
- policy can require Complete for a named scope;
- coverage is present in observations/audit output;
- no universal completeness claim exists.

# ORDO-NEXT-03 - Evidence dependency closure validation

## Requirement

Ordo MUST provide a pure deterministic operation that validates a supplied evidence dependency set.

It MUST detect:

- missing referenced evidence;
- self-cycles;
- multi-node cycles.

It MUST be able to return the transitive dependency closure for a selected evidence item or decision evidence set.

## Safety invariant

Evidence used to authorize a resolution/transition MUST NOT transitively derive from that same resolution.

Later temporal feedback is permitted only through a distinct later evidence/resolution identity.

## Rationale

`EvidenceKind.Derived` already records direct input IDs. Validation completes the existing model.

HelixNote demonstrates the practical importance of refusing cycles and surfacing existing contradictory cyclic histories.

## Acceptance criteria

- chain succeeds;
- shared dependency succeeds;
- missing reference fails with the missing ID/path;
- self-cycle fails;
- multi-node cycle fails with cycle path;
- ordering does not affect result;
- utility has no storage dependency;
- no graph database or global provenance store is introduced.

# ORDO-NEXT-04 - Unknown external-effect safety invariant

## Requirement

When an external effect has been attempted and the application cannot determine whether it occurred:

1. the outcome MUST be represented as Unknown rather than Failed;
2. the application MUST NOT automatically repeat or compensate the effect when doing so could be unsafe;
3. an explicit reconciliation obligation MUST remain outstanding;
4. reconciliation MUST observe the external system before deciding the next legal action.

## Boundary

The shared invariant belongs in Ordo methodology/requirements.

The exact external-effect state machine remains application-owned unless later evidence supports a shared type.

## Rationale

The same pattern exists independently in:

- Time Entry State Machine persistence;
- Time Tracking Application GitHub synchronization;
- Strata deployment;
- HelixNote durable correction/review commitment.

## Acceptance criteria

- examples show Unknown and Failed produce different legal next actions;
- no default retry exists for Unknown;
- unresolved Unknown is visible as an obligation;
- a reconciled result can return the application to an explicit settled state;
- Ordo does not become a workflow/effect engine.

# ORDO-NEXT-05 - Capability terminology and trust boundary

## Requirement

Public Ordo documentation and relevant type comments MUST state:

> Ordo Capability is a semantic authority prerequisite supplied by the host/application. It is not an authentication token, object-capability, signature, credential, or proof of identity.

The host/application remains responsible for establishing that the supplied capability set is legitimate.

## Rationale

Current implementation correctly prevents confidence/provider output from minting capability, but the word "capability" can imply stronger security semantics than the type provides.

## Acceptance criteria

- architecture docs carry the clarification;
- `Capability.fs` comments carry the clarification;
- no authentication/cryptography framework is added;
- examples distinguish provider judgment from host authority.

# ORDO-NEXT-06 - Negative knowledge and absence discipline

## Requirement

Ordo methodology MUST preserve the distinction between:

- unavailable;
- not searched / not observed;
- not compared;
- not modelled / unsupported;
- unverifiable / unknown;
- absent.

A negative claim MUST NOT be interpreted as absence unless the underlying scope and observation process are sufficient to support that claim.

## Rationale

Strata directly demonstrates that collapsing these states creates false "clean" results.

Time Tracking Application refuses to interpret a failed reference fetch as an empty catalog.

HelixNote refuses to guess missing units or unreported dated fields.

## Acceptance criteria

- examples explicitly show `unavailable != absent`;
- a caller can refuse a decision because coverage is incomplete;
- no generic scalar uncertainty score replaces named states;
- domain-specific negative states remain allowed.

# ORDO-RESEARCH-01 - Assumption dependency prototype

## Status

Research prototype only. Not yet a production requirement.

## Prototype objective

Represent stable assumptions and allow a decision/research record to reference them.

Minimum queries:

- which decisions depend on assumption A?
- what evidence or later decision contradicted/superseded A?
- which assumptions remain unresolved?

Use at least:

- Time Tracking `DEC-0002`;
- Strata offline-binding assumption;
- one Chrona withdrawn verification assumption.

## Explicit exclusions

- no ATMS;
- no automatic truth maintenance;
- no global proposition store;
- no automatic invalidation propagation.

# ORDO-RESEARCH-02 - Hypothetical snapshot basis

Deferred until a real application needs to pass scenario/hypothetical state through Ordo.

Safety rule remains:

> a hypothetical decision cannot directly authorize mutation of current authoritative state.

# Rejected next-pass concepts

- generic EvidenceRelation ontology;
- selective/dependency-aware fingerprint algorithm;
- generic consensus/voting;
- universal external-effect workflow;
- graph database / knowledge graph;
- object-capability runtime;
- automatic Deliberate -> Decide -> Compute promotion.

