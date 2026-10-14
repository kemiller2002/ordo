/// CONSTRUCT-010 phase 2, increment 1, in generated code: typed refusal codes
/// and branching transitions (`ordo.construction/4`). The Gate golden is a
/// synthetic vocabulary 4 plan; its sources are test input only.
module Ordo.Construction.Tests.CodedBranchingConstructionTests

open System
open Xunit
open Generated.Gate
open Ordo.Core.ConstructionPlan
open Ordo.Construction.Artifacts
open Ordo.Construction.Construction
open Ordo.Construction.Tests.Fixtures

let private states = [ GateState.Closed; GateState.Open; GateState.Jammed ]
let private commands = [ GateCommand.Raise; GateCommand.Lower; GateCommand.Reset ]

let private rules pathClear selected =
    { Guard = (fun GateGuard.PathClear _ _ -> pathClear)
      Select_Closed_Raise = (fun () -> selected) }

[<Fact>]
let ``a branching transition goes exactly where its selector picks, and only there`` () =
    for selected, expected in [ GateBranch_Closed_Raise.Open, GateState.Open; GateBranch_Closed_Raise.Jammed, GateState.Jammed ] do
        match GateTransition.decide (rules true selected) GateState.Closed GateCommand.Raise with
        | Ok d -> Assert.Equal(expected, d.Next)
        | Error e -> failwithf "Raise from Closed refused: %A" e
    Assert.Equal<(GateState * GateCommand * GateState list) list>(
        [ GateState.Closed, GateCommand.Raise, [ GateState.Jammed; GateState.Open ] ], GateTransition.branching)

[<Fact>]
let ``the selector is consulted only for its own pair`` () =
    let mutable calls = 0
    let counting = { rules true GateBranch_Closed_Raise.Open with Select_Closed_Raise = (fun () -> calls <- calls + 1; GateBranch_Closed_Raise.Open) }
    for state in states do
        for command in commands do
            GateTransition.decide counting state command |> ignore
    Assert.Equal(1, calls)

/// GATE-4: raising an open gate is AlreadyOpen, a blocked path is
/// Obstructed, any other command the gate does not accept is NotAccepted.
[<Fact>]
let ``every refusal reports the code the plan assigns`` () =
    let legal = set [ GateState.Closed, GateCommand.Raise; GateState.Open, GateCommand.Lower; GateState.Jammed, GateCommand.Reset ]
    for state in states do
        for command in commands do
            match GateTransition.decide (rules true GateBranch_Closed_Raise.Open) state command with
            | Ok _ -> Assert.True(Set.contains (state, command) legal, sprintf "%A %A accepted" state command)
            | Error refusal ->
                Assert.False(Set.contains (state, command) legal)
                let expected = if (state, command) = (GateState.Open, GateCommand.Raise) then GateRefusalCode.AlreadyOpen else GateRefusalCode.NotAccepted
                Assert.Equal(Some expected, GateTransition.refusalCode refusal)
    match GateTransition.decide (rules false GateBranch_Closed_Raise.Open) GateState.Open GateCommand.Lower with
    | Error refusal -> Assert.Equal(Some GateRefusalCode.Obstructed, GateTransition.refusalCode refusal)
    | Ok _ -> failwith "a blocked path lowered the gate"

[<Fact>]
let ``legal pairs carry no code`` () =
    for state, command in [ GateState.Closed, GateCommand.Raise; GateState.Open, GateCommand.Lower; GateState.Jammed, GateCommand.Reset ] do
        Assert.Equal(None, GateTransition.pairCode state command)

[<Fact>]
let ``the manifest lists the branching pair, its targets and the selector contract`` () =
    let c = construction gate
    let check = c.StructuralChecks |> List.find (fun k -> k.State = "Closed" && k.Command = "Raise")
    Assert.Equal<string list>([ "Jammed"; "Open" ], check.Targets)
    Assert.Equal(None, check.Expected)
    let manifest = (c.Artifacts |> List.last).Content
    Assert.Contains("\"expected\": \"branching\"", manifest)
    Assert.Contains("\"kind\": \"selection-function\"", manifest)
    Assert.Contains("IMPLEMENT:GateRules.Select/Closed/Raise", c.Obligations |> List.map _.Id)
    Assert.Equal(GeneratorVersion4, c.GeneratorVersion)

/// A selector whose decision point is undecided is not implementable yet.
[<Fact>]
let ``a selector with an undecided point gets a decide obligation, not implement`` () =
    let d = draft gate
    let reopened =
        d.Operations
        |> List.map (fun op ->
            match op with
            | DeclareExtensionInventory({ Extension = ExtensionRef.Selection _ } as i) ->
                DeclareExtensionInventory
                    { i with DecisionPoints = i.DecisionPoints |> List.map (fun p -> { p with Resolution = DecisionPointResolution.Undecided "Which report means jammed?" }) }
            | other -> other)
    let c = validate (prepared gate) { d with Operations = reopened } |> ok "validate" |> construct |> ok "construct"
    let ids = c.Obligations |> List.map _.Id
    Assert.Contains("DECIDE:GateRules.Select/Closed/Raise/MotorReport", ids)
    Assert.DoesNotContain("IMPLEMENT:GateRules.Select/Closed/Raise", ids)

/// Without codes or branching, generator 4 is generator 3 with a new name:
/// nothing a vocabulary 3 plan could say changes meaning.
[<Fact>]
let ``a vocabulary 4 plan without codes or branching generates vocabulary 3 code`` () =
    let d = draft timerOpen
    let v3 = construction timerOpen
    let v4 =
        validate (prepared timerOpen) { d with VocabularyVersion = VocabularyVersion4 } |> ok "validate" |> construct |> ok "construct"
    Assert.Equal(GeneratorVersion4, v4.GeneratorVersion)
    // The F# tiers; the manifest differs by the digests of those files.
    let tiers (c: Construction) = c.Artifacts |> List.filter _.Tier.IsSome
    Assert.Equal(3, (tiers v4).Length)
    for a3, a4 in List.zip (tiers v3) (tiers v4) do
        let neutral (text: string) (plan: string) (generator: string) = text.Replace(plan, "PLAN").Replace(generator, "GENERATOR")
        Assert.Equal(neutral a3.Content v3.PlanDigest GeneratorVersion3, neutral a4.Content v4.PlanDigest GeneratorVersion4)

[<Fact>]
let ``a refusal code the engine cannot place is refused with a typed reason`` () =
    let d = draft gate
    let renamed =
        d.Operations
        |> List.map (fun op ->
            match op with
            | DefineRefusalCode c when c.Code = "NotAccepted" -> DefineRefusalCode { c with Code = "None" }
            | AssignRefusalCode a when a.Code = "NotAccepted" -> AssignRefusalCode { a with Code = "None" }
            | other -> other)
    let v = validate (prepared gate) { d with Operations = renamed } |> ok "validate"
    Assert.Equal(Error [ Naming(Ordo.Construction.Naming.ReservedName("refusal code", "None")) ], construct v)

let private references =
    [ typeof<ValidatedPlan>.Assembly.Location
      typeof<PlannedWrite>.Assembly.Location ]

let private gateTiers =
    [ "Model"; "Transition" ]
    |> List.map (fun tier -> sprintf "#load @\"%s\"" (goldenPath gate (sprintf "generated/Gate/Gate.%s.g.fs" tier)))
    |> String.concat "\n"

[<Fact>]
let ``a selector cannot pick a state its transition does not allow, and cannot be omitted`` () =
    let failures =
        Ordo.Tests.FsiProbe.check references
            [ "positive control",
              gateTiers + "\nopen Generated.Gate\nlet r : GateRules = { Guard = (fun _ _ _ -> true); Select_Closed_Raise = (fun () -> GateBranch_Closed_Raise.Jammed) }", None
              "selector returns an arbitrary state",
              gateTiers + "\nopen Generated.Gate\nlet r : GateRules = { Guard = (fun _ _ _ -> true); Select_Closed_Raise = (fun () -> GateState.Closed) }", Some "FS0001"
              "selector names a target the plan does not list",
              gateTiers + "\nopen Generated.Gate\nlet t = GateBranch_Closed_Raise.Closed", Some "FS0039"
              "selector omitted",
              gateTiers + "\nopen Generated.Gate\nlet r : GateRules = { Guard = (fun _ _ _ -> true) }", Some "FS0764" ]
    Assert.True(List.isEmpty failures, String.concat "\n\n" failures)

/// Increment 1b: the same gate, generated from decision records version 3
/// instead of a hand-authored plan, constructs vocabulary 4 code.
[<Fact>]
let ``a default plan from version 3 records constructs codes and a selector`` () =
    let records =
        System.IO.File.ReadAllText(System.IO.Path.Combine(repositoryRoot, "tests", "fixtures", "construction", "gate-synthetic", "construction-decisions-v3.json"))
        |> Ordo.Core.ConstructionDecisions.readDecisionRecords |> ok "decisions"
    let plan =
        Ordo.Core.ConstructionDefaultPlan.generateDefaultPlan (prepared gate) records Ordo.Core.ConstructionDefaultPlan.ConstructionVocabulary.Version4
        |> ok "default plan"
    let c = construct plan.ValidatedPlan |> ok "construct"
    Assert.Equal(GeneratorVersion4, c.GeneratorVersion)
    let transition = (c.Artifacts |> List.find (fun a -> a.Tier = Some Transition)).Content
    Assert.Contains("| GateState.Open, GateCommand.Raise -> Some GateRefusalCode.AlreadyOpen", transition)
    Assert.Contains("| GateState.Jammed, GateCommand.Lower -> Some GateRefusalCode.NotAccepted", transition)
    Assert.Contains("Select_Closed_Raise: unit -> GateBranch_Closed_Raise", transition)
    Assert.Contains("IMPLEMENT:GateRules.Select/Closed/Raise", c.Obligations |> List.map _.Id)
