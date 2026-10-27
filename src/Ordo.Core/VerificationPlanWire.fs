/// Reader for the compiled verification plan Ordo schedules
/// (`indy-init.verification-plan/1`) and for the acceptance results that
/// say how far its obligations have been verified
/// (`causa.acceptance-evidence/1`).
///
/// Ordo does not admit the plan: Praxis does (intake, plan-identity digest
/// recomputation, provenance). Ordo reads the admitted bytes, optionally
/// checks they carry the digest the admission pinned, and maps them onto
/// `VerificationSchedule.VerificationPlan`. Unknown members are ignored so an
/// additive change to the plan does not break scheduling; a missing or
/// mistyped member the schedule needs is refused with its JSON path.
module Ordo.Core.VerificationPlanWire

open System
open Ordo.Core.Json
open Ordo.Core.VerificationSchedule

[<Literal>]
let PlanSchema = "indy-init.verification-plan/1"

[<Literal>]
let EvidenceSchema = "causa.acceptance-evidence/1"

/// Binding keys every policy in `indy-init.verification-plan/1` requires
/// evidence to match. All three policies (FP-LIVE-PER-COMMIT,
/// FP-REVIEW-PER-COMMIT, FP-DETERMINISTIC-INPUT-DIGEST) bind evidence to the
/// candidate source commit and to the run ("never reused from an earlier
/// rehearsal"); the plan's `boundTo` entries are prose and are carried, not
/// interpreted. `integrity` is the evidence bytes matching the pinned
/// evidence manifest.
let bindingsForPolicy (_policy: string) = [ "integrity"; "runId"; "sourceSha" ]

type PlanHeader =
    { PlanId: string
      Digest: string
      SourceRepository: string
      SourceCommit: string
      BaselineSha256: string }

/// One `causa.acceptance-evidence/1` result as the caller read it, with
/// whether its bytes matched the pinned evidence manifest entry.
type EvidenceResult =
    { SubjectType: string
      SubjectId: string
      Status: string
      RunId: string
      SourceSha: string
      IntegrityVerified: bool }

let private member' path name value =
    match value with
    | JObject members ->
        match members |> List.filter (fst >> (=) name) with
        | [ _, found ] -> Ok found
        | [] -> Error(sprintf "%s: missing member %s" path name)
        | _ -> Error(sprintf "%s: duplicate member %s" path name)
    | _ -> Error(sprintf "%s: expected object" path)

let private text path name value =
    member' path name value
    |> Result.bind (function
        | JString s when not (String.IsNullOrWhiteSpace s) -> Ok s
        | _ -> Error(sprintf "%s.%s: expected nonblank string" path name))

let private array path name value =
    member' path name value
    |> Result.bind (function
        | JArray items -> Ok items
        | _ -> Error(sprintf "%s.%s: expected array" path name))

let private texts path name value =
    array path name value
    |> Result.bind (fun items ->
        items
        |> List.mapi (fun i item ->
            match item with
            | JString s when not (String.IsNullOrWhiteSpace s) -> Ok s
            | _ -> Error(sprintf "%s.%s[%d]: expected nonblank string" path name i))
        |> List.fold (fun acc next -> Result.bind (fun xs -> Result.map (fun x -> x :: xs) next) acc) (Ok [])
        |> Result.map List.rev)

let private traverse f items =
    items
    |> List.mapi f
    |> List.fold (fun acc next -> Result.bind (fun xs -> Result.map (fun x -> x :: xs) next) acc) (Ok [])
    |> Result.map List.rev

let private readObligation mandatory method' path (value: JsonValue) =
    text path "id" value
    |> Result.bind (fun id ->
        member' path "testIdentity" value
        |> Result.bind (fun identity ->
            let ipath = path + ".testIdentity"

            text ipath "status" identity
            |> Result.bind (fun status ->
                array ipath "tests" identity
                |> Result.bind (traverse (fun i t -> text (sprintf "%s.tests[%d]" ipath i) "id" t))
                |> Result.bind (fun tests ->
                    match status, tests with
                    | "bound", _ :: _ -> Ok(Identified(tests |> List.sortWith (fun a b -> String.CompareOrdinal(a, b)) |> String.concat ", "))
                    | "bound", [] -> Error(sprintf "%s: status bound with no tests" ipath)
                    | "missing", _ -> Ok(MissingTest "no test identity bound in the verification plan")
                    | other, _ -> Error(sprintf "%s.status: unsupported %s" ipath other))))
        |> Result.map (fun test -> { Id = id; Mandatory = mandatory; Method = method'; Test = test; Evidence = [] }))

let private readRequirement (index: int) (value: JsonValue) =
    let path = sprintf "$.requirements[%d]" index

    text path "requirement" value
    |> Result.bind (fun id ->
        text path "priority" value
        |> Result.bind (fun priority ->
            member' path "exclusion" value
            |> Result.bind (text (path + ".exclusion") "status")
            |> Result.bind (fun exclusion ->
                match exclusion with
                | "in-scope" -> Ok true
                | "excluded" -> Ok false
                | other -> Error(sprintf "%s.exclusion.status: unsupported %s" path other))
            |> Result.bind (fun mandatory ->
                member' path "verificationMethod" value
                |> Result.bind (fun methodValue ->
                    let mpath = path + ".verificationMethod"

                    texts mpath "gates" methodValue
                    |> Result.bind (fun gates ->
                        array mpath "rules" methodValue
                        |> Result.bind (traverse (fun i r -> text (sprintf "%s.rules[%d]" mpath i) "id" r))
                        |> Result.map (fun rules ->
                            sprintf "gates: %s; rules: %s" (String.concat ", " gates) (String.concat ", " rules))))
                |> Result.bind (fun method' ->
                    array path "obligations" value
                    |> Result.bind (traverse (fun i o -> readObligation (priority = "P0" && mandatory) method' (sprintf "%s.obligations[%d]" path i) o)))
                |> Result.bind (fun obligations ->
                    member' path "dependencies" value
                    |> Result.bind (texts (path + ".dependencies") "requirements")
                    |> Result.bind (fun dependsOn ->
                        member' path "freshness" value
                        |> Result.bind (text (path + ".freshness") "policy")
                        |> Result.map (fun policy ->
                            { Id = id
                              Priority = (if mandatory then priority else priority + " (excluded)")
                              Obligations = obligations
                              DependsOn = dependsOn
                              FreshnessBindings = bindingsForPolicy policy }))))))

/// Reads an admitted plan. With `expectedDigest`, refuses a plan whose
/// declared `planIdentity.digest` differs (a stale pinned reference).
let readPlan (expectedDigest: string option) (json: string) : Result<PlanHeader * VerificationPlan, string> =
    match parse json with
    | Error error -> Error(sprintf "plan is not JSON: %A" error)
    | Ok root ->
        text "$" "schema" root
        |> Result.bind (fun schema ->
            if schema <> PlanSchema then Error(sprintf "$.schema: expected %s, found %s" PlanSchema schema) else Ok schema)
        |> Result.bind (fun schema ->
            member' "$" "planIdentity" root
            |> Result.bind (fun identity ->
                text "$.planIdentity" "planId" identity
                |> Result.bind (fun planId -> text "$.planIdentity" "digest" identity |> Result.map (fun digest -> planId, digest)))
            |> Result.bind (fun (planId, digest) ->
                match expectedDigest with
                | Some expected when not (String.Equals(expected.Replace("sha256:", ""), digest, StringComparison.Ordinal)) ->
                    Error(sprintf "plan digest %s differs from the admitted digest %s" digest expected)
                | _ -> Ok(planId, digest))
            |> Result.bind (fun (planId, digest) ->
                member' "$" "source" root
                |> Result.bind (fun source ->
                    text "$.source" "repository" source
                    |> Result.bind (fun repository ->
                        text "$.source" "commit" source
                        |> Result.bind (fun commit ->
                            text "$.source" "baselineSha256" source
                            |> Result.map (fun baseline ->
                                { PlanId = planId
                                  Digest = digest
                                  SourceRepository = repository
                                  SourceCommit = commit
                                  BaselineSha256 = baseline })))))
            |> Result.bind (fun header ->
                array "$" "requirements" root
                |> Result.bind (traverse readRequirement)
                |> Result.map (fun requirements ->
                    header, { Schema = schema; PlanDigest = "sha256:" + header.Digest; Requirements = requirements })))

/// Reads one `causa.acceptance-evidence/1` result.
let readEvidence (integrityVerified: bool) (json: string) : Result<EvidenceResult, string> =
    match parse json with
    | Error error -> Error(sprintf "evidence is not JSON: %A" error)
    | Ok root ->
        text "$" "schema" root
        |> Result.bind (fun schema ->
            if schema <> EvidenceSchema then Error(sprintf "$.schema: expected %s, found %s" EvidenceSchema schema)
            else
                match text "$" "subject_type" root, text "$" "subject_id" root, text "$" "status" root, text "$" "run_id" root, text "$" "source_sha" root with
                | Ok subjectType, Ok subjectId, Ok status, Ok runId, Ok sourceSha ->
                    Ok
                        { SubjectType = subjectType
                          SubjectId = subjectId
                          Status = status
                          RunId = runId
                          SourceSha = sourceSha
                          IntegrityVerified = integrityVerified }
                | Error e, _, _, _, _
                | _, Error e, _, _, _
                | _, _, Error e, _, _
                | _, _, _, Error e, _
                | _, _, _, _, Error e -> Error e)

let private outcomeOf status =
    match status with
    | "PASS" -> EvidenceOutcome.Pass
    | "FAIL" -> EvidenceOutcome.Fail
    | "BLOCKED" -> EvidenceOutcome.Blocked
    | _ -> EvidenceOutcome.NotRun

/// Attaches requirement-level results to every obligation of their subject
/// requirement. A result for a subject the plan does not contain changes
/// nothing; results for gates and defects are not requirement evidence.
let attachEvidence (results: EvidenceResult list) (plan: VerificationPlan) : VerificationPlan =
    let bySubject =
        results
        |> List.filter (fun r -> r.SubjectType = "requirement")
        |> List.groupBy (fun r -> r.SubjectId)
        |> Map.ofList

    let recorded (r: EvidenceResult) =
        { Outcome = outcomeOf r.Status
          Bindings =
            Map.ofList
                [ "integrity", (if r.IntegrityVerified then "verified" else "mismatch")
                  "runId", r.RunId
                  "sourceSha", r.SourceSha ] }

    { plan with
        Requirements =
            plan.Requirements
            |> List.map (fun requirement ->
                match Map.tryFind requirement.Id bySubject with
                | None -> requirement
                | Some found ->
                    let evidence = found |> List.map recorded
                    { requirement with Obligations = requirement.Obligations |> List.map (fun o -> { o with Evidence = o.Evidence @ evidence }) }) }

/// The subject the schedule is computed for. A value left `None` is not
/// judged; callers that supply evidence should supply both.
let context (sourceSha: string option) (runId: string option) =
    { Current =
        Map.ofList
            [ yield "integrity", "verified"
              match sourceSha with
              | Some sha -> yield "sourceSha", sha
              | None -> ()
              match runId with
              | Some run -> yield "runId", run
              | None -> () ] }
