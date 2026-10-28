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

/// The freshness policies of `indy-init.verification-plan/1`. Any other
/// policy id is refused when the plan is read (DF-SDE-2026-0026): the plan
/// reader already refuses unsupported enumeration values with their JSON
/// path, and a refusal can never grant reuse. The plan's `boundTo` entries
/// are prose and are carried, not interpreted.
[<RequireQualifiedAccess>]
type FreshnessPolicy =
    /// Live provider or deployed-site run of the exact candidate commit.
    | LivePerCommit
    /// Independent review naming the reviewed commit.
    | ReviewPerCommit
    /// Deterministic test whose result depends only on its declared inputs.
    | DeterministicInputDigest

let parsePolicy (policy: string) =
    match policy with
    | "FP-LIVE-PER-COMMIT" -> Some FreshnessPolicy.LivePerCommit
    | "FP-REVIEW-PER-COMMIT" -> Some FreshnessPolicy.ReviewPerCommit
    | "FP-DETERMINISTIC-INPUT-DIGEST" -> Some FreshnessPolicy.DeterministicInputDigest
    | _ -> None

/// Per-run binding: the evidence bytes match the pinned manifest
/// (`integrity`) and the evidence belongs to the run and the candidate
/// commit being judged. Every policy has it.
let PerRunBindings = [ "integrity"; "runId"; "sourceSha" ]

/// Content-addressed reuse binding of FP-DETERMINISTIC-INPUT-DIGEST:
/// integrity, the digest of the declared inputs of the requirement's tests,
/// and the plan and rehearsal identity, so evidence is never reused from
/// another plan or an earlier rehearsal. Not `runId`, and not `sourceSha`:
/// the input digest covers the tested source paths.
let ContentBindings = [ "integrity"; "inputDigest"; "planDigest"; "rehearsalId" ]

/// The binding keys a requirement's evidence is judged on: `Bindings` always,
/// `Reuse` as the content-addressed alternative (empty: never reused).
type PolicyBindings = { Bindings: string list; Reuse: string list }

/// Bindings for a known policy. `reuseDeclared` is the plan's own
/// `freshnessPolicies[].reusable` for that policy: a deterministic policy the
/// plan does not declare reusable keeps the per-run binding only. Live and
/// review evidence are never reused, whatever the plan declares.
let bindingsFor (reuseDeclared: bool) (policy: FreshnessPolicy) =
    match policy with
    | FreshnessPolicy.LivePerCommit
    | FreshnessPolicy.ReviewPerCommit -> { Bindings = PerRunBindings; Reuse = [] }
    | FreshnessPolicy.DeterministicInputDigest ->
        { Bindings = PerRunBindings
          Reuse = (if reuseDeclared then ContentBindings else []) }

/// Bindings for a policy id as the plan names it; an unknown id is refused.
let bindingsForPolicy (reuseDeclared: bool) (policy: string) : Result<PolicyBindings, string> =
    match parsePolicy policy with
    | Some known -> Ok(bindingsFor reuseDeclared known)
    | None -> Error(sprintf "unsupported freshness policy %s" policy)

type PlanHeader =
    { PlanId: string
      Digest: string
      SourceRepository: string
      SourceCommit: string
      BaselineSha256: string }

/// One `causa.acceptance-evidence/1` result as the caller read it, with
/// whether its bytes matched the pinned evidence manifest entry.
/// `InputDigest`, `PlanDigest` and `RehearsalId` are the optional members
/// `input_digest`, `plan_digest` and `rehearsal_id`; evidence without all
/// three is never reused across runs (DF-SDE-2026-0026).
type EvidenceResult =
    { SubjectType: string
      SubjectId: string
      Status: string
      RunId: string
      SourceSha: string
      IntegrityVerified: bool
      InputDigest: string option
      PlanDigest: string option
      RehearsalId: string option }

/// Current values that only a caller can supply for content-addressed reuse:
/// the rehearsal being judged and, per requirement id, the digest of the
/// declared inputs of the tests that verify it (`ordo.verification-reuse-context/1`).
type ReuseContext =
    { RehearsalId: string
      InputDigests: Map<string, string> }

[<Literal>]
let ReuseContextSchema = "ordo.verification-reuse-context/1"

/// Plan digests compare in their `sha256:`-prefixed form.
let normalizePlanDigest (digest: string) =
    if digest.StartsWith("sha256:", StringComparison.Ordinal) then digest else "sha256:" + digest

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

/// An optional member: absent is `None`; present must be a nonblank string.
let private optionalText path name value =
    match tryMember name value with
    | Ok None -> Ok None
    | Ok(Some(JString s)) when not (String.IsNullOrWhiteSpace s) -> Ok(Some s)
    | Ok(Some _) -> Error(sprintf "%s.%s: expected nonblank string" path name)
    | Error _ -> Error(sprintf "%s: expected object" path)

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

/// Whether an obligation's own acceptance contract (plan revision 2,
/// `acceptanceContract.freshness.policy`, the strictest of the requirement
/// policy and the obligation's evidence-category policies) permits
/// content-addressed reuse. No `acceptanceContract` (revision 1): inherit the
/// requirement. A present contract must name a known policy; an unknown or
/// mistyped one refuses the plan, so it can never grant reuse.
let private obligationReuseAllowed (reusable: Set<string>) path (value: JsonValue) =
    match tryMember "acceptanceContract" value with
    | Ok None -> Ok true
    | Ok(Some contract) ->
        let cpath = path + ".acceptanceContract"

        member' cpath "freshness" contract
        |> Result.bind (text (cpath + ".freshness") "policy")
        |> Result.bind (fun policy ->
            bindingsForPolicy (Set.contains policy reusable) policy
            |> Result.map (fun bindings -> not bindings.Reuse.IsEmpty)
            |> Result.mapError (sprintf "%s.freshness.policy: %s" cpath))
    | Error _ -> Error(sprintf "%s: expected object" path)

let private readObligation (reusable: Set<string>) mandatory method' path (value: JsonValue) =
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
        |> Result.bind (fun test ->
            obligationReuseAllowed reusable path value
            |> Result.map (fun reuseAllowed ->
                { Id = id; Mandatory = mandatory; Method = method'; Test = test; Evidence = []; ReuseAllowed = reuseAllowed })))

/// Policy ids the plan's optional `freshnessPolicies` declares
/// `"reusable": true`. An absent array, an entry without `reusable`, or
/// `reusable: false` declares nothing reusable; a mistyped array or entry is
/// refused with its path.
let private declaredReusable (root: JsonValue) : Result<Set<string>, string> =
    match tryMember "freshnessPolicies" root with
    | Ok None -> Ok Set.empty
    | Ok(Some(JArray entries)) ->
        entries
        |> traverse (fun i entry ->
            let path = sprintf "$.freshnessPolicies[%d]" i

            text path "id" entry
            |> Result.bind (fun id ->
                match tryMember "reusable" entry with
                | Ok None -> Ok(id, false)
                | Ok(Some(JBool flag)) -> Ok(id, flag)
                | _ -> Error(sprintf "%s.reusable: expected boolean" path)))
        |> Result.map (List.filter snd >> List.map fst >> Set.ofList)
    | _ -> Error "$.freshnessPolicies: expected array"

let private readRequirement (reusable: Set<string>) (index: int) (value: JsonValue) =
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
                    |> Result.bind (traverse (fun i o -> readObligation reusable (priority = "P0" && mandatory) method' (sprintf "%s.obligations[%d]" path i) o)))
                |> Result.bind (fun obligations ->
                    member' path "dependencies" value
                    |> Result.bind (texts (path + ".dependencies") "requirements")
                    |> Result.bind (fun dependsOn ->
                        member' path "freshness" value
                        |> Result.bind (text (path + ".freshness") "policy")
                        |> Result.bind (fun policy ->
                            bindingsForPolicy (Set.contains policy reusable) policy
                            |> Result.mapError (sprintf "%s.freshness.policy: %s" path))
                        |> Result.map (fun policy ->
                            { Id = id
                              Priority = (if mandatory then priority else priority + " (excluded)")
                              Obligations = obligations
                              DependsOn = dependsOn
                              FreshnessBindings = policy.Bindings
                              ReuseBindings = policy.Reuse }))))))

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
                declaredReusable root
                |> Result.bind (fun reusable -> array "$" "requirements" root |> Result.bind (traverse (readRequirement reusable)))
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
                    optionalText "$" "input_digest" root
                    |> Result.bind (fun inputDigest ->
                        optionalText "$" "plan_digest" root
                        |> Result.bind (fun planDigest ->
                            optionalText "$" "rehearsal_id" root
                            |> Result.map (fun rehearsalId ->
                                { SubjectType = subjectType
                                  SubjectId = subjectId
                                  Status = status
                                  RunId = runId
                                  SourceSha = sourceSha
                                  IntegrityVerified = integrityVerified
                                  InputDigest = inputDigest
                                  PlanDigest = planDigest |> Option.map normalizePlanDigest
                                  RehearsalId = rehearsalId })))
                | Error e, _, _, _, _
                | _, Error e, _, _, _
                | _, _, Error e, _, _
                | _, _, _, Error e, _
                | _, _, _, _, Error e -> Error e)

/// Reads an `ordo.verification-reuse-context/1` document:
/// `{ "schema", "rehearsalId", "inputDigests": { requirementId: digest } }`.
let readReuseContext (json: string) : Result<ReuseContext, string> =
    match parse json with
    | Error error -> Error(sprintf "reuse context is not JSON: %A" error)
    | Ok root ->
        text "$" "schema" root
        |> Result.bind (fun schema ->
            if schema <> ReuseContextSchema then Error(sprintf "$.schema: expected %s, found %s" ReuseContextSchema schema)
            else text "$" "rehearsalId" root)
        |> Result.bind (fun rehearsalId ->
            member' "$" "inputDigests" root
            |> Result.bind (function
                | JObject members ->
                    members
                    |> traverse (fun _ (requirement, value) ->
                        match value with
                        | JString digest when not (String.IsNullOrWhiteSpace digest) -> Ok(requirement, digest)
                        | _ -> Error(sprintf "$.inputDigests.%s: expected nonblank string" requirement))
                    |> Result.bind (fun pairs ->
                        match pairs |> List.countBy fst |> List.tryFind (fun (_, n) -> n > 1) with
                        | Some(duplicate, _) -> Error(sprintf "$.inputDigests: duplicate member %s" duplicate)
                        | None -> Ok(Map.ofList pairs))
                | _ -> Error "$.inputDigests: expected object")
            |> Result.map (fun digests -> { RehearsalId = rehearsalId; InputDigests = digests }))

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
                [ yield "integrity", (if r.IntegrityVerified then "verified" else "mismatch")
                  yield "runId", r.RunId
                  yield "sourceSha", r.SourceSha
                  // Content identity, only when the evidence carries it.
                  match r.InputDigest with
                  | Some digest -> yield "inputDigest", digest
                  | None -> ()
                  match r.PlanDigest with
                  | Some digest -> yield "planDigest", digest
                  | None -> ()
                  match r.RehearsalId with
                  | Some rehearsal -> yield "rehearsalId", rehearsal
                  | None -> () ] }

    { plan with
        Requirements =
            plan.Requirements
            |> List.map (fun requirement ->
                match Map.tryFind requirement.Id bySubject with
                | None -> requirement
                | Some found ->
                    let evidence = found |> List.map recorded
                    { requirement with Obligations = requirement.Obligations |> List.map (fun o -> { o with Evidence = o.Evidence @ evidence }) }) }

let private runValues (sourceSha: string option) (runId: string option) =
    Map.ofList
        [ yield "integrity", "verified"
          match sourceSha with
          | Some sha -> yield "sourceSha", sha
          | None -> ()
          match runId with
          | Some run -> yield "runId", run
          | None -> () ]

/// The subject the schedule is computed for. A value left `None` is not
/// judged; callers that supply evidence should supply both. Grants no reuse.
let context (sourceSha: string option) (runId: string option) = currentOnly (runValues sourceSha runId)

/// The subject the schedule is computed for, for one plan: `context` plus
/// the plan's own digest (`planDigest`, always known) and, with a reuse
/// context, the rehearsal id and each requirement's current input digest.
/// Without a reuse context no evidence can be reused, because the
/// `rehearsalId` and `inputDigest` reuse keys have no current value.
let planContext (plan: VerificationPlan) (sourceSha: string option) (runId: string option) (reuse: ReuseContext option) =
    let current = runValues sourceSha runId |> Map.add "planDigest" (normalizePlanDigest plan.PlanDigest)

    match reuse with
    | None -> currentOnly current
    | Some reuse ->
        { Current = current |> Map.add "rehearsalId" reuse.RehearsalId
          BySubject = reuse.InputDigests |> Map.map (fun _ digest -> Map.ofList [ "inputDigest", digest ]) }
