/// CONSTRUCT-010 phase 2: decision records version 4 generate vocabulary 6
/// default plans with effect outcome policies and a persisted instance. An
/// absent choice is never defaulted: it stays undecided and becomes a typed
/// obligation. Fixtures: the persisted synthetic gate, and the real timer
/// cohort's records converted to version 4 in test code (test approvals).
module Ordo.Tests.ConstructionDecisionsV4Tests

open System
open System.IO
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionDecisions
open Ordo.Core.ConstructionDefaultPlan
open Ordo.Core.ConstructionClassification
module ConstructionDecisions = Ordo.Core.ConstructionDecisions

let private fixtures () =
    let rec find (current: DirectoryInfo) =
        let candidate = Path.Combine(current.FullName, "tests", "fixtures", "construction")
        if Directory.Exists candidate then candidate
        else
            match current.Parent with
            | null -> failwith "construction fixtures missing from repository"
            | parent -> find parent
    find (DirectoryInfo AppContext.BaseDirectory)

let private read dir name = File.ReadAllText(Path.Combine(fixtures (), dir, name))
let private ok label value = match value with Ok x -> x | Error e -> failwithf "%s: %A" label e
let private gate = "gate-persisted-synthetic"

let private gateCohort () =
    let manifest = readManifest (read gate "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read gate "blueprint.json") |> ok "blueprint"
    prepareCohort manifest blueprint "COH-GATE" (Set.singleton "DEC-GATE") |> ok "prepare"

let private gateRecords () = readDecisionRecords (read gate "construction-decisions-v4.json") |> ok "records"
let private v6 = ConstructionVocabulary.Version6
let private generateGate r = generateDefaultPlan (gateCohort ()) r v6

let private mapContent f (r: ApprovedDecisionRecords) = { r with Records = r.Records |> List.map (fun x -> { x with Content = f x.Content }) }
let private refusedWith (predicate: DefaultPlanRefusal -> bool) (result: Result<DefaultPlan, DefaultPlanRefusal list>) =
    match result with
    | Error refusals -> Assert.True(List.exists predicate refusals, sprintf "%A" refusals)
    | Ok _ -> failwith "accepted"

[<Fact>]
let ``version 4 records generate a vocabulary 6 plan with the persisted instance and its codes`` () =
    let plan = generateGate (gateRecords ()) |> ok "plan"
    let w = (view plan.ValidatedPlan).Workflow
    Assert.Equal(VocabularyVersion6, (view plan.ValidatedPlan).Binding.VocabularyVersion)
    Assert.Equal(Some(IdempotencyScope.Durable, StaleAction.Refuse), w.Persistence |> Option.map (fun p -> p.Idempotency, p.OnStale))
    Assert.Contains(w.CodeAssignments, fun a -> a.Target = CodeSubject.NotFound)
    Assert.Contains(w.CodeAssignments, fun a -> a.Target = CodeSubject.StaleRevision)
    Assert.DoesNotContain(plan.Obligations, fun o -> o.Kind = TypedObligationKind.UndecidedPersistence)

[<Fact>]
let ``an absent persistence choice is undecided, never defaulted`` () =
    let r = gateRecords () |> mapContent (function Persistence p -> Persistence { p with Idempotency = None; OnStale = None } | c -> c)
    // A stale-revision code needs a refusing persistence, so drop it here.
    let r =
        r |> mapContent (function
            | StateModel m -> StateModel { m with CodeAssignments = m.CodeAssignments |> List.filter (fun a -> a.Target <> RecordedCodeTarget.StaleRevision) }
            | c -> c)
    let plan = generateGate r |> ok "plan"
    let p = (view plan.ValidatedPlan).Workflow.Persistence |> Option.get
    Assert.Equal((IdempotencyScope.Unresolved, StaleAction.Unresolved), (p.Idempotency, p.OnStale))
    let subjects = plan.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.UndecidedPersistence) |> List.choose _.PlanSubject
    Assert.Equal<string list>([ "persistence:idempotency"; "persistence:onStale" ], List.sort subjects)
    let openSubjects = openChoices (classify plan.ValidatedPlan) |> List.map _.Subject
    for s in subjects do Assert.Contains(s, openSubjects)

[<Fact>]
let ``records and vocabulary must match`` () =
    refusedWith (function DecisionSchemaVocabularyMismatch(DecisionsSchemaVersion4, VocabularyVersion4) -> true | _ -> false)
        (generateDefaultPlan (gateCohort ()) (gateRecords ()) ConstructionVocabulary.Version4)

[<Fact>]
let ``persistence and effect policies exist only in version 4 records`` () =
    // Each version 4 content kind on its own, in an otherwise valid version 3 set.
    let v3 = read "gate-synthetic" "construction-decisions-v3.json"
    for kind, content in
        [ "persistence", """{"kind":"persistence","idempotency":"durable","requirementKeys":["k"]}"""
          "effectPolicies", """{"kind":"effectPolicies","policies":[]}""" ] do
        let text = v3.Replace("\"records\": [", sprintf "\"records\": [ {\"nodeId\": \"DEC-GATE\", \"content\": %s}," content)
        match readDecisionRecords text with
        | Error e -> Assert.Contains("unsupported content kind: " + kind, e)
        | Ok _ -> failwithf "version 3 accepted %s" kind
    let codes = (read gate "construction-decisions-v4.json").Replace(DecisionsSchemaVersion4, DecisionsSchemaVersion3)
    Assert.True(Result.isError (readDecisionRecords codes))

[<Fact>]
let ``one persisted instance per record set`` () =
    let r = gateRecords ()
    let extra = { r with Records = r.Records @ [ { NodeId = "DEC-GATE"; Content = Persistence { Idempotency = Some IdempotencyScope.Session; OnStale = None; RequirementKeys = [ "synthetic/gate.md#GATE-5" ] } } ] }
    refusedWith (function DuplicateRecord "DEC-GATE" -> true | _ -> false) (generateGate extra)

[<Fact>]
let ``version 4 records are canonical and deterministic`` () =
    let r = gateRecords ()
    let again = readDecisionRecords (encodeDecisionRecords r) |> ok "reread"
    Assert.Equal(ConstructionDecisions.digest r, ConstructionDecisions.digest again)
    Assert.Equal((generateGate r |> ok "a").Digest, (generateGate again |> ok "b").Digest)

// ---- Effect policies, on the real timer cohort (test-approved records) ----

let private timer = "time-entry-timer"
let private stopKey = "kemiller2002/time-entry-state-machine/docs/architecture/business-activity-ledger-state-model.md#L131-L144"

let private timerCohort () =
    let manifest = readManifest (read timer "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read timer "blueprint.v2-remain.json") |> ok "blueprint"
    prepareCohort manifest blueprint "COH-TIMER" (set [ "DEC-TIMER-STATES"; "DEC-TIMER-STOP-FAILURE" ]) |> ok "prepare"

/// The approved version 2 records, as version 4: the Stop port drops its
/// onFailure, and DEC-TIMER-STOP-FAILURE (a test approval) states the policy.
let private timerRecords (policy: RecordedEffectPolicy option) =
    let v2 = readDecisionRecords (read timer "construction-decisions-v2.json") |> ok "v2"
    let blueprint = readBlueprint (read timer "blueprint.v2-remain.json") |> ok "blueprint"
    let ports =
        v2 |> mapContent (function
            | PortContract ps -> PortContract(ps |> List.map (function RecordedTransitionEffect(c, _, k, p) -> RecordedTransitionEffect(c, None, k, p) | other -> other))
            | c -> c)
    { ports with
        SchemaVersion = DecisionsSchemaVersion4
        BlueprintDigest = blueprintDigest blueprint
        Records = ports.Records @ (policy |> Option.map (fun p -> { NodeId = "DEC-TIMER-STOP-FAILURE"; Content = EffectPolicies [ p ] }) |> Option.toList) }

let private stopPolicy = { Command = "Stop"; Retries = 2; OnFailed = Some FailedEffectAction.Compensate; OnUnknown = Some UnknownEffectAction.Reconcile; RequirementKeys = [ stopKey ] }

[<Fact>]
let ``an approved policy record generates the effect policy`` () =
    let plan = generateDefaultPlan (timerCohort ()) (timerRecords (Some stopPolicy)) v6 |> ok "plan"
    let p = List.exactlyOne (view plan.ValidatedPlan).Workflow.EffectPolicies
    Assert.Equal((2, FailedEffectAction.Compensate, UnknownEffectAction.Reconcile), (p.Retries, p.OnFailed, p.OnUnknown))
    Assert.DoesNotContain(plan.Obligations, fun o -> o.Kind = TypedObligationKind.UndecidedEffectFailure)
    Assert.Equal(ChoiceClass.Approved [ "DEC-TIMER-STOP-FAILURE" ],
                 ((classify plan.ValidatedPlan).Choices |> List.find (fun c -> c.Subject = "effectFailure:Stop")).Class)

[<Fact>]
let ``an absent action stays undecided, with an obligation, never a default`` () =
    let plan = generateDefaultPlan (timerCohort ()) (timerRecords (Some { stopPolicy with OnUnknown = None })) v6 |> ok "plan"
    let p = List.exactlyOne (view plan.ValidatedPlan).Workflow.EffectPolicies
    Assert.Equal(UnknownEffectAction.Unresolved, p.OnUnknown)
    let o = plan.Obligations |> List.find (fun o -> o.Kind = TypedObligationKind.UndecidedEffectFailure)
    Assert.Contains("what an unknown outcome means", o.Question)

[<Fact>]
let ``every effect needs exactly one policy record, for an effect that exists`` () =
    // Without the test-approved failure decision, nothing states the policy.
    let manifest = readManifest (read timer "source-manifest.json") |> ok "manifest"
    let plain = readBlueprint (read timer "blueprint.json") |> ok "blueprint"
    let cohort = prepareCohort manifest plain "COH-TIMER" (Set.singleton "DEC-TIMER-STATES") |> ok "prepare"
    let noPolicy = { timerRecords None with BlueprintDigest = blueprintDigest plain }
    refusedWith (function MissingEffectPolicyRecord "Stop" -> true | _ -> false) (generateDefaultPlan cohort noPolicy v6)
    let pause = { stopPolicy with Command = "Pause" }
    let r = timerRecords (Some stopPolicy)
    let extra = { r with Records = r.Records |> List.map (fun x -> match x.Content with EffectPolicies ps -> { x with Content = EffectPolicies(ps @ [ pause ]) } | _ -> x) }
    refusedWith (function PolicyForUnknownEffect("DEC-TIMER-STOP-FAILURE", "Pause") -> true | _ -> false) (generateDefaultPlan (timerCohort ()) extra v6)

[<Fact>]
let ``a version 4 effect port carries no onFailure`` () =
    let r = timerRecords (Some stopPolicy)
    let text = (encodeDecisionRecords r).Replace("\"port\": \"transitionEffect\"", "\"onFailure\": \"remain\", \"port\": \"transitionEffect\"")
    Assert.True(Result.isError (readDecisionRecords text))
    Assert.DoesNotContain("onFailure", encodeDecisionRecords r)

[<Fact>]
let ``persistence with effects is refused as a dual write`` () =
    let r = timerRecords (Some stopPolicy)
    let persisted = { r with Records = r.Records @ [ { NodeId = "DEC-TIMER-STATES"; Content = Persistence { Idempotency = Some IdempotencyScope.Durable; OnStale = Some StaleAction.Refuse; RequirementKeys = [ "kemiller2002/time-entry-state-machine/docs/architecture/business-activity-ledger-state-model.md#L116-L129" ] } } ] }
    refusedWith (function GeneratedPlanInvalid vs -> List.contains (PersistenceWithEffects "persistence") vs | _ -> false) (generateDefaultPlan (timerCohort ()) persisted v6)
