/// Loads the checked-in construction fixtures through the same strict
/// readers and gates a host would use.
module Ordo.Construction.Tests.Fixtures

open System
open System.IO
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionPlanWire

let repositoryRoot =
    let rec find (current: DirectoryInfo) =
        if File.Exists(Path.Combine(current.FullName, "ros.json")) then current.FullName
        else
            match current.Parent with
            | null -> failwith "repository root not found"
            | parent -> find parent
    find (DirectoryInfo AppContext.BaseDirectory)

let ok label value = match value with Ok x -> x | Error e -> failwithf "%s: %A" label e

type Fixture =
    { /// The golden directory under Golden/.
      Name: string
      /// The source fixture directory under tests/fixtures/construction/.
      Source: string
      /// The plan file in that directory.
      Plan: string
      /// The blueprint file in that directory.
      Blueprint: string
      Cohort: string
      /// Stands in for the external decision authority. A test input only.
      Approved: Set<string> }

let timer =
    { Name = "time-entry-timer"; Source = "time-entry-timer"; Plan = "plan.json"; Blueprint = "blueprint.json"
      Cohort = "COH-TIMER"; Approved = Set.singleton "DEC-TIMER-STATES" }

let door =
    { Name = "door-synthetic"; Source = "door-synthetic"; Plan = "plan.json"; Blueprint = "blueprint.json"
      Cohort = "COH-DOOR"; Approved = Set.singleton "DEC-DOOR" }

/// Vocabulary 2 variants of the timer plan, one per effect-failure
/// disposition of the Stop effect. Same sources and cohort. A decided
/// disposition must cite a required decision, so `remain` and `proceed` use a
/// test-only blueprint variant that adds DEC-TIMER-STOP-FAILURE, approved
/// here by a test input; `unresolved` uses the original blueprint.
let private timerV2 disposition =
    { timer with Name = "time-entry-timer-v2-" + disposition; Plan = sprintf "plan.v2-%s.json" disposition }

let private decided disposition =
    { timerV2 disposition with
        Blueprint = sprintf "blueprint.v2-%s.json" disposition
        Approved = Set.ofList [ "DEC-TIMER-STATES"; "DEC-TIMER-STOP-FAILURE" ] }

let timerRemain = decided "remain"
let timerProceed = decided "proceed"
let timerUnresolved = timerV2 "unresolved"

let timerV2Fixtures = [ timerRemain; timerProceed; timerUnresolved ]

/// Vocabulary 3 (CONSTRUCT-010): the timer default plan generated from
/// `construction-decisions-v2.json` with unlisted pairs left undecided and the
/// workflow renamed TimerOpen, so the rounding tie and every unlisted pair
/// are open decisions in generated code.
let timerOpen = { timer with Name = "time-entry-timer-v3-open"; Plan = "plan.v3-open.json" }

let private read (fixture: Fixture) name =
    File.ReadAllText(Path.Combine(repositoryRoot, "tests", "fixtures", "construction", fixture.Source, name))

let prepared fixture =
    let manifest = readManifest (read fixture "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read fixture fixture.Blueprint) |> ok "blueprint"
    prepareCohort manifest blueprint fixture.Cohort fixture.Approved |> ok "prepare"

let draft fixture = readPlan (read fixture fixture.Plan) |> ok "plan"

let validated fixture = validate (prepared fixture) (draft fixture) |> ok "validate"

let construction fixture = Ordo.Construction.Construction.construct (validated fixture) |> ok "construct"

let goldenPath (fixture: Fixture) (path: string) =
    Path.Combine(repositoryRoot, "tests", "Ordo.Construction.Tests", "Golden", fixture.Name, path)
