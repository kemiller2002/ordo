/// A typed decision→conflict link: an authorized decision's
/// `conflictResolution` record settles blueprint conflicts, so they are no
/// longer emitted as open questions. The frozen expanded slice has no
/// decision node for its accepted idempotency decisions, so these tests add
/// one as a test input; it is not an approval and the fixture is unchanged.
module Ordo.Tests.ConstructionConflictResolutionTests

open System
open System.IO
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionDecisions
open Ordo.Core.ConstructionDefaultPlan
open Ordo.Core.ConstructionLedger

let private directory () =
    let rec find (current: DirectoryInfo) =
        let candidate = Path.Combine(current.FullName, "tests", "fixtures", "construction", "coverage-calibration", "expanded")
        if Directory.Exists candidate then candidate
        else
            match current.Parent with
            | null -> failwith "expanded slice fixture missing from repository"
            | parent -> find parent
    find (DirectoryInfo AppContext.BaseDirectory)

let private read name = File.ReadAllText(Path.Combine(directory (), name))
let private ok label value = match value with Ok x -> x | Error e -> failwithf "%s: %A" label e

let private o8 = "CONFLICT-O8-OPERATION-ID-REUSE"
let private o13 = "CONFLICT-O13-IDEMPOTENCY-NOT-PORTED"
let private o5 = "CONFLICT-O5-MIDNIGHT-END"
let private lifecycle = "DEC-ENTRY-LIFECYCLE"
let private settling = "DEC-TEST-OPERATION-ID"

let private manifest () = readManifest (read "source-manifest.json") |> ok "manifest"
let private original () = readBlueprint (read "blueprint.json") |> ok "blueprint"
let private node (b: Blueprint) id = b.Nodes |> List.find (fun n -> n.Id = id)
let private o8Key () = (node (original ()) o8).RequirementKeys |> List.exactlyOne

/// The slice plus one decision node owning O8's requirement, linked both ways.
let private blueprint () =
    let b = original ()
    let key = o8Key ()
    { b with
        Nodes = b.Nodes @ [ { Id = settling; Kind = Decision; RequirementKeys = [ key ]; DependsOn = []; Justification = Some "test input" } ]
        Requirements = b.Requirements |> List.map (fun r -> if r.Source.Key = key then { r with NodeIds = r.NodeIds @ [ settling ] } else r) }

let private prepared (b: Blueprint) approved =
    prepareCohort (manifest ()) b "COH-TIME-ENTRY-WEEK" approved |> ok "prepare"

let private records (b: Blueprint) extra =
    let base' = readDecisionRecords (read "construction-decisions.json") |> ok "decisions"
    { base' with BlueprintDigest = blueprintDigest b; Records = base'.Records @ extra }

let private resolution conflicts keys = { NodeId = settling; Content = ConflictResolution { Conflicts = conflicts; RequirementKeys = keys } }
let private approved = set [ lifecycle; settling ]

let private planWith extra =
    let b = blueprint ()
    generateDefaultPlan (prepared b approved) (records b extra) ConstructionVocabulary.Version2

/// The slice as frozen: no settling decision node.
let private baseline () =
    let b = original ()
    generateDefaultPlan (prepared b (set [ lifecycle ])) (records b []) ConstructionVocabulary.Version2

let private conflictsIn (plan: DefaultPlan) =
    plan.Obligations |> List.filter (fun o -> o.Kind = TypedObligationKind.Conflict) |> List.collect _.NodeIds |> Set.ofList

[<Fact>]
let ``without a resolution, the conflicts stay open questions`` () =
    let plan = baseline () |> ok "plan"
    Assert.Contains(o8, conflictsIn plan)
    Assert.Contains(o13, conflictsIn plan)
    Assert.Empty(plan.ResolvedConflicts)

[<Fact>]
let ``an authorized resolution removes exactly the conflicts it names`` () =
    let before = baseline () |> ok "before"
    let after = planWith [ resolution [ o8 ] [ o8Key () ] ] |> ok "after"
    Assert.DoesNotContain(o8, conflictsIn after)
    Assert.Contains(o13, conflictsIn after)
    Assert.Equal<Set<string>>(Set.remove o8 (conflictsIn before), conflictsIn after)
    Assert.Equal<(string * string list) list>([ o8, [ settling ] ], after.ResolvedConflicts)
    Assert.Contains("\"resolvedConflicts\"", encodeDefaultPlan after)
    Assert.DoesNotContain("\"resolvedConflicts\"", encodeDefaultPlan before)

[<Fact>]
let ``the coverage report counts one fewer open decision`` () =
    let count (b: Blueprint) approvedIds extra =
        let p = prepared b approvedIds
        let plan = generateDefaultPlan p (records b extra) ConstructionVocabulary.Version2 |> ok "plan"
        (Ordo.Core.ConstructionCoverage.report p plan).Totals.UnresolvedDecisions
    Assert.Equal(count (original ()) (set [ lifecycle ]) [] - 1, count (blueprint ()) approved [ resolution [ o8 ] [ o8Key () ] ])

[<Fact>]
let ``the ledger records the deciding node as an approval and stops calling the conflict open`` () =
    let b = blueprint ()
    let key = o8Key ()
    let rowFor extra =
        let plan = generateDefaultPlan (prepared b approved) (records b extra) ConstructionVocabulary.Version2 |> ok "plan"
        let ledger = buildLedger (manifest ()) b [ plan ] Map.empty noTags |> ok "ledger"
        ledger.Rows |> List.find (fun r -> r.Requirement.Key = key)
    let partial = rowFor [ resolution [ o8 ] [ key ] ]
    Assert.Contains(settling, partial.ApprovedDecisionIds)
    Assert.Contains(ResolutionClass.AlreadyApproved, partial.Classes)
    // O13 still links the same requirement, so it is still a domain question.
    Assert.Contains(ResolutionClass.GenuineDomainDecision, partial.Classes)
    let both = rowFor [ resolution [ o8; o13 ] [ key ] ]
    Assert.DoesNotContain(both.OpenObligationIds, fun id -> id.Contains "conflict")
    Assert.True(both.OpenObligationIds.Length < partial.OpenObligationIds.Length)

[<Fact>]
let ``with every conflict settled, the conflicts no longer make the requirement a domain question`` () =
    // Every conflicted requirement on this slice is also uncovered. The
    // operator tag (a test input) says the requirement text fixes it, which
    // takes the uncovered question out, so only the conflicts remain.
    let b = blueprint ()
    let key = o8Key ()
    let tags = { noTags with FixedByRequirement = Set.singleton key }
    let classesFor extra =
        let plan = generateDefaultPlan (prepared b approved) (records b extra) ConstructionVocabulary.Version2 |> ok "plan"
        let ledger = buildLedger (manifest ()) b [ plan ] Map.empty tags |> ok "ledger"
        (ledger.Rows |> List.find (fun r -> r.Requirement.Key = key)).Classes
    Assert.Contains(ResolutionClass.GenuineDomainDecision, classesFor [ resolution [ o8 ] [ key ] ])
    Assert.DoesNotContain(ResolutionClass.GenuineDomainDecision, classesFor [ resolution [ o8; o13 ] [ key ] ])

[<Fact>]
let ``a resolution must name real conflicts it shares a requirement with`` () =
    let refused extra expected =
        match planWith extra with
        | Error refusals -> Assert.Contains(expected, refusals)
        | Ok _ -> failwithf "accepted %A" extra
    refused [ resolution [ "CONFLICT-NOPE" ] [ o8Key () ] ] (ResolvesUnknownNode(settling, "CONFLICT-NOPE"))
    refused [ resolution [ lifecycle ] [ o8Key () ] ] (ResolvesNonConflict(settling, lifecycle, "decision"))
    refused [ resolution [ o5 ] [ o8Key () ] ] (UnrelatedConflictResolution(settling, o5))

[<Fact>]
let ``a resolution cites only requirements its node owns`` () =
    let foreign = (node (original ()) o5).RequirementKeys |> List.head
    match planWith [ resolution [ o8 ] [ o8Key (); foreign ] ] with
    | Error refusals -> Assert.Contains(refusals, function RequirementNotOwnedByNode(n, _, k) -> n = settling && k = foreign | _ -> false)
    | Ok _ -> failwith "a resolution cited a requirement its node does not own"

[<Fact>]
let ``a node may hold structure and a resolution, but not two resolutions`` () =
    // A record on a decision outside the cohort's authorized scope is refused
    // by the same node check as every other content kind (ConstructionDefaultPlanTests).
    // The same node may hold structure and a resolution, but not two resolutions.
    match planWith [ resolution [ o8 ] [ o8Key () ]; resolution [ o13 ] [ o8Key () ] ] with
    | Error refusals -> Assert.Contains(DuplicateRecord settling, refusals)
    | Ok _ -> failwith "two resolutions on one node were accepted"
    // The lifecycle decision holds the state model; it may also settle a
    // conflict, provided it owns the requirement it cites.
    let b3 = original ()
    let o5Keys = (node b3 o5).RequirementKeys
    let owned = (node b3 lifecycle).RequirementKeys |> Set.ofList
    let withBoth =
        records b3 [ { NodeId = lifecycle; Content = ConflictResolution { Conflicts = [ o5 ]; RequirementKeys = o5Keys } } ]
    match generateDefaultPlan (prepared b3 (set [ lifecycle ])) withBoth ConstructionVocabulary.Version2, o5Keys |> List.forall (fun k -> Set.contains k owned) with
    | Ok plan, true -> Assert.DoesNotContain(o5, conflictsIn plan)
    | Error refusals, false ->
        Assert.Contains(refusals, function RequirementNotOwnedByNode(n, _, _) -> n = lifecycle | _ -> false)
        Assert.DoesNotContain(DuplicateRecord lifecycle, refusals)
    | outcome, ownsAll -> failwithf "owns all keys: %b; got %A" ownsAll outcome

[<Fact>]
let ``the reader accepts the kind in both schema versions and round-trips it canonically`` () =
    for schema in [ DecisionsSchemaVersion; DecisionsSchemaVersion2 ] do
        let text =
            sprintf """{"schemaVersion":"%s","blueprintDigest":"sha256:%s","cohortId":"C","records":[{"nodeId":"D","content":{"kind":"conflictResolution","conflicts":["B","A"],"requirementKeys":["k"]}}]}"""
                schema (String.replicate 64 "0")
        let r = readDecisionRecords text |> ok "read"
        Assert.Equal(ConflictResolution { Conflicts = [ "B"; "A" ]; RequirementKeys = [ "k" ] }, (List.exactlyOne r.Records).Content)
        let again = readDecisionRecords (encodeDecisionRecords r) |> ok "reread"
        Assert.Equal(digest r, digest again)
        let text = encodeDecisionRecords r
        Assert.True(text.IndexOf("\"A\"") < text.IndexOf("\"B\""), "conflicts are sorted in canonical form")
    let empty = """{"schemaVersion":"ordo.construction-decisions/1","blueprintDigest":"x","cohortId":"C","records":[{"nodeId":"D","content":{"kind":"conflictResolution","conflicts":[],"requirementKeys":["k"]}}]}"""
    match readDecisionRecords empty with
    | Error e -> Assert.Contains("at least one conflict", e)
    | Ok _ -> failwith "an empty resolution was accepted"
