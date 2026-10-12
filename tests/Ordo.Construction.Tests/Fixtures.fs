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
    { Name: string
      Cohort: string
      /// Stands in for the external decision authority. A test input only.
      Approved: Set<string> }

let timer = { Name = "time-entry-timer"; Cohort = "COH-TIMER"; Approved = Set.singleton "DEC-TIMER-STATES" }
let door = { Name = "door-synthetic"; Cohort = "COH-DOOR"; Approved = Set.singleton "DEC-DOOR" }

let private read (fixture: Fixture) name =
    File.ReadAllText(Path.Combine(repositoryRoot, "tests", "fixtures", "construction", fixture.Name, name))

let prepared fixture =
    let manifest = readManifest (read fixture "source-manifest.json") |> ok "manifest"
    let blueprint = readBlueprint (read fixture "blueprint.json") |> ok "blueprint"
    prepareCohort manifest blueprint fixture.Cohort fixture.Approved |> ok "prepare"

let draft fixture = readPlan (read fixture "plan.json") |> ok "plan"

let validated fixture = validate (prepared fixture) (draft fixture) |> ok "validate"

let construction fixture = Ordo.Construction.Construction.construct (validated fixture) |> ok "construct"

let goldenPath (fixture: Fixture) (path: string) =
    Path.Combine(repositoryRoot, "tests", "Ordo.Construction.Tests", "Golden", fixture.Name, path)
