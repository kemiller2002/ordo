---
id: SDE-ARCH-001
title: Executable Ordo v0.1
status: draft
version: 0.1.0
created: 2026-09-18
updated: 2026-09-20
related_documents:
  - research/decisions/DF-SDE-2026-0006--introduce-executable-ordo-primitives.md
  - docs/architecture/executable-ordo-traceability.md
  - doctrine/FOUR-TIER-ARCHITECTURE.md
  - doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md
  - method/CHANGE-CLASSIFICATION.md
tags: [architecture, ordo, executable-intelligence]
---

# Executable Ordo v0.1

## Why this exists

Ordo is a methodology. It stays one. What this code adds is a small set of
primitives for the handful of concepts every project that uses judgment
keeps re-inventing badly: what a decision is, what evidence is, what
authority is, and what separates having decided something from being allowed
to act on it.

Nothing here is required to use Ordo. An application that only follows the
methodology installs none of this, and every primitive works with no AI
provider at all.

The line this draws is the point:

```text
Domain state
   ↓
Domain contract
   ↓
Compute  /  Decide  /  Deliberate
   ↓
Structured result
   ↓
Evidence + capability + policy + state-version check
   ↓
Legal transition
   ↓
New domain state
```

The intelligence provider does not appear in that chain as an owner. It
supplies information. It may contribute judgment and may say it cannot
answer. It does not own truth, state, authority, policy, or what transitions
are legal.

## What owns what

| Layer | Owns |
|---|---|
| **Application** | domain state, transitions, choices, invariants, business policy, what evidence means in the domain, what consequence a change carries |
| **Ordo** (this code) | reusable semantics: resolution modes, bounded decision contracts, structured outcomes, evidence references, capabilities, obligations, escalation, the provider boundary, transition precondition mechanics |
| **ROS** | execution history, provider measurement, cost, latency, replay orchestration, calibration, comparison, optimisation recommendations |
| **Provider** | information and judgment — nothing else |

Ordo does not reference ROS, and never will: the dependency runs
`ROS → Ordo`. `tests/Ordo.Tests/ArchitectureTests.fs` asserts this against
the loaded assemblies rather than trusting this table.

This maps onto `doctrine/FOUR-TIER-ARCHITECTURE.md` directly.
`Ordo.Core` is Tier 1 vocabulary — identifiers, evidence, capabilities,
obligations, uncertainty. `Ordo.Core.Transition` and `Ordo.Decisions.Gate`
are Tier 2: what legal change may happen. `Ordo.Providers.Anthropic` is
Tier 4: it is the only thing that touches a network.

## Compute, Decide, Deliberate

- **Compute** — the answer follows mechanically from what is already known.
  A model is not required because using one is convenient, and a failure to
  compute stays a visible failure rather than quietly becoming a decision.
- **Decide** — judgment is required, the legal output space is known, and
  the answer is one of a fixed set of typed choices.
- **Deliberate** — the work needs exploration, and its output space is not
  known in advance.

Use the narrowest mode that can resolve the work correctly. Over time,
understanding moves work the other way — `Deliberate → Decide → Compute` —
but nothing here performs that promotion automatically. Observing that a
model has agreed with itself a hundred times is a research finding, not a
licence to replace judgment with a rule.

The names describe engineering behaviour. "System 1 / System 2" may be
useful in the research record; it is not what the code means, so it is not
what the code says.

## The distinctions the types enforce

**A decision is not a transition.** `DecisionOutcome` cannot change
anything. To change state you need a `TransitionAuthorization`, whose
constructor is private to `Ordo.Core.Transition` and which only
`Transition.evaluate` returns — after checking source state, capabilities,
evidence, obligations, policy and state identity. "The provider said Safe,
therefore we acted" is not expressible.

**A decision is not an outcome.** What was decided and what later turned out
to be true are separate records. A decision of `Unsafe` at 0.91 confidence
followed by an observed outcome of `Safe` leaves the original decision
intact.

**Confidence is not capability.** No magnitude grants authority. `Gate.evaluate`
does not read the confidence on a result at all. A domain that wants a
threshold expresses it in its own `Policy`, and the policy's verdict arrives
as one more input alongside the capability check — never instead of it.

**Confidence is not calibration.** A `Confidence` cannot be constructed
without a `ConfidenceProvenance`. `ProviderReported 0.92` is a self-report;
`EmpiricallyCalibrated("1000 recorded outcomes", 1000) 0.92` is a measurement.
Ordo never produces the second — calibration needs history, which belongs to
an observing system.

**Missing evidence is not low confidence.** `InsufficientEvidence` and a
`Decided` result with a small number are different outcomes with different
remedies. So is `Stale` evidence, which needs reacquiring rather than
acquiring.

**Evidence is not truth.** Evidence having been recorded says nothing about
whether it is correct, fresh, relevant or complete. And a model's inference
is never recorded as an observation: `EvidenceKind` separates `Direct`,
`Derived` and `Inferred`, and a requirement may refuse to be satisfied by
anything but a direct observation.

**Derived evidence is reconstructable or it is structurally invalid.**
`EvidenceDependency` validates only the `Derived(... fromEvidence)`
relation. A supplied evidence set must contain every referenced input, must
not contain duplicate evidence identities, and must not contain a derivation
cycle. Its closure is deterministic and dependency-first. This says nothing
about whether the derivation is *correct*; it says only that its declared
provenance is structurally reconstructable.

**Replay is not execution.** `Replay.execute` re-evaluates a recorded
request and returns a record. It never calls the gate, so there is no path
from a replay to a state change.

## The worked example

The example domain is a real open question from this repository's own
method. `method/CHANGE-CLASSIFICATION.md` says outright that it "does not
yet specify a mechanical test for 'is this Mechanical Propagation or a
disguised Semantic Change'". That is a `Decide`: judgment is required, the
output space is small and known, and getting it wrong picks the wrong
verification path rather than breaking anything.

It lives in `tests/Ordo.Tests/Fixtures.fs`, not in a production project,
because the states and choices belong to an application and this repository
holds methodology.

### 1. The contract

```fsharp
type ChangeClass =
    | MechanicalPropagation
    | SemanticChange
    | BoundaryChange

let changeClassContract =
    DecisionContract.create
        (DecisionContractId.create "sde.change-site-class" |> ok)
        (ContractVersion.create 1 |> ok)
        "A mechanical obligation exposed this change site. Is filling it in a
         mechanical propagation of a decision already made, a semantic change
         in disguise, or a change to a representation crossing a boundary?"
        "One change site in a repository with no presentation tier."
        (ChoiceSpace.create changeClassToken
            [ MechanicalPropagation; SemanticChange; BoundaryChange ] |> ok)
    |> DecisionContract.requiring [ toolDiagnostic; siteDiff ]
    |> DecisionContract.withConsequence "low: selects a verification path"
    |> DecisionContract.activated
```

Both required pieces of evidence accept only `AnyDirect`. A model's opinion
about what the diff probably says cannot satisfy a requirement for the diff.

### 2. The request, and running it

```fsharp
let request =
    DecisionRequest.create requestId resolutionId changeClassContract snapshot evidence now
    |> ok

let! record = Resolve.execute ResolveOptions.standard provider request cancellation
```

`Resolve.execute` checks the provider's declared capabilities and the
contract's evidence requirements *before* anything leaves the process. A
request missing required evidence returns `InsufficientEvidence` and costs
nothing — and, more importantly, reports the absence as an absence instead
of getting back a guess.

### 3. The gate

```fsharp
match Gate.evaluate authorizeFastPath context record.Outcome with
| ChangeAuthorized authorization -> applyFastPath authorization
| ChangeRefused refusal -> record refusal
| ChangeNeedsHumanReview (reason, outstanding) -> queueForReview reason
```

`Gate.evaluate` overwrites the caller's `FormedAgainst` with the state the
decision itself saw, so presenting a stale decision as a fresh one is not
possible even by mistake.

### 4. Escalation

A provider that declines returns `RequiresDeliberation`, and the escalation
is recorded as a step — `Decide → ToDeliberation`, with a reason and a
timestamp — on the chain carried in the observation. `Decide → Deliberate →
Decide → Human` stays four visible facts rather than collapsing into the
last answer.

### 5. Stale state

```fsharp
// decided against revision r1
let record = run provider request

// the site moved to r2 while the decision was being made
match Gate.evaluate authorizeFastPath (contextFor movedOn choice) record.Outcome with
| ChangeRefused (DecisionIsStale (decidedAgainst, current)) -> ()
```

This is not a provider error and not a defect. The world moved.

### 6. Replay

```fsharp
let! replayed =
    Replay.execute options otherProvider (IndependentVerification DifferentProvider)
        newResolutionId originalRequest historicalOutcome cancellation
```

The historical outcome is carried unchanged beside the fresh one. Whether
they agree is reported as a fact; agreement is not a verdict, and two
frontier models agreeing is not independent evidence.

## Anti-patterns

Each of these is something the architecture refuses, with the test that
proves it.

| Anti-pattern | What actually happens |
|---|---|
| Sending arithmetic, parsing or a lookup to a model because it is convenient | That is `Compute`. A failure to compute must stay a visible failure; it is never retried as a decision. |
| Treating a model's confidence as permission | `Gate.evaluate` never reads confidence. Perfect confidence with a missing capability is refused — `InvariantTests`, invariant 4. |
| Letting the provider invent a choice | Only a declared token parses, ordinally and exactly. `mostly-mechanical`, `MECHANICAL-PROPAGATION` and `mechanical-propagation ` are all contract violations — `InvariantTests`, invariant 1. |
| Parsing prose to find the authoritative answer | The answer is the typed choice. A rationale arguing for a different one is recorded in `RationaleMentionsOtherChoices` and changes nothing. |
| Retrying until the answer is acceptable | Only transport failures are retryable, and only by the adapter. `ProviderError.isTransportTransient` returns false for every semantic failure. |
| Treating a model's inference as an observed fact | `EvidenceKind.Inferred` is not `Direct`, and a requirement can refuse anything but `Direct`. |
| Applying a decision made against state that has since changed | `DecisionIsStale`, for every choice at every confidence — `InvariantTests`, invariant 7. |
| Falling back to another provider silently | There is no fallback anywhere. A caller holds the provider it chose. |
| Calling a self-reported number a calibrated probability | `Confidence.isCalibrated` is false for `ProviderReported` and for `Derived`. |
| Letting a provider failure become a domain decision | Every `ProviderError` yields no decision and refuses the change — `InvariantTests`, invariant 8. |

## Limits, stated

- **Exact replay of a remote model is not promised.** Logical replay — same
  contract revision, same recorded inputs, a fresh call — is what exists.
  A provider may answer differently with identical input.
- **Replay does not enforce access control.** A caller that can read a
  historical request can re-run it; whether the caller should still be able
  to see that evidence is the application's to decide. Deferred as Class C.
- **`Ambiguous` is not implemented.** Supporting it properly needs candidate
  scoring and a provider capability for it; deferred as Class B rather than
  faked.
- **Provider disagreement is representable but not orchestrated.** Multiple
  results can be recorded; no consensus rule exists here, and none will:
  voting and adjudication are domain or orchestration policy.
- **Observation facts are not persisted by Ordo.** `ResolutionObservation.encode`
  produces the record; where it goes is the caller's business.

## Governed next pass, not yet fully implemented

GH-18 accepted six next-pass semantic requirements after repository-backed validation. They are authoritative requirements, but executable support must remain distinguishable from current v0.1 behavior until implementation completes.

Implementation status:

- **implemented in GH-20:** explicit state-view schema/version identity, full-view fingerprinting, schema-v1 historical decode, and mechanical refusal of legacy snapshots for new decisions/transitions;
- **implemented in GH-21:** deterministic Derived-evidence closure, missing-input/duplicate/cycle refusal, and request-boundary enforcement for structurally invalid provenance;
- pending: a scoped coverage representation chosen by an implementation comparison;
- explicit reconciliation-obligation semantics for unknown external effects;
- public/type-comment clarification of Capability's trust boundary;
- reusable negative-observation support only if the implementation comparison shows a common wire value is warranted.

GH-20 preserved the existing assembly and Four-Tier boundaries. `Ordo.Core` still acquires no I/O, provider SDK, persistence, or ROS dependency.

State snapshots written under the new semantics carry a domain-owned `StateViewSchema` identity/version. Their fingerprint covers that schema plus the entire canonical selected view. Schema-v1 snapshots remain readable as historical records, re-encode as v1, and are rejected by both new decision construction and transition authorization until state is recaptured under an explicit current schema.

Authoritative semantics: `doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md`.
Governance decisions: `DF-SDE-2026-0007` through `DF-SDE-2026-0013`.
Implementation/migration plan: `docs/architecture/ordo-next-pass-implementation-plan.md`.

## Reading the code

| Where | What |
|---|---|
| `src/Ordo.Core/` | identifiers, JSON wire vocabulary, clock, resolution modes, evidence, capabilities, obligations, state identity, policy, transitions, serialization |
| `src/Ordo.Decisions/` | confidence, contracts, requests, outcomes, the provider boundary, response validation, escalation, deliberation, observation, resolution, the gate, replay |
| `src/Ordo.Providers.Anthropic/` | `Contract.fs` — the tool schema and the interpretation of a reply, pure and offline-testable. `Adapter.fs` — the only file that touches a network |
| `tests/Ordo.Tests/` | the example domain, the deterministic providers, and the suite |

Compile order inside each project is dependency order, listed most primitive
first, so a cycle cannot be written.
