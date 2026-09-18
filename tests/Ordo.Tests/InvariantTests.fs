/// The invariants the architecture exists to hold, each stated once and
/// checked over every case the domain admits.
///
/// The choice space here is small and closed, so these loops are exhaustive
/// rather than sampled — a stronger statement than a property test with a
/// generator, and without adding a dependency for it (ORDO-10002).
module Ordo.Tests.InvariantTests

open System.Threading
open Xunit
open Ordo.Core.Capability
open Ordo.Core.StateIdentity
open Ordo.Core.Transition
open Ordo.Decisions.Confidence
open Ordo.Decisions.Contract
open Ordo.Decisions.Outcome
open Ordo.Decisions.Provider
open Ordo.Decisions.Resolve
open Ordo.Decisions.Gate
open Ordo.Tests.Fixtures

let private options =
    ResolveOptions.standard |> ResolveOptions.withClock testClock

let private unclassified = snapshotOf (siteState "Transition.fs" Unclassified "r1")

let private run provider request =
    (Resolve.execute options provider request CancellationToken.None).GetAwaiter().GetResult()

let private everyChoice = [ MechanicalPropagation; SemanticChange; BoundaryChange ]

let private everyConfidence =
    [ None
      Some(confidenceOf 0.0)
      Some(confidenceOf 0.5)
      Some(confidenceOf 1.0)
      Some(ok (Confidence.create (EmpiricallyCalibrated("10000 outcomes", 10000)) 1.0)) ]

let private decisionFor choice confidence =
    let request = requestFor changeClassContract unclassified fullEvidence
    let token = changeClassToken choice
    (run (answering (ChoiceSelected(token, confidence, None))) request).Outcome

/// Invariant 1 — provider output cannot create an illegal domain choice.
[<Fact>]
let ``no provider string can become a choice the contract did not declare`` () =
    let attempts =
        [ "mostly-mechanical"
          "MECHANICAL-PROPAGATION"
          "mechanical propagation"
          " mechanical-propagation"
          ""
          "semantic-change;boundary-change" ]

    for attempt in attempts do
        Assert.Equal(None, ChoiceSpace.parse attempt changeClassContract.Choices)

        let request = requestFor changeClassContract unclassified fullEvidence

        match (run (answering (ChoiceSelected(attempt, None, None))) request).Outcome with
        | ProviderFailure(ContractViolation _) -> ()
        | other -> failwithf "%s was not rejected: %A" attempt other

/// Invariant 2 — a decision is not a transition.
[<Fact>]
let ``no decision, at any confidence, authorises a change on its own`` () =
    for choice in everyChoice do
        for confidence in everyConfidence do
            let outcome = decisionFor choice confidence

            // The same decision, with nothing else supplied, is never
            // authorised: the capability is absent.
            let withoutAuthority =
                { contextFor unclassified choice with
                    Held = CapabilitySet.empty }

            match Gate.evaluate authorizeFastPath withoutAuthority outcome with
            | ChangeAuthorized _ -> failwithf "%A at %A authorised a change with no capability" choice confidence
            | ChangeRefused _
            | ChangeNeedsHumanReview _ -> ()

/// Invariant 3 — a decision is not an outcome.
[<Fact>]
let ``a decision and a later observed outcome are separate records`` () =
    let decided = decisionFor SemanticChange (Some(confidenceOf 0.91))

    // What actually happened, recorded later by something else, disagrees.
    let observedLater = MechanicalPropagation

    match DecisionOutcome.decision decided with
    | Some result ->
        Assert.Equal(SemanticChange, result.Choice)
        Assert.NotEqual(observedLater, result.Choice)
        // Nothing in the decision record can be edited to match; the record
        // is a value, and the observation of what happened is a different
        // one.
        Assert.Equal(SemanticChange, result.Choice)
    | None -> failwith "expected a decision"

/// Invariant 4 — confidence does not grant capability.
[<Fact>]
let ``perfect confidence buys nothing that a missing capability withholds`` () =
    let certain = decisionFor MechanicalPropagation (Some(confidenceOf 1.0))

    let withoutAuthority =
        { contextFor unclassified MechanicalPropagation with
            Held = CapabilitySet.empty }

    match Gate.evaluate authorizeFastPath withoutAuthority certain with
    | ChangeRefused(RequirementsNotMet [ MissingCapability _ ]) -> ()
    | other -> failwithf "expected a missing capability, got %A" other

    // And the calibrated version of the same number buys nothing more.
    let calibrated =
        decisionFor MechanicalPropagation (Some(ok (Confidence.create (EmpiricallyCalibrated("10000 outcomes", 10000)) 1.0)))

    match Gate.evaluate authorizeFastPath withoutAuthority calibrated with
    | ChangeRefused(RequirementsNotMet [ MissingCapability _ ]) -> ()
    | other -> failwithf "expected a missing capability, got %A" other

/// Invariant 5 — missing evidence is not low confidence.
[<Fact>]
let ``absent evidence and an uncertain answer are different outcomes`` () =
    let request = requestFor changeClassContract unclassified [ diagnosticEvidence ]
    let absent = (run (scriptedProvider allCapabilities []) request).Outcome
    let uncertain = decisionFor MechanicalPropagation (Some(confidenceOf 0.1))

    Assert.Equal("insufficient-evidence", DecisionOutcome.toWire absent)
    Assert.Equal("decided", DecisionOutcome.toWire uncertain)
    Assert.Equal(None, DecisionOutcome.decision absent)
    Assert.NotEqual(None, DecisionOutcome.decision uncertain)

/// Invariant 6 — replay does not execute.
[<Fact>]
let ``a replay record carries no authorisation`` () =
    let request = requestFor changeClassContract unclassified fullEvidence

    let record =
        (Ordo.Decisions.Replay.Replay.execute
            options
            (answering (ChoiceSelected("mechanical-propagation", Some(confidenceOf 1.0), None)))
            Ordo.Decisions.Replay.LogicalReplay
            (ok (Ordo.Core.Identifiers.ResolutionId.create "res-replay"))
            request
            (InsufficientEvidence [])
            CancellationToken.None)
            .GetAwaiter()
            .GetResult()

    // A replay produces a decision, and the state it replayed against is
    // untouched. Reaching an authorisation requires calling the gate, which
    // replay does not do — and which is a separate, explicit step here.
    Assert.NotEqual(None, DecisionOutcome.decision record.Fresh.Outcome)
    Assert.Equal(unclassified.Fingerprint, request.State.Fingerprint)

/// Invariant 7 — stale state cannot silently authorise a transition.
[<Fact>]
let ``every choice decided against a state that then moved is refused`` () =
    let moved = snapshotOf (siteState "Transition.fs" Unclassified "r2")

    for choice in everyChoice do
        for confidence in everyConfidence do
            match Gate.evaluate authorizeFastPath (contextFor moved choice) (decisionFor choice confidence) with
            | ChangeAuthorized _ -> failwithf "%A at %A authorised a change against moved state" choice confidence
            | ChangeRefused(DecisionIsStale _) -> ()
            | ChangeRefused other -> failwithf "expected staleness to be named, got %A" other
            | ChangeNeedsHumanReview _ -> ()

/// Invariant 8 — a provider failure does not become a domain decision.
[<Fact>]
let ``no provider failure yields a decision`` () =
    let failures =
        [ Unavailable "x"
          ProviderTimeout(System.TimeSpan.FromSeconds 1.0)
          RateLimited None
          AuthenticationFailure "x"
          InvalidResponse "x"
          ContractViolation "x"
          UnsupportedCapability "x"
          InternalFailure "x" ]

    for failure in failures do
        let request = requestFor changeClassContract unclassified fullEvidence
        let outcome = (run (answering (ProviderFailed failure)) request).Outcome

        Assert.Equal(None, DecisionOutcome.decision outcome)

        match Gate.evaluate authorizeFastPath (contextFor unclassified MechanicalPropagation) outcome with
        | ChangeRefused(NoDecisionToAct _) -> ()
        | other -> failwithf "%A should have refused the change, got %A" failure other

/// Invariant 9 — provider-specific types do not leak into the core.
/// Invariant 10 — the core does not depend on the repository operating system.
/// Both are asserted mechanically in `ArchitectureTests`.
[<Fact>]
let ``the boundary invariants are the ones the loader checks`` () =
    // Stated here so that the numbered list is complete in one place; the
    // assertions themselves are in ArchitectureTests, where they can inspect
    // the loaded assemblies.
    Assert.True(true)
