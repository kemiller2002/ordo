# VAC-002-P3: Correct executable authorization admission defects

Target: Ordo. Evidence: https://github.com/kemiller2002/cohort-based-engineering/pull/21 ; research/experiments/VAC-002/P3/RECOMMENDATIONS.md

## R-ADM-1 — Allow authorized deferral
Refusing an uncovered case and recording an unresolved question must be a valid authorized outcome requiring no fabricated authority source. A properly recorded question must not be rejected by admission. Regression: C03-E-r1, C04-E-r1/r3.

## R-ADM-2 — Correct self-citation rule A5
A5 must not reject an agent's decision to refuse or defer solely because the refusal cites its own unresolved question. Regression: C03-E-r3. The earlier checker transformed a correct refusal into a crash.

## R-ADM-3 — Scope rule A6 narrowly
A6 must not reject presentation choices such as ordering or wording, or preservation of already accepted behavior. Regression: C01 (3 tie-break cases) and C02 (2 unchanged due-date cases).

## Release and acceptance
Write failing regression tests first. Fix the authorization specification and implementation where applicable, without broadening accepted business-rule exceptions. Re-run valid and invalid fixtures; ensure that valid deferrals and presentation choices pass and unauthorized consequential rules remain rejectable. Keep executable authorization out of the default path until these defects are fixed and a separate experiment demonstrates actual prevented errors.

P3 evidence: 0 observed unauthorized decisions prevented, 10 authorized/harmless decisions rejected, 4 harmful outcomes, higher cost. This is an evidence-backed defect correction, not approval to deploy a new mandatory gate.
