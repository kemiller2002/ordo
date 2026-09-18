/// The one test that spends money, and only when asked to.
///
/// The normal suite must never need a network, an API key or a paid call
/// (ORDO-3607 / ORDO-3-191), so this test reports itself as skipped unless
/// `ORDO_LIVE_PROVIDER_TESTS` is set, and says why in the skip reason rather
/// than passing silently on a machine that never ran it (ORDO-3-239 /
/// ORDO-10403).
///
///     ORDO_LIVE_PROVIDER_TESTS=1 ANTHROPIC_API_KEY=... \
///       dotnet test tests/Ordo.Tests
module Ordo.Tests.LiveAdapterTests

open System
open System.Threading
open Xunit
open Anthropic
open Ordo.Decisions.Outcome
open Ordo.Decisions.Resolve
open Ordo.Providers.Anthropic
open Ordo.Tests.Fixtures

/// A fact that runs only when the live-provider switch and a credential are
/// both present.
type LiveProviderFactAttribute() =
    inherit FactAttribute()

    do
        let enabled =
            not (String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable "ORDO_LIVE_PROVIDER_TESTS"))

        let credentialled =
            not (String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable "ANTHROPIC_API_KEY"))

        if not enabled then
            base.Skip <- "live-provider tests are opt-in; set ORDO_LIVE_PROVIDER_TESTS=1 to run them"
        elif not credentialled then
            base.Skip <- "ORDO_LIVE_PROVIDER_TESTS is set but ANTHROPIC_API_KEY is not"

/// The model this test asks. Named explicitly rather than taken from an
/// alias, so that what answered is recoverable from the record
/// (ORDO-10504).
[<Literal>]
let private Model = "claude-opus-5"

[<LiveProviderFact>]
let ``a live provider answers the bounded contract within its choice space`` () =
    use client = new AnthropicClient()
    let provider = Adapter.create client (Adapter.AnthropicOptions.forModel Model)

    let request =
        requestFor changeClassContract (snapshotOf (siteState "Transition.fs" Unclassified "r1")) fullEvidence

    let record =
        (Resolve.execute ResolveOptions.standard provider request CancellationToken.None)
            .GetAwaiter()
            .GetResult()

    // What is asserted is the contract, not the answer. Which of the three
    // classes a live model picks is a judgment; that whatever it picks is
    // one of the three, or a typed escalation, is the architecture.
    match record.Outcome with
    | Decided result ->
        Assert.Contains(result.Choice, [ MechanicalPropagation; SemanticChange; BoundaryChange ])
        Assert.Equal(request.State.Fingerprint, result.DecidedAgainst)

        match result.Confidence with
        | Some confidence ->
            Assert.InRange(confidence.Magnitude, 0.0, 1.0)
            Assert.False(Ordo.Decisions.Confidence.Confidence.isCalibrated confidence)
        | None -> ()
    | InsufficientEvidence _
    | RequiresDeliberation _
    | RequiresHumanReview _ -> ()
    | ProviderFailure error -> failwithf "the live provider failed: %A" error
    | ResolutionCancelled -> failwith "the live call was cancelled, which this test never does"

    Assert.Equal(Some Model, record.Observation.Provider |> Option.bind (fun p -> p.Model))
    Assert.Equal(Some Contract.AdapterVersion, record.Observation.Provider |> Option.map (fun p -> p.AdapterVersion))
