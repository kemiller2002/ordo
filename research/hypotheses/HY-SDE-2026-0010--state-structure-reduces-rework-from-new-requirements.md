---
id: HY-SDE-2026-0010
title: A state-structured domain requires less rework of existing code when a new requirement arrives
research_area: sde
status: proposed
type: hypothesis
disposition: untested
confidence: very-low
confidence_rationale: "Untested and, unlike its predecessors, registered with a concrete reason to expect either direction. The evidence base already contains a measurement pointing the other way (EV-SDE-2026-0010: the state-structured domain has more decision points, not fewer) and one pointing toward it (EV-SDE-2026-0008). Neither is about rework."
source_experiment: EX-SDE-2026-0003
evidence_for: []
evidence_against: []
related_evidence: [EV-SDE-2026-0008, EV-SDE-2026-0010]
created: 2026-09-17
updated: 2026-09-17
tags: [rework, requirements-change, preregistered, time-entry, churn]
---

# HY-SDE-2026-0010

**Statement:** When a new or changed requirement arrives against an existing
implementation, a state-structured domain requires a smaller share of its work
to be spent modifying code that earlier requirements wrote than a
conventionally-structured domain does.

**Status: PROPOSED — registered before EX-SDE-2026-0003 runs**, so the
prediction is on record and cannot be adjusted to fit a result.

**Why this hypothesis and not the ones already on file.** Every prior
measurement in this programme has been of a *state*: decision points in a
finished tree, cost of a single frozen mission, discovery rate on one change.
None measured what happens when requirements arrive over time, which is the
condition the methodology is actually claimed to help with. The existing rework
figure for the state-structured arm — `M-TE-CSSTATE-REWORK` at 0% — is
explicitly the absence of a cause: no later requirement ever arrived during that
trial.

**Evidence for:** None.

**Evidence against:** None directly. But `EV-SDE-2026-0010` measured the
state-structured C# domain as having *more* decision points than the
conventional one (88 against 78), which is at least not the picture of a
simpler thing that absorbs change more cheaply.

**Why it is not simply "probably true":** an explicit state machine names its
cases, and a new case means touching every exhaustive match over that type. The
compiler will enumerate those sites — which `M-TE-CSSTATE-RIPPLE` and
`M-TE-FSSTATE-RIPPLE` both show it doing — but *enumerating* rework is not
*avoiding* it. A conventional implementation that reads a status string in three
places may need three edits where a state-structured one needs seventeen the
compiler found for it. The hardened HelixNote condition already showed a *lower*
as-experienced discovery rate while spending less on search
[TH-SDE-2026-0003], so the relationship between what the machine reports and
what the work costs is not monotonic in this evidence base.

**Secondary prediction, registered at the same time and separately falsifiable:**
requirements marked **Changed** in the wave-2 document force more rework than
those marked **New**, in both conditions. The document contains two Changed and
seven New requirements. If this does not hold, the measure is probably not
capturing what the word rework means.

**Unknowns:** Direction, magnitude, and whether any effect survives outside one
domain, one requirement sequence and one model family.

**Disposition:** UNTESTED. To be decided only by EX-SDE-2026-0003 under its
pre-registered falsification criteria, including the criterion that a null or
reversed result is a reportable outcome rather than a failed run.

**Implications if supported:** the first measured support in this evidence base
for the claim the methodology is usually sold on. It would still be scoped to
one domain and one requirement sequence.

**Implications if not supported:** three consecutive measurements — cost,
decision points, and now rework — would point away from the structural claims,
and the programme's defensible claim narrows to mechanism-of-discovery: which
mechanism catches a mistake, not how much the work costs.
