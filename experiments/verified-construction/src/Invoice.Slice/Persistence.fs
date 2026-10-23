/// The payment persistence boundary (REQ-INV-003, DEC-FIN-002): a payment is
/// written only after it has been validated and applied, and only by the
/// payments service.
///
/// Compiler-enforced, outside this assembly and the payments-service assembly:
/// - `persist` accepts only an AppliedPayment, which only
///   Invoicing.recordPayment can produce; a decimal, a Money value or a
///   ValidatedPayment does not type-check;
/// - `persist` requires a PaymentsServiceCapability, whose constructor is
///   internal to Invoice.Slice and visible only to Invoice.Slice.PaymentsService;
/// - a PaymentStore's append function cannot be called except through
///   `persist`, because the field is private.
/// Runtime-only or organizational: who may call the payments service, and
/// what a host's own append function does with its storage (LIMITATIONS.md).
module Invoice.Slice.Persistence

open Echelon.Provenance
open Invoice.Slice.Invoicing

/// Proof that the caller is the payments service (DEC-FIN-002). Not a
/// credential: it cannot be fabricated by ordinary code, which is all a type
/// can say. Only Invoice.Slice.PaymentsService constructs it.
[<Authority("DEC-FIN-002", "REQ-INV-003")>]
type PaymentsServiceCapability =
    internal
    | PaymentsServiceCapability of role: string

    member this.Role =
        let (PaymentsServiceCapability role) = this
        role

type PersistError = PersistError of reason: string

/// The persistence port a host implements. Its append function is reachable
/// only through `persist`.
[<Authority("REQ-INV-003", "DEC-FIN-002")>]
type PaymentStore =
    private
        { Append: AppliedPayment -> Result<unit, PersistError> }

/// Wraps a host's storage function as the port.
let store (append: AppliedPayment -> Result<unit, PersistError>) : PaymentStore = { Append = append }

/// Writes an applied payment under the payments-service capability.
[<Authority("REQ-INV-003", "DEC-FIN-002")>]
let persist (capability: PaymentsServiceCapability) (target: PaymentStore) (payment: AppliedPayment) : Result<unit, PersistError> =
    ignore capability.Role
    target.Append payment
