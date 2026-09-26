---
id: DF-SDE-2026-D68A
title: Record requester provenance as an audit-only fact, separate from capability and evidence
status: accepted
type: decision-record
created: 2026-09-26
updated: 2026-09-26
tags: [architecture, ordo, provenance, identity, capability, evidence, wire-versioning, echelon]
supersedes: []
superseded_by: []
related_documents:
  - research/packages/RP-SDE-2026-DE93--ordo-requester-provenance-requirements.md
  - research/decisions/DF-SDE-2026-0011--clarify-ordo-capability-as-semantic-authority-not-authentication.md
  - research/decisions/DF-SDE-2026-0013--separate-sde-release-and-ordo-wire-versioning.md
  - docs/architecture/executable-ordo-traceability.md
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

# DF-SDE-2026-D68A

## Context

ORDO-NEXT-07 (`RP-SDE-2026-DE93`) requires that the actor requesting a decision or transition be identifiable. Executable Ordo had no such fact:

- `CapabilitySet` is authority, deliberately not identity (DF-SDE-2026-0011).
- `TransitionContext`, `TransitionAuthorization`, and `DecisionRequest` had no requester.
- `ResolutionObservation` (schema v2) names the provider that answered, not who asked.
- `EvidenceSource.System` is free text about where evidence came from.

The Echelon actor shape is owned by Praxis (`RQ-ROS-2026-A001`, `RQ-ROS-2026-A013`–`A015`, commit `58cf46a`).

## Decision

1. **Placement.** A Tier 1 module, `Ordo.Core.Provenance`, holds `ActorKind`, `Actor`, `ExecutionKey`, and `Requester`. It is compiled after `Evidence`, `Coverage`, `NegativeKnowledge`, `Capability`, `Obligation`, and `ExternalEffect`. F# compile order therefore makes it impossible for those modules to read a requester. `Transition` is compiled after it only so that it can carry the requester.
2. **Representation.** `Actor` has a private representation built by validating constructors. Its JSON is the Praxis actor, in contract key order: `kind`, `id`, `provider`, `model`, `runtime`, then preserved extension fields. `provider`, `model`, and `runtime` are omitted when absent. That is the contract's own shape, and differs deliberately from Ordo's usual `null` for absent optionals. The `requestedBy` wrapper is Ordo's: `{"actor": {...}, "execution": "EXE-..." | null}`.
3. **Unknown fields.** Ordo's `JsonValue` keeps object members as an ordered list, so unknown actor fields are kept verbatim as an extension list and re-emitted in their original order. They are preserved, not refused. Unknown `kind` tokens are refused as `UnknownVariant`, which is Ordo's rule for closed vocabularies (ORDO-8404) and Praxis's rule for `kind`. Namespaced `x-` kinds are accepted.
4. **Carriers.**
   - `DecisionRequest.RequestedBy`, set only by the host (`DecisionRequest.requestedBy`).
   - `TransitionContext.RequestedBy`, copied by `Transition.evaluate` onto `TransitionAuthorization.RequestedBy` without being read by any check.
   - `ResolutionObservation.RequestedBy`, copied by `Resolve` from the request.
   - `ProviderRequest` and `ProviderOutcome` do **not** carry it, so a provider can neither see nor mint a requester.
5. **Wire.** `ordo.resolution-observation` moves to schema v3 with a `requestedBy` member. The decoder accepts v2 and v3. v2 yields `RequestedBy = None` and refuses a v2 document that contains `requestedBy`. Other versions are refused. `encodeAtVersion 2` refuses an observation that carries a requester rather than dropping it. No other record's version changes.
6. **Identity is not authority or evidence.** No check reads the requester. Invariance tests over several actor kinds are the enforcement.
7. **No invention.** Absent stays absent. `Requester.fromIdentityVariables` is a pure helper over a caller-supplied lookup of the whitelisted Praxis keys. It never guesses: an unset value becomes `unknown`, and GitHub Actions with nothing declared becomes the contract's automation default. It uses `ROS_EXECUTION_ID` when propagated, otherwise a host-named `EXE-<system>.<run>`, and never mints a Praxis `EXE-<timestamp>-<rand>`.
8. **Credentials.** Identity values, including string values inside preserved extension fields, that look like credentials are refused. The check is hand-written rather than regex-based so that `Ordo.Core` gains no new assembly reference.

## Alternatives rejected

- **Reusing `ProviderIdentity` or `EvidenceSource.System` for the requester.** That conflates who answered, or where evidence came from, with who asked.
- **Putting identity inside `CapabilitySet`.** That reverses DF-SDE-2026-0011.
- **Refusing unknown actor fields.** That breaks Praxis forward compatibility. The Ordo JSON model can preserve them losslessly, so refusal was unnecessary.
- **Extending `RP-SDE-2026-0004`.** It is an accepted intake pinned to an external source commit.

## Consequences

- Source-level change: `TransitionContext`, `DecisionRequest`, and `ResolutionObservation` gain a field. Hosts that build `TransitionContext` literally add `RequestedBy = None`.
- Observers reading v2 history keep working. New records are v3.
- Follow-ups: attribution of obligation satisfaction; an interchange-record codec if Ordo ever stores Praxis records; upgrading the vendored ROS (1.2.1) to a release with `provenance record`.
