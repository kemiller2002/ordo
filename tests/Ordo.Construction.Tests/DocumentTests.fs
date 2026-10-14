/// CONSTRUCT-010 phase 2, vocabulary 7: document persistence in generated
/// code. Many instances live in one document; commands change it in memory;
/// an explicit save writes it under one operation id with a version
/// compare-and-swap. An unknown save is returned as unknown, blocks further
/// changes, and is settled only by an explicit reconcile that inspects the
/// stored document. The goldens are the synthetic gate yard; its sources are
/// test input.
module Ordo.Construction.Tests.DocumentTests

open Xunit
open Generated.GateYard
open Generated.SessionYard
open Generated.OpenYard
open Ordo.Core.ConstructionPlan
open Ordo.Construction.Construction
open Ordo.Construction.Tests.Fixtures

/// An in-memory document store. Like the stores the vocabulary is written
/// for, it records every landed operation with the version it produced. A
/// scripted fault replaces the next write.
type private Yard(initial: GateYardDocument option) =
    let mutable stored = initial
    member val Fault: (GateYardOperationId -> GateYardDocument -> GateYardDocumentWrite) option = None with get, set
    member val ReadFault: string option = None with get, set
    member val Writes = 0 with get, set
    member _.Stored = stored

    /// Lands a write as the store would, whoever makes it.
    member this.Land(operation, document: GateYardDocument) =
        let current = stored |> Option.defaultValue GateYardDocuments.empty
        let version = (current.Version |> Option.defaultValue 0L) + 1L
        this.Writes <- this.Writes + 1
        stored <- Some { document with Version = Some version; Applied = Map.add operation version current.Applied }
        version

    interface IGateYardDocumentStore with
        member this.Read() =
            match this.ReadFault, stored with
            | Some reason, _ -> GateYardDocumentRead.ReadFailed reason
            | None, Some document -> GateYardDocumentRead.Found document
            | None, None -> GateYardDocumentRead.Missing

        member this.Write(operation, expected, document) =
            match this.Fault with
            | Some fault ->
                this.Fault <- None
                fault operation document
            | None ->
                let current = stored |> Option.bind _.Version
                if current = expected then GateYardDocumentWrite.Written(this.Land(operation, document))
                else GateYardDocumentWrite.VersionMismatch current

let private gate1 = GateYardId "gate-1"
let private save1 = GateYardOperationId "save-1"
let private rules = { GateYardRules.Guard = (fun _ _ _ -> true); Select_Closed_Raise = (fun () -> GateYardBranch_Closed_Raise.Open) }

let private session (outcome: GateYardDocumentLoaded) =
    match outcome with
    | GateYardDocumentLoaded.Loaded s | GateYardDocumentLoaded.DocumentMissing s -> s
    | other -> failwithf "%A" other

let private created (outcome: GateYardCreated) =
    match outcome with
    | GateYardCreated.Created s -> s
    | other -> failwithf "%A" other

let private applied (outcome: GateYardCommandOutcome) =
    match outcome with
    | GateYardCommandOutcome.Applied(s, _) -> s
    | other -> failwithf "%A" other

/// A yard holding gate-1, closed, stored at version 1 by an earlier save.
let private seeded () =
    let yard = Yard None
    let s = GateYardDocuments.create (session (GateYardDocuments.load yard)) gate1 |> created
    match GateYardDocuments.save yard s (GateYardOperationId "seed") with
    | GateYardSaveOutcome.Saved _, _ -> ()
    | other -> failwithf "%A" other
    yard

let private raised (yard: Yard) =
    GateYardDocuments.apply rules (session (GateYardDocuments.load yard)) gate1 None GateYardCommand.Raise |> applied

[<Fact>]
let ``a missing document starts empty, and its first save creates it expecting no version`` () =
    let yard = Yard None
    match GateYardDocuments.load yard with
    | GateYardDocumentLoaded.DocumentMissing s -> Assert.Equal(None, s.Stored.Version)
    | other -> failwithf "%A" other
    Assert.Equal(Some 1L, (seeded ()).Stored |> Option.bind _.Version)

[<Fact>]
let ``commands change the document in memory only, and one save writes it once`` () =
    let yard = seeded ()
    let s = raised yard
    Assert.Equal(1, yard.Writes)
    Assert.Equal(GateYardState.Open, s.Working.Entries[gate1].State)
    Assert.Equal(1L, s.Working.Entries[gate1].Revision)
    Assert.Equal(GateYardState.Closed, s.Stored.Entries[gate1].State)
    match GateYardDocuments.save yard s save1 with
    | GateYardSaveOutcome.Saved(operation, 2L), after ->
        Assert.Equal(save1, operation)
        Assert.Equal(after.Stored, after.Working)
        Assert.Equal(Some 2L, Map.tryFind save1 yard.Stored.Value.Applied)
    | other -> failwithf "%A" other
    Assert.Equal(2, yard.Writes)

[<Fact>]
let ``a missing entry and a stale entry revision are refused with their codes`` () =
    let s = session (GateYardDocuments.load (seeded ()))
    let missing = GateYardDocuments.apply rules s (GateYardId "gate-9") None GateYardCommand.Raise
    Assert.Equal(GateYardCommandOutcome.EntryNotFound(GateYardId "gate-9"), missing)
    Assert.Equal(Some GateYardRefusalCode.NoSuchGate, GateYardDocuments.commandCode missing)
    let stale = GateYardDocuments.apply rules s gate1 (Some 3L) GateYardCommand.Raise
    Assert.Equal(GateYardCommandOutcome.EntryStale(gate1, 3L, 0L), stale)
    Assert.Equal(Some GateYardRefusalCode.StaleEntry, GateYardDocuments.commandCode stale)
    match GateYardDocuments.apply rules s gate1 (Some 0L) GateYardCommand.Raise with
    | GateYardCommandOutcome.Applied _ -> ()
    | other -> failwithf "%A" other
    Assert.Equal(GateYardCreated.AlreadyExists gate1, GateYardDocuments.create s gate1)

[<Fact>]
let ``a refused command reports its own code and changes nothing`` () =
    let s = session (GateYardDocuments.load (seeded ()))
    let refused = GateYardDocuments.apply rules s gate1 None GateYardCommand.Lower
    match refused with
    | GateYardCommandOutcome.Refused(_, entry) -> Assert.Equal(s.Working.Entries[gate1], entry)
    | other -> failwithf "%A" other
    Assert.Equal(Some GateYardRefusalCode.NotAccepted, GateYardDocuments.commandCode refused)

[<Fact>]
let ``a save that already landed reports the version it produced, after reload, without writing`` () =
    let yard = seeded ()
    GateYardDocuments.save yard (raised yard) save1 |> ignore
    let reloaded = session (GateYardDocuments.load yard)
    match GateYardDocuments.save yard reloaded save1 with
    | GateYardSaveOutcome.AlreadySaved(operation, 2L), _ -> Assert.Equal(save1, operation)
    | other -> failwithf "%A" other
    Assert.Equal(2, yard.Writes)

[<Fact>]
let ``retrying a confirmed save in the same session reports it saved and does not write again`` () =
    let yard = seeded ()
    let _, saved = GateYardDocuments.save yard (raised yard) save1
    Assert.Equal(GateYardSaveOutcome.AlreadySaved(save1, 2L), fst (GateYardDocuments.save yard saved save1))
    Assert.Equal(2, yard.Writes)

[<Fact>]
let ``an unknown save is returned as unknown and blocks every further change until reconciled`` () =
    let yard = seeded ()
    yard.Fault <- Some(fun _ _ -> GateYardDocumentWrite.WriteFailed { Reason = "timeout"; ProvenNotApplied = false })
    let outcome, pending = GateYardDocuments.save yard (raised yard) save1
    Assert.Equal(GateYardSaveOutcome.OutcomeUnknown save1, outcome)
    Assert.Equal(Some save1, pending.Pending)
    Assert.Equal(GateYardCommandOutcome.Unreconciled save1, GateYardDocuments.apply rules pending gate1 None GateYardCommand.Lower)
    Assert.Equal(GateYardCreated.Unreconciled save1, GateYardDocuments.create pending (GateYardId "gate-2"))
    Assert.Equal(GateYardSaveOutcome.Unreconciled save1, fst (GateYardDocuments.save yard pending save1))
    Assert.Equal(GateYardSaveOutcome.Unreconciled save1, fst (GateYardDocuments.save yard pending (GateYardOperationId "save-2")))
    Assert.Equal(1, yard.Writes)

[<Fact>]
let ``reconciling an unknown save that landed reports it applied, and a retry does not write again`` () =
    let yard = seeded ()
    yard.Fault <- Some(fun operation document ->
        yard.Land(operation, document) |> ignore
        GateYardDocumentWrite.WriteFailed { Reason = "timeout after commit"; ProvenNotApplied = false })
    let _, pending = GateYardDocuments.save yard (raised yard) save1
    let result, settled = GateYardDocuments.reconcile yard pending save1
    Assert.Equal(GateYardReconciled.Applied 2L, result)
    Assert.Equal(None, settled.Pending)
    Assert.Equal(GateYardState.Open, settled.Stored.Entries[gate1].State)
    Assert.Equal(GateYardSaveOutcome.AlreadySaved(save1, 2L), fst (GateYardDocuments.save yard settled save1))
    Assert.Equal(2, yard.Writes)

[<Fact>]
let ``reconciling an unknown save that did not land reports it not applied, so a retry writes exactly once`` () =
    let yard = seeded ()
    yard.Fault <- Some(fun _ _ -> GateYardDocumentWrite.WriteFailed { Reason = "timeout before commit"; ProvenNotApplied = false })
    let _, pending = GateYardDocuments.save yard (raised yard) save1
    let result, settled = GateYardDocuments.reconcile yard pending save1
    Assert.Equal(GateYardReconciled.NotApplied, result)
    Assert.Equal(GateYardState.Open, settled.Working.Entries[gate1].State)
    match GateYardDocuments.save yard settled save1 with
    | GateYardSaveOutcome.Saved(_, 2L), _ -> ()
    | other -> failwithf "%A" other
    Assert.Equal(2, yard.Writes)

[<Fact>]
let ``an unknown save overtaken by another writer reconciles as a conflict, the plan's choice`` () =
    let yard = seeded ()
    yard.Fault <- Some(fun _ document ->
        yard.Land(GateYardOperationId "someone-else", document) |> ignore
        GateYardDocumentWrite.WriteFailed { Reason = "lost"; ProvenNotApplied = false })
    let _, pending = GateYardDocuments.save yard (raised yard) save1
    let result, settled = GateYardDocuments.reconcile yard pending save1
    Assert.Equal(GateYardReconciled.ReconcileConflict(Some 2L), result)
    Assert.Equal(None, settled.Pending)

[<Fact>]
let ``a failed read leaves the save unknown and still blocking`` () =
    let yard = seeded ()
    yard.Fault <- Some(fun _ _ -> GateYardDocumentWrite.WriteFailed { Reason = "timeout"; ProvenNotApplied = false })
    let _, pending = GateYardDocuments.save yard (raised yard) save1
    yard.ReadFault <- Some "unreachable"
    let result, still = GateYardDocuments.reconcile yard pending save1
    Assert.Equal(GateYardReconciled.StillUnknown "unreachable", result)
    Assert.Equal(Some save1, still.Pending)
    Assert.Equal(GateYardDocumentLoaded.LoadFailed "unreachable", GateYardDocuments.load yard)

[<Fact>]
let ``a moved version is refused with its code, and a proven failure is definitely not saved`` () =
    let yard = seeded ()
    let s = raised yard
    yard.Land(GateYardOperationId "someone-else", yard.Stored.Value) |> ignore
    let conflict, unchanged = GateYardDocuments.save yard s save1
    Assert.Equal(GateYardSaveOutcome.SaveConflict(Some 2L), conflict)
    Assert.Equal(Some GateYardRefusalCode.YardMoved, GateYardDocuments.saveCode conflict)
    Assert.Equal(s, unchanged)
    let fresh = raised yard
    yard.Fault <- Some(fun _ _ -> GateYardDocumentWrite.WriteFailed { Reason = "quota"; ProvenNotApplied = true })
    let failed, kept = GateYardDocuments.save yard fresh save1
    Assert.Equal(GateYardSaveOutcome.NotSaved "quota", failed)
    Assert.Equal(None, kept.Pending)
    match GateYardDocuments.save yard kept save1 with
    | GateYardSaveOutcome.Saved(_, 3L), _ -> ()
    | other -> failwithf "%A" other

/// Session scope: saved operation ids are remembered by the session only.
type private SessionYardStore(write: SessionYardDocumentWrite, read: SessionYardDocumentRead) =
    member val Writes = 0 with get, set
    interface ISessionYardDocumentStore with
        member _.Read() = read
        member this.Write(_, _, _) =
            this.Writes <- this.Writes + 1
            write

[<Fact>]
let ``session scope deduplicates within a session only, and escalates what it cannot settle`` () =
    let document = { SessionYardDocument.Version = Some 1L; Entries = Map.empty }
    let op = SessionYardOperationId "save"
    let store = SessionYardStore(SessionYardDocumentWrite.Written 2L, SessionYardDocumentRead.Found document)
    let first = SessionYardDocuments.start document
    let _, saved = SessionYardDocuments.save store first op
    Assert.Equal(SessionYardSaveOutcome.AlreadySaved(op, 2L), fst (SessionYardDocuments.save store saved op))
    Assert.Equal(1, store.Writes)
    // A new session remembers nothing: the plan chose session scope, so this
    // retry writes again. That is the documented meaning of the choice.
    SessionYardDocuments.save store first op |> ignore
    Assert.Equal(2, store.Writes)
    let moved = SessionYardStore(SessionYardDocumentWrite.VersionMismatch(Some 5L), SessionYardDocumentRead.Found { document with Version = Some 5L })
    match SessionYardDocuments.save moved first op with
    | SessionYardSaveOutcome.Escalated _, _ -> ()
    | other -> failwithf "%A" other
    match SessionYardDocuments.reconcile moved { first with Pending = Some op } op with
    | SessionYardReconciled.Escalated _, s -> Assert.Equal(Some op, s.Pending)
    | other -> failwithf "%A" other

[<Fact>]
let ``an unresolved scope deduplicates nothing, leaves a moved document unresolved, and records the open questions`` () =
    let document = { OpenYardDocument.Version = Some 1L; Entries = Map.empty }
    let op = OpenYardOperationId "save"
    let moved =
        { new IOpenYardDocumentStore with
            member _.Read() = OpenYardDocumentRead.Found { document with Version = Some 2L }
            member _.Write(_, _, _) = OpenYardDocumentWrite.VersionMismatch(Some 2L) }
    let pending = { OpenYardDocuments.start document with Pending = Some op }
    match OpenYardDocuments.reconcile moved pending op with
    | OpenYardReconciled.Unresolved _, _ -> ()
    | other -> failwithf "%A" other
    match OpenYardDocuments.save moved (OpenYardDocuments.start document) op with
    | OpenYardSaveOutcome.Unresolved _, _ -> ()
    | other -> failwithf "%A" other
    let ids = (construction openYard).Obligations |> List.map _.Id
    Assert.Contains("RESOLVE-DOCUMENT:idempotency", ids)
    Assert.Contains("RESOLVE-DOCUMENT:onSaveConflict", ids)
    Assert.Contains("RESOLVE-DOCUMENT:onReconcileMoved", ids)

[<Fact>]
let ``the document store is handed off as implement obligations`` () =
    let ids = (construction gateYard).Obligations |> List.map _.Id
    Assert.Contains("IMPLEMENT:IGateYardDocumentStore.Read", ids)
    Assert.Contains("IMPLEMENT:IGateYardDocumentStore.Write", ids)
    Assert.DoesNotContain("RESOLVE-DOCUMENT:idempotency", ids)

[<Fact>]
let ``a decided save conflict and an open reconcile-moved case generate separately`` () =
    let d = draft gateYard
    let openMoved =
        { d with
            Operations =
                d.Operations |> List.map (function DeclareDocument p -> DeclareDocument { p with OnReconcileMoved = ReconcileMovedAction.Unresolved } | other -> other) }
    let c = validate (prepared gateYard) openMoved |> ok "validate" |> construct |> ok "construct"
    let document = (c.Artifacts |> List.find (fun a -> a.Path.EndsWith ".Document.g.fs")).Content
    Assert.Contains("| GateYardDocumentWrite.VersionMismatch current -> GateYardSaveOutcome.SaveConflict current, session", document)
    Assert.Contains("| None -> GateYardReconciled.Unresolved(", document)
    Assert.DoesNotContain("ReconcileConflict", document)
    let ids = c.Obligations |> List.map _.Id
    Assert.Contains("RESOLVE-DOCUMENT:onReconcileMoved", ids)
    Assert.DoesNotContain("RESOLVE-DOCUMENT:onSaveConflict", ids)

let private references =
    [ typeof<ValidatedPlan>.Assembly.Location
      typeof<PlannedWrite>.Assembly.Location ]

let private tiers (fixture: Fixture) (workflow: string) =
    [ "Model"; "Transition"; "Document" ]
    |> List.map (fun tier -> sprintf "#load @\"%s\"" (goldenPath fixture (sprintf "generated/%s/%s.%s.g.fs" workflow workflow tier)))
    |> String.concat "\n"
    |> fun loads -> loads + sprintf "\nopen Generated.%s\n" workflow

[<Fact>]
let ``merging or overwriting a moved document cannot be expressed, and scopes keep their own records`` () =
    let durable = tiers gateYard "GateYard"
    let session = tiers sessionYard "SessionYard"
    let open' = tiers openYard "OpenYard"
    let failures =
        Ordo.Tests.FsiProbe.check references
            [ "positive control", durable + "let x = GateYardSaveOutcome.OutcomeUnknown(GateYardOperationId \"s\")", None
              "no merged save", durable + "let x = GateYardSaveOutcome.Merged", Some "FS0039"
              "no overwriting save", durable + "let x = GateYardSaveOutcome.Overwritten", Some "FS0039"
              "an unknown save cannot be read as saved", durable + "let x : GateYardSaveOutcome = GateYardSaveOutcome.OutcomeUnknown(GateYardOperationId \"s\", 2L)", Some "FS0001"
              "session documents carry no applied ids",
              session + "let d = { SessionYardDocument.Version = None; Entries = Map.empty; Applied = Map.empty }", Some "FS0039"
              "an unresolved scope has no already-saved outcome", open' + "let x = OpenYardSaveOutcome.AlreadySaved", Some "FS0039"
              "an unresolved scope cannot report a save applied", open' + "let x = OpenYardReconciled.Applied 1L", Some "FS0039" ]
    Assert.True(List.isEmpty failures, String.concat "\n\n" failures)
