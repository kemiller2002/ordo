/// CONSTRUCT-010 phase 2: plan vocabulary 7 document persistence. One
/// `declareDocument` states the idempotency scope of save operation ids and
/// what a save against a moved document means; codes may go to a missing
/// entry, a stale entry revision or a refused save conflict; a document with
/// transition effects (a dual write) or together with a persisted instance
/// is refused.
module Ordo.Tests.ConstructionVocabulary7Tests

open System
open System.IO
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionPlanWire
open Ordo.Core.ConstructionClassification
module ConstructionPlan = Ordo.Core.ConstructionPlan

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

let private prepared () =
    let manifest = readManifest (read yard "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read yard "blueprint.json") |> ok "blueprint"
    prepareCohort manifest blueprint "COH-GATE" (Set.singleton "DEC-GATE") |> ok "prepare"

let private draft () = readPlan (read yard "plan.v7-durable.json") |> ok "plan"
let private violations plan =
    match validate (prepared ()) plan with
    | Ok _ -> []
    | Error vs -> vs
let private mapDocument f (d: PlanDraft) =
    { d with Operations = d.Operations |> List.map (function DeclareDocument p -> DeclareDocument(f p) | other -> other) }
let private documentTrace () = (draft ()).Operations |> List.pick (function DeclareDocument p -> Some p.Trace | _ -> None)

[<Fact>]
let ``every yard plan validates and round-trips`` () =
    for file in [ "plan.v7-durable.json"; "plan.v7-session.json"; "plan.v7-open.json" ] do
        let d = readPlan (read yard file) |> ok file
        let v = validate (prepared ()) d |> ok "validate"
        Assert.Equal(ConstructionPlan.digest v, ConstructionPlan.digest (validate (prepared ()) (readPlan (encodePlan d) |> ok "reread") |> ok "again"))
    let w = (view (validate (prepared ()) (draft ()) |> ok "v")).Workflow
    let d = w.Document |> Option.get
    Assert.Equal((IdempotencyScope.Durable, StaleAction.Refuse), (d.Idempotency, d.OnSaveConflict))
    Assert.Equal(None, w.Persistence)

[<Fact>]
let ``a document is not in earlier vocabularies`` () =
    let v6 = { draft () with VocabularyVersion = VocabularyVersion6 }
    let vs = violations v6
    Assert.Contains(vs, function NotInVocabulary(_, "declareDocument", VocabularyVersion6) -> true | _ -> false)
    Assert.Contains(vs, function NotInVocabulary(_, "saveConflict", VocabularyVersion6) -> true | _ -> false)
    match readPlan ((read yard "plan.v7-durable.json").Replace(VocabularyVersion7, VocabularyVersion6)) with
    | Error e -> Assert.Contains("not in " + VocabularyVersion6, e)
    | Ok _ -> failwith "vocabulary 6 accepted a document"

[<Fact>]
let ``the reader refuses an unknown document choice`` () =
    for bad in [ "\"idempotency\": \"durable\"", "\"idempotency\": \"forever\""
                 "\"onSaveConflict\": \"refuse\"", "\"onSaveConflict\": \"merge\"" ] do
        let text = (read yard "plan.v7-durable.json").Replace(fst bad, snd bad)
        Assert.NotEqual<string>(read yard "plan.v7-durable.json", text)
        Assert.True(Result.isError (readPlan text), snd bad)

[<Fact>]
let ``one document per workflow, and never together with a persisted instance`` () =
    let trace = documentTrace ()
    let twice = { draft () with Operations = (draft ()).Operations @ [ DeclareDocument { Idempotency = IdempotencyScope.Session; OnSaveConflict = StaleAction.Escalate; Trace = trace } ] }
    Assert.Contains(DuplicateDefinition "document", violations twice)
    let both = { draft () with Operations = (draft ()).Operations @ [ DeclarePersistence { Idempotency = IdempotencyScope.Durable; OnStale = StaleAction.Refuse; Trace = trace } ] }
    let vs = violations both
    Assert.Contains(PersistenceAndDocument "document", vs)
    Assert.Contains(PersistenceAndDocument "persistence", vs)

[<Fact>]
let ``a document with transition effects is a dual write and is refused`` () =
    let timer = "time-entry-timer"
    let manifest = readManifest (read timer "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read timer "blueprint.json") |> ok "blueprint"
    let cohort = prepareCohort manifest blueprint "COH-TIMER" (Set.singleton "DEC-TIMER-STATES") |> ok "prepare"
    let escalate = readPlan (read timer "plan.v5-escalate.json") |> ok "plan"
    let trace = escalate.Operations |> List.pick (function DefineWorkflow w -> Some w.Trace | _ -> None)
    let documented =
        { escalate with
            VocabularyVersion = VocabularyVersion7
            Operations = escalate.Operations @ [ DeclareDocument { Idempotency = IdempotencyScope.Durable; OnSaveConflict = StaleAction.Refuse; Trace = trace } ] }
    match validate cohort documented with
    | Error vs ->
        Assert.Contains(PersistenceWithEffects "document", vs)
        Assert.DoesNotContain(vs, function PersistenceAndDocument _ -> true | _ -> false)
    | Ok _ -> failwith "a document with effects was accepted"

[<Fact>]
let ``a save-conflict code needs a document that refuses the conflict`` () =
    let escalating = draft () |> mapDocument (fun d -> { d with OnSaveConflict = StaleAction.Escalate })
    let vs = violations escalating
    Assert.Contains(CodeOnNonRefusal "codeFor:saveConflict", vs)
    // A document refuses a missing entry and a stale entry revision itself,
    // whatever it does with a save conflict.
    Assert.DoesNotContain(CodeOnNonRefusal "codeFor:notFound", vs)
    Assert.DoesNotContain(CodeOnNonRefusal "codeFor:staleRevision", vs)

[<Fact>]
let ``each document choice is classified, and an open one keeps the plan incomplete`` () =
    let classOf d subject = ((classify (validate (prepared ()) d |> ok "v")).Choices |> List.find (fun c -> c.Subject = subject)).Class
    Assert.Equal(ChoiceClass.Required [ "synthetic/gate.md#GATE-6" ], classOf (draft ()) "document:idempotency")
    Assert.Equal(ChoiceClass.Required [ "synthetic/gate.md#GATE-6" ], classOf (draft ()) "document:onSaveConflict")
    for f, subject in [ (fun (d: DocumentDraft) -> { d with Idempotency = IdempotencyScope.Unresolved }), "document:idempotency"
                        (fun d -> { d with OnSaveConflict = StaleAction.Unresolved }), "document:onSaveConflict" ] do
        // The session yard assigns no codes, so leaving its save conflict open is valid.
        let open' = readPlan (read yard "plan.v7-session.json") |> ok "plan" |> mapDocument f
        match classOf open' subject with
        | ChoiceClass.Unresolved _ -> ()
        | other -> failwithf "%s: %A" subject other
        Assert.True(Result.isError (complete (validate (prepared ()) open' |> ok "v")), subject)
        // A requirement traced only by a document with an open choice is not constructed.
        let coverage = (view (validate (prepared ()) open' |> ok "v")).Coverage
        Assert.Equal(WorkflowLevel, coverage["synthetic/gate.md#GATE-6"])
    let decided = (view (validate (prepared ()) (readPlan (read yard "plan.v7-session.json") |> ok "plan") |> ok "v")).Coverage
    Assert.Equal(Constructed [ "document" ], decided["synthetic/gate.md#GATE-6"])
