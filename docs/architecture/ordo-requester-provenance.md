---
id: SDE-ARCH-003
title: Executable Ordo requester identity and provenance requirements
status: accepted
version: 1.1.0
created: 2026-09-26
updated: 2026-09-26
related_documents:
  - research/decisions/DF-SDE-2026-0016--separate-requester-identity-from-capability-and-evidence.md
  - research/decisions/DF-SDE-2026-0011--clarify-ordo-capability-as-semantic-authority-not-authentication.md
  - docs/architecture/executable-ordo.md
  - docs/architecture/executable-ordo-traceability.md
tags: [architecture, ordo, provenance, identity, echelon]
---

# Executable Ordo requester identity and provenance

Work items: `FEAT-ECHELON-PROVENANCE` (1.0.0), `FEAT-ECHELON-PROVENANCE-R12`
(1.1.0). Decision: `DF-SDE-2026-0016`.

## Authority

Praxis is authoritative for what an actor, an execution, a contribution,
lineage, and "unknown" mean. These requirements adopt that model; they
restate it only where Ordo makes a decision of its own, and cite it
everywhere else:

| Praxis record (contract revision 1.2, commit `b0037183389c8b9392919f58521b9487d1b4d5c6`) | What Ordo takes from it |
|---|---|
| `RQ-ROS-2026-A019` provenance never substitutes for authentication, authorization, or evidence | the three-way separation; Ordo is named there as a downstream system that must restate it with tests |
| `RQ-ROS-2026-A015` versioned interchange block with deterministic receiving rules | the `praxis.provenance/1` block, its `supported` / `unsupported` / `malformed` verdicts, and the append rules |
| `RQ-ROS-2026-A016` (revision 1.2.0) identity propagation without re-implementing discovery | the requester comes from one explicit host declaration or is unknown; never guessed, never mixed field by field |
| `RQ-ROS-2026-A017` no credentials in provenance | credential-like values make a block malformed and a requester unconstructable |
| `RQ-ROS-2026-A018` cross-system conformance | the vendored conformance cases and end-to-end chain |
| `DF-ROS-2026-A037` Echelon provenance interchange | the contract as a whole, including `EXT-<system>.<run-id>` and `EXT-op.<seg(operationId)>` keys |

The canonical model is Praxis `docs/agent-provenance.md` and
`schemas/provenance-interchange.schema.json`. Ordo does not define a second
identity model.

## The separation

| Question | Answered by | Never answered by |
|---|---|---|
| Who requested this? | `Requester` (a Praxis actor and its execution), carried beside a request | capability, evidence, confidence, the provider that answered |
| May this actor request this? | the host-supplied `CapabilitySet` (`DF-SDE-2026-0011`) | the requester's kind, id, provider, model, or runtime |
| Why should the transition occur? | `Evidence` against `EvidenceRequirement`s | who produced the evidence or who asked |

## Requirements

### ORDO-PROV-01 — Identity, capability, and evidence stay separate

Executable Ordo MUST keep requester identity, capability, and evidence as
separate inputs. No function may grant, deny, weight, satisfy, or verify
anything because of an actor's kind, id, provider, model, or runtime.
Restates `RQ-ROS-2026-A019` for Ordo's decision points.

Acceptance: requester identity is a distinct type that no check accepts;
the metamorphic tests in ORDO-PROV-03 hold.

### ORDO-PROV-02 — Requests may say who requested them; absence is unknown

A transition request and a decision request MAY carry an optional requester:
a Praxis actor (`kind`, `id`, and for non-humans `provider`, `model`,
`runtime`, with the literal `unknown` when not known) and, when known, the
execution it acted in (`EXE-...` or `EXT-<system>.<run-id>`). The requester
serializes as a `praxis.provenance/1` block whose single `created`
contribution is the requester's, keyed by its execution or, when the
execution is unknown, by `EXT-op.<seg(request id)>`, where `seg` is the
contract's per-code-point `escapeKeySegment` (ORDO-PROV-06). A request id
that cannot form a key (empty, or with an unpaired UTF-16 surrogate) is
refused, never keyed by an invented id; Ordo identifiers refuse unpaired
surrogates at construction (`IdentifierNotWellFormed`), so a decision
request id always forms a key.

An absent requester MUST read as unknown. Ordo MUST NOT infer a requester
from the provider identity, the capability set, the host process, the
environment, or any other ambient signal (`RQ-ROS-2026-A016`). Ordo reads
no environment variable at all — in particular not `ROS_EXECUTION_ID` or any
variable in Praxis's `identity-environment.json`; a host that honours
`ROS_EXECUTION_ID` does so only when the same environment also declares
the identity, and takes the actor wholly from one source (contract revision
1.2, `RQ-ROS-2026-A016` 1.2.0); it passes the result to Ordo explicitly.
Ordo's own `Requester` is that one source: it holds exactly the declared
actor and execution and fills nothing from elsewhere. The provider
that answered a decision and the actor that requested it are different
facts and are recorded separately.

Acceptance: `TransitionRequest.RequestedBy`, `DecisionRequest.RequestedBy`,
and `ResolutionObservation.RequestProvenance` are optional; legacy
constructors leave them empty; a requester cannot be constructed with a
credential-like value or a non-execution key.

### ORDO-PROV-03 — Evaluation never reads the requester

`Transition.evaluate` and decision resolution MUST NOT read the requester.
For the same state, capabilities, evidence, obligations, and policy verdict,
every requester — agent, human, automation, unknown, extension kind, any
provider, or none — MUST receive the same verdict, the same failures, the
same decision outcome, the same confidence, and the same provider request.
An identity MUST NOT grant a capability that is not held; a held capability
MUST work with no requester. The requester MAY be recorded on the resulting
authorisation for audit only.

Acceptance: exhaustive metamorphic tests over every requester and every
transition verdict; the requester sits outside `TransitionContext`, so the
checks cannot see it by construction.

### ORDO-PROV-04 — Evidence may carry its producer's provenance, which never strengthens it

An evidence record MAY carry the provenance of the contribution that
produced it (a `praxis.provenance/1` block beside the record). That
provenance MUST NOT change what evidence satisfies, its kind, its freshness,
or any confidence. `EvidenceKind.Inferred`'s `ProviderId` remains the
epistemic basis of an inference, not the actor who requested or recorded it,
and a human-attributed inference is still an inference.

Acceptance: `Evidence.checkAll` results are identical for any attribution;
attributed evidence still decodes as the same evidence for readers that
ignore provenance.

### ORDO-PROV-05 — Obligations, unknown effects, and negative knowledge may record contributors

Obligations, unknown external effects (and their reconciliation
obligations), and negative observations MAY carry the provenance of the
contributions that created, resolved, or otherwise settled them. Recording a
contributor MUST NOT discharge an obligation or settle an effect: only the
record's own state does. Provenance is appended by the Praxis rules
(never re-attribute, at most one `created`, preserve unknown fields).

Acceptance: an obligation created by an agent and resolved by a human keeps
the agent as originator and the human in the `resolved` role, and blocks a
transition until its state is satisfied regardless of who is recorded.

### ORDO-PROV-06 — The codec conforms to the Praxis contract

Ordo's `praxis.provenance/1` codec MUST reach the Praxis verdict and warning
count for every case in the vendored `cases.json`, replay the vendored
`echelon-chain.json` to its recorded expectations, carry another major
version verbatim, refuse a malformed block at the boundary with its
problems, and refuse credential-like values. The vendored files MUST stay
byte-identical to the recorded Praxis commit (`SOURCE.json` SHA-256).

At contract revision 1.1 this includes: exact matching (patterns anchored
with `\z`, so a trailing newline is malformed); calendar-valid timestamps
(year 0001-9999, no February 30, no `24:00`) ordered at millisecond
precision with extra fraction digits truncated; JSON `null` never meaning
absent; appends that never return a block `classify` rejects (no
credentials, no contribution dated before the creation, no second
originator, the result re-classified); same-key merges that keep the
incoming contribution's unknown fields (existing wins), take the later
time, and refuse an unknown identity extending a known actor's entry; and
injective `_xx` escaping of `EXT-op.<operationId>` keys.

At contract revision 1.2 it also includes (vendored `text-cases.json`,
`envelope-key-cases.json`, and `lineage-cases.json`, and the 70 cases of
`cases.json`):

- **Well-formed text.** Ordo's JSON reader (`Json.parse`) refuses, as
  `MalformedJson`, text that is not JSON, that repeats a member name within
  any one object, or that holds an unpaired UTF-16 surrogate in a member
  name or string; the check is made while reading the text, for every Ordo
  record, not only provenance. `Json.parse`, `ProvenanceBlock.classifyText`
  and `ProvenanceBlock.classify` never throw. A lone surrogate inside an
  already-built value makes a block malformed.
- **ASCII whitespace.** "Blank" means empty after trimming tab, LF, VT, FF,
  CR, and space only; every other character (U+0085, U+FEFF, U+001C,
  U+00A0, ...) is content. Credential patterns use explicit ASCII classes,
  `RegexOptions.CultureInvariant`, and no `\b`, `\s`, or case-insensitive
  option; the bearer pattern is the contract's.
- **Checked lineage.** `ProvenanceBlock.addLineage` / `addLineageJson`
  return the new block and whether it changed, or a refusal: the block must
  be supported, the references an array of non-blank, credential-free,
  well-formed strings (duplicates dropped, first kept), and the result must
  classify as supported. Nothing is stored on refusal.
- **Key segments.** `ContributionKey.escapeSegment` escapes per Unicode
  code point: ASCII letters, digits, and `-` pass through; every other code
  point, `.` and `_` included, becomes `_xx` per UTF-8 byte. It refuses an
  empty segment or one with an unpaired surrogate. `ofOperation` and
  `ofEnvelopeV1` (the reference `keyFromEnvelopeV1`) use it. Keys already
  stored are never rewritten.
- **Null.** A stored `"provenance": null` is malformed, not absent.

Implements `RQ-ROS-2026-A015`, `RQ-ROS-2026-A017`, `RQ-ROS-2026-A018` for
Ordo.

### ORDO-PROV-07 — Wire changes are additive and keep Praxis ingest working

`ordo.resolution-observation` schema v2 MAY carry an optional
`requestProvenance` member, emitted only when the requester is known. It MUST
NOT change any existing member, so a legacy observation encodes exactly as
before, and Praxis `ros ordo ingest` (which fails closed on unknown
*versions* but ignores unknown *members* and stores the raw record verbatim)
accepts it. The member is namespaced because it describes the decision
request, not the observation. Any Ordo record MAY carry its own provenance
as an additive `provenance` member; existing decoders ignore it.

### ORDO-PROV-08 — Legacy records are never rewritten or backfilled

Records and requests without provenance remain valid and read as
unattributed. Ordo MUST NOT backfill or infer historical requesters.
Method v0.1 and historical experiment run data (`research/runs/EX-SDE-*`)
are not modified.

## Revision notes

- **1.1.0 (2026-09-26, `FEAT-ECHELON-PROVENANCE-R12`).** Adopts Praxis
  provenance contract revision 1.2 (commit `b003718`) after the second
  adversarial review. ORDO-PROV-06 gains the revision 1.2 rules: Ordo's JSON
  reader refuses duplicate member names and unpaired surrogates (finding 5:
  a duplicated contribution key had given two entries and originator
  `mallory`; finding 10: `Json.parse` threw on a lone surrogate); blank and
  credential checks use ASCII semantics (finding 11); `addLineage` returns a
  result and refuses what `classify` would reject; key segments escape per
  code point and escape `.`. ORDO-PROV-02 now keys an unknown execution by
  `EXT-op.<seg(request id)>`, refuses an id that cannot form a key, and cites
  the one-identity-source rule of `RQ-ROS-2026-A016` 1.2.0.
  `Requester.toBlock` re-classifies its result and refuses a reason that
  would make the block malformed. Existing stored keys are unchanged: only
  request ids containing `.` or characters outside the BMP map differently.
- **1.0.0 (2026-09-26, `FEAT-ECHELON-PROVENANCE`).** Initial requirements
  at Praxis contract revision 1.1 (commit `c2657ef`).

## Out of scope

- Authentication, attestation, and signatures: the host establishes who is
  acting (`DF-SDE-2026-0011`); attestation belongs behind an adapter
  boundary (Praxis `DF-ROS-2026-A036`).
- Execution envelopes: Ordo does not receive Echelon envelopes; a host that
  does maps the envelope's actor and execution into a `Requester`.
- Ordo does not discover identity. It records what the host declares.
