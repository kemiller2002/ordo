/// Deterministic construction-plan consensus (DF-SDE-2026-0021). The pilot
/// plans B1, B2 and B3 are real agent outputs from the time-entry timer pilot
/// (research-documents PR #7). The synthetic drafts are the checked-in timer
/// plan with one disagreement each. Every test tries to make one draft's
/// choice win, or to lose a question, and must fail to.
module Ordo.Tests.ConstructionPlanConsensusTests

open System
open System.IO
open System.Security.Cryptography
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionPlanWire
open Ordo.Core.ConstructionPlanConsensus

// ---------------------------------------------------------------------------
// Fixtures.
// ---------------------------------------------------------------------------

let private fixtures () =
    let rec find (current: DirectoryInfo) =
        let candidate = Path.Combine(current.FullName, "tests", "fixtures", "construction")
        if Directory.Exists(Path.Combine(candidate, "time-entry-timer-pilot-01")) then candidate
        else
            match current.Parent with
            | null -> failwith "construction fixtures missing from repository"
            | parent -> find parent
    find (DirectoryInfo AppContext.BaseDirectory)

let private read folder name = File.ReadAllText(Path.Combine(fixtures (), folder, name))
let private timer name = read "time-entry-timer" name
let private pilotText run = read "time-entry-timer-pilot-01" (run + ".plan.json")
let private ok label value = match value with Ok x -> x | Error e -> failwithf "%s: %A" label e

let private manifest () = readManifest (timer "source-manifest.json") |> ok "manifest"
let private cohort = "COH-TIMER"
/// Stands in for the external authority that approves DEC-TIMER-STATES. A
/// test input, not an approval recorded anywhere.
let private approved = Set.singleton "DEC-TIMER-STATES"
let private prepared = prepareCohort (manifest ()) (readBlueprint (timer "blueprint.json") |> ok "blueprint") cohort approved |> ok "prepare"

/// The test-only blueprint whose DEC-TIMER-STOP-FAILURE lets a draft decide
/// the Stop effect (see the time-entry-timer fixture README).
let private preparedV2 =
    let b = readBlueprint (timer "blueprint.v2-remain.json") |> ok "blueprint v2"
    prepareCohort (manifest ()) b cohort (Set.add "DEC-TIMER-STOP-FAILURE" approved) |> ok "prepare v2"

let private basePlan = readPlan (timer "plan.json") |> ok "plan"
let private pilot run = readPlan (pilotText run) |> ok run

let private combineOk prepared drafts = combine prepared drafts |> ok "combine"

let private refused prepared drafts =
    match combine prepared drafts with
    | Ok result -> failwithf "expected a refusal; got a plan %s" result.Digest
    | Error refusals -> refusals

let private opByLabel label (draft: PlanDraft) = draft.Operations |> List.find (fun o -> operationLabel o = label)
let private traceAt label = opByLabel label basePlan |> operationTrace
let private withOps (draft: PlanDraft) ops = { draft with Operations = ops }
let private add ops (draft: PlanDraft) = withOps draft (draft.Operations @ ops)
let private replace label replacement (draft: PlanDraft) =
    withOps draft (draft.Operations |> List.map (fun o -> if operationLabel o = label then replacement else o))

let private transition command from target trace =
    DefineTransition { TransitionDraft.Command = command; From = from; To = target; Guards = []; Trace = trace }

let private unresolved subject effect question trace =
    DeclareUnresolved { UnresolvedDraft.Subject = subject; Effect = effect; Question = question; Trace = trace }

let private labels (result: ConsensusResult) =
    canonicalOperations result.Plan |> List.map operationLabel

let private unresolvedIn (result: ConsensusResult) = (view result.Plan).Workflow.Unresolved

let private noTrace: TraceDraft = { RequirementKeys = []; NodeIds = [] }

let private withoutTrace operation =
    match operation with
    | DefineWorkflow w -> DefineWorkflow { w with Trace = noTrace }
    | DefineState s -> DefineState { s with Trace = noTrace }
    | DefineTransition t -> DefineTransition { t with Trace = noTrace }
    | DefineInvariant i -> DefineInvariant { i with Trace = noTrace }
    | DeclarePort p -> DeclarePort { p with Trace = noTrace }
    | BindCapability c -> BindCapability { c with Trace = noTrace }
    | DeclareUnresolved u -> DeclareUnresolved { u with Trace = noTrace }
    | DeclarePairRule r -> DeclarePairRule { r with Trace = noTrace }
    | DefineRefusal r -> DefineRefusal { r with Trace = noTrace }
    | DeclareUndecidedTransition u -> DeclareUndecidedTransition { u with Trace = noTrace }
    | DeclareExtensionInventory i -> DeclareExtensionInventory { i with Trace = noTrace }
    | DefineRefusalCode c -> DefineRefusalCode { c with Trace = noTrace }
    | AssignRefusalCode a -> AssignRefusalCode { a with Trace = noTrace }
    | DefineBranchingTransition b -> DefineBranchingTransition { b with Trace = noTrace }

let private structuralIdentities (operations: OperationDraft list) =
    operations
    |> List.filter (function DeclareUnresolved _ -> false | _ -> true)
    |> List.map (fun o -> operationLabel o, withoutTrace o)

/// The consensus never invents a structural operation: everything it keeps
/// is, apart from trace, exactly what every draft proposed. The one
/// exception is an effect port whose disposition the drafts disputed, which
/// is kept as `unresolved`.
let private assertNothingInvented prepared (drafts: PlanDraft list) (result: ConsensusResult) =
    let perDraft =
        drafts |> List.map (fun d -> validate prepared d |> ok "draft" |> canonicalOperations |> structuralIdentities |> Set.ofList)
    let disputedEffects =
        result.Disagreements |> List.filter (fun d -> d.Kind = EffectDisposition) |> List.map _.Label |> Set.ofList
    for label, op in canonicalOperations result.Plan |> structuralIdentities do
        if not (Set.contains label disputedEffects) then
            for found in perDraft do
                Assert.True(Set.contains (label, op) found, sprintf "%s is not in every draft" label)

let rec private permutations list =
    match list with
    | [] -> [ [] ]
    | _ -> list |> List.collect (fun x -> permutations (List.filter ((<>) x) list) |> List.map (fun rest -> x :: rest))

let private sha256File (text: string) =
    text |> Text.Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()

// ---------------------------------------------------------------------------
// 1. Pilot evidence: three independent agents, one cohort.
// ---------------------------------------------------------------------------

/// The pilot plans are copied verbatim; an edit changes these.
let private pilotFileHashes =
    [ "B1", "bf26bd2084968126b9cb5d4b9636aec1842ed0b372d9b8553c264a0bc21ba3a2"
      "B2", "0214996d2e6b316199085eb9505cbbe018166ada7f9ba3823b1cf409c6a8ec5a"
      "B3", "c9b5c4e9e98dea95de747945f11bd193cbea8c91c76f517c1ba0b5107a682629" ]

/// Changing consensus or canonicalization changes this value; that is a
/// documented semantic change, not a constant to update.
let private pinnedPilotConsensusDigest = "sha256:bac1177f3f61cebc14555d85ba2988118dc3c7f987590dd4689fffd6c7a12349"

let private pilots () = [ pilot "B1"; pilot "B2"; pilot "B3" ]

[<Fact>]
let ``the pilot plans are the verbatim agent outputs`` () =
    for run, expected in pilotFileHashes do
        Assert.Equal(expected, sha256File (pilotText run))

[<Fact>]
let ``the pilot plans bind the timer fixture cohort and each validates on its own`` () =
    for run in [ "B1"; "B2"; "B3" ] do
        let draft = pilot run
        Assert.Equal(basePlan.Binding, draft.Binding)
        Assert.Equal("sha256:03e29eddb6e528baea0707ec6660f18b0495ac8a6424f3c794a53cc2f5925a01", draft.Binding.SourceManifestDigest)
        Assert.Equal("sha256:28dd3964f9c49f3492f3c34174ab04d57f4ddcba298276b2a287b4a6f3fdc4cb", draft.Binding.BlueprintDigest)
        Assert.Equal(draft.Binding.SourceManifestDigest, (preparedCohortView prepared).Manifest.Digest)
        Assert.Equal(draft.Binding.BlueprintDigest, blueprintDigest (preparedCohortView prepared).Blueprint)
        let plan = validate prepared draft |> ok run
        Assert.True(isBoundTo prepared plan)

[<Fact>]
let ``the pilot consensus agrees on 11 structural operations, disputes none and keeps all 9 questions`` () =
    let drafts = pilots ()
    let result = combineOk prepared drafts
    Assert.Equal<string list>(
        [ "port:currentTime"; "port:transitionEffect/Stop"
          "state:NoTimer"; "state:Paused"; "state:Running"
          "transition:NoTimer/Start"; "transition:Paused/Resume"; "transition:Paused/Stop"
          "transition:Running/Pause"; "transition:Running/Stop"
          "workflow:Timer" ],
        result.Agreed)
    Assert.Empty(result.Disagreements)
    Assert.Equal(9, result.Unresolved.Length)
    Assert.Equal(9, (unresolvedIn result).Length)
    // Every question any planner raised survives with its own words.
    let raised =
        drafts
        |> List.collect (fun d -> d.Operations |> List.choose (function DeclareUnresolved u -> Some u.Question | _ -> None))
        |> List.sort
    Assert.Equal<string list>(raised, result.Unresolved |> List.map _.Question |> List.sort)
    // Only B2 asked what a failed Stop means; the consensus keeps it.
    let b2 = result.Drafts |> List.find (fun d -> d.Digest = digest (validate prepared (pilot "B2") |> ok "B2"))
    let failure = result.Unresolved |> List.find (fun q -> q.Subject = "StopCompletionFailure")
    Assert.Equal<int list>([ b2.Index ], failure.RaisedBy)
    Assert.Equal(pinnedPilotConsensusDigest, result.Digest)
    // Each draft's own digest is the one the pilot's structural verification
    // reported (results.md), and drafts are numbered in digest order.
    Assert.Equal<(int * string) list>(
        [ 1, "sha256:1993df015678980493b7e9c7e8a602205c75bf2eb40c4aaa8ac4541f4afbcfa8" // B1
          2, "sha256:e3b50f04a97468e1081b2c6b83abd1e7725cd650062c020afe5819e2f2cc1644" // B3
          3, "sha256:f6544d88aba44849dc1cd36282ca8be782d3aa497d7e7d8c790af81fb7a096cc" ], // B2
        result.Drafts |> List.map (fun d -> d.Index, d.Digest))
    Assert.Equal(result.Digest, digest result.Plan)
    assertNothingInvented prepared drafts result

[<Fact>]
let ``the pilot consensus keeps each question's trace and the plan's coverage`` () =
    let drafts = pilots ()
    let result = combineOk prepared drafts
    let w = (view result.Plan).Workflow
    for draft in drafts do
        for op in draft.Operations do
            match op with
            | DeclareUnresolved u ->
                let kept = w.Unresolved |> List.find (fun k -> k.Question = u.Question)
                Assert.True(Set.isSubset (Set.ofList u.Trace.RequirementKeys) (Set.ofList kept.Trace.RequirementKeys))
                Assert.True(Set.isSubset (Set.ofList u.Trace.NodeIds) (Set.ofList kept.Trace.NodeIds))
            | _ -> ()
    // Structural traces are the union of the drafts' traces.
    let start = w.Transitions |> List.find (fun t -> t.From.Key = "NoTimer")
    let union =
        drafts |> List.collect (fun d -> (operationTrace (opByLabel "transition:NoTimer/Start" d)).NodeIds) |> List.distinct |> List.sort
    Assert.Equal<string list>(union, start.Trace.NodeIds)
    for _, coverage in Map.toList (view result.Plan).Coverage do
        match coverage with
        | Constructed _ | WorkflowLevel -> ()
        | UnresolvedOnly ops -> Assert.NotEmpty ops

[<Fact>]
let ``the pilot consensus does not depend on the order of the drafts`` () =
    let results = permutations (pilots ()) |> List.map (combineOk prepared)
    Assert.Equal(6, results.Length)
    for result in results do
        Assert.Equal(results.Head, result)

// ---------------------------------------------------------------------------
// 2. Synthetic disagreements: nothing proposed by only some drafts is chosen.
// ---------------------------------------------------------------------------

let private restart = transition "Restart" "Paused" "Running" (traceAt "transition:Paused/Resume")
let private withRestart = basePlan |> add [ restart ]

[<Fact>]
let ``an extra transition in one draft becomes a disagreement, not a construct`` () =
    let drafts = [ basePlan; basePlan; withRestart ]
    let result = combineOk prepared drafts
    Assert.DoesNotContain("transition:Paused/Restart", labels result)
    let d = Assert.Single result.Disagreements
    Assert.Equal(Structural, d.Kind)
    Assert.Equal("transition:Paused/Restart", d.Label)
    Assert.Equal("DisagreementTransitionPausedRestart", d.Subject)
    let proposer = result.Drafts |> List.find (fun x -> x.Digest = digest (validate prepared withRestart |> ok "restart")) |> _.Index
    Assert.Equal<int list>([ proposer ], d.Proposing)
    Assert.Equal<int list>([ 1; 2; 3 ] |> List.except [ proposer ], d.NotProposing)
    let record = unresolvedIn result |> List.find (fun u -> u.Subject.Key = d.Subject)
    let others = [ 1; 2; 3 ] |> List.except [ proposer ]
    Assert.Equal(
        sprintf "Draft %d proposed defineTransition Restart from Paused to Running. Drafts %d and %d proposed nothing for transition:Paused/Restart. Consensus does not choose between independent drafts; a person must decide."
            proposer others[0] others[1],
        record.Question)
    Assert.Equal(None, record.Effect)
    // Requirement coverage is preserved through the record's trace.
    let restartTrace = traceAt "transition:Paused/Resume"
    Assert.Equal<string list>(restartTrace.RequirementKeys |> List.distinct |> List.sort, record.Trace.RequirementKeys)
    assertNothingInvented prepared drafts result

[<Fact>]
let ``two agreeing drafts do not outvote one dissent: there is no majority`` () =
    let drafts = [ withRestart; withRestart; basePlan ]
    let result = combineOk prepared drafts
    Assert.DoesNotContain("transition:Paused/Restart", labels result)
    let d = Assert.Single result.Disagreements
    Assert.Equal(2, d.Proposing.Length)
    Assert.Equal(1, d.NotProposing.Length)
    let only = Assert.Single d.Proposals
    Assert.Equal<int list>(d.Proposing, only.Drafts)

[<Fact>]
let ``a conflicting target drops both transitions into one disagreement`` () =
    let other = basePlan |> replace "transition:Paused/Stop" (transition "Stop" "Paused" "Running" (traceAt "transition:Paused/Stop"))
    let drafts = [ basePlan; other ]
    let result = combineOk prepared drafts
    Assert.DoesNotContain("transition:Paused/Stop", labels result)
    let d = Assert.Single result.Disagreements
    Assert.Equal("transition:Paused/Stop", d.Label)
    Assert.Equal<string list>(
        [ "defineTransition Stop from Paused to NoTimer"; "defineTransition Stop from Paused to Running" ],
        d.Proposals |> List.map _.Variant |> List.sort)
    Assert.Equal<int list>([ 1; 2 ], d.Proposing)
    Assert.Empty(d.NotProposing)
    Assert.Equal(1, unresolvedIn result |> List.filter (fun u -> u.Subject.Key = "DisagreementTransitionPausedStop") |> List.length)
    assertNothingInvented prepared drafts result

[<Fact>]
let ``a disagreement that leaves the plan invalid is refused with the disagreement list`` () =
    // Dropping the disputed Start leaves Running and Paused unreachable. The
    // consensus does not patch the plan or pick a target; a person decides.
    let other = basePlan |> replace "transition:NoTimer/Start" (transition "Start" "No Timer" "Paused" (traceAt "transition:NoTimer/Start"))
    match refused prepared [ basePlan; other ] with
    | [ CombinedPlanInvalid(violations, disagreements) ] ->
        Assert.Contains(UnreachableState "Running", violations)
        Assert.Contains(UnreachableState "Paused", violations)
        let d = Assert.Single disagreements
        Assert.Equal("transition:NoTimer/Start", d.Label)
        Assert.Equal(2, d.Proposals.Length)
    | other -> failwithf "unexpected refusal %A" other

[<Fact>]
let ``a differing state role is a disagreement, and the dangling transition makes the plan refused`` () =
    let stateTrace = opByLabel "state:Running" basePlan |> operationTrace
    let archive = transition "Archive" "No Timer" "Archived" (traceAt "transition:NoTimer/Start")
    let terminal = basePlan |> add [ DefineState { StateDraft.Name = "Archived"; Role = Terminal; Trace = stateTrace }; archive ]
    let ordinary =
        basePlan
        |> add [ DefineState { StateDraft.Name = "Archived"; Role = Ordinary; Trace = stateTrace }
                 archive
                 transition "Restore" "Archived" "No Timer" (traceAt "transition:NoTimer/Start") ]
    validate prepared terminal |> ok "terminal draft" |> ignore
    validate prepared ordinary |> ok "ordinary draft" |> ignore
    match refused prepared [ terminal; ordinary ] with
    | [ CombinedPlanInvalid(violations, disagreements) ] ->
        Assert.Contains(UnknownState("transition:NoTimer/Archive", "Archived"), violations)
        Assert.Equal<string list>([ "state:Archived"; "transition:Archived/Restore" ], disagreements |> List.map _.Label)
        let role = disagreements |> List.find (fun d -> d.Label = "state:Archived")
        Assert.Equal<string list>(
            [ "defineState Archived ordinary"; "defineState Archived terminal" ],
            role.Proposals |> List.map _.Variant |> List.sort)
    | other -> failwithf "unexpected refusal %A" other

[<Fact>]
let ``differently worded questions under one subject are both kept`` () =
    let reworded =
        basePlan
        |> replace "unresolved:TimerScope" (unresolved "Timer scope" None "Is there one timer per person, per device or per session?" (traceAt "unresolved:TimerScope"))
    let result = combineOk prepared [ basePlan; reworded ]
    Assert.Empty(result.Disagreements)
    let scope = result.Unresolved |> List.filter (fun q -> q.RaisedSubject = "TimerScope")
    Assert.Equal<string list>([ "TimerScopeDraft1"; "TimerScopeDraft2" ], scope |> List.map _.Subject |> List.sort)
    Assert.Equal(2, scope |> List.map _.Question |> List.distinct |> List.length)
    for q in scope do
        Assert.Equal(1, q.RaisedBy.Length)
    // The identical question is deduplicated, raised by both.
    let refused' = result.Unresolved |> List.find (fun q -> q.RaisedSubject = "StopCompletionRefused")
    Assert.Equal<int list>([ 1; 2 ], refused'.RaisedBy)

// ---------------------------------------------------------------------------
// 3. Vocabulary 2: a disputed effect-failure disposition is not decided.
// ---------------------------------------------------------------------------

let private v2 disposition =
    let draft = readPlan (timer (sprintf "plan.v2-%s.json" disposition)) |> ok disposition
    // One cohort for all: the decided variants' blueprint. The workflow name
    // only separates the generated goldens, so it is made common here.
    let remain = readPlan (timer "plan.v2-remain.json") |> ok "remain"
    { draft with Binding = remain.Binding }
    |> replace (operationLabel (draft.Operations |> List.find (function DefineWorkflow _ -> true | _ -> false)))
        (DefineWorkflow { WorkflowDraft.Name = "Timer"; Trace = traceAt "workflow:Timer" })

let private stopPort (result: ConsensusResult) =
    (view result.Plan).Workflow.Ports |> List.pick (fun p -> match p.Kind with TransitionEffectPort(c, d) -> Some(c.Key, d) | CurrentTimePort -> None)

[<Fact>]
let ``an onFailure disagreement keeps the port unresolved and links a disagreement record`` () =
    let drafts = [ v2 "remain"; v2 "unresolved" ]
    for d in drafts do validate preparedV2 d |> ok "v2 draft" |> ignore
    let result = combineOk preparedV2 drafts
    Assert.Equal(("Stop", Some EffectFailureDisposition.Unresolved), stopPort result)
    let d = Assert.Single result.Disagreements
    Assert.Equal(EffectDisposition, d.Kind)
    Assert.Equal("port:transitionEffect/Stop", d.Label)
    Assert.Equal<string list>([ "onFailure remain"; "onFailure unresolved" ], d.Proposals |> List.map _.Variant |> List.sort)
    let linked = unresolvedIn result |> List.filter (fun u -> u.Effect.IsSome)
    let link = Assert.Single linked
    Assert.Equal("DisagreementPortTransitionEffectStop", link.Subject.Key)
    Assert.Equal("Stop", link.Effect.Value.Key)
    // The unresolved draft's own question is kept, unlinked.
    let own = result.Unresolved |> List.find (fun q -> q.RaisedSubject = "StopCompletionRefused")
    Assert.Equal(None, own.Effect)
    Assert.DoesNotContain("port:transitionEffect/Stop", result.Agreed)
    assertNothingInvented preparedV2 drafts result

[<Fact>]
let ``remain against proceed is not settled by either`` () =
    let result = combineOk preparedV2 [ v2 "remain"; v2 "proceed" ]
    Assert.Equal(("Stop", Some EffectFailureDisposition.Unresolved), stopPort result)
    let d = Assert.Single result.Disagreements
    Assert.Equal<string list>([ "onFailure proceed"; "onFailure remain" ], d.Proposals |> List.map _.Variant |> List.sort)

[<Fact>]
let ``an agreed decided disposition is kept`` () =
    let result = combineOk preparedV2 [ v2 "remain"; v2 "remain" ]
    Assert.Equal(("Stop", Some EffectFailureDisposition.Remain), stopPort result)
    Assert.Empty(result.Disagreements)

[<Fact>]
let ``differently worded links to one unresolved effect are all kept and none is chosen`` () =
    let other =
        v2 "unresolved"
        |> replace "unresolved:StopCompletionRefused"
            (unresolved "Stop failure" (Some "Stop") "If the entry is refused, is the timer kept running or discarded?" (traceAt "unresolved:StopCompletionRefused"))
    let drafts = [ v2 "unresolved"; other ]
    let result = combineOk preparedV2 drafts
    Assert.Equal(("Stop", Some EffectFailureDisposition.Unresolved), stopPort result)
    let d = Assert.Single result.Disagreements
    Assert.Equal(EffectQuestion, d.Kind)
    Assert.Equal("effect-question:Stop", d.Label)
    let link = unresolvedIn result |> List.filter (fun u -> u.Effect.IsSome) |> Assert.Single
    Assert.Equal("DisagreementEffectQuestionStop", link.Subject.Key)
    for subject in [ "StopCompletionRefused"; "StopFailure" ] do
        let q = result.Unresolved |> List.find (fun q -> q.Subject = subject)
        Assert.Equal(None, q.Effect)
    Assert.Contains("port:transitionEffect/Stop", result.Agreed)

[<Fact>]
let ``one agreed linked question keeps its link`` () =
    let result = combineOk prepared [ readPlan (timer "plan.v2-unresolved.json") |> ok "u"; readPlan (timer "plan.v2-unresolved.json") |> ok "u" ]
    Assert.Empty(result.Disagreements)
    let link = unresolvedIn result |> List.filter (fun u -> u.Effect.IsSome) |> Assert.Single
    Assert.Equal("StopCompletionRefused", link.Subject.Key)

// ---------------------------------------------------------------------------
// 4. Refusals: inputs are never dropped or mixed.
// ---------------------------------------------------------------------------

[<Fact>]
let ``a draft that does not validate is refused by position, never dropped`` () =
    let broken = basePlan |> add [ transition "Stop" "Running" "Paused" (traceAt "transition:Running/Stop") ]
    match refused prepared [ basePlan; broken; basePlan ] with
    | [ InvalidDraft(2, violations) ] -> Assert.Contains(ConflictingTransition("Running", "Stop", [ "NoTimer"; "Paused" ]), violations)
    | other -> failwithf "unexpected refusal %A" other

[<Fact>]
let ``a single draft or none is refused`` () =
    Assert.Equal<ConsensusRefusal list>([ TooFewDrafts 1 ], refused prepared [ basePlan ])
    Assert.Equal<ConsensusRefusal list>([ TooFewDrafts 0 ], refused prepared [])

[<Fact>]
let ``mixed vocabulary versions are refused even when each draft validates`` () =
    let second = readPlan (timer "plan.v2-unresolved.json") |> ok "v2"
    validate prepared second |> ok "v2 alone" |> ignore
    Assert.Equal<ConsensusRefusal list>(
        [ MixedVocabularyVersions [ VocabularyVersion1; VocabularyVersion2 ] ],
        refused prepared [ basePlan; second ])

[<Fact>]
let ``mixed schema versions are refused`` () =
    match refused prepared [ basePlan; { basePlan with SchemaVersion = "ordo.construction-plan/9" } ] with
    | MixedSchemaVersions [ _; _ ] :: InvalidDraft(2, _) :: [] -> ()
    | other -> failwithf "unexpected refusal %A" other

[<Fact>]
let ``drafts bound to another cohort cannot be combined`` () =
    let v2Bound = v2 "unresolved"
    match refused prepared [ basePlan; { v2Bound with VocabularyVersion = VocabularyVersion1 } ] with
    | [ InvalidDraft(2, violations) ] -> Assert.Contains(violations, fun v -> match v with StaleBlueprint _ -> true | _ -> false)
    | other -> failwithf "unexpected refusal %A" other

// ---------------------------------------------------------------------------
// 5. Determinism and idempotence.
// ---------------------------------------------------------------------------

[<Fact>]
let ``every permutation of three disagreeing drafts gives the same consensus`` () =
    let conflicting = basePlan |> replace "transition:Paused/Stop" (transition "Stop" "Paused" "Running" (traceAt "transition:Paused/Stop"))
    let reworded = basePlan |> replace "unresolved:TimerScope" (unresolved "Timer scope" None "Per person or per session?" (traceAt "unresolved:TimerScope"))
    let drafts = [ withRestart; conflicting; reworded ]
    let results = permutations drafts |> List.map (combineOk prepared)
    Assert.Equal(6, results.Length)
    for result in results do
        Assert.Equal(results.Head, result)
        Assert.Equal(results.Head.Digest, result.Digest)
        Assert.Equal(canonicalJson results.Head.Plan, canonicalJson result.Plan)
    Assert.Equal<string list>([ "transition:Paused/Restart"; "transition:Paused/Stop" ], results.Head.Disagreements |> List.map _.Label)

[<Fact>]
let ``every permutation of a refused set gives the same refusal`` () =
    let other = basePlan |> replace "transition:NoTimer/Start" (transition "Start" "No Timer" "Paused" (traceAt "transition:NoTimer/Start"))
    let refusals = permutations [ basePlan; other; withRestart ] |> List.map (refused prepared)
    for r in refusals do
        Assert.Equal<ConsensusRefusal list>(refusals.Head, r)

[<Fact>]
let ``the consensus of identical drafts is that plan`` () =
    let alone = validate prepared basePlan |> ok "plan"
    for count in [ 2; 3 ] do
        let result = combineOk prepared (List.replicate count basePlan)
        Assert.Equal(digest alone, result.Digest)
        Assert.Equal(canonicalJson alone, canonicalJson result.Plan)
        Assert.Empty(result.Disagreements)
        for q in result.Unresolved do
            Assert.Equal<int list>([ 1 .. count ], q.RaisedBy)

[<Fact>]
let ``identical structure with different traces combines to the union of traces`` () =
    let label = "transition:Running/Pause"
    let extra = { (traceAt label) with NodeIds = (traceAt label).NodeIds @ [ "VER-TIMER-TABLE" ] }
    let traced = basePlan |> replace label (transition "Pause" "Running" "Paused" extra)
    let result = combineOk prepared [ basePlan; traced ]
    Assert.Empty(result.Disagreements)
    let expected = validate prepared traced |> ok "union"
    Assert.Equal(digest expected, result.Digest)
    Assert.Equal(canonicalJson expected, canonicalJson result.Plan)

// ---------------------------------------------------------------------------
// 6. No authority.
// ---------------------------------------------------------------------------

[<Fact>]
let ``a consensus plan is an ordinary validated plan and revalidates to itself`` () =
    let result = combineOk prepared (pilots ())
    let reread = readPlan (canonicalJson result.Plan) |> ok "reread"
    let again = validate prepared reread |> ok "revalidate"
    Assert.Equal(result.Digest, digest again)
    Assert.True(isBoundTo prepared result.Plan)
    // The binding is exactly the cohort's: consensus adds no decision.
    Assert.Equal<string list>([ "DEC-TIMER-STATES" ], (view result.Plan).Binding.DecisionIds)
