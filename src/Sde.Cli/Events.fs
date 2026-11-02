/// `sde events`: record what happened after recognition, and summarize the
/// decision event log (ORDO-OBS-001). Recording an execution or a
/// verification is an explicit act by whoever observed it; Ordo never
/// records one on anybody's behalf.
module Sde.Cli.Events

open System
open System.IO
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.DecisionEvents

type Target =
    { DecisionId: string option
      PacketId: string option
      WorkItemId: string option }

type ExecutionOptions =
    { Events: string option
      Target: Target
      Status: string
      DurationMs: string option
      Executor: string option
      Source: string option }

type VerdictSource =
    | Manual of verdict: string * verifier: string * scope: string option
    | DokimosReceipt of path: string

type VerificationOptions =
    { Events: string option
      Target: Target
      Verdict: VerdictSource
      Commit: string option }

type SummarizeOptions = { Events: string option }

/// What the host provides: clock, nonce, version and environment.
type Host =
    { Now: unit -> DateTimeOffset
      Nonce: unit -> string
      OrdoVersion: string
      Env: string -> string option }

let private optionalId field value =
    match value with
    | None -> Ok None
    | Some v -> checkIdentifier field v |> Result.map Some

let private ( <!> ) (r: Result<'a, string>) (f: 'a -> Result<'b, string>) = Result.bind f r

let private correlationOf (host: Host) (target: Target) =
    optionalId "--decision" target.DecisionId <!> fun decision ->
    optionalId "--packet" target.PacketId <!> fun packet ->
    optionalId "--work-item" target.WorkItemId <!> fun work ->
        let ambient = DecisionLog.ambient host.Env
        if decision.IsNone && packet.IsNone && work.IsNone then
            Error "name what is being reported on: --decision ID, --packet ID or --work-item ID"
        else
            Ok { RecognitionId = None
                 RunId = ambient.RunId
                 SessionId = ambient.SessionId
                 WorkItemId = work |> Option.orElse ambient.WorkItemId
                 CohortId = None
                 RequirementKey = None
                 RequirementId = None
                 DecisionId = decision
                 PacketId = packet }

let private eventsPath (host: Host) (path: string option) =
    match DecisionLog.resolvePath host.Env path with
    | Some p -> Ok p
    | None -> Error(sprintf "no event log: give --events FILE or set %s" DecisionLog.EventsVariable)

let private durationOf value =
    match value with
    | None -> Ok None
    | Some(text: string) ->
        match Int64.TryParse text with
        | true, ms when ms >= 0L -> Ok(Some ms)
        | _ -> Error "--duration-ms must be a non-negative integer"

let private write path event =
    DecisionLog.append path [ encodeEvent event ]
    |> Result.map (fun () -> event)

let private outcome asJson (result: Result<DecisionEvent, string>) =
    match result with
    | Ok event ->
        let text =
            if asJson then encodeEvent event
            else sprintf "Recorded %s %s." (kindOf event.Body) event.EventId
        0, [ text ], []
    | Error message ->
        if asJson then 1, [ sprintf "{\"schemaVersion\":\"%s\",\"status\":\"rejected\",\"reason\":%s}" EventSchemaVersion (render (JString message)) ], []
        else 1, [], [ "Event refused: " + message ]

let private executionResult (host: Host) (options: ExecutionOptions) =
    eventsPath host options.Events <!> fun path ->
    correlationOf host options.Target <!> fun correlation ->
    (parseExecutionStatus options.Status |> Option.map Ok |> Option.defaultValue (Error "--status must be succeeded, failed or abandoned")) <!> fun status ->
    durationOf options.DurationMs <!> fun duration ->
    optionalId "--executor" options.Executor <!> fun executor ->
    optionalId "--source" options.Source <!> fun source ->
        executionEvent (host.Now()) host.OrdoVersion (host.Nonce()) (source |> Option.defaultValue "agent-reported") correlation
            { Status = status; DurationMs = duration; Executor = executor }
        |> write path

let recordExecution (host: Host) (asJson: bool) (options: ExecutionOptions) =
    executionResult host options |> outcome asJson

let private sha256 (bytes: byte[]) = "sha256:" + (SHA256.HashData bytes |> Convert.ToHexString).ToLowerInvariant()

/// Reads a Dokimos receipt. Only contracts whose meaning is known are
/// accepted; anything else is refused rather than guessed.
let readDokimosReceipt (bytes: byte[]) : Result<VerificationVerdict * string * string * string option, string> =
    let memberOf (name: string) value =
        match value with
        | JObject members -> members |> List.tryFind (fun (k, _) -> String.Equals(k, name, StringComparison.OrdinalIgnoreCase)) |> Option.map snd
        | _ -> None
    let text name value = match memberOf name value with Some(JString s) -> Some s | _ -> None
    match parse (Encoding.UTF8.GetString bytes) with
    | Error _ -> Error "Dokimos receipt is not valid JSON"
    | Ok json ->
        match text "contract" json with
        | Some "dokimos.completion-verification" ->
            let verdict =
                match text "verdict" json with
                | Some "verified" -> Ok VerificationVerdict.Passed
                | Some "rejected" -> Ok VerificationVerdict.Failed
                | Some other -> Error(sprintf "unknown Dokimos completion verdict '%s'" other)
                | None -> Error "Dokimos completion receipt has no verdict"
            verdict |> Result.map (fun v -> v, "dokimos", "completion", text "commit" json |> safeIdentifier)
        | Some "dokimos.recognition-audit/1" ->
            // Dokimos emits this contract only when the audit found nothing.
            Ok(VerificationVerdict.Passed, "dokimos", "recognition-classification", None)
        | Some other -> Error(sprintf "unsupported Dokimos contract '%s'" other)
        | None -> Error "Dokimos receipt names no contract"

let private verificationResult (host: Host) (options: VerificationOptions) =
    let facts =
        match options.Verdict with
        | Manual(verdict, verifier, scope) ->
            (parseVerdict verdict |> Option.map Ok |> Option.defaultValue (Error "--verdict must be passed, failed or indeterminate")) <!> fun v ->
            checkIdentifier "--verifier" verifier <!> fun who ->
            optionalId "--scope" scope <!> fun scope ->
                Ok({ Verdict = v; Verifier = who; Scope = scope |> Option.defaultValue "unspecified"; ReceiptDigest = None; Commit = None }, "verifier-reported:" + who)
        | DokimosReceipt path ->
            (try Ok(File.ReadAllBytes path) with e -> Error("cannot read Dokimos receipt: " + e.Message)) <!> fun bytes ->
            readDokimosReceipt bytes |> Result.map (fun (v, who, scope, commit) ->
                { Verdict = v; Verifier = who; Scope = scope; ReceiptDigest = Some(sha256 bytes); Commit = commit }, "dokimos-receipt")
    eventsPath host options.Events <!> fun path ->
    correlationOf host options.Target <!> fun correlation ->
    facts <!> fun (facts, source) ->
    optionalId "--commit" options.Commit <!> fun commit ->
        verificationEvent (host.Now()) host.OrdoVersion (host.Nonce()) source correlation
            { facts with Commit = commit |> Option.orElse facts.Commit }
        |> write path

let recordVerification (host: Host) (asJson: bool) (options: VerificationOptions) =
    verificationResult host options |> outcome asJson

let summarizeFile (host: Host) (asJson: bool) (options: SummarizeOptions) =
    match eventsPath host options.Events |> Result.bind DecisionLog.read with
    | Error message ->
        if asJson then 3, [ sprintf "{\"schemaVersion\":\"%s\",\"status\":\"unavailable\",\"reason\":%s}" SummarySchemaVersion (render (JString message)) ], []
        else 3, [], [ message ]
    | Ok log ->
        let s = summarize log
        let text =
            if asJson then encodeSummary s
            else
                let rate = s.MatchRate |> Option.map (sprintf "%.1f%%" << (*) 100.0) |> Option.defaultValue "n/a"
                sprintf "%d decisions evaluated in %d run(s): %d matched (%s), %d partial, %d unmatched, %d ambiguous, %d rejected; %d fallback to agent; execution unknown for %d; %d executed but unverified; %d corrupt line(s)."
                    s.Evaluated s.Runs s.Matched rate s.Partial s.Unmatched s.Ambiguous s.Rejected s.FallbackToAgent s.ExecutionUnknown
                    s.ExecutedButUnverified s.CorruptLines
        0, [ text ], []
