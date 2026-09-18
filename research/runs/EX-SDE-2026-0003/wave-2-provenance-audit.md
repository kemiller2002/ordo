# Wave-2 provenance audit

Taken before any run, to test the limitation EX-SDE-2026-0003 recorded against
itself: that wave 2, being reverse-engineered from the F# implementation, might
follow F#-shaped seams and so favour the state-structured condition.

Source measured: `requirements-reconstruction/fsharp-business-requirements-beyond-csharp.md`
in `kemiller2002/time-entry-state-machine`.

## 1. Can wave 2 be re-derived from `prompts/` instead? No.

`prompts/` is the original specification set both implementations were built
against, so it would be uncontaminated by either. It does not contain the
wave-2 scope.

| Wave-2 requirement | Mentions in `prompts/` |
|---|---|
| Recording granularity (exact-minute) | **0** |
| Business purpose | **0** |
| Daily attestation | **0** |
| Recording time with a timer | **0** |

Apparent hits elsewhere are false positives: "merge" in `prompts/` means
state-model merges and git write reconciliation; "evidence" in
`12-research-experiment-plan.md` means research evidence. The only place three
wave-2 concepts appear together is a bare bullet list of data-layer
responsibilities in `business_activity_ledger_data_execution_contract.md`
("Void/restore", "Split/merge", "Evidence metadata") carrying no rules,
preconditions or acceptance criteria.

The three documents `prompts/00-time-entry-specification-index.md` names as
**authoritative** for business requirements — `time-entry-system-ai-requirements.txt`,
`time-entry-state-model-v0.1.txt`, `time-entry-large-view-architecture-v0.1.txt` —
**do not exist in the repository.**

`prompts/` is also the wrong kind of document: it is mostly architecture and
contract specification (WASM boundary, service layer, dependency direction,
GitHub persistence). Using it as a requirements source would prescribe
*structure* to both conditions and destroy the independent variable rather than
clean it up.

## 2. Does wave 2 leak F# structure? No — measured, not assumed.

Structural vocabulary in the wave-2 document:

| Term | Occurrences |
|---|---|
| discriminated union | 0 |
| module | 0 |
| Option | 0 |
| class | 0 |
| interface | 0 |
| nullable | 0 |
| enum | 0 |
| record | 1 — *"both remain on record"*, ordinary English |
| result | 1 — *"supplied for the result"*, ordinary English |
| type | 1 — *"a type (a URL, a LinkedIn…)"*, kind of evidence |

**Zero F#-shape leakage.** The document is written in domain language
throughout: *"A time entry may start at any minute (e.g. 9:03) and last any
positive whole number of minutes"*. The limitation the experiment recorded
against itself is **not supported by measurement**, and is downgraded
accordingly.

## 3. A different and narrower asymmetry, which does exist

The document is written **differentially against the C# baseline** — six
explicit references, in three of the nine requirements, including both
requirements marked **Changed** (the two that force the most rework).

Classified one by one:

| Line | Reference | Kind | Accurate for condition B? |
|---|---|---|---|
| 1 | *"beyond the C# baseline"* | framing | n/a |
| 13 | *"what actually changed from the C# rule"* | framing | n/a |
| 18 | *"C# required both start time and duration to sit on the six-minute grid"* | **wave-1 rule** | **yes** |
| 37 | *"C#'s label check is unconditional"* | **wave-1 rule** | **yes** |
| 122 | *"Beyond the C# capability set (Correct/Split/Void for Recorded entries…)"* | **wave-1 rule** | **yes** |
| 46 | *"This also fixes the C# implementation's own inconsistency between its rejection message and its actual unconditional behavior"* | **implementation quirk** | **UNKNOWN** |

Five of six are statements about **wave-1 rules**. Condition B's baseline is
built from the same wave-1 document, so it implements the same rules and those
references describe it correctly by construction.

**Line 46 is the exception and the only real defect.** It asks the agent to fix
an inconsistency between a rejection message and actual behaviour — a property
of the *C# implementation*, not of the wave-1 rules. Condition B's baseline,
built fresh, may not reproduce it.

**Direction: this favours condition B**, the state-structured one, because
condition A has strictly more to do on requirement 2 if the inconsistency is
present in its baseline and absent from B's. That is the same direction as the
limitation this audit refuted, arrived at by a different route and on a much
narrower surface — one clause of one requirement out of nine.

## Precondition added to the experiment

Before any wave-2 run, and recorded in the run manifest:

> Check whether condition B's baseline reproduces the rejection-message /
> actual-behaviour inconsistency that wave-2 requirement 2 asks to fix. If it
> does, there is no asymmetry. If it does not, requirement 2's rework figures
> are reported with that clause's contribution identified separately for
> condition A.

This is a check on a built artifact, not a rewrite of a requirement. Nothing in
the wave-2 document is edited: it stays exactly as it was authored, before this
experiment was conceived.
