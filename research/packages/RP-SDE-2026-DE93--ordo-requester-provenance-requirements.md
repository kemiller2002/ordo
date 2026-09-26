---
identifier: RP-SDE-2026-DE93
title: Ordo requester provenance requirements intake
research_area: state-directed-engineering
discipline: [software-engineering-methodology, software-architecture, agentic-engineering]
author_agent: anthropic/claude-code
version: 1.0.0
status: accepted
confidence:
  label: high
  rationale: The separation it requires already exists structurally in executable Ordo (DF-SDE-2026-0011); this package only adds a recorded, non-authoritative "who asked" fact and states what may never read it. The actor shape is taken unchanged from the Praxis provenance contract rather than invented here.
completion:
  state: complete
  estimate: 1.0
priority: high
related_projects: [state-directed-engineering, repository-operating-system, echelon]
related_documents:
  - research/packages/RP-SDE-2026-0004--ordo-next-pass-requirements.md
  - research/decisions/DF-SDE-2026-D68A--record-requester-provenance-as-audit-only-fact.md
  - research/decisions/DF-SDE-2026-0011--clarify-ordo-capability-as-semantic-authority-not-authentication.md
  - research/decisions/DF-SDE-2026-0013--separate-sde-release-and-ordo-wire-versioning.md
  - docs/architecture/executable-ordo-traceability.md
supersedes: []
superseded_by: []
tags: [ordo, provenance, identity, requester, echelon, wire-versioning]
keywords: [requestedBy, actor, execution, provenance-not-authorization]
created: 2026-09-26
updated: 2026-09-26
provenance:
  contributions:
    EXE-ordo.ros-work-951cc7c00fea5fcd18cb1d3d:
      operations: [created]
      at: 2026-09-26T21:30:00.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Echelon cross-system provenance upgrade (work item FEAT-ECHELON-PROVENANCE)"
---

# Governance status

This package is **accepted as the governed requirements intake** for adding requester provenance to executable Ordo (work item `FEAT-ECHELON-PROVENANCE`). It is a new package rather than an amendment because `RP-SDE-2026-0004` is an accepted, externally sourced intake whose content is pinned to a source commit; extending it would rewrite a provenance record.

| Requirement | Disposition | Decision |
|---|---|---|
| ORDO-NEXT-07 requester provenance | accepted | DF-SDE-2026-D68A |

The provenance block in this file's front matter keys this repository's own run as `EXE-ordo.<run>` because no Praxis execution (`ROS_EXECUTION_ID`) was propagated to it, per Praxis `RQ-ROS-2026-A014`. The vendored `./ros` (1.2.1) has no `provenance record` command, so the block was written by hand; it is not validated by `./ros validate`.

## External contract (referenced, not duplicated)

The actor shape and the rules for identity as provenance are owned by Praxis (`kemiller2002/praxis`, commit `58cf46a`). Ordo implements a small local codec with identical JSON and takes **no** code or package dependency on Praxis.

- `RQ-ROS-2026-A001` actor identity model — <https://github.com/kemiller2002/praxis/blob/58cf46a/research/requirements/RQ-ROS-2026-A001--actor-identity-model.md>
- `RQ-ROS-2026-A010` provenance, not attestation — <https://github.com/kemiller2002/praxis/blob/58cf46a/research/requirements/RQ-ROS-2026-A010--provenance-not-attestation.md>
- `RQ-ROS-2026-A013` provenance interchange record — <https://github.com/kemiller2002/praxis/blob/58cf46a/research/requirements/RQ-ROS-2026-A013--provenance-interchange-record.md>
- `RQ-ROS-2026-A014` execution propagation — <https://github.com/kemiller2002/praxis/blob/58cf46a/research/requirements/RQ-ROS-2026-A014--execution-propagation.md>
- `RQ-ROS-2026-A015` no silent stripping — <https://github.com/kemiller2002/praxis/blob/58cf46a/research/requirements/RQ-ROS-2026-A015--no-silent-stripping.md>
- Actor schema — <https://github.com/kemiller2002/praxis/blob/58cf46a/schemas/provenance-actor.schema.json>

The four-tier guardrail of `RP-SDE-2026-0004` applies unchanged.

# ORDO-NEXT-07 - Requester provenance

## Requirement

Three questions stay separate in Ordo:

| Question | Answered by | Authority |
|---|---|---|
| Who requested this? | **Requester** (identity) | none: an audit fact |
| May this actor request this? | **Capability** (`CapabilitySet`, DF-SDE-2026-0011) | host-supplied semantic authority |
| Why should the transition occur? | **Evidence**, coverage, obligations, policy | the transition rules |

1. Ordo MUST let a host supply an **optional, self-reported requester**: `requestedBy = {actor, execution}`.
   - `actor` is exactly the Praxis actor (`kind`, `id`, `provider`, `model`, `runtime`; key order as written; `kind` in `agent | human | automation | unknown | x-<ext>`).
   - An `agent` MUST state `provider`, `model`, and `runtime`, as the literal `unknown` when not known. A human omits them. `id` MUST NOT be empty; `unknown` is spelled `unknown`.
   - `execution` is optional. When present it is an execution key `EXE-...` or a contribution key `CTB-...`. An agent requester MUST carry an `EXE-...` key.
   - Fields of an actor that Ordo does not model MUST be preserved, not dropped. An unknown non-`x-` kind MUST be refused loudly.
   - No identity value may carry a credential.
2. The requester MAY be carried by:
   - `DecisionRequest`;
   - `ResolutionObservation`, only in a new wire schema version (3);
   - `TransitionContext`, from which `Transition.evaluate` copies it, unread, onto the resulting `TransitionAuthorization` as an audit fact.
3. **Non-interference.** `Transition.evaluate`, capability checks, evidence requirement checks, coverage checks, negative-knowledge support, policy evaluation, and provider request construction MUST NOT read the requester. Two otherwise identical inputs that differ only in requester MUST yield identical verdicts.
4. **Not evidence.** Evidence admissibility and weight MUST NOT depend on who requested, observed, or asserted it. The identity of whoever observed or asserted an absence (negative knowledge) is provenance about the observation, not evidence strength, and MUST NOT upgrade `Partial` or `Unknown` coverage. Who attempted an external effect MUST NOT establish retry safety for an `Unknown` outcome.
5. **Providers cannot mint it.** A provider MUST NOT see or set the requester. The observation's requester is copied from the host-built request, never from provider output.
6. **Wire versioning** (DF-SDE-2026-0013). Observation schema v3 carries `requestedBy` (`null` when the host supplied none). A v2 observation decodes with the requester **absent**; it is never invented, and v2 re-encodes as v2. A v2 document that contains `requestedBy` is refused rather than reinterpreted. Unknown observation versions are refused.
7. **No invention.** Ordo MUST NOT infer a requester from provider identity, `EvidenceSource.System`, environment defaults it cannot see, Git, or anything else. A host that resolves identity from environment variables may use a pure Ordo helper that reads only the whitelisted Praxis keys (`ROS_ACTOR_KIND`, `ROS_ACTOR`, `ROS_TELEMETRY_PROVIDER`, `ROS_TELEMETRY_MODEL`, `ROS_TELEMETRY_RUNTIME`, `ROS_EXECUTION_ID`, `GITHUB_ACTIONS`), yields `unknown` for anything unset, and never mints a Praxis-shaped `EXE-<timestamp>-<rand>`.

## Rationale

- Agents now request Ordo transitions. An audit that cannot say which agent, in which run, asked for an authorized change is incomplete.
- Identity is the easiest thing to mistake for authority ("the trusted agent asked") or for evidence ("a senior reviewer said it is absent"). Recording it next to authority and evidence is only safe if the separation is structural and tested.
- Praxis already defines the actor; a second Ordo-specific shape would fork the Echelon identity model.

## Acceptance criteria

- A Tier 1 `Provenance` module is compiled after `Evidence`, `Coverage`, `NegativeKnowledge`, `Capability`, `Obligation`, and `ExternalEffect`, so none of them can reference it.
- The actor codec round-trips, keeps contract key order, preserves unknown actor fields, accepts `x-` kinds, and refuses unknown kinds, an agent missing `provider`/`model`/`runtime`, an empty id, and credential-shaped values.
- Observation schema v3 round-trips `requestedBy`; a v2 observation decodes with the requester absent and re-encodes as v2.
- Invariance tests over agent, human, automation, unknown, extension, and absent requesters show identical transition verdicts, gate outcomes, capability outcomes, provider requests, and resolution outcomes.
- A provider cannot set the requester.
- The Praxis provenance-record conformance fixtures are vendored with digests, and the actor/execution-key codec is run over them; the subset it does not implement is listed explicitly.
- `ArchitectureTests` still pass; Core gains no dependency.

## Non-requirements

- No authentication, attestation, or signature verification (Praxis `RQ-ROS-2026-A010`).
- No interchange-record reader or writer: Ordo does not transport Praxis provenance records. That remains a follow-up if Ordo ever stores them.
- No change to obligation lifecycle wire shape. Who satisfied an obligation is recorded by the host (for example as the provenance of the completion evidence); adding it to `ObligationState` is a follow-up.

# REP mandatory sections (compact)

1. **Executive Summary** — ORDO-NEXT-07 accepted; high confidence; caveat: identity is self-reported.
2. **Original Objective** — make the requesting actor identifiable without letting identity substitute for evidence or authorization.
3. **Scope** — Tier 1 provenance types, codec, request/observation/authorization audit fields. Excludes authentication, interchange-record handling, and obligation attribution.
4. **Repository Context** — DF-SDE-2026-0011 (capability is not identity), DF-SDE-2026-0013 (wire versioning); observation schema v2 before this change.
5. **Current Understanding** — before this change, no Ordo record named a requester. `ProviderIdentity` names the model service that answered, not who asked.
6. **Key Discoveries** — Praxis documentation describes an Ordo "assessor"; that concept is Praxis-side only and does not exist in Ordo.
7. **Evidence Registry** — none added; verification is by test (see traceability).
8. **Hypothesis Registry** — not applicable: requirements intake.
9. **Failed Assumptions** — none.
10. **Open Questions** — whether obligation satisfaction should carry a satisfier in a future obligation wire schema.
11. **Recommended Next Research** — none required.
12. **Research Backlog** — interchange-record codec if Ordo ever persists Praxis records; ROS upgrade (see Research Debt).
13. **Suggested Specialized Research Agents** — none needed.
14. **Parallel Research Opportunities** — other Echelon systems adopt the same actor codec independently.
15. **Risks** — a host could treat the recorded requester as authorization outside Ordo; documentation and DF-SDE-2026-D68A say it must not.
16. **Cross-Discipline Opportunities** — none.
17. **Knowledge Relationships** — DF-SDE-2026-D68A; DF-SDE-2026-0011; DF-SDE-2026-0013; Praxis RQ-ROS-2026-A001/A010/A013/A014/A015.
18. **Theory Impact Assessment** — no theory record affected.
19. **Research Quality Metrics** — not measured: requirements intake.
20. **Research Debt** — vendored `./ros` is 1.2.1 and lacks `provenance record`; upgrading it is a follow-up and was deliberately not done here.
21. **Repository Updates** — this package, DF-SDE-2026-D68A, `src/Ordo.Core/Provenance.fs`, wire and observation changes, tests, and the traceability table.
22. **Website Updates** — none.
23. **AI Consumption Notes** — `requestedBy` answers "who asked"; never read it as permission or evidence.
24. **Handoff Instructions** — see the traceability section "Echelon provenance — requester".
25. **Research Journal** — none.
26. **Appendix** — none.
27. **Completion Checklist** — complete; verification is recorded in `docs/architecture/executable-ordo-traceability.md`.
