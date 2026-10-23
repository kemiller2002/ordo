# VC-002: Provenance-backed typed construction (bounded prototype)

## Goal
Prototype a constrained F# construction model in Ordo that represents authorized states, transitions and boundaries before implementation, rather than adding another general decision gate.

## Requirements
1. Accept a versioned intermediate representation containing requirement IDs, accepted decision IDs, source revisions, scope, unresolved questions and supersession relationships. Every consequential business rule must carry provenance or be explicitly marked unverified.
2. Never infer authority solely from existing source code, tests, generated artifacts or an agent's own proposal. Unverified rules must be surfaced as questions, not silently adopted, deleted or rewritten.
3. Prototype F# discriminated unions, single-case types, validated constructors and transition functions for a bounded financial-domain slice. Encode only genuinely established invariants; distinguish policy ambiguity from mechanically invalid states.
4. Prevent invalid state transitions and cross-boundary operations through compiler-enforced interfaces where feasible. Document what remains runtime-only or cannot be represented in the type system.
5. Ensure authorized refusal/deferral and unresolved-question recording remain valid outcomes. Do not repeat P3 admission defects R-ADM-1..3. Keep experimental authorization gates off the default path.
6. Emit traceability from generated types/constructors/transitions to input requirement/decision IDs and stable source revisions; provide deterministic output and reproducible builds.
7. Keep dependencies minimal and avoid external services or Kubernetes.

## Acceptance
Small compileable F# prototype, negative compile tests for invalid constructions, positive tests for valid deferral and behavior, provenance manifest, reproducibility instructions and measured limitations. Do not claim prevention from compile tests alone. Keep feature experimental pending VC-001.
