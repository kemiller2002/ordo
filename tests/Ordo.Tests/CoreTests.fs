/// The primitives, tested at the level where their rules actually bite.
module Ordo.Tests.CoreTests

open System
open Xunit
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.Coverage
open Ordo.Core.NegativeKnowledge
open Ordo.Core.Capability
open Ordo.Core.Obligation
open Ordo.Core.ExternalEffect
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
let ``derived evidence closure is dependency-first and independent of caller order`` () =
    let a = evidence "a" Direct now (JString "a")
    let b = evidence "b" (EvidenceKind.Derived("b-from-a", [ a.Id ])) now (JString "b")
    let c = evidence "c" (EvidenceKind.Derived("c-from-b", [ b.Id ])) now (JString "c")

    let ids (items: Evidence list) = items |> List.map (fun item -> EvidenceId.value item.Id)

    match EvidenceDependency.closure [ c; a; b ] [ c.Id ] with
    | Ok closure -> Assert.Equal<string list>([ "a"; "b"; "c" ], ids closure)
    | Error error -> failwithf "expected a valid closure, got %A" error

[<Fact>]
let ``shared derived dependencies are legal and closure ordering is deterministic`` () =
    let a = evidence "a" Direct now (JString "shared")
    let b = evidence "b" (EvidenceKind.Derived("b-from-a", [ a.Id ])) now (JString "b")
    let c = evidence "c" (EvidenceKind.Derived("c-from-a", [ a.Id ])) now (JString "c")
    let d = evidence "d" (EvidenceKind.Derived("d-from-c-and-b", [ c.Id; b.Id ])) now (JString "d")

    let ids (items: Evidence list) = items |> List.map (fun item -> EvidenceId.value item.Id)

    let first = EvidenceDependency.closure [ d; c; a; b ] [ d.Id ]
    let second = EvidenceDependency.closure [ b; a; d; c ] [ d.Id ]

    match first, second with
    | Ok firstClosure, Ok secondClosure ->
        Assert.Equal<string list>([ "a"; "b"; "c"; "d" ], ids firstClosure)
        Assert.Equal<string list>(ids firstClosure, ids secondClosure)
    | other -> failwithf "expected shared dependencies to be valid, got %A" other

[<Fact>]
let ``a derived record naming evidence outside the closed set is refused`` () =
    let missing = ok (EvidenceId.create "missing")
    let derived = evidence "derived" (EvidenceKind.Derived("needs-missing", [ missing ])) now JNull

    match EvidenceDependency.validateClosedSet [ derived ] with
    | Error(MissingEvidenceDependency(dependent, absent)) ->
        Assert.Equal(derived.Id, dependent)
        Assert.Equal(missing, absent)
    | other -> failwithf "expected a missing dependency, got %A" other

[<Fact>]
let ``a derived record cannot depend on itself`` () =
    let self = ok (EvidenceId.create "self")
    let derived = evidence "self" (EvidenceKind.Derived("self-reference", [ self ])) now JNull

    match EvidenceDependency.validateClosedSet [ derived ] with
    | Error(EvidenceDependencyCycle cycle) ->
        Assert.Equal<string list>([ "self"; "self" ], cycle |> List.map EvidenceId.value)
    | other -> failwithf "expected a self-cycle, got %A" other

[<Fact>]
let ``a multi-node derived evidence cycle is refused with its path`` () =
    let aId = ok (EvidenceId.create "a")
    let bId = ok (EvidenceId.create "b")
    let cId = ok (EvidenceId.create "c")

    let a = evidence "a" (EvidenceKind.Derived("a-from-b", [ bId ])) now JNull
    let b = evidence "b" (EvidenceKind.Derived("b-from-c", [ cId ])) now JNull
    let cEvidence = evidence "c" (EvidenceKind.Derived("c-from-a", [ aId ])) now JNull

    match EvidenceDependency.validateClosedSet [ cEvidence; b; a ] with
    | Error(EvidenceDependencyCycle cycle) ->
        Assert.Equal<string list>([ "a"; "b"; "c"; "a" ], cycle |> List.map EvidenceId.value)
    | other -> failwithf "expected a multi-node cycle, got %A" other

[<Fact>]
let ``duplicate evidence identities are refused before provenance traversal`` () =
    let first = evidence "duplicate" Direct now (JString "one")
    let second = evidence "duplicate" Direct now (JString "two")

    match EvidenceDependency.validateClosedSet [ first; second ] with
    | Error(DuplicateEvidenceId id) -> Assert.Equal("duplicate", EvidenceId.value id)
    | other -> failwithf "expected a duplicate identity error, got %A" other

[<Fact>]
let ``coverage scopes are explicit semantic identifiers`` () =
    Assert.Equal(Error CoverageScopeEmpty, CoverageScope.create "")
    Assert.Equal(Error(CoverageScopeNotTrimmed " relations"), CoverageScope.create " relations")

    let scope = ok (CoverageScope.create "schema.relations")
    Assert.Equal("schema.relations", CoverageScope.value scope)

[<Fact>]
let ``Strata-style coverage keeps complete and partial dimensions separate`` () =
    let relations = ok (CoverageScope.create "strata.relations")
    let access = ok (CoverageScope.create "strata.relation-access")
    let provenance = evidence "strata-introspection" Direct now (JString "hidden.secret is catalog-visible but unreadable")

    let complete = ok (ContextCoverageClaim.create relations Complete [ provenance.Id ])
    let partial = ok (ContextCoverageClaim.create access Partial [ provenance.Id ])
    let claims = [ complete; partial ]

    Assert.Equal(None, ContextCoverage.checkRequirement claims (CoverageRequirement.complete relations "relations must be fully enumerated"))

    match ContextCoverage.checkRequirement claims (CoverageRequirement.complete access "relation access must be fully established") with
    | Some(CoveragePartial(requirement, claim)) ->
        Assert.Equal(access, requirement.Scope)
        Assert.Equal(Partial, claim.Status)
    | other -> failwithf "expected relation-access to remain Partial, got %A" other

[<Fact>]
let ``Time Tracking unreadable reference catalog is Unknown rather than empty or Partial`` () =
    let scope = ok (CoverageScope.create "chrona.reference-catalog")
    let failedPull = evidence "reference-pull" Direct now (JString "network error while reading reference.json")
    let claim = ok (ContextCoverageClaim.create scope CoverageStatus.Unknown [ failedPull.Id ])
    let requirement = CoverageRequirement.complete scope "the current reference catalog must be established"

    match ContextCoverage.checkRequirement [ claim ] requirement with
    | Some(CoverageUnknown(_, actual)) -> Assert.Equal(CoverageStatus.Unknown, actual.Status)
    | other -> failwithf "expected Unknown reference-catalog coverage, got %A" other

[<Fact>]
let ``coverage claims require evidence provenance and reject ambiguous duplicate scopes`` () =
    let scope = ok (CoverageScope.create "scope")
    let provenance = evidence "coverage-source" Direct now JNull

    Assert.Equal(Error(CoverageProvenanceRequired scope), ContextCoverageClaim.create scope Complete [])

    let first = ok (ContextCoverageClaim.create scope Complete [ provenance.Id ])
    let second = ok (ContextCoverageClaim.create scope Partial [ provenance.Id ])

    match ContextCoverage.validateClaims [ provenance ] [ first; second ] with
    | Error(DuplicateCoverageScope duplicate) -> Assert.Equal(scope, duplicate)
    | other -> failwithf "expected duplicate coverage scope refusal, got %A" other

[<Fact>]
let ``a complete scoped negative observation may structurally support absence`` () =
    let scope = ok (CoverageScope.create "strata.relations")
    let observation =
        ok (
            NegativeObservation.create
                "app.missing_table"
                scope
                "postgres-catalog-introspection"
                (Some "pg_class/pg_namespace lookup")
                "database-revision:42"
                []
                []
        )

    let evidence =
        evidence
            "missing-table-observation"
            Direct
            now
            (Ordo.Core.Wire.encodeNegativeObservation observation)

    let coverage = ok (ContextCoverageClaim.create scope Complete [ evidence.Id ])

    Assert.Equal(Ok(), NegativeKnowledge.supportsAbsence evidence coverage observation)

[<Fact>]
let ``Strata partial coverage cannot turn not-found into absence`` () =
    let scope = ok (CoverageScope.create "strata.relation-access")
    let observation =
        ok (
            NegativeObservation.create
                "hidden.secret"
                scope
                "postgres-access-probe"
                None
                "database-revision:42"
                [ "visible but unreadable relations" ]
                []
        )

    let evidence =
        evidence
            "partial-relation-access"
            Direct
            now
            (Ordo.Core.Wire.encodeNegativeObservation observation)

    let coverage = ok (ContextCoverageClaim.create scope Partial [ evidence.Id ])

    Assert.Equal(
        Error(NegativeObservationCoverageNotComplete Partial),
        NegativeKnowledge.supportsAbsence evidence coverage observation
    )

[<Fact>]
let ``Time Tracking failed reference pull cannot become an absence claim`` () =
    let scope = ok (CoverageScope.create "chrona.reference-catalog")
    let observation =
        ok (
            NegativeObservation.create
                "project-42"
                scope
                "reference-json-read"
                (Some "project id project-42")
                "repository-commit:abc123"
                []
                [ "network error while reading reference.json" ]
        )

    let evidence =
        evidence
            "reference-pull-failed"
            Direct
            now
            (Ordo.Core.Wire.encodeNegativeObservation observation)

    let coverage = ok (ContextCoverageClaim.create scope CoverageStatus.Unknown [ evidence.Id ])

    Assert.Equal(
        Error(NegativeObservationCoverageNotComplete CoverageStatus.Unknown),
        NegativeKnowledge.supportsAbsence evidence coverage observation
    )

[<Fact>]
let ``negative observation must be the provenance for the matching coverage scope`` () =
    let observedScope = ok (CoverageScope.create "scope-a")
    let otherScope = ok (CoverageScope.create "scope-b")
    let observation =
        ok (NegativeObservation.create "target" observedScope "method" None "state-1" [] [])
    let negativeEvidence = evidence "negative" Direct now (Ordo.Core.Wire.encodeNegativeObservation observation)
    let unrelatedEvidence = evidence "unrelated" Direct now JNull

    let wrongScope = ok (ContextCoverageClaim.create otherScope Complete [ negativeEvidence.Id ])

    match NegativeKnowledge.supportsAbsence negativeEvidence wrongScope observation with
    | Error(NegativeObservationScopeMismatch(actual, coverage)) ->
        Assert.Equal(observedScope, actual)
        Assert.Equal(otherScope, coverage)
    | other -> failwithf "expected a scope mismatch, got %A" other

    let wrongProvenance = ok (ContextCoverageClaim.create observedScope Complete [ unrelatedEvidence.Id ])

    Assert.Equal(
        Error(NegativeObservationNotCoverageProvenance negativeEvidence.Id),
        NegativeKnowledge.supportsAbsence negativeEvidence wrongProvenance observation
    )

    let wrongContentEvidence = evidence "wrong-content" Direct now JNull
    let matchingIdButWrongContent = ok (ContextCoverageClaim.create observedScope Complete [ wrongContentEvidence.Id ])

    Assert.Equal(
        Error(NegativeObservationEvidenceContentMismatch wrongContentEvidence.Id),
        NegativeKnowledge.supportsAbsence wrongContentEvidence matchingIdButWrongContent observation
    )

[<Fact>]
let ``negative observation refuses missing minimum provenance fields`` () =
    let scope = ok (CoverageScope.create "scope")

    Assert.Equal(
        Error NegativeTargetRequired,
        NegativeObservation.create "" scope "method" None "state" [] []
    )

    Assert.Equal(
        Error NegativeMethodRequired,
        NegativeObservation.create "target" scope "" None "state" [] []
    )

    Assert.Equal(
        Error NegativeStateReferenceRequired,
        NegativeObservation.create "target" scope "method" None "" [] []
    )

[<Fact>]
let ``a capability set answers only what it was granted`` () =
    let held = CapabilitySet.ofList [ authorizeVerificationPath ]
    let other = ok (CapabilityId.create "deploy-to-production")

    Assert.True(CapabilitySet.grants authorizeVerificationPath.Id held)
    Assert.False(CapabilitySet.grants other held)
    Assert.Equal<CapabilityId list>([ other ], CapabilitySet.missing [ authorizeVerificationPath.Id; other ] held)

[<Fact>]
let ``unknown external effect creates an outstanding reconciliation obligation`` () =
    let effectId = ok (ExternalEffectId.create "github-write-42")
    let obligationId = ok (ObligationId.create "reconcile-github-write-42")

    match ExternalEffect.recordOutcome obligationId effectId now (Unknown "connection dropped after send") with
    | ReconciliationRequired(Unknown reason, obligation) ->
        Assert.Equal("connection dropped after send", reason)
        Assert.True(Obligation.isOutstanding obligation)
        Assert.Equal(ReconcileExternalEffect effectId, obligation.Kind)
    | other -> failwithf "expected reconciliation-required outcome, got %A" other

[<Fact>]
let ``known success and known failure do not invent reconciliation work`` () =
    let effectId = ok (ExternalEffectId.create "effect-1")
    let obligationId = ok (ObligationId.create "ob-1")

    match ExternalEffect.recordOutcome obligationId effectId now Succeeded with
    | Settled Succeeded -> ()
    | other -> failwithf "expected settled success, got %A" other

    match ExternalEffect.recordOutcome obligationId effectId now (Failed "definite rejection") with
    | Settled(Failed "definite rejection") -> ()
    | other -> failwithf "expected settled failure, got %A" other

[<Fact>]
let ``retry before reconciliation is never inferred and never removes the obligation`` () =
    let effectId = ok (ExternalEffectId.create "create-only-write")
    let obligationId = ok (ObligationId.create "reconcile-create-only-write")

    Assert.False(ExternalEffect.mayRepeatBeforeReconciliation RetrySafetyNotEstablished)

    Assert.True(
        ExternalEffect.mayRepeatBeforeReconciliation
            (RetrySafeByExternalContract "stable create-only operation identity")
    )

    match ExternalEffect.recordOutcome obligationId effectId now (Unknown "response lost") with
    | ReconciliationRequired(_, obligation) -> Assert.True(Obligation.isOutstanding obligation)
    | other -> failwithf "retry safety must not erase reconciliation, got %A" other

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
let ``outstanding reconciliation can block a later transition until explicitly discharged`` () =
    let effectId = ok (ExternalEffectId.create "deploy-42")
    let obligationId = ok (ObligationId.create "reconcile-deploy-42")

    let obligation =
        match ExternalEffect.recordOutcome obligationId effectId now (Unknown "deployment response lost") with
        | ReconciliationRequired(_, obligation) -> obligation
        | other -> failwithf "expected reconciliation obligation, got %A" other

    let requirement =
        TransitionRequirement.create "publish-follow-up"
        |> TransitionRequirement.requiringObligations [ obligationId ]

    let baseContext =
        { CurrentState = snapshotOf (JString "current")
          FormedAgainst = (snapshotOf (JString "current")).Fingerprint
          Held = CapabilitySet.empty
          Available = []
          Obligations = [ obligation ]
          Policy = fastPathPolicy.Identity, Ordo.Core.Policy.PolicyAllows
          Now = now }

    match Transition.evaluate requirement baseContext with
    | TransitionRefused failures ->
        Assert.Contains(UnsatisfiedObligation [ obligationId ], failures)
    | other -> failwithf "expected reconciliation obligation to block transition, got %A" other

    let reconciled = obligation |> Obligation.satisfy "observed external system" (now.AddMinutes 1.0)

    match Transition.evaluate requirement { baseContext with Obligations = [ reconciled ] } with
    | TransitionAllowed _ -> ()
    | other -> failwithf "expected reconciled obligation to allow transition, got %A" other

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
