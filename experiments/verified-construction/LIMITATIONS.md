# Limitations (VC-002 verified-construction slice)

This experiment is pending VC-001, which has not been run. Nothing here shows that agents behave differently because
of this construction.

## What the evidence does and does not show

- **Compile-negative tests show that a value cannot be constructed. They do not show that an unauthorized decision is
  prevented.** They prove that ordinary code *outside* the defining module or assembly cannot write a
  `ValidatedPayment`, `PaymentLimit` or capability. They do not prove that nobody can change the slice. Anyone who can
  edit `Payment.fs` can change the limit check. Anyone who can edit the translation table can change the state model.
  Anyone who can edit `AssemblyInfo.fs` can widen `InternalsVisibleTo`. The defence against those edits is review,
  plus Dokimos's independent audit of the trace against the source. The compiler is not that defence.
- **F# `private` is module scope, and `internal` is assembly scope.** Code inside `Invoice.Slice` could construct a
  `PaymentsServiceCapability`. A test scans the slice's source with a heuristic regex to check that none does. That
  scan is weaker than a compiler check.
- **Who may call the payments service is not enforced.** `CompositionRoot.recordAndPersist` is public. The capability
  ensures only that writes go through the payments service's code path. Whether a given caller *is* the payments
  service role is a deployment and organizational fact, not a type-level one.
- **The host's storage function is not constrained.** A host that wraps its own append function with
  `Persistence.store` can still call that function, or its database, directly. The port constrains calls *through*
  the port.
- **Legality per state is enforced at runtime, not at compile time.** States carry no data and commands are a plain
  union, so the compiler does not stop code from issuing Void on a PartiallyPaid invoice. The generated `decide`
  refuses it at runtime, and it reports a deferral rather than a refusal. A typestate encoding, with one type per
  state, would make some of these compile-time errors. It was not attempted, because Ordo does not generate one.
- **The trace is only as good as its inputs.** Generated-code provenance comes from the validated plan, so it is
  mechanical. Handwritten provenance comes from a hand-maintained table, `Handwritten.fs`. Tests check that table
  against the `[<Authority>]` markers in source and against the attributes in the built DLLs. Those checks make the
  table *consistent*. They do not make it *true*: a marker can cite a plausible ID for code that does something else.
  Judging that is Dokimos's job.
- **Q-FIN-001 is not lexically reachable from the generated `undecided` function.** Dokimos reports this as an
  uncertain (non-blocking) finding on `InvoiceTransition.undecided:PartiallyPaid/Void`. The generated tier cannot
  reach the handwritten mapping: tiers depend only downward. Ordo's emitter also never renders a question's text or
  ID into generated code, by design, and the generated `refusalCode` maps `Undecided` to `None`. The Q-FIN-001
  deferral is produced in handwritten code: `Invoicing.ofGenerated` turns the undecided pair into
  `DeferredPendingQuestion` through `Invoicing.pendingQuestion`, which names `Authority.qFin001` and the
  `PolicyUnresolvedQFIN001` code. Both are traced, and `pendingQuestion` is traced as `deferred-pending-question`.
  Closing this lexically would need an Ordo vocabulary change, a code on an undecided pair, which was out of scope.
- **The R2 reuse closure is partial.** It is checked only for the reuse edges declared in `Handwritten.reuses`, and
  only for requirement, decision and question IDs. `issue` and `recordPayment` also call `ofGenerated`, but no edge
  is declared for them, because its Q-FIN-001 branch is unreachable for those commands. Invariant IDs are not part of the closure. Reuse of generated code
  is recorded by giving the generated `decide` the authority of every transition and refusal. Each handwritten
  command function cites only the rules it implements, not everything `decide` carries.

## Inputs

- **The handoff is hand-authored.** No Praxis exporter exists yet (CURRENT-STATE.md B1). The decisions marked
  "accepted by finance-controller (human)" are fixture stand-ins, not approvals.
- **The translation from prose to a state model is an agent-authored table** (`Translate.plan`), specific to this
  slice. The handoff carries requirements as sentences, not as states and transitions. Each row cites handoff IDs,
  and translation refuses when that authority is missing, not accepted, or superseded. The table itself is still
  the agent's reading of the requirements. The following readings are interpretations the owner should confirm:
  - **Unlisted pairs are refused.** The plan treats REQ-INV-001's lifecycle sentence as the complete list of legal
    commands. On that reading, paying a Draft and issuing an Issued invoice are both refused, with
    `COMMAND_NOT_ALLOWED_IN_STATE`. REQ-INV-001 does not say this explicitly. Ordo classifies the rule as *required*
    by REQ-INV-001, because that is what the plan cites.
  - **PartiallyPaid is a state.** No requirement names it. It is derived from "a payment may be partial"
    (REQ-INV-002) together with the lifecycle.
  - **"An issued invoice with no payment"** is modelled as the Issued state. Any payment moves the invoice to
    PartiallyPaid or Paid.
  - **The refusal code names are presentation choices by the agent**, except `PAYMENT_LIMIT_EXCEEDED`, which
    DEC-FIN-001 names. So are the order of checks (state legality, then positive amount, currency, limit, balance)
    and the probe's `UNCODED_REFUSAL` string. That string is unreachable for this plan. These choices were not
    raised as questions, consistent with R-ADM-3: presentation is not policy.
- **Gaps the handoff leaves open** were not decided and not raised as new questions. The slice applies no rounding
  or scale rule to amounts, and no list of allowed currencies; only the three-letter shape is checked. The probe can
  reach only USD.
- **Requirement source digests are whole-file digests** of the source documents, as the handoff gives them. They are
  not digests of the individual requirement sections. ECIR's manifest therefore pins a file, not a paragraph.

## Vocabulary gaps (Ordo construction vocabulary 4)

Ordo core was not modified. Each gap below was filled with experiment-local code.

| Gap | Effect here |
|---|---|
| States and commands carry no data | Amounts, balances, currency, the limit and the applied payments are handwritten (`Money`, `Payment`, `Invoicing`). The data invariants INV-INV-001..003 hold only through runtime checks in the validating constructors. |
| Refusal codes attach only to a refused pair, the unlisted pairs, or a guard | `PAYMENT_LIMIT_EXCEEDED`, `EXCEEDS_OUTSTANDING_BALANCE`, `NON_POSITIVE_AMOUNT` and `CURRENCY_MISMATCH` are *defined* in the plan, so they appear in the generated union and in the trace. Handwritten code *raises* them; the plan assigns them to nothing. |
| Branching transitions take no guards, and the selector is `unit -> Branch` | The data-dependent refusals cannot sit on the RecordPayment transition. They run before `decide`. The selector closes over the balance after the payment (`settledWhen`). |
| An undecided pair cannot carry a code | The mapping from `Undecided(PartiallyPaid, Void)` to `POLICY_UNRESOLVED_Q_FIN_001` and the question ID is handwritten (`Invoicing.pendingQuestion`). |
| No operation carries a decision parameter, such as a limit amount or a role | `AuthorityEmit` writes `Invoice.Authority.g.fs` from the admitted handoff. This is an experiment emitter, not Ordo's. |
| No capability-as-type or persistence construct | `bindCapability` is a semantic capability that the host checks. A `transitionEffect` port would need an `onFailure` disposition, and no decision gives one. Inventing one would have been an agent decision. REQ-INV-003 is therefore its own ECIR cohort, `COH-PAYMENT-PERSISTENCE`, which Ordo never prepares or validates. The boundary is entirely handwritten. |
| ECIR has no policy-question node kind | Q-FIN-001 is a `deferral` node linked to REQ-INV-001. That puts it in scope and makes it citable, and it authorizes nothing. A `conflict` node would have blocked the cohort. |

## Determinism and environment

- Byte-identical construction output is tested by running construction twice in-process and comparing the results
  with the committed files. `run.sh` repeats the check with the CLI, on one Linux machine. Construction has not been
  compared across operating systems.
- A clean rebuild of `Invoice.Slice.dll` gave the same sha256 once (`Deterministic`, `DebugType none`), checked by
  hand on one machine, on the same path, with SDK 10.0.402. It is not tested in CI.
- Only the .NET 10 runtime was available. Ordo's own `Ordo.Construction.Tests` target net8.0. They were run here with
  `DOTNET_ROLL_FORWARD=Major`, and 116 of 116 passed. They were not run on net8.0.
- The trace-schema test needs python3 with `jsonschema`. Without it the test fails rather than skips.

## Not done here

- Dokimos's audit of OBS-LEGACY-001/002 as owner questions, the invariant checks INV-INV-001..004 within a bound,
  and the seeded limit-dropping mutant (frozen outcomes 4 and 5) belong to Dokimos.
- Only one handoff was constructed. No second domain was tried, so the translation table's generality is untested.
- No agent was involved in consuming this construction. VC-001 is the experiment that would measure that.
