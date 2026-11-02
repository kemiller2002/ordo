/// ORDO-OBS-001 at the command line: a real `sde ecir recognize` writes
/// identifiable decision events without changing its own output; events can
/// be recorded concurrently; telemetry failure never fails recognition;
/// executions and verifications are recorded only when someone reports them.
module Sde.Tests.DecisionEventCommandTests

open System
open System.IO
open System.Threading.Tasks
open Xunit
open Sde.Cli
open Sde.Tests.Fixtures
open Ordo.Core.DecisionEvents

let private fixture name =
    Path.Combine(repositoryRoot, "tests", "fixtures", "construction", "time-entry-timer", name)

let private recognizeArgs extra =
    [ "ecir"; "recognize"; "--manifest"; fixture "source-manifest.json"; "--blueprint"; fixture "blueprint.json"
      "--cohort"; "COH-TIMER"; "--json" ] @ extra

let private withDir prefix (body: string -> unit) =
    let dir = makeTempDir prefix
    try body dir finally cleanup [ dir ]

let private readEvents path =
    match DecisionLog.read path with
    | Ok log -> log
    | Error e -> failwith e

[<Fact>]
let ``a real recognition with --events produces identifiable events and an unchanged report`` () =
    withDir "ordo-events-" (fun dir ->
        let events = Path.Combine(dir, "nested", "decision-events.jsonl")
        let baseCode, baseOut, baseErr = Program.run (recognizeArgs []) dir
        let code, stdout, stderr = Program.run (recognizeArgs [ "--events"; events ]) dir
        Assert.Equal(baseCode, code)
        Assert.Equal<string list>(baseOut, stdout)
        Assert.Equal<string list>(baseErr, stderr)
        let log = readEvents events
        Assert.Empty log.Corrupt
        let run = log.Events |> List.pick (fun e -> match e.Body with RecognitionRun r -> Some r | _ -> None)
        let evaluated = log.Events |> List.filter (fun e -> match e.Body with PatternEvaluated _ -> true | _ -> false)
        Assert.Equal(run.Evaluated, evaluated.Length)
        Assert.True(run.DurationMs.IsSome)
        Assert.All(log.Events, fun e ->
            Assert.StartsWith("ode-", e.EventId)
            Assert.Equal("ordo.recognize", e.EvidenceSource)
            Assert.Equal(Some "COH-TIMER", e.Correlation.CohortId))
        Assert.All(evaluated, fun e -> Assert.True(e.Correlation.DecisionId.IsSome && e.Correlation.RequirementKey.IsSome)))

[<Fact>]
let ``recognition succeeds with identical output when the event log cannot be written`` () =
    withDir "ordo-events-unwritable-" (fun dir ->
        let blocker = Path.Combine(dir, "not-a-directory")
        File.WriteAllText(blocker, "x")
        let events = Path.Combine(blocker, "decision-events.jsonl")
        let baseCode, baseOut, _ = Program.run (recognizeArgs []) dir
        let code, stdout, stderr = Program.run (recognizeArgs [ "--events"; events ]) dir
        Assert.Equal(baseCode, code)
        Assert.Equal(0, code)
        Assert.Equal<string list>(baseOut, stdout)
        Assert.Contains(stderr, fun line -> line.StartsWith "warning: Ordo decision events not recorded")
        Assert.False(File.Exists events))

[<Fact>]
let ``concurrent writers never interleave or lose a line`` () =
    withDir "ordo-events-concurrent-" (fun dir ->
        let path = Path.Combine(dir, "events.jsonl")
        let lineFor i = encodeEvent (executionEvent DateTimeOffset.UtcNow "test" (string i) "agent-reported"
                                        { RecognitionId = None; RunId = None; SessionId = None; WorkItemId = None; CohortId = None
                                          RequirementKey = None; RequirementId = None; DecisionId = Some(sprintf "odc-%04d" i); PacketId = None }
                                        { Status = ExecutionStatus.Succeeded; DurationMs = Some(int64 i); Executor = None })
        let writers = 16
        let perWriter = 25
        let results =
            [| for w in 0 .. writers - 1 ->
                   Task.Run(fun () ->
                       [ for n in 0 .. perWriter - 1 -> DecisionLog.append path [ lineFor (w * perWriter + n) ] ]) |]
            |> Array.map _.Result
            |> List.concat
        Assert.All(results, fun r -> Assert.Equal(Ok(), r))
        let log = readEvents path
        Assert.Empty log.Corrupt
        Assert.Equal(writers * perWriter, log.Events.Length)
        Assert.Equal(writers * perWriter, log.Events |> List.choose _.Correlation.DecisionId |> List.distinct |> List.length))

[<Fact>]
let ``execution and a failing Dokimos completion receipt are recorded as separate correlated events`` () =
    withDir "ordo-events-verify-" (fun dir ->
        let events = Path.Combine(dir, "events.jsonl")
        Program.run (recognizeArgs [ "--events"; events ]) dir |> ignore
        let packet =
            (readEvents events).Events
            |> List.pick (fun e -> match e.Body with PatternSelection { Disposition = SelectionDisposition.FallbackAgent } -> e.Correlation.PacketId | _ -> None)
        let code, _, stderr =
            Program.run [ "events"; "record-execution"; "--events"; events; "--packet"; packet; "--status"; "succeeded"; "--duration-ms"; "900" ] dir
        Assert.Equal(0, code)
        Assert.Empty stderr
        let receipt = Path.Combine(dir, "receipt.json")
        File.WriteAllText(receipt, """{ "Contract": "dokimos.completion-verification", "Verdict": "rejected", "WorkItemId": "WI-1" }""")
        let code, _, stderr = Program.run [ "events"; "record-verification"; "--events"; events; "--packet"; packet; "--dokimos-receipt"; receipt ] dir
        Assert.Equal(0, code)
        Assert.Empty stderr
        let log = readEvents events
        let verification = log.Events |> List.pick (fun e -> match e.Body with VerificationResult v -> Some(e, v) | _ -> None)
        Assert.Equal(VerificationVerdict.Failed, (snd verification).Verdict)
        Assert.StartsWith("sha256:", (snd verification).ReceiptDigest.Value)
        Assert.Equal("dokimos-receipt", (fst verification).EvidenceSource)
        let summary = summarize log
        Assert.Equal<(string * int) list>([ "failed", 1 ], summary.Verified)
        Assert.Equal(0, summary.Uncorrelated)
        let code, stdout, _ = Program.run [ "events"; "summarize"; "--events"; events; "--json" ] dir
        Assert.Equal(0, code)
        Assert.Contains("\"schemaVersion\":\"ordo.decision-summary/1\"", String.concat "" stdout |> fun s -> s.Replace(" ", "")))

[<Fact>]
let ``an unknown Dokimos contract, a credential-shaped id or a missing target is refused and nothing is written`` () =
    withDir "ordo-events-refuse-" (fun dir ->
        let events = Path.Combine(dir, "events.jsonl")
        let receipt = Path.Combine(dir, "receipt.json")
        File.WriteAllText(receipt, """{ "Contract": "dokimos.something-else" }""")
        let attempts =
            [ [ "events"; "record-verification"; "--events"; events; "--decision"; "odc-1"; "--dokimos-receipt"; receipt ]
              [ "events"; "record-execution"; "--events"; events; "--decision"; "ghp_abcdefghijklmnop"; "--status"; "failed" ]
              [ "events"; "record-execution"; "--events"; events; "--status"; "failed" ]
              [ "events"; "record-execution"; "--events"; events; "--decision"; "odc-1"; "--status"; "passed" ] ]
        for args in attempts do
            let code, _, _ = Program.run args dir
            Assert.NotEqual(0, code)
        Assert.False(File.Exists events))

[<Fact>]
let ``summarizing a missing log reports it unavailable rather than empty`` () =
    withDir "ordo-events-missing-" (fun dir ->
        let code, stdout, _ = Program.run [ "events"; "summarize"; "--events"; Path.Combine(dir, "absent.jsonl"); "--json" ] dir
        Assert.Equal(3, code)
        Assert.Contains("unavailable", String.concat "" stdout))

[<Fact>]
let ``recognition without --events writes nothing`` () =
    withDir "ordo-events-off-" (fun dir ->
        let code, _, stderr = Program.run (recognizeArgs []) dir
        Assert.Equal(0, code)
        Assert.Empty stderr
        Assert.Empty(Directory.EnumerateFileSystemEntries dir))
