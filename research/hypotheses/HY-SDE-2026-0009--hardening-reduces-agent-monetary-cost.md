---
id: HY-SDE-2026-0009
title: Mechanical boundary hardening reduces an agent's monetary cost to implement an identical mission
status: proposed
type: hypothesis
disposition: untested
confidence: very-low
confidence_rationale: "Untested, not disbelieved. No evidence exists in either direction; the lowest available label is used because the schema has no 'unestimated' value, and TH-SDE-2026-0003 gives concrete reason to think the effect could run either way."
source_experiment: EX-SDE-2026-0001
evidence_for: []
evidence_against: []
related_evidence: [EV-SDE-2026-0007]
created: 2026-09-17
updated: 2026-09-17
tags: [agent-cost, tokens, experiment-3, open-question, preregistered, attempted-inconclusive]
---

# HY-SDE-2026-0009

**Statement:** An engineering agent implementing an identical, frozen
mission incurs lower monetary cost working against a mechanically hardened
boundary architecture than against the unhardened baseline architecture.

**Status: OPEN — attempted, still untested.** This hypothesis is registered *before*
its experiment (EX-SDE-2026-0001) runs, so that the prediction is on record
and cannot be adjusted to fit a result.

**Attempted and stopped short.** EX-SDE-2026-0001 ran six trials on
2026-09-17 and did not reach its threshold [EV-SDE-2026-0007]. Three runs
verified — baseline $19.94 and $15.92, hardened $12.66 — and three void, two
on a criterion that proved mis-specified and one that never completed. That
is n=2 against n=1, where the criteria require three per condition with
separation exceeding within-condition spread; the two baseline runs alone
span $4.02 on identical inputs, the same order as the gap between
conditions.

Every completed run placed baseline above hardened. **This is not recorded
as evidence for.** A direction from an underpowered set is what Experiment 3
already produced and had to withdraw.

**Evidence for:** None. Note specifically that Experiment 3's apparent
figures — 59% fewer tool uses, 68% fewer tokens, 59% less wall clock — are
**not** evidence for this hypothesis and must not be recorded as such.
Condition B's telemetry covers a resumed partial run beginning after roughly
78% of its eventual log already existed [EV-HN-2026-0005;
`doctrine/CONTRADICTIONS-AND-DEPRECATED.md`]. The true total could match or
exceed Condition A's.

**Evidence against:** None.

**Why it is not simply "probably true":** TH-SDE-2026-0003 establishes that
mechanical discovery and engineering cost are different constructs that have
already been observed moving in opposite directions in this evidence base —
Condition B had a *lower* as-experienced mechanical discovery rate while
spending less on search and repair. A hardened architecture that enumerates
more propagation sites can plausibly cost an agent *more* to work through,
not less. The `M-TE-CSSTATE-RIPPLE` and `M-TE-FSSTATE-RIPPLE` results point
the same way: the compiler names the sites, and there are many of them.

**Unknowns:** Everything. Direction, magnitude, whether any effect survives
outside this one codebase, one mutation and one model family.

**Disposition:** UNTESTED. To be decided only by EX-SDE-2026-0001 under its
pre-registered falsification criteria, including the criterion that a null
or reversed result is a reportable outcome rather than a failed run.

**Implications if supported:** SDE may claim a cost effect *within the
scope measured*, with the run-level spread published alongside. No effect
size generalises from three runs on one codebase.

**Implications if not supported:** The open question on the Ordo site's
research page gains a measured answer instead of an absence, and the
disqualified Experiment 3 percentages stay permanently disqualified with a
measured result standing next to them.
