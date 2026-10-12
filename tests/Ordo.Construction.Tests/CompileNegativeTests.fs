/// Compile-negative proof, run through the real F# compiler against the built
/// assemblies and the golden generated files. Each failing probe must fail
/// with the named error; each group has a positive control that must compile.
module Ordo.Construction.Tests.CompileNegativeTests

open System.IO
open Xunit
open Ordo.Construction.Tests.Fixtures

let private references =
    [ typeof<Ordo.Core.ConstructionPlan.ValidatedPlan>.Assembly.Location
      typeof<Ordo.Construction.Construction.PlannedWrite>.Assembly.Location ]

let private load (fixture: Fixture) (files: string list) =
    files |> List.map (fun f -> sprintf "#load @\"%s\"" (goldenPath fixture f)) |> String.concat "\n"

let private doorFile name = sprintf "generated/Door/Door.%s.g.fs" name
let private doorTiers = load door [ doorFile "Model"; doorFile "Transition"; doorFile "Orchestration" ]

let private engine =
    """
open Ordo.Core.ConstructionPlan
open Ordo.Construction.Construction
"""

[<Fact>]
let ``the engine accepts only a validated plan`` () =
    let failures =
        Ordo.Tests.FsiProbe.check references
            [ "positive control", engine + "let f (p: ValidatedPlan) = construct p", None
              "draft given to the engine", engine + "let f (d: PlanDraft) = construct d", Some "FS0001"
              "view given to the engine", engine + "let f (v: ValidatedPlanView) = construct v", Some "FS0001"
              "workflow given to the engine", engine + "let f (w: Workflow) = construct w", Some "FS0001"
              "internal emitter reached around validation", engine + "let f (w: Workflow) = Ordo.Construction.Emit.shapeOf w", Some "FS1092" ]
    Assert.True(List.isEmpty failures, String.concat "\n\n" failures)

[<Fact>]
let ``missing handwritten behavior cannot compile into a permissive default`` () =
    let failures =
        Ordo.Tests.FsiProbe.check references
            [ "positive control: all tiers in order with complete rules",
              doorTiers + "\nopen Generated.Door\nlet rules = { Guard = (fun _ _ _ -> false); Invariant = (fun _ _ -> false) }\nlet r = DoorTransition.decide rules Set.empty DoorState.Closed DoorCommand.Open", None
              "guard implementation omitted",
              doorTiers + "\nopen Generated.Door\nlet rules : DoorRules = { Invariant = (fun _ _ -> true) }", Some "FS0764"
              "decide called without rules",
              doorTiers + "\nopen Generated.Door\nlet r = DoorTransition.decide Set.empty DoorState.Closed DoorCommand.Open", Some "FS0001"
              "host omits a required port",
              doorTiers + "\nopen Generated.Door\nlet host = { new IDoorPorts with member _.CurrentTime() = System.DateTimeOffset.MinValue }", Some "FS0366" ]
    Assert.True(List.isEmpty failures, String.concat "\n\n" failures)

[<Fact>]
let ``a lower tier cannot be compiled against a higher one`` () =
    let failures =
        Ordo.Tests.FsiProbe.check references
            [ "positive control: downward order", load door [ doorFile "Model"; doorFile "Transition" ], None
              "transition tier before the semantic model", load door [ doorFile "Transition"; doorFile "Model" ], Some "FS0039"
              "orchestration tier without the transition tier", load door [ doorFile "Model"; doorFile "Orchestration" ], Some "FS0039" ]
    Assert.True(List.isEmpty failures, String.concat "\n\n" failures)
