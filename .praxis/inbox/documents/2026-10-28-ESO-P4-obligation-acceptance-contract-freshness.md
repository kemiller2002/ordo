# ESO-P4: Verification schedule respects per-obligation acceptance-contract freshness

Priority: P2
Owner system: Ordo
Target repositories: ordo
Programme: Echelon development speed optimization, Priority Four (incremental verification)
Intake: Praxis document ingestion. Source material, not a canonical requirement.

## Finding (verified)

`src/Ordo.Core/VerificationPlanWire.fs` reads freshness per requirement only (`freshness.policy`, DF-SDE-2026-0026)
and ignores the revision-2 per-obligation `acceptanceContract`, whose `freshness.policy` is the strictest of the
requirement policy and the obligation's evidence-category policies (Indy-init `docs/VERIFICATION-PLAN-CONTRACT.md`,
item 10). An obligation needing live or review evidence under a deterministic requirement could therefore be
satisfied by reused evidence. In the committed Indy-init v2 plan the two policies agree for all 326 obligations,
so this is latent.

## Requirements

- **ESO-P4-O1.** An obligation whose acceptance-contract policy permits no reuse is judged on the per-run binding
  only. A contract never widens its requirement's reuse.
- **ESO-P4-O2.** No contract (revision 1): inherit the requirement, schedule unchanged.
- **ESO-P4-O3.** A present contract without `freshness.policy`, or with an unknown policy, refuses the plan with its
  JSON path.
- **ESO-P4-O4.** Negative controls for each rule; full Ordo suite passes with exact counts.
