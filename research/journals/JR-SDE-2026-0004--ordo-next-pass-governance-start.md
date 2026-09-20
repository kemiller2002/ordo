---
id: JR-SDE-2026-0004
title: Ordo next-pass governance start and pre-upgrade baseline
research_area: state-directed-engineering
author_agent: ChatGPT
created: 2026-09-20
related_mission:
related_package: RP-SDE-2026-0004
evidence_ids:
  - EV-SDE-2026-0011
hypothesis_ids: []
theory_ids: []
tags: [ordo, gh-18, governance, baseline, four-tier]
---

# Research Journal Entry

## Objective

Begin GH-18 under the current ROS protocol and establish an executed pre-upgrade baseline before promoting or implementing any requirement from `RP-SDE-2026-0004`.

## Starting state

- Requirements intake existed as `RP-SDE-2026-0004`.
- External work-item truth was created as GitHub issue #18.
- No GH-18 local ROS work context existed yet.
- The first CI run after adding the intake package was red before reaching Ordo tests.

## Actions taken

1. Inspected the current ROS work protocol and SDE routing/architecture authority.
2. Created GitHub issue #18 as the external work item.
3. Investigated the failed main-branch ROS validation.
4. Corrected a stale generated research-package registry.
5. Investigated the next validation failure.
6. Corrected RP status from illegal `candidate` to legal `review`.
7. Rebuilt the generated research-package registry.
8. Re-ran the existing ROS validation workflow to a green implementation baseline.
9. Located the successful SDE distribution run associated with executable-Ordo commit `e82cbe9...`.
10. Verified by GitHub compare that later commits through the governance baseline changed only the requirements intake and generated registry.
11. Created branch `work/gh-18-ordo-next-pass`.
12. Used a temporary branch-only GitHub Actions runner to execute the repository's actual:
   `./ros work begin GH-18 --type research --actor ChatGPT`
13. The runner committed the ROS-generated work state/event and deleted its own temporary workflow.

## Observations

### Process enforcement worked

The intake did not get a false green result:

- stale generated registry stopped CI immediately;
- illegal RP lifecycle status stopped CI immediately after the registry was repaired.

Both failures were repository-governance failures, not Ordo test failures.

### Local work state is now real

On branch `work/gh-18-ordo-next-pass`, `.ros/context/current.json` records:

- id: `GH-18`
- type: `research`
- state: `active`
- semanticState: `active`

This state was produced by the repository ROS CLI, not hand-authored.

### Executable baseline is green

See `EV-SDE-2026-0011`.

## Evidence collected

- `EV-SDE-2026-0011`
- GitHub Actions ROS validation run `35493473648`
- GitHub Actions SDE distribution run `35381674083`
- branch work-start commit `d3b710ce0c852c7577cc510c19c1d6017a32f940`

## Hypotheses considered

No new SDE outcome hypothesis is promoted by this baseline.

The baseline exists to support causal/regression attribution during the next-pass implementation, not to prove the new architecture candidates.

## Attempts to falsify

The process itself found two defects before accepting the baseline:

- stale registry;
- illegal RP status.

The implementation baseline then ran the same Ordo architecture/invariant suite that guards existing properties including provider choice-space limits, decision/transition separation, confidence/capability separation, stale-state authorization, and assembly boundaries.

## Decisions and rationale

- Keep `RP-SDE-2026-0004` in `review` until governance is complete.
- Do not modify authoritative doctrine merely because the research package recommends a requirement.
- Preserve Four-Tier Architecture as a hard constraint on every candidate.
- Use actual CI execution, not checked-in tests alone, as the implementation baseline.

## Failures and dead ends

- The connected GitHub interface cannot directly execute repository shell commands.
- A branch-only temporary workflow was therefore used to execute the repository's own ROS CLI. It removed itself after producing the ROS state/event, leaving no permanent workflow surface.
- The live-provider test was intentionally skipped by the repository's existing workflow and remains outside this baseline.

## Confidence changes

Confidence that the repository has a clean mechanical pre-upgrade baseline: high.

Confidence that every RP-SDE-2026-0004 candidate should be promoted: unchanged. Governance still has to decide each item separately.

## Files changed

Durable records created in this phase:

- `research/evidence/EV-SDE-2026-0011--ordo-next-pass-pre-upgrade-baseline.md`
- `research/journals/JR-SDE-2026-0004--ordo-next-pass-governance-start.md`

ROS-generated state lives under `.ros/`.

## Highest-value next step

Govern `ORDO-NEXT-01` through `ORDO-NEXT-06` one by one, beginning with the cross-cutting Four-Tier Architecture guardrail and the minimal semantically complete state-view definition.

For each requirement, record:

- disposition: accept / revise / reject / defer;
- semantic authority affected;
- four-tier placement;
- compatibility/version impact;
- tests required;
- migration behavior;
- evidence that would falsify the decision.
