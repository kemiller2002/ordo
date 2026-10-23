# Verified construction: invoice slice (VC-002)

**Status: EXPERIMENTAL.** It stays experimental until VC-001 has run; VC-001 is the experiment that would test
whether this construction changes what agents do, and it has not been run. This directory is not part of any Ordo
solution, release, or CI workflow.

## Purpose

This is the Ordo part of the bounded "verified construction" slice. It takes an authority handoff
(`echelon.authority-handoff/0.1`, Praxis-owned) for a synthetic invoice workflow and produces three things:

1. typed F# that compiles, in which invalid values and an unauthorized persistence path cannot be written from
   outside the slice's assemblies;
2. Ordo's own generated state machine for the workflow, where an unresolved policy question is a recorded
   deferral, not a rule;
3. a deterministic construction trace (`echelon.construction-trace/0.1`, Ordo-owned) that links every constructed
   symbol to the requirement, decision and question IDs it was built from. Dokimos audits that trace
   independently.

Source requirements: VC-002 (Ordo intake, `.praxis/inbox/documents/2026-10-23-VC-002-provenance-backed-typed-construction.md`
on ordo main via PR #104) and VAC-002-P3-03-R2 (the trace also covers rules a change reuses). The input fixture is
`fixture/invoice-slice/`. It is copied byte for byte from `cohort-based-engineering/program/contracts`
(fixture-v1, handoff sha256 `ec5d0e6a…bcd`), and a test pins that digest.

## How to run

```sh
./run.sh
```

`run.sh` does the following, in order:

1. checks the fixture digest;
2. builds `construct`;
3. runs it to write `src/Invoice.Slice/generated/` and `out/`;
4. runs `construct` twice more into scratch directories, and fails unless both runs are byte-identical to each
   other and to the working tree;
5. builds the slice;
6. runs the tests.

It needs the .NET 10 SDK and python3 with `jsonschema`. The python package is used only by the trace-schema test.

You can also run the steps by hand:

```sh
dotnet build src/Echelon.Construct/Echelon.Construct.fsproj -c Release
dotnet src/Echelon.Construct/bin/Release/net10.0/construct.dll fixture/invoice-slice/handoff.json .
dotnet test tests/VerifiedConstruction.Tests/VerifiedConstruction.Tests.fsproj -c Release
```

`construct <handoff> <root>` exits with one of these codes:

| Code | Meaning |
|---|---|
| 0 | output written |
| 2 | usage error |
| 3 | refused; the typed errors are printed |

Construction is refused in any of these cases:

- the handoff fails admission;
- a cited source document is missing or its digest does not match;
- the authority the slice needs is absent or not accepted;
- Ordo rejects the plan;
- a trace row cites an ID that is not current authority.

## Pipeline

```text
handoff.json ──read (strict)──► Handoff ──admit──► AdmittedHandoff
     │                                                │
     │ source digests verified                        ├─► ECIR manifest + blueprint ─► Ordo prepareCohort
     │                                                ├─► vocabulary-4 PlanDraft ───► Ordo validate ─► ValidatedPlan
     │                                                │                                 │
     │                                                │            Ordo.Construction.construct (unchanged engine)
     │                                                │                                 ▼
     │                                                │      generated/Invoice/Invoice.{Model,Transition}.g.fs
     │                                                ├─► AuthorityEmit ─► generated/Invoice.Authority.g.fs
     │                                                └─► Trace (generated constructs + Handwritten table)
     ▼                                                                   ▼
out/construction-trace.json, out/plan.v4.json, out/classification.json, out/ecir/*.json
```

### Reused Ordo code and handwritten code

| Part | Source |
|---|---|
| ECIR manifest digest, blueprint validation, cohort preparation | **Ordo.Core**, unchanged (`ConstructionIr`, `ConstructionIrWire`) |
| Plan validation (vocabulary 4), canonical JSON and digest | **Ordo.Core**, unchanged (`ConstructionPlan`) |
| Choice classification (`complete` refuses because of Q-FIN-001) | **Ordo.Core**, unchanged (`ConstructionClassification`) |
| States, commands, refusal codes, branching unions, transition table, `decide`, `pairCode` | **Ordo.Construction** emitter, unchanged: `generated/Invoice/*.g.fs` |
| Compile-negative harness | **Ordo** `tests/Ordo.Tests/FsiProbe.fs`, linked unchanged |
| JSON parse and render | **Ordo.Core** `Json` |
| Handoff reader, admission, translation table, trace | experiment code: `src/Echelon.Construct/` |
| Decision-parameter constants | experiment extension emitter: `generated/Invoice.Authority.g.fs` |
| Money, payment validation, limit, AppliedPayment, invoice data, selectors, persistence port, capability, probe | handwritten extension code: `src/Invoice.Slice*/` |

No file outside `experiments/verified-construction/` was changed.

## The constructed model

### Generated by Ordo

**States:** Draft (initial), Issued, PartiallyPaid, Paid (terminal), Voided (terminal).

**Commands:** Issue, RecordPayment, Void.

**Transitions:**

| From | Command | To |
|---|---|---|
| Draft | Issue | Issued |
| Draft | Void | Voided |
| Issued | Void | Voided |
| Issued | RecordPayment | PartiallyPaid or Paid (branching) |
| PartiallyPaid | RecordPayment | PartiallyPaid or Paid (branching) |

The handwritten selector `Invoicing.settledWhen` implements the two branching transitions. It picks Paid exactly
when the balance after the payment is zero (REQ-INV-002). These are Ordo's `IMPLEMENT:` obligations for the slice.

**Undecided pair:** Void from PartiallyPaid. The plan declares it with `declareUndecidedTransition`, citing the
`Q-FIN-001` node. Generated `decide` returns `Undecided`, never `InvalidTransition`. Ordo emits the obligation
`RESOLVE-PAIR:PartiallyPaid/Void`, and `ConstructionClassification.complete` refuses the plan with exactly one open
choice, `pair:PartiallyPaid/Void`.

**Unlisted pairs** are refused under a pair rule that cites REQ-INV-001. They get these codes:

| Pairs | Code |
|---|---|
| any command in a terminal state | `INVOICE_CLOSED` |
| any other unlisted pair | `COMMAND_NOT_ALLOWED_IN_STATE` |

**Refusal codes:**

| Code | Rests on |
|---|---|
| `PAYMENT_LIMIT_EXCEEDED` | DEC-FIN-001; the wire string is the decision's `refusalCode` parameter |
| `POLICY_UNRESOLVED_Q_FIN_001` | Q-FIN-001 |
| `EXCEEDS_OUTSTANDING_BALANCE` | REQ-INV-002 |
| `NON_POSITIVE_AMOUNT` | REQ-INV-002 |
| `CURRENCY_MISMATCH` | REQ-INV-002 |
| `INVOICE_CLOSED` | REQ-INV-001 |
| `COMMAND_NOT_ALLOWED_IN_STATE` | REQ-INV-001 |

### Handwritten values

These live in `src/Invoice.Slice/`. Each rule-bearing symbol carries `[<Authority("ID", ...)>]` on its own line.

| Value | Rule |
|---|---|
| `Money.PositiveAmount` | private; built only by `positiveAmount` |
| `Payment.PaymentLimit` | private. Its only value is `Payment.decFin001`, built from the DEC-FIN-001 parameter. `exceedsLimit : PaymentLimit -> PositiveAmount -> bool` takes no customer. |
| `Payment.ValidatedPayment` | private; built only by `Payment.validate`, which checks positive amount, currency and limit |
| `Invoicing.AppliedPayment` | private; built only by `Invoicing.recordPayment`, which checks legality in the generated table and the outstanding balance |
| `Invoicing.Invoice` | private; holds the generated `InvoiceState` plus currency, total and applied payments |
| `Invoicing.Refusal` | `Refused code`, or `DeferredPendingQuestion(code, questionId)` |

### Persistence boundary (DEC-FIN-002, REQ-INV-003)

`Persistence.persist : PaymentsServiceCapability -> PaymentStore -> AppliedPayment -> Result<unit, PersistError>`.

- `PaymentsServiceCapability` has an `internal` case. `InternalsVisibleTo` names only `Invoice.Slice.PaymentsService`.
- `PaymentsService.CompositionRoot` mints the capability privately, with the DEC-FIN-002 `role` parameter, and
  never returns it.
- Callers get `recordAndPersist`, which validates, applies and persists in one call.
- `PaymentStore`'s append field is private, so it can be reached only through `persist`.

### Probe facade (for Dokimos)

`Invoice.Slice.dll` is built by `src/Invoice.Slice/Invoice.Slice.fsproj` for net10.0. The probe API:

```fsharp
module Invoice.Slice.Probe
type ProbeCommand = Issue | Pay of customerId: string * amount: decimal | Void
type ProbeState                                            // opaque
val start           : total: decimal -> ProbeState         // currency fixed "USD"; ArgumentException if total <= 0
val step            : ProbeState -> ProbeCommand -> Result<ProbeState, string>   // Error = refusal code string
val stateName       : ProbeState -> string                 // "Draft"|"Issued"|"PartiallyPaid"|"Paid"|"Voided"
val outstanding     : ProbeState -> decimal
val paymentsApplied : ProbeState -> decimal list           // oldest first
```

### Trace conventions

Symbols are fully qualified F# names. Three exceptions use a pseudo-symbol, because the construct is an entry in a
generated table rather than a symbol of its own:

- `Generated.Invoice.InvoiceTransition.legal:From/Command`;
- `pairCode:From/Command`, `pairCode:unlisted` and `pairRule:unlisted`;
- `undecided:From/Command`.

Dispositions:

| Disposition | Meaning |
|---|---|
| `deferred-pending-question` | the construct rests on a policy question. Here, four constructs: the undecided pair, its code, the Q-FIN-001 constant and `Invoicing.pendingQuestion`. |
| `authorized` | built from accepted requirements and decisions. Every other construct here. |
| `technical` | defined by the schema; no construct in this slice uses it. |

`excluded` lists OBS-LEGACY-001 (the STR- exemption) and OBS-LEGACY-002 (the 25,000.00 limit). Both are routed to
`finance-controller`, the owner of DEC-FIN-001. The legacy file is not modified.

## VC-002 requirements: evidence

| # | Requirement | Evidence | Status |
|---|---|---|---|
| 1 | Accept a versioned IR with requirement and decision IDs, revisions, scope, questions and supersession. Every consequential rule carries provenance or is marked unverified. | `Handoff.read` (strict) and `Admission.admit`. Source digests are re-verified (`Translate.verifySources`). Observations stay `unverified` and are excluded. Tests: `AdmissionTests`, `FixturePinTests`. | implemented and tested locally |
| 2 | Never infer authority from code, tests, artifacts or the agent's own proposal. Unverified rules surface as questions. | An accepted decision needs a human `authorizedBy`, or admission refuses it. Observations claiming authority are refused. A trace row citing a superseded record or an observation makes `construct` refuse. Legacy rules are excluded, routed to their owner, and absent from source (`TraceTests`). | implemented and tested locally |
| 3 | F# unions, single-case types, validated constructors and transitions for a bounded financial slice. Encode only established invariants; separate policy ambiguity from invalid states. | Generated unions and transition table, plus the private value types above. Q-FIN-001 is an *undecided* pair, distinct from a refusal (`TranslationTests`). | implemented and tested locally |
| 4 | Prevent invalid transitions and cross-boundary operations through compiler-enforced interfaces where feasible; document what is runtime-only. | `CompileNegativeTests`: 13 failing probes plus 4 positive controls, run against the built DLLs. The table below lists what is compiler-enforced and what is runtime-only. | implemented and tested locally |
| 5 | Refusal, deferral and recorded questions are valid outcomes. No R-ADM-1..3. Experimental gates off the default path. | Refuse-and-record and defer-scope questions are admitted. So are agent-raised questions and proposed presentation decisions (`AdmissionTests` R-ADM-1..3). Void from PartiallyPaid returns `DeferredPendingQuestion(_, "Q-FIN-001")`. Nothing here is in Ordo's default build. | implemented and tested locally |
| 6 | Traceability from generated types, constructors and transitions to IDs and revisions. Deterministic output, reproducible builds. | `out/construction-trace.json` validates against the schema (python jsonschema). Generated code carries the plan digest and handoff digest. The ECIR manifest carries source revisions and digests. The marker, reflection and trace rows correspond one to one, and the R2 reuse closure is checked (`TraceTests`). Byte-identical reruns are checked (`DeterminismTests`, `run.sh`). A clean rebuild of `Invoice.Slice.dll` produced the same sha256 (checked by hand once, on one machine). | implemented and tested locally; DLL reproducibility checked once by hand |
| 7 | Minimal dependencies; no external services. | The slice depends on FSharp.Core only. `construct` adds Ordo.Core and Ordo.Construction. The tests use xunit. | met |

### Compiler-enforced or runtime-only

| Property | Enforced by |
|---|---|
| A ValidatedPayment, AppliedPayment, PositiveAmount, PaymentLimit or Invoice cannot be built except through its function | compiler (FS1093), for code outside the defining module. Inside the module it is review only. |
| A different limit cannot be supplied, and the limit cannot depend on a customer | compiler (FS0001), outside the assembly |
| A payment cannot be persisted without the capability, or as a raw amount or an unapplied payment | compiler (FS0001) |
| The capability cannot be minted outside Invoice.Slice and Invoice.Slice.PaymentsService; the composition root's capability is unreachable | compiler (FS1093, FS1094) |
| Code inside Invoice.Slice does not mint the capability | runtime source scan (heuristic regex), not the compiler |
| A missing selector or an unlisted branch target | compiler (Ordo's generated `InvoiceRules` record and branch unions) |
| A new refusal code without a wire string | compiler (exhaustive `wireCode`; FS0025 is an error) |
| Which commands are legal in which state | generated table, evaluated at runtime by `decide`. The compiler does not stop code from calling a command in a state that refuses it. |
| Amount > 0, currency match, amount ≤ 10,000.00, amount ≤ balance | runtime checks inside the validating constructors. The compiler forces callers to go through them. |
| Who may call `recordAndPersist`; what a host's store does | runtime and organizational; not enforced here |

## Frozen slice outcomes (fixture README)

| # | Outcome | Status |
|---|---|---|
| 1 | Refuses payments over 10,000.00 for every customer; no STR- exemption, no 25,000 limit | behaviour tests, a source-absence test, and compile probes on `exceedsLimit` |
| 2 | Void from PartiallyPaid is refused with the Q-FIN-001 code and the trace marks it `deferred-pending-question` | behaviour and trace tests |
| 3 | Persisting needs an applied payment and the payments-service capability; violating code does not compile | compile-negative tests |
| 4, 5 | Dokimos's outcomes | not run here |

## Mutation checks (measured)

Each mutant was applied by hand, the suite was run, and the change was reverted. The suite failed for all seven.

| Mutant | Change | Failing tests |
|---|---|---|
| M1 | limit check disabled | 4 |
| M2 | STR- exemption reintroduced in `validate` | 4, including the source-absence test |
| M3 | Q-FIN-001 deferral mapping dropped | 2 |
| M4 | translator adds Void PartiallyPaid → Voided | 15. Ordo refuses the plan with a conflicting pair disposition, so `construct` fails. |
| M5 | an agent accepted as authorizer | 1 |
| M6 | a refuse-and-record question rejected (the R-ADM-1 regression) | 25 |
| M7 | overpayment check removed | 1 |

The mutation script is not committed. Each mutant is a single string replacement, and the table above lists them.

See [LIMITATIONS.md](LIMITATIONS.md) for what this does not show.
