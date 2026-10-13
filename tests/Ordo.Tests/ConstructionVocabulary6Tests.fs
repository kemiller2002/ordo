/// CONSTRUCT-010 phase 2, increments 3 and 4: plan vocabulary 6 persisted
/// instances. One `declarePersistence` states the idempotency scope and what
/// a stale write means; codes may go to a missing instance or a refused stale
/// write; persistence with transition effects (a dual write) is refused.
module Ordo.Tests.ConstructionVocabulary6Tests

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
let private gate = "gate-persisted-synthetic"

let private prepared () =
    let manifest = readManifest (read gate "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read gate "blueprint.json") |> ok "blueprint"
    prepareCohort manifest blueprint "COH-GATE" (Set.singleton "DEC-GATE") |> ok "prepare"

let private draft () = readPlan (read gate "plan.v6-durable.json") |> ok "plan"
let private violations plan =
    match validate (prepared ()) plan with
    | Ok _ -> []
    | Error vs -> vs
let private mapPersistence f (d: PlanDraft) =
    { d with Operations = d.Operations |> List.map (function DeclarePersistence p -> DeclarePersistence(f p) | other -> other) }
let private persistenceTrace () = (draft ()).Operations |> List.pick (function DeclarePersistence p -> Some p.Trace | _ -> None)

[<Fact>]
let ``both persisted gate plans validate and round-trip`` () =
    for file in [ "plan.v6-durable.json"; "plan.v6-session.json" ] do
        let d = readPlan (read gate file) |> ok file
        let v = validate (prepared ()) d |> ok "validate"
        Assert.Equal(ConstructionPlan.digest v, ConstructionPlan.digest (validate (prepared ()) (readPlan (encodePlan d) |> ok "reread") |> ok "again"))
    let p = (view (validate (prepared ()) (draft ()) |> ok "v")).Workflow.Persistence |> Option.get
    Assert.Equal((IdempotencyScope.Durable, StaleAction.Refuse), (p.Idempotency, p.OnStale))

[<Fact>]
let ``persistence is not in earlier vocabularies`` () =
    let v5 = { draft () with VocabularyVersion = VocabularyVersion5 }
    let vs = violations v5
    Assert.Contains(vs, function NotInVocabulary(_, "declarePersistence", VocabularyVersion5) -> true | _ -> false)
    Assert.Contains(vs, function NotInVocabulary(_, "persistence code target", VocabularyVersion5) -> true | _ -> false)
    match readPlan ((read gate "plan.v6-durable.json").Replace(VocabularyVersion6, VocabularyVersion5)) with
    | Error e -> Assert.Contains("not in " + VocabularyVersion5, e)
    | Ok _ -> failwith "vocabulary 5 accepted persistence"

[<Fact>]
let ``one persisted instance per workflow`` () =
    let twice = { draft () with Operations = (draft ()).Operations @ [ DeclarePersistence { Idempotency = IdempotencyScope.Session; OnStale = StaleAction.Escalate; Trace = persistenceTrace () } ] }
    Assert.Contains(DuplicateDefinition "persistence", violations twice)

[<Fact>]
let ``persistence with transition effects is a dual write and is refused`` () =
    let timer = "time-entry-timer"
    let manifest = readManifest (read timer "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read timer "blueprint.json") |> ok "blueprint"
    let cohort = prepareCohort manifest blueprint "COH-TIMER" (Set.singleton "DEC-TIMER-STATES") |> ok "prepare"
    let escalate = readPlan (read timer "plan.v5-escalate.json") |> ok "plan"
    let trace = escalate.Operations |> List.pick (function DefineWorkflow w -> Some w.Trace | _ -> None)
    let persisted =
        { escalate with
            VocabularyVersion = VocabularyVersion6
            Operations = escalate.Operations @ [ DeclarePersistence { Idempotency = IdempotencyScope.Durable; OnStale = StaleAction.Refuse; Trace = trace } ] }
    match validate cohort persisted with
    | Error vs -> Assert.Contains(PersistenceWithEffects "persistence", vs)
    | Ok _ -> failwith "persistence with effects was accepted"

[<Fact>]
let ``a stale-revision code needs a refusing persistence`` () =
    let escalating = draft () |> mapPersistence (fun p -> { p with OnStale = StaleAction.Escalate })
    Assert.Contains(CodeOnNonRefusal "codeFor:staleRevision", violations escalating)

[<Fact>]
let ``each persistence choice is classified, and an open one keeps the plan incomplete`` () =
    let classOf d subject = ((classify (validate (prepared ()) d |> ok "v")).Choices |> List.find (fun c -> c.Subject = subject)).Class
    Assert.Equal(ChoiceClass.Required [ "synthetic/gate.md#GATE-5" ], classOf (draft ()) "persistence:idempotency")
    let open' = draft () |> mapPersistence (fun p -> { p with Idempotency = IdempotencyScope.Unresolved })
    match classOf open' "persistence:idempotency" with
    | ChoiceClass.Unresolved _ -> ()
    | other -> failwithf "%A" other
    Assert.True(Result.isError (complete (validate (prepared ()) open' |> ok "v")))
