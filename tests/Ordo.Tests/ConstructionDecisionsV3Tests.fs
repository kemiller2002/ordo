/// CONSTRUCT-010 phase 2, increment 1b: decision records version 3 generate
/// vocabulary 4 default plans with typed refusal codes and branching
/// transitions. The fixture is the synthetic gate; its sources are test
/// input, not product requirements.
module Ordo.Tests.ConstructionDecisionsV3Tests

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

let private directory () =
    let rec find (current: DirectoryInfo) =
        let candidate = Path.Combine(current.FullName, "tests", "fixtures", "construction", "gate-synthetic")
        if Directory.Exists candidate then candidate
        else
            match current.Parent with
            | null -> failwith "gate fixture missing from repository"
            | parent -> find parent
    find (DirectoryInfo AppContext.BaseDirectory)

let private read name = File.ReadAllText(Path.Combine(directory (), name))
let private ok label value = match value with Ok x -> x | Error e -> failwithf "%s: %A" label e

let private prepared () =
    let manifest = readManifest (read "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read "blueprint.json") |> ok "blueprint"
    prepareCohort manifest blueprint "COH-GATE" (Set.singleton "DEC-GATE") |> ok "prepare"

let private records () = readDecisionRecords (read "construction-decisions-v3.json") |> ok "decisions"
let private generate r = generateDefaultPlan (prepared ()) r ConstructionVocabulary.Version4
let private plan () = generate (records ()) |> ok "plan"

let private withModel (f: StateModelContent -> StateModelContent) (r: ApprovedDecisionRecords) =
    { r with Records = r.Records |> List.map (fun x -> match x.Content with StateModel m -> { x with Content = StateModel(f m) } | _ -> x) }

let private refusedWith (predicate: DefaultPlanRefusal -> bool) (result: Result<DefaultPlan, DefaultPlanRefusal list>) =
    match result with
    | Error refusals -> Assert.Contains(refusals, fun r -> predicate r)
    | Ok _ -> failwith "the records were accepted"

let private gate n = sprintf "synthetic/gate.md#GATE-%d" n

[<Fact>]
let ``version 3 records generate a vocabulary 4 plan with codes and a branching transition`` () =
    let p = plan ()
    let w = (view p.ValidatedPlan).Workflow
    Assert.Equal(VocabularyVersion4, (view p.ValidatedPlan).Binding.VocabularyVersion)
    Assert.Equal<string list>([ "AlreadyOpen"; "NotAccepted" ], w.RefusalCodes |> List.map (fst >> _.Key))
    Assert.Equal(2, w.CodeAssignments.Length)
    let b = List.exactlyOne w.BranchingTransitions
    Assert.Equal<string list>([ "Jammed"; "Open" ], b.Targets |> List.map _.Key)
    Assert.Contains(w.Inventories, fun i -> i.Extension = ExtensionTarget.Selection(b.InState, b.OnCommand))

[<Fact>]
let ``a decided selector is handed off as an implement obligation on its contract`` () =
    let p = plan ()
    let selector = p.Obligations |> List.filter (fun o -> o.ExtensionSurface = [ "GateRules.Select/Closed/Raise" ])
    Assert.Equal(TypedObligationKind.ExtensionBehavior, (List.exactlyOne selector).Kind)
    Assert.Equal<string list>([ gate 1; gate 2 ], (List.exactlyOne selector).RequirementKeys)

[<Fact>]
let ``an undecided selector point is a decide obligation, never an implement one`` () =
    let r =
        records ()
        |> withModel (fun m ->
            { m with
                Branching =
                    m.Branching |> List.map (fun b -> { b with DecisionPoints = b.DecisionPoints |> List.map (fun p -> { p with Question = Some "Which motor report means jammed?" }) }) })
    let p = generate r |> ok "plan"
    let selector = p.Obligations |> List.filter (fun o -> o.ExtensionSurface = [ "GateRules.Select/Closed/Raise" ])
    Assert.Equal<TypedObligationKind list>([ TypedObligationKind.UndecidedExtensionDecision ], selector |> List.map _.Kind)
    match complete p.ValidatedPlan with
    | Error open' -> Assert.Contains(open', fun c -> c.Subject = "extension:selection/Closed/Raise/point:MotorReport")
    | Ok _ -> failwith "an undecided selector point was treated as complete"

[<Fact>]
let ``what the records do not construct is still an open obligation`` () =
    // GATE-3 (the path must be clear) needs a guard; default plans have none.
    let p = plan ()
    Assert.Contains(p.Obligations, fun o -> o.Kind = TypedObligationKind.UncoveredRequirement && o.RequirementKeys = [ gate 3 ])

[<Fact>]
let ``records and vocabulary must match`` () =
    refusedWith (function DecisionSchemaVocabularyMismatch(DecisionsSchemaVersion3, VocabularyVersion3) -> true | _ -> false)
        (generateDefaultPlan (prepared ()) (records ()) ConstructionVocabulary.Version3)
    let v2 = { records () with SchemaVersion = DecisionsSchemaVersion2 }
    refusedWith (function DecisionSchemaVocabularyMismatch(DecisionsSchemaVersion2, VocabularyVersion4) -> true | _ -> false) (generate v2)

[<Fact>]
let ``version 2 records cannot carry version 3 members`` () =
    let text = (read "construction-decisions-v3.json").Replace(DecisionsSchemaVersion3, DecisionsSchemaVersion2)
    match readDecisionRecords text with
    | Error e -> Assert.Contains("refusalCodes", e)
    | Ok _ -> failwith "version 2 accepted version 3 members"

[<Fact>]
let ``branching is checked against the state model`` () =
    let replace f = records () |> withModel (fun m -> { m with Branching = m.Branching |> List.map f })
    refusedWith (function TransitionUnknownState("DEC-GATE", "branching:Closed/Raise", "Broken") -> true | _ -> false)
        (generate (replace (fun b -> { b with Targets = [ "Open"; "Broken" ] })))
    let both = records () |> withModel (fun m -> { m with Transitions = m.Transitions @ [ { Command = "Raise"; From = "Closed"; To = "Open"; RequirementKeys = [ gate 1 ] } ] })
    refusedWith (function ConflictingRecordedTransitions("DEC-GATE", "Closed", "Raise", _) -> true | _ -> false) (generate both)
    // One target is not a branch; plan validation refuses it.
    refusedWith (function GeneratedPlanInvalid vs -> List.contains (BranchingNeedsTwoTargets "branching:Closed/Raise") vs | _ -> false)
        (generate (replace (fun b -> { b with Targets = [ "Open"; "open" ] })))

[<Fact>]
let ``codes are cited, owned and given only to refusals`` () =
    let r = records () |> withModel (fun m -> { m with RefusalCodes = m.RefusalCodes |> List.map (fun c -> { c with RequirementKeys = [] }) })
    refusedWith (function MissingRequirementCitation("DEC-GATE", "code:AlreadyOpen") -> true | _ -> false) (generate r)
    let legal =
        records ()
        |> withModel (fun m ->
            { m with CodeAssignments = m.CodeAssignments @ [ { Target = RecordedCodeTarget.Pair("Jammed", "Reset"); Code = "NotAccepted"; RequirementKeys = [ gate 4 ] } ] })
    refusedWith (function GeneratedPlanInvalid vs -> List.contains (CodeOnNonRefusal "codeFor:pair/Jammed/Reset") vs | _ -> false) (generate legal)

[<Fact>]
let ``version 3 records are canonical and deterministic`` () =
    let r = records ()
    let again = readDecisionRecords (encodeDecisionRecords r) |> ok "reread"
    Assert.Equal(ConstructionDecisions.digest r, ConstructionDecisions.digest again)
    Assert.Equal(encodeDefaultPlan (plan ()), encodeDefaultPlan (plan ()))
    Assert.Equal((plan ()).Digest, (generate again |> ok "again").Digest)

[<Fact>]
let ``the coverage report counts the branching transition and its selector`` () =
    let report = Ordo.Core.ConstructionCoverage.report (prepared ()) (plan ())
    // 3 states, 2 transitions, 1 branching transition, 1 refusal.
    Assert.Equal(7, report.Totals.GeneratedConstructs)
    Assert.Contains(report.Extensions, fun e -> e.Contract = "GateRules.Select/Closed/Raise" && e.DecisionPoints = 1)
