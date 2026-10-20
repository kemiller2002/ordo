/// Read-only ECIR validation adapter. It is deliberately not an approval
/// authority: a caller cannot grant decision authority via a CLI flag.
module Sde.Cli.Ecir

open System
open System.IO
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire

module Contract = Ordo.Core.ConstructibilityContract

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
                    let modeled =
                        blueprint.Requirements
                        |> List.filter (fun row -> row.Disposition = Modeled)
                        |> List.length
                    let unresolved =
                        blueprint.Requirements
                        |> List.filter (fun row ->
                            match row.Disposition with Unresolved _ -> true | _ -> false)
                        |> List.length
                    let deferred =
                        blueprint.Requirements
                        |> List.filter (fun row ->
                            match row.Disposition with Deferred _ -> true | _ -> false)
                        |> List.length
                    let counts = digest, manifest.Requirements.Length, blueprint.Requirements.Length, modeled, unresolved, deferred
                    match cohort with
                    | None -> Ok counts
                    | Some id ->
                        // Never infer approval from the model or command line.
                        match validateCohort manifest blueprint id Set.empty with
                        | [] -> Ok counts
                        | issues -> Error(String.concat "; " (issues |> List.map (sprintf "%A")))))

    let escaped (value: string) =
        Ordo.Core.Json.JString value |> Ordo.Core.Json.render

    match outcome with
    | Ok(digest, imported, represented, modeled, unresolved, deferred) ->
        let message =
            if asJson then
                sprintf "{\"schemaVersion\":\"ecir.validate/1\",\"status\":\"trace-validated\",\"sourceRequirements\":%d,\"representedRequirements\":%d,\"modeledRequirements\":%d,\"unresolvedRequirements\":%d,\"deferredRequirements\":%d,\"blueprintDigest\":%s,\"executionAuthorized\":false}" imported represented modeled unresolved deferred (escaped digest)
            else
                sprintf "ECIR source trace validated: %d/%d represented; %d modeled, %d unresolved, %d deferred; %s. Execution authorization not granted."
                    represented imported modeled unresolved deferred digest
        0, [ message ], []
    | Error issue ->
        let message =
            if asJson then sprintf "{\"schemaVersion\":\"ecir.validate/1\",\"status\":\"rejected\",\"reason\":%s}" (escaped issue)
            else "ECIR refused: " + issue
        1, (if asJson then [ message ] else []), (if asJson then [] else [ message ])

/// Manifest-free admission of a published contract (`ordo.constructibility-
/// identity/1` on success, `ordo.constructibility-diagnostics/1` on refusal).
/// With an expected digest it also detects a stale pinned reference. With one
/// or more requirement keys it is readiness mode (REL-GATE-002): the work that
/// completes those requirements must be ready under the authorized decision
/// IDs (`ordo.constructibility-readiness/1` on success). The IDs state what the
/// caller's own authority approved; the CLI grants nothing. Exit 0 admitted
/// (or ready), 1 refused. Neither is execution authority.
let checkContract
    (asJson: bool)
    (blueprintPath: string)
    (expectedDigest: string option)
    (requirementKeys: string list)
    (authorizedDecisionIds: string list)
    =
    let refusal code message : string =
        Contract.encodeDiagnostics
            { Contract.DiagnosticSet.SchemaVersion = Contract.DiagnosticsSchemaVersion
              Contract.DiagnosticSet.Diagnostics =
                [ { Contract.Diagnostic.Code = code; Contract.Diagnostic.Subject = Some blueprintPath; Contract.Diagnostic.Message = message } ] }
    let assess (json: string) =
        if List.isEmpty requirementKeys then
            Contract.admitPinnedContract expectedDigest json
            |> Result.map (fun identity ->
                Contract.encodeContractIdentity identity,
                sprintf "Contract admitted: %s %s; %d requirements, %d decisions, %d conflicts. Execution authorization not granted."
                    identity.ContractSchemaVersion identity.ContractDigest identity.RequirementKeys.Length
                    identity.DecisionIds.Length identity.ConflictIds.Length)
        else
            Contract.assessPinnedReadiness expectedDigest json (Set.ofList requirementKeys) (Set.ofList authorizedDecisionIds)
            |> Result.map (fun ready ->
                let view = Contract.readyScopeView ready
                Contract.encodeReadyScope ready,
                sprintf "Contract ready: %s %s; %d requirements in scope, %d required decisions authorized. Execution authorization not granted."
                    view.Identity.ContractSchemaVersion view.Identity.ContractDigest view.RequirementKeys.Length
                    view.RequiredDecisionIds.Length)
    let outcome =
        try
            if not (File.Exists blueprintPath) then
                Error(refusal "ORDO.CONTRACT.MISSING_FILE" "contract file missing")
            else
                File.ReadAllText blueprintPath |> assess |> Result.mapError Contract.encodeDiagnostics
        with :? IOException as ex ->
            Error(refusal "ORDO.CONTRACT.UNREADABLE_FILE" ex.Message)

    match outcome with
    | Ok(json, _) when asJson -> 0, [ json ], []
    | Ok(_, text) -> 0, [ text ], []
    | Error diagnostics when asJson -> 1, [ diagnostics ], []
    | Error diagnostics -> 1, [], [ "Contract refused:\n" + diagnostics ]

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

/// Pattern recognition report (`ordo.pattern-recognition/1`) and, with
/// `--packets`, one `ordo.delegation-contract/1` file per delegation packet.
/// The approvals file only shapes this report: unapproved decisions show as
/// DecisionRequired, and nothing here authorizes construction.
let recognizeFiles (asJson: bool) (options: Sde.Cli.Args.EcirRecognizeOptions) =
    let readFile label path =
        try
            if not (File.Exists path) then Error(label + " missing: " + path) else Ok(File.ReadAllText path)
        with :? IOException as ex -> Error(label + ": " + ex.Message)
    let optional label reader path =
        match path with
        | None -> Ok None
        | Some p -> readFile label p |> Result.bind reader |> Result.map Some
    let approvals (text: string) =
        match Ordo.Core.Json.parse text with
        | Ok(Ordo.Core.Json.JArray items) ->
            items
            |> List.map (function Ordo.Core.Json.JString id -> Ok id | _ -> Error "approvals: expected an array of decision ids")
            |> List.fold (fun acc next -> Result.bind (fun xs -> Result.map (fun x -> x :: xs) next) acc) (Ok [])
            |> Result.map Set.ofList
        | _ -> Error "approvals: expected an array of decision ids"
    let outcome =
        readFile "source manifest" options.Manifest
        |> Result.bind readManifest
        |> Result.bind (fun manifest ->
            readFile "construction blueprint" options.Blueprint
            |> Result.bind readBlueprint
            |> Result.bind (fun blueprint ->
                optional "decision records" Ordo.Core.ConstructionDecisions.readDecisionRecords options.Decisions
                |> Result.bind (fun records ->
                    optional "approvals" approvals options.Approvals
                    |> Result.bind (fun approved ->
                        optional "registry pin" Ordo.Core.PatternRecognitionWire.readPin options.Pin
                        |> Result.bind (fun pin ->
                            let input: Ordo.Core.PatternRecognition.RecognitionInput =
                                { Manifest = manifest
                                  Blueprint = blueprint
                                  CohortId = options.Cohort
                                  AuthorizedDecisionIds = approved |> Option.defaultValue Set.empty
                                  Records = records
                                  Policy =
                                    { AllowedPaths = options.AllowedPaths
                                      ArchitectureConstraints = []
                                      EscalateOpenQuestions = options.EscalateOpenQuestions } }
                            Ordo.Core.PatternRecognitionWire.recognize Ordo.Core.PatternRegistry.BuiltIn.registry pin input
                            |> Result.mapError (List.map (sprintf "%A") >> String.concat "; "))))))
    let write (report: Ordo.Core.PatternRecognition.RecognitionReport) =
        match options.PacketsDirectory with
        | None -> Ok []
        | Some dir ->
            try
                Directory.CreateDirectory dir |> ignore
                report.Packets
                |> List.map (fun p ->
                    let path = Path.Combine(dir, p.Id + ".json")
                    File.WriteAllText(path, Ordo.Core.PatternRecognitionWire.encodePacket p)
                    path)
                |> Ok
            with
            | :? IOException as e -> Error("cannot write packets: " + e.Message)
            | :? UnauthorizedAccessException as e -> Error("cannot write packets: " + e.Message)
    let quoted (value: string) = Ordo.Core.Json.JString value |> Ordo.Core.Json.render
    match outcome |> Result.bind (fun report -> write report |> Result.map (fun paths -> report, paths)) with
    | Ok(report, paths) ->
        let m = report.Measurements
        let message =
            if asJson then Ordo.Core.PatternRecognitionWire.encodeReport report
            else
                sprintf "Recognized %d requirements of %s: %d constructible, %d awaiting a decision, %d delegated, %d blocked, %d excluded; %d packet(s), %d bytes%s. Generation %s. Execution authorization not granted."
                    m.AssignedRequirements report.CohortId m.Constructible m.DecisionRequired m.Delegated m.Blocked m.Excluded
                    m.DelegationPackets m.DelegationBytes
                    (match paths with [] -> "" | ps -> " written to " + String.concat ", " ps)
                    (if report.GenerationWorthwhile then "worthwhile" else "skipped (nothing constructible)")
        0, [ message ], []
    | Error issue ->
        let message =
            if asJson then sprintf "{\"schemaVersion\":\"ordo.pattern-recognition/1\",\"status\":\"rejected\",\"reason\":%s}" (quoted issue)
            else "Recognition refused: " + issue
        1, (if asJson then [ message ] else []), (if asJson then [] else [ message ])
