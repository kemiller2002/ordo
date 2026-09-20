/// The primitives, tested at the level where their rules actually bite.
module Ordo.Tests.CoreTests

open System
open Xunit
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.Capability
open Ordo.Core.Obligation
open Ordo.Core.Resolution
open Ordo.Core.StateIdentity
open Ordo.Core.Transition
open Ordo.Tests.Fixtures

[<Fact>]
let ``an identifier refuses the shapes that are usually a defect`` () =
    Assert.Equal(Error IdentifierEmpty, DecisionContractId.create "")
    Assert.Equal(Error(IdentifierNotTrimmed " x"), DecisionContractId.create " x")
    Assert.Equal(Error(IdentifierHasControlCharacter "a\nb"), DecisionContractId.create "a\nb")

    match DecisionContractId.create (String('x', MaxIdentifierLength + 1)) with
    | Error(IdentifierTooLong(length, limit)) ->
        Assert.Equal(MaxIdentifierLength + 1, length)
        Assert.Equal(MaxIdentifierLength, limit)
    | other -> failwithf "expected a length refusal, got %A" other

[<Fact>]
let ``a version is a positive revision or it is not a version`` () =
    Assert.Equal(Error(VersionNotPositive 0), ContractVersion.create 0)
    Assert.Equal(1, ContractVersion.value (ok (ContractVersion.create 1)))

[<Fact>]
let ``a state-view schema requires a name and positive version`` () =
    Assert.Equal(Error StateViewSchemaIdIsEmpty, StateViewSchema.create "" 1)
    Assert.Equal(Error(StateViewSchemaVersionMustBePositive 0), StateViewSchema.create "state" 0)

    let schema = ok (StateViewSchema.create "state" 2)
    Assert.Equal("state", StateViewSchema.id schema)
    Assert.Equal(2, StateViewSchema.version schema)

[<Fact>]
let ``resolution modes have wire tokens independent of their case names`` () =
    for mode in [ Compute; Decide; Deliberate ] do
        Assert.Equal(Some mode, ResolutionMode.fromWire (ResolutionMode.toWire mode))

    Assert.Equal(None, ResolutionMode.fromWire "system-one")

[<Fact>]
let ``the narrowest sufficient mode is the one that constrains most`` () =
    Assert.True(ResolutionMode.isAtLeastAsNarrowAs Decide Compute)
    Assert.True(ResolutionMode.isAtLeastAsNarrowAs Decide Decide)
    Assert.False(ResolutionMode.isAtLeastAsNarrowAs Decide Deliberate)

[<Fact>]
let ``missing evidence stale evidence and the wrong kind of evidence are three answers`` () =
    let requirement =
        EvidenceRequirement.create "freshness" "Something recent."
        |> EvidenceRequirement.withMaximumAge (TimeSpan.FromHours 1.0)
        |> EvidenceRequirement.acceptingOnly [ AnyDirect ]

    let fresh = evidence "freshness" Direct now (JString "fresh")
    let old = evidence "freshness" Direct (now.AddHours -5.0) (JString "old")

    let inferred =
        evidence "freshness" (Inferred(ok (ProviderId.create "a-model"))) now (JString "a guess")

    match Evidence.checkRequirement now [] requirement with
    | Unsatisfied _ -> ()
    | other -> failwithf "expected Unsatisfied, got %A" other

    match Evidence.checkRequirement now [ old ] requirement with
    | Stale(_, _, age) -> Assert.Equal(TimeSpan.FromHours 5.0, age)
    | other -> failwithf "expected Stale, got %A" other

    match Evidence.checkRequirement now [ inferred ] requirement with
    | WrongKind(_, rejected) -> Assert.Single rejected |> ignore
    | other -> failwithf "expected WrongKind, got %A" other

    match Evidence.checkRequirement now [ old; fresh ] requirement with
    | Ordo.Core.Evidence.Satisfied(_, by) -> Assert.Equal(now, by.ObservedAt)
    | other -> failwithf "expected Satisfied by the newest, got %A" other

[<Fact>]
let ``an inference is never recorded as a direct observation`` () =
    let inference = evidence "claim" (Inferred(ok (ProviderId.create "a-model"))) now JNull
    Assert.Equal("inferred", EvidenceKind.toWire inference.Kind)
    Assert.False(EvidenceKind.matchesPattern AnyDirect inference.Kind)

[<Fact>]
let ``a capability set answers only what it was granted`` () =
    let held = CapabilitySet.ofList [ authorizeVerificationPath ]
    let other = ok (CapabilityId.create "deploy-to-production")

    Assert.True(CapabilitySet.grants authorizeVerificationPath.Id held)
    Assert.False(CapabilitySet.grants other held)
    Assert.Equal<CapabilityId list>([ other ], CapabilitySet.missing [ authorizeVerificationPath.Id; other ] held)

[<Fact>]
let ``an obligation records how it was discharged`` () =
    let obligation =
        Obligation.create (ok (ObligationId.create "ob-1")) (AcquireEvidence "site-diff") now

    Assert.True(Obligation.isOutstanding obligation)

    let satisfied = obligation |> Obligation.satisfy "read the diff" (now.AddMinutes 5.0)

    Assert.False(Obligation.isOutstanding satisfied)

    match satisfied.State with
    | Satisfied(how, at) ->
        Assert.Equal("read the diff", how)
        Assert.Equal(now.AddMinutes 5.0, at)
    | other -> failwithf "expected Satisfied, got %A" other

[<Fact>]
let ``a fingerprint depends on the complete semantic view rather than on field order`` () =
    let one = JObject [ "a", JInt 1L; "b", JInt 2L ]
    let other = JObject [ "b", JInt 2L; "a", JInt 1L ]

    Assert.Equal(StateFingerprint.ofView changeSiteViewSchema one, StateFingerprint.ofView changeSiteViewSchema other)

    Assert.NotEqual(
        StateFingerprint.ofView changeSiteViewSchema one,
        StateFingerprint.ofView changeSiteViewSchema (JObject [ "a", JInt 1L; "b", JInt 3L ])
    )

[<Fact>]
let ``the same view under a different semantic schema is a different state identity`` () =
    let view = JObject [ "a", JInt 1L ]
    let nextSchema = ok (StateViewSchema.create "sde.change-site-state" 2)
    let otherSchema = ok (StateViewSchema.create "sde.other-state" 1)

    Assert.NotEqual(
        StateFingerprint.ofView changeSiteViewSchema view,
        StateFingerprint.ofView nextSchema view
    )

    Assert.NotEqual(
        StateFingerprint.ofView changeSiteViewSchema view,
        StateFingerprint.ofView otherSchema view
    )

[<Fact>]
let ``unrelated ambient state does not change identity when the domain selects the same semantic view`` () =
    let semanticView = JObject [ "status", JString "ready" ]

    let ambientA =
        JObject
            [ "semantic", semanticView
              "browserWidth", JInt 1024L
              "selectedTab", JString "one" ]

    let ambientB =
        JObject
            [ "semantic", semanticView
              "browserWidth", JInt 1440L
              "selectedTab", JString "two" ]

    let project =
        function
        | JObject members ->
            members
            |> List.pick (fun (name, value) -> if name = "semantic" then Some value else None)
        | _ -> failwith "ambient state was not an object"

    let a = StateSnapshot.take changeSiteViewSchema now (project ambientA)
    let b = StateSnapshot.take changeSiteViewSchema now (project ambientB)

    Assert.Equal(a.Fingerprint, b.Fingerprint)
    Assert.Equal(semanticView, a.View)
    Assert.Equal(semanticView, b.View)

[<Fact>]
let ``a fingerprint read back from a record must have the shape this build writes`` () =
    let fingerprint = StateFingerprint.ofView changeSiteViewSchema (JString "x")

    Assert.Equal(Some fingerprint, StateFingerprint.parse (StateFingerprint.value fingerprint))
    Assert.Equal(None, StateFingerprint.parse "sha256:not-hex")
    Assert.Equal(None, StateFingerprint.parse "md5:abc")

[<Fact>]
let ``redacting for a provider does not change which state was judged`` () =
    let view =
        JObject
            [ "site", JString "Transition.fs"
              "reviewerEmail", JString "someone@example.com" ]

    let snapshot = snapshotOf view

    let redact =
        function
        | JObject members -> JObject(members |> List.filter (fun (name, _) -> name <> "reviewerEmail"))
        | other -> other

    let providerView = StateSnapshot.providerView redact snapshot

    Assert.Equal(Ok None, tryMember "reviewerEmail" providerView)
    Assert.Equal(StateFingerprint.ofView changeSiteViewSchema view, snapshot.Fingerprint)

[<Fact>]
let ``legacy state cannot authorize a new transition even when its fingerprint matches formed-against`` () =
    let legacyDocument =
        JObject
            [ "schema", JString "ordo.state-snapshot"
              "schemaVersion", JInt 1L
              "fingerprint", JString "sha256:e4c13c4401d43136853edf41d4fa114389d5228b7f01ca06038e4ec9b73796ea"
              "revision", JNull
              "takenAt", JString(Ordo.Core.Clock.toWire now)
              "view", JString "legacy" ]

    let legacy =
        match Ordo.Core.Wire.decodeStateSnapshot legacyDocument with
        | Ok snapshot -> snapshot
        | Error error -> failwithf "legacy snapshot fixture failed to decode: %A" error

    let context =
        { CurrentState = legacy
          FormedAgainst = legacy.Fingerprint
          Held = CapabilitySet.empty
          Available = []
          Obligations = []
          Policy = fastPathPolicy.Identity, Ordo.Core.Policy.PolicyAllows
          Now = now }

    match Transition.evaluate (TransitionRequirement.create "legacy-refusal") context with
    | TransitionRefused failures ->
        Assert.Contains(UnversionedCurrentState, failures)
    | other -> failwithf "expected legacy current state to be refused, got %A" other

[<Fact>]
let ``json survives a round trip through text`` () =
    let value =
        JObject
            [ "text", JString "quote \" backslash \\ newline \n tab \t"
              "int", JInt -42L
              "float", JFloat 0.25
              "true", JBool true
              "null", JNull
              "array", JArray [ JInt 1L; JString "two" ]
              "nested", JObject [ "deep", JArray [] ] ]

    Assert.Equal(Ok value, parse (render value))

[<Fact>]
let ``a non-finite number is never written as something no reader can read`` () =
    Assert.Equal("null", render (JFloat nan))
    Assert.Equal("null", render (JFloat infinity))
