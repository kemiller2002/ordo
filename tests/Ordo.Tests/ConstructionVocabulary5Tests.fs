/// CONSTRUCT-010 phase 2, increment 2: plan vocabulary 5 effect outcome
/// policies. Every effect states, separately, what a definite failure means
/// (after at most five retries) and what an unknown outcome means; an
/// unknown outcome is never retried, and compensation needs a cited, bound
/// decision. The fixture is the real timer cohort.
module Ordo.Tests.ConstructionVocabulary5Tests

open System
open System.IO
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionPlanWire
open Ordo.Core.ConstructionClassification
module ConstructionPlan = Ordo.Core.ConstructionPlan

let private directory () =
    let rec find (current: DirectoryInfo) =
        let candidate = Path.Combine(current.FullName, "tests", "fixtures", "construction", "time-entry-timer")
        if Directory.Exists candidate then candidate
        else
            match current.Parent with
            | null -> failwith "timer fixture missing from repository"
            | parent -> find parent
    find (DirectoryInfo AppContext.BaseDirectory)

let private read name = File.ReadAllText(Path.Combine(directory (), name))
let private ok label value = match value with Ok x -> x | Error e -> failwithf "%s: %A" label e

let private prepared blueprint approved =
    let manifest = readManifest (read "source-manifest.json") |> ok "manifest"
    let b = readBlueprint (read blueprint) |> ok "blueprint"
    prepareCohort manifest b "COH-TIMER" approved |> ok "prepare"

let private policyCohort () = prepared "blueprint.v2-remain.json" (set [ "DEC-TIMER-STATES"; "DEC-TIMER-STOP-FAILURE" ])
let private escalateCohort () = prepared "blueprint.json" (set [ "DEC-TIMER-STATES" ])
let private policyDraft () = readPlan (read "plan.v5-policy.json") |> ok "policy plan"
let private escalateDraft () = readPlan (read "plan.v5-escalate.json") |> ok "escalate plan"

let private violations cohort draft =
    match validate cohort draft with
    | Ok _ -> []
    | Error vs -> vs

let private mapPolicy f (draft: PlanDraft) =
    { draft with Operations = draft.Operations |> List.map (function DeclareEffectPolicy p -> DeclareEffectPolicy(f p) | other -> other) }

[<Fact>]
let ``both timer policies validate and round-trip`` () =
    for cohort, draft in [ policyCohort (), policyDraft (); escalateCohort (), escalateDraft () ] do
        let v = validate cohort draft |> ok "validate"
        let again = readPlan (encodePlan draft) |> ok "reread"
        Assert.Equal(ConstructionPlan.digest v, ConstructionPlan.digest (validate cohort again |> ok "again"))
    let p = List.exactlyOne (view (validate (policyCohort ()) (policyDraft ()) |> ok "v")).Workflow.EffectPolicies
    Assert.Equal((2, FailedEffectAction.Compensate, UnknownEffectAction.Reconcile), (p.Retries, p.OnFailed, p.OnUnknown))

[<Fact>]
let ``every effect needs a policy, and the port no longer carries onFailure`` () =
    let without = { policyDraft () with Operations = (policyDraft ()).Operations |> List.filter (function DeclareEffectPolicy _ -> false | _ -> true) }
    Assert.Contains(MissingEffectPolicy "Stop", violations (policyCohort ()) without)
    let withDisposition =
        { policyDraft () with
            Operations =
                (policyDraft ()).Operations
                |> List.map (function
                    | DeclarePort({ Kind = TransitionEffect(c, None) } as p) -> DeclarePort { p with Kind = TransitionEffect(c, Some EffectFailureDisposition.Remain) }
                    | other -> other) }
    Assert.Contains(violations (policyCohort ()) withDisposition, function NotInVocabulary(_, "onFailure", VocabularyVersion5) -> true | _ -> false)
    let text = (read "plan.v5-policy.json").Replace("\"port\": \"transitionEffect\",", "\"port\": \"transitionEffect\", \"onFailure\": \"remain\",")
    Assert.True(Result.isError (readPlan text))

[<Fact>]
let ``a policy is not in earlier vocabularies`` () =
    let v4 = { policyDraft () with VocabularyVersion = VocabularyVersion4 }
    Assert.Contains(violations (policyCohort ()) v4, function NotInVocabulary(_, "declareEffectPolicy", VocabularyVersion4) -> true | _ -> false)
    let v4Text =
        (read "plan.v5-policy.json")
            .Replace(VocabularyVersion5, VocabularyVersion4)
            .Replace("\"port\": \"transitionEffect\",", "\"port\": \"transitionEffect\", \"onFailure\": \"remain\",")
    match readPlan v4Text with
    | Error e -> Assert.Contains("is not in " + VocabularyVersion4, e)
    | Ok _ -> failwith "vocabulary 4 accepted an effect policy"

[<Fact>]
let ``retries are bounded`` () =
    Assert.Contains(RetriesOutOfRange("effectPolicy:Stop", 6), violations (policyCohort ()) (policyDraft () |> mapPolicy (fun p -> { p with Retries = 6 })))
    Assert.Contains(RetriesOutOfRange("effectPolicy:Stop", -1), violations (policyCohort ()) (policyDraft () |> mapPolicy (fun p -> { p with Retries = -1 })))
    match readPlan ((read "plan.v5-policy.json").Replace("\"retries\": 2", "\"retries\": 9")) with
    | Error e -> Assert.Contains("retries must be 0 to 5", e)
    | Ok _ -> failwith "the reader accepted 9 retries"

[<Fact>]
let ``compensation needs a cited, bound decision`` () =
    let uncited = policyDraft () |> mapPolicy (fun p -> { p with Trace = { p.Trace with NodeIds = [ "IFC-TIMER-PORTS" ] } })
    Assert.Contains(CompensationWithoutDecision "effectPolicy:Stop", violations (policyCohort ()) uncited)

[<Fact>]
let ``an open policy needs its linked question, and a decided one may not have one`` () =
    let noLink = { escalateDraft () with Operations = (escalateDraft ()).Operations |> List.filter (function DeclareUnresolved { Effect = Some _ } -> false | _ -> true) }
    Assert.Contains(UnresolvedEffectWithoutObligation "effectPolicy:Stop", violations (escalateCohort ()) noLink)
    let decided = escalateDraft () |> mapPolicy (fun p -> { p with OnUnknown = UnknownEffectAction.Escalate })
    Assert.Contains(violations (escalateCohort ()) decided, function EffectAlreadyDecided(_, "Stop") -> true | _ -> false)

[<Fact>]
let ``a policy for an effect the plan does not declare is refused`` () =
    let trace = (policyDraft ()).Operations |> List.pick (function DeclareEffectPolicy p -> Some p.Trace | _ -> None)
    let extra = { policyDraft () with Operations = (policyDraft ()).Operations @ [ DeclareEffectPolicy { Effect = "Pause"; Retries = 0; OnFailed = FailedEffectAction.Remain; OnUnknown = UnknownEffectAction.Escalate; Trace = trace } ] }
    let vs = violations (policyCohort ()) extra
    Assert.True(vs |> List.exists (function UnknownEffect("effectPolicy:Pause", "Pause") -> true | _ -> false), sprintf "%A" vs)

[<Fact>]
let ``the policy is classified: approved compensation, or unresolved`` () =
    let classOf cohort draft subject =
        ((classify (validate cohort draft |> ok "v")).Choices |> List.find (fun c -> c.Subject = subject)).Class
    Assert.Equal(ChoiceClass.Approved [ "DEC-TIMER-STOP-FAILURE" ], classOf (policyCohort ()) (policyDraft ()) "effectFailure:Stop")
    match classOf (escalateCohort ()) (escalateDraft ()) "effectFailure:Stop" with
    | ChoiceClass.Unresolved _ -> ()
    | other -> failwithf "%A" other
