/// CONSTRUCT-001 (GH-64): the typed construction plan over a real, prepared
/// ECIR cohort derived from the time-entry timer requirements, plus
/// compile-negative proof that unchecked input cannot stand in for a
/// validated plan.
module Ordo.Tests.ConstructionPlanTests

open System
open System.IO
open System.Security.Cryptography
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionPlanWire

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
let private draft () = readPlan (read "plan.json") |> ok "plan"
let private cohort = "COH-TIMER"
/// Stands in for the external authority that approves DEC-TIMER-STATES. It
/// is a test input, not an approval recorded anywhere.
let private approved = Set.singleton "DEC-TIMER-STATES"
let private prepared () = prepareCohort (manifest ()) (blueprint ()) cohort approved |> ok "prepare"

/// Changing canonicalization changes this value; that requires a vocabulary
/// or plan schema version change, not an updated constant.
let private pinnedPlanDigest = "sha256:860dd8fe579b4932207e77457373bb833180653f368376669090fdb5eb29fba8"

/// Vocabulary 2 variants of the same plan, one per disposition of the Stop
/// effect (ordo.construction-vocabulary/2). Pinned for the same reason.
let private pinnedV2Digests =
    [ "remain", "sha256:d0aa4619c6739dd185241031288acff0ecb155efbef689cd7fceb76b458a1a32"
      "proceed", "sha256:05c9f7b75b754082192cd77ca4f78aad464a6479b6d6187b7334066ab0804daf"
      "unresolved", "sha256:b498ab7c4e4b3d96cfa7e31347e119a5d0b900770dc47676f394a158a07542c9" ]

let private draftV2 disposition = readPlan (read (sprintf "plan.v2-%s.json" disposition)) |> ok ("plan v2 " + disposition)

/// A decided disposition (remain, proceed) must cite a required decision, so
/// those variants bind a test-only blueprint that adds DEC-TIMER-STOP-FAILURE.
/// Its approval here stands in for the external authority; it is a test
/// input, not a recorded decision.
let private stopDecision = "DEC-TIMER-STOP-FAILURE"

let private preparedV2 disposition =
    match disposition with
    | "unresolved" -> prepared ()
    | decided ->
        let b = readBlueprint (read (sprintf "blueprint.v2-%s.json" decided)) |> ok ("blueprint v2 " + decided)
        prepareCohort (manifest ()) b cohort (Set.add stopDecision approved) |> ok ("prepare v2 " + decided)

[<Fact>]
let ``fixture requirement digests are the digests of the verbatim source excerpts`` () =
    let sources = Path.Combine(fixtureDirectory (), "sources")
    let digests =
        Directory.GetFiles sources
        |> Array.map (fun file -> "sha256:" + (File.ReadAllBytes file |> SHA256.HashData |> Convert.ToHexString).ToLowerInvariant())
        |> Set.ofArray
    let m = manifest ()
    Assert.True(verifyManifestDigest m |> Result.isOk)
    Assert.Equal<Set<string>>(digests, m.Requirements |> List.map _.ContentDigest |> Set.ofList)

[<Fact>]
let ``the time-entry timer plan validates against its prepared cohort with a pinned digest`` () =
    let plan = validate (prepared ()) (draft ()) |> ok "validate"
    Assert.Equal(pinnedPlanDigest, digest plan)
    let w = (view plan).Workflow
    Assert.Equal("Timer", w.Name.Key)
    Assert.Equal<string list>([ "NoTimer"; "Paused"; "Running" ], w.States |> List.map _.Name.Key)
    Assert.Equal(Initial, (List.head w.States).Role)
    Assert.Equal<string list>([ "Pause"; "Resume"; "Start"; "Stop" ], w.Commands |> List.map _.Key)
    Assert.Equal(5, w.Transitions.Length)
    Assert.Equal(2, w.Unresolved.Length)

[<Fact>]
let ``the ambiguous short-stop requirement is preserved but cannot be planned or prepared`` () =
    let m, b = manifest (), blueprint ()
    let short = b.Requirements |> List.find (fun r -> match r.Disposition with Unresolved _ -> true | _ -> false)
    Assert.Empty(Ordo.Core.ConstructionIr.validate m b)
    Assert.True(Result.isError (prepareCohort m b "CONFLICT-TIMER-SHORT-STOP" approved))
    let d = draft ()
    let smuggled =
        { d with
            Operations =
                d.Operations
                @ [ DeclareUnresolved { Subject = "Short stop"; Effect = None; Question = "decide later"; Trace = { RequirementKeys = [ short.Source.Key ]; NodeIds = [ "CONFLICT-TIMER-SHORT-STOP" ] } } ] }
    match validate (prepared ()) smuggled with
    | Ok _ -> failwith "an unresolved requirement outside the cohort was planned"
    | Error errors ->
        Assert.Contains(UnassignedRequirement("unresolved:ShortStop", short.Source.Key), errors)
        Assert.Contains(UnauthorizedNodeReference("unresolved:ShortStop", "CONFLICT-TIMER-SHORT-STOP"), errors)

[<Fact>]
let ``revoking the decision authorization prevents any plan`` () =
    Assert.True(Result.isError (prepareCohort (manifest ()) (blueprint ()) cohort Set.empty))

[<Fact>]
let ``a plan formed against an earlier blueprint is stale`` () =
    let b = blueprint ()
    let revised = { b with Nodes = b.Nodes |> List.map (fun n -> if n.Id = "IFC-TIMER-PORTS" then { n with Justification = Some "revised" } else n) }
    let p = prepareCohort (manifest ()) revised cohort approved |> ok "prepare revised"
    match validate p (draft ()) with
    | Error errors -> Assert.Contains(StaleBlueprint((draft ()).Binding.BlueprintDigest, blueprintDigest revised), errors)
    | Ok _ -> failwith "stale plan accepted"

[<Fact>]
let ``reading and re-encoding the checked-in plan is lossless`` () =
    let d = draft ()
    Assert.Equal(d, readPlan (encodePlan d) |> ok "round trip")
    Assert.Equal(read "plan.json", encodePlan d + "\n")
    for disposition, _ in pinnedV2Digests do
        let v2 = draftV2 disposition
        Assert.Equal(v2, readPlan (encodePlan v2) |> ok "v2 round trip")
        Assert.Equal(read (sprintf "plan.v2-%s.json" disposition), encodePlan v2 + "\n")

[<Fact>]
let ``the vocabulary 1 timer plan keeps its meaning: no disposition, no effect link`` () =
    let d = draft ()
    Assert.Equal(VocabularyVersion1, d.VocabularyVersion)
    let plan = validate (prepared ()) d |> ok "validate"
    Assert.Equal(pinnedPlanDigest, digest plan)
    let w = (view plan).Workflow
    Assert.All(w.Ports, fun p -> match p.Kind with TransitionEffectPort(_, d) -> Assert.Equal(None, d) | CurrentTimePort -> ())
    Assert.All(w.Unresolved, fun u -> Assert.Equal(None, u.Effect))
    Assert.DoesNotContain("onFailure", canonicalJson plan)

[<Fact>]
let ``the vocabulary 2 timer plans validate with pinned digests and carry their disposition`` () =
    for disposition, pinned in pinnedV2Digests do
        let plan = validate (preparedV2 disposition) (draftV2 disposition) |> ok ("validate " + disposition)
        Assert.Equal(pinned, digest plan)
        Assert.Equal(VocabularyVersion2, (view plan).Binding.VocabularyVersion)
        Assert.Contains(sprintf "\"onFailure\": \"%s\"" disposition, canonicalJson plan)
        let stop = (view plan).Workflow.Ports |> List.pick (fun p -> match p.Kind with TransitionEffectPort(c, d) -> Some(c.Key, d) | CurrentTimePort -> None)
        Assert.Equal("Stop", fst stop)
    let unresolved = validate (prepared ()) (draftV2 "unresolved") |> ok "unresolved"
    let linked = (view unresolved).Workflow.Unresolved |> List.filter (fun u -> u.Effect.IsSome)
    Assert.Equal<string list>([ "StopCompletionRefused" ], linked |> List.map _.Subject.Key)

[<Fact>]
let ``a decided Stop disposition is refused without the decision it cites`` () =
    for disposition in [ "remain"; "proceed" ] do
        let d = draftV2 disposition
        // Against the original blueprint the stop decision does not exist, so
        // the cohort does not require it and the plan cannot cite it.
        match validate (prepared ()) { d with Binding = { d.Binding with BlueprintDigest = blueprintDigest (blueprint ()); DecisionIds = [ "DEC-TIMER-STATES" ] } } with
        | Ok _ -> failwithf "%s accepted without an authorized decision" disposition
        | Error errors ->
            Assert.Contains(EffectDispositionWithoutDecision "port:transitionEffect/Stop", errors)
            Assert.Contains(UnauthorizedNodeReference("port:transitionEffect/Stop", stopDecision), errors)
        // Withholding the approval blocks preparation of the decided blueprint.
        let b = readBlueprint (read (sprintf "blueprint.v2-%s.json" disposition)) |> ok "blueprint"
        Assert.True(Result.isError (prepareCohort (manifest ()) b cohort approved))

[<Fact>]
let ``the same timer plan read as vocabulary 2 without dispositions is refused, not upgraded`` () =
    let d = draft ()
    match validate (prepared ()) { d with VocabularyVersion = VocabularyVersion2 } with
    | Ok _ -> failwith "a vocabulary 1 plan relabelled as vocabulary 2 was accepted without a disposition"
    | Error errors -> Assert.Contains(MissingEffectDisposition "port:transitionEffect/Stop", errors)
    let relabelled = (read "plan.json").Replace(VocabularyVersion1, VocabularyVersion2)
    match readPlan relabelled with
    | Ok _ -> failwith "the reader accepted a vocabulary 2 effect port without onFailure"
    | Error message -> Assert.Contains("missing JSON member onFailure", message)

// ---- compile-negative proof ----

let private reference = typeof<PlanDraft>.Assembly.Location

let private preamble =
    """
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionPlan
let draft : PlanDraft =
    { SchemaVersion = PlanSchemaVersion; VocabularyVersion = VocabularyVersion
      Binding = { SourceManifestDigest = ""; BlueprintDigest = ""; CohortId = ""; DecisionIds = [] }
      Operations = [] }
let consume (plan: ValidatedPlan) = digest plan
"""

[<Fact>]
let ``ordinary typed code cannot substitute unchecked input for a validated plan`` () =
    let probes =
        [ "positive control", preamble + "let f (p: PreparedCohort) = validate p draft |> Result.map consume", None
          "draft passed as validated plan", preamble + "let x = consume draft", Some "FS0001"
          "view passed as validated plan", preamble + "let f (p: ValidatedPlan) = consume (view p)", Some "FS0001"
          "validated plan constructed directly", preamble + "let f (v: ValidatedPlanView) = consume (ValidatedPlan v)", Some "FS1093"
          "prepared cohort constructed directly", preamble + "let f (v: PreparedCohortView) = validate (PreparedCohort v) draft", Some "FS1093"
          "fabricated validated marker", preamble + "let x = consume { draft with Validated = true }", Some "FS1129"
          "semantic name constructed without its grammar", preamble + "let n = SemanticName \"x\\\"; System.IO.File.Delete(\\\"a\\\")\"", Some "FS1093"
          "no file-writing operation exists", preamble + "let op = WriteFile(\"src/Program.fs\", \"code\")", Some "FS0039"
          "no raw code operation exists", preamble + "let op : OperationDraft = RawCode \"printfn \\\"hi\\\"\"", Some "FS0039"
          "an effect port takes a closed disposition", preamble + "let k = TransitionEffect(\"Stop\", Some EffectFailureDisposition.Remain)", None
          "a disposition is not free text", preamble + "let k = TransitionEffect(\"Stop\", Some \"stay\")", Some "FS0001"
          "no escape-hatch disposition exists", preamble + "let d = EffectFailureDisposition.Custom \"retry\"", Some "FS0039"
          "a current-time port cannot carry a disposition", preamble + "let k = CurrentTime(Some EffectFailureDisposition.Remain)", Some "FS0003" ]
    let failures = FsiProbe.check [ reference ] probes
    Assert.True(List.isEmpty failures, String.concat "\n\n" failures)

// ---- review findings: each was accepted by the first implementation ----

let private refusedWith expected (plan: PlanDraft) =
    match validate (prepared ()) plan with
    | Ok _ -> failwithf "accepted; expected %A" expected
    | Error errors -> Assert.True(List.contains expected errors, sprintf "expected %A in %A" expected errors)

let private traceOn keys nodes : TraceDraft = { RequirementKeys = keys; NodeIds = nodes }
let private assigned () = (preparedCohortView (prepared ())).AssignedRequirementKeys |> Set.toList

[<Fact>]
let ``the cohort node cannot be cited to make every requirement look justified`` () =
    let d = draft ()
    let gamed =
        { d with
            Operations =
                [ DefineWorkflow { Name = "Timer"; Trace = traceOn (assigned ()) [ "COH-TIMER" ] }
                  DefineState { Name = "Start"; Role = Initial; Trace = traceOn (assigned ()) [ "COH-TIMER" ] }
                  DefineState { Name = "Done"; Role = Terminal; Trace = traceOn (assigned ()) [ "COH-TIMER" ] }
                  DefineTransition { Command = "Finish"; From = "Start"; To = "Done"; Guards = []; Trace = traceOn (assigned ()) [ "COH-TIMER" ] } ] }
    refusedWith (CohortNodeCited("workflow:Timer", "COH-TIMER")) gamed

[<Fact>]
let ``coverage distinguishes constructed, unresolved-only and workflow-level requirements`` () =
    let plan = validate (prepared ()) (draft ()) |> ok "validate"
    let c = (view plan).Coverage
    let key (suffix: string) = c |> Map.toList |> List.find (fun (k: string, _) -> k.EndsWith suffix)
    Assert.Equal(WorkflowLevel, snd (key "#L145-L148"))
    Assert.Equal(WorkflowLevel, snd (key "#L367-L368"))
    match snd (key "#L116-L129") with
    | Constructed ops -> Assert.Contains("transition:Running/Pause", ops)
    | other -> failwithf "transitions requirement reported as %A" other
    let unresolvedOnly =
        let d = draft ()
        { d with Operations = d.Operations |> List.filter (function DeclarePort { Kind = TransitionEffect _ } -> false | _ -> true) }
    let c2 = (view (validate (prepared ()) unresolvedOnly |> ok "unresolved-only")).Coverage
    Assert.Equal(UnresolvedOnly [ "unresolved:StopCompletionRefused" ], c2 |> Map.toList |> List.find (fun (k: string, _) -> k.EndsWith "#L131-L144") |> snd)

[<Fact>]
let ``a capability requirement must cite an authorized decision`` () =
    let d = draft ()
    let transitionsKey = assigned () |> List.find (fun k -> k.EndsWith "#L116-L129")
    let bind nodes = BindCapability { Command = "Start"; Capability = "time-entry.timer.start"; Trace = traceOn [ transitionsKey ] nodes }
    refusedWith (CapabilityWithoutDecision "capability:Start/time-entry.timer.start") { d with Operations = d.Operations @ [ bind [ "VER-TIMER-TABLE" ] ] }
    let accepted = validate (prepared ()) { d with Operations = d.Operations @ [ bind [ "DEC-TIMER-STATES" ] ] } |> ok "capability"
    Assert.Equal(1, (view accepted).Workflow.Capabilities.Length)

[<Fact>]
let ``capability text cannot carry quotes, whitespace or operators`` () =
    let d = draft ()
    let key = assigned () |> List.head
    for raw in [ "\"); Process.Start(\"sh"; "a b"; "x/y"; "1start"; String('a', 129) ] do
        refusedWith (InvalidCapability(sprintf "capability:Start/%s" raw, raw))
            { d with Operations = d.Operations @ [ BindCapability { Command = "Start"; Capability = raw; Trace = traceOn [ key ] [ "DEC-TIMER-STATES" ] } ] }

[<Fact>]
let ``a manifest whose content no longer matches its digest is refused even if the blueprint agrees`` () =
    let m, b = manifest (), blueprint ()
    let target = List.head m.Requirements
    let altered = { target with Location = "L1" }
    let swap (r: SourceRequirement) = if r.Key = target.Key then altered else r
    let m2 = { m with Requirements = m.Requirements |> List.map swap }
    let b2 = { b with Requirements = b.Requirements |> List.map (fun r -> { r with Source = swap r.Source }) }
    let p = prepareCohort m2 b2 cohort approved |> ok "prepare tampered"
    let d = draft ()
    match validate p { d with Binding = { d.Binding with BlueprintDigest = blueprintDigest b2 } } with
    | Ok _ -> failwith "tampered manifest accepted"
    | Error errors -> Assert.Contains(errors, fun e -> match e with UnverifiedSourceManifest _ -> true | _ -> false)

[<Fact>]
let ``a state whose only way out is a self-loop is a dead end`` () =
    let d = draft ()
    let key = assigned () |> List.find (fun k -> k.EndsWith "#L116-L129")
    let extra =
        [ DefineState { Name = "Stuck"; Role = Ordinary; Trace = traceOn [ key ] [ "DEC-TIMER-STATES" ] }
          DefineTransition { Command = "Jam"; From = "Paused"; To = "Stuck"; Guards = []; Trace = traceOn [ key ] [ "DEC-TIMER-STATES" ] }
          DefineTransition { Command = "Poke"; From = "Stuck"; To = "Stuck"; Guards = []; Trace = traceOn [ key ] [ "DEC-TIMER-STATES" ] } ]
    refusedWith (DeadEndState "Stuck") { d with Operations = d.Operations @ extra }

[<Fact>]
let ``duplicate guards on one transition are refused rather than merged`` () =
    let d = draft ()
    let twice : GuardDraft list = [ { Name = "has project"; Predicate = ExtensionPredicate }; { Name = "HasProject"; Predicate = ExtensionPredicate } ]
    let ops = d.Operations |> List.map (function DefineTransition t when t.Command = "Start" -> DefineTransition { t with Guards = twice } | op -> op)
    refusedWith (DuplicateDefinition "transition:NoTimer/Start/guard:HasProject") { d with Operations = ops }

[<Fact>]
let ``an unresolved question has a bounded length`` () =
    let d = draft ()
    let ops = d.Operations |> List.map (function DeclareUnresolved u -> DeclareUnresolved { u with Question = String('?', 1001) } | op -> op)
    refusedWith (QuestionTooLong("unresolved:StopCompletionRefused", 1001)) { d with Operations = ops }

[<Fact>]
let ``structural validation stays fast on a very long hostile chain`` () =
    let d = draft ()
    let key = assigned () |> List.find (fun k -> k.EndsWith "#L116-L129")
    let t = traceOn [ key ] [ "DEC-TIMER-STATES" ]
    let n = 20000
    let chain =
        [ for i in 0 .. n - 1 do
            DefineState { Name = sprintf "S%d" i; Role = (if i = n - 1 then Terminal else Ordinary); Trace = t }
            if i < n - 1 then DefineTransition { Command = "Next"; From = sprintf "S%d" i; To = sprintf "S%d" (i + 1); Guards = []; Trace = t } ]
          @ [ DefineTransition { Command = "Enter"; From = "Paused"; To = "S0"; Guards = []; Trace = t } ]
    let work = Threading.Tasks.Task.Run(fun () -> validate (prepared ()) { d with Operations = d.Operations @ chain })
    Assert.True(work.Wait(TimeSpan.FromSeconds 30.0), "validation did not finish")
    Assert.True(Result.isOk work.Result, sprintf "%A" (work.Result |> Result.mapError (List.truncate 3)))

[<Fact>]
let ``a validated plan reports the exact cohort it is bound to`` () =
    let plan = validate (prepared ()) (draft ()) |> ok "validate"
    Assert.True(isBoundTo (prepared ()) plan)
    let b = blueprint ()
    let other = prepareCohort (manifest ()) { b with Nodes = b.Nodes |> List.map (fun n -> if n.Id = "VER-TIMER-SESSION" then { n with Justification = Some "x" } else n) } cohort approved |> ok "other"
    Assert.False(isBoundTo other plan)
