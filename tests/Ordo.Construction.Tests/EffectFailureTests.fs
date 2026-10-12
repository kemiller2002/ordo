/// Vocabulary 2 (ordo.construction-vocabulary/2): the construction plan
/// declares what a failed or unknown effect outcome means, and the generated
/// orchestration applies it. Evidence: in the time-entry timer pilot
/// (construction-by-design time-entry-timer-01, finding F2) two of three
/// construction-arm agents decided this silently in handwritten session code
/// by assigning `decision.Next` whatever the outcome. These tests run the
/// compiled goldens of the three timer variants, one per disposition of the
/// Stop effect.
module Ordo.Construction.Tests.EffectFailureTests

open System
open Xunit
open Ordo.Core.ConstructionPlan
open Ordo.Construction.Artifacts
open Ordo.Construction.Tests.Fixtures

let private at = DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero)

/// A host whose Stop effect always reports the given outcome. It counts calls
/// so a test can see that Perform ran exactly once per requested effect.
type private Host<'effect, 'decision, 'outcome>(outcome: 'outcome) =
    let mutable calls = 0
    member _.Calls = calls
    member _.Perform(_: 'effect, _: 'decision) =
        calls <- calls + 1
        outcome

module private Remain =
    open Generated.TimerRemain

    type Ports(outcome: TimerRemainEffectOutcome) =
        let host = Host<TimerRemainEffect, TimerRemainDecision, TimerRemainEffectOutcome>(outcome)
        member _.Calls = host.Calls
        interface ITimerRemainPorts with
            member _.CurrentTime() = at
            member _.Perform(effect, decision) = host.Perform(effect, decision)

    let handle outcome state command =
        let ports = Ports outcome
        TimerRemainOrchestration.handle ports state command, ports.Calls

module private Proceed =
    open Generated.TimerProceed

    type Ports(outcome: TimerProceedEffectOutcome) =
        interface ITimerProceedPorts with
            member _.CurrentTime() = at
            member _.Perform(_, _) = outcome

    let handle outcome state command = TimerProceedOrchestration.handle (Ports outcome) state command

module private Unresolved =
    open Generated.TimerUnresolved

    type Ports(outcome: TimerUnresolvedEffectOutcome) =
        interface ITimerUnresolvedPorts with
            member _.CurrentTime() = at
            member _.Perform(_, _) = outcome

    let handle outcome state command = TimerUnresolvedOrchestration.handle (Ports outcome) state command

// ---- remain ----

open Generated.TimerRemain

[<Fact>]
let ``remain: a failed Stop leaves a running timer Running`` () =
    match Remain.handle (TimerRemainEffectOutcome.Failed "entry overlaps") TimerRemainState.Running TimerRemainCommand.Stop with
    | TimerRemainResult.Remained(decision, time, effects, failure, state), calls ->
        Assert.Equal(TimerRemainState.Running, state)
        Assert.Equal(TimerRemainState.Running, decision.Previous)
        // The rules accepted Stop; the plan's disposition, not the host, keeps the source state.
        Assert.Equal(TimerRemainState.NoTimer, decision.Next)
        Assert.Equal(at, time)
        Assert.Equal(TimerRemainEffectFailure.Failed(TimerRemainEffect.Stop, "entry overlaps"), failure)
        Assert.Equal<(TimerRemainEffect * TimerRemainEffectOutcome) list>([ TimerRemainEffect.Stop, TimerRemainEffectOutcome.Failed "entry overlaps" ], effects)
        Assert.Equal(1, calls)
    | other -> failwithf "expected Remained in Running, got %A" other

[<Fact>]
let ``remain: a failed Stop leaves a paused timer Paused`` () =
    match Remain.handle (TimerRemainEffectOutcome.Failed "entry overlaps") TimerRemainState.Paused TimerRemainCommand.Stop with
    | TimerRemainResult.Remained(_, _, _, _, state), _ -> Assert.Equal(TimerRemainState.Paused, state)
    | other -> failwithf "expected Remained in Paused, got %A" other

[<Fact>]
let ``remain: an unknown Stop outcome also remains, and stays distinct from a failure`` () =
    for source in [ TimerRemainState.Running; TimerRemainState.Paused ] do
        match Remain.handle (TimerRemainEffectOutcome.Unknown "timeout") source TimerRemainCommand.Stop with
        | TimerRemainResult.Remained(_, _, _, failure, state), _ ->
            Assert.Equal(source, state)
            Assert.Equal(TimerRemainEffectFailure.Unknown(TimerRemainEffect.Stop, "timeout"), failure)
            Assert.NotEqual(TimerRemainEffectFailure.Failed(TimerRemainEffect.Stop, "timeout"), failure)
        | other -> failwithf "expected Remained, got %A" other

[<Fact>]
let ``remain: a completed Stop is accepted and reports the target state`` () =
    match Remain.handle TimerRemainEffectOutcome.Completed TimerRemainState.Running TimerRemainCommand.Stop with
    | TimerRemainResult.Accepted(_, _, effects, state), calls ->
        Assert.Equal(TimerRemainState.NoTimer, state)
        Assert.Equal<(TimerRemainEffect * TimerRemainEffectOutcome) list>([ TimerRemainEffect.Stop, TimerRemainEffectOutcome.Completed ], effects)
        Assert.Equal(1, calls)
    | other -> failwithf "expected Accepted, got %A" other

[<Fact>]
let ``remain: a command without an effect is accepted without performing anything`` () =
    match Remain.handle (TimerRemainEffectOutcome.Failed "never asked") TimerRemainState.Running TimerRemainCommand.Pause with
    | TimerRemainResult.Accepted(_, _, effects, state), calls ->
        Assert.Equal(TimerRemainState.Paused, state)
        Assert.Empty(effects)
        Assert.Equal(0, calls)
    | other -> failwithf "expected Accepted, got %A" other

[<Fact>]
let ``remain: a refused command reports the unchanged state and performs nothing`` () =
    match Remain.handle TimerRemainEffectOutcome.Completed TimerRemainState.NoTimer TimerRemainCommand.Stop with
    | TimerRemainResult.Refused(TimerRemainRefusal.InvalidTransition(s, c), time, state), calls ->
        Assert.Equal(TimerRemainState.NoTimer, s)
        Assert.Equal(TimerRemainCommand.Stop, c)
        Assert.Equal(TimerRemainState.NoTimer, state)
        Assert.Equal(at, time)
        Assert.Equal(0, calls)
    | other -> failwithf "expected Refused, got %A" other

[<Fact>]
let ``remain: the generated disposition table says what the plan says`` () =
    Assert.Equal(TimerRemainFailureDisposition.Remain, TimerRemainOrchestration.onFailure TimerRemainEffect.Stop)
    Assert.Equal(None, TimerRemainOrchestration.firstFailure [ TimerRemainEffect.Stop, TimerRemainEffectOutcome.Completed ])

/// A host loop written the way the pilot's handwritten session code was, but
/// reading the state from the result: it cannot discard the timer on failure.
[<Fact>]
let ``remain: a host that keeps the reported state keeps the timer after a failed Stop`` () =
    let stateAfter result =
        match result with
        | TimerRemainResult.Accepted(_, _, _, state)
        | TimerRemainResult.Remained(_, _, _, _, state)
        | TimerRemainResult.Refused(_, _, state) -> state
    let mutable state = TimerRemainState.NoTimer
    for command, outcome in
        [ TimerRemainCommand.Start, TimerRemainEffectOutcome.Completed
          TimerRemainCommand.Pause, TimerRemainEffectOutcome.Completed
          TimerRemainCommand.Stop, TimerRemainEffectOutcome.Failed "entry overlaps" ] do
        state <- stateAfter (fst (Remain.handle outcome state command))
    Assert.Equal(TimerRemainState.Paused, state)

// ---- proceed ----

open Generated.TimerProceed

[<Fact>]
let ``proceed: a failed Stop still reaches the target state and reports the failure`` () =
    for source in [ TimerProceedState.Running; TimerProceedState.Paused ] do
        match Proceed.handle (TimerProceedEffectOutcome.Failed "entry overlaps") source TimerProceedCommand.Stop with
        | TimerProceedResult.Proceeded(decision, _, effects, failure, state) ->
            Assert.Equal(TimerProceedState.NoTimer, state)
            Assert.Equal(source, decision.Previous)
            Assert.Equal(TimerProceedEffectFailure.Failed(TimerProceedEffect.Stop, "entry overlaps"), failure)
            Assert.Equal(1, List.length effects)
        | other -> failwithf "expected Proceeded, got %A" other

[<Fact>]
let ``proceed: an unknown Stop outcome proceeds and is reported as unknown`` () =
    match Proceed.handle (TimerProceedEffectOutcome.Unknown "timeout") TimerProceedState.Running TimerProceedCommand.Stop with
    | TimerProceedResult.Proceeded(_, _, _, TimerProceedEffectFailure.Unknown(TimerProceedEffect.Stop, "timeout"), TimerProceedState.NoTimer) -> ()
    | other -> failwithf "expected Proceeded with Unknown, got %A" other

[<Fact>]
let ``proceed: a completed Stop is an ordinary acceptance, not a proceed-despite-failure`` () =
    match Proceed.handle TimerProceedEffectOutcome.Completed TimerProceedState.Running TimerProceedCommand.Stop with
    | TimerProceedResult.Accepted(_, _, _, TimerProceedState.NoTimer) -> ()
    | other -> failwithf "expected Accepted, got %A" other

// ---- unresolved ----

open Generated.TimerUnresolved

[<Fact>]
let ``unresolved: a failed or unknown Stop picks no state and hands the failure to the host`` () =
    for outcome, expected in
        [ TimerUnresolvedEffectOutcome.Failed "entry overlaps", TimerUnresolvedEffectFailure.Failed(TimerUnresolvedEffect.Stop, "entry overlaps")
          TimerUnresolvedEffectOutcome.Unknown "timeout", TimerUnresolvedEffectFailure.Unknown(TimerUnresolvedEffect.Stop, "timeout") ] do
        for source in [ TimerUnresolvedState.Running; TimerUnresolvedState.Paused ] do
            match Unresolved.handle outcome source TimerUnresolvedCommand.Stop with
            | TimerUnresolvedResult.Unresolved(decision, _, _, failure) ->
                Assert.Equal(expected, failure)
                Assert.Equal(source, decision.Previous)
            | other -> failwithf "expected Unresolved, got %A" other

[<Fact>]
let ``unresolved: a completed Stop is accepted with its target state`` () =
    match Unresolved.handle TimerUnresolvedEffectOutcome.Completed TimerUnresolvedState.Paused TimerUnresolvedCommand.Stop with
    | TimerUnresolvedResult.Accepted(_, _, _, TimerUnresolvedState.NoTimer) -> ()
    | other -> failwithf "expected Accepted, got %A" other

[<Fact>]
let ``unresolved: the construction carries an open RESOLVE obligation linked to the effect`` () =
    let c = construction timerUnresolved
    let linked = c.Obligations |> List.filter (fun o -> match o.Kind with ResolveEffectFailure _ -> true | _ -> false)
    match linked with
    | [ o ] ->
        Assert.Equal("RESOLVE:StopCompletionRefused", o.Id)
        match o.Kind with
        | ResolveEffectFailure(effect, subject, question) ->
            Assert.Equal("Stop", effect)
            Assert.Equal("StopCompletionRefused", subject)
            Assert.Contains("discarded or stays active", question)
        | other -> failwithf "unexpected %A" other
    | other -> failwithf "expected one linked RESOLVE obligation, got %A" other
    // The question is prose for a person; it never reaches generated code.
    for a in c.Artifacts |> List.filter (fun a -> a.Tier.IsSome) do
        Assert.DoesNotContain("discarded or stays active", a.Content)

[<Fact>]
let ``remain and proceed variants carry no effect-failure obligation`` () =
    for fixture in [ timerRemain; timerProceed ] do
        let c = construction fixture
        Assert.DoesNotContain(c.Obligations, fun o -> match o.Kind with ResolveEffectFailure _ -> true | _ -> false)

// ---- manifest and generator version ----

[<Fact>]
let ``the manifest records each effect's disposition for vocabulary 2 only`` () =
    let manifestOf fixture = (construction fixture).Artifacts |> List.last |> _.Content
    Assert.DoesNotContain("effectFailure", manifestOf timer)
    Assert.DoesNotContain("effectFailure", manifestOf door)
    for fixture, spelled in [ timerRemain, "remain"; timerProceed, "proceed"; timerUnresolved, "unresolved" ] do
        let manifest = manifestOf fixture
        Assert.Contains("\"generatorVersion\": \"ordo.construction/2\"", manifest)
        Assert.Contains(sprintf "\"effect\": \"Stop\",\n      \"onFailure\": \"%s\"" spelled, manifest)

[<Fact>]
let ``every vocabulary 2 effect port reaches the generated table with its declared disposition`` () =
    for fixture, expected in
        [ timerRemain, EffectFailureDisposition.Remain
          timerProceed, EffectFailureDisposition.Proceed
          timerUnresolved, EffectFailureDisposition.Unresolved ] do
        let w = (view (validated fixture)).Workflow
        let declared = w.Ports |> List.choose (fun p -> match p.Kind with TransitionEffectPort(c, d) -> Some(c.Key, d) | PortKind.CurrentTimePort -> None)
        Assert.Equal<(string * EffectFailureDisposition option) list>([ "Stop", Some expected ], declared)
        let orchestration = (construction fixture).Artifacts |> List.find (fun a -> a.Tier = Some Orchestration) |> _.Content
        Assert.Contains(sprintf "| %sEffect.Stop -> %sFailureDisposition.%A" w.Name.Key w.Name.Key expected, orchestration)
