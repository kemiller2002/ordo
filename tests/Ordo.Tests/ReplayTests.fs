/// Replay re-evaluates. It never applies.
module Ordo.Tests.ReplayTests

open System.Threading
open Xunit
open Ordo.Core.Identifiers
open Ordo.Decisions.Contract
open Ordo.Decisions.Outcome
open Ordo.Decisions.Provider
open Ordo.Decisions.Resolve
open Ordo.Decisions.Replay
open Ordo.Tests.Fixtures

let private options =
    ResolveOptions.standard |> ResolveOptions.withClock testClock

let private unclassified = snapshotOf (siteState "Transition.fs" Unclassified "r1")

let private replay provider purpose original historical =
    (Replay.execute options provider purpose (ok (ResolutionId.create "res-replay")) original historical CancellationToken.None)
        .GetAwaiter()
        .GetResult()

[<Fact>]
let ``a replay keeps the historical result and adds a new one beside it`` () =
    let request = requestFor changeClassContract unclassified fullEvidence

    let original =
        (Resolve.execute options (answering (ChoiceSelected("mechanical-propagation", None, None))) request CancellationToken.None)
            .GetAwaiter()
            .GetResult()

    let record =
        replay
            (answering (ChoiceSelected("semantic-change", None, None)))
            LogicalReplay
            request
            original.Outcome

    match DecisionOutcome.decision record.Historical with
    | Some historical -> Assert.Equal(MechanicalPropagation, historical.Choice)
    | None -> failwith "the historical decision was lost"

    match DecisionOutcome.decision record.Fresh.Outcome with
    | Some fresh -> Assert.Equal(SemanticChange, fresh.Choice)
    | None -> failwith "the replay produced no decision"

    Assert.Equal(Some false, Replay.agrees record)

[<Fact>]
let ``a replay runs under its own identity, linked to the original`` () =
    let request = requestFor changeClassContract unclassified fullEvidence

    let record =
        replay (answering (ChoiceSelected("mechanical-propagation", None, None))) LogicalReplay request (InsufficientEvidence [])

    Assert.Equal(ok (ResolutionId.create "res-replay"), record.Fresh.Observation.Resolution)
    Assert.Equal(Some request.Resolution, record.Fresh.Observation.CausedBy)
    Assert.Equal(request.Id, record.Fresh.Observation.Request)

[<Fact>]
let ``a replay evaluates the contract revision that was in force`` () =
    let request = requestFor changeClassContract unclassified fullEvidence

    // The contract has since moved on. The recorded request still carries the
    // revision that judged, so a newer one cannot stand in for it.
    let newer: DecisionContract<ChangeClass> =
        { changeClassContract with
            Version = ok (ContractVersion.create 2) }

    Assert.NotEqual(newer.Version, request.Contract.Version)

    let record =
        replay (answering (ChoiceSelected("mechanical-propagation", None, None))) LogicalReplay request (InsufficientEvidence [])

    Assert.Equal(changeClassContract.Version, record.Fresh.Observation.ContractVersion)

[<Fact>]
let ``a replay does not touch the state it replays against`` () =
    let request = requestFor changeClassContract unclassified fullEvidence
    let before = request.State.Fingerprint

    let record =
        replay (answering (ChoiceSelected("mechanical-propagation", None, None))) LogicalReplay request (InsufficientEvidence [])

    Assert.Equal(before, request.State.Fingerprint)
    Assert.Equal(before, record.Fresh.Observation.State)

[<Fact>]
let ``the purpose of a duplicate evaluation and how independent it is are both recorded`` () =
    let request = requestFor changeClassContract unclassified fullEvidence

    let record =
        replay
            (answering (ChoiceSelected("mechanical-propagation", None, None)))
            (IndependentVerification SameProviderSameModel)
            request
            (InsufficientEvidence [])

    match record.Purpose with
    | IndependentVerification independence ->
        Assert.Equal("independent-verification", Replay.purposeToWire record.Purpose)
        Assert.Equal("same-provider-same-model", Replay.independenceToWire independence)
    | other -> failwithf "expected an independent verification, got %A" other

[<Fact>]
let ``agreement between two runs is reported as a fact and not as a verdict`` () =
    let request = requestFor changeClassContract unclassified fullEvidence

    let agreeing =
        replay
            (answering (ChoiceSelected("mechanical-propagation", None, None)))
            LogicalReplay
            request
            (Decided
                { Choice = MechanicalPropagation
                  Provider = fakeIdentity
                  Confidence = None
                  EvidenceUsed = []
                  Rationale = None
                  RationaleMentionsOtherChoices = []
                  DecidedAgainst = unclassified.Fingerprint
                  DecidedAt = now })

    Assert.Equal(Some true, Replay.agrees agreeing)

    // Where either side produced no decision there is nothing to compare,
    // and the answer is "unknown" rather than "disagreed".
    let noDecision =
        replay (answering (ProviderFailed(Unavailable "x"))) LogicalReplay request (InsufficientEvidence [])

    Assert.Equal(None, Replay.agrees noDecision)
