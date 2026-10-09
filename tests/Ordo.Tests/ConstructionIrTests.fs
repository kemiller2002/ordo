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
