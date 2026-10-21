# VAC-ORDO-001: "Verified" decisions must name a covering independent check; agent-written decisions enter as proposed

Priority: P2
Owner system: Ordo (decision records and contracts); Praxis validation consumes it
Intake: Praxis document ingestion. Source material, not a canonical requirement.
Existing work items: extends REL-ORDO-001 / ORD-REL-002 ("each referenced decision ... satisfies required
approval/resolution rules"). ORD-REL-002 governs decisions referenced by `ecir/1` contracts. This input applies the
same rule to repository decision records that implementing agents write during their work, and adds coverage
links. It does not duplicate either.

Experimental finding: kemiller2002/cohort-based-engineering, branch `claude/verified-agent-continuity-vfjrrx`:
- `research/verified-agent-continuity/hallucination-evaluation.md` §3–4
- `research/verified-agent-continuity/verified-continuity-design.md` §4
- `research/experiments/VAC-001/results/layer3/`

## Findings

1. **Unsupported decisions were the scope-addition type.**
   - Of 202 decisions adjudicated blind (two reviewers, κ = 0.905, plus a third adjudicator), 8 were
     `unsupported-requirement`: behaviour or interfaces no requirement asks for, presented as settled. Examples:
     unrequested `Store` operations, and `withdraw` made to respect holds.
   - The code was correct and the hidden oracle passed. No deterministic check (oracle, rule check, evidence
     existence, or Ordo contract admission) detected them.
2. **Self-accepted decision records.** In the Ordo-controlled arms, agents wrote their own records with status
   `accepted` (for example DEC-V1-STORE-CLOSE and DEC-V3-STORE-HOLDS) to justify those additions. The adjudicator
   rejected them as backing, because they were written by the unit that made the change. A later reader sees an
   accepted decision that nobody authorized.
3. **"Verified" was asserted, not computed.** The prototype promoted a decision to `verified` when its cited
   evidence existed and its unit passed. An out-of-scope decision (R1/D U7-D3) was therefore labelled `verified`,
   although no requirement or check covers it.

## Requirements (proposed)

- **VAC-ORDO-001-R1.** A decision record carries `coveredBy`: the independent checks that exercise it, such as
  acceptance or oracle case IDs, rule IDs, or accepted decision IDs.
- **VAC-ORDO-001-R2.** Decision status `verified` (or an equivalent) is computed by validation: it requires a
  `coveredBy` check with a recorded passing result at the decision's revision. Without one, the status is
  `unchecked`, never `verified`.
- **VAC-ORDO-001-R3.** A decision record written by the actor implementing the affected requirement enters as
  `proposed`. Only a different authorizing actor, or a rule the repository configures, can move it to `accepted`.
  Validation reports an `accepted` record whose only provenance is the implementing actor.
- **VAC-ORDO-001-R4.** Contract admission (`ordo ecir check`) and `praxis validate` report decisions that add
  behaviour outside every referenced requirement key as `scope-extension`, a warning. This applies only where the
  decision record declares the requirement keys it serves.

## Acceptance evidence

- Fixtures:
  - a self-accepted record is reported;
  - an uncovered decision computes as `unchecked`;
  - a covered decision with a passing check computes as `verified`;
  - a decision with no requirement key is reported as `scope-extension`.
- No change to Tier 1/2 purity (SDE-DOCTRINE-003 guardrail).
- Canonical work items created by Praxis with provenance linking to this input. Do not use GitHub Issues.
