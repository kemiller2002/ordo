# ARCH-001-03: Optional decision-to-code trace evidence

Priority: P3
Owner system: Ordo (constructibility/traceability); Dokimos may verify
Intake: Praxis document ingestion
Evidence: kemiller2002/cohort-based-engineering `research/architectural-correctness/REPORT.md`, `RESULTS.json`, `BASELINE.md`, `ARCHITECTURE-RULES.md` (branch `claude/echelon-architectural-correctness-m4glhq`, PR #16).
Related: ecir/1 RequirementTraceability, DelegationPolicy.ArchitectureConstraints.

## Finding
Ordo traces requirement → decision → verification obligation and stops there: nothing links a decision id to source files, symbols or tests (BASELINE.md §2.2). ARCH-001 supplied decision → code traceability as a brief convention instead: cite each `DEC-` id in the realizing code and a test. Agents in the Ordo arms did so for every decision (0 untraced at the final commit).

ARCH-001 found **no** decision-fidelity benefit from Ordo contracts on routine work:
- 78/78 decision tests passed in every arm;
- all arms refused the conflicting requirement in T5.

Architecture-rule contract node kinds and mandatory contracts are therefore **not** requested. `ArchitectureConstraints` is still never evaluated.

## Requirement
1. *Optional*: `ordo ecir check` accepts a trace manifest mapping decision ids to code and test locations. It reports decisions with no trace as advisory, never as refusal.
2. No change to admission semantics, and no new mandatory node kinds.

## Acceptance criteria
- With no trace manifest, behaviour is unchanged.
- With a manifest, untraced decisions are listed in the JSON output under a distinct advisory code.
- The docs state that ARCH-001 showed no fidelity gain from contracts on routine work, and that this is evidence plumbing, not a correctness control.
