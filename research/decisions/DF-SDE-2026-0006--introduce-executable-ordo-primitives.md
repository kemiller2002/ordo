---
id: DF-SDE-2026-0006
title: Introduce executable Ordo primitives as three F# projects, with the decision boundary provider-neutral and one adapter
status: accepted
type: decision-record
created: 2026-09-18
updated: 2026-09-18
tags: [architecture, ordo, executable-intelligence, dependencies]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/executable-ordo.md
  - docs/architecture/executable-ordo-traceability.md
  - doctrine/FOUR-TIER-ARCHITECTURE.md
  - method/CHANGE-CLASSIFICATION.md
  - research/decisions/DF-SDE-2026-0005--adopt-ordo-as-the-public-methodology-name.md
---

# DF-SDE-2026-0006

## Context

Three approved requirement passes (*Ordo Executable Intelligence
Requirements* Pass 1, Pass 2 and Pass 3) and an implementation script
directed this repository to extend Ordo from a methodology into a
methodology with a small set of executable primitives, and to prove them
with one narrow end-to-end decision path.

Until now this repository contained no application architecture at all —
`context/ARCHITECTURE.md` says so explicitly: it holds methodology, not
application code. Its F# projects are tooling (`Ordo.Site`, `Ordo.Complexity`,
`Ordo.Churn`) and distribution (`Sde.Core`, `Sde.Cli`, `Sde.PackageBuilder`).
It had no third-party production dependency of any kind.

Adding a runtime library is therefore a change in what this repository *is*,
and the choices below establish boundaries, public contracts and a core
dependency. `docs/architecture/README.md` requires a `DF-` record for exactly
that.

## Decisions

### 1. Three production projects, not one and not five

`src/Ordo.Core`, `src/Ordo.Decisions`, `src/Ordo.Providers.Anthropic`.

Pass 3 §7 names `Ordo.Core` and `Ordo.Decisions` separately, while
`ORDO-3-085` warns that semantic separation does not automatically justify
an assembly. The split is kept because it is load-bearing rather than
cosmetic: `ORDO-0104` requires every core concept to work with no AI
provider, and with the split an application that wants only state, evidence,
capabilities, obligations and transitions references `Ordo.Core` and takes on
no decision or provider machinery whatever.

`Ordo.Execution` (`ORDO-3-082`) and `Ordo.Testing` (`ORDO-3-084`) are **not**
created. Coordination turned out to be one module of about 200 lines
(`Ordo.Decisions/Resolve.fs`), and the test utilities have exactly one
consumer.

The boundaries are asserted by `tests/Ordo.Tests/ArchitectureTests.fs`
against the loaded assemblies, not by review. `Ordo.Core`'s entire reference
set is `FSharp.Core`, `System.Collections`, `System.Runtime`,
`System.Security.Cryptography`, `System.Text.Json`, `netstandard` — and the
test asserts that allowlist, so a new dependency fails the build rather than
appearing in a diff nobody reads.

### 2. The one third-party production dependency is confined to the adapter

`Anthropic` 12.49.0 is referenced by `Ordo.Providers.Anthropic` and by
nothing else.

`ORDO-0203` requires a dependency's value to exceed its cost. The alternative
— hand-rolling request shaping, retry classification and error mapping over
an API that changes — is more code, and code whose defects would be silent.
`ORDO-0204` permits provider SDKs in adapters and prohibits them in the core,
which is what is implemented and what is checked.

Anthropic was chosen over other providers because it is the provider this
repository's own tooling is already developed against, and because its
tool-schema constraint gives the adapter a native way to bound the output
space. Nothing about the choice is structural: `ORDO-4303` is satisfied, and
adding a second adapter changes no contract.

### 3. The authority chain is enforced by a private constructor

`Ordo.Core.Transition.TransitionAuthorization` has a private constructor, and
`Transition.evaluate` is its only producer. A state change therefore cannot
be performed without having passed the source-state, capability, evidence,
obligation, policy and state-identity checks.

This is the mechanical form of the requirements' central invariant — a
decision is not a transition (`ORDO-0603`, `ORDO-3-321`). It was chosen over
documenting the rule because the repository's own doctrine holds that
structural enforcement is what makes a rule survive contact with an agent.

`Ordo.Decisions.Gate.evaluate` deliberately never reads the confidence on a
result. A domain that wants a threshold puts it in its own `Policy`, whose
verdict arrives alongside the capability check rather than instead of it.

### 4. The first bounded decision is this repository's own open method question

`method/CHANGE-CLASSIFICATION.md` states that it "does not yet specify a
mechanical test for 'is this Mechanical Propagation or a disguised Semantic
Change'". That is a genuine `Decide`: judgment is required, the output space
is small and known, and a wrong answer selects the wrong verification path
rather than causing harm.

The domain lives in `tests/Ordo.Tests/Fixtures.fs` rather than in a
production project, because domain states and choices belong to an
application (`ORDO-0806`) and this repository has no application that owns
change sites. The implementation script's §8 anticipates this case.

The contract is scoped in its own `Scope` field to a repository with no
presentation tier, which is how it offers three choices where the method
defines four classes — rather than dropping `Presentation` silently.

### 5. Wire shapes are written by hand and versioned

No encoding is derived from an F# type or case name (`ORDO-8402`,
`ORDO-3-181`), every record carries a schema name and version, and an
unrecognised version or union case is refused rather than guessed at
(`ORDO-8404`). A state snapshot is refused outright if its stored fingerprint
disagrees with its stored view.

## Consequences

- This repository now ships a runtime library as well as a methodology. The
  methodology remains usable without it; `ORDO-3903` is preserved because
  nothing existing gained a dependency.
- `Ordo.Core`'s allowlist test means any future dependency on it is a
  deliberate, reviewed act.
- Executable Ordo v0.1 is **not** published as a package. `ORDO-4001` says to
  package only stable reusable surfaces, and this surface has had no external
  consumer yet. Packaging is a later decision.
- `Ambiguous` outcomes, provider disagreement, outcome records and human
  override remain unimplemented and are recorded as Class B in
  `docs/architecture/executable-ordo-traceability.md`, along with everything
  deferred to Class C and D.
- The judgment quality of a live model on the chosen contract is untested and
  is an experiment, not an implementation task.

## Evidence

- Build: six pre-existing source projects plus three new ones, clean, no
  warnings (`TreatWarningsAsErrors` is on repository-wide).
- Tests: 201 pre-existing tests still pass; 96 new tests pass and 1 skips
  itself because it is opt-in and needs a credential.
- Architecture: 6 assembly-reference tests.
- `./ros validate` passes.
