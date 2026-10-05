/// `sde boundary assess` (`ordo boundary assess` in the native build): the
/// host for Ordo.Core's boundary-amplification assessment (GH-52,
/// DF-SDE-2026-0016).
///
/// This file is an adapter. It reads the four input documents, optionally
/// reads changed source files to extract their imports, calls the pure
/// `BoundaryAmplification.assess`, and renders the result. Every judgement
/// is made in Ordo.Core.
///
/// Exit codes: 0 whenever an assessment was produced — a High risk is a
/// successful assessment, and gating on it is the consumer's decision;
/// 2 when the arguments were not understood; 6 when an input document is
/// unreadable, malformed or inconsistent with the boundary map.
module Sde.Cli.Boundary

open System
open System.IO
open Ordo.Core
open Ordo.Core.BoundaryAmplification

type private Failure = { Message: string }

let private readFile (label: string) (path: string) =
    try
        Ok(File.ReadAllText path)
    with ex ->
        Error { Message = sprintf "cannot read %s file %s: %s" label path ex.Message }

let private decoded label path decode =
    readFile label path
    |> Result.bind (fun text ->
        decode text
        |> Result.mapError (fun e ->
            { Message = sprintf "invalid %s file %s: %s" label path (BoundaryAmplificationWire.DecodeError.describe e) }))

/// Namespaces a source file imports: F# `open X` and C# `using X;`.
/// Aliases (`using A = B;`), `using static` and `open type` are skipped:
/// they name types, not the namespaces indicators are written against.
let extractImports (fileName: string) (lines: string seq) : string list =
    let extension =
        Path.GetExtension fileName |> Option.ofObj |> Option.defaultValue "" |> fun e -> e.ToLowerInvariant()

    let firstToken (text: string) =
        text.Split([| ' '; '\t'; ';'; '/' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.tryHead

    let fsharp (line: string) =
        let t = line.Trim()

        if t.StartsWith "open " && not (t.StartsWith "open type ") then
            firstToken (t.Substring 5) |> Option.map (fun n -> n.Replace("global.", ""))
        else
            None

    let csharp (line: string) =
        let t = line.Trim()

        if t.StartsWith "using " && t.EndsWith ";" && not (t.StartsWith "using static ") && not (t.Contains "=") && not (t.Contains "(") then
            firstToken (t.Substring 6) |> Option.map (fun n -> n.Replace("global::", ""))
        else
            None

    let extract =
        match extension with
        | ".fs"
        | ".fsi"
        | ".fsx" -> Some fsharp
        | ".cs" -> Some csharp
        | _ -> None

    match extract with
    | None -> []
    | Some f -> lines |> Seq.choose f |> Seq.distinct |> Seq.sort |> List.ofSeq

let private withImports (root: string option) (changes: ChangeSet) : ChangeSet =
    match root with
    | None -> changes
    | Some directory ->
        let enrich (file: ChangedFile) =
            if not file.Imports.IsEmpty then
                file
            else
                let full = Path.Combine(directory, normalizePath file.Path)
                // A deleted or unreadable file has no imports to observe;
                // its path is still classified.
                if File.Exists full then
                    try
                        { file with Imports = extractImports full (File.ReadLines full) }
                    with _ ->
                        file
                else
                    file

        { changes with Files = changes.Files |> List.map enrich }

let private generator (version: string) (document: Json.JsonValue) =
    match document with
    | Json.JObject members ->
        Json.JObject(members @ [ "generator", Json.JObject [ "tool", Json.JString "ordo"; "version", Json.JString version ] ])
    | other -> other

/// Runs `boundary assess`; returns exit code, stdout lines, stderr lines.
let run (json: bool) (version: string) (options: Args.BoundaryAssessOptions) : int * string list * string list =
    let policy =
        match options.Policy with
        | None -> Ok(AmplificationPolicy.defaults, "default")
        | Some path -> decoded "policy" path BoundaryAmplificationWire.decodePolicy |> Result.map (fun p -> p, path)

    let result =
        decoded "boundary map" options.Map BoundaryAmplificationWire.decodeMap
        |> Result.bind (fun map ->
            decoded "expectation" options.Expected BoundaryAmplificationWire.decodeExpectation
            |> Result.bind (fun expectation ->
                decoded "changed-files" options.Changed BoundaryAmplificationWire.decodeChanges
                |> Result.bind (fun changes ->
                    policy
                    |> Result.bind (fun (policy, source) ->
                        assess map policy expectation (withImports options.Root changes)
                        |> Result.map (fun a -> a, source)
                        |> Result.mapError (fun errors ->
                            { Message =
                                errors
                                |> List.map BoundaryAmplificationWire.describeAssessmentError
                                |> String.concat "; " })))))

    match result with
    | Ok(assessment, source) ->
        if json then
            Sde.Core.Lifecycle.ExitCodes.success,
            [ BoundaryAmplificationWire.encodeAssessment source assessment |> generator version |> Json.render ],
            []
        else
            Sde.Core.Lifecycle.ExitCodes.success, BoundaryAmplificationWire.renderText assessment, []
    | Error failure ->
        let code = Sde.Core.Lifecycle.ExitCodes.invalidInput

        if json then
            code, [ Sde.Core.Json.render (Sde.Core.Output.errorToJson "boundary assess" failure.Message code) ], []
        else
            code, [], [ failure.Message ]
