/// CONSTRUCT-010 phase 2, increments 3 and 4: a persisted instance in
/// generated code. Durable idempotency survives reload, so a retried
/// operation never writes twice; an unknown save is settled by reading the
/// instance back, never by assuming failure or writing again; a stale write
/// is refused. The goldens are the synthetic gate; its sources are test input.
module Ordo.Construction.Tests.PersistenceTests

open System
open Xunit
open Generated.StoredGate
open Generated.SessionGate
open Ordo.Core.ConstructionPlan
open Ordo.Construction.Construction
open Ordo.Construction.Tests.Fixtures

/// An in-memory store with a compare-and-swap Save and a scripted fault for
/// the next save.
type private Store(initial: StoredGateRecord option) =
    let mutable stored = initial
    member val Fault: (StoredGateRecord -> StoredGateSaved) option = None with get, set
    member val Writes = 0 with get, set
    member _.Stored = stored
    member this.Replace(record) = stored <- Some record
    interface IStoredGateStore with
        member _.Load id =
            match stored with
            | Some r when r.Id = id -> StoredGateLoaded.Found r
            | _ -> StoredGateLoaded.Missing
        member this.Save(expected, record) =
            match this.Fault with
            | Some fault ->
                this.Fault <- None
                fault record
            | None ->
                match stored with
                | Some r when r.Revision = expected ->
                    this.Writes <- this.Writes + 1
                    stored <- Some record
                    StoredGateSaved.Saved
                | Some r -> StoredGateSaved.Stale r.Revision
                | None -> StoredGateSaved.SaveFailed "missing"

let private id' = StoredGateId "gate-1"
let private op = StoredGateOperationId "op-1"
let private closed = { Id = id'; Revision = 4L; State = StoredGateState.Closed; Applied = Set.empty }
let private rules = { StoredGateRules.Guard = (fun _ _ _ -> true); Select_Closed_Raise = (fun () -> StoredGateBranch_Closed_Raise.Open) }
let private raise' (store: Store) = StoredGatePersistence.execute store rules id' op StoredGateCommand.Raise

[<Fact>]
let ``an executed command is saved once, with the next revision and the operation recorded`` () =
    let store = Store(Some closed)
    match raise' store with
    | StoredGateExecution.Executed(_, record) ->
        Assert.Equal(5L, record.Revision)
        Assert.Equal(StoredGateState.Open, record.State)
        Assert.Contains(op, record.Applied)
    | other -> failwithf "%A" other
    Assert.Equal(1, store.Writes)

[<Fact>]
let ``a retried operation never writes twice, even after reload`` () =
    let store = Store(Some closed)
    raise' store |> ignore
    // A fresh process reloads the instance and retries the same operation.
    let reloaded = Store(store.Stored)
    Assert.Equal(StoredGateExecution.AlreadyApplied op, raise' reloaded)
    Assert.Equal(0, reloaded.Writes)

[<Fact>]
let ``an unknown save that landed is settled by reading back, without a second write`` () =
    let store = Store(Some closed)
    store.Fault <- Some(fun record -> store.Replace record; StoredGateSaved.SaveUnknown "timeout after commit")
    match raise' store with
    | StoredGateExecution.Executed(_, record) -> Assert.Contains(op, record.Applied)
    | other -> failwithf "%A" other
    Assert.Equal(StoredGateExecution.AlreadyApplied op, raise' store)

[<Fact>]
let ``an unknown save that did not land is definitely not saved, so a retry applies it exactly once`` () =
    let store = Store(Some closed)
    store.Fault <- Some(fun _ -> StoredGateSaved.SaveUnknown "timeout before commit")
    match raise' store with
    | StoredGateExecution.NotSaved _ -> ()
    | other -> failwithf "%A" other
    match raise' store with
    | StoredGateExecution.Executed _ -> ()
    | other -> failwithf "%A" other
    Assert.Equal(1, store.Writes)

[<Fact>]
let ``an unknown save overtaken by another write is a stale write, refused with its code`` () =
    let store = Store(Some closed)
    store.Fault <- Some(fun _ -> store.Replace { closed with Revision = 5L; State = StoredGateState.Jammed }; StoredGateSaved.SaveUnknown "lost")
    let result = raise' store
    match result with
    | StoredGateExecution.StaleRevision(_, 4L, 5L) -> ()
    | other -> failwithf "%A" other
    Assert.Equal(Some StoredGateRefusalCode.StaleRevision, StoredGatePersistence.executionCode result)

[<Fact>]
let ``a stale compare-and-swap is refused, and a missing instance reports its code`` () =
    let store = Store(Some closed)
    store.Fault <- Some(fun _ -> StoredGateSaved.Stale 7L)
    match raise' store with
    | StoredGateExecution.StaleRevision(_, 4L, 7L) -> ()
    | other -> failwithf "%A" other
    let missing = StoredGatePersistence.execute (Store None) rules id' op StoredGateCommand.Raise
    Assert.Equal(StoredGateExecution.NotFound id', missing)
    Assert.Equal(Some StoredGateRefusalCode.NoSuchGate, StoredGatePersistence.executionCode missing)

/// The same protocol with session scope: deduplication does not survive reload.
type private SessionStore(record: SessionGateRecord, save: SessionGateSaved) =
    member val Writes = 0 with get, set
    interface ISessionGateStore with
        member _.Load _ = SessionGateLoaded.Found record
        member this.Save(_, _) =
            this.Writes <- this.Writes + 1
            save

[<Fact>]
let ``session scope deduplicates within a session only, and escalates an unknown save`` () =
    let record = { SessionGateRecord.Id = SessionGateId "g"; Revision = 1L; State = SessionGateState.Closed }
    let rules = { SessionGateRules.Guard = (fun _ _ _ -> true); Select_Closed_Raise = (fun () -> SessionGateBranch_Closed_Raise.Open) }
    let op = SessionGateOperationId "op"
    let store = SessionStore(record, SessionGateSaved.Saved)
    let first, seen = SessionGatePersistence.execute store rules Set.empty record.Id op SessionGateCommand.Raise
    let second, _ = SessionGatePersistence.execute store rules seen record.Id op SessionGateCommand.Raise
    Assert.Equal(SessionGateExecution.AlreadyApplied op, second)
    Assert.Equal(1, store.Writes)
    // A new session has an empty set: the plan chose session scope, so this
    // retry writes again. That is the documented meaning of the choice.
    SessionGatePersistence.execute store rules Set.empty record.Id op SessionGateCommand.Raise |> ignore
    Assert.Equal(2, store.Writes)
    match first with
    | SessionGateExecution.Executed _ -> ()
    | other -> failwithf "%A" other
    let unknown, _ = SessionGatePersistence.execute (SessionStore(record, SessionGateSaved.SaveUnknown "lost")) rules Set.empty record.Id op SessionGateCommand.Raise
    match unknown with
    | SessionGateExecution.Escalated _ -> ()
    | other -> failwithf "%A" other

[<Fact>]
let ``an unresolved scope generates no deduplication and records the open question`` () =
    let d = draft storedGate
    let open' =
        { d with
            Operations =
                d.Operations |> List.map (function DeclarePersistence p -> DeclarePersistence { p with Idempotency = IdempotencyScope.Unresolved } | other -> other) }
    let c = validate (prepared storedGate) open' |> ok "validate" |> construct |> ok "construct"
    let persistence = (c.Artifacts |> List.find (fun a -> a.Path.EndsWith ".Persistence.g.fs")).Content
    Assert.DoesNotContain("OperationId", persistence)
    Assert.Contains("| StoredGateSaved.SaveUnknown reason -> StoredGateExecution.Unresolved(decision, reason)", persistence)
    Assert.Contains("RESOLVE-PERSISTENCE:idempotency", c.Obligations |> List.map _.Id)

[<Fact>]
let ``the store is handed off as implement obligations`` () =
    let ids = (construction storedGate).Obligations |> List.map _.Id
    Assert.Contains("IMPLEMENT:IStoredGateStore.Load", ids)
    Assert.Contains("IMPLEMENT:IStoredGateStore.Save", ids)

let private references =
    [ typeof<ValidatedPlan>.Assembly.Location
      typeof<PlannedWrite>.Assembly.Location ]

let private tiers (fixture: Fixture) (workflow: string) =
    [ "Model"; "Transition"; "Persistence" ]
    |> List.map (fun tier -> sprintf "#load @\"%s\"" (goldenPath fixture (sprintf "generated/%s/%s.%s.g.fs" workflow workflow tier)))
    |> String.concat "\n"

[<Fact>]
let ``rebasing a stale write cannot be expressed, and session records cannot carry applied ids`` () =
    let stored = tiers storedGate "StoredGate" + "\nopen Generated.StoredGate\n"
    let session = tiers sessionGate "SessionGate" + "\nopen Generated.SessionGate\n"
    let failures =
        Ordo.Tests.FsiProbe.check references
            [ "positive control", stored + "let x = StoredGateExecution.NotFound(StoredGateId \"g\")", None
              "no rebase outcome", stored + "let x = StoredGateExecution.Rebased", Some "FS0039"
              "session record has no durable applied ids",
              session + "let r = { SessionGateRecord.Id = SessionGateId \"g\"; Revision = 1L; State = SessionGateState.Closed; Applied = Set.empty }", Some "FS0039" ]
    Assert.True(List.isEmpty failures, String.concat "\n\n" failures)
