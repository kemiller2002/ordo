/// What is written down, and what happens when something unreadable is read.
module Ordo.Tests.WireTests

open System
open Xunit
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.Obligation
open Ordo.Core.StateIdentity
open Ordo.Core.Wire
open Ordo.Tests.Fixtures

let private roundTrip encode decode value =
    match parse (render (encode value)) with
    | Error error -> failwithf "encoded form was not readable JSON: %A" error
    | Ok document -> decode document

[<Fact>]
let ``evidence survives a round trip through its wire form`` () =
    match roundTrip encodeEvidence decodeEvidence diagnosticEvidence with
    | Ok decoded -> Assert.Equal(diagnosticEvidence, decoded)
    | Error error -> failwithf "decoding failed: %A" error

[<Fact>]
let ``derived and inferred evidence keep their derivation`` () =
    let derived =
        evidence "derived" (Derived("complexity-count", [ diagnosticEvidence.Id ])) now (JInt 7L)

    let inferred =
        evidence "inferred" (Inferred(ok (ProviderId.create "a-model"))) now (JString "a guess")

    for original in [ derived; inferred ] do
        match roundTrip encodeEvidence decodeEvidence original with
        | Ok decoded -> Assert.Equal(original.Kind, decoded.Kind)
        | Error error -> failwithf "decoding failed: %A" error

[<Fact>]
let ``an evidence requirement keeps its freshness rule and accepted kinds`` () =
    let requirement =
        EvidenceRequirement.create "r" "Something."
        |> EvidenceRequirement.withMaximumAge (TimeSpan.FromMinutes 90.0)
        |> EvidenceRequirement.acceptingOnly [ AnyDirect; AnyDerived ]

    match roundTrip encodeEvidenceRequirement decodeEvidenceRequirement requirement with
    | Ok decoded -> Assert.Equal(requirement, decoded)
    | Error error -> failwithf "decoding failed: %A" error

[<Fact>]
let ``a requirement with no freshness rule round-trips as having none`` () =
    let requirement = EvidenceRequirement.create "r" "Something."

    match roundTrip encodeEvidenceRequirement decodeEvidenceRequirement requirement with
    | Ok decoded ->
        Assert.Equal(None, decoded.MaximumAge)
        Assert.Empty decoded.AcceptableKinds
    | Error error -> failwithf "decoding failed: %A" error

[<Fact>]
let ``a state snapshot is refused if its stored fingerprint disagrees with its view`` () =
    let snapshot = snapshotOf (siteState "Transition.fs" Unclassified "r1")

    match roundTrip encodeStateSnapshot decodeStateSnapshot snapshot with
    | Ok decoded ->
        Assert.Equal(snapshot.ViewSchema, decoded.ViewSchema)
        Assert.Equal(snapshot.Fingerprint, decoded.Fingerprint)
        Assert.Equal(snapshot.View, decoded.View)
    | Error error -> failwithf "decoding failed: %A" error

    match encodeStateSnapshot snapshot with
    | JObject members ->
        Assert.Contains(("schemaVersion", JInt(int64 StateSnapshotSchemaVersion)), members)
        Assert.Contains(members, fun (name, _) -> name = "viewSchema")
    | other -> failwithf "expected a state snapshot object, got %A" other

    let tampered =
        match encodeStateSnapshot snapshot with
        | JObject members ->
            JObject(
                members
                |> List.map (fun (name, value) ->
                    if name = "view" then
                        name, siteState "Something-else.fs" Unclassified "r1"
                    else
                        name, value)
            )
        | other -> other

    match decodeStateSnapshot tampered with
    | Error(InvalidField("$.fingerprint", _)) -> ()
    | other -> failwithf "expected the tampered view to be refused, got %A" other


[<Fact>]
let ``changing only the declared view schema invalidates a stored current fingerprint`` () =
    let snapshot = snapshotOf (siteState "Transition.fs" Unclassified "r1")

    let tampered =
        match encodeStateSnapshot snapshot with
        | JObject members ->
            JObject(
                members
                |> List.map (fun (name, value) ->
                    if name = "viewSchema" then
                        match value with
                        | JObject schemaMembers ->
                            name,
                            JObject(
                                schemaMembers
                                |> List.map (fun (schemaName, schemaValue) ->
                                    if schemaName = "version" then schemaName, JInt 2L else schemaName, schemaValue)
                            )
                        | _ -> name, value
                    else
                        name, value)
            )
        | other -> other

    match decodeStateSnapshot tampered with
    | Error(InvalidField("$.fingerprint", _)) -> ()
    | other -> failwithf "expected the changed view schema to invalidate the fingerprint, got %A" other

[<Fact>]
let ``a schema-v1 state snapshot stays readable as legacy history and stays schema-v1 when re-encoded`` () =
    // sha256 over the schema-v1 canonical view: renderCanonical (JString "legacy")
    let legacy =
        JObject
            [ "schema", JString "ordo.state-snapshot"
              "schemaVersion", JInt 1L
              "fingerprint", JString "sha256:e4c13c4401d43136853edf41d4fa114389d5228b7f01ca06038e4ec9b73796ea"
              "revision", JNull
              "takenAt", JString(Ordo.Core.Clock.toWire now)
              "view", JString "legacy" ]

    match decodeStateSnapshot legacy with
    | Ok decoded ->
        Assert.Equal(None, decoded.ViewSchema)
        Assert.False(StateSnapshot.isVersioned decoded)
        Assert.Equal(JString "legacy", decoded.View)

        match encodeStateSnapshot decoded with
        | JObject members ->
            Assert.Contains(("schemaVersion", JInt 1L), members)
            Assert.DoesNotContain(members, fun (name, _) -> name = "viewSchema")
        | other -> failwithf "expected a legacy state snapshot object, got %A" other

        match
            Ordo.Decisions.Request.DecisionRequest.create
                (ok (DecisionRequestId.create "legacy-request"))
                (ok (ResolutionId.create "legacy-resolution"))
                changeClassContract
                decoded
                fullEvidence
                now
        with
        | Error(Ordo.Decisions.Request.UnversionedStateSnapshot contract) ->
            Assert.Equal(changeClassContract.Id, contract)
        | other -> failwithf "expected a legacy state snapshot to be refused for a new decision, got %A" other
    | Error error -> failwithf "legacy decoding failed: %A" error

[<Fact>]
let ``a future state-snapshot wire version is refused without affecting v1 evidence records`` () =
    let snapshot = snapshotOf (siteState "Transition.fs" Unclassified "r1")

    let future =
        match encodeStateSnapshot snapshot with
        | JObject members ->
            JObject(
                members
                |> List.map (fun (name, value) ->
                    if name = "schemaVersion" then name, JInt 99L else name, value)
            )
        | other -> other

    match decodeStateSnapshot future with
    | Error(UnsupportedSchemaVersion(99, supported)) ->
        Assert.Equal<int list>([ 1; StateSnapshotSchemaVersion ], supported)
    | other -> failwithf "expected the future state-snapshot schema to be refused, got %A" other

[<Fact>]
let ``a schema version this build does not understand is refused, not guessed at`` () =
    let future =
        match encodeEvidence diagnosticEvidence with
        | JObject members ->
            JObject(
                members
                |> List.map (fun (name, value) ->
                    if name = "schemaVersion" then name, JInt 99L else name, value)
            )
        | other -> other

    match decodeEvidence future with
    | Error(UnsupportedSchemaVersion(99, supported)) -> Assert.Equal<int list>([ SchemaVersion ], supported)
    | other -> failwithf "expected the future schema to be refused, got %A" other

[<Fact>]
let ``a record of the wrong kind is refused even when its fields would fit`` () =
    let mislabelled =
        match encodeEvidence diagnosticEvidence with
        | JObject members ->
            JObject(
                members
                |> List.map (fun (name, value) ->
                    if name = "schema" then name, JString "ordo.state-snapshot" else name, value)
            )
        | other -> other

    match decodeEvidence mislabelled with
    | Error(UnexpectedSchema("ordo.state-snapshot", "ordo.evidence")) -> ()
    | other -> failwithf "expected the schema mismatch to be refused, got %A" other

[<Fact>]
let ``a union case this build has never heard of is refused, not dropped`` () =
    match decodeEvidenceKind (JObject [ "kind", JString "divinely-revealed" ]) with
    | Error(UnknownVariant("kind", "divinely-revealed")) -> ()
    | other -> failwithf "expected the unknown variant to be refused, got %A" other

[<Fact>]
let ``every obligation kind has a distinct stable token`` () =
    let kinds =
        [ AcquireEvidence "x"
          ReacquireStaleEvidence "x"
          HumanReview "x"
          RunVerification "x"
          InvestigateFailure "x"
          Custom "x" ]

    for kind in kinds do
        match decodeObligationKind (encodeObligationKind kind) with
        | Ok decoded -> Assert.Equal(kind, decoded)
        | Error error -> failwithf "decoding %A failed: %A" kind error

    let tokens =
        kinds
        |> List.map (fun kind ->
            match encodeObligationKind kind with
            | JObject members ->
                members
                |> List.pick (function
                    | "kind", JString token -> Some token
                    | _ -> None)
            | _ -> failwith "an obligation kind did not encode as an object")

    Assert.Equal(List.length tokens, tokens |> List.distinct |> List.length)

[<Fact>]
let ``wire tokens do not follow the F# case names they were written from`` () =
    // If these are ever derived from type names rather than written out, a
    // rename in F# becomes a silent breaking change to stored records. The
    // assertion is the kebab-case convention, which no F# case name has.
    Assert.Equal("acquire-evidence", (match encodeObligationKind (AcquireEvidence "x") with
                                      | JObject members ->
                                          members
                                          |> List.pick (function
                                              | "kind", JString token -> Some token
                                              | _ -> None)
                                      | _ -> ""))

[<Fact>]
let ``malformed text is a readable failure rather than an exception`` () =
    match parse "{not json" with
    | Error(MalformedJson _) -> ()
    | other -> failwithf "expected malformed JSON to be reported, got %A" other
