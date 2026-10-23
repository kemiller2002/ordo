/// The payments service's composition root: the designated place that holds
/// the payments-service capability (DEC-FIN-002). The capability is minted
/// here, privately, and never returned; callers get a service function that
/// validates, applies and persists in one step (REQ-INV-003).
module Invoice.Slice.PaymentsService.CompositionRoot

open Echelon.Provenance
open Invoice.Slice
open Invoice.Slice.Money
open Invoice.Slice.Payment
open Invoice.Slice.Invoicing
open Invoice.Slice.Persistence

/// The one capability value. Private to this module; its role is the
/// DEC-FIN-002 parameter.
[<Authority("DEC-FIN-002")>]
let private capability = PaymentsServiceCapability Authority.decFin002Role

type ServiceError =
    | PaymentRefused of Refusal
    | NotPersisted of PersistError

/// Records a payment on an invoice and persists the applied payment. The
/// limit is always DEC-FIN-001's; callers cannot supply another.
[<Authority("REQ-INV-001", "REQ-INV-002", "REQ-INV-003", "DEC-FIN-001", "DEC-FIN-002")>]
let recordAndPersist (target: PaymentStore) (invoice: Invoice) (customer: CustomerId) (amount: decimal) (paymentCurrency: Currency) =
    pay decFin001 invoice customer amount paymentCurrency
    |> Result.mapError PaymentRefused
    |> Result.bind (fun (next, applied) ->
        persist capability target applied |> Result.map (fun () -> next, applied) |> Result.mapError NotPersisted)
