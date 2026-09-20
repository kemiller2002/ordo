/// Contracts, confidence and the validation of what a provider says.
module Ordo.Tests.DecisionTests

open System
open Xunit
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Decisions.Confidence
open Ordo.Decisions.Contract
open Ordo.Decisions.Request
open Ordo.Decisions.Outcome
open Ordo.Decisions.Provider
open Ordo.Tests.Fixtures

let private snapshot = snapshotOf (siteState "Transition.fs" Unclassified "r1")

let private validateResponse response =
    Ordo.Decisions.Validation.validate changeClassContract snapshot [] fakeIdentity now response

// -------------------------------------------------------------- confidence

[<Fact>]
let ``confidence outside its documented scale is refused rather than clamped`` () =
    Assert.Equal(Error(ConfidenceOutOfRange 1.5), Confidence.providerReported 1.5)
    Assert.Equal(Error(ConfidenceOutOfRange -0.1), Confidence.providerReported -0.1)
    Assert.Equal(Error ConfidenceNotFinite, Confidence.providerReported nan)
    Assert.Equal(0.0, (ok (Confidence.providerReported 0.0)).Magnitude)
    Assert.Equal(1.0, (ok (Confidence.providerReported 1.0)).Magnitude)

[<Fact>]
let ``a provider's self-report is not a calibrated probability`` () =
    let selfReported = ok (Confidence.providerReported 0.92)

    Assert.False(Confidence.isCalibrated selfReported)
    Assert.Equal("provider-reported", Confidence.provenanceToWire selfReported.Provenance)

    let measured = ok (Confidence.create (EmpiricallyCalibrated("1000 recorded outcomes", 1000)) 0.92)

    Assert.True(Confidence.isCalibrated measured)
    Assert.Equal(selfReported.Magnitude, measured.Magnitude)

[<Fact>]
let ``calibration without a sample is not calibration`` () =
    Assert.Equal(Error(CalibrationSampleNotPositive 0), Confidence.create (EmpiricallyCalibrated("nothing", 0)) 0.5)

[<Fact>]
let ``a transformed score keeps the fact that it was transformed`` () =
    let derived = ok (Confidence.create (Derived "logprob normalised to 0..1") 0.7)

    Assert.False(Confidence.isCalibrated derived)

    match derived.Provenance with
    | Derived transformation -> Assert.Equal("logprob normalised to 0..1", transformation)
    | other -> failwithf "expected Derived, got %A" other

// ------------------------------------------------------------ choice space

[<Fact>]
let ``a choice space refuses to be built where parsing would be ambiguous`` () =
    Assert.Equal(Error NoChoicesDeclared, ChoiceSpace.create changeClassToken [])
    Assert.Equal(Error(DuplicateChoiceToken "same"), ChoiceSpace.create (fun _ -> "same") [ SemanticChange; BoundaryChange ])
    Assert.Equal(Error EmptyChoiceToken, ChoiceSpace.create (fun _ -> " ") [ SemanticChange ])

[<Fact>]
let ``only a declared token parses and it parses exactly`` () =
    let space = changeClassContract.Choices

    Assert.Equal(Some SemanticChange, ChoiceSpace.parse "semantic-change" space)
    Assert.Equal(None, ChoiceSpace.parse "mostly-safe" space)
    Assert.Equal(None, ChoiceSpace.parse "Semantic-Change" space)
    Assert.Equal(None, ChoiceSpace.parse "semantic-change " space)
    Assert.Equal(Some "semantic-change", ChoiceSpace.tokenOf SemanticChange space)

// --------------------------------------------------------------- lifecycle

[<Fact>]
let ``a retired contract stops receiving questions but stays readable`` () =
    let replacement =
        { Replacement = ok (DecisionContractId.create "sde.change-site-class-2")
          ReplacementVersion = ok (ContractVersion.create 1)
          Compatibility = IncompatibleReplacement }

    let retired =
        changeClassContract |> DecisionContract.retiredAt now (Some replacement)

    Assert.False(ContractLifecycle.acceptsNewRequests retired.Lifecycle)
    Assert.True(ContractLifecycle.acceptsNewRequests changeClassContract.Lifecycle)

    match DecisionRequest.create (ok (DecisionRequestId.create "r")) (ok (ResolutionId.create "x")) retired snapshot [] now with
    | Error(ContractRetired(id, version)) ->
        Assert.Equal(changeClassContract.Id, id)
        Assert.Equal(changeClassContract.Version, version)
    | other -> failwithf "expected the request to be refused, got %A" other

    // The retired contract still says what it meant: its question, its
    // choices and its revision are all still there to interpret an old
    // record with.
    Assert.Equal(changeClassContract.Question, retired.Question)
    Assert.Equal<string list>(ChoiceSpace.tokens changeClassContract.Choices, ChoiceSpace.tokens retired.Choices)

[<Fact>]
let ``a new decision request refuses structurally invalid derived provenance`` () =
    let missing = ok (EvidenceId.create "missing-input")
    let malformed = evidence "derived-with-gap" (Derived("gap", [ missing ])) now JNull

    match
        DecisionRequest.create
            (ok (DecisionRequestId.create "bad-provenance"))
            (ok (ResolutionId.create "bad-provenance-resolution"))
            changeClassContract
            snapshot
            (malformed :: fullEvidence)
            now
    with
    | Error(InvalidEvidenceDependencies(MissingEvidenceDependency(dependent, absent))) ->
        Assert.Equal(malformed.Id, dependent)
        Assert.Equal(missing, absent)
    | other -> failwithf "expected invalid derived provenance to be refused, got %A" other

[<Fact>]
let ``a deprecated contract still answers`` () =
    let deprecated =
        { changeClassContract with
            Lifecycle = Deprecated None }

    Assert.True(ContractLifecycle.acceptsNewRequests deprecated.Lifecycle)

// -------------------------------------------------------------- validation

[<Fact>]
let ``a legal choice becomes a decision carrying the state it was made against`` () =
    match validateResponse (ChoiceSelected("mechanical-propagation", Some(confidenceOf 0.8), None)) with
    | Decided result ->
        Assert.Equal(MechanicalPropagation, result.Choice)
        Assert.Equal(snapshot.Fingerprint, result.DecidedAgainst)
        Assert.Equal(Some 0.8, result.Confidence |> Option.map (fun c -> c.Magnitude))
    | other -> failwithf "expected Decided, got %A" other

[<Fact>]
let ``an undeclared choice is a contract violation and not a near match`` () =
    match validateResponse (ChoiceSelected("mostly-mechanical", None, None)) with
    | ProviderFailure(ContractViolation detail) -> Assert.Contains("mostly-mechanical", detail)
    | other -> failwithf "expected a contract violation, got %A" other

[<Fact>]
let ``a decision without confidence is a decision`` () =
    match validateResponse (ChoiceSelected("semantic-change", None, None)) with
    | Decided result -> Assert.Equal(None, result.Confidence)
    | other -> failwithf "expected Decided, got %A" other

[<Fact>]
let ``a rationale arguing for another choice is surfaced and changes nothing`` () =
    let rationale = "On reflection this is really a semantic-change, not what I picked."

    match validateResponse (ChoiceSelected("mechanical-propagation", None, Some rationale)) with
    | Decided result ->
        Assert.Equal(MechanicalPropagation, result.Choice)
        Assert.Equal<string list>([ "semantic-change" ], result.RationaleMentionsOtherChoices)
    | other -> failwithf "expected Decided, got %A" other

[<Fact>]
let ``a rationale agreeing with the choice raises no conflict`` () =
    match validateResponse (ChoiceSelected("semantic-change", None, Some "This is a semantic-change: a new rule was decided here.")) with
    | Decided result -> Assert.Empty result.RationaleMentionsOtherChoices
    | other -> failwithf "expected Decided, got %A" other

[<Fact>]
let ``a provider may only report evidence the contract declared`` () =
    match validateResponse (EvidenceInsufficient [ "tool-diagnostic" ]) with
    | InsufficientEvidence [ requirement ] -> Assert.Equal("tool-diagnostic", requirement.Id)
    | other -> failwithf "expected InsufficientEvidence, got %A" other

    match validateResponse (EvidenceInsufficient [ "a-requirement-nobody-declared" ]) with
    | ProviderFailure(ContractViolation detail) -> Assert.Contains("a-requirement-nobody-declared", detail)
    | other -> failwithf "expected a contract violation, got %A" other

[<Fact>]
let ``escalations and failures each keep their own identity`` () =
    match validateResponse (DeliberationRequested "the site touches two authorities") with
    | RequiresDeliberation(ProviderDeclined note) -> Assert.Equal("the site touches two authorities", note)
    | other -> failwithf "expected RequiresDeliberation, got %A" other

    match validateResponse (HumanReviewRequested "needs an owner") with
    | RequiresHumanReview(ProviderRequestedPerson note) -> Assert.Equal("needs an owner", note)
    | other -> failwithf "expected RequiresHumanReview, got %A" other

    match validateResponse (ProviderFailed(RateLimited None)) with
    | ProviderFailure(RateLimited None) -> ()
    | other -> failwithf "expected a provider failure, got %A" other

    match validateResponse ResponseCancelled with
    | ResolutionCancelled -> ()
    | other -> failwithf "expected ResolutionCancelled, got %A" other

[<Fact>]
let ``every outcome has a distinct wire token and only one yields a decision`` () =
    let outcomes: DecisionOutcome<ChangeClass> list =
        [ InsufficientEvidence []
          RequiresDeliberation(OutsideContractScope "x")
          RequiresHumanReview(PolicyRequiresPerson "x")
          ProviderFailure(Unavailable "x")
          ResolutionCancelled ]

    for outcome in outcomes do
        Assert.Equal(None, DecisionOutcome.decision outcome)

    let tokens = outcomes |> List.map DecisionOutcome.toWire
    Assert.Equal(List.length tokens, tokens |> List.distinct |> List.length)

[<Fact>]
let ``only transport failures make re-sending the same request reasonable`` () =
    Assert.True(ProviderError.isTransportTransient (Unavailable "x"))
    Assert.True(ProviderError.isTransportTransient (ProviderTimeout(TimeSpan.FromSeconds 1.0)))
    Assert.True(ProviderError.isTransportTransient (RateLimited None))
    Assert.False(ProviderError.isTransportTransient (ContractViolation "x"))
    Assert.False(ProviderError.isTransportTransient (AuthenticationFailure "x"))
    Assert.False(ProviderError.isTransportTransient (InvalidResponse "x"))
