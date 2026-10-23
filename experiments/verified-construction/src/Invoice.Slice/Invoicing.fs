/// The invoice: the generated state machine (Generated.Invoice, emitted by
/// Ordo.Construction from the plan) plus the data its states cannot carry.
///
/// Which commands are legal in which state is decided only by the generated
/// `InvoiceTransition.decide`; this module never matches on a (state, command)
/// pair to allow or refuse it. It supplies the extension the generated code
/// requires (the RecordPayment selectors) and the data rules the vocabulary
/// cannot express (amounts, balances, the limit).
module Invoice.Slice.Invoicing

open Echelon.Provenance
open Generated.Invoice
open Invoice.Slice.Money
open Invoice.Slice.Payment

/// A validated payment applied to an invoice: it did not exceed the balance
/// outstanding before it (REQ-INV-002). Constructible only by `recordPayment`.
/// It is what persistence accepts (REQ-INV-003, DEC-FIN-002).
[<Authority("REQ-INV-002", "REQ-INV-003", "INV-INV-003")>]
type AppliedPayment =
    private
        { Payment: ValidatedPayment
          OutstandingBefore: decimal
          OutstandingAfter: decimal }

    member this.Validated = this.Payment
    member this.Amount = this.Payment.Value.Value
    member this.Before = this.OutstandingBefore
    member this.After = this.OutstandingAfter

/// An invoice. Its state is the generated InvoiceState; the payments applied
/// to it are kept newest first. Constructible only by `create` and the
/// command functions below.
[<Authority("REQ-INV-001", "REQ-INV-002", "INV-INV-001", "INV-INV-002")>]
type Invoice =
    private
        { State: InvoiceState
          Currency: Currency
          Total: PositiveAmount
          Applied: AppliedPayment list }

/// Why a command was refused. A deferral is a valid outcome, not an error:
/// it reports an unresolved policy question by its ID (VC-002 req. 5).
[<Authority("REQ-INV-001", "REQ-INV-002", "Q-FIN-001")>]
type Refusal =
    /// Refused by a requirement or decision, with the plan's typed code.
    | Refused of code: InvoiceRefusalCode
    /// Neither allowed nor forbidden: the command waits on an unresolved question.
    | DeferredPendingQuestion of code: InvoiceRefusalCode * questionId: string
    /// A refusal the plan gives no code. Unreachable for this plan (every
    /// refused pair has a code and the only open pair has a question); kept so
    /// that a future plan cannot be mapped to a code by default.
    | Uncoded of state: InvoiceState * command: InvoiceCommand

/// Wire strings of the generated refusal codes. Exhaustive: a new code in
/// the plan fails compilation here until it is given its string.
[<Authority("REQ-INV-001", "REQ-INV-002", "DEC-FIN-001", "Q-FIN-001")>]
let wireCode (code: InvoiceRefusalCode) : string =
    match code with
    | InvoiceRefusalCode.PaymentLimitExceeded -> Authority.decFin001RefusalCode
    | InvoiceRefusalCode.PolicyUnresolvedQFIN001 -> "POLICY_UNRESOLVED_Q_FIN_001"
    | InvoiceRefusalCode.ExceedsOutstandingBalance -> "EXCEEDS_OUTSTANDING_BALANCE"
    | InvoiceRefusalCode.NonPositiveAmount -> "NON_POSITIVE_AMOUNT"
    | InvoiceRefusalCode.CurrencyMismatch -> "CURRENCY_MISMATCH"
    | InvoiceRefusalCode.InvoiceClosed -> "INVOICE_CLOSED"
    | InvoiceRefusalCode.CommandNotAllowedInState -> "COMMAND_NOT_ALLOWED_IN_STATE"

/// Presents a refusal as its wire code. A presentation mapping: it cites the
/// requirements, decision and question whose refusals it presents.
[<Authority("REQ-INV-001", "REQ-INV-002", "DEC-FIN-001", "Q-FIN-001")>]
let refusalCode (refusal: Refusal) : string =
    match refusal with
    | Refused code -> wireCode code
    | DeferredPendingQuestion(code, _) -> wireCode code
    | Uncoded _ -> "UNCODED_REFUSAL"

let state (invoice: Invoice) = invoice.State
let currency (invoice: Invoice) = invoice.Currency
let total (invoice: Invoice) = invoice.Total.Value

/// Payments applied, oldest first.
let payments (invoice: Invoice) : AppliedPayment list = List.rev invoice.Applied

let outstanding (invoice: Invoice) : decimal =
    invoice.Total.Value - (invoice.Applied |> List.sumBy _.Amount)

/// An invoice is created as a draft (REQ-INV-001).
[<Authority("REQ-INV-001")>]
let create (currency: Currency) (total: PositiveAmount) : Invoice =
    { State = InvoiceState.Draft; Currency = currency; Total = total; Applied = [] }

/// The open pairs of the plan and the unresolved question each waits on.
[<Authority("Q-FIN-001")>]
let private pendingQuestion (s: InvoiceState) (c: InvoiceCommand) : (string * InvoiceRefusalCode) option =
    if s = InvoiceState.PartiallyPaid && c = InvoiceCommand.Void then Some(Authority.qFin001, InvoiceRefusalCode.PolicyUnresolvedQFIN001)
    else None

/// Maps the generated refusals: a refused pair to its plan code (REQ-INV-001),
/// the open pair to a deferral that names Q-FIN-001.
[<Authority("REQ-INV-001", "Q-FIN-001")>]
let private ofGenerated (refusal: InvoiceRefusal) : Refusal =
    match refusal with
    | InvoiceRefusal.Undecided(s, c) ->
        match pendingQuestion s c with
        | Some(questionId, code) -> DeferredPendingQuestion(code, questionId)
        | None -> Uncoded(s, c)
    | InvoiceRefusal.InvalidTransition(s, c) ->
        match InvoiceTransition.refusalCode refusal with
        | Some code -> Refused code
        | None -> Uncoded(s, c)

/// The extension Ordo requires for the RecordPayment branching transitions
/// (obligations IMPLEMENT:InvoiceRules.Select/Issued/RecordPayment and
/// .../PartiallyPaid/RecordPayment): the target is Paid exactly when the
/// balance after the command is zero (REQ-INV-002). For a command that moves
/// no money the balance after it is the balance before it.
[<Authority("REQ-INV-002", "INV-INV-002")>]
let settledWhen (balanceAfter: decimal) : InvoiceRules =
    { Select_Issued_RecordPayment =
        fun () -> if balanceAfter = 0m then InvoiceBranch_Issued_RecordPayment.Paid else InvoiceBranch_Issued_RecordPayment.PartiallyPaid
      Select_PartiallyPaid_RecordPayment =
        fun () -> if balanceAfter = 0m then InvoiceBranch_PartiallyPaid_RecordPayment.Paid else InvoiceBranch_PartiallyPaid_RecordPayment.PartiallyPaid }

/// The generated decision for one command, given the balance after it.
let private decide (invoice: Invoice) (command: InvoiceCommand) (balanceAfter: decimal) : Result<InvoiceState, Refusal> =
    InvoiceTransition.decide (settledWhen balanceAfter) invoice.State command
    |> Result.map _.Next
    |> Result.mapError ofGenerated

/// Presents a payment refusal (REQ-INV-002, DEC-FIN-001) with its plan code.
[<Authority("REQ-INV-002", "DEC-FIN-001")>]
let private ofPaymentRefusal (refusal: PaymentRefusal) : Refusal =
    match refusal with
    | PaymentRefusal.NonPositiveAmount _ -> Refused InvoiceRefusalCode.NonPositiveAmount
    | PaymentRefusal.CurrencyMismatch _ -> Refused InvoiceRefusalCode.CurrencyMismatch
    | PaymentRefusal.PaymentLimitExceeded _ -> Refused InvoiceRefusalCode.PaymentLimitExceeded

/// Issue (REQ-INV-001). Legality is the generated table's.
[<Authority("REQ-INV-001")>]
let issue (invoice: Invoice) : Result<Invoice, Refusal> =
    decide invoice InvoiceCommand.Issue (outstanding invoice) |> Result.map (fun next -> { invoice with State = next })

/// Void (REQ-INV-001). From PartiallyPaid the generated code reports the pair
/// as undecided, which becomes a deferral citing Q-FIN-001.
[<Authority("REQ-INV-001", "Q-FIN-001")>]
let voidInvoice (invoice: Invoice) : Result<Invoice, Refusal> =
    decide invoice InvoiceCommand.Void (outstanding invoice) |> Result.map (fun next -> { invoice with State = next })

/// Applies a validated payment (REQ-INV-002): the command must be legal in the
/// current state (generated table), the payment must be in the invoice's
/// currency and must not exceed the outstanding balance. The target state is
/// picked by the generated branching transition through `settledWhen`.
[<Authority("REQ-INV-001", "REQ-INV-002", "DEC-FIN-001", "INV-INV-001", "INV-INV-002", "INV-INV-003")>]
let recordPayment (invoice: Invoice) (payment: ValidatedPayment) : Result<Invoice * AppliedPayment, Refusal> =
    let before = outstanding invoice
    decide invoice InvoiceCommand.RecordPayment before
    |> Result.bind (fun _ ->
        if payment.PaymentCurrency <> invoice.Currency then Error(Refused InvoiceRefusalCode.CurrencyMismatch)
        elif payment.Value.Value > before then Error(Refused InvoiceRefusalCode.ExceedsOutstandingBalance)
        else Ok { Payment = payment; OutstandingBefore = before; OutstandingAfter = before - payment.Value.Value })
    |> Result.bind (fun applied ->
        decide invoice InvoiceCommand.RecordPayment applied.OutstandingAfter
        |> Result.map (fun next -> { invoice with State = next; Applied = applied :: invoice.Applied }, applied))

/// Validates and applies a payment in one step. Legality in the current state
/// is checked first, so a closed invoice reports INVOICE_CLOSED whatever the
/// amount; the order of checks is a presentation choice, not policy.
[<Authority("REQ-INV-001", "REQ-INV-002", "DEC-FIN-001")>]
let pay (limit: PaymentLimit) (invoice: Invoice) (customer: CustomerId) (amount: decimal) (paymentCurrency: Currency) : Result<Invoice * AppliedPayment, Refusal> =
    decide invoice InvoiceCommand.RecordPayment (outstanding invoice)
    |> Result.bind (fun _ -> validate limit invoice.Currency customer amount paymentCurrency |> Result.mapError ofPaymentRefusal)
    |> Result.bind (recordPayment invoice)
