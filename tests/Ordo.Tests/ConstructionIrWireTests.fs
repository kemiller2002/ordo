module Ordo.Tests.ConstructionIrWireTests

open Xunit
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire
open Ordo.Core.ConstructibilityContract

let private source id doc =
    { Key = doc + "#" + id
      OriginalId = id
      Document = doc
      Location = "L12"
      Revision = "v1"
      ContentDigest = "sha256:" + System.String('c', 64) }

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
let ``ECIR rejects non-cryptographic placeholder hashes even when internally consistent`` () =
    let forgedRequirement = { r1 with ContentDigest = "sha256:not-a-real-digest" }
    let forgedManifest =
        { Requirements = [ forgedRequirement; r2 ]
          Digest = manifestDigest [ forgedRequirement; r2 ] }
    let forgedBlueprint =
        { blueprint with
            SourceManifestDigest = forgedManifest.Digest
            Requirements =
                blueprint.Requirements
                |> List.map (fun row ->
                    if row.Source.Key = r1.Key then { row with Source = forgedRequirement }
                    else row) }
    Assert.True(validatePinned forgedManifest forgedBlueprint |> Result.isError)

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

[<Fact>]
let ``scaffold is source-complete, deterministic and cannot be executed before analysis`` () =
    let initial = scaffold manifest |> expectOk
    Assert.Equal(2, initial.Requirements.Length)
    Assert.Equal(2, initial.Nodes.Length)
    Assert.Empty(validate manifest initial)
    Assert.True(initial.Requirements |> List.forall (fun r ->
        match r.Disposition with Unresolved _ -> true | _ -> false))
    Assert.True(initial.Nodes |> List.forall (fun n -> n.Kind = Conflict))
    Assert.True(validatePinnedCohort manifest initial "MISSING" Set.empty |> Result.isError)
    Assert.Equal(blueprintDigest initial, scaffold { manifest with Requirements = [ r2; r1 ] } |> expectOk |> blueprintDigest)

[<Fact>]
let ``scaffold rejects tampered, empty and duplicated independent input`` () =
    let forged = { manifest with Digest = "sha256:" + System.String('a', 64) }
    Assert.True(scaffold forged |> Result.isError)
    let empty =
        { Requirements = []
          Digest = manifestDigest [] }
    Assert.True(scaffold empty |> Result.isError)
    let duplicated =
        { Requirements = [ r1; r1 ]
          Digest = manifestDigest [ r1; r1 ] }
    Assert.True(scaffold duplicated |> Result.isError)

// ---- GH-59 hardening ----

[<Fact>]
let ``a host-pinned blueprint digest refuses a stale or substituted blueprint`` () =
    let pinned = blueprintDigest blueprint
    Assert.True(verifyBlueprintDigest pinned blueprint |> Result.isOk)
    let substituted = { blueprint with Requirements = [ { deferred with Disposition = Deferred "another reason" }; modeled ] }
    Assert.True(verifyBlueprintDigest pinned substituted |> Result.isError)
    Assert.True(verifyBlueprintDigest "sha256:not-hex" blueprint |> Result.isError)

[<Fact>]
let ``canonical blueprint bytes are stable across repeated encoding`` () =
    let once = canonicalBlueprint blueprint
    Assert.Equal(once, canonicalBlueprint (readBlueprint (encodeBlueprint blueprint) |> expectOk))
    Assert.Equal(once, canonicalBlueprint blueprint)

[<Fact>]
let ``every disposition variant round-trips through the strict reader`` () =
    for disposition in [ Modeled; Deferred "d"; Unresolved "u"; Rejected "r"; Superseded(r1.Key, "s") ] do
        let candidate = { blueprint with Requirements = [ { deferred with Disposition = disposition }; modeled ] }
        Assert.Equal(candidate, readBlueprint (encodeBlueprint candidate) |> expectOk)

[<Fact>]
let ``unknown node kinds, unknown dispositions, wrong types and blank strings are refused`` () =
    let text = encodeBlueprint blueprint
    for mutated in
        [ text.Replace("\"kind\": \"deferral\"", "\"kind\": \"generatedCode\"")
          text.Replace("\"kind\": \"deferred\"", "\"kind\": \"approved\"")
          text.Replace("\"dependsOn\": []", "\"dependsOn\": \"DECISION\"")
          text.Replace("\"reason\": \"release scope\"", "\"reason\": \"  \"")
          text.Replace("\"id\": \"DEFERRAL\"", "\"id\": 7") ] do
        Assert.NotEqual<string>(text, mutated)
        Assert.True(readBlueprint mutated |> Result.isError, mutated)

[<Fact>]
let ``constructibility contract publishes exact version negotiation and no implicit migrations`` () =
    Assert.Equal(Supported SchemaVersion, negotiateVersion SchemaVersion)
    Assert.Equal(Unsupported("ecir/2", [ SchemaVersion ]), negotiateVersion "ecir/2")
    Assert.Empty automaticMigrations

    let future = encodeBlueprint blueprint |> fun json -> json.Replace("\"ecir/1\"", "\"ecir/2\"")
    match readAndPrepare (encodeManifest manifest) future (blueprintDigest blueprint) "COHORT" (Set.singleton "DECISION") with
    | Ok _ -> failwith "an unsupported schema was accepted"
    | Error failure ->
        Assert.Equal<string list>([ "ORDO.CONTRACT.UNSUPPORTED_SCHEMA" ], failure.Diagnostics |> List.map _.Code)

[<Fact>]
let ``constructibility intake binds every authorization to a real in-scope authorizable node`` () =
    let pinned = blueprintDigest blueprint
    let codes result =
        match result with
        | Ok _ -> failwith "invalid authorization was accepted"
        | Error failure -> failure.Diagnostics |> List.map _.Code

    Assert.Contains("ORDO.CONTRACT.UNKNOWN_AUTHORIZATION",
                    prepare manifest blueprint pinned "COHORT" (Set.ofList [ "DECISION"; "UNKNOWN" ]) |> codes)
    Assert.Contains("ORDO.CONTRACT.AUTHORIZATION_KIND_MISMATCH",
                    prepare manifest blueprint pinned "COHORT" (Set.ofList [ "DECISION"; "VERIFY" ]) |> codes)

    let outsideDecision = node "DEC-OUT" Decision [ r2.Key ] []
    let outside =
        { blueprint with
            Requirements =
                blueprint.Requirements
                |> List.map (fun row ->
                    if row.Source.Key = r2.Key then { row with NodeIds = row.NodeIds @ [ outsideDecision.Id ] }
                    else row)
            Nodes = blueprint.Nodes @ [ outsideDecision ] }
    Assert.Empty(validate manifest outside)
    Assert.Contains("ORDO.CONTRACT.AUTHORIZATION_OUTSIDE_COHORT",
                    prepare manifest outside (blueprintDigest outside) "COHORT" (Set.ofList [ "DECISION"; "DEC-OUT" ]) |> codes)

[<Fact>]
let ``constructibility view is deterministic and links requirements to ownership decisions and verification`` () =
    let prepared = prepare manifest blueprint (blueprintDigest blueprint) "COHORT" (Set.singleton "DECISION") |> expectOk
    let view = contractView prepared
    Assert.Equal(blueprintDigest blueprint, view.ContractDigest)
    Assert.Equal(encodeContractView view, encodeContractView (contractView prepared))

    let modeledContract = view.Requirements |> List.find (fun requirement -> requirement.RequirementKey = r1.Key)
    Assert.Equal("modeled", modeledContract.Disposition)
    Assert.Equal<string list>([ "COHORT"; "DECISION"; "VERIFY" ], modeledContract.NodeIds)

    let verification = view.Nodes |> List.find (fun node -> node.NodeId = "VERIFY")
    Assert.Equal("verificationObligation", verification.Kind)
    Assert.Equal<string list>([ "DECISION" ], verification.DependsOn)

    let modeledTrace = view.Traceability |> List.find (fun trace -> trace.RequirementKey = r1.Key)
    Assert.True(modeledTrace.RequiredByCohort)
    Assert.Equal<string list>([ "COHORT" ], modeledTrace.OwnerCohortIds)
    Assert.Equal<string list>([ "DECISION" ], modeledTrace.DecisionIds)
    Assert.Equal<string list>([ "VERIFY" ], modeledTrace.VerificationObligationIds)

    let deferredTrace = view.Traceability |> List.find (fun trace -> trace.RequirementKey = r2.Key)
    Assert.False(deferredTrace.RequiredByCohort)
    Assert.Empty(deferredTrace.VerificationObligationIds)

[<Fact>]
let ``constructibility diagnostics have a stable machine-readable envelope`` () =
    let result = readAndPrepare (encodeManifest manifest) (encodeBlueprint blueprint)
                                (blueprintDigest blueprint) "COHORT" Set.empty
    match result with
    | Ok _ -> failwith "missing decision authorization was accepted"
    | Error failure ->
        Assert.Equal(DiagnosticsSchemaVersion, failure.SchemaVersion)
        Assert.Contains(failure.Diagnostics, fun item -> item.Code = "ORDO.CONTRACT.MISSING_DECISION_AUTHORIZATION")
        let first = encodeDiagnostics failure
        Assert.Equal(first, encodeDiagnostics failure)
        Assert.Contains("ordo.constructibility-diagnostics/1", first)
