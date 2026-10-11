---
id: DF-SDE-2026-0017
title: Prepare cohorts only after transitive dependency readiness checks
status: draft
type: decision-record
created: 2026-10-11
updated: 2026-10-11
tags: [ecir, prevention, readiness, gh-61]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/ecir-v1.md
  - src/Ordo.Core/ConstructionIr.fs
  - tests/Ordo.Tests/ConstructionIrTests.fs
provenance:
  contributions:
    EXE-20261011T073712704Z-a851bd5a:
      operations: [created, modified]
      at: 2026-10-11T07:41:54.341Z
      last: 2026-10-11T07:44:34.971Z
      actor:
        kind: agent
        id: openai/codex
        provider: openai
        model: unknown
        runtime: codex
      reason: "Record dependency-readiness bypass reproduction and opaque preparation boundary"
---

# Context and evidence

User-directed prevention-by-construction work exposed a cohort-readiness gap
in Ordo #61, following Praxis #228/#229. `validateCohort` checked only direct
selected-cohort requirement dispositions and their decision approvals, despite
validating the whole artifact's shape. Four new regressions reproduced readiness
with a deferred dependency, transitive unresolved dependency, cross-requirement
unapproved decision and blocker behind a verification obligation.

# Proposed decision

After full semantic validation, compute the selected cohort's required node
scope. Follow explicit dependencies, selected/reached cohort construction
obligations (excluding other delivery cohorts unless explicitly depended on),
and required decisions for reached source requirements. Refuse every nonmodeled
requirement and unauthorized decision in that scope. Keep unrelated requirements
represented without falsely blocking independent work.

Expose `prepareCohort` with an opaque private-constructor success snapshot, whose
view retains exact immutable inputs and scoped source/node/decision identities.
Assigned sources stay separate from required dependency sources, preventing a
caller from treating prerequisite requirements as work implemented by this cohort.
The ordinary validator remains available for diagnostics. Preparation delegates
to that same semantic authority rather than copying rules into Praxis.

# Authority and compatibility

A snapshot is pure semantic evidence, not authenticated host permission,
source-manifest trust, behavioral verification or proof that prerequisite work
has completed. Host receipts, freshness, dependency evidence and protected
effects remain separate obligations. No model confidence or artifact field can
grant authority. No wire schema, existing package pin or Praxis runtime gate is
changed here. The stricter check repairs the documented dependency/blocker
contract; formerly accepted dependency-blocked artifacts now refuse.

# Alternatives, risk and reversibility

Checking only direct membership preserves the demonstrated bypass. Traversing
explicit edges alone misses obligations attached to a cohort's own requirement
mappings. Treating every source as an execution blocker prevents unrelated
ready work. The selected closure follows declared semantic links; misleading
links or an incorrect specification still need independent review.

The API addition and pure readiness check can be reverted locally, but hosts
must not qualify the old acceptance behavior as equivalent. Retain the existing
pinned release until release/package qualification and independent acceptance.
No network/model/persistence dependency is added to Ordo.Core.

# Validation and remaining work

The initial FSI harness executed the actual semantic test functions using a
minimal assertion shim: 21 tests, four expected failures on the old validator.
After implementation, all 25 semantic source tests passed, including scope
isolation, all nonmodeled dispositions, dependent cohort obligations and diamond
dependencies. This harness is not the official xUnit runner. Ordo.Core built
under the available .NET 10 target override with zero warnings/errors; the
repository's supported .NET 8 build and official CI suite remain separate checks.
The private constructor has a compiler rejection probe (FS1093).

The official xUnit runner subsequently passed all 41 ECIR tests and the full
Ordo.Tests suite: 257 passed, one live-provider test skipped, zero failed.
Production/test assemblies target .NET 8; execution used the installed .NET 10
runtime with DOTNET_ROLL_FORWARD=Major. Native .NET 8 runtime CI remains a
separate qualification. Registry build passed. Current Praxis validation found
pre-existing abandoned-backlog/active-live WI-0035 disagreement on main; Ordo
#62 records it. The original creation/start interval and absence of ID reuse
support reconciliation of the same obligation. Native `work abandon` recorded
an effective-current cancellation preserving history, and validation then passed.
No research completion or historical telemetry is inferred.

Record CI results in the PR. Runtime consumption, released
package qualification and the protected local coordinator pilot remain pending.
