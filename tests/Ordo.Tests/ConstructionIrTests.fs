/// Adversarial contract tests for the first ECIR semantic authority.
module Ordo.Tests.ConstructionIrTests

open Xunit
open Ordo.Core.ConstructionIr

let private source key originalId =
    { Key = key
      OriginalId = originalId
      Document = "requirements/domain.md"
      Location = "line 20"
      Revision = "v1"
      ContentDigest = "sha256:content" }

let private a = source "domain/R-001" "R-001"
let private b = source "domain/R-002" "R-002"
let private manifest = { Digest = "sha256:manifest"; Requirements = [ a; b ] }

let private mapping src disposition nodes =
    { Source = src; Disposition = disposition; NodeIds = nodes }

let private node id kind keys =
    { Id = id
      Kind = kind
      RequirementKeys = keys
      DependsOn = []
      Justification = None }

let private blueprint =
    { SchemaVersion = SchemaVersion
      SourceManifestDigest = manifest.Digest
      Requirements =
        [ mapping a Modeled [ "DEC"; "VERIFY"; "COHORT" ]
          mapping b (Deferred "out of this release") [ "DEF" ] ]
      Nodes =
        [ node "DEC" Decision [ a.Key ]
          node "VERIFY" VerificationObligation [ a.Key ]
          node "COHORT" Cohort [ a.Key ]
          node "DEF" Deferral [ b.Key ] ] }

let private contains error actual = Assert.True(List.contains error actual, sprintf "expected %A; got %A" error actual)

[<Fact>]
let ``complete two-source blueprint is valid and only approved decisions are runnable`` () =
    Assert.Empty(validate manifest blueprint)
    Assert.Empty(validateCohort manifest blueprint "COHORT" (Set.ofList [ "DEC" ]))

[<Fact>]
let ``the intake is independent and detects a dropped source requirement`` () =
    let missing = { blueprint with Requirements = [ List.head blueprint.Requirements ] }
    contains (MissingRequirement b.Key) (validate manifest missing)

[<Fact>]
let ``blueprint must not fabricate source requirements to hide omissions`` () =
    let invented = source "domain/R-999" "R-999"
    let extra = { blueprint with Requirements = blueprint.Requirements @ [ mapping invented (Rejected "not in spec") [] ] }
    contains (InventedRequirement invented.Key) (validate manifest extra)

[<Fact>]
let ``the same qualified source may not be counted twice`` () =
    let duplicate = { blueprint with Requirements = blueprint.Requirements @ [ List.head blueprint.Requirements ] }
    contains (DuplicateBlueprintRequirement a.Key) (validate manifest duplicate)

[<Fact>]
let ``a requirement revision or source content cannot silently change`` () =
    let changed = { a with ContentDigest = "sha256:another-content" }
    let rows = mapping changed Modeled [ "DEC"; "VERIFY"; "COHORT" ] :: (List.tail blueprint.Requirements)
    contains (ChangedRequirement a.Key) (validate manifest { blueprint with Requirements = rows })

[<Fact>]
let ``two documents may use the same local source ID without collapsing`` () =
    let other = { b with OriginalId = a.OriginalId; Document = "other.md" }
    let separate = { manifest with Requirements = [ a; other ] }
    let rows = [ List.head blueprint.Requirements; mapping other (Deferred "out of scope") [ "DEF" ] ]
    Assert.Empty(validate separate { blueprint with Requirements = rows })

[<Fact>]
let ``both directions of each requirement-node edge must agree`` () =
    let changedNodes =
        blueprint.Nodes
        |> List.map (fun n -> if n.Id = "VERIFY" then { n with RequirementKeys = [] } else n)

    let broken = { blueprint with Nodes = changedNodes }
    contains (NonReciprocalLink(a.Key, "VERIFY")) (validate manifest broken)
    contains (UnjustifiedNode "VERIFY") (validate manifest broken)

[<Fact>]
let ``an apparently modeled requirement without a verification obligation is refused`` () =
    let row = mapping a Modeled [ "DEC"; "COHORT" ]
    let nodes = blueprint.Nodes |> List.filter (fun n -> n.Id <> "VERIFY")
    let broken = { blueprint with Requirements = row :: List.tail blueprint.Requirements; Nodes = nodes }
    contains (MissingVerification a.Key) (validate manifest broken)

[<Fact>]
let ``a modeled requirement with verification but no construction cohort is not buildable`` () =
    let mapped = mapping a Modeled [ "DEC"; "VERIFY" ]
    let nodes = blueprint.Nodes |> List.filter (fun n -> n.Id <> "COHORT")
    let broken = { blueprint with Requirements = mapped :: List.tail blueprint.Requirements; Nodes = nodes }
    contains (MissingConstructionCohort a.Key) (validate manifest broken)

[<Fact>]
let ``an unresolved requirement is represented but cannot execute in its cohort`` () =
    let changedB = mapping b (Unresolved "conflicting source directives") [ "CONFLICT"; "COHORT" ]
    let nodes =
        blueprint.Nodes
        |> List.filter (fun n -> n.Id <> "DEF")
        |> List.map (fun n ->
            if n.Id = "COHORT" then { n with RequirementKeys = [ a.Key; b.Key ] }
            else n)
    let broken =
        { blueprint with
            Requirements = [ List.head blueprint.Requirements; changedB ]
            Nodes = nodes @ [ node "CONFLICT" Conflict [ b.Key ] ] }
    Assert.Empty(validate manifest broken)
    contains (BlockedRequirement b.Key) (validateCohort manifest broken "COHORT" (Set.ofList [ "DEC" ]))

[<Fact>]
let ``model-written decisions are never their own approval receipts`` () =
    Assert.Empty(validate manifest blueprint)
    contains (MissingDecisionAuthorization "DEC") (validateCohort manifest blueprint "COHORT" Set.empty)

[<Fact>]
let ``unapproved and invalid artifacts may not be dispatched`` () =
    let changed = { blueprint with SourceManifestDigest = "sha256:stale" }
    contains SourceDigestMismatch (validate manifest changed)
    contains (InvalidBlueprint SourceDigestMismatch) (validateCohort manifest changed "COHORT" Set.empty)

[<Fact>]
let ``the validator refuses dangling, self and cyclic dependencies`` () =
    let nodes =
        blueprint.Nodes
        |> List.map (fun n ->
            if n.Id = "VERIFY" then { n with DependsOn = [ "UNKNOWN"; "DEC" ] }
            elif n.Id = "DEC" then { n with DependsOn = [ "VERIFY" ] }
            else n)
    let errors = validate manifest { blueprint with Nodes = nodes }
    contains (MissingDependency("VERIFY", "UNKNOWN")) errors
    contains (DependencyCycle "DEC") errors
    contains (DependencyCycle "VERIFY") errors

    let selfNodes =
        blueprint.Nodes
        |> List.map (fun n -> if n.Id = "COHORT" then { n with DependsOn = [ "COHORT" ] } else n)
    contains (SelfDependency "COHORT") (validate manifest { blueprint with Nodes = selfNodes })

[<Fact>]
let ``blank deferred reason, bogus supersession, and unlinked nodes fail closed`` () =
    let row = mapping b (Deferred "   ") [ "DEF" ]
    contains (MissingDispositionReason b.Key)
        (validate manifest { blueprint with Requirements = [ List.head blueprint.Requirements; row ] })

    let row = mapping b (Superseded(b.Key, "same requirement")) [ "DEF" ]
    contains (InvalidSupersession(b.Key, b.Key))
        (validate manifest { blueprint with Requirements = [ List.head blueprint.Requirements; row ] })

    let bogus = node "UNOWNED" Contract []
    contains (UnjustifiedNode "UNOWNED") (validate manifest { blueprint with Nodes = blueprint.Nodes @ [ bogus ] })

[<Fact>]
let ``same initial source content with distinct identities still exposes collisions`` () =
    let badManifest = { manifest with Requirements = [ a; a ] }
    contains (DuplicateManifestRequirement a.Key) (validate badManifest blueprint)

[<Fact>]
let ``empty intake and unsupported future schema cannot appear successful`` () =
    contains EmptyIntake (validate { manifest with Requirements = [] } blueprint)
    contains (UnsupportedSchema "ecir/2") (validate manifest { blueprint with SchemaVersion = "ecir/2" })

let private dependsOnRoot dependency =
    { blueprint with
        Nodes =
            blueprint.Nodes |> List.map (fun n ->
                if n.Id = "COHORT" then { n with DependsOn = [ dependency ] } else n) }

[<Fact>]
let ``a deferred dependency cannot hide outside the selected cohort`` () =
    let candidate = dependsOnRoot "DEF"
    Assert.Empty(validate manifest candidate)
    contains (BlockedRequirement b.Key) (validateCohort manifest candidate "COHORT" (Set.singleton "DEC"))

[<Fact>]
let ``a transitive unresolved dependency blocks construction`` () =
    let row = mapping b (Unresolved "pending design") [ "CONFLICT" ]
    let candidate =
        { blueprint with
            Requirements = [ List.head blueprint.Requirements; row ]
            Nodes =
                blueprint.Nodes |> List.map (fun n ->
                    if n.Id = "COHORT" then { n with DependsOn = [ "DEC" ] }
                    elif n.Id = "DEC" then { n with DependsOn = [ "CONFLICT" ] }
                    elif n.Id = "DEF" then node "CONFLICT" Conflict [ b.Key ]
                    else n) }
    Assert.Empty(validate manifest candidate)
    contains (BlockedRequirement b.Key) (validateCohort manifest candidate "COHORT" (Set.singleton "DEC"))

let private crossRequirementDecision =
    { blueprint with
        Requirements = [ List.head blueprint.Requirements; mapping b Modeled [ "D2"; "V2"; "C2" ] ]
        Nodes =
            (dependsOnRoot "D2").Nodes |> List.filter (fun n -> n.Id <> "DEF")
            |> fun nodes -> nodes @ [ node "D2" Decision [ b.Key ]; node "V2" VerificationObligation [ b.Key ]; node "C2" Cohort [ b.Key ] ] }

[<Fact>]
let ``an explicit cross-requirement decision needs external authorization`` () =
    Assert.Empty(validate manifest crossRequirementDecision)
    contains (MissingDecisionAuthorization "D2") (validateCohort manifest crossRequirementDecision "COHORT" (Set.singleton "DEC"))
    Assert.Empty(validateCohort manifest crossRequirementDecision "COHORT" (Set.ofList [ "DEC"; "D2" ]))

[<Fact>]
let ``dependencies of the cohorts verification obligations cannot be ignored`` () =
    let candidate =
        { blueprint with
            Nodes =
                blueprint.Nodes |> List.map (fun n ->
                    if n.Id = "VERIFY" then { n with DependsOn = [ "DEF" ] } else n) }
    Assert.Empty(validate manifest candidate)
    contains (BlockedRequirement b.Key) (validateCohort manifest candidate "COHORT" (Set.singleton "DEC"))

[<Fact>]
let ``unrelated deferred requirements remain represented without blocking ready work`` () =
    Assert.Empty(validateCohort manifest blueprint "COHORT" (Set.singleton "DEC"))

[<Fact>]
let ``preparation produces only exact semantically ready snapshots`` () =
    match prepareCohort manifest crossRequirementDecision "COHORT" (Set.ofList [ "DEC"; "D2" ]) with
    | Error errors -> failwithf "ready blueprint refused: %A" errors
    | Ok prepared ->
        let view = preparedCohortView prepared
        Assert.True((view.Manifest = manifest && view.Blueprint = crossRequirementDecision), "snapshot lost its exact input")
        Assert.True((view.CohortId = "COHORT"), "wrong cohort")
        Assert.True((view.AssignedRequirementKeys = Set.singleton a.Key), "dependencies were promoted to assigned work")
        Assert.True((view.RequiredRequirementKeys = Set.ofList [ a.Key; b.Key ]), "required source scope lost")
        Assert.True((view.RequiredDecisionIds = Set.ofList [ "DEC"; "D2" ]), "required decision scope lost")
        Assert.True(not (Set.contains "C2" view.RequiredNodeIds), "unreferenced sibling cohort was pulled into execution scope")

[<Fact>]
let ``preparation cannot manufacture success for blocked unapproved or malformed inputs`` () =
    for candidate, cohort, approved in
        [ dependsOnRoot "DEF", "COHORT", Set.singleton "DEC"
          crossRequirementDecision, "COHORT", Set.singleton "DEC"
          blueprint, "MISSING", Set.singleton "DEC"
          { blueprint with SourceManifestDigest = "stale" }, "COHORT", Set.singleton "DEC" ] do
        Assert.True(Result.isError (prepareCohort manifest candidate cohort approved), "invalid input produced a prepared cohort")

[<Fact>]
let ``all nonmodeled dependency dispositions refuse while unrelated cohorts stay outside scope`` () =
    for disposition, kind in [ Deferred "later", Deferral; Unresolved "pending", Conflict; Rejected "declined", EngineeringRationale; Superseded(a.Key, "replaced"), EngineeringRationale ] do
        let candidate =
            { blueprint with
                Requirements = [ List.head blueprint.Requirements; mapping b disposition [ "DEF" ] ]
                Nodes = (dependsOnRoot "DEF").Nodes |> List.map (fun n -> if n.Id = "DEF" then { n with Kind = kind } else n) }
        Assert.Empty(validate manifest candidate)
        contains (BlockedRequirement b.Key) (validateCohort manifest candidate "COHORT" (Set.singleton "DEC"))

[<Fact>]
let ``dependent cohort obligations and diamond dependencies are checked once`` () =
    let candidate =
        { crossRequirementDecision with
            Nodes = crossRequirementDecision.Nodes |> List.map (fun n ->
                if n.Id = "COHORT" then { n with DependsOn = [ "C2"; "D2" ] }
                elif n.Id = "V2" then { n with DependsOn = [ "DEC" ] }
                else n) }
    Assert.Empty(validate manifest candidate)
    let errors = validateCohort manifest candidate "COHORT" Set.empty
    Assert.True((errors = [ MissingDecisionAuthorization "D2"; MissingDecisionAuthorization "DEC" ]), "dependency closure omitted or duplicated approval")
    Assert.Empty(validateCohort manifest candidate "COHORT" (Set.ofList [ "DEC"; "D2" ]))
