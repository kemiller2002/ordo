---
id: DF-SDE-2026-0018
title: Constrain agent construction to a typed, versioned vocabulary bound to a prepared ECIR cohort
status: draft
type: decision-record
created: 2026-10-12
updated: 2026-10-12
tags: [ecir, construction, prevention, gh-64, gh-65]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/construction-plan-v1.md
  - docs/architecture/ecir-v1.md
  - src/Ordo.Core/ConstructionPlan.fs
  - src/Ordo.Core/ConstructionPlanWire.fs
  - schemas/ordo-construction-plan.v1.schema.json
  - tests/Ordo.Tests/ConstructionPlanTests.fs
  - tests/Ordo.Tests/ConstructionPlanAdversarialTests.fs
provenance:
  contributions:
    EXE-20261012T125044669Z-9fb1c008:
      operations: [created]
      at: 2026-10-12T13:02:41.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record the typed construction vocabulary decision for GH-64"
---

# Context

ECIR (#59) conserves source requirements, and cohort readiness (#61,
DF-SDE-2026-0017) refuses to prepare work that has unsatisfied semantic
prerequisites. After preparation, though, a coding agent still writes arbitrary
files, code, project settings and dependencies. Every architectural choice it
makes is one it can get wrong, and validators only find those mistakes
afterwards. Ordo #64 asks for prevention instead: a bounded vocabulary in which
the wrong choices cannot be expressed at all.

# Decision

1. **Placement.** Add `ConstructionPlan` and `ConstructionPlanWire` to
   `Ordo.Core`, after `ConstructionIrWire`, as Tier 1 vocabulary. Wire versions
   are `ordo.construction-plan/1` and `ordo.construction-vocabulary/1`.
   Generation lives in a separate project (#65). No dependency is added, and
   the strict JSON decoding rules are shared through the internal `StrictJson`
   module rather than copied.
2. **Closed operations.**
   - `defineWorkflow` (exactly one per plan in v1);
   - `defineState`, with a role of `initial`, `ordinary` or `terminal`;
   - `defineTransition`; commands are derived from transitions;
   - `defineInvariant`;
   - `declarePort`: `currentTime`, or `transitionEffect` for a command;
   - `bindCapability`;
   - `declareUnresolved`.

   Predicates have one kind, `extension`: a named contract that handwritten
   code implements. Expression strings have no representation. A name is ASCII
   words starting with a letter and is identified by its PascalCase form.
   Capability text is a dotted ASCII word. The agent chooses no operation IDs,
   ownership, paths, names of generated artifacts or placement. Each is either
   derived from what an operation defines or left to the engine.
3. **Binding.** `validate` requires a `PreparedCohort`, so readiness cannot be
   skipped. It recomputes both the manifest digest and the blueprint digest, and
   compares the cohort ID. The bound decision set must equal exactly the
   decisions and standalone rationales the cohort requires.
4. **Trace.**
   - Every operation cites assigned requirements and in-scope nodes. The
     cohort node may not be cited.
   - Every anchored node must link one of the operation's requirements.
   - Every assigned requirement must be traced, and the plan reports how:
     `Constructed`, `UnresolvedOnly` or `WorkflowLevel`.
   - Capability bindings must cite an externally authorized decision.
5. **Identity and opacity.**
   - `ValidatedPlan` has a private constructor.
   - Its digest is the SHA-256 of canonical JSON: names normalized, lists
     ordered, keys sorted.
   - The time-entry fixture's digest is pinned.
   - Changing canonicalization requires a version change.
6. **Authority.** A validated plan is semantic agreement with one exact
   cohort. It is not a credential, not permission for effects, not evidence of
   implementation and not evidence of verification. Hosts (Praxis #242) keep
   all of that.

# Alternatives

- **A general template or expression language.** Rejected: it hands back
  exactly the choices this work removes, and #64 forbids expression strings.
- **Validating agent-written F# after the fact.** Rejected: that detects
  mistakes instead of preventing them.
- **A separate construction state model that duplicates ECIR.** Rejected: the
  existing issue comment requires `ConstructionIr` to stay canonical.
- **Declaring commands, operation IDs and ownership explicitly.** Rejected:
  each is one more agent choice that can disagree with the rest of the plan.
  They are derived instead.

# Evidence

- Real fixture: `tests/fixtures/construction/time-entry-timer/`.
  - Source text is verbatim excerpts from
    `kemiller2002/time-entry-state-machine@73a63f1`, and each excerpt's digest
    is rechecked by tests.
  - The ECIR conserves all 7 excerpts. The short-stop disagreement stays
    `unresolved` outside the cohort, where the plan cannot reach it.
- An independent semantic and API review found nine issues, all fixed with
  regression tests:
  - coverage gamed through the workflow or cohort node;
  - unanchored capability policy;
  - trusted manifest digest;
  - quadratic reachability;
  - self-loop dead ends;
  - merged duplicate guards;
  - free-text capability IDs and questions;
  - no cohort-binding check;
  - a misleading `UnauthorizedDecision` name.
- Test results:
  - 65 adversarial tests and 17 plan tests pass in the full Ordo.Tests run
    (354 passed, 1 live test skipped).
  - 9 compile-negative probes compile against the built assembly with the real
    F# compiler and assert the exact error codes FS0001, FS1093, FS1129 and
    FS0039, plus a positive control that must compile.

# Consequences and reversibility

The new modules are additive. `ecir/1` is unchanged. Hosts pinned to earlier
releases are unaffected until they choose to qualify a new release. The
vocabulary is deliberately narrow: data-carrying states, multiple workflows and
computed guards are unsupported in v1. A plan that needs them must say so with
`declareUnresolved` and must not approximate them. Widening the vocabulary
requires a new vocabulary version and a decision record.
