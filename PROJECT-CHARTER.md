---
id: PROJECT-CHARTER-state-directed-engineering
title: State Directed Engineering Project Charter
status: accepted
version: 0.3.0
created: 2026-09-02
updated: 2026-09-05
supersedes: []
superseded_by: []
---

# State Directed Engineering project charter

## Purpose

Establish and validate State-Directed Engineering (SDE) — the engineering
methodology for building software under State Programming principles
(explicit semantic states, legal transitions, invariants, evidence,
capabilities, obligations, effects, uncertainty, boundary preservation) —
as a durable, evidence-traceable body of doctrine, method, and templates,
distinct from the paradigm itself. See `doctrine/STATE-PROGRAMMING.md` and
`doctrine/STATE-DIRECTED-ENGINEERING.md`.

Charter v0.2.0 superseded the greenfield-pilot placeholder text (v0.1.0,
"communication problem" framing) with the first bounded outcome. This v0.3.0
update adds SDE v0.2's structural-locality and navigation contracts while
preserving their empirical claims as open hypotheses. It does not change
ROS's pilot-evaluation posture: SDE's status as a validated discipline
remains an open, evidence-gated question, not an assumed fact (see
`README.md`'s "Pilot rule").

## Intended users

Engineering agents (human or AI) constructing software who need a
repeatable method for classifying changes, ordering implementation and
verification, and knowing when to stop, under State Programming
constraints. Secondary: researchers extending the evidence base this
methodology rests on.

## First bounded outcome

**Complete (this migration, 2026-09-02):** consolidate HelixNote's State
Programming research (Four-Tier architecture, Boundary Preservation,
Controlled Experiments 1-3) into SDE v0.1 doctrine, method, and evidence,
with full provenance back to HelixNote's repository, branches, and commits.
See `research/packages/RP-SDE-2026-0001--state-programming-to-sde-migration.md`.

**Completed bounded outcome (2026-09-05):** reconcile the repository's
structural-locality and agent-navigation research notes into an explicitly
versioned SDE v0.2 method, evidence chain, templates, distribution package,
and portable structural-review warning.

**Next bounded outcome (not yet started):** execute the first engineering
validation — a real requirement, taken from requirement to verified
software, on a project other than HelixNote, following a frozen method
version — per
`method/FIRST-VALIDATION-DESIGN.md`, evaluated in two to four weeks once
begun.

## Included

- Definition of the SDE doctrine/method knowledge structure and its
  traceability back to HelixNote evidence.
- A migration manifest accounting for every relevant HelixNote artifact.
- Evidence and decision traceability (ROS `EV-`/`HY-`/`TH-`/`DF-` records).
- Evaluation of the Repository Operating System pilot on this specific kind
  of work (research consolidation + doctrine authoring), continued from the
  original charter.
- SDE v0.2 contracts for structural locality, repository semantic maps,
  feature manifests, bounded context escalation, and context telemetry.

## Excluded

- Any modification to HelixNote (treated as read-only source evidence).
- Solving the 4.0 Boundary Change Amplification finding during this phase
  (`research/decisions/DF-SDE-2026-0003--*.md`; superseded on 2026-10-05 by
  `DF-SDE-2026-0016`, which charters BCA detection as its own workstream).
- Redesigning HelixNote's or any other application's architecture as part
  of this phase.
- Executing the first engineering validation (designed, not run, in this
  phase).
- Broad discipline claims without comparative evidence — every REQUIRED/
  RECOMMENDED rule and every empirical outcome claim must be classified in
  `doctrine/EVIDENCE-TO-ENGINEERING-MAP.md`.

## Success criteria

- A fresh agent, with no conversational memory, can answer the questions
  listed in `research/packages/RP-SDE-2026-0001`'s "Original Objective"
  from the repository alone.
- Material decisions cite their evidence and alternatives (ROS `DF-`
  records; `doctrine/EVIDENCE-TO-ENGINEERING-MAP.md`).
- A successor can continue from repository records without chat history
  (`research/journals/JR-SDE-2026-0002`, `HANDOFF.md`, and this charter's
  "Next bounded outcome").
- A fresh agent can route from the repository semantic map to the current
  method, distinguish manifests from semantic authority, and explain why
  structural/context diagnostics are not correctness gates.
- The distributed v0.2 package builds deterministically and its integrity,
  navigation links, and warning-only structural review are executable.
- Pilot measurements can compare the ROS-operated approach with the
  declared baseline (see `context/CURRENT-STATE.md`).

## Constraints and assumptions

- Constraint: HelixNote is read-only for this project; any needed
  correction to HelixNote is recorded as a HelixNote-side ROS-equivalent
  work item, not made silently (mission rule, preserved here for future
  work).
- Constraint: constitutive method contracts may be adopted by an accepted
  decision, but the outcome they predict must remain explicitly
  `Experimental` or `Open` until discriminating evidence supports it. The
  two may not be conflated in `doctrine/EVIDENCE-TO-ENGINEERING-MAP.md`.
- Assumption: HelixNote's three-experiment sequence, though a single
  application's evidence, is sufficient to scaffold a v0.1 method for
  engineering validation — not sufficient to call the method established.

## Owners and decision authority

Repository governance per `docs/00-governance/README.md`; SDE-specific
decisions recorded as `DF-SDE-` records under `research/decisions/`.
