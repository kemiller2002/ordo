/// Read-only ECIR validation adapter. It is deliberately not an approval
/// authority: a caller cannot grant decision authority via a CLI flag.
module Sde.Cli.Ecir

open System
open System.IO
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire

let run (asJson: bool) (manifestPath: string) (blueprintPath: string) (cohort: string option) =
    let readFile label path =
        try
            if not (File.Exists path) then Error (label + " missing: " + path)
            else Ok(File.ReadAllText path)
        with :? IOException as ex -> Error(label + ": " + ex.Message)

    let outcome =
        readFile "source manifest" manifestPath
        |> Result.bind readManifest
        |> Result.bind (fun manifest ->
            readFile "construction blueprint" blueprintPath
            |> Result.bind readBlueprint
            |> Result.bind (fun blueprint ->
                match validatePinned manifest blueprint with
                | Error errors -> Error(String.concat "; " errors)
                | Ok digest ->
                    match cohort with
                    | None -> Ok digest
                    | Some id ->
                        // Never infer approval from the model or command line.
                        match validateCohort manifest blueprint id Set.empty with
                        | [] -> Ok digest
                        | issues -> Error(String.concat "; " (issues |> List.map (sprintf "%A")))))

    let escaped (value: string) =
        Ordo.Core.Json.JString value |> Ordo.Core.Json.render

    match outcome with
    | Ok digest ->
        let message =
            if asJson then
                sprintf "{\"schemaVersion\":\"ecir.validate/1\",\"status\":\"validated\",\"blueprintDigest\":%s,\"executionAuthorized\":false}" (escaped digest)
            else sprintf "ECIR validated: %s (execution authorization not granted)" digest
        0, [ message ], []
    | Error issue ->
        let message =
            if asJson then sprintf "{\"schemaVersion\":\"ecir.validate/1\",\"status\":\"rejected\",\"reason\":%s}" (escaped issue)
            else "ECIR refused: " + issue
        1, (if asJson then [ message ] else []), (if asJson then [] else [ message ])

/// Construct a source-complete, intentionally non-executable starter
/// blueprint. An already existing output is NEVER modified: revisions must
/// be new immutable artifacts with explicit source/decision history.
let scaffoldFile (asJson: bool) (manifestPath: string) (outputPath: string) =
    let outcome =
        try
            if not (File.Exists manifestPath) then
                Error("source manifest missing: " + manifestPath)
            elif File.Exists outputPath then
                Error("output already exists; never overwrite an ECIR revision: " + outputPath)
            else
                let proposal =
                    File.ReadAllText manifestPath
                    |> readManifest
                    |> Result.mapError List.singleton
                    |> Result.bind scaffold

                match proposal with
                | Error reasons -> Error(String.concat "; " reasons)
                | Ok blueprint ->
                    let content = encodeBlueprint blueprint
                    // CreateNew enforces immutability even if the file appears
                    // between the existence check and the write.
                    use stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                    use writer = new StreamWriter(stream, System.Text.Encoding.UTF8)
                    writer.Write(content)
                    writer.Flush()
                    Ok(blueprintDigest blueprint, blueprint.Requirements.Length)
        with
        | :? IOException as problem -> Error("cannot write ECIR scaffold: " + problem.Message)
        | :? UnauthorizedAccessException as problem -> Error("cannot write ECIR scaffold: " + problem.Message)

    let quoted (value: string) = Ordo.Core.Json.JString value |> Ordo.Core.Json.render

    match outcome with
    | Ok(digest, count) ->
        let message =
            if asJson then
                sprintf "{\"schemaVersion\":\"ecir.scaffold/1\",\"status\":\"unresolved\",\"requirementsRepresented\":%d,\"blueprintDigest\":%s,\"output\":%s,\"executionAuthorized\":false}" count (quoted digest) (quoted outputPath)
            else
                sprintf "ECIR draft: %d source requirements preserved, all unresolved; %s (%s). No execution authorized." count outputPath digest
        0, [ message ], []
    | Error issue ->
        let message =
            if asJson then
                sprintf "{\"schemaVersion\":\"ecir.scaffold/1\",\"status\":\"rejected\",\"reason\":%s}" (quoted issue)
            else
                "ECIR scaffold refused: " + issue
        1, (if asJson then [ message ] else []), (if asJson then [] else [ message ])
