/// ORDO-PATTERN-001: deterministic pattern recognition with early
/// delegation. The acceptance tests of the registry contract
/// (docs/architecture/pattern-registry.md): determinism, order
/// independence, ambiguity detection, immediate delegation, no silent
/// defaults, complete provenance, pinned versions, safe failure on unknown
/// versions, and no silent change under registry updates. Then the
/// adversarial cases: dependencies, open questions, invalid sources, the
/// single generator run, and the completion contract.
module Ordo.Construction.Tests.PatternRecognitionTests

open System
open System.IO
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionDecisions
open Ordo.Core.ConstructionDefaultPlan
open Ordo.Core.PatternRegistry
open Ordo.Core.PatternRecognition
open Ordo.Core.PatternRecognitionWire
open Ordo.Construction.Tests.Fixtures

let private read name =
    File.ReadAllText(Path.Combine(repositoryRoot, "tests", "fixtures", "construction", "time-entry-timer", name))

let private manifest () = readManifest (read "source-manifest.json") |> ok "manifest"
let private blueprint () = readBlueprint (read "blueprint.json") |> ok "blueprint"
let private records () = readDecisionRecords (read "construction-decisions.json") |> ok "decisions"

let private policy =
    { AllowedPaths = [ "src/Timer/Extensions/**" ]; ArchitectureConstraints = [ "ARCH-FUNCTIONAL-CORE" ]; EscalateOpenQuestions = false }

let private inputOf (b: Blueprint) (r: ApprovedDecisionRecords option) =
    { Manifest = manifest ()
      Blueprint = b
      CohortId = "COH-TIMER"
      AuthorizedDecisionIds = Set.singleton "DEC-TIMER-STATES"
      Records = r |> Option.map (fun r -> { r with BlueprintDigest = blueprintDigest b })
      Policy = policy }

let private timerInput () = inputOf (blueprint ()) (Some(records ()))

let private run input = recognize BuiltIn.registry None input |> ok "recognize"

let private keyEnding (suffix: string) (report: RecognitionReport) =
    report.Requirements |> List.find (fun r -> r.Key.EndsWith suffix)

let private outcomeOf suffix report = (keyEnding suffix report).Outcome

/// Adds a node, linking every requirement it owns back to it.
let private withNode (node: ConstructionNode) (b: Blueprint) =
    { b with
        Nodes = b.Nodes @ [ node ]
        Requirements =
            b.Requirements
            |> List.map (fun r ->
                if List.contains r.Source.Key node.RequirementKeys then { r with NodeIds = r.NodeIds @ [ node.Id ] } else r) }

let private keyOf (suffix: string) (b: Blueprint) = b.Requirements |> List.map _.Source.Key |> List.find (fun k -> k.EndsWith suffix)

// ---------------------------------------------------------------------------
// Baseline on the timer cohort.
// ---------------------------------------------------------------------------

[<Fact>]
let ``the timer cohort gets one outcome per requirement from structured facts only`` () =
    let report = run (timerInput ())
    Assert.Equal(6, report.Requirements.Length)
    Assert.Empty report.Excluded
    // States and transitions the approved record fixes.
    for suffix in [ "L116-L129"; "md#L104"; "L367-L368" ] do
        Assert.Equal(Constructible([ BuiltIn.stateMachineContract.Ref ], []), outcomeOf suffix report)
    // The Stop effect's failure disposition is not decided: no default.
    match outcomeOf "L131-L144" report with
    | DecisionRequired(gaps, generatedBy) ->
        Assert.Equal<DecisionGap list>([ EffectFailureUndecided("IFC-TIMER-PORTS", "Stop") ], gaps)
        Assert.Equal<PatternRef list>([], generatedBy)
    | other -> failwithf "expected DecisionRequired, got %A" other
    // Realized only through a generated port: interface generated, behavior delegated.
    Assert.Equal(Delegate([ "ITimerPorts.CurrentTime" ], []), outcomeOf "L105-L107" report)
    // Owned by the approved decision, but no record constructs it (session scope).
    Assert.Equal(Delegate([], []), outcomeOf "L145-L148" report)
    Assert.True report.GenerationWorthwhile
    Assert.False report.CompleteConstruction

[<Fact>]
let ``recognition costs one claim evaluation per requirement and selected pattern`` () =
    let report = run (timerInput ())
    Assert.Equal(3, report.Measurements.PatternsSelected)
    Assert.Equal(report.Measurements.Evaluated * 3, report.Measurements.ClaimEvaluations)

// ---------------------------------------------------------------------------
// Acceptance 1-2: determinism and order independence.
// ---------------------------------------------------------------------------

[<Fact>]
let ``identical inputs produce byte-identical reports and packets`` () =
    let a = run (timerInput ())
    let b = run (timerInput ())
    Assert.Equal(encodeReport a, encodeReport b)
    Assert.Equal<string list>(a.Packets |> List.map encodePacket, b.Packets |> List.map encodePacket)

[<Fact>]
let ``registry, record, node and requirement order do not change the result`` () =
    let expected = run (timerInput ()) |> encodeReport
    let b = blueprint ()
    let shuffled = { b with Nodes = List.rev b.Nodes; Requirements = List.rev b.Requirements }
    let r = records ()
    let reversedRecords = { r with Records = List.rev r.Records }
    let reversedRegistry = Registry.create BuiltIn.RegistryVersion (List.rev BuiltIn.definitions) |> ok "registry"
    let actual = recognize reversedRegistry None (inputOf shuffled (Some reversedRecords)) |> ok "recognize"
    Assert.Equal(expected, encodeReport actual)

// ---------------------------------------------------------------------------
// Acceptance 3: ambiguous overlapping patterns.
// ---------------------------------------------------------------------------

let private competing =
    let contract = { BuiltIn.stateMachineContract with Ref = { Id = "test.competing-state-machine"; Version = 1 }; Title = "Competing" }
    { Contract = contract
      Claim = fun cohort facts -> BuiltIn.stateMachine.Claim cohort facts |> Option.map (fun c -> { c with Pattern = contract.Ref }) }

[<Fact>]
let ``two patterns constructing the same requirement differently are detected, never resolved by order`` () =
    let registry = Registry.create "test/ambiguous" (competing :: BuiltIn.definitions) |> ok "registry"
    let report = recognize registry None (timerInput ()) |> ok "recognize"
    let expectedGap = PatternSelectionRequired(List.sort [ competing.Contract.Ref; BuiltIn.stateMachineContract.Ref ])
    for suffix in [ "L116-L129"; "md#L104"; "L367-L368" ] do
        match outcomeOf suffix report with
        | DecisionRequired(gaps, _) -> Assert.Contains(expectedGap, gaps)
        | other -> failwithf "expected DecisionRequired for %s, got %A" suffix other
    Assert.Equal(0, report.Measurements.Constructible)
    Assert.Contains(report.Decisions, fun d -> d.Gap = expectedGap)

[<Fact>]
let ``a duplicate pattern identity is refused when the registry is built`` () =
    match Registry.create "dup" [ BuiltIn.stateMachine; BuiltIn.stateMachine ] with
    | Error errors -> Assert.Contains(DuplicatePattern BuiltIn.stateMachineContract.Ref, errors)
    | Ok _ -> failwith "expected refusal"

// ---------------------------------------------------------------------------
// Acceptance 4: unsupported work delegates immediately (early exit).
// ---------------------------------------------------------------------------

[<Fact>]
let ``without approved structured content nothing is generated and everything delegates at once`` () =
    let report = run (inputOf (blueprint ()) None)
    Assert.All(report.Requirements, fun r ->
        match r.Outcome with
        | Delegate([], []) -> ()
        | other -> failwithf "%s: expected bare Delegate, got %A" r.Key other)
    Assert.False report.GenerationWorthwhile
    Assert.NotEmpty report.Packets
    Assert.Equal<string list>(
        report.Requirements |> List.map _.Key |> List.sort,
        report.Packets |> List.collect _.RequirementKeys |> List.sort)

[<Fact>]
let ``a delegation packet carries the approved decisions, interfaces, oracles and host boundaries`` () =
    let report = run (timerInput ())
    let p = report.Packets |> List.find (fun p -> p.RequirementKeys |> List.exists (fun k -> k.EndsWith "L105-L107"))
    Assert.Contains("ITimerPorts.CurrentTime", p.Interfaces)
    Assert.Contains(p.VerificationObligations, fun n -> n.NodeId = "VER-TIMER-COMPLETION")
    Assert.Contains(p.Contracts, fun n -> n.NodeId = "IFC-TIMER-PORTS")
    Assert.Equal<string list>([ "generated/Timer/**" ], p.ForbiddenPaths)
    Assert.Equal<string list>(policy.AllowedPaths, p.AllowedPaths)
    Assert.Equal<string list>(policy.ArchitectureConstraints, p.ArchitectureConstraints)
    let session = report.Packets |> List.find (fun p -> p.RequirementKeys |> List.exists (fun k -> k.EndsWith "L145-L148"))
    Assert.Contains(session.ApprovedDecisions, fun n -> n.NodeId = "DEC-TIMER-STATES")
    // Only what the agent needs: no generated plan, no other requirement.
    Assert.DoesNotContain(session.RequirementKeys, fun k -> k.EndsWith "L116-L129")

// ---------------------------------------------------------------------------
// Acceptance 5: missing decisions never become silent defaults.
// ---------------------------------------------------------------------------

[<Fact>]
let ``an undecided effect failure is an explicit decision obligation, not a default`` () =
    let report = run (timerInput ())
    let d = report.Decisions |> List.exactlyOne
    Assert.Equal(EffectFailureUndecided("IFC-TIMER-PORTS", "Stop"), d.Gap)
    Assert.Contains("remain", d.Question)
    Assert.Contains("proceed", d.Question)
    Assert.DoesNotContain(report.Packets |> List.collect _.RequirementKeys, fun k -> k.EndsWith "L131-L144")

[<Fact>]
let ``an unapproved decision makes its requirements await a decision instead of refusing the cohort`` () =
    let input = { timerInput () with AuthorizedDecisionIds = Set.empty }
    let report = run input
    for suffix in [ "L116-L129"; "md#L104"; "L367-L368"; "L145-L148" ] do
        match outcomeOf suffix report with
        | DecisionRequired(gaps, _) -> Assert.Contains(DecisionNotAuthorized "DEC-TIMER-STATES", gaps)
        | other -> failwithf "expected DecisionRequired for %s, got %A" suffix other
    // The port-only requirement does not depend on the unapproved decision.
    Assert.Equal(Delegate([ "ITimerPorts.CurrentTime" ], []), outcomeOf "L105-L107" report)

[<Fact>]
let ``a decided disposition recorded on a non-decision node is blocked, never applied`` () =
    let r = records ()
    let decided =
        { r with
            Records =
                r.Records
                |> List.map (fun x ->
                    match x.Content with
                    | PortContract ports ->
                        { x with
                            Content =
                                PortContract(ports |> List.map (function
                                    | RecordedTransitionEffect(c, None, k) ->
                                        RecordedTransitionEffect(c, Some Ordo.Core.ConstructionPlan.EffectFailureDisposition.Remain, k)
                                    | p -> p)) }
                    | StateModel _ -> x) }
    let report = run (inputOf (blueprint ()) (Some decided))
    match outcomeOf "L131-L144" report with
    | Blocked [ UnusableContent(p, defects) ] ->
        Assert.Equal(BuiltIn.transitionEffectContract.Ref, p)
        Assert.Contains(defects, fun d -> d.Contains "not a decision")
    | other -> failwithf "expected Blocked, got %A" other

// ---------------------------------------------------------------------------
// Acceptance 6: requirement provenance stays complete.
// ---------------------------------------------------------------------------

let private assertProvenance (input: RecognitionInput) (report: RecognitionReport) =
    let cohort = input.Blueprint.Nodes |> List.find (fun n -> n.Id = input.CohortId)
    let reported = (report.Requirements |> List.map _.Key) @ (report.Excluded |> List.map _.Key)
    Assert.Equal<string list>(cohort.RequirementKeys |> List.distinct |> List.sort, reported |> List.sort)
    Assert.Equal(reported.Length, reported |> List.distinct |> List.length)
    for p in report.Packets do
        Assert.Equal<string list>(p.RequirementKeys, p.Sources |> List.map _.Key)
        for s in p.Sources do
            let m = input.Manifest.Requirements |> List.find (fun x -> x.Key = s.Key)
            Assert.Equal(m.ContentDigest, s.ContentDigest)
            Assert.Equal(m.Revision, s.Revision)
    for d in report.Decisions do
        Assert.NotEmpty d.RequirementKeys

[<Fact>]
let ``every assigned requirement appears exactly once and every packet cites its pinned sources`` () =
    let input = timerInput ()
    assertProvenance input (run input)
    let bare = inputOf (blueprint ()) None
    assertProvenance bare (run bare)

// ---------------------------------------------------------------------------
// Acceptance 7-9: pinned versions, unknown versions, registry updates.
// ---------------------------------------------------------------------------

[<Fact>]
let ``the report pins every selected pattern by contract digest and the pin reproduces it`` () =
    let report = run (timerInput ())
    Assert.Equal(BuiltIn.RegistryVersion, report.Pin.RegistryVersion)
    Assert.Equal(3, report.Pin.Patterns.Length)
    Assert.All(report.Pin.Patterns, fun (_, digest) -> Assert.StartsWith("sha256:", digest))
    let pin = readPin (encodePin report.Pin) |> ok "pin"
    let again = recognize BuiltIn.registry (Some pin) (timerInput ()) |> ok "pinned"
    Assert.Equal(encodeReport report, encodeReport again)

[<Fact>]
let ``an unknown pinned version fails safely before anything is recognized`` () =
    let report = run (timerInput ())
    let pin = { report.Pin with Patterns = report.Pin.Patterns |> List.map (fun (r, d) -> (if r.Id = "ordo.state-machine" then { r with Version = 99 } else r), d) }
    match recognize BuiltIn.registry (Some pin) (timerInput ()) with
    | Error [ RegistryPinRefused [ UnknownPatternVersion r ] ] -> Assert.Equal(({ PatternRef.Id = "ordo.state-machine"; Version = 99 }: PatternRef), r)
    | other -> failwithf "expected UnknownPatternVersion, got %A" other

[<Fact>]
let ``a changed contract under the same version is refused for an approved pin`` () =
    let approved = (run (timerInput ())).Pin
    let edited = { BuiltIn.stateMachine with Contract = { BuiltIn.stateMachineContract with Invariants = [ "silently weakened" ] } }
    let updated = Registry.create BuiltIn.RegistryVersion [ edited; BuiltIn.transitionEffect; BuiltIn.currentTime ] |> ok "registry"
    match recognize updated (Some approved) (timerInput ()) with
    | Error [ RegistryPinRefused [ PatternContractChanged(r, _, _) ] ] -> Assert.Equal(BuiltIn.stateMachineContract.Ref, r)
    | other -> failwithf "expected PatternContractChanged, got %A" other

[<Fact>]
let ``a newer pattern version or a new pattern does not join previously approved construction`` () =
    let approved = run (timerInput ())
    let v2 =
        { Contract = { BuiltIn.stateMachineContract with Ref = { Id = "ordo.state-machine"; Version = 2 }; Unsupported = [] }
          Claim = fun _ _ -> None }
    let updated = Registry.create "ordo.patterns/2" (competing :: v2 :: BuiltIn.definitions) |> ok "registry"
    let pinned = recognize updated (Some approved.Pin) (timerInput ()) |> ok "pinned"
    Assert.Equal(encodeReport approved, encodeReport pinned)
    // Unpinned, the latest versions are selected and the result may differ, visibly.
    let unpinned = recognize updated None (timerInput ()) |> ok "unpinned"
    Assert.Contains(unpinned.Pin.Patterns, fun (r, _) -> r = v2.Contract.Ref)
    Assert.NotEqual<string>(encodeReport approved, encodeReport unpinned)

// ---------------------------------------------------------------------------
// Adversarial: dependencies, open questions, invalid input.
// ---------------------------------------------------------------------------

[<Fact>]
let ``generated behavior never rests on an undecided prerequisite`` () =
    let b = blueprint ()
    let dependent =
        { b with Nodes = b.Nodes |> List.map (fun n -> if n.Id = "DEC-TIMER-STATES" then { n with DependsOn = [ "IFC-TIMER-PORTS" ] } else n) }
    let report = run (inputOf dependent (Some(records ())))
    match outcomeOf "L116-L129" report with
    | Blocked [ AwaitingPrerequisites keys ] -> Assert.Contains(keyOf "L131-L144" b, keys)
    | other -> failwithf "expected Blocked awaiting prerequisites, got %A" other
    Assert.False report.CompleteConstruction

[<Fact>]
let ``a delegated prerequisite leaves generated work constructible and names the interface dependency`` () =
    let b = blueprint ()
    let contract =
        { Id = "CON-SESSION"; Kind = Contract; RequirementKeys = [ keyOf "L145-L148" b ]; DependsOn = []; Justification = Some "Session scope contract." }
    let b2 =
        b |> withNode contract
        |> fun x -> { x with Nodes = x.Nodes |> List.map (fun n -> if n.Id = "DEC-TIMER-STATES" then { n with DependsOn = [ "CON-SESSION" ] } else n) }
    let report = run (inputOf b2 (Some(records ())))
    match outcomeOf "L116-L129" report with
    | Constructible(_, delegated) -> Assert.Contains(keyOf "L145-L148" b, delegated)
    | other -> failwithf "expected Constructible with a delegated prerequisite, got %A" other
    Assert.False report.CompleteConstruction

[<Fact>]
let ``an open conflict is a decision obligation, and under policy an escalation the agent may not answer`` () =
    let b = blueprint ()
    let conflict =
        { Id = "CONFLICT-SESSION-SCOPE"; Kind = Conflict; RequirementKeys = [ keyOf "L145-L148" b ]; DependsOn = []
          Justification = Some "Is the session scope per device or per person?" }
    let b2 = b |> withNode conflict
    let strict = run (inputOf b2 (Some(records ())))
    Assert.Equal(DecisionRequired([ ConflictUnresolved "CONFLICT-SESSION-SCOPE" ], []), outcomeOf "L145-L148" strict)
    Assert.DoesNotContain(strict.Packets |> List.collect _.RequirementKeys, fun k -> k.EndsWith "L145-L148")
    let escalating = run { inputOf b2 (Some(records ())) with Policy = { policy with EscalateOpenQuestions = true } }
    Assert.Equal(DecisionRequired([ ConflictUnresolved "CONFLICT-SESSION-SCOPE" ], []), outcomeOf "L145-L148" escalating)
    let p = escalating.Packets |> List.find (fun p -> p.RequirementKeys |> List.exists (fun k -> k.EndsWith "L145-L148"))
    Assert.Contains(keyOf "L145-L148" b, p.EscalationRequired)
    Assert.Contains(("conflict:CONFLICT-SESSION-SCOPE", "Is the session scope per device or per person?"), p.OpenQuestions)

[<Fact>]
let ``similar syntax with different semantics: a transition record on a non-decision node is not constructed`` () =
    let r = records ()
    let moved = { r with Records = r.Records |> List.map (fun x -> match x.Content with StateModel _ -> { x with NodeId = "IFC-TIMER-PORTS" } | _ -> { x with NodeId = "DEC-TIMER-STATES" }) }
    let report = run (inputOf (blueprint ()) (Some moved))
    Assert.Equal(0, report.Measurements.Constructible)
    Assert.Contains(report.Requirements, fun x -> match x.Outcome with Blocked [ UnusableContent _ ] -> true | _ -> false)

[<Fact>]
let ``a structurally invalid source blocks every assigned requirement without throwing`` () =
    let b = blueprint ()
    let broken = { b with Requirements = b.Requirements |> List.filter (fun r -> not (r.Source.Key.EndsWith "L104")) }
    let report = run (inputOf broken None)
    Assert.Equal(6, report.Requirements.Length)
    Assert.All(report.Requirements, fun r -> match r.Outcome with Blocked [ InvalidSource(_ :: _) ] -> () | o -> failwithf "%A" o)
    Assert.False report.GenerationWorthwhile

[<Fact>]
let ``records bound to another blueprint are refused outright`` () =
    let input = { timerInput () with Records = Some { records () with BlueprintDigest = "sha256:" + String('0', 64) } }
    match recognize BuiltIn.registry None input with
    | Error [ RecordsBoundToOtherBlueprint _ ] -> ()
    | other -> failwithf "expected RecordsBoundToOtherBlueprint, got %A" other

// ---------------------------------------------------------------------------
// Recognition and the generator agree; one run, no retry.
// ---------------------------------------------------------------------------

[<Fact>]
let ``nothing recognition calls constructible is an open obligation of the generated plan`` () =
    let report = run (timerInput ())
    let plan = generateDefaultPlan (prepared timer) (records ()) ConstructionVocabulary.Version2 |> ok "default plan"
    let constructible = report.Requirements |> List.filter (fun r -> match r.Outcome with Constructible _ -> true | _ -> false) |> List.map _.Key |> Set.ofList
    let obligated = plan.Obligations |> List.collect _.RequirementKeys |> Set.ofList
    Assert.Empty(Set.intersect constructible obligated)
    // And every assigned requirement the generator leaves open is not
    // constructible here. (It also reports a related conflict outside the cohort.)
    let assigned = report.Requirements |> List.map _.Key |> Set.ofList
    Assert.True(Set.isSubset (Set.intersect obligated assigned) (Set.difference assigned constructible))

[<Fact>]
let ``a generator refusal blocks only the requirements that relied on generated content`` () =
    let report = run (timerInput ())
    let after = applyGeneration [ "GeneratedPlanInvalid" ] report
    for suffix in [ "L116-L129"; "md#L104"; "L367-L368" ] do
        Assert.Equal(Blocked [ GenerationRefused [ "GeneratedPlanInvalid" ] ], outcomeOf suffix after)
    Assert.Equal(outcomeOf "L145-L148" report, outcomeOf "L145-L148" after)
    Assert.Equal(report.Packets.Length, after.Packets.Length)
    Assert.False after.GenerationWorthwhile
    Assert.Equal(applyGeneration [] report |> encodeReport, encodeReport report)

// ---------------------------------------------------------------------------
// The completion contract Praxis checks before applying anything.
// ---------------------------------------------------------------------------

let private sessionPacket () =
    let report = run (timerInput ())
    report.Packets |> List.find (fun p -> p.RequirementKeys |> List.exists (fun k -> k.EndsWith "L145-L148"))

let private completionFor (p: DelegationPacket) =
    { SchemaVersion = CompletionSchemaVersion
      PacketId = p.Id
      PacketDigest = packetDigest p
      Artifacts = [ { Path = "src/Timer/Extensions/Session.fs"; ContentDigest = "sha256:" + String('a', 64) } ]
      Dispositions = p.RequirementKeys |> List.map (fun k -> k, CompletionDisposition.Implemented)
      VerificationEvidence = p.VerificationObligations |> List.map (fun v -> v.NodeId, "dokimos:verify/run-1") }

[<Fact>]
let ``a complete, bounded completion validates`` () =
    let p = sessionPacket ()
    Assert.Empty(validateCompletion (packetDigest p) p (completionFor p))

[<Fact>]
let ``a completion that writes generated files, escapes its paths or skips a requirement is refused`` () =
    let p = sessionPacket ()
    let c = completionFor p
    let check completion = validateCompletion (packetDigest p) p completion
    Assert.Contains(ArtifactInForbiddenPath "generated/Timer/Timer.Model.g.fs",
                    check { c with Artifacts = c.Artifacts @ [ { Path = "generated/Timer/Timer.Model.g.fs"; ContentDigest = "x" } ] })
    Assert.Contains(ArtifactOutsideAllowedPaths "src/Other.fs", check { c with Artifacts = [ { Path = "src/Other.fs"; ContentDigest = "x" } ] })
    Assert.Contains(UnsafePath "src/Timer/Extensions/../../../etc/passwd",
                    check { c with Artifacts = [ { Path = "src/Timer/Extensions/../../../etc/passwd"; ContentDigest = "x" } ] })
    Assert.Contains(MissingDisposition p.RequirementKeys.Head, check { c with Dispositions = [] })
    Assert.Contains(PacketMismatch(p.Id, packetDigest p), check { c with PacketDigest = "sha256:stale" })
    Assert.Contains(MissingVerificationEvidence "VER-TIMER-SESSION", check { c with VerificationEvidence = [] })
    Assert.Contains(UnknownRequirement "invented", check { c with Dispositions = c.Dispositions @ [ "invented", CompletionDisposition.Implemented ] })

[<Fact>]
let ``reporting an escalation-required requirement as implemented is refused`` () =
    let b = blueprint ()
    let conflict = { Id = "CONFLICT-S"; Kind = Conflict; RequirementKeys = [ keyOf "L145-L148" b ]; DependsOn = []; Justification = Some "Open?" }
    let report = run { inputOf (withNode conflict b) (Some(records ())) with Policy = { policy with EscalateOpenQuestions = true } }
    let p = report.Packets |> List.find (fun p -> not (List.isEmpty p.EscalationRequired))
    let c = completionFor p
    Assert.Contains(ImplementedDespiteOpenQuestion(keyOf "L145-L148" b), validateCompletion (packetDigest p) p c)
    let escalated = { c with Dispositions = c.Dispositions |> List.map (fun (k, _) -> k, CompletionDisposition.Escalated "Per device or per person?") }
    Assert.Empty(validateCompletion (packetDigest p) p escalated)

[<Fact>]
let ``the completion reader is strict`` () =
    let p = sessionPacket ()
    let json =
        sprintf """{"schemaVersion":"%s","packetId":"%s","packetDigest":"%s","artifacts":[{"path":"src/Timer/Extensions/S.fs","contentDigest":"d"}],"dispositions":[{"requirementKey":"%s","status":"escalated","reason":"which scope?"}],"verificationEvidence":[]}"""
            CompletionSchemaVersion p.Id (packetDigest p) p.RequirementKeys.Head
    let c = readCompletion json |> ok "completion"
    Assert.Equal(CompletionDisposition.Escalated "which scope?", snd c.Dispositions.Head)
    Assert.True(readCompletion (json.Replace("\"verificationEvidence\":[]", "\"verificationEvidence\":[],\"extra\":1")) |> Result.isError)
    Assert.True(readCompletion (json.Replace("\"escalated\",\"reason\":\"which scope?\"", "\"escalated\"")) |> Result.isError)

[<Theory>]
[<InlineData("src/**", "src/a/b.fs", true)>]
[<InlineData("src/*.fs", "src/a.fs", true)>]
[<InlineData("src/*.fs", "src/a/b.fs", false)>]
[<InlineData("generated/Timer/**", "generated/Timer/Timer.Model.g.fs", true)>]
[<InlineData("generated/Timer/**", "generated/TimerX/a.fs", false)>]
let ``path globs match whole segments`` (pattern: string, path: string, expected: bool) =
    Assert.Equal(expected, globMatch pattern path)
