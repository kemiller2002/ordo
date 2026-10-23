/// The payments service persists only applied payments, under its capability.
module VerifiedConstruction.Tests.PersistenceTests

open Xunit
open Invoice.Slice
open Invoice.Slice.Money
open Invoice.Slice.Payment
open Invoice.Slice.Invoicing
open Invoice.Slice.Persistence
open Invoice.Slice.PaymentsService

let private issuedInvoice total =
    positiveAmount total
    |> Result.map (create usd)
    |> Result.mapError (sprintf "%A")
    |> Result.bind (issue >> Result.mapError (sprintf "%A"))
    |> Result.defaultWith failwith

/// A store that accepts exactly the expected amount, so the test observes
/// what was written through the result, without shared mutable state.
let private expecting (amount: decimal) =
    store (fun applied -> if applied.Amount = amount then Ok() else Error(PersistError(sprintf "unexpected %M" applied.Amount)))

/// A store that must never be reached.
let private unreachable = store (fun applied -> Error(PersistError(sprintf "persisted a refused payment of %M" applied.Amount)))

[<Fact>]
let ``the payments service persists an applied payment`` () =
    match CompositionRoot.recordAndPersist (expecting 250m) (issuedInvoice 1000m) (CustomerId "C-1") 250m usd with
    | Ok(next, applied) ->
        Assert.Equal(Generated.Invoice.InvoiceState.PartiallyPaid, state next)
        Assert.Equal(250m, applied.Amount)
        Assert.Equal(1000m, applied.Before)
        Assert.Equal(750m, applied.After)
    | Error e -> failwithf "%A" e

[<Fact>]
let ``a refused payment is never persisted`` () =
    match CompositionRoot.recordAndPersist unreachable (issuedInvoice 50000m) (CustomerId "STR-001") 10000.01m usd with
    | Error(CompositionRoot.PaymentRefused(Refused code)) ->
        Assert.Equal(Generated.Invoice.InvoiceRefusalCode.PaymentLimitExceeded, code)
    | other -> failwithf "expected a refusal before persistence, got %A" other

[<Fact>]
let ``a store failure is reported, not swallowed`` () =
    let failing = store (fun _ -> Error(PersistError "disk full"))
    match CompositionRoot.recordAndPersist failing (issuedInvoice 100m) (CustomerId "C-1") 10m usd with
    | Error(CompositionRoot.NotPersisted(PersistError reason)) -> Assert.Equal("disk full", reason)
    | other -> failwithf "expected NotPersisted, got %A" other

[<Fact>]
let ``no code in Invoice.Slice constructs the capability; only the payments service does`` () =
    let sources dir =
        System.IO.Directory.GetFiles(Fixtures.path dir, "*.fs", System.IO.SearchOption.AllDirectories)
        |> Array.filter (fun f -> not (f.Contains "/obj/" || f.Contains "/bin/"))
    // Applying the union case: `PaymentsServiceCapability <arg>`, excluding its
    // declaration, its `of` case, a destructuring pattern and type annotations.
    let applied = System.Text.RegularExpressions.Regex(@"(?<![:(]\s*)\bPaymentsServiceCapability\s+(?!of\b|=)\S")
    let minting (file: string) =
        System.IO.File.ReadAllLines file
        |> Array.exists (fun line -> not (line.Trim().StartsWith "//") && applied.IsMatch line)
    Assert.Empty(sources "src/Invoice.Slice" |> Array.filter minting)
    Assert.NotEmpty(sources "src/Invoice.Slice.PaymentsService" |> Array.filter minting)
