/// Compile-negative evidence, run through the real F# compiler (`dotnet fsi`)
/// against the built Invoice.Slice and Invoice.Slice.PaymentsService
/// assemblies, with Ordo's FsiProbe harness. Each failing probe must fail with
/// the named error code, and each group has a positive control that must
/// compile, so a probe that fails for an unrelated reason is not counted.
///
/// What this shows: ordinary code outside these assemblies cannot construct
/// these values or reach the port without the capability. It does not show
/// that an unauthorized decision cannot be made (LIMITATIONS.md).
module VerifiedConstruction.Tests.CompileNegativeTests

open Xunit

let private references =
    [ typeof<Invoice.Slice.Payment.ValidatedPayment>.Assembly.Location
      typeof<Invoice.Slice.PaymentsService.CompositionRoot.ServiceError>.Assembly.Location ]

let private prelude =
    """
open Invoice.Slice
open Invoice.Slice.Money
open Invoice.Slice.Payment
open Invoice.Slice.Invoicing
open Invoice.Slice.Persistence
let amount = match positiveAmount 100m with Ok a -> a | Error e -> failwithf "%A" e
let invoice = create usd amount
"""

let private check probes =
    let failures = Ordo.Tests.FsiProbe.check references probes
    Assert.True(List.isEmpty failures, String.concat "\n\n" failures)

[<Fact>]
let ``validated values cannot be constructed around their validators`` () =
    check
        [ "positive control: values through their validators",
          prelude + "let v = validate decFin001 usd (CustomerId \"C-1\") 100m usd\nlet p = pay decFin001 invoice (CustomerId \"C-1\") 100m usd",
          None
          "ValidatedPayment constructed as a record",
          prelude + "let v : ValidatedPayment = { Customer = CustomerId \"C-1\"; Amount = amount; Currency = usd }",
          Some "FS1093"
          "PaymentLimit constructed from an arbitrary number",
          prelude + "let l = PaymentLimit 25000m",
          Some "FS1093"
          "PositiveAmount constructed without its check",
          prelude + "let a = PositiveAmount -5m",
          Some "FS1093"
          "AppliedPayment constructed as a record",
          prelude + "let a (v: ValidatedPayment) : AppliedPayment = { Payment = v; OutstandingBefore = 0m; OutstandingAfter = 0m }",
          Some "FS1093"
          "an invoice constructed in an arbitrary state",
          prelude + "let i : Invoice = { State = Generated.Invoice.InvoiceState.Paid; Currency = usd; Total = amount; Applied = [] }",
          Some "FS1093" ]

[<Fact>]
let ``the limit has no customer parameter to exempt anyone with`` () =
    check
        [ "positive control: the limit applied to an amount",
          prelude + "let over = exceedsLimit decFin001 amount",
          None
          "a customer passed to the limit check",
          prelude + "let over : bool = exceedsLimit decFin001 (CustomerId \"STR-001\")",
          Some "FS0001"
          "a different limit passed to the service in place of DEC-FIN-001's",
          prelude + "let l : PaymentLimit = 25000m",
          Some "FS0001" ]

[<Fact>]
let ``a payment cannot be persisted without the capability or as anything but an applied payment`` () =
    check
        [ "positive control: the payments service records and persists",
          prelude
          + "let s = store (fun _ -> Ok ())\n"
          + "let r = Invoice.Slice.PaymentsService.CompositionRoot.recordAndPersist s invoice (CustomerId \"C-1\") 10m usd",
          None
          "positive control: persist type-checks given a capability and an applied payment",
          prelude + "let f (c: PaymentsServiceCapability) (s: PaymentStore) (a: AppliedPayment) = persist c s a",
          None
          "persisting without the capability",
          prelude + "let f (s: PaymentStore) (a: AppliedPayment) = persist s a",
          Some "FS0001"
          "persisting a raw decimal",
          prelude + "let f (c: PaymentsServiceCapability) (s: PaymentStore) = persist c s 100m",
          Some "FS0001"
          "persisting a validated but unapplied payment",
          prelude + "let f (c: PaymentsServiceCapability) (s: PaymentStore) (v: ValidatedPayment) = persist c s v",
          Some "FS0001"
          "minting the capability outside the payments service",
          prelude + "let c = PaymentsServiceCapability \"payments-service\"",
          Some "FS1093"
          "calling the store's append function around persist",
          prelude + "let f (s: PaymentStore) (a: AppliedPayment) = s.Append a",
          Some "FS1093"
          "reaching the composition root's private capability",
          prelude + "let c = Invoice.Slice.PaymentsService.CompositionRoot.capability",
          Some "FS1094" ]
