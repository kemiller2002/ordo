/// The deterministic verification schedule (`ordo.verification-schedule/1`):
/// determinism, completeness (no requirement dropped by the heuristic),
/// cycle detection, stale-evidence classification and priority ordering.
module Ordo.Tests.VerificationScheduleTests

open System
open Xunit
open Ordo.Core.VerificationSchedule
open Ordo.Core.VerificationScheduleWire

// ---------------------------------------------------------------------------
// Fixture.
//
//   R-CORE  <- R-API  <- R-UI
//   R-CORE  <- R-AUDIT
//   R-SOLO  (independent)
//
// R-CORE unblocks R-API, R-UI and R-AUDIT; R-SOLO unblocks nothing.
// ---------------------------------------------------------------------------

let private current = currentOnly (Map.ofList [ "sourceCommit", "abc123"; "baselineSha256", "b1" ])
let private fresh = Map.ofList [ "sourceCommit", "abc123"; "baselineSha256", "b1" ]
let private oldCommit = Map.ofList [ "sourceCommit", "old999"; "baselineSha256", "b1" ]

let private obligation id mandatory test evidence =
    { Id = id; Mandatory = mandatory; Method = "test"; Test = test; Evidence = evidence }

let private requirement id dependsOn obligations =
    { Id = id; Priority = "P0"; Obligations = obligations; DependsOn = dependsOn; FreshnessBindings = [ "sourceCommit"; "baselineSha256" ]; ReuseBindings = [] }

let private passed bindings = [ { Outcome = EvidenceOutcome.Pass; Bindings = bindings } ]

let private fixture () =
    { Schema = "test"
      PlanDigest = "sha256:fixture"
      Requirements =
        [ requirement "R-UI" [ "R-API" ] [ obligation "R-UI.1" true (Identified "ui.spec") [] ]
          requirement "R-SOLO" [] [ obligation "R-SOLO.1" true (Identified "solo.spec") (passed fresh) ]
          requirement "R-API" [ "R-CORE" ] [ obligation "R-API.1" true (MissingTest "no precise test") [] ]
          requirement "R-AUDIT" [ "R-CORE" ] [ obligation "R-AUDIT.1" true (Identified "audit.spec") (passed oldCommit) ]
          requirement
              "R-CORE"
              []
              [ obligation "R-CORE.1" true (Identified "core.a") [ { Outcome = EvidenceOutcome.Fail; Bindings = fresh } ]
                obligation "R-CORE.2" false (Identified "core.b") [] ] ] }

let private run context plan =
    match schedule context plan with
    | Ok value -> value
    | Error errors -> failwithf "schedule refused: %A" errors

// ---------------------------------------------------------------------------
// Determinism.
// ---------------------------------------------------------------------------

[<Fact>]
let ``same plan renders byte-identical schedules`` () =
    let first = render (run current (fixture ()))
    let second = render (run current (fixture ()))
    Assert.Equal(first, second)
    Assert.True(Text.Encoding.UTF8.GetBytes first = Text.Encoding.UTF8.GetBytes second)

[<Fact>]
let ``input order does not change the schedule bytes`` () =
    let plan = fixture ()
    let reversed =
        { plan with
            Requirements =
                plan.Requirements
                |> List.rev
                |> List.map (fun r -> { r with Obligations = List.rev r.Obligations; DependsOn = List.rev r.DependsOn }) }

    Assert.Equal(render (run current plan), render (run current reversed))

[<Fact>]
let ``schedule carries the plan digest and its own digest`` () =
    let text = render (run current (fixture ()))
    Assert.Contains("\"planDigest\": \"sha256:fixture\"", text)
    Assert.Contains("\"scheduleDigest\": \"sha256:", text)
    Assert.Contains("\"schema\": \"ordo.verification-schedule/1\"", text)

// ---------------------------------------------------------------------------
// Completeness.
// ---------------------------------------------------------------------------

let private assertComplete (plan: VerificationPlan) (result: VerificationSchedule) =
    let planIds = plan.Requirements |> List.map (fun r -> r.Id) |> List.sort
    Assert.Equal<string list>(planIds, result.Requirements |> List.map (fun r -> r.Id) |> List.sort)
    // Every requirement is placed exactly once: in one wave or behind a cycle.
    let placed = (List.concat result.IndependentWork) @ result.CycleBlocked
    Assert.Equal<string list>(planIds, placed |> List.sort)
    // Ranks are a permutation of 1..n.
    Assert.Equal<int list>([ 1 .. planIds.Length ], result.Requirements |> List.map (fun r -> r.Rank) |> List.sort)
    // Every unsatisfied obligation yields required verification.
    let unsatisfied =
        result.Requirements
        |> List.collect (fun r -> r.Obligations |> List.filter (fun (_, s) -> s <> ObligationState.Satisfied) |> List.map (fun (o, _) -> r.Id, o))
        |> List.sort
    Assert.Equal<(string * string) list>(unsatisfied, result.RequiredVerification |> List.map (fun w -> w.RequirementId, w.ObligationId) |> List.sort)

[<Fact>]
let ``schedule contains every plan requirement`` () =
    let plan = fixture ()
    assertComplete plan (run current plan)

[<Fact>]
let ``heuristic never drops a requirement in a large generated plan`` () =
    // 120 requirements, with dependencies to earlier ids, unknown ids, a
    // cycle, zero-obligation requirements and requirements nobody depends on.
    let id n = sprintf "REQ-%03d" n
    let requirements =
        [ for n in 0 .. 119 ->
              let deps =
                  [ if n > 0 && n % 3 = 0 then yield id (n / 2)
                    if n % 17 = 0 then yield "REQ-ABSENT"
                    if n = 50 then yield id 51
                    if n = 51 then yield id 50 ]
              let obligations =
                  [ for k in 0 .. (n % 4) - 1 ->
                        obligation (sprintf "%s.%d" (id n) k) (k % 2 = 0) (if k = 1 then MissingTest "none" else Identified "t") [] ]
              requirement (id n) deps obligations ]
    let plan = { Schema = "test"; PlanDigest = "sha256:large"; Requirements = requirements }
    let result = run current plan
    assertComplete plan result
    Assert.Equal(120, result.Requirements.Length)

// ---------------------------------------------------------------------------
// Cycles and unknown dependencies.
// ---------------------------------------------------------------------------

[<Fact>]
let ``dependency cycles are reported and their requirements kept`` () =
    let plan =
        { Schema = "test"
          PlanDigest = "sha256:cycle"
          Requirements =
            [ requirement "A" [ "B" ] [ obligation "A.1" true (Identified "a") [] ]
              requirement "B" [ "A" ] [ obligation "B.1" true (Identified "b") [] ]
              requirement "C" [ "A" ] [ obligation "C.1" true (Identified "c") [] ]
              requirement "S" [ "S" ] [ obligation "S.1" true (Identified "s") [] ]
              requirement "D" [] [ obligation "D.1" true (Identified "d") [] ] ] }

    let result = run current plan
    Assert.Equal<string list list>([ [ "A"; "B" ]; [ "S" ] ], result.Cycles)
    Assert.Equal<string list list>([ [ "D" ] ], result.IndependentWork)
    Assert.Equal<string list>([ "A"; "B"; "C"; "S" ], result.CycleBlocked |> List.sort)
    Assert.True(result.Requirements |> List.filter (fun r -> r.InCycle) |> List.map (fun r -> r.Id) |> List.sort = [ "A"; "B"; "S" ])
    assertComplete plan result
    Assert.Contains("\"cycles\": [", render result)

[<Fact>]
let ``a dependency on a requirement outside the plan is reported, not dropped`` () =
    let plan =
        { Schema = "test"
          PlanDigest = "sha256:unknown"
          Requirements = [ requirement "A" [ "MISSING" ] [ obligation "A.1" true (Identified "a") [] ] ] }

    let result = run current plan
    Assert.Equal<DependencyEdge list>([ { Requirement = "A"; DependsOn = "MISSING" } ], result.UnknownDependencies)
    Assert.Equal<string list list>([ [ "A" ] ], result.IndependentWork)

[<Fact>]
let ``duplicate requirement or obligation ids are refused`` () =
    let plan =
        { Schema = "test"
          PlanDigest = "sha256:dup"
          Requirements =
            [ requirement "A" [] [ obligation "O" true (Identified "a") []; obligation "O" true (Identified "a") [] ]
              requirement "A" [] [] ] }

    match schedule current plan with
    | Ok _ -> failwith "duplicates must be refused"
    | Error errors ->
        Assert.Contains(DuplicateRequirement "A", errors)
        Assert.Contains(DuplicateObligation("A", "O"), errors)

// ---------------------------------------------------------------------------
// Stale evidence.
// ---------------------------------------------------------------------------

[<Fact>]
let ``evidence bound to another commit is stale, never satisfied`` () =
    let freshness = [ "sourceCommit"; "baselineSha256" ]
    let classify evidence = classifyObligation current freshness (obligation "O" true (Identified "t") evidence)
    Assert.Equal(ObligationState.Satisfied, classify (passed fresh))
    Assert.Equal(ObligationState.Stale [ "sourceCommit" ], classify (passed oldCommit))
    Assert.Equal(ObligationState.Stale [ "baselineSha256"; "sourceCommit" ], classify (passed Map.empty))
    Assert.Equal(ObligationState.Stale [ "sourceCommit" ], classify [ { Outcome = EvidenceOutcome.Fail; Bindings = oldCommit } ])
    Assert.Equal(ObligationState.Failing, classify [ { Outcome = EvidenceOutcome.Fail; Bindings = fresh } ])
    Assert.Equal(ObligationState.NotRun, classify [])
    Assert.Equal(ObligationState.TestMissing "x", classifyObligation current freshness (obligation "O" true (MissingTest "x") (passed fresh)))

[<Fact>]
let ``a binding without a current value is not judged stale`` () =
    let partial = currentOnly (Map.ofList [ "baselineSha256", "b1" ])
    Assert.Equal(ObligationState.Satisfied, classifyObligation partial [ "sourceCommit"; "baselineSha256" ] (obligation "O" true (Identified "t") (passed oldCommit)))

[<Fact>]
let ``stale evidence becomes refresh work and blocks acceptance`` () =
    let result = run current (fixture ())
    Assert.Equal<StaleEvidence list>([ { RequirementId = "R-AUDIT"; ObligationId = "R-AUDIT.1"; Mismatched = [ "sourceCommit" ] } ], result.StaleEvidence)
    Assert.Contains("R-AUDIT", result.AcceptanceBlockers)
    Assert.Contains(result.RequiredVerification, fun w -> w.ObligationId = "R-AUDIT.1" && w.Kind = WorkKind.RefreshEvidence)

// ---------------------------------------------------------------------------
// Priority and classification on the small fixture.
// ---------------------------------------------------------------------------

[<Fact>]
let ``priority ranks by mandatory acceptance unblocked then stable id`` () =
    let result = run current (fixture ())
    // R-CORE unblocks its own 1 P0 plus R-API, R-UI, R-AUDIT (1 each) = 4.
    // R-API unblocks 2; R-AUDIT, R-SOLO and R-UI unblock 1 and tie: id order.
    Assert.Equal<string list>([ "R-CORE"; "R-API"; "R-AUDIT"; "R-SOLO"; "R-UI" ], result.Requirements |> List.map (fun r -> r.Id))
    let core = result.Requirements |> List.head
    Assert.Equal({ MandatoryObligations = 1; TransitiveDependents = 3; MandatoryUnblocked = 4 }, core.Score)

[<Fact>]
let ``schedule separates blockers, implementation, verification and waves`` () =
    let result = run current (fixture ())
    Assert.Equal<string list>([ "R-CORE"; "R-API"; "R-AUDIT"; "R-UI" ], result.AcceptanceBlockers)
    Assert.Equal<(string * WorkKind) list>(
        [ "R-CORE.1", WorkKind.FixImplementation; "R-API.1", WorkKind.AuthorTest ],
        result.ImplementationTasks |> List.map (fun w -> w.ObligationId, w.Kind))
    Assert.Equal<string list list>([ [ "R-CORE"; "R-SOLO" ]; [ "R-API"; "R-AUDIT" ]; [ "R-UI" ] ], result.IndependentWork)
    Assert.Equal(3, result.Dependencies.Length)
    // A non-mandatory unrun obligation is verification work but not a blocker on its own.
    Assert.Contains(result.RequiredVerification, fun w -> w.ObligationId = "R-CORE.2" && not w.Mandatory)

[<Fact>]
let ``several results combine: stale beats fail beats pass`` () =
    let freshness = [ "sourceCommit" ]
    let classify evidence = classifyObligation current freshness (obligation "O" true (Identified "t") evidence)
    let result outcome bindings = { Outcome = outcome; Bindings = bindings }
    Assert.Equal(ObligationState.Stale [ "sourceCommit" ], classify [ result EvidenceOutcome.Pass fresh; result EvidenceOutcome.Pass oldCommit ])
    Assert.Equal(ObligationState.Failing, classify [ result EvidenceOutcome.Pass fresh; result EvidenceOutcome.Fail fresh ])
    Assert.Equal(ObligationState.NotRun, classify [ result EvidenceOutcome.Pass fresh; result EvidenceOutcome.NotRun fresh ])
    Assert.Equal(ObligationState.Satisfied, classify [ result EvidenceOutcome.Pass fresh; result EvidenceOutcome.Pass fresh ])

// ---------------------------------------------------------------------------
// indy-init.verification-plan/1 reader.
// ---------------------------------------------------------------------------

open System.IO
open Ordo.Core.VerificationPlanWire

let private planText () =
    let rec find (current: DirectoryInfo) =
        let candidate = Path.Combine(current.FullName, "tests", "fixtures", "verification-plan", "plan.json")
        if File.Exists candidate then candidate
        else
            match current.Parent with
            | null -> failwith "verification-plan fixture missing from repository"
            | parent -> find parent
    File.ReadAllText(find (DirectoryInfo AppContext.BaseDirectory))

let private readOk expected =
    match readPlan expected (planText ()) with
    | Ok value -> value
    | Error message -> failwith message

let private evidenceJson subject status run sha =
    sprintf """{"schema":"causa.acceptance-evidence/1","run_id":"%s","source_sha":"%s","subject_type":"requirement","subject_id":"%s","status":"%s","exit_code":0,"command":"dotnet test","evidence_type":"test"}""" run sha subject status

[<Fact>]
let ``reads the compiled plan: ids, tests, exclusion and dependencies`` () =
    let header, plan = readOk None
    Assert.Equal("VP-aaaaaaaaaaaa", header.PlanId)
    Assert.Equal("sha256:" + String('a', 64), plan.PlanDigest)
    Assert.Equal<string list>([ "AR-001"; "AR-002"; "AR-003"; "AR-004" ], plan.Requirements |> List.map (fun r -> r.Id))
    let ar1 = plan.Requirements.Head
    Assert.Equal(Identified "tests/ar.spec::core", ar1.Obligations.Head.Test)
    Assert.Equal(MissingTest "no test identity bound in the verification plan", ar1.Obligations.[1].Test)
    let excluded = plan.Requirements |> List.find (fun r -> r.Id = "AR-004")
    Assert.True(excluded.Obligations |> List.forall (fun o -> not o.Mandatory))
    let result = run (context (Some "src1") (Some "run1")) plan
    assertComplete plan result
    Assert.Equal<DependencyEdge list>([ { Requirement = "AR-003"; DependsOn = "NOT-IN-PLAN" } ], result.UnknownDependencies)
    Assert.DoesNotContain("AR-004", result.AcceptanceBlockers)

[<Fact>]
let ``a plan whose digest differs from the admitted digest is refused`` () =
    Assert.True(readPlan (Some(String('a', 64))) (planText ()) |> Result.isOk)
    Assert.True(readPlan (Some("sha256:" + String('a', 64))) (planText ()) |> Result.isOk)
    match readPlan (Some(String('f', 64))) (planText ()) with
    | Ok _ -> failwith "stale pinned digest must be refused"
    | Error message -> Assert.Contains("differs from the admitted digest", message)

[<Fact>]
let ``evidence from another run, commit or with altered bytes is stale`` () =
    let _, plan = readOk None
    let read verified json = match readEvidence verified json with Ok e -> e | Error m -> failwith m
    let results =
        [ read true (evidenceJson "AR-001" "PASS" "run1" "src1")
          read true (evidenceJson "AR-002" "PASS" "run0" "src1")
          read false (evidenceJson "AR-004" "PASS" "run1" "src1") ]
    let result = run (context (Some "src1") (Some "run1")) (attachEvidence results plan)
    let stateOf requirement obligation =
        result.Requirements |> List.find (fun r -> r.Id = requirement) |> fun r -> r.Obligations |> List.find (fst >> (=) obligation) |> snd
    Assert.Equal(ObligationState.Satisfied, stateOf "AR-001" "VPO-0000000000000001")
    Assert.Equal(ObligationState.TestMissing "no test identity bound in the verification plan", stateOf "AR-001" "VPO-0000000000000002")
    Assert.Equal(ObligationState.Stale [ "runId" ], stateOf "AR-002" "VPO-0000000000000003")
    Assert.Equal(ObligationState.Stale [ "integrity" ], stateOf "AR-004" "VPO-0000000000000005")
    Assert.Equal<string list>([ "AR-002"; "AR-004" ], result.StaleEvidence |> List.map (fun s -> s.RequirementId) |> List.sort)
    // Same inputs, same bytes.
    Assert.Equal(render result, render (run (context (Some "src1") (Some "run1")) (attachEvidence results plan)))

// ---------------------------------------------------------------------------
// Policy-aware freshness (DF-SDE-2026-0026): negative controls.
// ---------------------------------------------------------------------------

let private planDigestHex = String('a', 64)

/// The fixture plan with its four `freshness.policy` values replaced in order
/// and `freshnessPolicies` declaring which policies are reusable.
let private planWith (policies: string list) (reusable: (string * bool) list) =
    let declared =
        reusable
        |> List.map (fun (id, flag) -> sprintf """{"id":"%s","reusable":%s}""" id (if flag then "true" else "false"))
        |> String.concat ","

    let marker = "\"policy\": \"FP-DETERMINISTIC-INPUT-DIGEST\""
    let parts = (planText ()).Replace("\"freshnessPolicies\": []", sprintf "\"freshnessPolicies\": [%s]" declared).Split(marker)
    Assert.Equal(policies.Length + 1, parts.Length)
    let replaced = List.zip (List.ofArray parts |> List.take policies.Length) policies |> List.map (fun (part, policy) -> part + sprintf "\"policy\": \"%s\"" policy) |> String.concat ""
    replaced + parts[parts.Length - 1]

let private deterministic = "FP-DETERMINISTIC-INPUT-DIGEST"
let private live = "FP-LIVE-PER-COMMIT"
let private review = "FP-REVIEW-PER-COMMIT"
let private declaredReusable = [ live, false; review, false; deterministic, true ]

let private readPlanOk text =
    match readPlan None text with
    | Ok(_, plan) -> plan
    | Error message -> failwith message

/// Evidence for AR-002 (obligation VPO-0000000000000003, a bound test) with
/// optional content-identity members.
let private evidenceWith run sha (inputDigest: string option) (planDigest: string option) (rehearsal: string option) =
    let optional name value = value |> Option.map (sprintf ",\"%s\":\"%s\"" name) |> Option.defaultValue ""
    sprintf
        """{"schema":"causa.acceptance-evidence/1","run_id":"%s","source_sha":"%s","subject_type":"requirement","subject_id":"AR-002","status":"PASS","exit_code":0%s%s%s}"""
        run
        sha
        (optional "input_digest" inputDigest)
        (optional "plan_digest" planDigest)
        (optional "rehearsal_id" rehearsal)

let private reuseFor digest = Some { RehearsalId = "rehearsal-005"; InputDigests = Map.ofList [ "AR-002", digest ] }

/// State of AR-002's bound obligation when judged for run1 at src1.
let private judge (plan: VerificationPlan) (reuse: ReuseContext option) (verified: bool) (json: string) =
    let evidence =
        match readEvidence verified json with
        | Ok e -> e
        | Error m -> failwith m

    let result = run (planContext plan (Some "src1") (Some "run1") reuse) (attachEvidence [ evidence ] plan)
    result.Requirements |> List.find (fun r -> r.Id = "AR-002") |> fun r -> r.Obligations |> List.find (fst >> (=) "VPO-0000000000000003") |> snd

let private allDeterministic () = readPlanOk (planWith [ deterministic; deterministic; deterministic; deterministic ] declaredReusable)
let private matching run = evidenceWith run "src0" (Some "sha256:in1") (Some planDigestHex) (Some "rehearsal-005")

[<Fact>]
let ``bindings per policy: live and review per run, deterministic adds content reuse`` () =
    Assert.Equal(Ok { Bindings = [ "integrity"; "runId"; "sourceSha" ]; Reuse = [] }, bindingsForPolicy true live)
    Assert.Equal(Ok { Bindings = [ "integrity"; "runId"; "sourceSha" ]; Reuse = [] }, bindingsForPolicy true review)
    Assert.Equal(Ok { Bindings = [ "integrity"; "runId"; "sourceSha" ]; Reuse = [ "integrity"; "inputDigest"; "planDigest"; "rehearsalId" ] }, bindingsForPolicy true deterministic)
    // A deterministic policy the plan does not declare reusable is never reused.
    Assert.Equal(Ok { Bindings = [ "integrity"; "runId"; "sourceSha" ]; Reuse = [] }, bindingsForPolicy false deterministic)

[<Fact>]
let ``deterministic evidence of an earlier run with matching input digest, plan and rehearsal is fresh`` () =
    Assert.Equal(ObligationState.Satisfied, judge (allDeterministic ()) (reuseFor "sha256:in1") true (matching "run0"))
    // The plan digest may be written with or without its sha256: prefix.
    let prefixed = evidenceWith "run0" "src0" (Some "sha256:in1") (Some("sha256:" + planDigestHex)) (Some "rehearsal-005")
    Assert.Equal(ObligationState.Satisfied, judge (allDeterministic ()) (reuseFor "sha256:in1") true prefixed)

[<Fact>]
let ``deterministic evidence with a different input digest is stale`` () =
    Assert.Equal(ObligationState.Stale [ "inputDigest"; "runId"; "sourceSha" ], judge (allDeterministic ()) (reuseFor "sha256:in2") true (matching "run0"))
    // Even from the current run: evidence that contradicts its subject is never fresh.
    let sameRun = evidenceWith "run1" "src1" (Some "sha256:in1") (Some planDigestHex) (Some "rehearsal-005")
    Assert.Equal(ObligationState.Stale [ "inputDigest" ], judge (allDeterministic ()) (reuseFor "sha256:in2") true sameRun)

[<Fact>]
let ``deterministic evidence from another plan or another rehearsal is stale`` () =
    let otherPlan = evidenceWith "run0" "src0" (Some "sha256:in1") (Some(String('f', 64))) (Some "rehearsal-005")
    Assert.Equal(ObligationState.Stale [ "planDigest"; "runId"; "sourceSha" ], judge (allDeterministic ()) (reuseFor "sha256:in1") true otherPlan)
    let otherRehearsal = evidenceWith "run0" "src0" (Some "sha256:in1") (Some planDigestHex) (Some "rehearsal-004")
    Assert.Equal(ObligationState.Stale [ "rehearsalId"; "runId"; "sourceSha" ], judge (allDeterministic ()) (reuseFor "sha256:in1") true otherRehearsal)

[<Fact>]
let ``deterministic evidence whose bytes fail integrity is stale even when everything else matches`` () =
    Assert.Equal(ObligationState.Stale [ "integrity"; "runId"; "sourceSha" ], judge (allDeterministic ()) (reuseFor "sha256:in1") false (matching "run0"))
    Assert.Equal(ObligationState.Stale [ "integrity" ], judge (allDeterministic ()) (reuseFor "sha256:in1") false (evidenceWith "run1" "src1" None None None))

[<Fact>]
let ``live evidence of an earlier run is stale whatever content identity it carries`` () =
    let plan = readPlanOk (planWith [ deterministic; live; deterministic; deterministic ] [ live, true; deterministic, true ])
    Assert.Equal(ObligationState.Stale [ "runId"; "sourceSha" ], judge plan (reuseFor "sha256:in1") true (matching "run0"))
    Assert.Equal(ObligationState.Stale [ "runId" ], judge plan (reuseFor "sha256:in1") true (evidenceWith "run0" "src1" (Some "sha256:in1") (Some planDigestHex) (Some "rehearsal-005")))
    Assert.Equal(ObligationState.Satisfied, judge plan (reuseFor "sha256:in1") true (evidenceWith "run1" "src1" None None None))

[<Fact>]
let ``review evidence for another source commit is stale`` () =
    let plan = readPlanOk (planWith [ deterministic; review; deterministic; deterministic ] [ review, true; deterministic, true ])
    Assert.Equal(ObligationState.Stale [ "sourceSha" ], judge plan (reuseFor "sha256:in1") true (evidenceWith "run1" "src0" (Some "sha256:in1") (Some planDigestHex) (Some "rehearsal-005")))
    Assert.Equal(ObligationState.Satisfied, judge plan (reuseFor "sha256:in1") true (evidenceWith "run1" "src1" None None None))

[<Fact>]
let ``deterministic evidence without an input digest is not reusable and falls back to the run`` () =
    let noDigest run sha = evidenceWith run sha None (Some planDigestHex) (Some "rehearsal-005")
    Assert.Equal(ObligationState.Stale [ "runId"; "sourceSha" ], judge (allDeterministic ()) (reuseFor "sha256:in1") true (noDigest "run0" "src0"))
    Assert.Equal(ObligationState.Stale [ "runId" ], judge (allDeterministic ()) (reuseFor "sha256:in1") true (noDigest "run0" "src1"))
    Assert.Equal(ObligationState.Satisfied, judge (allDeterministic ()) (reuseFor "sha256:in1") true (noDigest "run1" "src1"))
    // Missing plan or rehearsal identity is equally not reusable.
    Assert.Equal(ObligationState.Stale [ "runId"; "sourceSha" ], judge (allDeterministic ()) (reuseFor "sha256:in1") true (evidenceWith "run0" "src0" (Some "sha256:in1") None (Some "rehearsal-005")))
    Assert.Equal(ObligationState.Stale [ "runId"; "sourceSha" ], judge (allDeterministic ()) (reuseFor "sha256:in1") true (evidenceWith "run0" "src0" (Some "sha256:in1") (Some planDigestHex) None))

[<Fact>]
let ``no reuse without a reuse context, a current input digest, or a plan that declares the policy reusable`` () =
    Assert.Equal(ObligationState.Stale [ "runId"; "sourceSha" ], judge (allDeterministic ()) None true (matching "run0"))
    let otherSubject = Some { RehearsalId = "rehearsal-005"; InputDigests = Map.ofList [ "AR-001", "sha256:in1" ] }
    Assert.Equal(ObligationState.Stale [ "runId"; "sourceSha" ], judge (allDeterministic ()) otherSubject true (matching "run0"))
    let undeclared = readPlanOk (planWith [ deterministic; deterministic; deterministic; deterministic ] [ deterministic, false ])
    Assert.Equal(ObligationState.Stale [ "runId"; "sourceSha" ], judge undeclared (reuseFor "sha256:in1") true (matching "run0"))
    // The fixture as committed declares no policy at all.
    let _, fixture = readOk None
    Assert.Equal(ObligationState.Stale [ "runId"; "sourceSha" ], judge fixture (reuseFor "sha256:in1") true (matching "run0"))

[<Fact>]
let ``an unknown freshness policy fails safe: the plan is refused`` () =
    match readPlan None (planWith [ deterministic; "FP-SOMETHING-NEW"; deterministic; deterministic ] declaredReusable) with
    | Ok _ -> failwith "an unknown freshness policy must be refused"
    | Error message -> Assert.Equal("$.requirements[1].freshness.policy: unsupported freshness policy FP-SOMETHING-NEW", message)

    match readPlan None ((planText ()).Replace("\"freshnessPolicies\": []", "\"freshnessPolicies\": [{\"id\":\"FP-LIVE-PER-COMMIT\",\"reusable\":\"yes\"}]")) with
    | Ok _ -> failwith "a mistyped reusable flag must be refused"
    | Error message -> Assert.Equal("$.freshnessPolicies[0].reusable: expected boolean", message)

[<Fact>]
let ``optional content identity members are read strictly and evidence without them reads as before`` () =
    match readEvidence true (evidenceWith "run1" "src1" None None None) with
    | Ok e -> Assert.Equal((None, None, None), (e.InputDigest, e.PlanDigest, e.RehearsalId))
    | Error m -> failwith m

    match readEvidence true (evidenceWith "run1" "src1" (Some "d") (Some "e") (Some "r")) with
    | Ok e -> Assert.Equal((Some "d", Some "sha256:e", Some "r"), (e.InputDigest, e.PlanDigest, e.RehearsalId))
    | Error m -> failwith m

    let mistyped = (evidenceWith "run1" "src1" None None None).Replace("\"exit_code\":0", "\"exit_code\":0,\"input_digest\":7")
    Assert.Equal(Error "$.input_digest: expected nonblank string", readEvidence true mistyped |> Result.map ignore)

[<Fact>]
let ``reuse context reader refuses anything but ordo.verification-reuse-context/1`` () =
    let ok = """{"schema":"ordo.verification-reuse-context/1","rehearsalId":"rehearsal-005","inputDigests":{"AR-002":"sha256:in1"}}"""
    Assert.Equal(Ok { RehearsalId = "rehearsal-005"; InputDigests = Map.ofList [ "AR-002", "sha256:in1" ] }, readReuseContext ok)
    Assert.True(readReuseContext (ok.Replace("context/1", "context/2")) |> Result.isError)
    Assert.True(readReuseContext (ok.Replace("\"sha256:in1\"", "\"\"")) |> Result.isError)
    Assert.True(readReuseContext (ok.Replace("\"rehearsal-005\"", "\"\"")) |> Result.isError)
    Assert.True(readReuseContext """{"schema":"ordo.verification-reuse-context/1","rehearsalId":"r","inputDigests":{"A":"x","A":"y"}}""" |> Result.isError)

[<Fact>]
let ``existing evidence schedules byte-identically under the plan context and the old context`` () =
    let _, plan = readOk None
    let read verified json = match readEvidence verified json with Ok e -> e | Error m -> failwith m
    let results =
        [ read true (evidenceJson "AR-001" "PASS" "run1" "src1")
          read true (evidenceJson "AR-002" "PASS" "run0" "src1")
          read false (evidenceJson "AR-004" "PASS" "run1" "src1") ]
    let attached = attachEvidence results plan
    Assert.Equal(render (run (context (Some "src1") (Some "run1")) attached), render (run (planContext plan (Some "src1") (Some "run1") None) attached))
