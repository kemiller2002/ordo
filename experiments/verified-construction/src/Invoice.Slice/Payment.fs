/// Payment validation: REQ-INV-002 (a positive amount in the invoice's
/// currency) and DEC-FIN-001 (a single payment above the limit is refused,
/// for every customer). The balance rule of REQ-INV-002 needs the invoice and
/// is applied in Invoicing.recordPayment, which alone produces an AppliedPayment.
module Invoice.Slice.Payment

open Echelon.Provenance
open Invoice.Slice.Money

/// Who paid. An identity only: no rule reads it, and nothing derives a
/// category from it. In particular, no customer is exempt from the limit
/// (DEC-FIN-001; OBS-LEGACY-001 is excluded from construction).
type CustomerId = CustomerId of string

/// The single-payment limit. Its constructor is private and its only value is
/// `decFin001`, built from the DEC-FIN-001 parameter in Invoice.Authority.g.fs.
/// It has no customer-category parameter, so none can be supplied.
[<Authority("DEC-FIN-001", "REQ-INV-002")>]
type PaymentLimit =
    private
    | PaymentLimit of decimal

    member this.Amount =
        let (PaymentLimit amount) = this
        amount

/// The DEC-FIN-001 limit. `construct` refuses a non-positive limit parameter,
/// so this is always a positive amount.
[<Authority("DEC-FIN-001")>]
let decFin001 : PaymentLimit = PaymentLimit Authority.decFin001Limit

type PaymentRefusal =
    | NonPositiveAmount of amount: decimal
    | CurrencyMismatch of invoice: Currency * payment: Currency
    | PaymentLimitExceeded of limit: decimal * amount: decimal

/// A payment that is positive, in the invoice's currency and within the
/// DEC-FIN-001 limit. Constructible only by `validate`.
[<Authority("REQ-INV-002", "DEC-FIN-001")>]
type ValidatedPayment =
    private
        { Customer: CustomerId
          Amount: PositiveAmount
          Currency: Currency }

    member this.CustomerId = this.Customer
    member this.Value = this.Amount
    member this.PaymentCurrency = this.Currency

/// DEC-FIN-001: refused when the amount is *above* the limit. The function
/// takes no customer, so the outcome cannot depend on one.
[<Authority("DEC-FIN-001")>]
let exceedsLimit (limit: PaymentLimit) (amount: PositiveAmount) : bool = amount.Value > limit.Amount

/// The only way to obtain a ValidatedPayment. Checks, in order: positive
/// amount, currency match, limit. The order decides only which code a caller
/// sees when several rules fail; it is a presentation choice, not policy.
[<Authority("REQ-INV-002", "DEC-FIN-001")>]
let validate
    (limit: PaymentLimit)
    (invoiceCurrency: Currency)
    (customer: CustomerId)
    (amount: decimal)
    (paymentCurrency: Currency)
    : Result<ValidatedPayment, PaymentRefusal> =
    match positiveAmount amount with
    | Error _ -> Error(NonPositiveAmount amount)
    | Ok _ when paymentCurrency <> invoiceCurrency -> Error(CurrencyMismatch(invoiceCurrency, paymentCurrency))
    | Ok positive when exceedsLimit limit positive -> Error(PaymentLimitExceeded(limit.Amount, amount))
    | Ok positive -> Ok { Customer = customer; Amount = positive; Currency = paymentCurrency }
