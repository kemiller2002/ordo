module Ordo.Tests.ConstructionIrWireTests

open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire

let private source id doc =
    { Key = doc + "#" + id
      OriginalId = id
      Document = doc
      Location = "L12"
      Revision = "v1"
      ContentDigest = "sha256:one-content" }

let private r1 = source "R-001" "requirements/one.md"
let private r2 = source "R-001" "requirements/two.md"

let private manifest =
    { Requirements = [ r1; r2 ]
      Digest = manifestDigest [ r1; r2 ] }

let private modeled =
    { Source = r1
      Disposition = Modeled
      NodeIds = [ "COHORT"; "DECISION"; "VERIFY" ] }

let private deferred =
    { Source = r2
      Disposition = Deferred "release scope"
      NodeIds = [ "DEFERRAL" ] }

let private node id kind requirements depends =
    { Id = id
      Kind = kind
      RequirementKeys = requirements
      DependsOn = depends
      Justification = None }

let private blueprint =
    { SchemaVersion = SchemaVersion
      SourceManifestDigest = manifest.Digest
      Requirements = [ deferred; modeled ]
      Nodes =
        [ node "VERIFY" VerificationObligation [ r1.Key ] [ "DECISION" ]
          node "DEFERRAL" Deferral [ r2.Key ] []
          node "COHORT" Cohort [ r1.Key ] [ "DECISION" ]
          node "DECISION" Decision [ r1.Key ] [] ] }

let private expectOk value =
    match value with Ok x -> x | Error errors -> failwithf "%A" errors

[<Fact>]
let ``manifest and blueprint round-trip through strict ECIR wire`` () =
    Assert.Equal(manifest, readManifest (encodeManifest manifest) |> expectOk)
    Assert.Equal(blueprint, readBlueprint (encodeBlueprint blueprint) |> expectOk)
    let digest = validatePinned manifest blueprint |> expectOk
    Assert.StartsWith("sha256:", digest)
    Assert.Equal(digest, blueprintDigest blueprint)

[<Fact>]
let ``canonical blueprint digest ignores object and reference order`` () =
    let reversed =
        { blueprint with
            Requirements = List.rev blueprint.Requirements
            Nodes =
                blueprint.Nodes
                |> List.rev
                |> List.map (fun n ->
                    { n with RequirementKeys = List.rev n.RequirementKeys
                             DependsOn = List.rev n.DependsOn }) }
    Assert.Equal(blueprintDigest blueprint, blueprintDigest reversed)

[<Fact>]
let ``canonical manifest digest ignores record ordering but detects mutation`` () =
    Assert.Equal(manifest.Digest, manifestDigest [ r2; r1 ])
    Assert.NotEqual<string>(manifest.Digest, manifestDigest [ { r1 with Location = "L13" }; r2 ])
    Assert.NotEqual<string>(manifest.Digest, manifestDigest [ { r1 with ContentDigest = "sha256:changed" }; r2 ])

[<Fact>]
let ``ECIR refuses unverified source digest even when blueprint agrees with its lie`` () =
    let fake = { manifest with Digest = "sha256:lie" }
    let fakeBlueprint = { blueprint with SourceManifestDigest = fake.Digest }
    Assert.True(validatePinned fake fakeBlueprint |> Result.isError)

[<Fact>]
let ``ECIR refuses dropped requirement even if blueprint says it has coverage`` () =
    let dropped = { blueprint with Requirements = [ modeled ] }
    match validatePinned manifest dropped with
    | Error xs -> Assert.Contains(xs, fun message -> message.Contains("MissingRequirement"))
    | Ok _ -> failwith "source omission was accepted"

[<Fact>]
let ``ECIR refuses unknown attributes and duplicate JSON keys`` () =
    Assert.True(readManifest """{"digest":"a","digest":"b","requirements":[]}""" |> Result.isError)
    Assert.True(readManifest """{"digest":"a","requirements":[],"ignored":2}""" |> Result.isError)
    Assert.True(readBlueprint """{"schemaVersion":"ecir/9","sourceManifestDigest":"a","requirements":[],"nodes":[]}""" |> Result.isError)

[<Fact>]
let ``ECIR distinguishes two same-named IDs from different documents`` () =
    Assert.Equal(r1.OriginalId, r2.OriginalId)
    Assert.NotEqual<string>(r1.Key, r2.Key)
    Assert.Equal(2, (readManifest (encodeManifest manifest) |> expectOk).Requirements.Length)

[<Fact>]
let ``execution cannot proceed without external decision authorization`` () =
    Assert.True(validatePinnedCohort manifest blueprint "COHORT" Set.empty |> Result.isError)
    Assert.Equal(blueprintDigest blueprint,
                 validatePinnedCohort manifest blueprint "COHORT" (Set.singleton "DECISION") |> expectOk)

[<Fact>]
let ``invalid disposition shape is refused before semantics`` () =
    let wrong = encodeBlueprint blueprint
                |> fun s -> s.Replace("\"kind\": \"modeled\"", "\"kind\": \"modeled\", \"reason\": \"not allowed\"")
    Assert.True(readBlueprint wrong |> Result.isError)
