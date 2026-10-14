/// CONSTRUCT-010 phase 2, increment 1: plan vocabulary 4 adds typed refusal
/// codes and branching (multi-target) transitions. The fixture is a synthetic
/// gate; its sources are test input, not product requirements.
module Ordo.Tests.ConstructionVocabulary4Tests

open System
open System.IO
open Xunit
open Ordo.Core.Json
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionPlanWire
open Ordo.Core.ConstructionClassification
module ConstructionPlan = Ordo.Core.ConstructionPlan

let private fixtureDirectory () =
    let rec find (current: DirectoryInfo) =
        let candidate = Path.Combine(current.FullName, "tests", "fixtures", "construction", "gate-synthetic")
        if Directory.Exists candidate then candidate
        else
            match current.Parent with
            | null -> failwith "gate fixture missing from repository"
            | parent -> find parent
    find (DirectoryInfo AppContext.BaseDirectory)

let private read name = File.ReadAllText(Path.Combine(fixtureDirectory (), name))
let private ok label value = match value with Ok x -> x | Error e -> failwithf "%s: %A" label e

let private prepared () =
    let manifest = readManifest (read "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read "blueprint.json") |> ok "blueprint"
    prepareCohort manifest blueprint "COH-GATE" (Set.singleton "DEC-GATE") |> ok "prepare"

let private draft () = readPlan (read "plan.v4.json") |> ok "plan"
let private operations () = (draft ()).Operations
let private withOps ops = { draft () with Operations = ops }
let private violations plan =
    match validate (prepared ()) plan with
    | Ok _ -> []
    | Error vs -> vs

let private trace: TraceDraft = { RequirementKeys = [ "synthetic/gate.md#GATE-4" ]; NodeIds = [ "CON-GATE" ] }
let private pair from command: PairDraft = { InState = from; OnCommand = command }
let private assign target code = AssignRefusalCode { Target = target; Code = code; Trace = trace }

[<Fact>]
let ``the gate plan validates and round-trips through the strict reader`` () =
    let v = validate (prepared ()) (draft ()) |> ok "validate"
    let again = readPlan (encodePlan (draft ())) |> ok "reread"
    Assert.Equal(ConstructionPlan.digest v, ConstructionPlan.digest (validate (prepared ()) again |> ok "validate again"))
    let w = (view v).Workflow
    Assert.Equal<string list>([ "AlreadyOpen"; "NotAccepted"; "Obstructed" ], w.RefusalCodes |> List.map (fst >> _.Key))
    Assert.Equal(3, w.CodeAssignments.Length)
    Assert.Equal<string list>([ "Jammed"; "Open" ], (List.exactlyOne w.BranchingTransitions).Targets |> List.map _.Key)
    Assert.Contains(w.Commands, fun c -> c.Key = "Raise")

[<Fact>]
let ``operation order and target order do not change identity`` () =
    let reordered =
        operations ()
        |> List.rev
        |> List.map (function
            | DefineBranchingTransition b -> DefineBranchingTransition { b with Targets = List.rev b.Targets }
            | other -> other)
    Assert.Equal(ConstructionPlan.digest (validate (prepared ()) (draft ()) |> ok "a"), ConstructionPlan.digest (validate (prepared ()) (withOps reordered) |> ok "b"))

[<Fact>]
let ``every vocabulary 4 member is refused in vocabulary 3, by validation and by the reader`` () =
    let v3 = { draft () with VocabularyVersion = VocabularyVersion3 }
    let refused = violations v3 |> List.choose (function NotInVocabulary(_, m, _) -> Some m | _ -> None) |> Set.ofList
    Assert.Equal<Set<string>>(set [ "defineRefusalCode"; "assignRefusalCode"; "defineBranchingTransition"; "selection" ], refused)
    // The reader refuses each one on its own, before validation runs.
    let alone operation = encodePlan { draft () with VocabularyVersion = VocabularyVersion3; Operations = [ operation ] }
    for operation, expected in
        [ DefineRefusalCode { Code = "Stalled"; WireText = None; Trace = trace }, "operation defineRefusalCode is not in"
          assign CodeTarget.Unlisted "Stalled", "operation assignRefusalCode is not in"
          DefineBranchingTransition { Pair = pair "Closed" "Raise"; Targets = [ "Open"; "Jammed" ]; Trace = trace }, "operation defineBranchingTransition is not in"
          DeclareExtensionInventory { Extension = ExtensionRef.Selection("Closed", "Raise"); DecisionPoints = []; Trace = trace }, "extension kind selection is not in" ] do
        match readPlan (alone operation) with
        | Error e -> Assert.Contains(expected + " " + VocabularyVersion3, e)
        | Ok _ -> failwithf "the reader accepted %s in vocabulary 3" (operationLabel operation)

[<Fact>]
let ``a code must be defined before it is assigned`` () =
    let vs = violations (withOps (operations () @ [ assign (CodeTarget.Pair(pair "Jammed" "Raise")) "Stalled" ]))
    Assert.Contains(UnknownRefusalCode("codeFor:pair/Jammed/Raise", "Stalled"), vs)

[<Fact>]
let ``a code goes only to something that refuses`` () =
    let without label = operations () |> List.filter (fun o -> operationLabel o <> label)
    // A legal pair, a branching pair, and a guard no transition uses.
    for target, label in
        [ CodeTarget.Pair(pair "Jammed" "Reset"), "codeFor:pair/Jammed/Reset"
          CodeTarget.Pair(pair "Closed" "Raise"), "codeFor:pair/Closed/Raise"
          CodeTarget.Guard "motor ready", "codeFor:guard/MotorReady" ] do
        Assert.Contains(CodeOnNonRefusal label, violations (withOps (operations () @ [ assign target "NotAccepted" ])))
    // An unlisted code needs a rule that refuses unlisted pairs.
    let undecided =
        without "pairRule"
        @ [ DeclarePairRule { Unlisted = UnlistedPairs.Undecided; Trace = trace } ]
    Assert.Contains(CodeOnNonRefusal "codeFor:unlisted", violations (withOps undecided))

[<Fact>]
let ``one target per code subject`` () =
    let vs = violations (withOps (operations () @ [ assign (CodeTarget.Pair(pair "Open" "Raise")) "NotAccepted" ]))
    Assert.Contains(DuplicateDefinition "codeFor:pair/Open/Raise", vs)

[<Fact>]
let ``a branching transition needs two distinct, known, non-terminal-source targets`` () =
    let replace f = operations () |> List.map (function DefineBranchingTransition b -> DefineBranchingTransition(f b) | other -> other)
    Assert.Contains(BranchingNeedsTwoTargets "branching:Closed/Raise", violations (withOps (replace (fun b -> { b with Targets = [ "Open"; "open" ] }))))
    Assert.Contains(UnknownState("branching:Closed/Raise", "Broken"), violations (withOps (replace (fun b -> { b with Targets = [ "Open"; "Broken" ] }))))
    let terminal =
        operations ()
        |> List.map (function DefineState s when s.Name = "Jammed" -> DefineState { s with Role = Terminal } | other -> other)
        |> List.filter (function DefineTransition t -> t.From <> "Jammed" | _ -> true)
        |> List.map (function DefineBranchingTransition b -> DefineBranchingTransition { b with Pair = pair "Jammed" "Raise"; Targets = [ "Open"; "Closed" ] } | other -> other)
        |> List.map (function
            | DeclareExtensionInventory({ Extension = ExtensionRef.Selection _ } as i) -> DeclareExtensionInventory { i with Extension = ExtensionRef.Selection("Jammed", "Raise") }
            | other -> other)
    Assert.Contains(TransitionFromTerminal "branching:Jammed/Raise", violations (withOps terminal))

[<Fact>]
let ``a pair is a branching transition or something else, never both`` () =
    let both = operations () @ [ DefineTransition { Command = "Raise"; From = "Closed"; To = "Open"; Guards = []; Trace = trace } ]
    Assert.Contains(violations (withOps both), function ConflictingPairDisposition("Closed", "Raise", _) -> true | _ -> false)

[<Fact>]
let ``every selector needs a decision inventory`` () =
    let without = operations () |> List.filter (function DeclareExtensionInventory { Extension = ExtensionRef.Selection _ } -> false | _ -> true)
    Assert.Contains(MissingExtensionInventory "selection/Closed/Raise", violations (withOps without))

[<Fact>]
let ``branching targets count as reachable`` () =
    // Jammed is reachable only through the branching transition.
    let v = validate (prepared ()) (draft ()) |> ok "validate"
    Assert.Contains((view v).Workflow.States, fun s -> s.Name.Key = "Jammed")

[<Fact>]
let ``codes, branching pairs and selectors are classified, and the gate is complete`` () =
    let v = validate (prepared ()) (draft ()) |> ok "validate"
    let c = classify v
    let classOf subject = (c.Choices |> List.find (fun x -> x.Subject = subject)).Class
    Assert.Equal(ChoiceClass.Required [ "synthetic/gate.md#GATE-1"; "synthetic/gate.md#GATE-2" ], classOf "pair:Closed/Raise")
    Assert.Equal(ChoiceClass.Required [ "synthetic/gate.md#GATE-2" ], classOf "extension:selection/Closed/Raise")
    Assert.Equal(ChoiceClass.Required [ "synthetic/gate.md#GATE-4" ], classOf "refusalCode:pair/Open/Raise")
    Assert.Equal(ChoiceClass.Required [ "synthetic/gate.md#GATE-3"; "synthetic/gate.md#GATE-4" ], classOf "refusalCode:guard/PathClear")
    Assert.Contains(c.Choices, fun x -> x.Subject = "refusalCode:unlisted")
    Assert.True(Result.isOk (complete v))
    Assert.Contains("\"kind\": \"refusalCode\"", encodeClassification c)

[<Fact>]
let ``an undecided selector point keeps the plan incomplete`` () =
    let reopened =
        operations ()
        |> List.map (function
            | DeclareExtensionInventory({ Extension = ExtensionRef.Selection _ } as i) ->
                DeclareExtensionInventory
                    { i with DecisionPoints = i.DecisionPoints |> List.map (fun p -> { p with Resolution = DecisionPointResolution.Undecided "Which motor report means jammed?" }) }
            | other -> other)
    match complete (validate (prepared ()) (withOps reopened) |> ok "validate") with
    | Error open' -> Assert.Equal<string list>([ "extension:selection/Closed/Raise/point:MotorReport" ], open' |> List.map _.Subject)
    | Ok _ -> failwith "an undecided selector point was treated as complete"
