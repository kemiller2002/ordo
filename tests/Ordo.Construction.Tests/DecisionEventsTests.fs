/// ORDO-OBS-001: decision events derived from real pattern recognitions.
/// Evaluation, selection, execution and verification are separate events;
/// none is inferred from another, and missing or damaged telemetry is
/// reported as unknown, never as zero or success.
module Ordo.Construction.Tests.DecisionEventsTests

open System
open System.IO
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionDecisions
open Ordo.Core.PatternRegistry
open Ordo.Core.PatternRecognition
open Ordo.Core.PatternRecognitionWire
open Ordo.Core.DecisionEvents
open Ordo.Construction.Tests.Fixtures

let private read name =
    File.ReadAllText(Path.Combine(repositoryRoot, "tests", "fixtures", "construction", "time-entry-timer", name))

let private manifest () = readManifest (read "source-manifest.json") |> ok "manifest"
let private blueprint () = readBlueprint (read "blueprint.json") |> ok "blueprint"
let private records () = readDecisionRecords (read "construction-decisions.json") |> ok "decisions"

let private inputOf (b: Blueprint) (r: ApprovedDecisionRecords option) =
    { Manifest = manifest ()
      Blueprint = b
      CohortId = "COH-TIMER"
      AuthorizedDecisionIds = Set.singleton "DEC-TIMER-STATES"
      Records = r |> Option.map (fun r -> { r with BlueprintDigest = blueprintDigest b })
      Policy = { AllowedPaths = [ "src/Timer/Extensions/**" ]; ArchitectureConstraints = []; EscalateOpenQuestions = false } }

let private timerInput () = inputOf (blueprint ()) (Some(records ()))

let private at = DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero)

let private context registry nonce =
    { OccurredAt = at
      OrdoVersion = "1.19.0-test"
      Nonce = nonce
      RunId = Some "RUN-1"
      SessionId = Some "SESSION-1"
      WorkItemId = Some "WI-0048"
      DurationMs = Some 42L
      Families = familiesOf registry }

let private eventsOf registry input =
    recognize registry None input |> ok "recognize" |> fun report -> report, fromReport (context registry "nonce-1") report

let private evaluations events =
    events |> List.choose (fun e -> match e.Body with PatternEvaluated f -> Some(e, f) | _ -> None)

let private selections events =
    events |> List.choose (fun e -> match e.Body with PatternSelection f -> Some(e, f) | _ -> None)

let private forKey (suffix: string) (items: (DecisionEvent * 'a) list) =
    items |> List.find (fun (e, _) -> e.Correlation.RequirementKey |> Option.exists (fun k -> k.EndsWith suffix)) |> snd

let private roundTrip events = events |> List.map encodeEvent |> String.concat "\n" |> readLog

let private competing =
    let contract = { BuiltIn.stateMachineContract with Ref = { Id = "test.competing-state-machine"; Version = 1 }; Title = "Competing" }
    { Contract = contract
      Claim = fun cohort facts -> BuiltIn.stateMachine.Claim cohort facts |> Option.map (fun c -> { c with Pattern = contract.Ref }) }

// ---------------------------------------------------------------------------
// Evaluation and selection from a real recognition.
// ---------------------------------------------------------------------------

[<Fact>]
let ``a successful match is an identifiable evaluation and a separate pattern-selected selection`` () =
    let report, events = eventsOf BuiltIn.registry (timerInput ())
    let evaluation = evaluations events |> forKey "L116-L129"
    Assert.Equal(MatchStatus.Matched, evaluation.Status)
    Assert.Equal<PatternRef list>([ BuiltIn.stateMachineContract.Ref ], evaluation.Patterns |> List.map _.Pattern)
    Assert.Equal(Some "state.machine-guarded-transition", evaluation.Patterns.Head.Family)
    let selection = selections events |> forKey "L116-L129"
    Assert.Equal(SelectionDisposition.PatternSelected, selection.Disposition)
    // One run event, then exactly one evaluation and one selection per evaluated requirement.
    Assert.Equal(1 + 2 * report.Requirements.Length, events.Length)
    Assert.Equal(report.Requirements.Length, (evaluations events).Length)
    // Nothing about execution or verification is inferred.
    Assert.DoesNotContain(events, fun e -> match e.Body with AgentExecution _ | VerificationResult _ -> true | _ -> false)

[<Fact>]
let ``an unmatched requirement falls back to agent reasoning through its packet`` () =
    let report, events = eventsOf BuiltIn.registry (inputOf (blueprint ()) None)
    Assert.All(evaluations events, fun (_, f) -> Assert.Equal(MatchStatus.Unmatched, f.Status))
    Assert.All(selections events, fun (e, f) ->
        Assert.Equal(SelectionDisposition.FallbackAgent, f.Disposition)
        Assert.True(e.Correlation.PacketId.IsSome, "a delegated decision names its packet"))
    let packetIds = report.Packets |> List.map _.Id |> Set.ofList
    Assert.All(selections events, fun (e, _) -> Assert.Contains(e.Correlation.PacketId.Value, packetIds))
    let summary = summarize (roundTrip events)
    Assert.Equal(Some 0.0, summary.MatchRate)
    Assert.Equal(report.Requirements.Length, summary.FallbackToAgent)

[<Fact>]
let ``an ambiguous match is recorded as ambiguous and escalated, never resolved`` () =
    let registry = Registry.create "test/ambiguous" (competing :: BuiltIn.definitions) |> ok "registry"
    let _, events = eventsOf registry (timerInput ())
    let evaluation = evaluations events |> forKey "L116-L129"
    Assert.Equal(MatchStatus.Ambiguous, evaluation.Status)
    Assert.Contains("patternSelectionRequired", evaluation.GapKinds)
    Assert.Equal(2, evaluation.Patterns.Length)
    Assert.Equal(SelectionDisposition.Escalated, (selections events |> forKey "L116-L129").Disposition)

[<Fact>]
let ``a rejected decision records blocker kinds and no reason text`` () =
    let b = blueprint ()
    let broken = { b with Requirements = b.Requirements |> List.filter (fun r -> not (r.Source.Key.EndsWith "L104")) }
    let report, events = eventsOf BuiltIn.registry (inputOf broken None)
    Assert.All(evaluations events, fun (_, f) ->
        Assert.Equal(MatchStatus.Rejected, f.Status)
        Assert.Equal<string list>([ "invalidSource" ], f.BlockerKinds))
    Assert.All(selections events, fun (_, f) -> Assert.Equal(SelectionDisposition.Rejected, f.Disposition))
    // The violation text recognition produced is not in the telemetry.
    let violations = report.Requirements |> List.collect (fun r -> match r.Outcome with Blocked [ InvalidSource vs ] -> vs | _ -> [])
    Assert.NotEmpty violations
    let wire = events |> List.map encodeEvent |> String.concat "\n"
    Assert.All(violations, fun v -> Assert.DoesNotContain(v, wire))

[<Fact>]
let ``no prose from the inputs reaches the wire`` () =
    let report, events = eventsOf BuiltIn.registry (timerInput ())
    let wire = events |> List.map encodeEvent |> String.concat "\n"
    Assert.All(report.Decisions, fun d -> Assert.DoesNotContain(d.Question, wire))
    Assert.DoesNotContain("prompt", wire.ToLowerInvariant())

[<Fact>]
let ``decision ids are stable for equal inputs and event ids are unique per invocation`` () =
    let report = recognize BuiltIn.registry None (timerInput ()) |> ok "recognize"
    let first = fromReport (context BuiltIn.registry "nonce-a") report
    let second = fromReport (context BuiltIn.registry "nonce-b") report
    let ids events = events |> List.choose _.Correlation.DecisionId
    Assert.Equal<string list>(ids first, ids second)
    Assert.Empty(Set.intersect (first |> List.map _.EventId |> Set.ofList) (second |> List.map _.EventId |> Set.ofList))
    Assert.Equal(first.Length, first |> List.map _.EventId |> List.distinct |> List.length)

[<Fact>]
let ``every event round-trips through its wire form`` () =
    let _, events = eventsOf BuiltIn.registry (timerInput ())
    let log = roundTrip events
    Assert.Empty log.Corrupt
    Assert.Equal<DecisionEvent list>(events, log.Events)

// ---------------------------------------------------------------------------
// Execution and verification are separate facts.
// ---------------------------------------------------------------------------

let private correlationFor (decision: string) =
    { RecognitionId = None; RunId = None; SessionId = None; WorkItemId = None; CohortId = None
      RequirementKey = None; RequirementId = None; DecisionId = Some decision; PacketId = None }

[<Fact>]
let ``an executed pattern that then fails verification is counted as failed, not inferred to pass`` () =
    let _, events = eventsOf BuiltIn.registry (timerInput ())
    let decision = events |> List.pick (fun e -> match e.Body with PatternSelection { Disposition = SelectionDisposition.PatternSelected } -> e.Correlation.DecisionId | _ -> None)
    let executed =
        executionEvent at "test" "x1" "agent-reported" (correlationFor decision)
            { Status = ExecutionStatus.Succeeded; DurationMs = Some 1200L; Executor = Some "anthropic/claude-code" }
    let before = summarize (roundTrip (events @ [ executed ]))
    Assert.Equal(1, before.ExecutedButUnverified)
    Assert.Empty before.Verified
    let failed =
        verificationEvent at "test" "v1" "dokimos-receipt" (correlationFor decision)
            { Verdict = VerificationVerdict.Failed; Verifier = "dokimos"; Scope = "completion"; ReceiptDigest = None; Commit = None }
    let after = summarize (roundTrip (events @ [ executed; failed ]))
    Assert.Equal<(string * int) list>([ "failed", 1 ], after.Verified)
    Assert.Equal(0, after.ExecutedButUnverified)
    Assert.Equal<(string * int) list>([ "succeeded", 1 ], after.ExecutionsReported)

[<Fact>]
let ``a selection with no execution event is execution-unknown, not failed`` () =
    let _, events = eventsOf BuiltIn.registry (timerInput ())
    let summary = summarize (roundTrip events)
    let actionable =
        selections events
        |> List.filter (fun (_, f) -> f.Disposition = SelectionDisposition.PatternSelected || f.Disposition = SelectionDisposition.FallbackAgent)
    Assert.NotEmpty actionable
    Assert.Equal(actionable.Length, summary.ExecutionUnknown)
    Assert.Empty summary.ExecutionsReported

[<Fact>]
let ``a verification that names no known decision is uncorrelated, not credited`` () =
    let _, events = eventsOf BuiltIn.registry (timerInput ())
    let stray =
        verificationEvent at "test" "v2" "dokimos-receipt" (correlationFor "odc-unknown")
            { Verdict = VerificationVerdict.Passed; Verifier = "dokimos"; Scope = "completion"; ReceiptDigest = None; Commit = None }
    let summary = summarize (roundTrip (events @ [ stray ]))
    Assert.Equal(1, summary.Uncorrelated)
    Assert.Empty summary.Verified

// ---------------------------------------------------------------------------
// Missing and corrupted telemetry.
// ---------------------------------------------------------------------------

[<Fact>]
let ``corrupt and truncated lines are reported by line number and the rest still reads`` () =
    let _, events = eventsOf BuiltIn.registry (timerInput ())
    let lines = events |> List.map encodeEvent
    let truncated = lines.[2].Substring(0, lines.[2].Length / 2)
    let wrongSchema = lines.[3].Replace(EventSchemaVersion, "ordo.decision-event/99")
    let text = String.concat "\n" ([ lines.[0]; lines.[1]; truncated; wrongSchema; "not json" ] @ (lines |> List.skip 4) @ [ lines.[0] ])
    let log = readLog text
    Assert.Equal<int list>([ 3; 4; 5 ], log.Corrupt |> List.map _.Line)
    Assert.Equal(events.Length - 2, log.Events.Length)
    Assert.Equal(1, log.Duplicates)
    let summary = summarize log
    Assert.Equal(3, summary.CorruptLines)
    // The run reported more evaluations than survived: it is incomplete.
    Assert.Equal(1, summary.IncompleteRuns)

[<Fact>]
let ``an empty log has no match rate rather than a zero one`` () =
    let summary = summarize (readLog "")
    Assert.Equal(None, summary.MatchRate)
    Assert.Equal(0, summary.Evaluated)

[<Fact>]
let ``identifiers that look like credentials or prose are refused`` () =
    for value in [ "sk-ant-api03-abc"; "ghp_0123456789abcdef"; "password=hunter2"; "has spaces in it"; "" ] do
        Assert.True(Result.isError (checkIdentifier "id" value), value)
    for value in [ "WI-0048"; "odc-0123abcd"; "anthropic/claude-code"; "7c4b3b1b-f6fb-5c60-9ad1-cbc453f2b1cf" ] do
        Assert.Equal(Ok value, checkIdentifier "id" value)
