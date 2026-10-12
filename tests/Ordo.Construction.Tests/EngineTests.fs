/// The engine's own guarantees: naming policy, ownership, tier direction,
/// absence of injected or effectful code, and absence of effects in the
/// engine itself.
module Ordo.Construction.Tests.EngineTests

open System
open System.IO
open System.Reflection
open System.Reflection.Metadata
open System.Reflection.PortableExecutable
open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionPlan
open Ordo.Construction.Artifacts
open Ordo.Construction.Naming
open Ordo.Construction.Construction
open Ordo.Construction.Tests.Fixtures

let private constructWith (rename: OperationDraft -> OperationDraft) =
    let d = draft door
    validate (prepared door) { d with Operations = d.Operations |> List.map rename } |> ok "validate" |> construct

let private renameState (from: string) (target: string) op =
    let swap (raw: string) = if raw = from then target else raw
    match op with
    | DefineState s -> DefineState { s with Name = swap s.Name }
    | DefineTransition t -> DefineTransition { t with From = swap t.From; To = swap t.To }
    | DefineInvariant i -> DefineInvariant { i with HoldsIn = i.HoldsIn |> List.map swap }
    | other -> other

// ---- naming policy ----

[<Fact>]
let ``names F# reserves or generates members for are refused, not mangled`` () =
    for reserved in [ "Tag"; "None"; "Some"; "Error"; "Item" ] do
        match constructWith (renameState "Open" reserved) with
        | Error refusals -> Assert.Contains(Naming(ReservedName("state", reserved)), refusals)
        | Ok _ -> failwithf "%s was generated" reserved

[<Fact>]
let ``a name that would collide with a generated tester is refused`` () =
    match constructWith (renameState "Open" "Is Open") with
    | Error refusals -> Assert.Contains(Naming(TesterCollision("state", "IsOpen")), refusals)
    | Ok _ -> failwith "IsOpen was generated"
    let guardRenamed op =
        match op with
        | DefineTransition t -> DefineTransition { t with Guards = t.Guards |> List.map (fun g -> { g with Name = "None" }) }
        | other -> other
    match constructWith guardRenamed with
    | Error refusals -> Assert.Contains(Naming(ReservedName("guard", "None")), refusals)
    | Ok _ -> failwith "a guard named None was generated"

[<Fact>]
let ``the engine, not the plan, chooses every generated name and path`` () =
    let c = construction timer
    Assert.Equal("generated/Timer/", c.OwnershipRoot)
    Assert.Equal<string list>(
        [ "generated/Timer/Timer.Model.g.fs"; "generated/Timer/Timer.Transition.g.fs"
          "generated/Timer/Timer.Orchestration.g.fs"; "generated/Timer/construction-manifest.json" ],
        c.Artifacts |> List.map _.Path)
    Assert.Equal<Tier option list>([ Some SemanticModel; Some Transition; Some Orchestration; None ], c.Artifacts |> List.map _.Tier)

// ---- justified tiers only ----

[<Fact>]
let ``no orchestration tier is generated for a plan that declares no ports`` () =
    let d = draft timer
    let noPorts = d.Operations |> List.filter (function DeclarePort _ -> false | _ -> true)
    let paused = (preparedCohortView (prepared timer)).AssignedRequirementKeys |> Set.toList |> List.find (fun k -> k.EndsWith "#L105-L107")
    let completion = (preparedCohortView (prepared timer)).AssignedRequirementKeys |> Set.toList |> List.find (fun k -> k.EndsWith "#L131-L144")
    // The two requirements the ports carried stay covered, explicitly undecided.
    let kept =
        noPorts
        @ [ DeclareUnresolved { Subject = "Elapsed time"; Question = "How segments are timed without a clock port is undecided."; Trace = { RequirementKeys = [ paused ]; NodeIds = [ "IFC-TIMER-PORTS" ] } } ]
        |> List.map (function
            | DeclareUnresolved u when u.Subject = "Stop completion refused" -> DeclareUnresolved { u with Trace = { u.Trace with RequirementKeys = [ completion ] } }
            | other -> other)
    let c = validate (prepared timer) { d with Operations = kept } |> ok "validate" |> construct |> ok "construct"
    Assert.Equal<Tier option list>([ Some SemanticModel; Some Transition; None ], c.Artifacts |> List.map _.Tier)
    Assert.DoesNotContain(c.ExtensionContracts, fun x -> x.Kind = CurrentTimePort || x.Kind = EffectPort)

// ---- ownership ----

[<Fact>]
let ``every artifact stays inside its workflow's ownership root`` () =
    for fixture in [ timer; door ] do
        let c = construction fixture
        for a in c.Artifacts do
            Assert.StartsWith(c.OwnershipRoot, a.Path)
            Assert.DoesNotContain("..", a.Path)
            Assert.False(Path.IsPathRooted a.Path, a.Path)
            Assert.Equal(Generated, a.Ownership)
        Assert.All(c.ExtensionContracts, fun x -> Assert.Equal(Handwritten, x.Ownership))

[<Fact>]
let ``write planning never overwrites or removes handwritten files`` () =
    let c = construction timer
    let model = c.Artifacts |> List.head
    Assert.Equal<PlannedWrite list>(c.Artifacts |> List.map (fun a -> Create a.Path), planWrites Map.empty c |> ok "fresh")
    let regenerated = c.Artifacts |> List.map (fun a -> a.Path, a.Content) |> Map.ofList
    Assert.Equal<PlannedWrite list>(c.Artifacts |> List.map (fun a -> Unchanged a.Path), planWrites regenerated c |> ok "same")
    let older = Map.add model.Path (GeneratedMarker + "/1 from sha256:older>\n") regenerated
    Assert.Contains(Replace model.Path, planWrites older c |> ok "replace")
    let stale = Map.add "generated/Timer/Timer.Retired.g.fs" (GeneratedMarker + "/1 from sha256:older>\n") regenerated
    Assert.Contains(RemoveStale "generated/Timer/Timer.Retired.g.fs", planWrites stale c |> ok "stale")
    let handwritten = Map.add model.Path "module MyTimer\nlet keep = 1\n" Map.empty
    Assert.Equal(Error [ WouldOverwriteHandwritten model.Path ], planWrites handwritten c)
    let intruder = Map.add "generated/Timer/Notes.fs" "let mine = 2\n" Map.empty
    Assert.Equal(Error [ HandwrittenInGeneratedRoot "generated/Timer/Notes.fs" ], planWrites intruder c)
    let outside = Map.add "src/Program.fs" "let main = 0\n" Map.empty
    Assert.Equal<PlannedWrite list>(c.Artifacts |> List.map (fun a -> Create a.Path), planWrites outside c |> ok "outside root untouched")

// ---- tier direction and injected code ----

let private content (c: Construction) (tier: Tier) =
    c.Artifacts |> List.find (fun a -> a.Tier = Some tier) |> _.Content

[<Fact>]
let ``no lower tier refers to a higher tier`` () =
    for fixture, wf in [ timer, "Timer"; door, "Door" ] do
        let c = construction fixture
        let model, transition, orchestration = content c SemanticModel, content c Transition, content c Orchestration
        for higher in [ wf + "Transition"; wf + "Refusal"; wf + "Decision"; wf + "Rules"; "I" + wf + "Ports"; wf + "Orchestration"; wf + "Result"; wf + "EffectOutcome" ] do
            Assert.DoesNotContain(higher, model)
        for higher in [ "I" + wf + "Ports"; wf + "Orchestration"; wf + "Result"; wf + "EffectOutcome"; "DateTimeOffset" ] do
            Assert.DoesNotContain(higher, transition)
        Assert.Contains(wf + "Transition.decide", orchestration)

[<Fact>]
let ``generated code contains no effect, escape hatch or permissive default`` () =
    let forbidden =
        [ "open "; "System.IO"; "System.Net"; "Process"; "Environment"; "Console"; "printf"; "#r"; "#load"
          "DllImport"; "extern"; "Reflection"; "Unchecked"; "failwith"; "raise"; "mutable"; "<-"
          "_ ->"; "fun _ -> true"; "Task"; "async"; "Thread" ]
    for fixture in [ timer; door ] do
        for a in (construction fixture).Artifacts |> List.filter (fun a -> a.Tier.IsSome) do
            for token in forbidden do
                Assert.False(a.Content.Contains token, sprintf "%s contains %s" a.Path token)

[<Fact>]
let ``requirement text, questions and trace keys never reach generated code`` () =
    let c = construction timer
    let v = view (validated timer)
    for a in c.Artifacts |> List.filter (fun a -> a.Tier.IsSome) do
        for key in v.AssignedRequirementKeys do Assert.DoesNotContain(key, a.Content)
        for u in v.Workflow.Unresolved do Assert.DoesNotContain(u.Question, a.Content)
        Assert.DoesNotContain(v.Binding.CohortId, a.Content)

// ---- the engine performs no effects ----

let private constructionAssembly = typeof<PlannedWrite>.Assembly

[<Fact>]
let ``the engine assembly references no file, network, process or console API`` () =
    let path = constructionAssembly.Location
    use stream = File.OpenRead path
    use pe = new PEReader(stream)
    let metadata = pe.GetMetadataReader()
    let referenced =
        metadata.TypeReferences
        |> Seq.map (fun handle ->
            let t = metadata.GetTypeReference handle
            metadata.GetString t.Namespace + "." + metadata.GetString t.Name)
        |> Set.ofSeq
    let forbidden (name: string) =
        [ "System.IO."; "System.Net."; "System.Diagnostics.Process"; "System.Console"; "System.Environment"
          "System.Reflection.Emit."; "System.Runtime.InteropServices.DllImport"; "System.Threading.Thread" ]
        |> List.exists (fun prefix -> name.StartsWith prefix)
    let offending = referenced |> Set.filter forbidden
    Assert.True(Set.isEmpty offending, sprintf "engine references effectful APIs: %A" (Set.toList offending))

[<Fact>]
let ``Ordo.Construction depends on Ordo.Core and the framework only, and Ordo.Core does not depend on it`` () =
    let names (assembly: Assembly) =
        assembly.GetReferencedAssemblies() |> Array.choose (fun n -> Option.ofObj n.Name) |> Set.ofArray
    let refs = names constructionAssembly
    Assert.Contains("Ordo.Core", refs)
    let unexpected = refs |> Set.filter (fun n -> not (n = "Ordo.Core" || n = "FSharp.Core" || n = "netstandard" || n.StartsWith "System"))
    Assert.True(Set.isEmpty unexpected, sprintf "unexpected dependencies: %A" unexpected)
    Assert.DoesNotContain("Ordo.Construction", names typeof<ValidatedPlan>.Assembly)

[<Fact>]
let ``no public engine entry point accepts unvalidated plan data`` () =
    let unvalidated = [ typeof<PlanDraft>; typeof<ValidatedPlanView>; typeof<Workflow> ]
    let offending =
        constructionAssembly.GetExportedTypes()
        |> Array.collect (fun t -> t.GetMethods(BindingFlags.Public ||| BindingFlags.Static))
        |> Array.filter (fun m -> m.GetParameters() |> Array.exists (fun p -> List.contains p.ParameterType unvalidated))
        |> Array.map (fun m -> (match m.DeclaringType with null -> "" | t -> t.Name) + "." + m.Name)
    Assert.Empty(offending)
    let construct = constructionAssembly.GetExportedTypes() |> Array.pick (fun t -> Option.ofObj (t.GetMethod "construct"))
    Assert.Equal<Type list>([ typeof<ValidatedPlan> ], construct.GetParameters() |> Array.map _.ParameterType |> Array.toList)

/// Hosts pin the engine by release version (GH-69): the project is packaged as
/// EchelonFoundry.Ordo.Construction and depends on nothing but Ordo.Core, so
/// the package can carry no other dependency.
[<Fact>]
let ``Ordo.Construction is packaged as EchelonFoundry.Ordo.Construction with only Ordo.Core as a dependency`` () =
    let rec root (dir: DirectoryInfo) =
        if File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")) then dir.FullName
        else
            match dir.Parent with
            | null -> failwith "repository root not found"
            | parent -> root parent

    let project =
        File.ReadAllText(Path.Combine(root (DirectoryInfo(AppContext.BaseDirectory)), "src", "Ordo.Construction", "Ordo.Construction.fsproj"))

    Assert.Contains("<IsPackable>true</IsPackable>", project)
    Assert.Contains("<PackageId>EchelonFoundry.Ordo.Construction</PackageId>", project)
    Assert.DoesNotContain("<PackageReference", project)
    let references = project.Split("<ProjectReference", StringSplitOptions.None) |> Array.tail
    Assert.Equal(1, references.Length)
    Assert.Contains("../Ordo.Core/Ordo.Core.fsproj", references[0])
