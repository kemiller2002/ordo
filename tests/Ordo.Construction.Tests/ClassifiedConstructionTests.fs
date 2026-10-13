/// CONSTRUCT-010 phase 4 in generated code. The TimerOpen golden is the real
/// timer cohort generated in vocabulary 3 with unlisted pairs left undecided
/// and the rounding tie open. Generated code reports an undecided pair as
/// `Undecided`, never as a refusal, and an extension with an open decision
/// point is never handed off for implementation.
module Ordo.Construction.Tests.ClassifiedConstructionTests

open System
open Xunit
open Generated.TimerOpen
open Ordo.Core.ConstructionClassification
open Ordo.Construction.Artifacts
open Ordo.Construction.Construction
open Ordo.Construction.Tests.Fixtures

/// Pairs the architecture note decides (ARCH L116-L129). Every other pair is
/// left open by these decision records, deliberately.
let private decided =
    [ TimerOpenState.NoTimer, TimerOpenCommand.Start, TimerOpenState.Running
      TimerOpenState.Running, TimerOpenCommand.Pause, TimerOpenState.Paused
      TimerOpenState.Paused, TimerOpenCommand.Resume, TimerOpenState.Running
      TimerOpenState.Running, TimerOpenCommand.Stop, TimerOpenState.NoTimer
      TimerOpenState.Paused, TimerOpenCommand.Stop, TimerOpenState.NoTimer ]

[<Fact>]
let ``an undecided pair is reported as undecided, never refused by omission`` () =
    let states = [ TimerOpenState.NoTimer; TimerOpenState.Running; TimerOpenState.Paused ]
    let commands = [ TimerOpenCommand.Start; TimerOpenCommand.Pause; TimerOpenCommand.Resume; TimerOpenCommand.Stop ]
    for state in states do
        for command in commands do
            match decided |> List.tryFind (fun (s, c, _) -> s = state && c = command), TimerOpenTransition.decide state command with
            | Some(_, _, next), Ok d -> Assert.Equal(next, d.Next)
            | None, Error(TimerOpenRefusal.Undecided(s, c)) ->
                Assert.Equal(state, s)
                Assert.Equal(command, c)
            | expected, actual -> failwithf "%A %A: expected %A, got %A" state command expected actual

[<Fact>]
let ``no pair of the open timer is refused as an invalid transition`` () =
    let refusedAsInvalid =
        [ for s in [ TimerOpenState.NoTimer; TimerOpenState.Running; TimerOpenState.Paused ] do
            for c in [ TimerOpenCommand.Start; TimerOpenCommand.Pause; TimerOpenCommand.Resume; TimerOpenCommand.Stop ] do
                match TimerOpenTransition.decide s c with
                | Error(TimerOpenRefusal.InvalidTransition _) -> yield s, c
                | _ -> () ]
    Assert.Empty(refusedAsInvalid)

[<Fact>]
let ``the rounding tie is a decide obligation and Stop is not handed off as implement`` () =
    let c = construction timerOpen
    let ids = c.Obligations |> List.map _.Id
    Assert.Contains("DECIDE:ITimerOpenPorts.Perform/Stop/RoundingTie", ids)
    Assert.DoesNotContain("IMPLEMENT:ITimerOpenPorts.Perform/Stop", ids)
    Assert.Contains("IMPLEMENT:ITimerOpenPorts.CurrentTime", ids)
    Assert.Equal(7, ids |> List.filter (fun id -> id.StartsWith "RESOLVE-PAIR:") |> List.length)

[<Fact>]
let ``structural checks label open pairs as undecided, not invalid`` () =
    let c = construction timerOpen
    Assert.Equal(7, c.StructuralChecks |> List.filter _.Undecided |> List.length)
    Assert.All(c.StructuralChecks |> List.filter _.Undecided, fun check -> Assert.True(check.Expected.IsNone))
    let manifest = c.Artifacts |> List.find (fun a -> a.Path.EndsWith "construction-manifest.json")
    Assert.Contains("\"expected\": \"undecided\"", manifest.Content)
    Assert.Contains("\"kind\": \"decide-extension\"", manifest.Content)

[<Fact>]
let ``the open timer is not a complete plan`` () =
    Assert.True(Result.isError (complete (validated timerOpen)))

[<Fact>]
let ``earlier vocabularies keep their generator and their bytes`` () =
    Assert.Equal(GeneratorVersion, (construction timer).GeneratorVersion)
    Assert.Equal(GeneratorVersion2, (construction timerRemain).GeneratorVersion)
    Assert.Equal(GeneratorVersion3, (construction timerOpen).GeneratorVersion)
    for fixture in [ timer; timerRemain ] do
        for a in (construction fixture).Artifacts |> List.filter (fun a -> a.Tier.IsSome) do
            Assert.DoesNotContain("Undecided of state", a.Content)
            Assert.DoesNotContain("let undecided", a.Content)
