/// CONSTRUCT-008: Ordo generates the default construction plan from the
/// prepared cohort, approved construction decision records and the
/// vocabulary only. The fixture is the time-entry timer cohort of pilot 01
/// (same manifest and blueprint) plus `construction-decisions.json`, the
/// structured content of DEC-TIMER-STATES and IFC-TIMER-PORTS.
module Ordo.Tests.ConstructionDefaultPlanTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open Xunit
open Ordo.Core
open Ordo.Core.Json
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionPlanWire
open Ordo.Core.ConstructionDecisions
open Ordo.Core.ConstructionDefaultPlan

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

let private sha256Of (text: string) =
    "sha256:" + (text |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString).ToLowerInvariant()

let private manifest () = readManifest (read "source-manifest.json") |> ok "manifest"
let private blueprint () = readBlueprint (read "blueprint.json") |> ok "blueprint"
let private cohort = "COH-TIMER"
/// Stands in for the external authority that approves DEC-TIMER-STATES. A
/// test input, not an approval recorded anywhere.
let private approved = Set.singleton "DEC-TIMER-STATES"
let private prepared () = prepareCohort (manifest ()) (blueprint ()) cohort approved |> ok "prepare"
let private records () = readDecisionRecords (read "construction-decisions.json") |> ok "decisions"
let private v2 = ConstructionVocabulary.Version2
let private v1 = ConstructionVocabulary.Version1
let private generate () = generateDefaultPlan (prepared ()) (records ()) v2 |> ok "generate"

let private keyA = "kemiller2002/time-entry-state-machine/docs/architecture/business-activity-ledger-state-model.md#L116-L129"
let private keyStop = "kemiller2002/time-entry-state-machine/docs/architecture/business-activity-ledger-state-model.md#L131-L144"
let private keySession = "kemiller2002/time-entry-state-machine/docs/architecture/business-activity-ledger-state-model.md#L145-L148"
let private keySingle = "kemiller2002/time-entry-state-machine/requirements-reconstruction/fsharp-business-requirements-beyond-csharp.md#L104"
let private keyPaused = "kemiller2002/time-entry-state-machine/requirements-reconstruction/fsharp-business-requirements-beyond-csharp.md#L105-L107"
let private keyShort = "kemiller2002/time-entry-state-machine/requirements-reconstruction/fsharp-business-requirements-beyond-csharp.md#L113-L114"
let private keyRejection = "kemiller2002/time-entry-state-machine/effort-experiment/f-sharp-state/REQUIREMENTS.md#L367-L368"

/// Changing canonicalization, generation or obligation wording changes these
/// values; that requires a generator version change, not an updated constant.
let private pinnedDecisionsDigest = "sha256:3b9d190488bc80f045578f91dbd059beb393340f34dc1ba2802f5d88a198d0d3"
let private pinnedPlanDigest = "sha256:9747faade3fbea4bb9ded5e1ed83ef457d93df4b4980183df581db9e2b964d63"
let private pinnedDefaultPlanDigest = "sha256:6bc5876224be505ce86def6d50d104b12be99db8f8777978679a55ecfeedffad"
let private pinnedObligationIds =
    [ "OBL-COH-TIMER-conflict-f569e9ac9d77"
      "OBL-COH-TIMER-extensionBehavior-03835228233e"
      "OBL-COH-TIMER-extensionBehavior-f2efc8429795"
      "OBL-COH-TIMER-uncoveredRequirement-fce50292ef11"
      "OBL-COH-TIMER-undecidedEffectFailure-9843158a9897" ]

let private refusedWith (expected: DefaultPlanRefusal) result =
    match result with
    | Ok _ -> failwithf "expected refusal %A, got a default plan" expected
    | Error refusals -> Assert.Contains(expected, (refusals: DefaultPlanRefusal list))

let private withRecord nodeId (f: DecisionContent -> DecisionContent) (r: ApprovedDecisionRecords) =
    { r with Records = r.Records |> List.map (fun x -> if x.NodeId = nodeId then { x with Content = f x.Content } else x) }

let private withModel f = withRecord "DEC-TIMER-STATES" (function StateModel m -> StateModel(f m) | other -> other)
let private withPorts f = withRecord "IFC-TIMER-PORTS" (function PortContract ports -> PortContract(f ports) | other -> other)

let private operations (plan: DefaultPlan) = plan.Draft.Operations

// ---------------------------------------------------------------------------
// The timer fixture.
// ---------------------------------------------------------------------------

[<Fact>]
let ``the fixture decision records are the canonical encoding and hash to their digest`` () =
    let text = read "construction-decisions.json"
    let r = records ()
    Assert.Equal(text, encodeDecisionRecords r)
    Assert.Equal(pinnedDecisionsDigest, ConstructionDecisions.digest r)
    Assert.Equal(pinnedDecisionsDigest, sha256Of text)
    Assert.Equal(blueprintDigest (blueprint ()), r.BlueprintDigest)

[<Fact>]
let ``the timer default plan validates with the pinned digest and an Ordo-computed binding`` () =
    let p = generate ()
    let revalidated = validate (prepared ()) p.Draft |> ok "validate"
    Assert.Equal(pinnedPlanDigest, ConstructionPlan.digest revalidated)
    Assert.Equal(pinnedPlanDigest, p.PlanDigest)
    Assert.True(isBoundTo (prepared ()) revalidated)
    Assert.Equal(VocabularyVersion2, p.Draft.VocabularyVersion)
    Assert.Equal(blueprintDigest (blueprint ()), p.Draft.Binding.BlueprintDigest)
    Assert.Equal((manifest ()).Digest, p.Draft.Binding.SourceManifestDigest)
    Assert.Equal<string list>([ "DEC-TIMER-STATES" ], p.Draft.Binding.DecisionIds)
    // The wire round trip keeps the plan.
    let reread = readPlan (encodePlan p.Draft) |> ok "reread"
    Assert.Equal(pinnedPlanDigest, ConstructionPlan.digest (validate (prepared ()) reread |> ok "revalidate"))

[<Fact>]
let ``the timer default plan has the three states and five transitions of the decision record`` () =
    let w = (view (generate ()).ValidatedPlan).Workflow
    Assert.Equal("Timer", w.Name.Key)
    Assert.Equal<string list>([ "NoTimer"; "Paused"; "Running" ], w.States |> List.map _.Name.Key)
    Assert.Equal(Initial, w.States.Head.Role)
    Assert.Equal<(string * string * string) list>(
        [ "NoTimer", "Start", "Running"
          "Paused", "Resume", "Running"
          "Paused", "Stop", "NoTimer"
          "Running", "Pause", "Paused"
          "Running", "Stop", "NoTimer" ],
        w.Transitions |> List.map (fun t -> t.From.Key, t.Command.Key, t.To.Key))
    Assert.True(w.Transitions |> List.forall (fun t -> List.isEmpty t.Guards))
    Assert.Empty(w.Invariants)
    Assert.Empty(w.Capabilities)
    Assert.Equal<PortKind list>(
        [ CurrentTimePort; TransitionEffectPort((SemanticName.create "Stop" |> ok "name"), Some EffectFailureDisposition.Unresolved) ],
        w.Ports |> List.map _.Kind)

[<Fact>]
let ``the timer obligations are typed, pinned and carried by the plan where it can carry them`` () =
    let p = generate ()
    Assert.Equal<string list>(pinnedObligationIds, p.Obligations |> List.map _.Id)
    let byKind kind = p.Obligations |> List.filter (fun o -> o.Kind = kind)
    let effect = byKind TypedObligationKind.UndecidedEffectFailure |> List.exactlyOne
    Assert.Equal<string list>([ "ITimerPorts.Perform/Stop" ], effect.ExtensionSurface)
    Assert.Equal(Some "Stop effect failure", effect.PlanSubject)
    let uncovered = byKind TypedObligationKind.UncoveredRequirement |> List.exactlyOne
    Assert.Equal<string list>([ keySession ], uncovered.RequirementKeys)
    Assert.Equal<string list>([ "DEC-TIMER-STATES"; "VER-TIMER-SESSION" ], uncovered.NodeIds)
    let extension = byKind TypedObligationKind.ExtensionBehavior
    Assert.Equal<string list>(List.sort [ keyPaused; keyStop ], extension |> List.collect _.RequirementKeys |> List.sort)
    Assert.True(extension |> List.forall (fun o -> List.contains "VER-TIMER-COMPLETION" o.NodeIds))
    Assert.Empty(byKind TypedObligationKind.UnsupportedVocabulary)
    // Every plan subject names a declareUnresolved in the plan, and back.
    let unresolved = (view p.ValidatedPlan).Workflow.Unresolved |> List.map _.Subject.Key |> Set.ofList
    let carried =
        p.Obligations |> List.choose _.PlanSubject
        |> List.map (fun s -> (SemanticName.create s |> ok "subject").Key) |> Set.ofList
    Assert.Equal<Set<string>>(unresolved, carried)
    let stopEffect = (view p.ValidatedPlan).Workflow.Unresolved |> List.find (fun u -> u.Subject.Key = "StopEffectFailure")
    Assert.Equal(Some "Stop", stopEffect.Effect |> Option.map _.Key)

// ---------------------------------------------------------------------------
// 1-2. Determinism and identical output.
// ---------------------------------------------------------------------------

[<Fact>]
let ``identical input gives an identical plan, obligations and byte-identical serialization`` () =
    let first = generate ()
    for _ in 1 .. 10 do
        let again = generate ()
        Assert.Equal(encodeDefaultPlan first, encodeDefaultPlan again)
        Assert.Equal(first.Digest, again.Digest)
        Assert.Equal(first.Draft, again.Draft)
        Assert.Equal(encodeObligations first.ObligationSet, encodeObligations again.ObligationSet)
        Assert.Equal(first.Provenance, again.Provenance)
    Assert.Equal(pinnedDefaultPlanDigest, first.Digest)
    Assert.Equal(sha256Of (encodeDefaultPlan first), first.Digest)

[<Fact>]
let ``generation is deterministic over record, state, transition and port order`` () =
    let r = records ()
    let shuffled =
        { r with Records = List.rev r.Records }
        |> withModel (fun m -> { m with States = List.rev m.States; Transitions = List.rev m.Transitions |> List.map (fun t -> { t with RequirementKeys = List.rev t.RequirementKeys }) })
        |> withPorts List.rev
    Assert.Equal(ConstructionDecisions.digest r, ConstructionDecisions.digest shuffled)
    let a = generate ()
    let b = generateDefaultPlan (prepared ()) shuffled v2 |> ok "shuffled"
    Assert.Equal(encodeDefaultPlan a, encodeDefaultPlan b)
    Assert.Equal(a.Digest, b.Digest)

// ---------------------------------------------------------------------------
// 3. Requirement coverage.
// ---------------------------------------------------------------------------

[<Fact>]
let ``every cohort requirement is covered by an operation or an obligation`` () =
    let p = generate ()
    let constructed =
        (view p.ValidatedPlan).Coverage
        |> Map.filter (fun _ c -> match c with Constructed ops -> ops |> List.exists (fun l -> not (l.StartsWith "port:")) | _ -> false)
        |> Map.keys |> Set.ofSeq
    let obligated = p.Obligations |> List.collect _.RequirementKeys |> Set.ofList
    let assigned = (preparedCohortView (prepared ())).AssignedRequirementKeys
    for key in assigned do
        Assert.True(Set.contains key constructed || Set.contains key obligated, sprintf "%s is neither constructed nor obligated" key)
    Assert.Equal<Set<string>>(set [ keyA; keySingle; keyRejection ], constructed)
    // A key the records construct structurally carries no obligation.
    for key in constructed do
        Assert.DoesNotContain(key, p.Obligations |> List.filter (fun o -> o.Kind <> TypedObligationKind.Conflict) |> List.collect _.RequirementKeys)

[<Fact>]
let ``a requirement no record cites becomes an uncovered obligation, never invented structure`` () =
    // Drop L104 and L367-L368 from every transition: they are still assigned.
    let r =
        records ()
        |> withModel (fun m -> { m with Transitions = m.Transitions |> List.map (fun t -> { t with RequirementKeys = [ keyA ] }) })
    let p = generateDefaultPlan (prepared ()) r v2 |> ok "generate"
    let uncovered =
        p.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.UncoveredRequirement) |> List.collect _.RequirementKeys
    Assert.Equal<string list>([ keySingle; keySession; keyRejection ] |> List.sort, List.sort uncovered)
    let w = (view p.ValidatedPlan).Workflow
    Assert.Equal(5, w.Transitions.Length)
    Assert.True(w.Transitions |> List.forall (fun t -> t.Trace.RequirementKeys = [ keyA ]))

// ---------------------------------------------------------------------------
// 4. Unapproved decisions and records for unapproved nodes.
// ---------------------------------------------------------------------------

[<Fact>]
let ``an unapproved decision cannot be prepared, so no default plan can be generated`` () =
    match prepareCohort (manifest ()) (blueprint ()) cohort Set.empty with
    | Ok _ -> failwith "prepared without approval"
    | Error errors -> Assert.Contains(MissingDecisionAuthorization "DEC-TIMER-STATES", errors)

/// The blueprint plus a decision node on the out-of-cohort conflict
/// requirement. It is a valid blueprint and the node is outside COH-TIMER's
/// required scope.
let private blueprintWithOutsideDecision () =
    let b = blueprint ()
    { b with
        Requirements =
            b.Requirements |> List.map (fun r -> if r.Source.Key = keyShort then { r with NodeIds = r.NodeIds @ [ "DEC-OUTSIDE" ] } else r)
        Nodes = b.Nodes @ [ { Id = "DEC-OUTSIDE"; Kind = Decision; RequirementKeys = [ keyShort ]; DependsOn = []; Justification = Some "test input" } ] }

[<Fact>]
let ``a record for a node outside the cohort's approved scope is refused`` () =
    let b = blueprintWithOutsideDecision ()
    let p = prepareCohort (manifest ()) b cohort approved |> ok "prepare"
    let r = records ()
    let outside =
        { r with
            BlueprintDigest = blueprintDigest b
            Records = r.Records @ [ { NodeId = "DEC-OUTSIDE"; Content = PortContract [ RecordedCurrentTime [ keyShort ] ] } ] }
    refusedWith (RecordForUnapprovedNode "DEC-OUTSIDE") (generateDefaultPlan p outside v2)

[<Fact>]
let ``records for unknown nodes, wrong node kinds and duplicates are refused`` () =
    let r = records ()
    let model = r.Records |> List.find (fun x -> x.NodeId = "DEC-TIMER-STATES")
    let ports = r.Records |> List.find (fun x -> x.NodeId = "IFC-TIMER-PORTS")
    refusedWith (UnknownRecordNode "DEC-NOWHERE") (generateDefaultPlan (prepared ()) { r with Records = r.Records @ [ { ports with NodeId = "DEC-NOWHERE" } ] } v2)
    refusedWith (RecordKindNotAllowed("VER-TIMER-TABLE", "verificationObligation", "portContract"))
        (generateDefaultPlan (prepared ()) { r with Records = r.Records @ [ { ports with NodeId = "VER-TIMER-TABLE" } ] } v2)
    refusedWith (RecordKindNotAllowed("COH-TIMER", "cohort", "portContract"))
        (generateDefaultPlan (prepared ()) { r with Records = r.Records @ [ { ports with NodeId = "COH-TIMER" } ] } v2)
    refusedWith (RecordKindNotAllowed("CONFLICT-TIMER-SHORT-STOP", "conflict", "portContract"))
        (generateDefaultPlan (prepared ()) { r with Records = r.Records @ [ { ports with NodeId = "CONFLICT-TIMER-SHORT-STOP" } ] } v2)
    refusedWith (RecordKindNotAllowed("IFC-TIMER-PORTS", "interface", "stateModel"))
        (generateDefaultPlan (prepared ()) { r with Records = [ { model with NodeId = "IFC-TIMER-PORTS" } ] } v2)
    refusedWith (DuplicateRecord "IFC-TIMER-PORTS") (generateDefaultPlan (prepared ()) { r with Records = r.Records @ [ ports ] } v2)

[<Fact>]
let ``a required decision without a record fails closed`` () =
    let r = records ()
    let onlyPorts = { r with Records = r.Records |> List.filter (fun x -> x.NodeId <> "DEC-TIMER-STATES") }
    refusedWith (MissingDecisionContent "DEC-TIMER-STATES") (generateDefaultPlan (prepared ()) onlyPorts v2)
    refusedWith MissingStateModel (generateDefaultPlan (prepared ()) onlyPorts v2)

[<Fact>]
let ``records approved against another blueprint or cohort are refused`` () =
    let r = records ()
    let other = "sha256:" + String('0', 64)
    refusedWith (DecisionBlueprintMismatch(other, blueprintDigest (blueprint ()))) (generateDefaultPlan (prepared ()) { r with BlueprintDigest = other } v2)
    refusedWith (DecisionCohortMismatch("COH-OTHER", cohort)) (generateDefaultPlan (prepared ()) { r with CohortId = "COH-OTHER" } v2)
    refusedWith (UnsupportedDecisionSchema "ordo.construction-decisions/0")
        (generateDefaultPlan (prepared ()) { r with SchemaVersion = "ordo.construction-decisions/0" } v2)

[<Fact>]
let ``records that contradict the blueprint are refused with a precise reason`` () =
    let r = records ()
    let generateWith records = generateDefaultPlan (prepared ()) records v2
    let mapTransitions f = withModel (fun m -> { m with Transitions = f m.Transitions }) r
    // A requirement the node does not own (IFC-TIMER-PORTS owns L131-L144).
    refusedWith (RequirementNotOwnedByNode("DEC-TIMER-STATES", "transition:NoTimer/Start", keyStop))
        (generateWith (mapTransitions (List.map (fun t -> if t.Command = "Start" then { t with RequirementKeys = keyStop :: t.RequirementKeys } else t))))
    refusedWith (MissingRequirementCitation("DEC-TIMER-STATES", "transition:Running/Pause"))
        (generateWith (mapTransitions (List.map (fun t -> if t.Command = "Pause" then { t with RequirementKeys = [] } else t))))
    // An unknown state in a transition.
    refusedWith (TransitionUnknownState("DEC-TIMER-STATES", "transition:Running/Pause", "Suspended"))
        (generateWith (mapTransitions (List.map (fun t -> if t.Command = "Pause" then { t with To = "Suspended" } else t))))
    // Conflicting transitions for one (state, command).
    let conflicting = { Command = "Stop"; From = "Running"; To = "Paused"; RequirementKeys = [ keyA ] }
    refusedWith (ConflictingRecordedTransitions("DEC-TIMER-STATES", "Running", "Stop", [ "NoTimer"; "Paused" ]))
        (generateWith (mapTransitions (fun ts -> ts @ [ conflicting ])))
    refusedWith (DuplicateRecordedTransition("DEC-TIMER-STATES", "transition:Running/Stop"))
        (generateWith (mapTransitions (fun ts -> ts @ [ { conflicting with To = "NoTimer" } ])))
    // State roles.
    refusedWith (MissingRecordedInitialState "DEC-TIMER-STATES")
        (generateWith (withModel (fun m -> { m with States = m.States |> List.map (fun s -> { s with Role = Ordinary }) }) r))
    refusedWith (MultipleRecordedInitialStates("DEC-TIMER-STATES", [ "NoTimer"; "Paused" ]))
        (generateWith (withModel (fun m -> { m with States = m.States |> List.map (fun s -> if s.Name = "Paused" then { s with Role = Initial } else s) }) r))
    refusedWith (DuplicateRecordedState("DEC-TIMER-STATES", "NoTimer"))
        (generateWith (withModel (fun m -> { m with States = m.States @ [ { Name = "no-timer"; Role = Ordinary } ] }) r))
    // Names outside the grammar.
    refusedWith (InvalidRecordName("DEC-TIMER-STATES", "Timer();", NameHasIllegalCharacter '('))
        (generateWith (withModel (fun m -> { m with Workflow = "Timer();" }) r))
    // An effect for a command no transition defines, and a duplicate port.
    refusedWith (EffectForUnknownCommand("IFC-TIMER-PORTS", "Reset"))
        (generateWith (withPorts (fun ps -> ps @ [ RecordedTransitionEffect("Reset", None, [ keyStop ]) ]) r))
    refusedWith (DuplicateRecordedPort "port:currentTime")
        (generateWith (withPorts (fun ps -> ps @ [ RecordedCurrentTime [ keyPaused ] ]) r))
    // A decided disposition on an interface node is not an approved decision.
    refusedWith (DecidedDispositionWithoutDecision("IFC-TIMER-PORTS", "Stop"))
        (generateWith (withPorts (List.map (function RecordedTransitionEffect(c, _, k) -> RecordedTransitionEffect(c, Some EffectFailureDisposition.Remain, k) | p -> p)) r))

[<Fact>]
let ``a structurally invalid state model is refused with the plan violations, never patched`` () =
    // Without Resume and Stop from Paused, Paused is reachable but a dead end.
    let r = records () |> withModel (fun m -> { m with Transitions = m.Transitions |> List.filter (fun t -> t.From <> "Paused") })
    match generateDefaultPlan (prepared ()) r v2 with
    | Ok _ -> failwith "generated a plan with a dead-end state"
    | Error [ GeneratedPlanInvalid violations ] -> Assert.Contains(DeadEndState "Paused", violations)
    | Error other -> failwithf "unexpected refusals %A" other
    // A state no transition touches has nothing to trace to.
    let unconnected = records () |> withModel (fun m -> { m with Transitions = m.Transitions |> List.filter (fun t -> t.From <> "Paused" && t.To <> "Paused") })
    refusedWith (UnconnectedRecordedState("DEC-TIMER-STATES", "Paused")) (generateDefaultPlan (prepared ()) unconnected v2)

// ---------------------------------------------------------------------------
// 5. Conflicting requirements remain unresolved.
// ---------------------------------------------------------------------------

[<Fact>]
let ``the out-of-cohort short-stop conflict stays an obligation and is not resolved by the plan`` () =
    let p = generate ()
    let conflict = p.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.Conflict) |> List.exactlyOne
    Assert.Equal<string list>([ "CONFLICT-TIMER-SHORT-STOP" ], conflict.NodeIds)
    Assert.Equal<string list>([ keyShort ], conflict.RequirementKeys)
    Assert.Equal(None, conflict.PlanSubject)
    Assert.Contains("shared source document", conflict.Question)
    // Nothing in the plan cites the conflict or its requirement.
    for op in operations p do
        let t = operationTrace op
        Assert.DoesNotContain(keyShort, t.RequirementKeys)
        Assert.DoesNotContain("CONFLICT-TIMER-SHORT-STOP", t.NodeIds)
    // The plan does not pick an outcome: Stop is exactly as recorded, with no guard.
    let stops = (view p.ValidatedPlan).Workflow.Transitions |> List.filter (fun t -> t.Command.Key = "Stop")
    Assert.Equal<(string * string) list>([ "Paused", "NoTimer"; "Running", "NoTimer" ], stops |> List.map (fun t -> t.From.Key, t.To.Key))
    Assert.True(stops |> List.forall (fun t -> List.isEmpty t.Guards))

[<Fact>]
let ``a record cannot cite the conflicting requirement to resolve it`` () =
    let r = records () |> withModel (fun m -> { m with Transitions = m.Transitions |> List.map (fun t -> if t.Command = "Stop" then { t with RequirementKeys = keyShort :: t.RequirementKeys } else t) })
    refusedWith (RequirementNotOwnedByNode("DEC-TIMER-STATES", "transition:Running/Stop", keyShort)) (generateDefaultPlan (prepared ()) r v2)

[<Fact>]
let ``a conflict the blueprint does not relate to the cohort produces no obligation`` () =
    // Move the conflict's requirement to a document no cohort requirement uses.
    let m = manifest ()
    let moved = m.Requirements |> List.map (fun s -> if s.Key = keyShort then { s with Document = "elsewhere/other.md" } else s)
    let m2 = { Digest = manifestDigest moved; Requirements = moved }
    let b = blueprint ()
    let b2 =
        { b with
            SourceManifestDigest = m2.Digest
            Requirements = b.Requirements |> List.map (fun r -> if r.Source.Key = keyShort then { r with Source = { r.Source with Document = "elsewhere/other.md" } } else r) }
    let p = prepareCohort m2 b2 cohort approved |> ok "prepare"
    let r = { records () with BlueprintDigest = blueprintDigest b2 }
    let plan = generateDefaultPlan p r v2 |> ok "generate"
    Assert.Empty(plan.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.Conflict))

// ---------------------------------------------------------------------------
// 6. Unsupported vocabulary.
// ---------------------------------------------------------------------------

[<Fact>]
let ``an unsupported vocabulary version is refused, never upgraded`` () =
    Assert.Equal(Error(UnsupportedVocabularyVersion "ordo.construction-vocabulary/3"), ConstructionVocabulary.ofVersion "ordo.construction-vocabulary/3")
    Assert.Equal(Ok ConstructionVocabulary.Version1, ConstructionVocabulary.ofVersion VocabularyVersion1)
    Assert.Equal(Ok ConstructionVocabulary.Version2, ConstructionVocabulary.ofVersion VocabularyVersion2)

/// The v2-remain test blueprint adds DEC-TIMER-STOP-FAILURE on L131-L144
/// ("TEST INPUT ONLY"); its approval here is a test input.
let private decidedFixture () =
    let b = readBlueprint (read "blueprint.v2-remain.json") |> ok "blueprint v2"
    let p = prepareCohort (manifest ()) b cohort (Set.add "DEC-TIMER-STOP-FAILURE" approved) |> ok "prepare v2"
    let r = records ()
    let decided =
        { r with
            BlueprintDigest = blueprintDigest b
            Records =
                (r |> withPorts (List.filter (function RecordedCurrentTime _ -> true | _ -> false))).Records
                @ [ { NodeId = "DEC-TIMER-STOP-FAILURE"
                      Content = PortContract [ RecordedTransitionEffect("Stop", Some EffectFailureDisposition.Remain, [ keyStop ]) ] } ] }
    p, decided

[<Fact>]
let ``vocabulary 1 cannot carry an approved disposition, so it becomes an explicit obligation`` () =
    let p, decided = decidedFixture ()
    let first = generateDefaultPlan p decided v1 |> ok "v1"
    Assert.Equal(VocabularyVersion1, first.Draft.VocabularyVersion)
    let unsupported = first.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.UnsupportedVocabulary) |> List.exactlyOne
    Assert.Equal<string list>([ "DEC-TIMER-STOP-FAILURE" ], unsupported.NodeIds)
    Assert.Equal<string list>([ "ITimerPorts.Perform/Stop" ], unsupported.ExtensionSurface)
    Assert.Contains("'remain'", unsupported.Question)
    Assert.Empty(first.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.UndecidedEffectFailure))
    let ports = (view first.ValidatedPlan).Workflow.Ports |> List.map _.Kind
    Assert.Contains(TransitionEffectPort((SemanticName.create "Stop" |> ok "name"), None), ports)
    // Vocabulary 2 carries it: no obligation, the port says remain.
    let second = generateDefaultPlan p decided v2 |> ok "v2"
    Assert.Empty(second.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.UnsupportedVocabulary || o.Kind = TypedObligationKind.UndecidedEffectFailure))
    Assert.Contains(TransitionEffectPort((SemanticName.create "Stop" |> ok "name"), Some EffectFailureDisposition.Remain), (view second.ValidatedPlan).Workflow.Ports |> List.map _.Kind)

[<Fact>]
let ``an undecided effect under vocabulary 1 is still an obligation`` () =
    let p = generateDefaultPlan (prepared ()) (records ()) v1 |> ok "v1"
    let effect = p.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.UndecidedEffectFailure) |> List.exactlyOne
    Assert.Equal(Some "Stop effect failure", effect.PlanSubject)
    Assert.True((view p.ValidatedPlan).Workflow.Unresolved |> List.exists (fun u -> u.Subject.Key = "StopEffectFailure" && u.Effect.IsNone))

[<Fact>]
let ``the decision wire refuses unknown kinds, ports, roles, members and dispositions`` () =
    let text = read "construction-decisions.json"
    let refused (edited: string) = Assert.True(Result.isError (readDecisionRecords edited), edited.Substring(0, min 80 edited.Length))
    refused (text.Replace("\"kind\": \"stateModel\"", "\"kind\": \"guardModel\""))
    refused (text.Replace("\"port\": \"currentTime\"", "\"port\": \"database\""))
    refused (text.Replace("\"role\": \"initial\"", "\"role\": \"start\""))
    refused (text.Replace("\"onFailure\": null", "\"onFailure\": \"retry\""))
    refused (text.Replace("\"onFailure\": null,", ""))
    refused (text.Replace("\"cohortId\": \"COH-TIMER\",", "\"cohortId\": \"COH-TIMER\", \"approved\": true,"))
    refused (text.Replace("ordo.construction-decisions/1", "ordo.construction-decisions/2"))
    refused (text.Replace("\"workflow\": \"Timer\"", "\"workflow\": \"Timer\", \"guards\": []"))
    Assert.True(Result.isOk (readDecisionRecords text))

// ---------------------------------------------------------------------------
// 8. No implicit dependency changes.
// ---------------------------------------------------------------------------

[<Fact>]
let ``neither the decision records nor the default plan can express a package, project or path`` () =
    let text = read "construction-decisions.json"
    for smuggled in [ "\"packages\": [\"Newtonsoft.Json\"]"; "\"projectEdits\": []"; "\"files\": []"; "\"command\": \"dotnet add\"" ] do
        let edited = text.Replace("\"cohortId\": \"COH-TIMER\",", "\"cohortId\": \"COH-TIMER\", " + smuggled + ",")
        Assert.True(Result.isError (readDecisionRecords edited), smuggled)
    let p = generate ()
    // Only the vocabulary's own operations, none of which carries a path or package.
    for op in operations p do
        match op with
        | DefineWorkflow _ | DefineState _ | DefineTransition _ | DeclarePort _ | DeclareUnresolved _ -> ()
        | other -> failwithf "generator emitted %A, which no record justifies" other
    for path in p.GeneratedLayout do
        Assert.StartsWith("generated/Timer/", path)
        Assert.False(path.EndsWith ".fsproj" || path.EndsWith ".props" || path.EndsWith ".targets" || path.Contains "packages", path)
    let encoded = encodeDefaultPlan p
    for word in [ "fsproj"; "PackageReference"; "nuget"; "packages" ] do
        Assert.DoesNotContain(word, encoded)

// ---------------------------------------------------------------------------
// 9. Stable provenance.
// ---------------------------------------------------------------------------

[<Fact>]
let ``provenance names the generator and the digest of every input, recomputed independently`` () =
    let p = generate ()
    let pv = p.Provenance
    Assert.Equal("ordo.default-plan", pv.GeneratorId)
    Assert.Equal("ordo.default-plan/1", pv.GeneratorVersion)
    Assert.Equal(VocabularyVersion2, pv.VocabularyVersion)
    Assert.Equal(cohort, pv.CohortId)
    Assert.Equal((manifest ()).Digest, pv.SourceManifestDigest)
    Assert.Equal(blueprintDigest (blueprint ()), pv.BlueprintDigest)
    Assert.Equal(sha256Of (read "construction-decisions.json"), pv.DecisionRecordsDigest)
    Assert.Equal(ConstructionPlan.digest (validate (prepared ()) p.Draft |> ok "validate"), pv.PlanDigest)
    Assert.Equal(sha256Of (encodeObligations p.ObligationSet), pv.ObligationsDigest)
    Assert.Equal(sha256Of (encodeDefaultPlan p), pv.DefaultPlanDigest)
    Assert.Equal(pv.PlanDigest, p.ObligationSet.PlanDigest)
    // The serialization carries every input digest.
    let encoded = encodeDefaultPlan p
    for value in [ pv.SourceManifestDigest; pv.BlueprintDigest; pv.DecisionRecordsDigest; pv.PlanDigest; pv.ObligationsDigest ] do
        Assert.Contains(value, encoded)

[<Fact>]
let ``any input change changes the default plan digest, and obligation ids depend only on content`` () =
    let a = generate ()
    let b = generateDefaultPlan (prepared ()) (records ()) v1 |> ok "v1"
    Assert.NotEqual<string>(a.Digest, b.Digest)
    Assert.NotEqual<string>(a.PlanDigest, b.PlanDigest)
    let edited = records () |> withModel (fun m -> { m with Transitions = m.Transitions |> List.map (fun t -> { t with RequirementKeys = [ keyA ] }) })
    let c = generateDefaultPlan (prepared ()) edited v2 |> ok "edited"
    Assert.NotEqual<string>(a.Provenance.DecisionRecordsDigest, c.Provenance.DecisionRecordsDigest)
    Assert.NotEqual<string>(a.Digest, c.Digest)
    // The conflict obligation is unaffected by the edit, so its id is unchanged.
    let conflictId (p: DefaultPlan) = p.Obligations |> List.find (fun o -> o.Kind = TypedObligationKind.Conflict) |> _.Id
    Assert.Equal(conflictId a, conflictId c)

[<Fact>]
let ``the obligations wire round-trips strictly`` () =
    let p = generate ()
    let encoded = encodeObligations p.ObligationSet
    let reread = readObligations encoded |> ok "obligations"
    Assert.Equal(p.ObligationSet, reread)
    Assert.Equal(encoded, encodeObligations reread)
    Assert.True(Result.isError (readObligations (encoded.Replace("\"kind\": \"conflict\"", "\"kind\": \"waived\""))))
    Assert.True(Result.isError (readObligations (encoded.Replace("\"cohortId\": \"COH-TIMER\",", "\"cohortId\": \"COH-TIMER\", \"resolved\": true,"))))
    Assert.True(Result.isError (readObligations (encoded.Replace("ordo.construction-obligations/1", "ordo.construction-obligations/9"))))
