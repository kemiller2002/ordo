# VAC-002-P2-03: Computed decision authorization: design input, not yet a build request

Priority: P2
Owner system: Ordo
Intake: Praxis document ingestion. Source material, not a canonical requirement.
Experimental finding: `kemiller2002/cohort-based-engineering` → `research/experiments/VAC-002/P2/`
(`DECISION-AUTHORITY.md`, `PILOT-RESULTS.md`, `RECOMMENDATIONS.md` R-ORDO-1..4).

## Finding

VAC-002-P2 built an isolated, experimental checker. It computes a business-rule decision's authorization from the
status and provenance of the sources the decision cites:

- requirements;
- accepted decision records;
- granted approvals;
- standing policy.

It maps onto existing Ordo structures:

| Checker rule | Existing Ordo structure |
|---|---|
| Who may act (rule A5) | `Authorization.HumanRequired` |
| Approved decisions | `ConstructionDecisions.ApprovedDecisionRecords` |
| Supersession | `ConstructionIr.Disposition.Superseded`, `InvalidSupersession` |

**What its 21 unit cases demonstrate.** It rejects all of the following:

- self-asserted status;
- self-citation;
- proposed and superseded authority;
- implementer-written records;
- exceptions backed only by a requirement (VAC-002's L04 error, had it been declared).

**What the pilot showed.** Over 24 executions it prevented nothing, because no agent proposed an unauthorized
decision. Its v0.1 rules also caused false rejections (VAC-002-P2-02).

**Blind spots, by construction:**

- approval scope;
- the agent's choice of decision type;
- undeclared decisions.

## Requirements (proposed; additional experiment required before any implementation)

- **VAC-002-P2-03-R1.** Do not add computed decision authorization to Ordo until a P3 experiment (VAC-002-P2-01) shows
  that it prevents errors that guidance alone does not.
- **VAC-002-P2-03-R2.** If built, adopt contract v0.2's constraints (VAC-002-P2-02):
  - status is computed, never agent-written;
  - evidence and verification are separate;
  - same-owner revisions are not overrides;
  - authorship is judged per section;
  - rejections name the behaviour.
- **VAC-002-P2-03-R3.** Defer machine-readable approval scope (F7), decision-record supersession and
  decision-to-verification traceability. P2 found no use for them: 0 events.

## Evidence class

Mechanism properties: **Demonstrated** (unit cases). Prevention benefit: **Inconclusive** (no events). Defects of
v0.1: **Demonstrated**.
