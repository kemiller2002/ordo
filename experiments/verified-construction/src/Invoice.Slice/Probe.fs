/// A primitive-typed facade for independent verification (Dokimos references
/// the built Invoice.Slice.dll). Its API is stable; everything behind it is
/// the constructed model. The currency is fixed to USD.
module Invoice.Slice.Probe

open Echelon.Provenance
open Invoice.Slice.Money
open Invoice.Slice.Payment
open Invoice.Slice.Invoicing

type ProbeCommand =
    | Issue
    | Pay of customerId: string * amount: decimal
    | Void

/// An invoice under test. Opaque.
type ProbeState = private ProbeState of Invoice

/// A draft invoice for `total` USD. Throws ArgumentException when `total` is
/// not positive, because the fixed signature has no error channel.
[<Authority("REQ-INV-001", "REQ-INV-002")>]
let start (total: decimal) : ProbeState =
    match positiveAmount total with
    | Ok amount -> ProbeState(create usd amount)
    | Error _ -> invalidArg (nameof total) "an invoice total must be positive"

/// One command. Error carries the refusal code string.
[<Authority("REQ-INV-001", "REQ-INV-002", "DEC-FIN-001", "Q-FIN-001")>]
let step (ProbeState invoice) (command: ProbeCommand) : Result<ProbeState, string> =
    match command with
    | Issue -> issue invoice
    | Void -> voidInvoice invoice
    | Pay(customerId, amount) -> pay decFin001 invoice (CustomerId customerId) amount usd |> Result.map fst
    |> Result.map ProbeState
    |> Result.mapError refusalCode

/// "Draft" | "Issued" | "PartiallyPaid" | "Paid" | "Voided".
let stateName (ProbeState invoice) : string =
    match state invoice with
    | Generated.Invoice.InvoiceState.Draft -> "Draft"
    | Generated.Invoice.InvoiceState.Issued -> "Issued"
    | Generated.Invoice.InvoiceState.PartiallyPaid -> "PartiallyPaid"
    | Generated.Invoice.InvoiceState.Paid -> "Paid"
    | Generated.Invoice.InvoiceState.Voided -> "Voided"

let outstanding (ProbeState invoice) : decimal = Invoicing.outstanding invoice

/// Amounts of the payments applied, oldest first.
let paymentsApplied (ProbeState invoice) : decimal list = payments invoice |> List.map _.Amount
