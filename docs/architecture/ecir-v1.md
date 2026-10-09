# Echelon Construction IR (ECIR) v1

Status: **draft / implementation in progress**. Contract owner: Ordo. Tracking: [ordo#59](https://github.com/kemiller2002/ordo/issues/59). Cross-system implementation: [conditor#81](https://github.com/kemiller2002/conditor/issues/81), [praxis#220](https://github.com/kemiller2002/praxis/issues/220), [dokimos#28](https://github.com/kemiller2002/dokimos/issues/28).

## Purpose

ECIR is a typed and versioned handoff between **lossless source requirement intake** and executable implementation cohorts. It prevents each coding agent from reinterpreting or silently omitting source requirements. It is not a replacement for the original requirements, a source of new authorization, or proof of implementation.

Existing foundations are preserved:
- Conditor: `conditor.requirements-trace/v1` and source-document digests.
- Ordo: decisions, capability separation, evidence and typed, provider-neutral semantic policy.
- Praxis: work items, evidence, work groups, authorized execution, checkpoints and telemetry.
- Dokimos: independently collected verification evidence, never inferred from generated code alone.

## Two authorities

1. **Pinned source manifest** supplied by Conditor or another trusted intake process, independent of the model's ECIR output. Contains a qualified key and original ID, document identity, revision and content digest for **every** ingested requirement, plus an immutable manifest digest.
2. **ECIR blueprint** returned by an analysis agent. Contains one registry entry for every source requirement, its explicit disposition and bidirectional references to construction nodes.

**Critical invariant:** For manifest `M` and blueprint `B`, their multisets of qualified requirement keys must match one-for-one, and each matching key must preserve original ID, document, revision and digest. The validator compares against `M`; it does not trust `B.requirementCount` or an LLM claim of coverage.

A raw requirement ID may recur in different source documents. The qualified key disambiguates it while `originalId` remains unchanged. A user-visible trace must always be able to recover the source ID and original location.

## Dispositions and distinct measurements

- `modeled`: mapped to construction obligations, **not yet implemented or verified**.
- `unresolved`: contradictory, underspecified or otherwise requires a decision. Must preserve the issue; blocks affected cohorts.
- `deferred`: explicitly out of current execution scope, with reason and authority.
- `superseded`: redirected to another source requirement, with justified and validated target.
- `rejected`: explicitly declined, with reason and authority.
- `duplicate` (future wire variant): retained as its own source entry, with a justified canonical target. Never drop duplicates from intake.

Traceability %, modeled %, implementation %, independently verified %, unresolved count and deferred count are **different metrics**. No report may label a modeled or deferred requirement as passed.

## IR nodes and directions

Each node has a stable ID, a typed kind (decision, contract, invariant, interface, cohort, verification obligation, conflict, deferral, engineering rationale), `requirementKeys`, optional other-node dependencies and an explicit justification when permitted. A requirement stores its outward `nodes` list. Links are **bidirectional**, not two loosely interpreted lists.

- Every source requirement must link to at least one relevant node. A deferred requirement still links to its deferral rationale.
- Every `modeled` normative requirement must link to an applicable verification obligation, unless a separately authorized waiver is recorded by the appropriate governing system.
- Every decision/contract/invariant/interface/cohort/verification obligation must have an originating requirement or an explicit, independently approved engineering rationale. The model's confidence is not approval.
- Node dependencies cannot dangle, self-reference or contain cycles where an execution dependency graph must be acyclic.
- Never generate behavioral code from unknown operators or unconstrained `expression: string` fragments. Typed operation vocabularies must be versioned and validated.
- Shared code and decisions can implement multiple requirements; one requirement may need multiple nodes, implementations and tests.

## Gates

| Gate | Check | Authority |
| --- | --- | --- |
| Intake | All original IDs, locations and source digests collected without loss | Conditor/source manifest |
| Structural | Known schema version, types, required fields and permitted variants | ECIR wire reader/schema |
| Semantic | Exact identity/content equality; uniqueness; reciprocal links; disposition semantics; no orphan nodes; obligations | Ordo pure validator |
| Construction ready | No unresolved hard blockers affecting cohort; authorized decisions, dependency graph, pinned compatible versions and independent verification plan | Praxis using Ordo results and external approvals |
| Completion | Independently observed acceptance, constraints, mutations/adversarial tests, code and evidence per original requirement | Dokimos and Praxis |

A failed gate is an explicit refusal with actionable IDs. An agent may revise a new blueprint version; it must never silently rewrite history or override an approved decision.

## Start with a lossless, deliberately unresolved scaffold

The Conditor import produces a pinned `ecir-source-manifest.json` alongside its existing requirements trace. Ordo can now generate an initial ECIR artifact **without having an LLM invent any architecture or dispositions**:

```bash
sde ecir scaffold \
  --manifest path/to/ecir-source-manifest.json \
  --output path/to/ecir-draft.json \
  --json

sde ecir validate \
  --manifest path/to/ecir-source-manifest.json \
  --blueprint path/to/ecir-draft.json \
  --json
```

Scaffolding refuses invalid/duplicate/empty source inputs and refuses overwriting an existing output. The output includes **exactly one registry entry for each imported requirement**, even when local IDs overlap in distinct source files. Each entry initially has an `unresolved` disposition and a unique `conflict` node with `analysis pending` explanation. Thus the scaffold can pass **source-conservation validation** but is **not ready to execute**. No decision approval, behavioral verification, or authorization is represented by the scaffold.

The requirements-analysis agent consumes this committed draft, source manifest and original documents. It must replace pending nodes with explicit bidirectional references to **decisions, contracts, invariants, coherent cohorts and verification obligations**. The validator rejects dropped requirements and broken references. A new candidate must be a new committed artifact and digest, not an overwrite of an earlier approved revision. Every individual requirement remains present even if deferred, superseded, rejected or disputed.

`sde ecir validate` returns `executionAuthorized: false` regardless of structural validity. Praxis separately verifies the pinned Ordo binary, committed ECIR digest and outside approvals.

## Compilation and batching

Reason over related requirements together, select cohesive implementation cohorts, freeze interfaces and invariants, compile after each shared-contract milestone, execute code generation with cohesive context and run targeted checks, then independently verify the finished behavior. **50 or 100 source IDs are not assumed to equal 50 or 100 behaviors**, nor are they an unconditional batch-size target. Optimize against verified throughput and rework, not green tests alone.

## Wire and determinism

Initial wire version: `ecir/1`. An ECIR artifact pins the source manifest digest; its canonical JSON digest is content-addressed and transmitted to execution agents by immutable Git reference. The JSON schema gives **structural** validation only; the Ordo validator enforces source-set equality and semantic rules. It must not accept an LLM-authored manifest as the source authority. JSON keys, reference strings and hashes have stable explicit meanings; a breaking semantic change requires a schema version change.

## Important negative tests

1. Requirement omitted from blueprint; requirement invented by blueprint; same key repeated; zero-item bypass.
2. Source ID/document/revision/digest mismatch even when key agrees; same local ID from different documents remains distinct.
3. Missing or duplicate node; dangling outward/inward edge; missing reciprocal edge; node dependency missing/self/cyclic.
4. Modeled requirement without verification obligation; deferred/unresolved without explanation; unsupported or self-targeting supersession.
5. Unjustified architectural node; conflicting decisions; unresolved hard conflict improperly declared runnable.
6. Changes to pinned inputs or previously approved architecture do not silently alter a running cohort; change produces a new digest and impact analysis.
7. Fake passing tests and generated self-attestation do not satisfy independent verification.

## Work sequence

**First increment (this PR):** versioned contract, source-set semantic validator, adversarial unit tests, schema and fixture. Keep package and dependencies unchanged. **Next:** strict JSON decoding/encoding and canonical artifact hashing; Conditor import bridge; Praxis execution gate and cohort dispatch; independent Dokimos verification; controlled trial versus natural-language and batch-only execution.

These are proposals until a CI run and approved decision/evidence records confirm implementation. No pass/fail claim should be based on this document alone.
