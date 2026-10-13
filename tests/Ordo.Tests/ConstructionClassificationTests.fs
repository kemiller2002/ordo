/// CONSTRUCT-010 phase 4: decision classification. Every semantic choice of a
/// plan is required, approved, implied, unresolved or unsupported; only the
/// first three may be generated, and a plan with any other choice cannot be
/// represented as complete. The fixture is the real time-entry timer cohort,
/// with `construction-decisions-v2.json` stating the rejection rule
/// (REQUIREMENTS.md L367-368) and the Stop effect's decision points.
module Ordo.Tests.ConstructionClassificationTests

open System
open System.IO
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionPlanWire
open Ordo.Core.ConstructionDecisions
open Ordo.Core.ConstructionDefaultPlan
open Ordo.Core.ConstructionClassification

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

let private prepared () =
    let manifest = readManifest (read "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read "blueprint.json") |> ok "blueprint"
    prepareCohort manifest blueprint "COH-TIMER" (Set.singleton "DEC-TIMER-STATES") |> ok "prepare"

let private recordsV1 () = readDecisionRecords (read "construction-decisions.json") |> ok "decisions v1"
let private recordsV2 () = readDecisionRecords (read "construction-decisions-v2.json") |> ok "decisions v2"
let private planV2 () = generateDefaultPlan (prepared ()) (recordsV1 ()) ConstructionVocabulary.Version2 |> ok "v2 plan"
let private planV3With records = generateDefaultPlan (prepared ()) records ConstructionVocabulary.Version3 |> ok "v3 plan"
let private planV3 () = planV3With (recordsV2 ())

let private keyStop = "kemiller2002/time-entry-state-machine/docs/architecture/business-activity-ledger-state-model.md#L131-L144"
let private keyRejection = "kemiller2002/time-entry-state-machine/effort-experiment/f-sharp-state/REQUIREMENTS.md#L367-L368"

let private subjects (choices: ClassifiedChoice list) = choices |> List.map _.Subject |> Set.ofList
let private classOf (c: Classification) subject = (c.Choices |> List.find (fun x -> x.Subject = subject)).Class

let private refusedPairs =
    [ "NoTimer/Pause"; "NoTimer/Resume"; "NoTimer/Stop"; "Paused/Pause"; "Paused/Start"; "Running/Resume"; "Running/Start" ]
    |> List.map ((+) "pair:")

let private withStateModel (f: StateModelContent -> StateModelContent) (records: ApprovedDecisionRecords) =
    { records with
        Records = records.Records |> List.map (fun r -> match r.Content with StateModel m -> { r with Content = StateModel(f m) } | _ -> r) }

let private withStopPoints (f: RecordedDecisionPoint list -> RecordedDecisionPoint list) (records: ApprovedDecisionRecords) =
    { records with
        Records =
          records.Records
          |> List.map (fun r ->
              match r.Content with
              | PortContract ports ->
                  { r with
                      Content =
                        PortContract(ports |> List.map (function
                            | RecordedTransitionEffect(c, d, k, Some points) -> RecordedTransitionEffect(c, d, k, Some(f points))
                            | other -> other)) }
              | _ -> r) }

// ---- the closed-world refusal, reproduced and fixed ----

[<Fact>]
let ``vocabulary 2 decides unlisted pairs by omission, and classification says so`` () =
    let c = classify (planV2 ()).ValidatedPlan
    for pair in refusedPairs do
        match classOf c pair with
        | ChoiceClass.Unsupported reason -> Assert.Contains("by omission", reason)
        | other -> failwithf "%s classified %A" pair other
    Assert.True(Result.isError (complete (planV2 ()).ValidatedPlan))

[<Fact>]
let ``vocabulary 3 refuses the same pairs on the authority of the cited rejection rule`` () =
    let c = classify (planV3 ()).ValidatedPlan
    for pair in refusedPairs do
        Assert.Equal(ChoiceClass.Implied("pairRule refused", [ keyRejection ]), classOf c pair)
    for legal in [ "pair:NoTimer/Start"; "pair:Running/Pause"; "pair:Paused/Resume"; "pair:Running/Stop"; "pair:Paused/Stop" ] do
        Assert.Equal(ChoiceClass.Approved [ "DEC-TIMER-STATES" ], classOf c legal)

[<Fact>]
let ``a pair the requirements leave open is unresolved and becomes an obligation, never a refusal`` () =
    let records = recordsV2 () |> withStateModel (fun m -> { m with Unlisted = Some RecordedUnlisted.Undecided })
    let plan = planV3With records
    let c = classify plan.ValidatedPlan
    for pair in refusedPairs do
        match classOf c pair with
        | ChoiceClass.Unresolved question -> Assert.Contains("do not decide", question)
        | other -> failwithf "%s classified %A" pair other
    let undecided = plan.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.UndecidedTransition)
    Assert.Equal(7, undecided.Length)

[<Fact>]
let ``an explicit undecided pair stays open even under a refusing rule`` () =
    let question = "Chrona-style gap: may a paused timer be paused again, or is that a refusal?"
    let records =
        recordsV2 ()
        |> withStateModel (fun m ->
            { m with Undecided = [ { RecordedUndecidedPair.Command = "Pause"; From = "Paused"; Question = question; RequirementKeys = [ keyRejection ] } ] })
    let c = classify (planV3With records).ValidatedPlan
    Assert.Equal(ChoiceClass.Unresolved question, classOf c "pair:Paused/Pause")
    Assert.Equal(ChoiceClass.Implied("pairRule refused", [ keyRejection ]), classOf c "pair:Paused/Start")

// ---- OPT-14: an extension with an open decision is not handed off as "implement" ----

[<Fact>]
let ``the rounding tie is an open decision, and Stop gets no implement obligation`` () =
    let plan = planV3 ()
    let c = classify plan.ValidatedPlan
    match classOf c "extension:effect/Stop/point:RoundingTie" with
    | ChoiceClass.Unresolved question -> Assert.Contains("exactly half a minute", question)
    | other -> failwithf "tie classified %A" other
    for decided in [ "ExcludePausedTime"; "RoundToTheNearestWholeMinute"; "CreateThroughTheSharedEntryPath" ] do
        Assert.True(generates (classOf c ("extension:effect/Stop/point:" + decided)), decided)
    let stopSurface = "ITimerPorts.Perform/Stop"
    let implementsStop =
        plan.Obligations
        |> List.filter (fun o -> o.Kind = TypedObligationKind.ExtensionBehavior && List.contains stopSurface o.ExtensionSurface)
    Assert.Empty(implementsStop)
    let decide = plan.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.UndecidedExtensionDecision)
    Assert.Equal(1, decide.Length)
    Assert.Contains(keyStop, decide.Head.RequirementKeys)
    Assert.Contains("Rounding tie", decide.Head.Question)

[<Fact>]
let ``vocabulary 2 cannot even state the extension's choices`` () =
    match classOf (classify (planV2 ()).ValidatedPlan) "extension:effect/Stop" with
    | ChoiceClass.Unsupported reason -> Assert.Contains("no decision inventory", reason)
    | other -> failwithf "%A" other

[<Fact>]
let ``once the tie is decided the Stop extension becomes implementable`` () =
    let decided = recordsV2 () |> withStopPoints (List.map (fun p -> { p with Question = None }))
    let plan = planV3With decided
    Assert.Empty(plan.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.UndecidedExtensionDecision))
    Assert.Contains(plan.Obligations, fun o -> o.Kind = TypedObligationKind.ExtensionBehavior && List.contains "ITimerPorts.Perform/Stop" o.ExtensionSurface)

// ---- completeness ----

[<Fact>]
let ``the real timer is incomplete exactly where the requirements are`` () =
    match complete (planV3 ()).ValidatedPlan with
    | Ok _ -> failwith "a plan with open decisions was represented as complete"
    | Error openChoices ->
        Assert.Equal<Set<string>>(
            set [ "effectFailure:Stop"; "extension:effect/Stop/point:RoundingTie"; "unresolved:UncoveredRequirementFfc2989b" ],
            subjects openChoices)

[<Fact>]
let ``classification is deterministic and its document is canonical`` () =
    let a = classify (planV3 ()).ValidatedPlan
    let b = classify (planV3 ()).ValidatedPlan
    Assert.Equal(a, b)
    Assert.Equal(encodeClassification a, encodeClassification b)
    Assert.StartsWith("sha256:", digest a)
    Assert.Contains("\"complete\": false", encodeClassification a)

// ---- plan validation (vocabulary 3) ----

let private draftV3 () = (planV3 ()).Draft

let private refused expected (draft: PlanDraft) =
    match validate (prepared ()) draft with
    | Ok _ -> failwithf "accepted; expected %A" expected
    | Error errors -> Assert.True(List.contains expected errors, sprintf "expected %A in %A" expected errors)

let private mapOps f (draft: PlanDraft) = { draft with Operations = f draft.Operations }

[<Fact>]
let ``a vocabulary 3 plan must state its pair rule`` () =
    draftV3 () |> mapOps (List.filter (function DeclarePairRule _ -> false | _ -> true)) |> refused MissingPairRule

[<Fact>]
let ``a pair cannot be both a transition and a refusal`` () =
    let t: TraceDraft = { RequirementKeys = [ keyRejection ]; NodeIds = [ "DEC-TIMER-STATES" ] }
    draftV3 ()
    |> mapOps (fun ops -> ops @ [ DefineRefusal { Pair = { InState = "Running"; OnCommand = "Pause" }; Trace = t } ])
    |> refused (ConflictingPairDisposition("Running", "Pause", [ "refusal"; "transition" ]))

[<Fact>]
let ``every effect needs a decision inventory, naming an extension that exists`` () =
    draftV3 () |> mapOps (List.filter (function DeclareExtensionInventory _ -> false | _ -> true)) |> refused (MissingExtensionInventory "effect/Stop")
    let t: TraceDraft = { RequirementKeys = [ keyStop ]; NodeIds = [ "IFC-TIMER-PORTS" ] }
    draftV3 ()
    |> mapOps (fun ops -> ops @ [ DeclareExtensionInventory { Extension = ExtensionRef.Guard "Has project"; DecisionPoints = []; Trace = t } ])
    |> refused (UnknownExtension("inventory:guard/HasProject", "guard/HasProject"))

[<Fact>]
let ``decision points are distinct by normalized name`` () =
    let t: TraceDraft = { RequirementKeys = [ keyStop ]; NodeIds = [ "IFC-TIMER-PORTS" ] }
    draftV3 ()
    |> mapOps (List.map (function
        | DeclareExtensionInventory i ->
            DeclareExtensionInventory
                { i with
                    DecisionPoints =
                        i.DecisionPoints @ [ { DecisionPointDraft.Point = "rounding-tie"; Resolution = DecisionPointResolution.Decided; Trace = t } ] }
        | op -> op))
    |> refused (DuplicateDefinition "inventory:effect/Stop/point:RoundingTie")

[<Fact>]
let ``earlier vocabularies cannot carry vocabulary 3 operations`` () =
    let draft = draftV3 ()
    let asV2 = { draft with VocabularyVersion = VocabularyVersion2 }
    refused (NotInVocabulary("pairRule", "declarePairRule", VocabularyVersion2)) asV2
    refused (NotInVocabulary("inventory:effect/Stop", "declareExtensionInventory", VocabularyVersion2)) asV2
    match readPlan (encodePlan asV2) with
    | Ok _ -> failwith "the wire accepted a vocabulary 3 operation in a vocabulary 2 plan"
    | Error message -> Assert.Contains("is not in " + VocabularyVersion2, message)

[<Fact>]
let ``the vocabulary 3 wire round-trips and refuses malformed points`` () =
    let draft = draftV3 ()
    Assert.Equal(draft, readPlan (encodePlan draft) |> ok "round trip")
    let text = encodePlan draft
    for broken in
        [ text.Replace("\"unlisted\": \"refused\"", "\"unlisted\": \"refuse-everything\"")
          text.Replace("\"decided\": false", "\"decided\": \"no\"")
          text.Replace("\"kind\": \"effect\"", "\"kind\": \"file\"") ] do
        Assert.NotEqual<string>(text, broken)
        Assert.True(Result.isError (readPlan broken), broken)

// ---- decision records version 2 ----

[<Fact>]
let ``decision record schema and vocabulary must match`` () =
    let refusals vocabulary records =
        match generateDefaultPlan (prepared ()) records vocabulary with
        | Ok _ -> failwith "generated across a schema/vocabulary mismatch"
        | Error refusals -> refusals
    Assert.Contains(DecisionSchemaVocabularyMismatch(DecisionsSchemaVersion, VocabularyVersion3), refusals ConstructionVocabulary.Version3 (recordsV1 ()))
    Assert.Contains(DecisionSchemaVocabularyMismatch(DecisionsSchemaVersion2, VocabularyVersion2), refusals ConstructionVocabulary.Version2 (recordsV2 ()))

[<Fact>]
let ``version 2 records must state the pair rule and every effect's decision points`` () =
    let text = read "construction-decisions-v2.json"
    for missing in [ "\"unlisted\""; "\"decisionPoints\"" ] do
        let start = text.IndexOf missing
        Assert.True(start > 0, missing)
    let withoutPoints = text.Replace("\"decisionPoints\": [", "\"ignoredPoints\": [")
    Assert.True(Result.isError (readDecisionRecords withoutPoints))
    Assert.True(Result.isError (readDecisionRecords (text.Replace("\"unlisted\":", "\"closedWorld\":"))))

[<Fact>]
let ``a decision point may cite only requirements its node owns`` () =
    let foreign = "kemiller2002/time-entry-state-machine/requirements-reconstruction/fsharp-business-requirements-beyond-csharp.md#L104"
    let records = recordsV2 () |> withStopPoints (List.map (fun p -> if p.Point = "Rounding tie" then { p with RequirementKeys = [ foreign ] } else p))
    match generateDefaultPlan (prepared ()) records ConstructionVocabulary.Version3 with
    | Ok _ -> failwith "a decision point cited a requirement its node does not own"
    | Error refusals ->
        Assert.Contains(RequirementNotOwnedByNode("IFC-TIMER-PORTS", "port:transitionEffect/Stop/point:Rounding tie", foreign), refusals)

[<Fact>]
let ``vocabulary 3 default plans are deterministic`` () =
    Assert.Equal(encodeDefaultPlan (planV3 ()), encodeDefaultPlan (planV3 ()))
    Assert.Equal((planV3 ()).Digest, (planV3 ()).Digest)

// ---- compile-negative: an incomplete plan cannot pass as complete ----

[<Fact>]
let ``ordinary code cannot represent an open plan as complete`` () =
    let preamble =
        """
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionClassification
let consume (plan: CompletePlan) = plan.Plan
"""
    let failures =
        FsiProbe.check [ typeof<CompletePlan>.Assembly.Location ]
            [ "positive control", preamble + "let f (p: ValidatedPlan) = complete p |> Result.map consume", None
              "validated plan passed as complete", preamble + "let f (p: ValidatedPlan) = consume p", Some "FS0001"
              "complete plan constructed directly", preamble + "let f (p: ValidatedPlan) c = consume (CompletePlan(p, c))", Some "FS1093"
              "classification marked complete", preamble + "let f (c: Classification) = { c with Complete = true }", Some "FS1129" ]
    Assert.True(List.isEmpty failures, String.concat "\n\n" failures)
