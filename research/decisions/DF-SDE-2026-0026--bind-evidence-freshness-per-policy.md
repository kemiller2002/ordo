---
id: DF-SDE-2026-0026
title: Bind verification evidence freshness per policy, reusing deterministic evidence only by content identity
status: accepted
type: decision-record
created: 2026-10-28
updated: 2026-10-28
tags: [verification, freshness, evidence-reuse, incremental-verification, eii-g]
supersedes: []
superseded_by: []
related_documents:
  - docs/architecture/verification-schedule.md
  - src/Ordo.Core/VerificationPlanWire.fs
  - src/Ordo.Core/VerificationSchedule.fs
  - src/Sde.Cli/Verification.fs
  - tests/Ordo.Tests/VerificationScheduleTests.fs
  - tests/Sde.Core.Tests/VerificationCommandTests.fs
  - .praxis/inbox/documents/2026-10-28-EII-G-policy-aware-freshness.md
provenance:
  contributions:
    EXE-20261028T005653390Z-07e5a75a:
      operations: [created]
      at: 2026-10-28T01:08:21.000Z
      actor:
        kind: agent
        id: anthropic/claude-code
        provider: anthropic
        model: unknown
        runtime: claude-code
      reason: "Record owner-approved policy-aware freshness binding (EII Workstream G)"
---

# Context

`indy-init.verification-plan/1` gives every requirement one of three freshness
policies: FP-LIVE-PER-COMMIT, FP-REVIEW-PER-COMMIT and
FP-DETERMINISTIC-INPUT-DIGEST. The plan itself declares only the third
`reusable: true`. Its reuse condition reads "reusable within one run only when
the test identity and the digest of every declared input … are unchanged …;
never reused from an earlier rehearsal".

Through Ordo 1.18.0, `VerificationPlanWire.bindingsForPolicy` ignored its
argument. Every policy bound evidence to `integrity`, `runId` and `sourceSha`.
The code comment called this a conservative reading of "never reused from an
earlier rehearsal". Because `runId` was always bound, deterministic evidence
whose content identity had not changed could never count as fresh in another
run. That held even for a CI rerun of the same commit in the same rehearsal.
Dokimos `evidence plan` (`EvidenceReuse.fs`) treats such content-addressed
evidence as reusable.

# Decision

Owner approval: 2026-10-28, Echelon Engineering Improvement Initiative,
Workstream G ("Incremental verification").

1. **Live and review evidence keep the per-run binding.** FP-LIVE-PER-COMMIT
   and FP-REVIEW-PER-COMMIT bind to `integrity`, `runId` and `sourceSha`, and
   are never reused. This preserves live freshness and per-commit review.
2. **Deterministic evidence gains a content-addressed alternative.**
   FP-DETERMINISTIC-INPUT-DIGEST keeps the per-run binding. Its evidence is
   also fresh when it is reusable, meaning it carries all of the following,
   each equal to the current value:
   - `integrity`;
   - `inputDigest`: the digest of the declared inputs of the requirement's
     tests;
   - `planDigest`;
   - `rehearsalId`.

   Reuse is not bound to `runId` or `sourceSha`, because the input digest
   covers the tested source paths. Binding to the plan and the rehearsal
   means evidence is never reused from another plan or an earlier rehearsal.
3. **Reuse is strict and opt-in.** A reuse key that is missing on either side
   never grants reuse, and the evidence falls back to the per-run binding.
   Current values for `inputDigest` and `rehearsalId` come only from a
   caller-supplied `ordo.verification-reuse-context/1`
   (`--reuse-context`). `planDigest` is the plan's own digest. A plan that does
   not declare the deterministic policy `reusable: true` grants no reuse.
   Evidence that carries a reuse key with a value different from its subject's
   is stale, even within its own run.
4. **Optional evidence members.** `causa.acceptance-evidence/1` is read with
   optional `input_digest`, `plan_digest` and `rehearsal_id`. Absence means
   the evidence is not reusable. A member that is present but blank or
   mistyped is refused with its JSON path.
5. **An unknown policy refuses the plan.** The error is
   `$.requirements[i].freshness.policy: unsupported freshness policy …`.
   This is consistent with how the plan reader already handles unsupported
   enumeration values (`exclusion.status`, `testIdentity.status`). A refusal
   cannot grant reuse. Silently applying the strictest binding would instead
   hide a plan Ordo does not understand.

# Alternatives

- **Keep the per-run binding for every policy (the 1.18.0 behaviour).**
  Rejected by the owner. It forbids reuse that the plan and Dokimos both
  permit, and makes reruns repeat unchanged deterministic work.
- **Bind deterministic evidence to the input digest only.** Rejected. It would
  allow reuse from an earlier rehearsal or another plan, which the plan
  forbids.
- **Strictest binding for unknown policies.** Rejected in favour of refusal
  (see 5). Both are fail-safe; refusal is the existing convention and makes
  the gap visible.
- **Treat a missing current value as matching, as the per-run binding does.**
  Rejected for reuse. A reuse decision must never rest on an unjudged key.

# Consequences

- With no reuse context, a plan with known policies schedules to the same
  bytes as in 1.18.0. The schema `ordo.verification-schedule/1` is unchanged.
  `mismatchedBindings` may additionally name `inputDigest`, `planDigest` or
  `rehearsalId`; this is additive.
- A plan with a fourth policy id is now refused, where 1.18.0 scheduled it.
- Reuse delivers value only once evidence producers write `input_digest`,
  `plan_digest` and `rehearsal_id`, and the caller supplies per-requirement
  input digests. Rehearsal 5 evidence carries none of them. Replayed over
  Rehearsal 5's plan (`VP-e77a2134e1b0`) and its one evidence result, old and
  new bindings both classify 0 obligations reusable and 0 stale: the plan binds
  no test to any of its 326 obligations, so all are `testMissing`. The
  potential scope is 177 obligations in 50 requirements under
  FP-DETERMINISTIC-INPUT-DIGEST.
- Ordo does not compute input digests. Producing them (for example from
  Dokimos `CurrentIdentity`) belongs to the evidence producer and the caller.
