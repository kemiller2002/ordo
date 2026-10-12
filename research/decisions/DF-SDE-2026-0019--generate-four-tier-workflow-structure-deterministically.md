---
id: DF-SDE-2026-0019
title: Generate Four-Tier finite-state workflow structure deterministically from validated plans
status: draft
type: decision-record
created: 2026-10-12
updated: 2026-10-12
tags: [construction, four-tier, determinism, gh-65]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/construction-engine-v1.md
  - docs/architecture/construction-plan-v1.md
  - research/decisions/DF-SDE-2026-0018--constrain-construction-to-a-typed-vocabulary.md
  - src/Ordo.Construction/Construction.fs
  - tests/Ordo.Construction.Tests/TimerSmokeTests.fs
provenance:
  contributions:
    EXE-20261012T130500312Z-ff47f231:
      operations: [created]
      at: 2026-10-12T13:15:43.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record the deterministic construction engine decision for GH-65"
---

# Context

Even with a typed plan (DF-SDE-2026-0018), an agent that writes the recurring
Four-Tier plumbing by hand still chooses names, placement, dependency
direction and failure behavior on every change. That leaves room for mistakes
such as a catch-all transition, a permissive default guard, a dependency on a
higher tier, or a stray infrastructure import. Ordo #65 asks for a narrow
engine that removes those choices without becoming a general code generator.

# Decision

1. **A separate project, `src/Ordo.Construction`.** It references `Ordo.Core`
   only and depends on no package. It is not packed. Its single public entry
   point, `construct`, accepts only a `ValidatedPlan`. The emitter is internal.
2. **A pure projection that performs no effects.**
   - Output is immutable `Artifact` values with paths, tiers, ownership,
     content, digests and traces.
   - The engine also returns the extension contracts that generated code
     requires, the obligations generation cannot discharge, and structural
     checks labeled as derived from the plan.
   - Applying output is a host's job. The pure `planWrites` function refuses
     to overwrite or remove handwritten files.
3. **The engine decides placement, names, ordering and dependency shape.**
   Tier 1 holds closed unions. Tier 2 holds:
   - an exhaustive legality table with no catch-all;
   - the refusal union;
   - capability, guard and invariant checks;
   - requested effects as data.

   Tier 3 is generated only when ports are declared. It reads ports,
   delegates to Tier 2, has the host perform effects and reports outcomes,
   including `Unknown`. Tier 4 is never generated.
4. **Custom behavior is a contract, never a default.**
   - Guards, invariants and ports are handwritten. A missing implementation
     does not compile.
   - Unresolved plan items, and independent behavioral verification, become
     explicit obligations.
5. **Determinism.** Output is a function of the canonical plan and
   `ordo.construction/1`, with LF endings, no BOM and fixed ordering. Any byte
   change is a new generator version, and the goldens are regenerated in the
   same commit.

# Alternatives

- **Templates or a template language.** Rejected: they reintroduce free-form
  shapes and the expression escape hatches that DF-SDE-2026-0018 removed.
- **Generating a host tier or project file edits.** Rejected: Tier 4 and
  project configuration belong to the host, and generating them would
  silently choose infrastructure.
- **Writing files from the engine.** Rejected: it would put an effect inside
  semantic code, and Praxis #242 owns trusted application.
- **A catch-all `| _ -> None` in `target`.** Rejected: an exhaustive listing
  keeps every refusal visible in review and avoids compiler warnings when
  every pair is legal.

# Evidence

The suite is `tests/Ordo.Construction.Tests`: 34 tests, all passing.
- **Golden regeneration.** The goldens are compiled under
  `TreatWarningsAsErrors`, and regeneration reproduces them byte for byte for
  the time-entry timer and a synthetic door fixture.
- **Time-entry smoke test.** All 12 (state, command) pairs agree with a table
  written from the original requirement text at
  `time-entry-state-machine@73a63f1`. The source has no requirement IDs, so the
  table cites line ranges.
- **Compile-negative probes** each fail with the asserted error code:
  - FS0001: the engine is given unvalidated data;
  - FS1092: the internal emitter is reached directly;
  - FS0764: a rule is missing;
  - FS0366: a host omits a port;
  - FS0039: tiers are compiled in upward order.
- **Mutation checks during development:**
  - a permissive emitter mutant and a guard-dropping mutant were caught by the
    golden check;
  - with the goldens regenerated from the permissive mutant, four independent
    behavior tests failed.

# Known limits

- Version 1 supports neither data-carrying states nor more than one workflow
  per plan, and has no computed predicates. A plan that needs any of them must
  declare it as an unresolved obligation.
- The time-entry application's elapsed-time arithmetic, the thirty-second rule,
  entry creation and diagnostic codes stay handwritten, or are unresolved
  obligations.
- Nothing here applies artifacts to a real repository. Praxis #242 does that.
