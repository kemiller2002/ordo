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
      Cohort: string
      /// Stands in for the external decision authority. A test input only.
      Approved: Set<string> }

let timer =
    { Name = "time-entry-timer"; Source = "time-entry-timer"; Plan = "plan.json"
      Cohort = "COH-TIMER"; Approved = Set.singleton "DEC-TIMER-STATES" }

let door =
    { Name = "door-synthetic"; Source = "door-synthetic"; Plan = "plan.json"
      Cohort = "COH-DOOR"; Approved = Set.singleton "DEC-DOOR" }

/// Vocabulary 2 variants of the timer plan, one per effect-failure
/// disposition of the Stop effect. Same sources, blueprint and cohort.
let private timerV2 disposition =
    { timer with Name = "time-entry-timer-v2-" + disposition; Plan = sprintf "plan.v2-%s.json" disposition }

let timerRemain = timerV2 "remain"
let timerProceed = timerV2 "proceed"
let timerUnresolved = timerV2 "unresolved"

let timerV2Fixtures = [ timerRemain; timerProceed; timerUnresolved ]

let private read (fixture: Fixture) name =
    File.ReadAllText(Path.Combine(repositoryRoot, "tests", "fixtures", "construction", fixture.Source, name))

let prepared fixture =
    let manifest = readManifest (read fixture "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read fixture "blueprint.json") |> ok "blueprint"
    prepareCohort manifest blueprint fixture.Cohort fixture.Approved |> ok "prepare"

let draft fixture = readPlan (read fixture fixture.Plan) |> ok "plan"

let validated fixture = validate (prepared fixture) (draft fixture) |> ok "validate"

let construction fixture = Ordo.Construction.Construction.construct (validated fixture) |> ok "construct"

let goldenPath (fixture: Fixture) (path: string) =
    Path.Combine(repositoryRoot, "tests", "Ordo.Construction.Tests", "Golden", fixture.Name, path)
