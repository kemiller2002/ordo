/// The synthetic door fixture exercises every vocabulary feature the timer
/// does not: guards, invariants, capabilities and both port kinds.
module Ordo.Construction.Tests.DoorBehaviorTests

open System
open Xunit
open Generated.Door

let private permissive = { Guard = (fun _ _ _ -> true); Invariant = (fun _ _ -> true) }
let private everything = set [ "door.unlock" ]

[<Fact>]
let ``guards are consulted only where the plan attached them`` () =
    let consulted = Collections.Generic.List<DoorGuard * DoorState * DoorCommand>()
    let recording = { permissive with Guard = (fun g s c -> consulted.Add((g, s, c)); true) }
    DoorTransition.decide recording everything DoorState.Closed DoorCommand.Open |> ignore
    Assert.Empty(consulted)
    DoorTransition.decide recording everything DoorState.Closed DoorCommand.Lock |> ignore
    Assert.Equal<(DoorGuard * DoorState * DoorCommand) list>([ DoorGuard.KeyPresent, DoorState.Closed, DoorCommand.Lock ], List.ofSeq consulted)

[<Fact>]
let ``a failing guard refuses the transition`` () =
    let noKey = { permissive with Guard = (fun _ _ _ -> false) }
    Assert.Equal(Error(DoorRefusal.GuardRejected DoorGuard.KeyPresent), DoorTransition.decide noKey everything DoorState.Closed DoorCommand.Lock)
    Assert.True(Result.isOk (DoorTransition.decide noKey everything DoorState.Closed DoorCommand.Open))

[<Fact>]
let ``an invariant is checked in the target state it holds in`` () =
    let unlatched = { permissive with Invariant = (fun _ _ -> false) }
    Assert.Equal(Error(DoorRefusal.InvariantViolated DoorInvariant.Latched), DoorTransition.decide unlatched everything DoorState.Closed DoorCommand.Lock)
    Assert.True(Result.isOk (DoorTransition.decide unlatched everything DoorState.Locked DoorCommand.Unlock))
    Assert.Equal<DoorInvariant list>([ DoorInvariant.Latched ], DoorTransition.invariantsIn DoorState.Locked)

[<Fact>]
let ``a missing semantic capability refuses before guards are consulted`` () =
    let guardCalls = ref 0
    let counting = { permissive with Guard = (fun _ _ _ -> guardCalls.Value <- guardCalls.Value + 1; true) }
    Assert.Equal(Error(DoorRefusal.MissingCapabilities(DoorCommand.Unlock, [ "door.unlock" ])), DoorTransition.decide counting Set.empty DoorState.Locked DoorCommand.Unlock)
    Assert.Equal(0, guardCalls.Value)
    Assert.True(Result.isOk (DoorTransition.decide permissive Set.empty DoorState.Closed DoorCommand.Lock))

[<Fact>]
let ``illegal pairs are refused before any extension code runs`` () =
    let exploding = { Guard = (fun _ _ _ -> failwith "guard ran"); Invariant = (fun _ _ -> failwith "invariant ran") }
    Assert.Equal(Error(DoorRefusal.InvalidTransition(DoorState.Open, DoorCommand.Lock)), DoorTransition.decide exploding everything DoorState.Open DoorCommand.Lock)

type private Host() =
    interface IDoorPorts with
        member _.CurrentTime() = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        member _.Perform(_, _) = DoorEffectOutcome.Completed

[<Fact>]
let ``orchestration delegates the decision and performs requested effects`` () =
    match DoorOrchestration.handle (Host()) permissive everything DoorState.Locked DoorCommand.Unlock with
    | DoorResult.Accepted(decision, _, effects) ->
        Assert.Equal(DoorState.Closed, decision.Next)
        Assert.Equal<(DoorEffect * DoorEffectOutcome) list>([ DoorEffect.Unlock, DoorEffectOutcome.Completed ], effects)
    | other -> failwithf "%A" other
    match DoorOrchestration.handle (Host()) permissive Set.empty DoorState.Locked DoorCommand.Unlock with
    | DoorResult.Refused(DoorRefusal.MissingCapabilities _, _) -> ()
    | other -> failwithf "%A" other
