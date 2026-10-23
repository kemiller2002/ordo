/// Behaviour of the constructed model through the Probe facade (the API
/// Dokimos uses), against the frozen slice outcomes 1 and 2.
module VerifiedConstruction.Tests.BehaviourTests

open Xunit
open Invoice.Slice.Probe

let private run total commands =
    commands |> List.fold (fun state command -> state |> Result.bind (fun s -> step s command)) (Ok(start total))

let private okState label result =
    match result with
    | Ok s -> s
    | Error code -> failwithf "%s: refused with %s" label code

let private issued total = run total [ Issue ] |> okState "issue"

[<Fact>]
let ``a new invoice is a draft with its total outstanding`` () =
    let s = start 500m
    Assert.Equal("Draft", stateName s)
    Assert.Equal(500m, outstanding s)
    Assert.Empty(paymentsApplied s)

[<Fact>]
let ``a partial then a full payment leads to Paid`` () =
    let partial = step (issued 1000m) (Pay("C-1", 400m)) |> okState "partial"
    Assert.Equal("PartiallyPaid", stateName partial)
    Assert.Equal(600m, outstanding partial)
    let paid = step partial (Pay("C-1", 600m)) |> okState "full"
    Assert.Equal("Paid", stateName paid)
    Assert.Equal(0m, outstanding paid)
    Assert.Equal<decimal list>([ 400m; 600m ], paymentsApplied paid)

[<Fact>]
let ``a single full payment from Issued leads straight to Paid`` () =
    let paid = step (issued 250m) (Pay("C-1", 250m)) |> okState "full"
    Assert.Equal("Paid", stateName paid)

[<Fact>]
let ``an overpayment is refused and changes nothing`` () =
    let s = step (issued 1000m) (Pay("C-1", 400m)) |> okState "partial"
    Assert.Equal(Error "EXCEEDS_OUTSTANDING_BALANCE", step s (Pay("C-1", 600.01m)) |> Result.map stateName)
    Assert.Equal(600m, outstanding s)

[<Fact>]
let ``a payment above 10,000.00 is refused for an ordinary customer`` () =
    Assert.Equal(Error "PAYMENT_LIMIT_EXCEEDED", step (issued 50000m) (Pay("C-1", 10000.01m)) |> Result.map stateName)

[<Fact>]
let ``a payment above 10,000.00 is refused for an STR- customer too (no exemption)`` () =
    Assert.Equal(Error "PAYMENT_LIMIT_EXCEEDED", step (issued 50000m) (Pay("STR-001", 10000.01m)) |> Result.map stateName)

[<Fact>]
let ``a payment between the old and new limits is refused (the 25,000.00 limit is gone)`` () =
    Assert.Equal(Error "PAYMENT_LIMIT_EXCEEDED", step (issued 50000m) (Pay("STR-001", 20000m)) |> Result.map stateName)

[<Fact>]
let ``a payment of exactly 10,000.00 is accepted`` () =
    let s = step (issued 50000m) (Pay("STR-001", 10000.00m)) |> okState "at limit"
    Assert.Equal("PartiallyPaid", stateName s)
    Assert.Equal(40000m, outstanding s)

[<Fact>]
let ``a non-positive payment is refused`` () =
    Assert.Equal(Error "NON_POSITIVE_AMOUNT", step (issued 100m) (Pay("C-1", 0m)) |> Result.map stateName)
    Assert.Equal(Error "NON_POSITIVE_AMOUNT", step (issued 100m) (Pay("C-1", -5m)) |> Result.map stateName)

[<Fact>]
let ``paying a draft is refused`` () =
    Assert.Equal(Error "COMMAND_NOT_ALLOWED_IN_STATE", step (start 100m) (Pay("C-1", 10m)) |> Result.map stateName)

[<Fact>]
let ``issuing twice is refused`` () =
    Assert.Equal(Error "COMMAND_NOT_ALLOWED_IN_STATE", step (issued 100m) Issue |> Result.map stateName)

[<Fact>]
let ``void from Draft and from Issued with no payment is accepted`` () =
    Assert.Equal(Ok "Voided", step (start 100m) Void |> Result.map stateName)
    Assert.Equal(Ok "Voided", step (issued 100m) Void |> Result.map stateName)

[<Fact>]
let ``void from PartiallyPaid is deferred with the Q-FIN-001 code`` () =
    let s = step (issued 1000m) (Pay("C-1", 1m)) |> okState "partial"
    Assert.Equal(Error "POLICY_UNRESOLVED_Q_FIN_001", step s Void |> Result.map stateName)

[<Fact>]
let ``the deferral value carries the question ID`` () =
    let open' = Invoice.Slice.Invoicing.create Invoice.Slice.Money.usd (Invoice.Slice.Money.positiveAmount 100m |> Result.defaultWith (fun _ -> failwith "amount"))
    let partial =
        Invoice.Slice.Invoicing.issue open'
        |> Result.bind (fun i -> Invoice.Slice.Invoicing.pay Invoice.Slice.Payment.decFin001 i (Invoice.Slice.Payment.CustomerId "C-1") 10m Invoice.Slice.Money.usd |> Result.map fst)
        |> Result.defaultWith (fun e -> failwithf "%A" e)
    match Invoice.Slice.Invoicing.voidInvoice partial with
    | Error(Invoice.Slice.Invoicing.DeferredPendingQuestion(code, questionId)) ->
        Assert.Equal("Q-FIN-001", questionId)
        Assert.Equal(Generated.Invoice.InvoiceRefusalCode.PolicyUnresolvedQFIN001, code)
    | other -> failwithf "expected a deferral, got %A" other

[<Fact>]
let ``terminal states refuse every command`` () =
    let paid = step (issued 100m) (Pay("C-1", 100m)) |> okState "paid"
    let voided = step (start 100m) Void |> okState "voided"
    for s in [ paid; voided ] do
        for command in [ Issue; Pay("C-1", 1m); Void ] do
            Assert.Equal(Error "INVOICE_CLOSED", step s command |> Result.map stateName)

[<Fact>]
let ``a non-positive total cannot start an invoice`` () =
    Assert.Throws<System.ArgumentException>(fun () -> start 0m |> ignore) |> ignore
