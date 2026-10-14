/// CONSTRUCT-010 phase 2: decision records version 5 generate vocabulary 7
/// default plans with a document (many instances in one document), code
/// assignments to `saveConflict`, and wire text on codes. An absent choice is
/// never defaulted: it stays undecided and becomes a typed obligation. The
/// fixture is the synthetic gate yard; its sources are test input.
module Ordo.Tests.ConstructionDecisionsV5Tests

open System
open System.IO
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionDecisions
open Ordo.Core.ConstructionDefaultPlan
module ConstructionDecisions = Ordo.Core.ConstructionDecisions

let private fixtures () =
    let rec find (current: DirectoryInfo) =
        let candidate = Path.Combine(current.FullName, "tests", "fixtures", "construction")
        if Directory.Exists candidate then candidate
        else
            match current.Parent with
            | null -> failwith "construction fixtures missing from repository"
            | parent -> find parent
    find (DirectoryInfo AppContext.BaseDirectory)

let private read dir name = File.ReadAllText(Path.Combine(fixtures (), dir, name))
let private ok label value = match value with Ok x -> x | Error e -> failwithf "%s: %A" label e
let private yard = "gate-document-synthetic"

let private yardCohort () =
    let manifest = readManifest (read yard "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read yard "blueprint.json") |> ok "blueprint"
    prepareCohort manifest blueprint "COH-GATE" (Set.singleton "DEC-GATE") |> ok "prepare"

let private yardRecords () = readDecisionRecords (read yard "construction-decisions-v5.json") |> ok "records"
let private generateYard r = generateDefaultPlan (yardCohort ()) r ConstructionVocabulary.Version7

let private mapDocument f (r: ApprovedDecisionRecords) =
    { r with Records = r.Records |> List.map (fun x -> match x.Content with Document d -> { x with Content = Document(f d) } | _ -> x) }
let private subjects (plan: DefaultPlan) =
    plan.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.UndecidedPersistence) |> List.choose _.PlanSubject |> List.sort
let private refusedWith (predicate: DefaultPlanRefusal -> bool) (result: Result<DefaultPlan, DefaultPlanRefusal list>) =
    match result with
    | Error refusals -> Assert.True(List.exists predicate refusals, sprintf "%A" refusals)
    | Ok _ -> failwith "accepted"

[<Fact>]
let ``version 5 records generate a vocabulary 7 plan with the document, its codes and their wire text`` () =
    let plan = generateYard (yardRecords ()) |> ok "plan"
    let w = (view plan.ValidatedPlan).Workflow
    Assert.Equal(VocabularyVersion7, (view plan.ValidatedPlan).Binding.VocabularyVersion)
    let d = w.Document |> Option.get
    Assert.Equal((IdempotencyScope.Durable, StaleAction.Refuse, ReconcileMovedAction.Conflict), (d.Idempotency, d.OnSaveConflict, d.OnReconcileMoved))
    Assert.Equal(None, w.Persistence)
    Assert.Contains(w.CodeAssignments, fun a -> a.Target = CodeSubject.SaveConflict && a.Code.Key = "YardMoved")
    Assert.Contains(("NoSuchGate", "NO_SUCH_GATE"), w.WireTexts |> List.map (fun (c, t) -> c.Key, t))
    Assert.Equal(5, List.length w.WireTexts)
    Assert.Empty(subjects plan)

[<Fact>]
let ``every absent document choice is undecided, never defaulted`` () =
    let r = yardRecords () |> mapDocument (fun d -> { d with Idempotency = None; OnSaveConflict = None; OnReconcileMoved = None })
    // A save-conflict code needs a refusing document, so drop it with the choice.
    let r =
        { r with
            Records =
                r.Records
                |> List.map (fun x ->
                    match x.Content with
                    | StateModel m ->
                        { x with
                            Content =
                                StateModel
                                    { m with
                                        CodeAssignments = m.CodeAssignments |> List.filter (fun a -> a.Target <> RecordedCodeTarget.SaveConflict)
                                        RefusalCodes = m.RefusalCodes |> List.filter (fun c -> c.Code <> "YardMoved") } }
                    | _ -> x) }
    let plan = generateYard r |> ok "plan"
    let d = (view plan.ValidatedPlan).Workflow.Document |> Option.get
    Assert.Equal((IdempotencyScope.Unresolved, StaleAction.Unresolved, ReconcileMovedAction.Unresolved), (d.Idempotency, d.OnSaveConflict, d.OnReconcileMoved))
    Assert.Equal<string list>([ "document:idempotency"; "document:onReconcileMoved"; "document:onSaveConflict" ], subjects plan)
    let o = plan.Obligations |> List.find (fun o -> o.PlanSubject = Some "document:onSaveConflict")
    Assert.Equal<string list>([ "IGateYardDocumentStore" ], o.ExtensionSurface)
    Assert.Equal<string list>([ "DEC-GATE" ], o.NodeIds)

/// The slice's case: save conflicts are decided (refused, never merged), and
/// what reconcile reports for a moved version is an open question.
[<Fact>]
let ``a decided save conflict with an open reconcile case raises only that question`` () =
    let plan = generateYard (yardRecords () |> mapDocument (fun d -> { d with OnReconcileMoved = None })) |> ok "plan"
    let d = (view plan.ValidatedPlan).Workflow.Document |> Option.get
    Assert.Equal((StaleAction.Refuse, ReconcileMovedAction.Unresolved), (d.OnSaveConflict, d.OnReconcileMoved))
    Assert.Equal<string list>([ "document:onReconcileMoved" ], subjects plan)

[<Fact>]
let ``session scope fixes reconcile to escalate, and refuses any other recorded action`` () =
    let session = yardRecords () |> mapDocument (fun d -> { d with Idempotency = Some IdempotencyScope.Session; OnReconcileMoved = None })
    let plan = generateYard session |> ok "plan"
    Assert.Equal(ReconcileMovedAction.Escalate, ((view plan.ValidatedPlan).Workflow.Document |> Option.get).OnReconcileMoved)
    Assert.Empty(subjects plan)
    let claimed = session |> mapDocument (fun d -> { d with OnReconcileMoved = Some ReconcileMovedAction.Conflict })
    refusedWith (function GeneratedPlanInvalid vs -> List.contains (ReconcileMovedWithoutDurableScope "document") vs | _ -> false) (generateYard claimed)

[<Fact>]
let ``records and vocabulary must match`` () =
    refusedWith (function DecisionSchemaVocabularyMismatch(DecisionsSchemaVersion5, VocabularyVersion6) -> true | _ -> false)
        (generateDefaultPlan (yardCohort ()) (yardRecords ()) ConstructionVocabulary.Version6)
    let asFour = { yardRecords () with SchemaVersion = DecisionsSchemaVersion4 }
    refusedWith (function DecisionSchemaVocabularyMismatch(DecisionsSchemaVersion4, VocabularyVersion7) -> true | _ -> false) (generateYard asFour)
    Assert.Equal(Ok ConstructionVocabulary.Version7, ConstructionVocabulary.ofVersion VocabularyVersion7)

[<Fact>]
let ``documents, save-conflict codes and wire text exist only in version 5 records`` () =
    let v5 = read yard "construction-decisions-v5.json"
    let asFour = v5.Replace(DecisionsSchemaVersion5, DecisionsSchemaVersion4)
    match readDecisionRecords asFour with
    | Error e -> Assert.Contains("wireText", e)
    | Ok _ -> failwith "version 4 accepted wire text"
    let v4 = read "gate-persisted-synthetic" "construction-decisions-v4.json"
    for kind, content in
        [ "document", """{"kind":"document","idempotency":"durable","requirementKeys":["k"]}""" ] do
        let text = v4.Replace("\"records\": [", sprintf "\"records\": [ {\"nodeId\": \"DEC-GATE\", \"content\": %s}," content)
        match readDecisionRecords text with
        | Error e -> Assert.Contains("unsupported content kind: " + kind, e)
        | Ok _ -> failwithf "version 4 accepted %s" kind
    let target = v4.Replace("\"kind\": \"staleRevision\"", "\"kind\": \"saveConflict\"")
    Assert.NotEqual<string>(v4, target)
    match readDecisionRecords target with
    | Error e -> Assert.Contains("unsupported refusal-code target: saveConflict", e)
    | Ok _ -> failwith "version 4 accepted a saveConflict target"

[<Fact>]
let ``an unknown document choice is refused, never read as undecided`` () =
    let v5 = read yard "construction-decisions-v5.json"
    for member', bad in [ "idempotency", "forever"; "onSaveConflict", "merge"; "onReconcileMoved", "overwrite" ] do
        let good =
            match member' with
            | "idempotency" -> "\"idempotency\": \"durable\""
            | "onSaveConflict" -> "\"onSaveConflict\": \"refuse\""
            | _ -> "\"onReconcileMoved\": \"conflict\""
        let text = v5.Replace(good, sprintf "\"%s\": \"%s\"" member' bad)
        Assert.NotEqual<string>(v5, text)
        match readDecisionRecords text with
        | Error e -> Assert.Contains("omit it to leave it undecided", e)
        | Ok _ -> failwithf "accepted %s %s" member' bad

[<Fact>]
let ``a document and a persisted instance together are refused`` () =
    let r = yardRecords ()
    let both =
        { r with
            Records =
                r.Records
                |> List.collect (fun x ->
                    match x.Content with
                    | Document d ->
                        [ x
                          { x with
                              Content = Persistence { Idempotency = Some IdempotencyScope.Durable; OnStale = Some StaleAction.Refuse; RequirementKeys = d.RequirementKeys } } ]
                    | _ -> [ x ]) }
    refusedWith (function GeneratedPlanInvalid vs -> List.contains (PersistenceAndDocument "document") vs | _ -> false) (generateYard both)

[<Fact>]
let ``version 5 records are canonical and deterministic`` () =
    let r = yardRecords ()
    let again = readDecisionRecords (encodeDecisionRecords r) |> ok "reread"
    Assert.Equal(ConstructionDecisions.digest r, ConstructionDecisions.digest again)
    Assert.Contains("\"wireText\": \"YARD_MOVED\"", encodeDecisionRecords r)
    Assert.Equal((generateYard r |> ok "a").Digest, (generateYard again |> ok "b").Digest)
    // Version 4 records keep their exact bytes: no new member is written.
    let v4 = readDecisionRecords (read "gate-persisted-synthetic" "construction-decisions-v4.json") |> ok "v4"
    Assert.DoesNotContain("wireText", encodeDecisionRecords v4)

/// Two approved decisions may not each record a document: the workflow has
/// one. The fixture blueprint has one decision node, so the test adds a
/// second in memory and approves both (test input, not an approval).
[<Fact>]
let ``one document per record set, across decision nodes`` () =
    let manifest = readManifest (read yard "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read yard "blueprint.json") |> ok "blueprint"
    let first = blueprint.Nodes |> List.find (fun n -> n.Id = "DEC-GATE")
    let widened =
        { blueprint with
            Nodes = blueprint.Nodes @ [ { first with Id = "DEC-GATE-2" } ]
            Requirements =
                blueprint.Requirements
                |> List.map (fun r -> if List.contains "DEC-GATE" r.NodeIds then { r with NodeIds = r.NodeIds @ [ "DEC-GATE-2" ] } else r) }
    let cohort = prepareCohort manifest widened "COH-GATE" (Set.ofList [ "DEC-GATE"; "DEC-GATE-2" ]) |> ok "prepare"
    let r = yardRecords ()
    let second = r.Records |> List.find (fun x -> match x.Content with Document _ -> true | _ -> false)
    let twice =
        { r with
            BlueprintDigest = blueprintDigest widened
            Records = r.Records @ [ { second with NodeId = "DEC-GATE-2" } ] }
    refusedWith (function MultipleDocumentRecords [ "DEC-GATE"; "DEC-GATE-2" ] -> true | _ -> false)
        (generateDefaultPlan cohort twice ConstructionVocabulary.Version7)
