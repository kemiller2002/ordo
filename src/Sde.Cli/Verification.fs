/// File adapter for `sde verification schedule`. It reads the admitted plan
/// and, when given, the pinned evidence manifest and the result files it
/// names; everything else is the pure `Ordo.Core.VerificationSchedule`.
/// Evidence paths are resolved against the current directory: the
/// repository root the acceptance verifier also resolves them against.
module Sde.Cli.Verification

open System
open System.IO
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.VerificationSchedule
open Ordo.Core.VerificationPlanWire

module ScheduleWire = Ordo.Core.VerificationScheduleWire

let private readFile label (path: string) =
    try
        if File.Exists path then Ok(File.ReadAllBytes path) else Error(sprintf "%s missing: %s" label path)
    with
    | :? IOException as ex -> Error(sprintf "%s: %s" label ex.Message)
    | :? UnauthorizedAccessException as ex -> Error(sprintf "%s: %s" label ex.Message)

let private text (bytes: byte array) = Text.Encoding.UTF8.GetString bytes
let private hex (bytes: byte array) = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

let private safeRelative (path: string) =
    not (String.IsNullOrWhiteSpace path) && not (Path.IsPathRooted path) && not (path.Split('/', '\\') |> Array.contains "..")

/// Reads every entry of a `causa.acceptance-evidence-manifest/1`. An entry
/// whose bytes do not match its pinned sha256 is still read, marked
/// unverified, so it schedules as stale (`integrity`) instead of vanishing.
let private readManifest (root: string) (path: string) : Result<EvidenceResult list, string> =
    readFile "evidence manifest" path
    |> Result.bind (fun bytes ->
        match parse (text bytes) with
        | Error e -> Error(sprintf "evidence manifest is not JSON: %A" e)
        | Ok manifest ->
            match tryMember "schema" manifest, tryMember "entries" manifest with
            | Ok(Some(JString "causa.acceptance-evidence-manifest/1")), Ok(Some(JArray entries)) ->
                entries
                |> List.sortBy (fun entry ->
                    match tryMember "path" entry with
                    | Ok(Some(JString p)) -> p
                    | _ -> "")
                |> List.map (fun entry ->
                    match tryMember "path" entry, tryMember "sha256" entry with
                    | Ok(Some(JString relative)), Ok(Some(JString pinned)) when safeRelative relative ->
                        readFile "evidence" (Path.Combine(root, relative))
                        |> Result.bind (fun raw -> readEvidence (hex raw = pinned) (text raw))
                        |> Result.mapError (sprintf "%s: %s" relative)
                    | _ -> Error "evidence manifest contains an invalid entry")
                |> List.fold (fun acc next -> Result.bind (fun xs -> Result.map (fun x -> x :: xs) next) acc) (Ok [])
                |> Result.map List.rev
            | _ -> Error "evidence manifest must be causa.acceptance-evidence-manifest/1 with an entries array")

let schedule (asJson: bool) (options: Args.VerificationScheduleOptions) : int * string list * string list =
    let evidence =
        match options.EvidenceManifest with
        | None -> Ok []
        | Some manifest -> readManifest (Environment.CurrentDirectory) manifest

    let reuse =
        match options.ReuseContext with
        | None -> Ok None
        | Some path ->
            readFile "reuse context" path
            |> Result.bind (text >> readReuseContext >> Result.mapError (sprintf "reuse context: %s"))
            |> Result.map Some

    let outcome =
        readFile "verification plan" options.Plan
        |> Result.bind (text >> readPlan options.ExpectedDigest)
        |> Result.bind (fun (header, plan) -> evidence |> Result.map (fun results -> header, attachEvidence results plan))
        |> Result.bind (fun (header, plan) -> reuse |> Result.map (fun reuse -> header, plan, planContext plan options.SourceSha options.RunId reuse))
        |> Result.bind (fun (header, plan, scheduleContext) ->
            match Ordo.Core.VerificationSchedule.schedule scheduleContext plan with
            | Ok result -> Ok(header, result)
            | Error errors -> Error(errors |> List.map (sprintf "%A") |> String.concat "; "))

    match outcome with
    | Error message ->
        let line =
            if asJson then render (JObject [ "schema", JString "ordo.verification-schedule-refusal/1"; "reason", JString message ])
            else "verification schedule refused: " + message

        1, (if asJson then [ line ] else []), (if asJson then [] else [ line ])
    | Ok(header, result) ->
        let document = ScheduleWire.render result

        let written =
            match options.Output with
            | None -> Ok()
            | Some path ->
                try
                    File.WriteAllText(path, document + "\n")
                    Ok()
                with
                | :? IOException as ex -> Error ex.Message
                | :? UnauthorizedAccessException as ex -> Error ex.Message

        match written with
        | Error message -> 1, [], [ "could not write schedule: " + message ]
        | Ok() when asJson -> 0, [ document ], []
        | Ok() ->
            let waves = result.IndependentWork |> List.map List.length |> List.map string |> String.concat "/"

            0,
            [ sprintf "Verification schedule for %s (%s)" header.PlanId result.PlanDigest
              sprintf "  requirements: %d; acceptance blockers: %d" result.Requirements.Length result.AcceptanceBlockers.Length
              sprintf "  dependencies: %d (unknown targets: %d); cycles: %d (%d requirement(s) behind them)" result.Dependencies.Length result.UnknownDependencies.Length result.Cycles.Length result.CycleBlocked.Length
              sprintf "  implementation tasks: %d; required verification: %d; stale evidence: %d" result.ImplementationTasks.Length result.RequiredVerification.Length result.StaleEvidence.Length
              sprintf "  independent waves: %d (sizes %s)" result.IndependentWork.Length waves
              sprintf "  schedule digest: %s" (ScheduleWire.digest result) ],
            []
