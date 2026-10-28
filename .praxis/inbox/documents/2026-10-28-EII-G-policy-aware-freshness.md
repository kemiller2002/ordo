# EII-G: Policy-aware freshness bindings for the verification schedule

Priority: P1
Owner system: Ordo
Intake: Praxis document ingestion. Source material, not a canonical requirement.
Origin: Echelon Engineering Improvement Initiative, Workstream G ("Incremental verification"), Ordo half.
Owner approval: 2026-10-28, Echelon Engineering Improvement Initiative, Workstream G (policy-aware binding).

## Finding (verified)

`src/Ordo.Core/VerificationPlanWire.fs` `bindingsForPolicy` ignores its policy argument and binds every
`indy-init.verification-plan/1` freshness policy (FP-LIVE-PER-COMMIT, FP-REVIEW-PER-COMMIT,
FP-DETERMINISTIC-INPUT-DIGEST) to `integrity`, `runId` and `sourceSha`. Deterministic evidence whose content
identity is unchanged can therefore never be reused, not even across CI reruns within one rehearsal, while
Dokimos `evidence plan` treats content-addressed evidence as reusable.

## Requirements

- **EII-G-R1.** Bindings are chosen per policy. FP-DETERMINISTIC-INPUT-DIGEST evidence may be reused when it is
  bound to `integrity`, its input digest and the rehearsal/plan identity (never from an earlier rehearsal or
  another plan), not to `runId`. FP-LIVE-PER-COMMIT and FP-REVIEW-PER-COMMIT keep `integrity`, `runId` and
  `sourceSha`.
- **EII-G-R2.** An unknown policy fails safe: no reuse is ever granted for it.
- **EII-G-R3.** `causa.acceptance-evidence/1` is read with optional input-digest and rehearsal/plan identity
  fields. Absent fields make deterministic evidence not reusable: it falls back to the per-run binding. No invalid
  reuse is ever permitted.
- **EII-G-R4.** Negative controls: matching digest+plan is fresh; mismatched input digest, another rehearsal or
  plan, integrity failure, live evidence from an earlier run, review evidence for another sourceSha are stale;
  missing digest is not reusable; unknown policy fails safe.
- **EII-G-R5.** `ordo verification schedule` output stays backward compatible; any JSON change is additive and
  documented.
- **EII-G-R6.** Measure, on real data only, reusable vs stale classification under old and new binding over the
  rehearsal-005 plan and evidence; say so if the inputs do not allow an honest measurement.
- **EII-G-R7.** Record the decision as a Decision Record and document per-policy freshness bindings.
