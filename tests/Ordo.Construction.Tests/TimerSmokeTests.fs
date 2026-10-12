/// Phase 5 smoke test: generated time-entry timer structure checked against
/// expectations written by hand from the ORIGINAL requirement text, not from
/// the plan and not from the time-entry F# code. Sources are
/// kemiller2002/time-entry-state-machine@73a63f1:
///   ARCH = docs/architecture/business-activity-ledger-state-model.md
///   RECON = requirements-reconstruction/fsharp-business-requirements-beyond-csharp.md
///   REQS = effort-experiment/f-sharp-state/REQUIREMENTS.md
/// This checks structure. It is not independent behavioral acceptance of the
/// time-entry application, whose arithmetic remains handwritten obligations.
module Ordo.Construction.Tests.TimerSmokeTests

open System
open Xunit
open Generated.Timer
open Ordo.Construction.Artifacts
open Ordo.Construction.Tests.Fixtures

/// (state, command) -> expected next state, or None for refusal.
let private expectations =
    [ // ARCH L117 "Idle --start--> Running"; ARCH L126-127 "`start` requires `NoTimer`"
      TimerState.NoTimer, TimerCommand.Start, Some TimerState.Running
      // REQS L367 "a timer command issued with no active timer" is rejected
      TimerState.NoTimer, TimerCommand.Pause, None
      TimerState.NoTimer, TimerCommand.Resume, None
      TimerState.NoTimer, TimerCommand.Stop, None
      // RECON L104 "Only one timer may be active at a time."; ARCH L126-127
      TimerState.Running, TimerCommand.Start, None
      TimerState.Paused, TimerCommand.Start, None
      // ARCH L119/L127 "`pause` requires `TimerRunning`"
      TimerState.Running, TimerCommand.Pause, Some TimerState.Paused
      // REQS L367-368 "issued against a timer already in the wrong run/pause state"
      TimerState.Paused, TimerCommand.Pause, None
      TimerState.Running, TimerCommand.Resume, None
      // ARCH L119/L128 "`resume` requires `TimerPaused`"
      TimerState.Paused, TimerCommand.Resume, Some TimerState.Running
      // ARCH L117/L121 "--stop--> (produces TimeEntry, back to Idle)"; ARCH L128-129 "`stop` requires either status"
      TimerState.Running, TimerCommand.Stop, Some TimerState.NoTimer
      TimerState.Paused, TimerCommand.Stop, Some TimerState.NoTimer ]

[<Fact>]
let ``the independent table covers every state and command exactly once`` () =
    let pairs = expectations |> List.map (fun (s, c, _) -> s, c)
    Assert.Equal(12, List.length (List.distinct pairs))
    // Four commands, five legal transitions: Stop is legal from two states.
    Assert.Equal(5, expectations |> List.filter (fun (_, _, e) -> e.IsSome) |> List.length)

[<Fact>]
let ``generated transitions agree with the requirement text for all twelve pairs`` () =
    for state, command, expected in expectations do
        Assert.Equal(expected, TimerTransition.target state command)
        match TimerTransition.decide state command, expected with
        | Ok decision, Some next ->
            Assert.Equal(next, decision.Next)
            Assert.Equal(state, decision.Previous)
        | Error(TimerRefusal.InvalidTransition(s, c)), None ->
            Assert.Equal(state, s)
            Assert.Equal(command, c)
        | actual, _ -> failwithf "%A %A: got %A, expected %A" state command actual expected

[<Fact>]
let ``a timer can be started again immediately after stopping`` () =
    // STATE-MODEL (REQS sibling) and ARCH L117: stop returns to Idle, from which start is legal.
    let run commands =
        commands |> List.fold (fun state command -> state |> Result.bind (fun s -> TimerTransition.decide s command |> Result.map _.Next)) (Ok TimerState.NoTimer)
    Assert.Equal(Ok TimerState.Running, run [ TimerCommand.Start; TimerCommand.Pause; TimerCommand.Resume; TimerCommand.Stop; TimerCommand.Start ])
    Assert.Equal(Error(TimerRefusal.InvalidTransition(TimerState.Running, TimerCommand.Start)), run [ TimerCommand.Start; TimerCommand.Start ])

/// A test host: a fixed clock and a recorder for requested effects.
type private RecordingHost(now: DateTimeOffset, outcome: TimerEffectOutcome) =
    let performed = Collections.Generic.List<TimerEffect * TimerDecision>()
    member _.Performed = List.ofSeq performed
    interface ITimerPorts with
        member _.CurrentTime() = now
        member _.Perform(effect, decision) =
            performed.Add((effect, decision))
            outcome

let private at = DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero)

[<Fact>]
let ``only stop requests the completion effect, and the host performs it`` () =
    // ARCH L131-144: stop completes by creating the entry through the shared path (handwritten host effect).
    let host = RecordingHost(at, TimerEffectOutcome.Completed)
    match TimerOrchestration.handle host TimerState.Paused TimerCommand.Stop with
    | TimerResult.Accepted(decision, time, effects) ->
        Assert.Equal(at, time)
        Assert.Equal(TimerState.NoTimer, decision.Next)
        Assert.Equal<(TimerEffect * TimerEffectOutcome) list>([ TimerEffect.Stop, TimerEffectOutcome.Completed ], effects)
    | other -> failwithf "%A" other
    let quiet = RecordingHost(at, TimerEffectOutcome.Completed)
    for state, command in [ TimerState.NoTimer, TimerCommand.Start; TimerState.Running, TimerCommand.Pause; TimerState.Paused, TimerCommand.Resume ] do
        TimerOrchestration.handle quiet state command |> ignore
    Assert.Empty(quiet.Performed)

[<Fact>]
let ``an unknown or failed effect is reported, not resolved by generated code`` () =
    // Whether a refused or unknown completion discards the timer is a declared unresolved obligation.
    for outcome in [ TimerEffectOutcome.Unknown "host lost the response"; TimerEffectOutcome.Failed "overlap" ] do
        match TimerOrchestration.handle (RecordingHost(at, outcome)) TimerState.Running TimerCommand.Stop with
        | TimerResult.Accepted(_, _, [ TimerEffect.Stop, reported ]) -> Assert.Equal(outcome, reported)
        | other -> failwithf "%A" other

[<Fact>]
let ``refused commands perform nothing and still report when they were refused`` () =
    let host = RecordingHost(at, TimerEffectOutcome.Completed)
    Assert.Equal(TimerResult.Refused(TimerRefusal.InvalidTransition(TimerState.NoTimer, TimerCommand.Stop), at), TimerOrchestration.handle host TimerState.NoTimer TimerCommand.Stop)
    Assert.Empty(host.Performed)

[<Fact>]
let ``generated contracts, structural checks and the plan describe the same transitions`` () =
    let c = construction timer
    let w = (Ordo.Core.ConstructionPlan.view (validated timer)).Workflow
    let fromPlan = w.Transitions |> List.map (fun t -> t.From.Key, t.Command.Key, t.To.Key) |> Set.ofList
    let fromChecks = c.StructuralChecks |> List.choose (fun s -> s.Expected |> Option.map (fun e -> s.State, s.Command, e)) |> Set.ofList
    let fromCode = TimerTransition.legal |> List.map (fun (s, cmd, t) -> sprintf "%A" s, sprintf "%A" cmd, sprintf "%A" t) |> Set.ofList
    Assert.Equal<Set<string * string * string>>(fromPlan, fromChecks)
    Assert.Equal<Set<string * string * string>>(fromPlan, fromCode)
    Assert.Equal(12, c.StructuralChecks.Length)

[<Fact>]
let ``custom behavior stays an explicit obligation, never generated`` () =
    let c = construction timer
    let ids = c.Obligations |> List.map _.Id
    Assert.Contains("IMPLEMENT:ITimerPorts.CurrentTime", ids)
    Assert.Contains("IMPLEMENT:ITimerPorts.Perform/Stop", ids)
    Assert.Contains("RESOLVE:StopCompletionRefused", ids)
    Assert.Contains("RESOLVE:TimerScope", ids)
    Assert.Contains("VERIFY:Timer", ids)
    // The unresolved short-stop requirement is outside the cohort: nothing generated claims it.
    for a in c.Artifacts do
        Assert.DoesNotContain(a.RequirementKeys, fun k -> k.EndsWith "#L113-L114")
