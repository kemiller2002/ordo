/// CONSTRUCT-010 phase 2, increment 2: vocabulary 5 effect outcome policies in
/// generated code. A definitely failed effect is retried up to the plan's
/// count; an unknown outcome is never performed again, only reconciled by host
/// code or escalated; no state is chosen for an escalated or unresolved
/// outcome. The goldens are the real timer cohort with test-approved policies.
module Ordo.Construction.Tests.EffectPolicyTests

open System
open Xunit
open Generated.TimerPolicy
open Generated.TimerEscalate
open Ordo.Construction.Construction
open Ordo.Construction.Tests.Fixtures

/// A host whose Perform answers from a script, recording every call.
type private ScriptedPorts(perform: TimerPolicyEffectOutcome list, reconcile: TimerPolicyReconciliation, compensate: TimerPolicyEffectOutcome) =
    let mutable remaining = perform
    member val Performed = 0 with get, set
    member val Reconciled = 0 with get, set
    member val Compensated = 0 with get, set
    interface ITimerPolicyPorts with
        member _.CurrentTime() = DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero)
        member this.Perform(_, _) =
            this.Performed <- this.Performed + 1
            match remaining with
            | next :: rest ->
                remaining <- rest
                next
            | [] -> failwith "performed more often than the script allows"
        member this.Reconcile(_, _) =
            this.Reconciled <- this.Reconciled + 1
            reconcile
        member this.Compensate(_, _) =
            this.Compensated <- this.Compensated + 1
            compensate

let private failed = TimerPolicyEffectOutcome.Failed "store refused"
let private unknown = TimerPolicyEffectOutcome.Unknown "connection lost"
let private ok' = TimerPolicyEffectOutcome.Completed

let private stop (ports: ScriptedPorts) = TimerPolicyOrchestration.handle ports TimerPolicyState.Running TimerPolicyCommand.Stop

[<Fact>]
let ``a definite failure is retried up to the plan's count`` () =
    let ports = ScriptedPorts([ failed; failed; ok' ], TimerPolicyReconciliation.Applied, ok')
    match stop ports with
    | TimerPolicyResult.Accepted(_, _, _, state) -> Assert.Equal(TimerPolicyState.NoTimer, state)
    | other -> failwithf "%A" other
    Assert.Equal(3, ports.Performed)
    Assert.Equal(0, ports.Compensated)

[<Fact>]
let ``once retries are spent, compensation runs and the workflow stays in its source state`` () =
    let ports = ScriptedPorts([ failed; failed; failed ], TimerPolicyReconciliation.Applied, ok')
    match stop ports with
    | TimerPolicyResult.Compensated(_, _, _, TimerPolicyEffectFailure.Failed _, state) -> Assert.Equal(TimerPolicyState.Running, state)
    | other -> failwithf "%A" other
    Assert.Equal(3, ports.Performed)
    Assert.Equal(1, ports.Compensated)

[<Fact>]
let ``compensation that does not complete is escalated, with no state chosen`` () =
    for compensation in [ failed; unknown ] do
        let ports = ScriptedPorts([ failed; failed; failed ], TimerPolicyReconciliation.Applied, compensation)
        match stop ports with
        | TimerPolicyResult.Escalated _ -> ()
        | other -> failwithf "%A" other

[<Fact>]
let ``an unknown outcome is never performed again; reconciliation decides`` () =
    let applied = ScriptedPorts([ unknown ], TimerPolicyReconciliation.Applied, ok')
    match stop applied with
    | TimerPolicyResult.Accepted _ -> ()
    | other -> failwithf "%A" other
    Assert.Equal(1, applied.Performed)
    Assert.Equal(1, applied.Reconciled)
    let still = ScriptedPorts([ unknown ], TimerPolicyReconciliation.StillUnknown "no record", ok')
    match stop still with
    | TimerPolicyResult.Escalated(_, _, _, TimerPolicyEffectFailure.Unknown _) -> ()
    | other -> failwithf "%A" other
    Assert.Equal(1, still.Performed)
    Assert.Equal(0, still.Compensated)

[<Fact>]
let ``an effect reconciled as not applied is a definite failure, with the retries it has left`` () =
    let ports = ScriptedPorts([ unknown; ok' ], TimerPolicyReconciliation.NotApplied "no record", ok')
    match stop ports with
    | TimerPolicyResult.Accepted _ -> ()
    | other -> failwithf "%A" other
    Assert.Equal(2, ports.Performed)

type private EscalatePorts(outcome: TimerEscalateEffectOutcome) =
    member val Performed = 0 with get, set
    interface ITimerEscalatePorts with
        member _.CurrentTime() = DateTimeOffset.UnixEpoch
        member this.Perform(_, _) =
            this.Performed <- this.Performed + 1
            outcome

[<Fact>]
let ``an escalating policy chooses no state, and an unresolved one says so`` () =
    let failedPorts = EscalatePorts(TimerEscalateEffectOutcome.Failed "store refused")
    match TimerEscalateOrchestration.handle failedPorts TimerEscalateState.Running TimerEscalateCommand.Stop with
    | TimerEscalateResult.Escalated _ -> Assert.Equal(1, failedPorts.Performed)
    | other -> failwithf "%A" other
    match TimerEscalateOrchestration.handle (EscalatePorts(TimerEscalateEffectOutcome.Unknown "lost")) TimerEscalateState.Running TimerEscalateCommand.Stop with
    | TimerEscalateResult.Unresolved(_, _, _, TimerEscalateEffectFailure.Unknown _) -> ()
    | other -> failwithf "%A" other

[<Fact>]
let ``reconciliation and compensation are handed off as implement obligations; the open question stays open`` () =
    let ids = (construction timerPolicy).Obligations |> List.map _.Id
    Assert.Contains("IMPLEMENT:ITimerPolicyPorts.Reconcile/Stop", ids)
    Assert.Contains("IMPLEMENT:ITimerPolicyPorts.Compensate/Stop", ids)
    let escalate = (construction timerEscalate).Obligations |> List.map _.Id
    Assert.Contains("RESOLVE:StopEffectFailure", escalate)
    Assert.DoesNotContain(escalate, fun id -> id.Contains "Reconcile" || id.Contains "Compensate")

let private references =
    [ typeof<Ordo.Core.ConstructionPlan.ValidatedPlan>.Assembly.Location
      typeof<PlannedWrite>.Assembly.Location ]

let private policyTiers =
    [ "Model"; "Transition"; "Orchestration" ]
    |> List.map (fun tier -> sprintf "#load @\"%s\"" (goldenPath timerPolicy (sprintf "generated/TimerPolicy/TimerPolicy.%s.g.fs" tier)))
    |> String.concat "\n"

[<Fact>]
let ``retrying an unknown outcome cannot be expressed, and a host must reconcile and compensate`` () =
    let host members = policyTiers + "\nopen Generated.TimerPolicy\nlet host = { new ITimerPolicyPorts with\n" + members + " }"
    let currentTime = "    member _.CurrentTime() = System.DateTimeOffset.MinValue\n"
    let perform = "    member _.Perform(_, _) = TimerPolicyEffectOutcome.Completed\n"
    let reconcile = "    member _.Reconcile(_, _) = TimerPolicyReconciliation.Applied\n"
    let compensate = "    member _.Compensate(_, _) = TimerPolicyEffectOutcome.Completed"
    let failures =
        Ordo.Tests.FsiProbe.check references
            [ "positive control", host (currentTime + perform + reconcile + compensate), None
              "no retry for an unknown outcome", policyTiers + "\nopen Generated.TimerPolicy\nlet x = TimerPolicyOnUnknown.Retry", Some "FS0039"
              "host omits reconciliation", host (currentTime + perform + compensate), Some "FS0366"
              "host omits compensation", host (currentTime + perform + reconcile.TrimEnd('\n')), Some "FS0366" ]
    Assert.True(List.isEmpty failures, String.concat "\n\n" failures)
