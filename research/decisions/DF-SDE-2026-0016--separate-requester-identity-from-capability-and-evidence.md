---
id: DF-SDE-2026-0016
title: Carry requester identity beside Ordo requests, never inside capability or evidence checks
status: accepted
type: decision-record
created: 2026-09-26
updated: 2026-09-26
tags: [architecture, ordo, provenance, identity, capability, evidence, echelon]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/ordo-requester-provenance.md
  - docs/architecture/executable-ordo-traceability.md
  - research/decisions/DF-SDE-2026-0011--clarify-ordo-capability-as-semantic-authority-not-authentication.md
  - research/decisions/DF-SDE-2026-0013--separate-sde-release-and-ordo-wire-versioning.md
---

# DF-SDE-2026-0016

## Context

An agent requesting a state transition or a bounded decision should be
identifiable, so that an audit can answer "who asked for this?". Executable
Ordo had no requester anywhere: `TransitionContext` held capabilities but
no actor, `DecisionRequest` carried correlation and causation but no
requester, and the only identity on a resolution observation was the
provider that answered. `EvidenceSource.System` and
`EvidenceKind.Inferred of ProviderId` name producing systems and the basis of
an inference; neither is a requester.

Praxis now defines the Echelon-wide identity and provenance contract
(`DF-ROS-2026-A037`; requirements `RQ-ROS-2026-A015`, `A016`, `A017`,
`A018`, and `A019`, which names Ordo capabilities explicitly: identity must
never become authorization). Praxis ingests `ordo.resolution-observation`
v2 through `ros ordo ingest`, which rejects unknown schema versions but
ignores unknown members and stores the raw document verbatim.

## Decision

1. **Three separate questions, three separate types.** Identity ("who
   requested this?") is a `Requester`: a Praxis actor plus, when known, its
   execution. Capability ("may this actor request this?") remains the
   host-supplied `CapabilitySet`. Evidence ("why should the transition
   occur?") remains `Evidence` checked against requirements. None is
   derived from another.
2. **The requester sits beside the checks, not inside them.** A new
   `TransitionRequest = { Context; RequestedBy }` wraps the unchanged
   `TransitionContext`; `Transition.evaluateRequest` evaluates the context
   exactly as `Transition.evaluate` does and only then records the requester
   on the authorisation for audit. `DecisionRequest` gains an optional
   `RequestedBy` that is never sent to a provider and never read when an
   outcome is formed. Absent is unknown; nothing is inferred.
3. **Praxis's model, Ordo's codec.** `Ordo.Core.Provenance` is a small,
   pure F# codec for `praxis.provenance/1` mirroring the Praxis reference
   library at contract revision 1.1 (Praxis commit `c2657ef`), tested
   against all 56 vendored Praxis conformance cases and the Echelon chain,
   whose SHA-256 values are recorded and checked. Ordo adds no identity model of
   its own and does not discover identity.
4. **Provenance for other records is attached, not embedded.** Evidence,
   obligations, unknown effects, and negative observations may carry
   provenance through `Attributed<'record>` and an additive `provenance`
   wire member. Checks accept only the bare records, so provenance cannot
   raise evidence strength or confidence or discharge an obligation.
5. **Additive wire change, no version bump.** `ordo.resolution-observation`
   v2 gains an optional `requestProvenance` member (the request's
   `praxis.provenance/1` block, `created` by the requester), emitted only
   when a requester is known. Every existing member is unchanged, legacy
   observations encode byte-for-byte as before, and Praxis ingest accepts
   the extended document (verified against `ros ordo ingest` at the Praxis
   contract commit `a42c44e`). Consistent with `DF-SDE-2026-0013`, a purely additive
   optional member does not change the observation's semantics, so the
   schema version stays 2.

## Four-tier placement

The requester and the codec are Tier 1 vocabulary (`Ordo.Core`). The
transition check (Tier 2) is unchanged and still takes only
`TransitionContext`. Identity discovery and authentication stay with the
host (Tier 4), which declares the requester to Ordo.

## Consequences

- `ResolutionObservation` gains a record field (`RequestProvenance`), and
  `DecisionRequest` gains `RequestedBy`. Code that builds either with a
  full record expression must add the field; the library is not packaged
  and every construction site in this repository is updated.
- `Ordo.Core` now references the framework's
  `System.Text.RegularExpressions`; the architecture test's allow-list
  records why.
- Praxis's parsed projection of an ingested observation does not yet model
  `requestProvenance`; the raw document keeps it. Modelling it in Praxis is
  a Praxis follow-up, not an Ordo requirement.

## Alternatives rejected

- **A `RequestedBy` field inside `TransitionContext`.** Rejected: the
  context is what the checks read, so the separation would rest on
  discipline rather than on the type, and every record expression building a
  context would break.
- **Deriving the requester from the provider identity.** Rejected:
  the provider answers, it does not ask (`RQ-ROS-2026-A016`).
- **A new observation schema version.** Rejected for an optional additive
  member that Praxis already tolerates; it would force Praxis to fail
  closed on every new Ordo observation for no semantic gain.
