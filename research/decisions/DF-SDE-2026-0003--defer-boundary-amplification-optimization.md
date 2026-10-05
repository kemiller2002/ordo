---
id: DF-SDE-2026-0003
title: Defer solving the 4.0 Boundary Change Amplification finding during this migration
status: superseded
type: decision-record
created: 2026-09-02
updated: 2026-10-05
tags: [governance, bca, scope]
supersedes: []
superseded_by: [DF-SDE-2026-0016]
---

# DF-SDE-2026-0003

## Context

Boundary Change Amplification measured at exactly 4.0 boundary files per
semantic decision across three independent HelixNote experiments
[EV-HN-2026-0003, EV-HN-2026-0004, EV-HN-2026-0005]. This is a strong,
repeated signal that boundary-propagation volume is architecture- and
mutation-shape-invariant for this class of change under the mechanisms
tried so far — an important engineering target ([TH-SDE-2026-0004]).

## Decision

This migration does not attempt to design a construction-optimization fix
for the 4.0 finding. It preserves the evidence, formalizes SDE v0.1
doctrine and method, and records the finding as an explicit, prioritized
open engineering problem for future, dedicated work — not something to
solve opportunistically while also standing up the methodology itself.

## Rationale

Changing architecture and methodology simultaneously would make it
impossible to attribute a future improvement to either cause cleanly. The
migration mission's own explicit instruction is not to redesign the
architecture during migration and not to solve the 4.0 boundary tax during
migration. `research/migration/STATE-PROGRAMMING-TO-SDE-MIGRATION-MANIFEST.md`
and `doctrine/CONTRADICTIONS-AND-DEPRECATED.md` both record this figure and
route any future work on it through a new, dedicated ROS work item rather
than through an ad hoc change made now.

## Consequences

`method/CONSTRUCTION-METHOD-v0.1.md` and `doctrine/FOUR-TIER-ARCHITECTURE.md`
present the current boundary-crossing mechanisms as they are, without a
speculative "future improved boundary architecture" section. Any future
change explicitly aimed at reducing BCA must open its own ROS work item and
its own evidence trail, separate from this migration's.

## Reversibility

Fully reversible — this is a sequencing decision, not a technical
commitment. It can be revisited the moment a dedicated, isolated engineering
effort is chartered for boundary-amplification reduction.

## Status

`superseded` by DF-SDE-2026-0016 (2026-10-05). The migration this record
scoped has finished. Boundary amplification now has its own work item
(GH-52) and evidence trail, as the Consequences section above required.
The decision text above is unchanged. HY-SDE-2026-0004 stays rejected.
