# ESO-P4: read the FP-PROCESS-THIS-RUN freshness policy

Source: Echelon development speed optimization, Priority Four (follow-up to
`2026-10-28-ESO-P4-obligation-acceptance-contract-freshness.md`).

Indy-init plan revision 2 (`VP-1b2fed568a3b`) binds 29 process and owner
obligations to Praxis producers. Their freshness policy is `FP-PROCESS-THIS-RUN`:
a record written to the run's own Praxis event log for the exact candidate
commit, never reused. Ordo refused any unknown policy, so it refused that plan.

## Requirement

Ordo reads `FP-PROCESS-THIS-RUN` at the requirement level and in an obligation's
acceptance contract. It binds evidence per run (`integrity`, `runId`,
`sourceSha`) and never grants reuse, even when the plan's
`freshnessPolicies[]` declares the policy reusable. Every other unknown policy
is still refused.
