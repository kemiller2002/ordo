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

let private current = { Current = Map.ofList [ "sourceCommit", "abc123"; "baselineSha256", "b1" ] }
let private fresh = Map.ofList [ "sourceCommit", "abc123"; "baselineSha256", "b1" ]
let private oldCommit = Map.ofList [ "sourceCommit", "old999"; "baselineSha256", "b1" ]

let private obligation id mandatory test evidence =
    { Id = id; Mandatory = mandatory; Method = "test"; Test = test; Evidence = evidence }

let private requirement id dependsOn obligations =
    { Id = id; Priority = "P0"; Obligations = obligations; DependsOn = dependsOn; FreshnessBindings = [ "sourceCommit"; "baselineSha256" ] }

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
    let partial = { Current = Map.ofList [ "baselineSha256", "b1" ] }
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
