/// Amounts and currencies. Every value here is constructible only through a
/// validating function, so an unvalidated amount has no type to hide in.
module Invoice.Slice.Money

open Echelon.Provenance

/// An ISO-4217-shaped currency code: three ASCII upper-case letters. The shape
/// is a technical check; which currencies exist is not decided here.
type Currency =
    private
    | Currency of string

    member this.Code =
        let (Currency code) = this
        code

/// A strictly positive amount (REQ-INV-002: "a payment is a positive amount").
/// No rounding or scale rule is applied: none is stated by the handoff.
[<Authority("REQ-INV-002")>]
type PositiveAmount =
    private
    | PositiveAmount of decimal

    member this.Value =
        let (PositiveAmount value) = this
        value

type MoneyError =
    | NonPositiveAmount of amount: decimal
    | InvalidCurrencyCode of code: string

/// A technical shape check serving REQ-INV-002 ("in the invoice's currency");
/// it decides no policy about which currencies are allowed.
[<Authority("REQ-INV-002")>]
let currency (code: string) : Result<Currency, MoneyError> =
    if code.Length = 3 && code |> Seq.forall (fun c -> c >= 'A' && c <= 'Z') then Ok(Currency code)
    else Error(InvalidCurrencyCode code)

/// The US dollar, the probe's fixed currency. A technical constant, not policy.
let usd : Currency = Currency "USD"

[<Authority("REQ-INV-002")>]
let positiveAmount (amount: decimal) : Result<PositiveAmount, MoneyError> =
    if amount > 0m then Ok(PositiveAmount amount) else Error(NonPositiveAmount amount)
