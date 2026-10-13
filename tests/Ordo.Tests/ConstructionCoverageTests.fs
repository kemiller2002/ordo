/// CONSTRUCT-010 phase 7: the pre-execution coverage report and advisory
/// route recommendation, over the real timer cohort and the frozen inputs of
/// the three other slices CONSTRUCT-008 measured.
module Ordo.Tests.ConstructionCoverageTests

open System
open System.IO
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionDecisions
open Ordo.Core.ConstructionDefaultPlan
open Ordo.Core.ConstructionCoverage

let private constructionFixtures () =
    let rec find (current: DirectoryInfo) =
        let candidate = Path.Combine(current.FullName, "tests", "fixtures", "construction")
        if Directory.Exists candidate then candidate
        else
            match current.Parent with
            | null -> failwith "construction fixtures missing from repository"
            | parent -> find parent
    find (DirectoryInfo AppContext.BaseDirectory)

let private ok label value = match value with Ok x -> x | Error e -> failwithf "%s: %A" label e

type private Slice =
    { Directory: string
      Decisions: string
      Cohort: string
      /// Stands in for the operator approval recorded with the slice. A test input.
      Decision: string }

let private timerSlice = { Directory = "time-entry-timer"; Decisions = "construction-decisions.json"; Cohort = "COH-TIMER"; Decision = "DEC-TIMER-STATES" }
let private summa = { Directory = "coverage-calibration/summa"; Decisions = "construction-decisions.json"; Cohort = "COH-SUMMA-DELIVERY"; Decision = "DEC-SUMMA-ATTEMPT-STATES" }
let private chrona = { Directory = "coverage-calibration/chrona"; Decisions = "construction-decisions.json"; Cohort = "COH-CHRONA-TIMER"; Decision = "DEC-TIMER-LIFECYCLE" }
let private expanded = { Directory = "coverage-calibration/expanded"; Decisions = "construction-decisions.json"; Cohort = "COH-TIME-ENTRY-WEEK"; Decision = "DEC-ENTRY-LIFECYCLE" }

let private load slice decisions vocabulary =
    let read name = File.ReadAllText(Path.Combine(constructionFixtures (), slice.Directory, name))
    let manifest = readManifest (read "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read "blueprint.json") |> ok "blueprint"
    let prepared = prepareCohort manifest blueprint slice.Cohort (Set.singleton slice.Decision) |> ok "prepare"
    let records = readDecisionRecords (read decisions) |> ok "decisions"
    prepared, generateDefaultPlan prepared records vocabulary |> ok "default plan"

let private reportOf slice =
    let prepared, plan = load slice slice.Decisions ConstructionVocabulary.Version2
    report prepared plan

let private keyStop = "kemiller2002/time-entry-state-machine/docs/architecture/business-activity-ledger-state-model.md#L131-L144"
let private keyShort = "kemiller2002/time-entry-state-machine/requirements-reconstruction/fsharp-business-requirements-beyond-csharp.md#L113-L114"

let private statusOf (r: CoverageReport) key = (r.Rows |> List.find (fun row -> row.Key = key)).Status

[<Fact>]
let ``every source requirement appears exactly once and the totals add up`` () =
    for slice in [ timerSlice; summa; chrona; expanded ] do
        let r = reportOf slice
        let t = r.Totals
        Assert.Equal(r.Rows.Length, r.Rows |> List.map _.Key |> List.distinct |> List.length)
        Assert.Equal(t.SourceRequirements, t.FullyGenerated + t.PartiallyGenerated + t.NeedsExtension + t.Unresolved + t.Unsupported + t.OutsideCohort)
        Assert.Equal(t.AssignedRequirements, t.SourceRequirements - t.OutsideCohort)

[<Fact>]
let ``the expanded slice reproduces the measured 6 of 46 generated requirements`` () =
    let t = (reportOf expanded).Totals
    Assert.Equal(46, t.AssignedRequirements)
    Assert.Equal(6, t.FullyGenerated + t.PartiallyGenerated)

[<Fact>]
let ``a requirement outside the cohort is reported, never counted as generated`` () =
    let r = reportOf timerSlice
    Assert.Equal(RequirementStatus.OutsideCohort, statusOf r keyShort)
    Assert.Empty((r.Rows |> List.find (fun row -> row.Key = keyShort)).GeneratedBy)

[<Fact>]
let ``an extension with an open decision point is unresolved, not merely an extension`` () =
    let prepared, v3 = load timerSlice "construction-decisions-v2.json" ConstructionVocabulary.Version3
    let r = report prepared v3
    Assert.Equal(RequirementStatus.Unresolved, statusOf r keyStop)
    let stop = r.Extensions |> List.find (fun e -> e.Contract = "ITimerPorts.Perform/Stop")
    Assert.Equal(4, stop.DecisionPoints)
    Assert.Equal(1, stop.OpenDecisionPoints)
    // 1 contract + 1 requirement + 4 points + 2 for an effect.
    Assert.Equal(8, stop.Points)

[<Fact>]
let ``the calibration's quoted ratios are reproducible from the frozen slices`` () =
    let ratio slice = (reportOf slice).Recommendation.StructureRatio
    Assert.Equal(0.7692, ratio timerSlice)
    Assert.Equal(0.4783, ratio summa)
    Assert.Equal(0.3913, ratio chrona)
    Assert.Equal(0.1304, ratio expanded)
    for evidence, value in [ "ratio 0.77", ratio timerSlice; "ratio 0.48", ratio summa; "ratio 0.39", ratio chrona; "ratio 0.13", ratio expanded ] do
        Assert.Contains(measuredCalibration.Evidence, fun e -> e.Contains evidence)
        Assert.Equal(evidence, sprintf "ratio %.2f" value)

[<Fact>]
let ``the recommendation matches every measured outcome it was fitted to, and says it was fitted`` () =
    Assert.Equal(Route.C, (reportOf timerSlice).Recommendation.Route)
    Assert.Equal(Route.C, (reportOf summa).Recommendation.Route)
    Assert.Equal(Route.APlus, (reportOf chrona).Recommendation.Route)
    Assert.Equal(Route.APlus, (reportOf expanded).Recommendation.Route)
    Assert.Contains((reportOf summa).Recommendation.Uncertainty, fun u -> u.Contains "fitted to four slices")

[<Fact>]
let ``the requirement share alone does not separate the measured slices`` () =
    // Summa (C cheaper) has a lower generated share than Chrona (C dearer);
    // a share threshold would have mispredicted one of them.
    Assert.True((reportOf summa).Recommendation.GeneratedShare < (reportOf chrona).Recommendation.GeneratedShare)

[<Fact>]
let ``every recommendation is advisory and states its uncertainty`` () =
    for slice in [ timerSlice; summa; chrona; expanded ] do
        let r = (reportOf slice).Recommendation
        Assert.True(r.Advisory)
        Assert.NotEmpty(r.Uncertainty)
    let near = (reportOf summa).Recommendation
    Assert.Contains(near.Uncertainty, fun u -> u.Contains "within")
    let b = recommendWithoutPlan [ MissingStateModel ]
    Assert.Equal(Route.B, b.Route)
    Assert.True(b.Advisory)

[<Fact>]
let ``open decisions are counted, and every route must escalate them`` () =
    let r = reportOf summa
    Assert.Equal(35, r.Totals.UnresolvedDecisions)
    Assert.Contains(r.Recommendation.Uncertainty, fun u -> u.Contains "35 decisions are open")

[<Fact>]
let ``the report is deterministic and canonical`` () =
    let a = reportOf chrona
    let b = reportOf chrona
    Assert.Equal(a, b)
    Assert.Equal(encodeCoverageReport a, encodeCoverageReport b)
    Assert.Equal(digest a, digest b)
    let text = encodeCoverageReport a
    Assert.Contains("\"schemaVersion\": \"ordo.coverage-report/1\"", text)
    Assert.Contains("\"advisory\": true", text)
    Assert.Contains("\"route\": \"A+\"", text)
