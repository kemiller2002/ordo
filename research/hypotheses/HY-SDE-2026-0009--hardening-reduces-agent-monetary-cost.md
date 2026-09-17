---
id: HY-SDE-2026-0009
title: Mechanical boundary hardening reduces an agent's monetary cost to implement an identical mission
status: proposed
type: hypothesis
disposition: untested
confidence: very-low
confidence_rationale: "Untested under its own criteria, and now with measured observation pointing away from it. Eleven runs across two experiments never reached the pre-registered sample; the sensitivity analysis over all verified runs shows separation ($2.61) well inside within-condition spread ($9.78 and $15.15) and an inconsistent direction. The lowest available label remains correct, for a changed reason."
source_experiment: EX-SDE-2026-0001
evidence_for: []
evidence_against: []
related_evidence: [EV-SDE-2026-0007, EV-SDE-2026-0009]
created: 2026-09-17
updated: 2026-09-17
tags: [agent-cost, tokens, experiment-3, open-question, preregistered, attempted-inconclusive, direction-reversed]
---

# HY-SDE-2026-0009

**Statement:** An engineering agent implementing an identical, frozen
mission incurs lower monetary cost working against a mechanically hardened
boundary architecture than against the unhardened baseline architecture.

**Status: OPEN — attempted twice, still untested, direction no longer consistent.** This hypothesis is registered *before*
its experiment (EX-SDE-2026-0001) runs, so that the prediction is on record
and cannot be adjusted to fit a result.

**Attempted twice, stopped short twice, and the direction has now broken.**

EX-SDE-2026-0001 ran six trials and did not reach its threshold
[EV-SDE-2026-0007]. EX-SDE-2026-0002 continued it under a corrected criterion
and reached n=3 baseline against n=2 hardened, where four per condition were
fixed in advance [EV-SDE-2026-0009]. A seven-day rate-limit ceiling then made
the remaining runs unreachable, and the stopping rule was not amended to fit
it.

The sensitivity analysis over all eleven complete, independently verified runs
— required in advance, because the void criterion preferentially removes
expensive runs — gives baseline $20.05 and hardened $17.43, a separation of
**$2.61** against within-condition spreads of **$9.78 and $15.15**, with the
ranges overlapping across nearly their whole extent.

**Wave 6 reversed.** A6 baseline $25.70, B6 hardened $27.15 — the hardened run
cost more, the first reversal in eleven runs. Both were voided on the epoch
criterion **at 13:44Z, while still executing and before either figure was
read**, which is the only reason the void is not indistinguishable from
discarding the inconvenient pair.

Ten of eleven runs placed baseline above hardened; the eleventh did not.
**This is still not recorded as evidence for.** A direction from an
underpowered set is what Experiment 3 produced and had to withdraw, and this
set is underpowered *and* no longer consistent.

**Evidence for:** None. Note specifically that Experiment 3's apparent
figures — 59% fewer tool uses, 68% fewer tokens, 59% less wall clock — are
**not** evidence for this hypothesis and must not be recorded as such.
Condition B's telemetry covers a resumed partial run beginning after roughly
78% of its eventual log already existed [EV-HN-2026-0005;
`doctrine/CONTRADICTIONS-AND-DEPRECATED.md`]. The true total could match or
exceed Condition A's.

**Evidence against:** The sensitivity analysis in EV-SDE-2026-0009, which is
the first measured observation pointing away from this hypothesis rather than
toward it. It is recorded here as such, and *not* as a contradiction: it comes
from a set that includes runs voided by a pre-registered criterion, and a null
that fails to separate two conditions is weaker evidence than a reversal that
does.

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

**Disposition:** UNTESTED under its own criteria, which were never reached by
either experiment. The pre-registered test is not merely unfinished but now
unreachable at this cost per run. What exists instead is a sensitivity analysis
that does not separate the conditions and a final pair that reversed — enough
to say the effect is **not demonstrated**, not enough to say it is absent.

A successor needs a cheaper unit of measurement and an interruption criterion
that does not correlate with cost. Until one exists, this hypothesis should be
treated as an open question with the balance of available observation no longer
favouring it.

**Implications if supported:** SDE may claim a cost effect *within the
scope measured*, with the run-level spread published alongside. No effect
size generalises from three runs on one codebase.

**Implications if not supported:** The open question on the Ordo site's
research page gains a measured answer instead of an absence, and the
disqualified Experiment 3 percentages stay permanently disqualified with a
measured result standing next to them.
