---
id: SDE-ARCH-002
title: Executable Ordo v0.1 requirement traceability
status: draft
version: 0.1.0
created: 2026-09-18
updated: 2026-09-20
related_documents:
  - docs/architecture/executable-ordo.md
  - research/decisions/DF-SDE-2026-0006--introduce-executable-ordo-primitives.md
tags: [architecture, ordo, traceability]
---

# Executable Ordo v0.1 — requirement traceability

Source requirements: *Ordo Executable Intelligence Requirements* Pass 1
(`ORDO-nnnn`), Pass 2 (`ORDO-5501`–`ORDO-111xx`) and Pass 3
(`ORDO-3-nnn`), plus the *Agent Implementation Script — Ordo Executable
Intelligence v0.1*. Pass 3 is the scope authority for what v0.1 contains
(`ORDO-3-040`).

Classes, per `ORDO-3-040`–`ORDO-3-044`:

| Class | Meaning |
|---|---|
| **A** | v0.1 core — required before executable Ordo is real |
| **B** | required before ROS consumes Ordo as a stable dependency |
| **C** | hardening, before broad production adoption |
| **D** | deferred research |
| **E** | explicit non-goal |

Statuses: **Implemented**, **Already satisfied**, **Deferred by approved
scope**, **Blocked**. Nothing is marked Implemented without a test or an
assembly-level check behind it.

## Class A — v0.1 core

| Requirement | Status | Implementation | Test |
|---|---|---|---|
| ORDO-0401, 0402–0406, 3-010, 3-050 — ResolutionMode: Compute / Decide / Deliberate | Implemented | `src/Ordo.Core/Resolution.fs` | `CoreTests` — wire tokens, narrowest-mode ordering |
| ORDO-3-051, 0802, 1901, 9701–9703 — strong semantic identifiers | Implemented | `src/Ordo.Core/Identifiers.fs` | `CoreTests` — refusal shapes, version positivity |
| ORDO-0501–0503, 3-052 — evidence first-class, referenceable, with provenance | Implemented | `src/Ordo.Core/Evidence.fs` | `CoreTests`, `WireTests` |
| ORDO-0504, 3-053, 1003, 3-325 — missing evidence distinct from uncertainty | Implemented | `Evidence.RequirementCheck`, `DecisionOutcome.InsufficientEvidence` | `InvariantTests` invariant 5; `SliceTests` |
| ORDO-5904 — an inference is not an observation | Implemented | `EvidenceKind.Inferred`, `EvidenceRequirement.AcceptableKinds` | `CoreTests`, `WireTests` |
| ORDO-0505 — evidence availability is not evidence validity | Implemented | nothing scores evidence; `checkRequirement` reports presence only | `CoreTests` |
| ORDO-0601, 0602, 3-323, 3-172 — capability is authority; confidence never grants it | Implemented | `src/Ordo.Core/Capability.fs`; `Gate.evaluate` never reads confidence | `InvariantTests` invariant 4 |
| ORDO-0701–0703, 9401–9403 — obligations with identity, state and completion evidence | Implemented | `src/Ordo.Core/Obligation.fs` | `CoreTests`, `SliceTests` |
| ORDO-0801, 0803, 0805, 3-011, 3-054 — typed decision contract | Implemented | `src/Ordo.Decisions/Contract.fs` | `DecisionTests` |
| ORDO-0804, 3-125, 3-320 — providers cannot invent choices | Implemented | `ChoiceSpace.parse`, `Validation.validate` | `InvariantTests` invariant 1; `DecisionTests`; `AdapterTests` |
| ORDO-0806, 3-030 — domain choices belong to the application | Implemented | contracts are generic in `'choice`; the example domain lives in tests | `Fixtures.fs` |
| ORDO-0901, 0902, 3-012, 3-055 — deliberate request context | Implemented | `src/Ordo.Decisions/Request.fs`; `Resolve.toProviderRequest` sends only contract inputs | `SliceTests` — what reaches the provider |
| ORDO-0903, 0904, 3-094, 5802 — identifiable state, decisions bound to it | Implemented | `src/Ordo.Core/StateIdentity.fs` | `CoreTests`, `SliceTests` |
| ORDO-1001, 3-013, 3-057 — structured outcomes | Implemented | `src/Ordo.Decisions/Outcome.fs` | `DecisionTests` |
| ORDO-1002 — no forced false decision | Implemented | provider may return insufficient-evidence or escalate | `AdapterTests`, `SliceTests` |
| ORDO-1101, 3-058, 6302 — confidence optional | Implemented | `DecisionResult.Confidence : Confidence option` | `DecisionTests` |
| ORDO-1102, 1103, 3-024, 6301, 6304 — confidence provenance and scale | Implemented | `src/Ordo.Decisions/Confidence.fs` | `DecisionTests` |
| ORDO-1104 — Ordo does not fabricate calibration | Implemented | `EmpiricallyCalibrated` requires a basis and a sample; nothing here constructs one | `DecisionTests` |
| ORDO-1301, 1302, 1305, 3-064, 4504 — explicit escalation, including to a person | Implemented | `src/Ordo.Decisions/Escalation.fs`; `Resolve.escalationFor` | `SliceTests` |
| ORDO-1306, 3-101 — escalation history preserved | Implemented | `EscalationChain` appends only | `SliceTests` |
| ORDO-1401–1403 — state stays application-owned | Already satisfied | no base type, no inheritance; `StateSnapshot` wraps a view | `Fixtures.fs` |
| ORDO-1404, 1405, 3-019, 3-062, 3-063 — explicit transitions, typed failure | Implemented | `src/Ordo.Core/Transition.fs` | `SliceTests` |
| ORDO-0603, 3-321, 2902 — a decision does not execute a transition | Implemented | private `TransitionAuthorization` constructor; `Gate` is a separate step | `InvariantTests` invariant 2 |
| ORDO-1501–1504, 3-020, 3-093 — inspectable, deterministic, versioned policy | Implemented | `src/Ordo.Core/Policy.fs` | `SliceTests` |
| ORDO-1601, 3-059, 3-242 — provider-neutral boundary | Implemented | `src/Ordo.Decisions/Provider.fs` | `ArchitectureTests` |
| ORDO-1602, 3-095 — provider identity, model and adapter version | Implemented | `ProviderIdentity` | `SliceTests`, `LiveAdapterTests` |
| ORDO-1603, 9201, 9202 — declared provider capabilities, failing early | Implemented | `ProviderCapability`; `Resolve.execute` checks before calling | `SliceTests` |
| ORDO-1604, 3-321 — typed provider failure | Implemented | `ProviderError` | `DecisionTests`, `InvariantTests` invariant 8 |
| ORDO-1701, 1702, 3-021, 3-060, 3-123, 4502 — non-AI and fake providers | Implemented | `DecisionProvider` is a record of functions; `Fixtures.scriptedProvider` | the whole suite runs on it |
| ORDO-1801–1803, 3-065, 3-098, 3-129 — replay, non-mutating, preserving semantics | Implemented | `src/Ordo.Decisions/Replay.fs` | `ReplayTests`; `InvariantTests` invariant 6 |
| ORDO-1802, 3-326 — replay is not execution | Implemented | `Replay` never calls `Gate` | `ReplayTests`, `InvariantTests` |
| ORDO-1901–1905, 3-066, 3-102, 7201 — observation facts, not conclusions | Implemented | `src/Ordo.Decisions/Observation.fs` | `SliceTests` |
| ORDO-1904, 3-096, 4202 — provider usage exposed, never invented | Implemented | `ProviderUsage`; `Adapter.usageOf` | `SliceTests` |
| ORDO-1903, 3-097, 4103 — timing observable | Implemented | `StartedAt` / `CompletedAt` / `ResolutionObservation.duration` | `SliceTests` |
| ORDO-2001, 3-023, 3-322 — decision is not outcome | Implemented | decision records are values; outcomes are separate records | `InvariantTests` invariant 3 |
| ORDO-2301–2303, 3-180–3-184, 8401, 8402 — deliberate, versioned, language-neutral serialization | Implemented | `src/Ordo.Core/Json.fs`, `src/Ordo.Core/Wire.fs` | `WireTests` |
| ORDO-2401, 2402, 3-279 — no database required | Already satisfied | nothing persists; records are values | `ArchitectureTests` |
| ORDO-2501, 2502, 3-170, 3-171 — credentials are never contract content | Implemented | the caller supplies the `AnthropicClient`; no credential type exists in Ordo | `ArchitectureTests` |
| ORDO-2503, 3-157, 5805, 7401 — context minimisation and redaction | Implemented | `Redaction`; `ResolveOptions.withRedaction` | `CoreTests`, `SliceTests` |
| ORDO-2601, 2602, 3-073 — deterministic things stay deterministic | Implemented | policy, validation and transition evaluation are pure and synchronous | `DecisionTests` |
| ORDO-2701–2703, 3-074 — expected failure is data | Implemented | `Result` and typed unions throughout; no expected path throws | `SliceTests` — a throwing provider returns `InternalFailure` |
| ORDO-2801, 2802, 3-126, 4505 — stale state rejected | Implemented | `Gate.evaluate`, `Transition.StaleState` | `InvariantTests` invariant 7; `SliceTests` |
| ORDO-3001, 3002, 6501, 3-034 — human review is a typed state, not an error | Implemented | `PolicyRequiresHumanReview`, `RequiresHumanReview`, `ChangeNeedsHumanReview` | `SliceTests` |
| ORDO-3201, 3-110 — Ordo does not depend on ROS | Implemented | no reference exists | `ArchitectureTests` |
| ORDO-3501–3504, 3-240, 3-243 — small API, unions over inheritance, exhaustive matching | Implemented | no class hierarchy; `TreatWarningsAsErrors` makes FS0025 an error | the build |
| ORDO-3601–3607, 3-190–3-203 — test coverage and no network in the normal suite | Implemented | `tests/Ordo.Tests/` | 96 passing, 1 skipped by design |
| ORDO-3704, 8601–8603, 3-234 — mechanical architecture checks | Implemented | `tests/Ordo.Tests/ArchitectureTests.fs` | 6 tests |
| ORDO-3801–3803, 3-210–3-220 — documentation, examples, anti-patterns | Implemented | `docs/architecture/executable-ordo.md` | — |
| ORDO-3-061, 3-124, 4503 — one real provider adapter, outside Core | Implemented | `src/Ordo.Providers.Anthropic/` | `AdapterTests`; `LiveAdapterTests` (opt-in) |
| ORDO-3-122 — the slice executes end to end | Implemented | `Resolve.execute` → `Gate.evaluate` → observation | `SliceTests` |
| ORDO-0201, 0202, 3-070 — F#, current SDK, no independent upgrade | Already satisfied | all production code is F# on `net8.0` per `Directory.Build.props` | the build |
| ORDO-0203, 0204, 3-078, 3-079 — minimal dependencies, no framework, no DI | Implemented | `Ordo.Core` references only FSharp.Core and the BCL; providers are records of functions | `ArchitectureTests` |
| ORDO-0205, 3-073, 3-075, 3-076 — functional core, async and cancellation only at the boundary | Implemented | only `Resolve`, `Replay` and the adapter are asynchronous | `SliceTests` — cancellation |
| ORDO-4101, 4102, 3-077 — no reflection-heavy design | Already satisfied | no reflection outside the architecture tests | `ArchitectureTests` |
| ORDO-4201, 3-238 — no paid service for build or test | Implemented | the live test skips itself | the suite |

## GH-20 next-pass implementation — versioned state identity

Authority: `DF-SDE-2026-0007`, `DF-SDE-2026-0013`.

| Requirement | Status | Implementation | Test |
|---|---|---|---|
| Domain-defined state-view schema identity/version | Implemented | `StateViewSchema` in `src/Ordo.Core/StateIdentity.fs` | `CoreTests` — constructor rules and schema-version identity |
| Fingerprint entire selected view plus schema identity/version | Implemented | `StateFingerprint.ofView` hashes the canonical schema+view envelope | `CoreTests` — field-order stability, relevant changes, schema changes, ambient-state projection |
| No selective invalidation/fingerprinting | Implemented by absence | Ordo receives a domain-selected view and hashes all of it | `CoreTests` — ambient state outside the projected view has no effect |
| State-snapshot wire v2 | Implemented | `StateSnapshotSchemaVersion = 2`; explicit `viewSchema` member | `WireTests` — v2 round trip and tamper rejection |
| Immutable v1 historical readability | Implemented | v1 decoder verifies the original view-only fingerprint and returns `ViewSchema=None`; re-encoding preserves v1 | `WireTests` — legacy fixture |
| Legacy history cannot authorize new work | Implemented | `DecisionRequest.create` refuses unversioned state; `Transition.evaluate` emits `UnversionedCurrentState` | `WireTests`, `CoreTests` |
| Unrelated wire records remain schema v1 | Implemented | state snapshots version independently; evidence/obligation wire schema remains v1 | `WireTests` — existing schema-version refusal test remains scoped to `SchemaVersion` |

The migration is intentionally non-destructive: no v1 record is rewritten or re-fingerprinted under v2 semantics. A new action requires recapturing current state with a current `StateViewSchema`.

## GH-21 next-pass implementation — derived evidence closure

Authority: `DF-SDE-2026-0009`.

| Requirement | Status | Implementation | Test |
|---|---|---|---|
| Deterministic transitive closure of selected evidence | Implemented | `EvidenceDependency.closure` in `src/Ordo.Core/Evidence.fs` | `CoreTests` — dependency-first ordering and caller-order independence |
| Valid shared dependencies | Implemented | visited-set traversal deduplicates shared inputs without treating them as cycles | `CoreTests` — shared dependency case |
| Missing Derived input refused | Implemented | `MissingEvidenceDependency` | `CoreTests`; `DecisionTests` request-boundary rejection |
| Direct self-cycle refused | Implemented | `EvidenceDependencyCycle` with repeated start/end id | `CoreTests` |
| Multi-node cycle refused | Implemented | path-aware Derived traversal | `CoreTests` — A -> B -> C -> A |
| Duplicate evidence identity refused | Implemented | `DuplicateEvidenceId` before traversal | `CoreTests` |
| New decision requests require structurally closed provenance | Implemented | `DecisionRequest.create` maps dependency errors to `InvalidEvidenceDependencies` | `DecisionTests` |
| No generic relationship graph | Implemented by scope | only `EvidenceKind.Derived` is traversed; correction/supersession/history relations are untouched | source/API surface |

Closure validation is structural, not epistemic: it does not claim that a derivation is correct, only that its declared inputs exist and form a reconstructable acyclic derivation.

## GH-22 next-pass implementation — scoped context coverage

Authority: `DF-SDE-2026-0008`, `DF-SDE-2026-0014`, `EX-SDE-2026-0006`.

| Requirement | Status | Implementation | Test |
|---|---|---|---|
| Contract/domain-defined named scopes | Implemented | `CoverageScope` in `src/Ordo.Core/Coverage.fs` | `CoreTests` |
| Complete / Partial / Unknown are distinct | Implemented | closed `CoverageStatus` union | `CoreTests`, `WireTests` |
| Multiple scopes per decision | Implemented | `DecisionRequest.Coverage : ContextCoverageClaim list` | Strata mixed-dimension slice test |
| Coverage backed by provenance | Implemented | every claim requires one or more Evidence IDs; request construction verifies they exist | `DecisionTests`, `WireTests` |
| Contract may require Complete named scope | Implemented | `DecisionContract.RequiredCoverage` / `requiringCoverage` | `SliceTests` |
| Required missing/Partial/Unknown coverage stops provider execution | Implemented | `InsufficientCoverage` and `Resolve.execute` preflight | `SliceTests` |
| Partial != Unknown | Implemented | distinct `CoveragePartial` / `CoverageUnknown` failures | Strata and Time Tracking scenario tests |
| Provider sees coverage explicitly | Implemented | `ProviderCoverage`, separate `<coverage>` block in Anthropic adapter | `AdapterTests`, `SliceTests` |
| Stable wire vocabulary | Implemented | `ordo.context-coverage` and `ordo.coverage-requirement` schema v1 | `WireTests` |
| No global completeness flag or inference | Implemented by API shape | only explicit scoped claims can be Complete | source/API surface |
| Evidence-only alternative rejected | Governed | EX-SDE-2026-0006 / DF-SDE-2026-0014 | repository-backed spike |

The Strata scenario proves one request may legitimately contain `relations = Complete` and `relation_access = Partial`. The Time Tracking scenario proves an unreadable current reference catalog is `Unknown`, not an empty Complete catalog. Inactive historical references and restore-overlap rules remain ordinary domain state rather than being absorbed into coverage.

## GH-23 next-pass implementation — unknown effects and Capability trust boundary

Authority: `DF-SDE-2026-0010`, `DF-SDE-2026-0011`.

| Requirement | Status | Implementation | Test |
|---|---|---|---|
| Succeeded / Failed / Unknown remain distinct | Implemented | `ExternalEffectOutcome` in `src/Ordo.Core/ExternalEffect.fs` | `CoreTests` |
| Unknown always creates reconciliation work | Implemented | `ExternalEffect.recordOutcome` -> `ReconciliationRequired` + `ReconcileExternalEffect` | `CoreTests` |
| Blind retry is not inferred | Implemented | `RetrySafetyNotEstablished` is the default semantic value | `CoreTests` |
| Retry may be considered only when host supplies external-contract proof | Implemented | `RetrySafeByExternalContract basis` / `mayRepeatBeforeReconciliation` | `CoreTests` |
| Retry safety does not erase reconciliation | Implemented | Unknown always creates obligation independent of retry-safety value | `CoreTests` |
| Reconciliation can gate later domain action | Implemented | existing obligation-aware transition requirement consumes named reconciliation obligation | `CoreTests` |
| Effect identity survives wire obligation form | Implemented | `reconcile-external-effect` token + `effectId` | `WireTests` |
| Ordo performs no effect/probe I/O | Implemented by boundary | `ExternalEffect` is pure Core semantics only | architecture tests |
| Capability explicitly means host-supplied semantic authority | Implemented | `Capability.fs` public comments and architecture docs | source/API review |
| Capability remains independent from provider/confidence | Preserved | existing `CapabilitySet` and Gate/Transition checks unchanged | invariant/architecture tests |

The application still owns effect execution, idempotency-key mechanics, compensation, reconciliation probes, and storage-specific conflict behavior. Ordo only preserves the semantic fact that the outcome is Unknown and that reconciliation remains required.

## Class B — before ROS integration

| Requirement | Status | Note |
|---|---|---|
| ORDO-3-090, 3-091, 7101–7103 — stable execution identity and causality | **Implemented early** — `ResolutionId`, `CorrelationId`, `CausedBy` on requests and observations; tested in `ReplayTests` |
| ORDO-3-092, 3-093 — contract and policy version survive persistence | **Implemented early** — both are on `ResolutionObservation` and its wire form |
| ORDO-3-099, 3-100, 6401–6405, 2002, 2003 — outcome linkage, human override preserving the original | Deferred by approved scope — the observation carries the identity an outcome record would link to; the outcome record itself is Class B |
| ORDO-1001 `Ambiguous`, ORDO-6201–6205, 3-150, 3-151 — candidate sets and provider disagreement | Deferred by approved scope — `ORDO-3-057` permits deferring `Ambiguous` when it is not simple, and it is not: it needs candidate scoring and a provider capability |
| ORDO-1201–1205, 7701–7704, 7801–7803 — deliberation execution | Partially implemented — the request, budget, capability-scoping and typed status types exist in `src/Ordo.Decisions/Deliberation.fs`; no deliberating provider is executed in v0.1 |

## Class C — hardening

Deferred by approved scope. Types that anticipate them exist where doing so
cost nothing, and are named here so that "we have the type" is not mistaken
for "we have the behaviour".

| Requirement | Present | Absent |
|---|---|---|
| ORDO-3-140, 3-141, 5601–5605 — contract lifecycle and supersession | `ContractLifecycle`, `ContractReplacement`, retired contracts refuse new requests | no migration tooling |
| ORDO-3-142, 3-143, 9501–9504 — evidence staleness and conflict | staleness is implemented and tested; conflicting evidence can coexist | no conflict-resolution model |
| ORDO-3-147, 3-148, 6601–6605, 6701–6704 — timeouts and retry distinction | per-attempt timeout, typed cancellation, transport-only retry with an observable count | no policy-driven timeout configuration |
| ORDO-3-152, 3-153, 6801–6804, 6901–6903 — partial failure and side-effect distinction | obligations can represent recovery work | no effect-request/effect-completion model |
| ORDO-3-158, 7404 — replay access control | — | a caller that can read a request can replay it |
| ORDO-3-159, 3-160, 7301–7304 — audit reconstruction and additive correction | observations carry what an audit needs; records are immutable values | no correction/supersession record type |
| ORDO-3-162, 8901–8903 — parallel evaluation | snapshots are immutable; nothing shared is mutable | no reconciliation of conflicting parallel results |
| ORDO-3-163, 9001–9004 — cache safety | no cache exists, which is the required default | — |
| ORDO-3-156, 9101–9103 — hidden fallback prohibited | no fallback exists anywhere | — |
| ORDO-10001–10007, 3-198–3-202 — deep testing | cancellation, corrupt-response, adversarial-evidence and stale-state tests exist; invariant tests are exhaustive over the closed choice space | no mutation testing; no property-test dependency introduced (`ORDO-10002`) |

## Class D — deferred research

`ORDO-3-250`–`ORDO-3-263` and Pass 1 §51 in full: provider ranking and
routing, calibration curves and dashboards, automatic `Deliberate → Decide`
and `Decide → Compute` detection, local or trained decision models, RL/RLCD,
corpus export, adaptive thresholds, automatic contract discovery, automatic
policy modification, cross-application decision libraries. None implemented.
The architecture does not block any of them: `ORDO-4303` is satisfied
because provider selection is a parameter of `Resolve.execute`, so an
orchestration layer can choose one without a contract changing.

## Class E — explicit non-goals

`ORDO-3-270`–`ORDO-3-281`. None implemented, and their absence is asserted
by what is not in the source tree: no workflow engine, no rules engine or
DSL, no agent framework, no plugin architecture, no event bus, no service
locator, no DI container, no generic repository, no provider registry, no
model-training platform, no authorization platform, no event-sourcing
framework, no vector database, no semantic-memory platform, no provider
marketplace, no required database, web server or UI.

## Conflicts surfaced rather than resolved silently

Per `ORDO-5501` and the implementation script's precedence rules.

1. **`ORDO-3-080`/`ORDO-3-081` name two projects; `ORDO-3-085` and
   `ORDO-3-004` say not to split on semantics alone.** Resolved in favour of
   two projects, because the split is load-bearing rather than cosmetic:
   `ORDO-0104` requires every core concept to work with no AI provider, and
   an application using only state, evidence, capabilities and transitions
   now references `Ordo.Core` and gets no decision or provider machinery at
   all. `ArchitectureTests` asserts the boundary. `Ordo.Execution`
   (`ORDO-3-082`) and `Ordo.Testing` (`ORDO-3-084`) were **not** created:
   coordination is one module (`Resolve.fs`) and the test utilities are used
   by one project.

2. **`ORDO-0203` minimises dependencies; the adapter needs a provider
   client.** Resolved by confining the one third-party production dependency
   (`Anthropic` 12.49.0) to `Ordo.Providers.Anthropic`, which `ORDO-0204`
   explicitly permits, and asserting mechanically that it reaches neither
   `Ordo.Core` nor `Ordo.Decisions`.

3. **`ORDO-3-121` recommends three or fewer choices; the method defines
   four change classes.** Resolved by scoping the contract to a repository
   with no presentation tier and saying so in the contract's own `Scope`
   field, rather than silently dropping a class.

4. **Not a conflict, but worth recording:** the implementation script's
   §8 prefers "a real, narrow decision from the existing Ordo
   repository/domain". The decision chosen is real — it is the open question
   `method/CHANGE-CLASSIFICATION.md` names — but this repository has no
   production application that owns change sites, so the domain lives in
   `tests/`, per the same section's fallback.

## Unresolved questions

- Whether a live model can answer `sde.change-site-class` usefully is
  untested. `LiveAdapterTests` asserts the contract holds, not that the
  judgment is good. Measuring that is an experiment, not an implementation.
- `ORDO-6104` (prose conflicting with the typed result) is implemented by
  detecting whole declared tokens in the rationale. That catches a rationale
  that names another choice; it does not catch one that argues for another
  choice without naming it. Recorded as a known limit rather than as
  coverage.
