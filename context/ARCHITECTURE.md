# State Directed Engineering architecture and repository semantic map

This file is the repository's existing equivalent of an SDE semantic map. The
root README links here rather than creating a competing `SDE-MAP.md`.

## Repository semantic map

| Semantic area | Purpose | Location | Authoritative map / manifest |
|---|---|---|---|
| Repository governance | authority, lifecycle, work protocol, quality | `AGENTS.md`, `docs/00-governance/`, `docs/work-protocol.md` | `docs/00-governance/README.md` |
| SDE research | evidence, hypotheses, theories, decisions, journals, REPs | `research/` | generated `registries/` plus `research/CHRONOLOGY.md` |
| SDE doctrine | authoritative paradigm and architectural rules | `doctrine/` | `doctrine/STATE-DIRECTED-ENGINEERING.md` and `doctrine/EVIDENCE-TO-ENGINEERING-MAP.md` |
| SDE method | construction, navigation, verification, agent procedure, metrics | `method/` | `method/CONSTRUCTION-METHOD-v0.2.md` |
| Reusable project artifacts | work/execution/trial/map/manifest templates | `templates/sde/` | this table; templates are individually named |
| Distribution package | generated execution package and installer/verifier | `distribution/` | `distribution/DISTRIBUTION-MAP.json` and `distribution/README.md` |
| Public website | the Ordo site: content, evidence manifest, generator, validator | `site/`, `src/Ordo.Site/`, `tests/Ordo.Site.Tests/` | `site/README.md` |
| Executable Ordo | runtime primitives for resolution modes, evidence, capabilities, obligations, bounded decisions, transitions and observation | `src/Ordo.Core/`, `src/Ordo.Decisions/`, `src/Ordo.Providers.Anthropic/`, `tests/Ordo.Tests/` | `docs/architecture/executable-ordo.md` |
| Supplied source documents | verbatim externally supplied inputs this repository executed, kept as provenance rather than as governance | `input-documents/` | `input-documents/README.md` |

This repository contains methodology domains rather than application
features. Separate feature manifests would duplicate the indexes above and are
therefore not required here. An adopting application uses
`method/FEATURE-MANIFESTS.md` and the templates under `templates/sde/` for its
real bounded features.

## Current architecture

SDE remains a methodology repository. It now also ships one runtime library
— executable Ordo — whose architecture is accepted in
[`DF-SDE-2026-0006`](../research/decisions/DF-SDE-2026-0006--introduce-executable-ordo-primitives.md)
and described in
[`docs/architecture/executable-ordo.md`](../docs/architecture/executable-ordo.md).
It is the first code in this repository to which the Four-Tier Architecture
applies as its own architecture rather than as advice to adopters:
`Ordo.Core` is Tier 1 vocabulary, `Ordo.Core.Transition` and
`Ordo.Decisions.Gate` are Tier 2, and `Ordo.Providers.Anthropic` is the only
Tier 4 component. The methodology remains usable without it, and nothing
that existed before gained a dependency on it.

The repository's own knowledge architecture, established by the SDE
migration, is unchanged:

```
research/   evidence, hypotheses, theories, decisions, journals, packages,
            migration manifest, chronology  -- explains WHY
doctrine/   State Programming, SDE, Four-Tier Architecture,
            Boundary Preservation, Glossary, Evidence-to-Engineering Map,
            Contradictions register          -- defines WHAT
method/     Construction Method v0.2, Change Classification,
            Navigation and Context Discovery, Feature Manifests,
            Verification Method, Agent Execution Rules,
            Engineering Metrics, First Validation Design -- defines HOW
templates/sde/  execution-log, completion-report, engineering-trial-report,
                semantic-map and feature-manifest templates
                                              -- makes HOW repeatable
```

Reused ROS conventions rather than duplicated: `research/journals`,
`research/packages`, `research/theories`, `research/evidence` are ROS's
own canonical roots (`ros.json`); `templates/` already existed and gained
an `sde/` subdirectory rather than a competing top-level directory.

## The doctrine SDE itself defines for future application projects

`doctrine/FOUR-TIER-ARCHITECTURE.md` (Semantic Model / Transition /
Orchestration / Host, downward-only dependencies) and
`doctrine/BOUNDARY-PRESERVATION.md` (representation collapse vs.
uncoordinated duplication; the Three-Contract Model), together with
`doctrine/STRUCTURAL-LOCALITY.md` (semantic area / authority / responsibility
cluster / physical module; bounded feature context), are the architectural
doctrine SDE recommends *to projects that adopt it* — not this repository's
own architecture, which has none (it holds methodology, not application
code).

## Required first decision (for the first engineering validation project)

`method/FIRST-VALIDATION-DESIGN.md` requires the target project to have "at
least one genuine multi-tier boundary" before the trial begins — that
project's own architecture decision is out of scope for this repository and
belongs to whichever ROS work item executes the validation.

## Architectural constraints

- Canonical records must remain independent of any model vendor or chat
  (carried forward from the original charter; SDE's doctrine documents
  satisfy this by design — no vendor-specific content appears in
  `doctrine/` or `method/`).
- Secrets and sensitive communication content must not enter fixtures,
  logs, or prompts (carried forward; not yet exercised, since this
  repository holds no application data).
- Generated views must not silently replace canonical source records
  (`registries/` is generated by `./ros registry build`; hand-editing it is
  prohibited per `framework/policies/OUTPUT-POLICY.md`).
- Feature manifests and nested agent instructions are navigation layers. They
  point to semantic authority and must not restate it.
- Executable Ordo must not depend on ROS, and `Ordo.Core` must not depend on
  any model-provider SDK. Both are asserted against the loaded assemblies in
  `tests/Ordo.Tests/ArchitectureTests.fs` rather than by review.
