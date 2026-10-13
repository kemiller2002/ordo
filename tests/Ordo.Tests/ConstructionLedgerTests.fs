/// CONSTRUCT-009: the requirements disposition ledger
/// (`ordo.requirements-ledger/1`). Every manifest requirement gets exactly one
/// derived row; no input lets a requirement disappear, lets an agent resolve a
/// genuine domain decision, or marks anything implemented without independent
/// acceptance. The fixture is the time-entry timer cohort the default-plan
/// tests use.
module Ordo.Tests.ConstructionLedgerTests

open System
open System.IO
open Xunit
open Ordo.Core
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionDecisions
open Ordo.Core.ConstructionDefaultPlan
open Ordo.Core.ConstructionLedger
open Ordo.Core.ConstructionLedgerWire

// ---------------------------------------------------------------------------
// Fixture.
// ---------------------------------------------------------------------------

let private fixtureDirectory () =
    let rec find (current: DirectoryInfo) =
        let candidate = Path.Combine(current.FullName, "tests", "fixtures", "construction", "time-entry-timer")
        if Directory.Exists candidate then candidate
        else
            match current.Parent with
            | null -> failwith "time-entry timer fixture missing from repository"
            | parent -> find parent
    find (DirectoryInfo AppContext.BaseDirectory)

let private read name = File.ReadAllText(Path.Combine(fixtureDirectory (), name))
let private ok label value = match value with Ok x -> x | Error e -> failwithf "%s: %A" label e

let private manifest () = readManifest (read "source-manifest.json") |> ok "manifest"
let private blueprint () = readBlueprint (read "blueprint.json") |> ok "blueprint"
let private cohort = "COH-TIMER"
/// Test input standing in for the external authority, as in the default-plan tests.
let private approved = Set.singleton "DEC-TIMER-STATES"
let private prepared () = prepareCohort (manifest ()) (blueprint ()) cohort approved |> ok "prepare"
let private records () = readDecisionRecords (read "construction-decisions.json") |> ok "decisions"
let private timerPlan () = generateDefaultPlan (prepared ()) (records ()) ConstructionVocabulary.Version2 |> ok "generate"

let private keyA = "kemiller2002/time-entry-state-machine/docs/architecture/business-activity-ledger-state-model.md#L116-L129"
let private keyStop = "kemiller2002/time-entry-state-machine/docs/architecture/business-activity-ledger-state-model.md#L131-L144"
let private keySession = "kemiller2002/time-entry-state-machine/docs/architecture/business-activity-ledger-state-model.md#L145-L148"
let private keySingle = "kemiller2002/time-entry-state-machine/requirements-reconstruction/fsharp-business-requirements-beyond-csharp.md#L104"
let private keyPaused = "kemiller2002/time-entry-state-machine/requirements-reconstruction/fsharp-business-requirements-beyond-csharp.md#L105-L107"
let private keyShort = "kemiller2002/time-entry-state-machine/requirements-reconstruction/fsharp-business-requirements-beyond-csharp.md#L113-L114"
let private keyRejection = "kemiller2002/time-entry-state-machine/effort-experiment/f-sharp-state/REQUIREMENTS.md#L367-L368"

let private allKeys = [ keyA; keyStop; keySession; keySingle; keyPaused; keyShort; keyRejection ]

/// The obligation ids the default-plan tests pin for this cohort.
let private obligationConflict = "OBL-COH-TIMER-conflict-f569e9ac9d77"
let private obligationUncovered = "OBL-COH-TIMER-uncoveredRequirement-fce50292ef11"
let private obligationEffect = "OBL-COH-TIMER-undecidedEffectFailure-9843158a9897"
let private extensionObligations = [ "OBL-COH-TIMER-extensionBehavior-03835228233e"; "OBL-COH-TIMER-extensionBehavior-f2efc8429795" ]

/// Independent acceptance cases, as a test input. They are not blueprint nodes.
let private passed cases = { CaseIds = cases; Verdict = AcceptanceVerdict.Passed }
let private failed cases = { CaseIds = cases; Verdict = AcceptanceVerdict.Failed }
let private fullEvidence =
    [ keyA, passed [ "ACC-TIMER-TRANSITIONS" ]
      keySingle, passed [ "ACC-TIMER-SINGLE" ]
      keyRejection, passed [ "ACC-TIMER-REJECTION" ]
      keyPaused, passed [ "ACC-TIMER-PAUSED-1"; "ACC-TIMER-PAUSED-2" ]
      keyStop, passed [ "ACC-TIMER-STOP" ] ]
    |> Map.ofList

let private build plans evidence tags = buildLedger (manifest ()) (blueprint ()) plans evidence tags
let private timerLedger () = build [ timerPlan () ] fullEvidence noTags |> ok "ledger"
let private rowOf key (ledger: Ledger) = ledger.Rows |> List.find (fun r -> r.Requirement.Key = key)

let private violations result =
    match result with
    | Ok _ -> failwith "expected ledger violations, got a ledger"
    | Error (errors: LedgerViolation list) -> errors

let private fails expected result = Assert.Contains(expected, violations result)

let private claim key disposition decisions =
    { RequirementKey = key; Disposition = disposition; DecisionIds = decisions }

let private withClaims claims = { noTags with Claims = claims }

let private isImplemented row = row.Disposition = FinalDisposition.Implemented

// ---------------------------------------------------------------------------
// Golden: the timer pilot.
// ---------------------------------------------------------------------------

[<Fact>]
let ``the timer ledger has the seven keys with their expected classes and dispositions`` () =
    let ledger = timerLedger ()
    Assert.Equal(LedgerSchemaVersion, ledger.SchemaVersion)
    Assert.Equal((manifest ()).Digest, ledger.SourceManifestDigest)
    Assert.Equal(blueprintDigest (blueprint ()), ledger.BlueprintDigest)
    Assert.Equal<string list>([ (timerPlan ()).Digest ], ledger.DefaultPlanDigests)
    Assert.Equal<string list>(List.sort allKeys, ledger.Rows |> List.map _.Requirement.Key)

    let classes key = (rowOf key ledger).Classes
    let disposition key = (rowOf key ledger).Disposition
    let approvedAndBuilt = [ ResolutionClass.AlreadyApproved; ResolutionClass.MechanicallyDetermined ]
    for key in [ keyA; keySingle; keyRejection ] do
        Assert.Equal<ResolutionClass list>(approvedAndBuilt, classes key)
        Assert.Equal(FinalDisposition.Implemented, disposition key)
        Assert.Equal<string list>([ "DEC-TIMER-STATES" ], (rowOf key ledger).ApprovedDecisionIds)
    Assert.Equal<ResolutionClass list>([ ResolutionClass.MechanicallyDetermined ], classes keyPaused)
    Assert.Equal(FinalDisposition.Implemented, disposition keyPaused)
    Assert.Equal<string list>([ "ITimerPorts.CurrentTime" ], (rowOf keyPaused ledger).ExtensionSurface)
    Assert.Equal<ResolutionClass list>([ ResolutionClass.MechanicallyDetermined; ResolutionClass.GenuineDomainDecision ], classes keyStop)
    Assert.Equal<ResolutionClass list>([ ResolutionClass.AlreadyApproved; ResolutionClass.GenuineDomainDecision ], classes keySession)
    Assert.Equal<ResolutionClass list>([ ResolutionClass.GenuineDomainDecision ], classes keyShort)

    // Stop has passing acceptance but an undecided effect failure, so it is
    // only partially implemented.
    match disposition keyStop with
    | FinalDisposition.PartiallyImplemented why -> Assert.Contains(obligationEffect, why)
    | other -> failwithf "Stop: %A" other
    Assert.Equal<string list>([ "ITimerPorts.CurrentTime"; "ITimerPorts.Perform/Stop" ], (rowOf keyStop ledger).ExtensionSurface)
    match disposition keySession with
    | FinalDisposition.Unresolved why -> Assert.Contains(obligationUncovered, why)
    | other -> failwithf "session: %A" other
    // The short stop is in no cohort; its blueprint reason is carried through.
    let short = rowOf keyShort ledger
    Assert.Equal(None, short.CohortId)
    Assert.Equal(FinalDisposition.Unresolved "Code and state model disagree on whether a stop under thirty seconds is a rejection or a transition to NoTimer", short.Disposition)
    Assert.Equal<string list>([ "CONFLICT-TIMER-SHORT-STOP" ], short.ConflictNodeIds)
    Assert.Equal<NodeKind list>([ NodeKind.Conflict ], short.Categories)
    Assert.Empty(short.GeneratedArtifacts)

    // The five known obligations: three open questions, two extension surfaces.
    Assert.Equal<string list>([ obligationEffect ], (rowOf keyStop ledger).OpenObligationIds)
    Assert.Equal<string list>([ obligationUncovered ], (rowOf keySession ledger).OpenObligationIds)
    Assert.Equal<string list>([ obligationConflict ], short.OpenObligationIds)
    Assert.Equal<string list>(
        List.sort [ obligationConflict; obligationUncovered; obligationEffect ],
        ledger.Rows |> List.collect _.OpenObligationIds |> List.sort)
    let plan = timerPlan ()
    let extension = plan.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.ExtensionBehavior)
    Assert.Equal<string list>(extensionObligations, extension |> List.map _.Id |> List.sort)
    Assert.Equal<string list>(
        List.sort (obligationConflict :: obligationUncovered :: obligationEffect :: extensionObligations),
        plan.Obligations |> List.map _.Id |> List.sort)

    // Constructed rows carry the plan's cohort, labels and generated files.
    let a = rowOf keyA ledger
    Assert.Equal(Some cohort, a.CohortId)
    Assert.Contains("transition:NoTimer/Start", a.OperationLabels)
    Assert.Equal<string list>(List.sort plan.GeneratedLayout, a.GeneratedArtifacts)
    Assert.Equal<string list>([ "VER-TIMER-TABLE" ], a.VerificationNodeIds)
    Assert.Equal<NodeKind list>([ Decision ], a.Categories)

[<Fact>]
let ``without acceptance evidence nothing is implemented`` () =
    let ledger = build [ timerPlan () ] Map.empty noTags |> ok "ledger"
    Assert.DoesNotContain(ledger.Rows, isImplemented)
    match (rowOf keyA ledger).Disposition with
    | FinalDisposition.PartiallyImplemented why -> Assert.Contains("no passing independent acceptance", why)
    | other -> failwithf "%A" other
    let failing = build [ timerPlan () ] (Map.ofList [ keyA, failed [ "ACC-TIMER-TRANSITIONS" ] ]) noTags |> ok "ledger"
    match (rowOf keyA failing).Disposition with
    | FinalDisposition.PartiallyImplemented why -> Assert.Contains("failed", why)
    | other -> failwithf "%A" other

// ---------------------------------------------------------------------------
// Accountability: every key exactly once, even with no plan.
// ---------------------------------------------------------------------------

[<Fact>]
let ``dropping the cohort's plan surfaces every key, unconstructed, as unresolved`` () =
    let ledger = build [] fullEvidence noTags |> ok "ledger"
    Assert.Equal<string list>(List.sort allKeys, ledger.Rows |> List.map _.Requirement.Key)
    Assert.Empty(ledger.DefaultPlanDigests)
    for row in ledger.Rows do
        Assert.Equal(None, row.CohortId)
        Assert.Empty(row.OperationLabels)
        Assert.Empty(row.GeneratedArtifacts)
        Assert.DoesNotContain(ResolutionClass.MechanicallyDetermined, row.Classes)
        match row.Disposition with
        | FinalDisposition.Unresolved why -> Assert.False(String.IsNullOrWhiteSpace why)
        | other -> failwithf "%s: %A" row.Requirement.Key other
    match (rowOf keyA ledger).Disposition with
    | FinalDisposition.Unresolved why -> Assert.Contains("COH-TIMER", why)
    | _ -> ()
    // Nothing approves the unconstructed Stop/paused requirements: genuine.
    Assert.Contains(ResolutionClass.GenuineDomainDecision, (rowOf keyStop ledger).Classes)

[<Fact>]
let ``every manifest key has exactly one row over every evidence subset, with and without the plan`` () =
    // Exhaustive over the 2^7 evidence subsets and both verdicts: a
    // deterministic property test without a property-testing dependency.
    let plan = timerPlan ()
    let keys = List.sort allKeys
    for planSet in [ []; [ plan ] ] do
        for mask in 0 .. (1 <<< keys.Length) - 1 do
            for verdict in [ AcceptanceVerdict.Passed; AcceptanceVerdict.Failed ] do
                let evidence =
                    keys
                    |> List.indexed
                    |> List.filter (fun (i, _) -> mask &&& (1 <<< i) <> 0)
                    |> List.map (fun (i, k) -> k, { CaseIds = [ sprintf "ACC-%d" i ]; Verdict = verdict })
                    |> Map.ofList
                let ledger = build planSet evidence noTags |> ok (sprintf "mask %d" mask)
                Assert.Equal<string list>(keys, ledger.Rows |> List.map _.Requirement.Key)
                Assert.Empty(accountabilityViolations (manifest ()) ledger.Rows)
                for row in ledger.Rows do
                    Assert.Empty(rowViolations row)
                    Assert.NotEmpty(row.Classes)
                    if isImplemented row then
                        Assert.Equal(Some AcceptanceVerdict.Passed, row.Acceptance)
                        Assert.Empty(row.OpenObligationIds)
                        Assert.True(row.CohortId.IsSome)
                        Assert.DoesNotContain(ResolutionClass.GenuineDomainDecision, row.Classes)
                Assert.Empty(validateLedger (manifest ()) (blueprint ()) planSet evidence noTags ledger)

// ---------------------------------------------------------------------------
// Violations.
// ---------------------------------------------------------------------------

[<Fact>]
let ``a missing row is an unaccounted requirement`` () =
    let ledger = timerLedger ()
    let dropped = { ledger with Rows = ledger.Rows |> List.filter (fun r -> r.Requirement.Key <> keyShort) }
    Assert.Equal<LedgerViolation list>(
        [ UnaccountedRequirement keyShort ],
        validateLedger (manifest ()) (blueprint ()) [ timerPlan () ] fullEvidence noTags dropped)

[<Fact>]
let ``a repeated row is a duplicate row`` () =
    let ledger = timerLedger ()
    let doubled = { ledger with Rows = rowOf keyA ledger :: ledger.Rows }
    Assert.Contains(DuplicateRow keyA, validateLedger (manifest ()) (blueprint ()) [ timerPlan () ] fullEvidence noTags doubled)

[<Fact>]
let ``a row, evidence entry, tag or claim outside the manifest is an invented requirement`` () =
    let invented = "kemiller2002/time-entry-state-machine/invented.md#L1"
    fails (InventedRequirement invented) (build [ timerPlan () ] (Map.add invented (passed [ "ACC-X" ]) fullEvidence) noTags)
    fails (InventedRequirement invented) (build [ timerPlan () ] fullEvidence { noTags with FixedByRequirement = Set.singleton invented })
    fails (InventedRequirement invented) (build [ timerPlan () ] fullEvidence (withClaims [ claim invented (FinalDisposition.Deferred "later") [] ]))
    let ledger = timerLedger ()
    let a = rowOf keyA ledger
    let renamed = { a with Requirement = { a.Requirement with Key = invented } }
    let violations = validateLedger (manifest ()) (blueprint ()) [ timerPlan () ] fullEvidence noTags { ledger with Rows = renamed :: ledger.Rows }
    Assert.Contains(InventedRequirement invented, violations)

[<Fact>]
let ``implemented requires passing, independent acceptance`` () =
    let plan = timerPlan ()
    let implemented = withClaims [ claim keyA FinalDisposition.Implemented [] ]
    fails (ImplementedWithoutIndependentAcceptance keyA) (build [ plan ] Map.empty implemented)
    fails (ImplementedWithoutIndependentAcceptance keyA) (build [ plan ] (Map.ofList [ keyA, failed [ "ACC-TIMER-TRANSITIONS" ] ]) implemented)
    fails (ImplementedWithoutIndependentAcceptance keyA) (build [ plan ] (Map.ofList [ keyA, passed [] ]) implemented)
    // The blueprint's own verification obligation is not independent acceptance.
    fails (ImplementedWithoutIndependentAcceptance keyA) (build [ plan ] (Map.ofList [ keyA, passed [ "VER-TIMER-TABLE" ] ]) implemented)
    // Derived, self-certified acceptance leaves the requirement partial.
    let selfCertified = build [ plan ] (Map.ofList [ keyA, passed [ "VER-TIMER-TABLE" ] ]) noTags |> ok "self-certified"
    Assert.True(match (rowOf keyA selfCertified).Disposition with FinalDisposition.PartiallyImplemented _ -> true | _ -> false)
    let accepted = build [ plan ] (Map.ofList [ keyA, passed [ "ACC-TIMER-TRANSITIONS" ] ]) implemented |> ok "accepted"
    Assert.Equal(FinalDisposition.Implemented, (rowOf keyA accepted).Disposition)

[<Fact>]
let ``implemented with an open obligation is refused, and so is an agent resolving the domain decision`` () =
    let errors = violations (build [ timerPlan () ] fullEvidence (withClaims [ claim keyStop FinalDisposition.Implemented [] ]))
    Assert.Contains(ImplementedWithOpenObligation(keyStop, [ obligationEffect ]), errors)
    Assert.Contains(AgentResolvedDomainDecision keyStop, errors)
    // The uncovered session requirement has an approved anchoring decision,
    // but that decision does not close the open question.
    let session =
        violations (build [ timerPlan () ] (Map.add keySession (passed [ "ACC-S" ]) fullEvidence) (withClaims [ claim keySession FinalDisposition.Implemented [] ]))
    Assert.Contains(ImplementedWithOpenObligation(keySession, [ obligationUncovered ]), session)
    Assert.Contains(AgentResolvedDomainDecision keySession, session)

[<Fact>]
let ``rejecting or superseding a genuine domain decision needs an approved closing decision`` () =
    Assert.Equal<LedgerViolation list>(
        [ AgentResolvedDomainDecision keyShort ],
        violations (build [ timerPlan () ] fullEvidence (withClaims [ claim keyShort (FinalDisposition.Rejected "agent prefers the code") [] ])))
    // Rejecting is a domain decision even for a constructed requirement.
    Assert.Equal<LedgerViolation list>(
        [ AgentResolvedDomainDecision keyA ],
        violations (build [ timerPlan () ] fullEvidence (withClaims [ claim keyA (FinalDisposition.Rejected "not needed") [] ])))
    // Deferring is not resolving: it needs a rationale, not a decision.
    let deferred = build [ timerPlan () ] fullEvidence (withClaims [ claim keyShort (FinalDisposition.Deferred "after the pilot") [] ]) |> ok "deferred"
    Assert.Equal(FinalDisposition.Deferred "after the pilot", (rowOf keyShort deferred).Disposition)
    // An unapproved supersession is refused as unapproved.
    fails (UnapprovedDecision(keyShort, "DEC-NOT-APPROVED"))
        (build [ timerPlan () ] fullEvidence (withClaims [ claim keyShort (FinalDisposition.Superseded("DEC-NOT-APPROVED", "replaced")) [] ]))
    // With an approved program-level decision the rejection stands.
    let tags = { withClaims [ claim keyShort (FinalDisposition.Rejected "program decision DEC-SHORT-STOP") [ "DEC-SHORT-STOP" ] ] with ApprovedDecisionIds = Set.singleton "DEC-SHORT-STOP" }
    let ledger = build [ timerPlan () ] fullEvidence tags |> ok "approved rejection"
    let row = rowOf keyShort ledger
    Assert.Equal(FinalDisposition.Rejected "program decision DEC-SHORT-STOP", row.Disposition)
    Assert.Equal<string list>([ "DEC-SHORT-STOP" ], row.ClosingDecisionIds)
    Assert.Contains(ResolutionClass.AlreadyApproved, row.Classes)

[<Fact>]
let ``a requirement in no supplied cohort is never implemented by construction`` () =
    let tags = { withClaims [ claim keyShort FinalDisposition.Implemented [ "DEC-SHORT-STOP" ] ] with ApprovedDecisionIds = Set.singleton "DEC-SHORT-STOP" }
    Assert.Equal<LedgerViolation list>(
        [ ImplementedWithoutCohort keyShort ],
        violations (build [ timerPlan () ] (Map.add keyShort (passed [ "ACC-SHORT" ]) fullEvidence) tags))

[<Fact>]
let ``every non-implemented disposition needs a rationale`` () =
    for disposition in
        [ FinalDisposition.Deferred " "
          FinalDisposition.Unresolved ""
          FinalDisposition.PartiallyImplemented "" ] do
        Assert.Equal<LedgerViolation list>(
            [ MissingRationale keyA ],
            violations (build [ timerPlan () ] fullEvidence (withClaims [ claim keyA disposition [] ])))
    let tags = { withClaims [ claim keyA (FinalDisposition.Superseded("DEC-PROGRAM", "")) [] ] with ApprovedDecisionIds = Set.singleton "DEC-PROGRAM" }
    Assert.Equal<LedgerViolation list>([ MissingRationale keyA ], violations (build [ timerPlan () ] fullEvidence tags))

[<Fact>]
let ``claims are unique and cite only approved decisions`` () =
    fails (DuplicateClaim keyA)
        (build [ timerPlan () ] fullEvidence (withClaims [ claim keyA (FinalDisposition.Deferred "a") []; claim keyA (FinalDisposition.Deferred "b") [] ]))
    fails (UnapprovedDecision(keyStop, "DEC-INVENTED"))
        (build [ timerPlan () ] fullEvidence (withClaims [ claim keyStop FinalDisposition.Implemented [ "DEC-INVENTED" ] ]))
    // The decision the generator already applied cannot close what it left
    // open, even when the operator lists it again.
    let session = Map.add keySession (passed [ "ACC-S" ]) fullEvidence
    for tags in [ noTags; { noTags with ApprovedDecisionIds = approved } ] do
        fails (NonClosingDecision(keySession, "DEC-TIMER-STATES"))
            (build [ timerPlan () ] session { tags with Claims = [ claim keySession FinalDisposition.Implemented [ "DEC-TIMER-STATES" ] ] })
        fails (NonClosingDecision(keySession, "DEC-TIMER-STATES"))
            (build [ timerPlan () ] session { tags with Claims = [ claim keySession (FinalDisposition.Superseded("DEC-TIMER-STATES", "covered")) [] ] })

[<Fact>]
let ``an approved decision can close an open obligation`` () =
    let tags =
        { withClaims [ claim keyStop FinalDisposition.Implemented [ "DEC-STOP-FAILURE-PROGRAM" ] ] with
            ApprovedDecisionIds = Set.singleton "DEC-STOP-FAILURE-PROGRAM" }
    let row = build [ timerPlan () ] fullEvidence tags |> ok "closed" |> rowOf keyStop
    Assert.Equal(FinalDisposition.Implemented, row.Disposition)
    Assert.Equal<string list>([ obligationEffect ], row.OpenObligationIds)
    Assert.Equal<string list>([ "DEC-STOP-FAILURE-PROGRAM" ], row.ClosingDecisionIds)
    Assert.Equal<ResolutionClass list>(
        [ ResolutionClass.AlreadyApproved; ResolutionClass.MechanicallyDetermined; ResolutionClass.GenuineDomainDecision ],
        row.Classes)

[<Fact>]
let ``a plan for another blueprint and two plans for one requirement are refused`` () =
    let other = readBlueprint (read "blueprint.v2-remain.json") |> ok "blueprint v2"
    fails (PlanBindingMismatch cohort) (buildLedger (manifest ()) other [ timerPlan () ] fullEvidence noTags)
    let plan = timerPlan ()
    fails (AmbiguousCohort(keyA, [ cohort; cohort ])) (build [ plan; plan ] fullEvidence noTags)

[<Fact>]
let ``an invalid blueprint is refused before any row is derived`` () =
    let b = blueprint ()
    let missing = { b with Requirements = b.Requirements |> List.filter (fun r -> r.Source.Key <> keyShort) }
    fails (InvalidBlueprint(MissingRequirement keyShort)) (buildLedger (manifest ()) missing [] Map.empty noTags)

[<Fact>]
let ``a tampered row is a row mismatch and its own rule violation`` () =
    let ledger = timerLedger ()
    let stop = rowOf keyStop ledger
    let tampered = { stop with Disposition = FinalDisposition.Implemented; Classes = [ ResolutionClass.MechanicallyDetermined ] }
    let rows = ledger.Rows |> List.map (fun r -> if r.Requirement.Key = keyStop then tampered else r)
    let errors = validateLedger (manifest ()) (blueprint ()) [ timerPlan () ] fullEvidence noTags { ledger with Rows = rows }
    Assert.Contains(RowMismatch keyStop, errors)
    Assert.Contains(ImplementedWithOpenObligation(keyStop, [ obligationEffect ]), errors)
    let reclassified = { stop with Classes = [ ResolutionClass.MechanicallyDetermined ] }
    Assert.Equal<LedgerViolation list>(
        [ RowMismatch keyStop ],
        validateLedger (manifest ()) (blueprint ()) [ timerPlan () ] fullEvidence noTags
            { ledger with Rows = ledger.Rows |> List.map (fun r -> if r.Requirement.Key = keyStop then reclassified else r) })
    let header = { ledger with DefaultPlanDigests = [] }
    Assert.Equal<LedgerViolation list>(
        [ LedgerHeaderMismatch "defaultPlanDigests" ],
        validateLedger (manifest ()) (blueprint ()) [ timerPlan () ] fullEvidence noTags header)
    Assert.Equal<LedgerViolation list>(
        [ UnclassifiedRow keyA ],
        rowViolations { rowOf keyA ledger with Classes = [] })

// ---------------------------------------------------------------------------
// Classes from other inputs.
// ---------------------------------------------------------------------------

[<Fact>]
let ``fixed-by-requirement is an explicit tag, never inferred`` () =
    Assert.DoesNotContain(timerLedger().Rows, fun r -> List.contains ResolutionClass.FixedByRequirement r.Classes)
    let ledger = build [ timerPlan () ] fullEvidence { noTags with FixedByRequirement = Set.singleton keyRejection } |> ok "tagged"
    Assert.Equal<ResolutionClass list>(
        [ ResolutionClass.FixedByRequirement; ResolutionClass.AlreadyApproved; ResolutionClass.MechanicallyDetermined ],
        (rowOf keyRejection ledger).Classes)

/// The v2-remain test blueprint approves DEC-TIMER-STOP-FAILURE (a test input).
let private decidedPlan vocabulary =
    let b = readBlueprint (read "blueprint.v2-remain.json") |> ok "blueprint v2"
    let p = prepareCohort (manifest ()) b cohort (Set.add "DEC-TIMER-STOP-FAILURE" approved) |> ok "prepare v2"
    let r = records ()
    let decided =
        { r with
            BlueprintDigest = blueprintDigest b
            Records =
                (r.Records |> List.map (fun x ->
                    match x.Content with
                    | PortContract ports when x.NodeId = "IFC-TIMER-PORTS" ->
                        { x with Content = PortContract(ports |> List.filter (function RecordedCurrentTime _ -> true | _ -> false)) }
                    | _ -> x))
                @ [ { NodeId = "DEC-TIMER-STOP-FAILURE"
                      Content = PortContract [ RecordedTransitionEffect("Stop", Some EffectFailureDisposition.Remain, [ keyStop ]) ] } ] }
    b, generateDefaultPlan p decided vocabulary |> ok "decided plan"

[<Fact>]
let ``a vocabulary that cannot carry an approved decision is unsupported-by-vocabulary, and only the right vocabulary implements it`` () =
    let b, v1 = decidedPlan ConstructionVocabulary.Version1
    let row = buildLedger (manifest ()) b [ v1 ] fullEvidence noTags |> ok "v1 ledger" |> rowOf keyStop
    Assert.Equal<ResolutionClass list>(
        [ ResolutionClass.AlreadyApproved; ResolutionClass.MechanicallyDetermined; ResolutionClass.UnsupportedByVocabulary ],
        row.Classes)
    Assert.Equal<string list>([ "DEC-TIMER-STOP-FAILURE" ], row.ApprovedDecisionIds)
    Assert.True(match row.Disposition with FinalDisposition.PartiallyImplemented _ -> true | _ -> false)
    let _, v2 = decidedPlan ConstructionVocabulary.Version2
    let decided = buildLedger (manifest ()) b [ v2 ] fullEvidence noTags |> ok "v2 ledger" |> rowOf keyStop
    Assert.Equal<ResolutionClass list>([ ResolutionClass.AlreadyApproved; ResolutionClass.MechanicallyDetermined ], decided.Classes)
    Assert.Equal(FinalDisposition.Implemented, decided.Disposition)

/// The timer blueprint with the short-stop requirement deferred instead of
/// unresolved, and the session requirement superseded or rejected.
let private variant (f: Blueprint -> Blueprint) = f (blueprint ())

let private redispose key disposition extraNode (b: Blueprint) =
    { b with
        Requirements =
            b.Requirements |> List.map (fun r ->
                if r.Source.Key = key then
                    { r with Disposition = disposition; NodeIds = r.NodeIds @ (extraNode |> Option.map (fun (n: ConstructionNode) -> n.Id) |> Option.toList) }
                else r)
        Nodes = b.Nodes @ Option.toList extraNode }

[<Fact>]
let ``blueprint deferred, rejected and superseded dispositions are carried through only with approval`` () =
    let deferral = { Id = "DEFER-SHORT"; Kind = Deferral; RequirementKeys = [ keyShort ]; DependsOn = []; Justification = Some "later" }
    let deferred = variant (redispose keyShort (Deferred "after pilot") (Some deferral))
    Assert.Empty(ConstructionIr.validate (manifest ()) deferred)
    let row = buildLedger (manifest ()) deferred [] Map.empty noTags |> ok "deferred" |> rowOf keyShort
    Assert.Equal(FinalDisposition.Deferred "after pilot", row.Disposition)

    let rationale = { Id = "RAT-SESSION"; Kind = EngineeringRationale; RequirementKeys = [ keySession ]; DependsOn = []; Justification = Some "out of scope" }
    let rejected = variant (redispose keySession (Rejected "out of scope") (Some rationale))
    Assert.Empty(ConstructionIr.validate (manifest ()) rejected)
    let unapproved = buildLedger (manifest ()) rejected [] Map.empty noTags |> ok "rejected" |> rowOf keySession
    match unapproved.Disposition with
    | FinalDisposition.Unresolved why -> Assert.Contains("no approved decision", why)
    | other -> failwithf "%A" other
    let approvedRejection =
        buildLedger (manifest ()) rejected [] Map.empty { noTags with ApprovedDecisionIds = Set.singleton "RAT-SESSION" }
        |> ok "approved rejection" |> rowOf keySession
    Assert.Equal(FinalDisposition.Rejected "out of scope", approvedRejection.Disposition)
    Assert.Contains("RAT-SESSION", approvedRejection.ClosingDecisionIds)

    let superseded = variant (redispose keySession (Superseded(keyA, "covered by the transition table")) None)
    Assert.Empty(ConstructionIr.validate (manifest ()) superseded)
    match (buildLedger (manifest ()) superseded [] Map.empty noTags |> ok "superseded" |> rowOf keySession).Disposition with
    | FinalDisposition.Unresolved why -> Assert.Contains("no approved decision", why)
    | other -> failwithf "%A" other
    let approvedSupersession =
        buildLedger (manifest ()) superseded [] Map.empty { noTags with ApprovedDecisionIds = approved }
        |> ok "approved supersession" |> rowOf keySession
    match approvedSupersession.Disposition with
    | FinalDisposition.Superseded("DEC-TIMER-STATES", why) -> Assert.Contains(keyA, why)
    | other -> failwithf "%A" other

// ---------------------------------------------------------------------------
// Wire.
// ---------------------------------------------------------------------------

[<Fact>]
let ``the ledger round-trips through its canonical wire form`` () =
    for ledger in [ timerLedger (); build [] Map.empty noTags |> ok "empty" ] do
        let text = encodeLedger ledger
        let reread = readLedger text |> ok "read"
        Assert.Equal(ledger, reread)
        Assert.Equal(text, encodeLedger reread)
        Assert.Equal(ConstructionLedgerWire.digest ledger, ConstructionLedgerWire.digest reread)
        Assert.Empty(validateLedger (manifest ()) (blueprint ()) (if List.isEmpty ledger.DefaultPlanDigests then [] else [ timerPlan () ])
                         (if List.isEmpty ledger.DefaultPlanDigests then Map.empty else fullEvidence) noTags reread)

[<Fact>]
let ``the ledger digest is deterministic and independent of input and list order`` () =
    let first = timerLedger ()
    let expected = ConstructionLedgerWire.digest first
    Assert.StartsWith("sha256:", expected)
    for _ in 1 .. 5 do
        Assert.Equal(expected, ConstructionLedgerWire.digest (timerLedger ()))
    let shuffled = { first with Rows = List.rev first.Rows |> List.map (fun r -> { r with OperationLabels = List.rev r.OperationLabels; GeneratedArtifacts = List.rev r.GeneratedArtifacts }) }
    Assert.Equal(expected, ConstructionLedgerWire.digest shuffled)
    let different = build [ timerPlan () ] Map.empty noTags |> ok "no evidence"
    Assert.NotEqual<string>(expected, ConstructionLedgerWire.digest different)

[<Fact>]
let ``the reader is strict`` () =
    let text = encodeLedger (timerLedger ())
    let refuse (from: string) (into: string) =
        Assert.Contains(from, text)
        Assert.True(Result.isError (readLedger (text.Replace(from, into))), into)
    refuse "\"ordo.requirements-ledger/1\"" "\"ordo.requirements-ledger/2\""
    refuse "\"mechanicallyDetermined\"" "\"guessed\""
    refuse "\"kind\": \"implemented\"" "\"kind\": \"done\""
    refuse "\"rows\": [" "\"extra\": 1, \"rows\": ["
    refuse "\"rationale\": \"Code" "\"x\": 1, \"rationale\": \"Code"
    refuse "\"acceptanceVerdict\": \"passed\"" "\"acceptanceVerdict\": \"waived\""
    refuse "\"cohortId\": null" "\"cohortId\": \" \""
    Assert.True(Result.isError (readLedger "{}"))

// ---------------------------------------------------------------------------
// Requirement-fixed but inexpressible: a vocabulary gap, not a domain question.
// ---------------------------------------------------------------------------

let private fixedTags keys = { noTags with FixedByRequirement = Set.ofList keys }

[<Fact>]
let ``an uncovered requirement the operator tags as fixed is a vocabulary gap that acceptance can close`` () =
    let evidence = fullEvidence |> Map.add keySession (passed [ "ACC-TIMER-SESSION" ])
    let ledger = build [ timerPlan () ] evidence (fixedTags [ keySession ]) |> ok "ledger"
    let session = rowOf keySession ledger
    Assert.Equal<ResolutionClass list>(
        [ ResolutionClass.FixedByRequirement; ResolutionClass.AlreadyApproved; ResolutionClass.UnsupportedByVocabulary ],
        session.Classes)
    Assert.Empty(session.OpenObligationIds)
    Assert.Equal(FinalDisposition.Implemented, session.Disposition)

[<Fact>]
let ``a fixed but inexpressible requirement without acceptance is only partially implemented, as handwritten`` () =
    let ledger = build [ timerPlan () ] fullEvidence (fixedTags [ keySession ]) |> ok "ledger"
    match (rowOf keySession ledger).Disposition with
    | FinalDisposition.PartiallyImplemented why -> Assert.Contains("Handwritten", why)
    | other -> failwithf "session: %A" other

[<Fact>]
let ``without the operator tag the uncovered requirement stays a genuine domain decision`` () =
    let evidence = fullEvidence |> Map.add keySession (passed [ "ACC-TIMER-SESSION" ])
    let session = rowOf keySession (build [ timerPlan () ] evidence noTags |> ok "ledger")
    Assert.Contains(ResolutionClass.GenuineDomainDecision, session.Classes)
    Assert.DoesNotContain(ResolutionClass.UnsupportedByVocabulary, session.Classes)
    Assert.Contains(obligationUncovered, session.OpenObligationIds)
    Assert.False(isImplemented session)

[<Fact>]
let ``the fixed tag neither closes conflicts nor undecided effect failures`` () =
    let tags = fixedTags [ keyShort; keyStop ]
    let ledger = build [ timerPlan () ] fullEvidence tags |> ok "ledger"
    let short = rowOf keyShort ledger
    Assert.Contains(ResolutionClass.GenuineDomainDecision, short.Classes)
    Assert.False(isImplemented short)
    let stop = rowOf keyStop ledger
    Assert.Contains(ResolutionClass.GenuineDomainDecision, stop.Classes)
    Assert.Contains(obligationEffect, stop.OpenObligationIds)
    Assert.False(isImplemented stop)
