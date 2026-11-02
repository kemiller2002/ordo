/// Host side of Ordo decision events (`ordo.decision-event/1`): where the
/// log lives, which ambient identifiers to attach, and the append-only
/// writer. Telemetry is strictly best-effort: nothing here can change a
/// command's exit code or its stdout, and every failure becomes a warning.
module Sde.Cli.DecisionLog

open System
open System.IO
open System.Text
open System.Threading
open Ordo.Core.DecisionEvents

/// Environment variable naming the log when `--events` is not given.
[<Literal>]
let EventsVariable = "ORDO_DECISION_EVENTS"

let private firstSet (env: string -> string option) names =
    names |> List.tryPick env |> safeIdentifier

/// Ambient correlation, from the environment Praxis, GitHub Actions and
/// Claude Code already provide. Values that are not safe identifiers are dropped.
type Ambient =
    { RunId: string option
      SessionId: string option
      WorkItemId: string option }

let ambient (env: string -> string option) =
    { RunId = firstSet env [ "ORDO_RUN_ID"; "PRAXIS_TELEMETRY_RUN_ID"; "ROS_TELEMETRY_RUN_ID"; "GITHUB_RUN_ID" ]
      SessionId = firstSet env [ "ORDO_SESSION_ID"; "PRAXIS_TELEMETRY_SESSION_ID"; "ROS_TELEMETRY_SESSION_ID"; "CLAUDE_CODE_SESSION_ID" ]
      WorkItemId = firstSet env [ "ORDO_WORK_ITEM"; "PRAXIS_WORK_ITEM" ] }

let environment (name: string) =
    match Environment.GetEnvironmentVariable name with
    | null -> None
    | v when String.IsNullOrWhiteSpace v -> None
    | v -> Some v

/// `--events` wins; otherwise the environment; otherwise telemetry is off.
let resolvePath (env: string -> string option) (explicitPath: string option) =
    explicitPath |> Option.orElse (env EventsVariable)

/// Lock contention, the only failure worth retrying: a sharing or lock
/// violation on Windows (32, 33), or EWOULDBLOCK from flock on Linux (11)
/// and macOS (35), which .NET reports as the raw errno.
let private isContention (e: IOException) =
    List.contains (e.HResult &&& 0xFFFF) [ 32; 33; 11; 35 ]

/// Appends whole lines to the log under an exclusive lock, so concurrent
/// writers never interleave within a line. Bounded retry on contention;
/// any other failure is returned, never thrown.
let append (path: string) (lines: string list) : Result<unit, string> =
    let payload = lines |> List.map (fun l -> l + "\n") |> String.concat "" |> Encoding.UTF8.GetBytes
    let rec attempt remaining =
        let outcome =
            try
                match Path.GetDirectoryName(Path.GetFullPath path) with
                | null -> ()
                | directory when directory = "" -> ()
                | directory -> Directory.CreateDirectory directory |> ignore
                use stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None)
                stream.Write(payload, 0, payload.Length)
                stream.Flush true
                Ok()
            with
            | :? UnauthorizedAccessException as e -> Error(false, e.Message)
            | :? DirectoryNotFoundException as e -> Error(false, e.Message)
            | :? IOException as e when isContention e -> Error(true, e.Message)
            | :? IOException as e -> Error(false, e.Message)
            | e -> Error(false, e.Message)
        match outcome with
        | Ok() -> Ok()
        | Error(true, _) when remaining > 0 ->
            Thread.Sleep(5 + Random.Shared.Next 20)
            attempt (remaining - 1)
        | Error(_, message) -> Error message
    if List.isEmpty lines then Ok() else attempt 200

/// Writes events, returning the warnings to show on stderr (empty on success).
let record (path: string option) (events: DecisionEvent list) : string list =
    match path with
    | None -> []
    | Some p ->
        match append p (events |> List.map encodeEvent) with
        | Ok() -> []
        | Error message -> [ sprintf "warning: Ordo decision events not recorded to %s: %s" p message ]

/// Reads a log. A missing file is reported as missing, not as an empty log.
let read (path: string) : Result<LogRead, string> =
    try
        if not (File.Exists path) then Error("decision event log not found: " + path)
        else
            use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            use reader = new StreamReader(stream, Encoding.UTF8)
            Ok(readLog (reader.ReadToEnd()))
    with
    | :? IOException as e -> Error("cannot read decision event log: " + e.Message)
    | :? UnauthorizedAccessException as e -> Error("cannot read decision event log: " + e.Message)
