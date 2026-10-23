/// Translation to an Ordo vocabulary-4 plan, validated and emitted by the
/// existing Ordo engine; translation refuses when authority is missing.
module VerifiedConstruction.Tests.TranslationTests

open System.Text.Json.Nodes
open Xunit
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionClassification
open Ordo.Construction.Artifacts
open Echelon.Construct.Admission
open Echelon.Construct.Translate
open VerifiedConstruction.Tests.Fixtures

let private admitted text = readAndAdmit text |> ok "admit"

let private validated () =
    let _, _, _, plan = validatedPlan (admitted (handoffText ())) |> ok "translate"
    plan

let private translationErrors text =
    match validatedPlan (admitted text) with
    | Ok _ -> failwith "expected translation to refuse"
    | Error e -> e

[<Fact>]
let ``the plan is vocabulary 4, bound to the workflow cohort and DEC-FIN-001 only`` () =
    let v = view (validated ())
    Assert.Equal(VocabularyVersion4, v.Binding.VocabularyVersion)
    Assert.Equal("COH-INVOICE-WORKFLOW", v.Binding.CohortId)
    Assert.Equal<string list>([ "DEC-FIN-001" ], v.Binding.DecisionIds)

[<Fact>]
let ``the workflow has the five states, three commands and branching RecordPayment`` () =
    let w = (view (validated ())).Workflow
    Assert.Equal<string list>([ "Draft"; "Issued"; "Paid"; "PartiallyPaid"; "Voided" ], w.States |> List.map _.Name.Key)
    Assert.Equal<string list>([ "Issue"; "RecordPayment"; "Void" ], w.Commands |> List.map _.Key)
    Assert.Equal<(string * string list) list>(
        [ "Issued", [ "Paid"; "PartiallyPaid" ]; "PartiallyPaid", [ "Paid"; "PartiallyPaid" ] ],
        w.BranchingTransitions |> List.map (fun b -> b.InState.Key, b.Targets |> List.map _.Key))

[<Fact>]
let ``Q-FIN-001 is an undecided pair citing the question node, not a refusal or a transition`` () =
    let w = (view (validated ())).Workflow
    let u = w.UndecidedTransitions |> List.exactlyOne
    Assert.Equal(("PartiallyPaid", "Void"), (u.InState.Key, u.OnCommand.Key))
    Assert.Contains("Q-FIN-001", u.Trace.NodeIds)
    Assert.DoesNotContain(w.Refusals, fun r -> r.InState.Key = "PartiallyPaid" && r.OnCommand.Key = "Void")
    Assert.DoesNotContain(w.Transitions, fun t -> t.From.Key = "PartiallyPaid" && t.Command.Key = "Void")

[<Fact>]
let ``the plan is not complete, and the only open choice is the Q-FIN-001 pair`` () =
    match complete (validated ()) with
    | Ok _ -> failwith "a plan with an unresolved question must not be complete"
    | Error openChoices ->
        Assert.Equal<string list>([ "pair:PartiallyPaid/Void" ], openChoices |> List.map _.Subject)

[<Fact>]
let ``the limit refusal code rests on the DEC-FIN-001 decision node`` () =
    let w = (view (validated ())).Workflow
    let _, trace = w.RefusalCodes |> List.find (fun (c, _) -> c.Key = "PaymentLimitExceeded")
    Assert.Contains("DEC-FIN-001", trace.NodeIds)

[<Fact>]
let ``the Ordo engine emits the obligations a host must meet, including RESOLVE-PAIR for Q-FIN-001`` () =
    let construction = Ordo.Construction.Construction.construct (validated ()) |> ok "construct"
    Assert.Equal(GeneratorVersion4, construction.GeneratorVersion)
    Assert.Equal<string list>(
        [ "IMPLEMENT:InvoiceRules.Select/Issued/RecordPayment"
          "IMPLEMENT:InvoiceRules.Select/PartiallyPaid/RecordPayment"
          "RESOLVE-PAIR:PartiallyPaid/Void"
          "VERIFY:Invoice" ],
        construction.Obligations |> List.map _.Id)

[<Fact>]
let ``a limit decision that is only proposed is not constructed`` () =
    let errors =
        mutate (fun d -> (record d "decisions" "DEC-FIN-001")["status"] <- JsonValue.Create "proposed")
        |> translationErrors
    Assert.Contains(NotAccepted "DEC-FIN-001", errors)

[<Fact>]
let ``a handoff without Q-FIN-001 is not constructed by guessing the Void rule`` () =
    let errors = mutate (fun d -> d["policyQuestions"] <- JsonArray()) |> translationErrors
    Assert.Contains(MissingAuthority("Q-FIN-001", "unresolved policy question"), errors)

[<Fact>]
let ``a non-positive limit parameter is refused`` () =
    let errors =
        mutate (fun d ->
            match (record d "decisions" "DEC-FIN-001")["parameters"] with
            | null -> failwith "parameters"
            | p -> p["limit"] <- JsonValue.Create "0.00")
        |> translationErrors
    Assert.Contains(InvalidParameter("DEC-FIN-001", "limit", "0.00"), errors)

[<Fact>]
let ``a source document whose digest does not match is refused`` () =
    let a = admitted (handoffText ())
    let errors = verifySources a (Map.ofList [ "sources/invoicing-requirements.md", "sha256:" + String.replicate 64 "0" ])
    Assert.Contains(SourceUnavailable "sources/finance-decisions.md", errors)
    Assert.True(errors |> List.exists (function SourceDigestMismatch("sources/invoicing-requirements.md", _, _) -> true | _ -> false))
