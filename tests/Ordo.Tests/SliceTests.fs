/// The vertical slice, end to end, against a deterministic provider.
///
/// Domain state -> request -> provider -> validated outcome -> policy,
/// capability, evidence and state-version checks -> authorised, refused or
/// escalated -> observation facts (ORDO-3-122).
module Ordo.Tests.SliceTests

open System
open System.Threading
open Xunit
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.Coverage
open Ordo.Core.Capability
open Ordo.Core.Resolution
open Ordo.Core.StateIdentity
open Ordo.Core.Transition
open Ordo.Decisions.Contract
open Ordo.Decisions.Request
open Ordo.Decisions.Outcome
open Ordo.Decisions.Provider
open Ordo.Decisions.Escalation
open Ordo.Decisions.Observation
open Ordo.Decisions.Resolve
open Ordo.Decisions.Gate
open Ordo.Tests.Fixtures

let private options =
    ResolveOptions.standard |> ResolveOptions.withClock testClock

let private unclassified = snapshotOf (siteState "Transition.fs" Unclassified "r1")

let private run provider request =
    (Resolve.execute options provider request CancellationToken.None).GetAwaiter().GetResult()

// ------------------------------------------------------------ the happy path

[<Fact>]
let ``a decided mechanical propagation authorises the fast path`` () =
    let request = requestFor changeClassContract unclassified fullEvidence

    let record =
        run (answering (ChoiceSelected("mechanical-propagation", Some(confidenceOf 0.85), None))) request

    match record.Outcome with
    | Decided result ->
        Assert.Equal(MechanicalPropagation, result.Choice)

        match Gate.evaluate authorizeFastPath (contextFor unclassified result.Choice) record.Outcome with
        | ChangeAuthorized authorization ->
            Assert.Equal("authorize-fast-path", authorization.Name)
            Assert.Equal(unclassified.Fingerprint, authorization.State)
            Assert.Equal(fastPathPolicy.Identity, authorization.Policy)
        | other -> failwithf "expected the change to be authorised, got %A" other
    | other -> failwithf "expected Decided, got %A" other

[<Fact>]
let ``the observation records what happened and nothing it would have to conclude`` () =
    let request = requestFor changeClassContract unclassified fullEvidence

    let record =
        run (answering (ChoiceSelected("mechanical-propagation", Some(confidenceOf 0.85), None))) request

    let observation = record.Observation

    Assert.Equal(request.Resolution, observation.Resolution)
    Assert.Equal(changeClassContract.Id, observation.Contract)
    Assert.Equal(changeClassContract.Version, observation.ContractVersion)
    Assert.Equal(request.Id, observation.Request)
    Assert.Equal(unclassified.Fingerprint, observation.State)
    Assert.Equal(changeSiteViewSchema, observation.StateViewSchema)
    Assert.Empty(observation.Coverage)
    Assert.Equal(Decide, observation.Mode)
    Assert.Equal("decided", observation.Outcome)
    Assert.Equal(Some "mechanical-propagation", observation.SelectedChoice)
    Assert.Equal(Some(0.85, "provider-reported"), observation.Confidence)
    Assert.Equal(Some fakeIdentity, observation.Provider)
    Assert.Equal(Some 100, observation.Usage.InputTokens)
    Assert.Equal(0, observation.TransportRetries)
    Assert.Equal(None, observation.Transition)
    Assert.Equal<EvidenceId list>([ diagnosticEvidence.Id; diffEvidence.Id ], observation.EvidenceUsed)

    // Timing is observable without Ordo owning a metrics store.
    Assert.Equal(TimeSpan.Zero, ResolutionObservation.duration observation)

    // The wire form is readable JSON carrying its own schema identity.
    match parse (render (ResolutionObservation.encode observation)) with
    | Ok encoded ->
        Assert.Equal(Ok(Some(JString "ordo.resolution-observation")), tryMember "schema" encoded)
        Assert.Equal(Ok(Some(JInt 2L)), tryMember "schemaVersion" encoded)
        match tryMember "stateViewSchema" encoded with
        | Ok(Some(JObject schema)) ->
            Assert.Contains(("id", JString "sde.change-site-state"), schema)
            Assert.Contains(("version", JInt 1L), schema)
        | other -> failwithf "expected state-view schema identity in observation v2, got %A" other
        Assert.Equal(Ok(Some(JArray [])), tryMember "coverage" encoded)
    | Error error -> failwithf "the observation did not render as readable JSON: %A" error

[<Fact>]
let ``a transition result can be added to an observation once one is known`` () =
    let request = requestFor changeClassContract unclassified fullEvidence
    let record = run (answering (ChoiceSelected("mechanical-propagation", None, None))) request
    let gate = Gate.evaluate authorizeFastPath (contextFor unclassified MechanicalPropagation) record.Outcome

    let observed =
        record.Observation
        |> ResolutionObservation.withTransition (Gate.toWire gate)
        |> ResolutionObservation.withPolicy fastPathPolicy.Identity
        |> ResolutionObservation.withExperimentReference "WI-0038"

    Assert.Equal(Some "authorized", observed.Transition)
    Assert.Equal(Some fastPathPolicy.Identity, observed.Policy)
    Assert.Equal(Some "WI-0038", observed.ExperimentReference)

// ------------------------------------------------------------- the refusals

[<Fact>]
let ``missing required evidence stops the request before a provider is asked`` () =
    let request = requestFor changeClassContract unclassified [ diagnosticEvidence ]

    // The scripted provider fails loudly if it is called, so reaching the
    // assertion is itself the proof that nothing was spent.
    let record = run (scriptedProvider allCapabilities []) request

    match record.Outcome with
    | InsufficientEvidence [ requirement ] -> Assert.Equal("site-diff", requirement.Id)
    | other -> failwithf "expected InsufficientEvidence, got %A" other

    Assert.Equal(None, record.Observation.Provider)
    Assert.Equal("insufficient-evidence", record.Observation.Outcome)

[<Fact>]
let ``partial required coverage stops resolution before a provider is asked`` () =
    let scope = ok (CoverageScope.create "strata.relation-access")
    let requirement = CoverageRequirement.complete scope "relation access must be fully established"
    let contract = changeClassContract |> DecisionContract.requiringCoverage [ requirement ]
    let observation = evidence "relation-access-observation" Direct now (JString "hidden.secret is visible but unreadable")
    let claim = ok (ContextCoverageClaim.create scope Partial [ observation.Id ])

    let request =
        ok (
            DecisionRequest.createWithCoverage
                (ok (DecisionRequestId.create "coverage-partial"))
                (ok (ResolutionId.create "coverage-partial-resolution"))
                contract
                unclassified
                (observation :: fullEvidence)
                [ claim ]
                now
        )

    let record = run (scriptedProvider allCapabilities []) request

    match record.Outcome with
    | InsufficientCoverage [ CoveragePartial(actualRequirement, actualClaim) ] ->
        Assert.Equal(scope, actualRequirement.Scope)
        Assert.Equal(Partial, actualClaim.Status)
    | other -> failwithf "expected InsufficientCoverage/Partial, got %A" other

    Assert.Equal(None, record.Observation.Provider)
    Assert.Equal("insufficient-coverage", record.Observation.Outcome)
    Assert.Equal<ContextCoverageClaim list>([ claim ], record.Observation.Coverage)

[<Fact>]
let ``complete required coverage permits resolution while other scopes may remain partial`` () =
    let relations = ok (CoverageScope.create "strata.relations")
    let access = ok (CoverageScope.create "strata.relation-access")
    let requirement = CoverageRequirement.complete relations "relations must be fully enumerated"
    let contract = changeClassContract |> DecisionContract.requiringCoverage [ requirement ]
    let observation = evidence "strata-scope-observation" Direct now (JString "relations complete; hidden.secret unreadable")
    let claims =
        [ ok (ContextCoverageClaim.create relations Complete [ observation.Id ])
          ok (ContextCoverageClaim.create access Partial [ observation.Id ]) ]

    let request =
        ok (
            DecisionRequest.createWithCoverage
                (ok (DecisionRequestId.create "coverage-mixed"))
                (ok (ResolutionId.create "coverage-mixed-resolution"))
                contract
                unclassified
                (observation :: fullEvidence)
                claims
                now
        )

    let record = run (answering (ChoiceSelected("mechanical-propagation", None, None))) request

    match record.Outcome with
    | Decided result -> Assert.Equal(MechanicalPropagation, result.Choice)
    | other -> failwithf "expected the complete required scope to permit resolution, got %A" other

    Assert.Equal<ContextCoverageClaim list>(claims, record.Observation.Coverage)

    match parse (render (ResolutionObservation.encode record.Observation)) with
    | Ok encoded ->
        match tryMember "coverage" encoded with
        | Ok(Some(JArray encodedClaims)) -> Assert.Equal(2, encodedClaims.Length)
        | other -> failwithf "expected scoped coverage in observation v2, got %A" other
    | Error error -> failwithf "the coverage observation did not render as readable JSON: %A" error

    let providerRequest = Resolve.toProviderRequest options request
    Assert.Equal<(string * string) list>(
        [ "strata.relations", "relations must be fully enumerated" ],
        providerRequest.RequiredCoverage
    )

    Assert.Contains(providerRequest.Coverage, fun claim -> claim.Scope = "strata.relations" && claim.Status = "complete")
    Assert.Contains(providerRequest.Coverage, fun claim -> claim.Scope = "strata.relation-access" && claim.Status = "partial")

[<Fact>]
let ``unknown Time Tracking reference-catalog coverage is not treated as an empty complete catalog`` () =
    let scope = ok (CoverageScope.create "chrona.reference-catalog")
    let requirement = CoverageRequirement.complete scope "current reference.json must be established"
    let contract = changeClassContract |> DecisionContract.requiringCoverage [ requirement ]
    let failedPull = evidence "reference-pull" Direct now (JString "network error")
    let claim = ok (ContextCoverageClaim.create scope Unknown [ failedPull.Id ])

    let request =
        ok (
            DecisionRequest.createWithCoverage
                (ok (DecisionRequestId.create "reference-catalog-unknown"))
                (ok (ResolutionId.create "reference-catalog-unknown-resolution"))
                contract
                unclassified
                (failedPull :: fullEvidence)
                [ claim ]
                now
        )

    let record = run (scriptedProvider allCapabilities []) request

    match record.Outcome with
    | InsufficientCoverage [ CoverageUnknown(_, actualClaim) ] -> Assert.Equal(Unknown, actualClaim.Status)
    | other -> failwithf "expected unknown reference-catalog coverage to stop resolution, got %A" other

[<Fact>]
let ``a contract needing a capability the provider lacks fails before the call`` () =
    let request = requestFor changeClassContract unclassified fullEvidence

    let record = run (scriptedProvider [ LocalExecution ] []) request

    match record.Outcome with
    | ProviderFailure(UnsupportedCapability capability) -> Assert.Equal("bounded-choice-selection", capability)
    | other -> failwithf "expected an unsupported capability, got %A" other

[<Fact>]
let ``a semantic change is decided and still refused the fast path`` () =
    let request = requestFor changeClassContract unclassified fullEvidence
    let record = run (answering (ChoiceSelected("semantic-change", Some(confidenceOf 0.99), None))) request

    match Gate.evaluate authorizeFastPath (contextFor unclassified SemanticChange) record.Outcome with
    | ChangeRefused(RequirementsNotMet failures) ->
        Assert.Contains(failures, (function
            | PolicyRejected(identity, _) -> identity = fastPathPolicy.Identity
            | _ -> false))
    | other -> failwithf "expected a policy refusal, got %A" other

[<Fact>]
let ``a boundary change routes to a person rather than to a refusal`` () =
    let request = requestFor changeClassContract unclassified fullEvidence
    let record = run (answering (ChoiceSelected("boundary-change", None, None))) request

    match Gate.evaluate authorizeFastPath (contextFor unclassified BoundaryChange) record.Outcome with
    | ChangeNeedsHumanReview(reason, outstanding) ->
        Assert.Contains("boundary change", reason)
        Assert.Empty outstanding
    | other -> failwithf "expected human review, got %A" other

[<Fact>]
let ``without the capability nothing else about the decision matters`` () =
    let request = requestFor changeClassContract unclassified fullEvidence
    let record = run (answering (ChoiceSelected("mechanical-propagation", Some(confidenceOf 1.0), None))) request

    let context =
        { contextFor unclassified MechanicalPropagation with
            Held = CapabilitySet.empty }

    match Gate.evaluate authorizeFastPath context record.Outcome with
    | ChangeRefused(RequirementsNotMet [ MissingCapability [ missing ] ]) ->
        Assert.Equal(authorizeVerificationPath.Id, missing)
    | other -> failwithf "expected a missing capability, got %A" other

[<Fact>]
let ``an already classified site is the wrong source state for this change`` () =
    let classified = snapshotOf (siteState "Transition.fs" FullPathRequired "r1")
    let request = requestFor changeClassContract classified fullEvidence
    let record = run (answering (ChoiceSelected("mechanical-propagation", None, None))) request

    match Gate.evaluate authorizeFastPath (contextFor classified MechanicalPropagation) record.Outcome with
    | ChangeRefused(RequirementsNotMet failures) ->
        Assert.Contains(failures, (function
            | InvalidState(expected, _) -> expected = "verification is unclassified"
            | _ -> false))
    | other -> failwithf "expected an invalid source state, got %A" other

[<Fact>]
let ``a decision made against one state cannot authorise a change to another`` () =
    let request = requestFor changeClassContract unclassified fullEvidence
    let record = run (answering (ChoiceSelected("mechanical-propagation", Some(confidenceOf 1.0), None))) request

    // The site moved on while the decision was being made.
    let moved = snapshotOf (siteState "Transition.fs" Unclassified "r2")

    match Gate.evaluate authorizeFastPath (contextFor moved MechanicalPropagation) record.Outcome with
    | ChangeRefused(DecisionIsStale(decidedAgainst, current)) ->
        Assert.Equal(unclassified.Fingerprint, decidedAgainst)
        Assert.Equal(moved.Fingerprint, current)
    | other -> failwithf "expected a stale decision, got %A" other

[<Fact>]
let ``a caller cannot present a stale decision as a fresh one`` () =
    let request = requestFor changeClassContract unclassified fullEvidence
    let record = run (answering (ChoiceSelected("mechanical-propagation", None, None))) request
    let moved = snapshotOf (siteState "Transition.fs" Unclassified "r2")

    // The caller claims the decision was formed against the current state.
    // The gate takes that from the decision instead.
    let lying =
        { contextFor moved MechanicalPropagation with
            FormedAgainst = moved.Fingerprint }

    match Gate.evaluate authorizeFastPath lying record.Outcome with
    | ChangeRefused(DecisionIsStale _) -> ()
    | other -> failwithf "expected the gate to use the decision's own state, got %A" other

[<Fact>]
let ``an outstanding obligation blocks a change that required it`` () =
    let obligation =
        Ordo.Core.Obligation.Obligation.create (ok (ObligationId.create "ob-1")) (Ordo.Core.Obligation.RunVerification "behaviour test") now

    let requirement =
        authorizeFastPath |> TransitionRequirement.requiringObligations [ obligation.Id ]

    let context =
        { contextFor unclassified MechanicalPropagation with
            Obligations = [ obligation ] }

    let request = requestFor changeClassContract unclassified fullEvidence
    let record = run (answering (ChoiceSelected("mechanical-propagation", None, None))) request

    match Gate.evaluate requirement context record.Outcome with
    | ChangeRefused(RequirementsNotMet [ UnsatisfiedObligation [ id ] ]) -> Assert.Equal(obligation.Id, id)
    | other -> failwithf "expected an unsatisfied obligation, got %A" other

// ------------------------------------------------------------- escalation

[<Fact>]
let ``a decision that escalates records where it went and why`` () =
    let request = requestFor changeClassContract unclassified fullEvidence
    let record = run (answering (DeliberationRequested "this site touches two semantic authorities")) request

    match record.Outcome with
    | RequiresDeliberation(ProviderDeclined note) -> Assert.Equal("this site touches two semantic authorities", note)
    | other -> failwithf "expected RequiresDeliberation, got %A" other

    match EscalationChain.steps record.Escalation with
    | [ step ] ->
        Assert.Equal(Decide, step.From)
        Assert.Equal(ToDeliberation, step.To)
        Assert.Contains("two semantic authorities", step.Reason)
        Assert.Equal(request.Resolution, step.Resolution)
    | other -> failwithf "expected one escalation step, got %A" other

    Assert.Single record.Observation.Escalation |> ignore

[<Fact>]
let ``a human-review escalation is recorded as an escalation, not a failure`` () =
    let request = requestFor changeClassContract unclassified fullEvidence
    let record = run (answering (HumanReviewRequested "the owner should look at this")) request

    match EscalationChain.steps record.Escalation with
    | [ step ] -> Assert.Equal(ToHumanReview, step.To)
    | other -> failwithf "expected one escalation step, got %A" other

    Assert.Equal("requires-human-review", record.Observation.Outcome)

[<Fact>]
let ``an escalation chain keeps every hop rather than only the last`` () =
    let step target reason =
        { Resolution = ok (ResolutionId.create "res-1")
          From = Decide
          To = target
          Reason = reason
          At = now }

    let chain =
        EscalationChain.empty
        |> EscalationChain.append (step ToDeliberation "not bounded yet")
        |> EscalationChain.append (step ToDecide "deliberation bounded it")
        |> EscalationChain.append (step ToHumanReview "policy wants an owner")

    Assert.Equal<EscalationTarget list>(
        [ ToDeliberation; ToDecide; ToHumanReview ],
        EscalationChain.steps chain |> List.map (fun s -> s.To)
    )

// ----------------------------------------------------- failure and cancellation

[<Fact>]
let ``a provider failure never becomes a domain decision`` () =
    let request = requestFor changeClassContract unclassified fullEvidence
    let record = run (answering (ProviderFailed(Unavailable "connection reset"))) request

    Assert.Equal(None, DecisionOutcome.decision record.Outcome)

    match Gate.evaluate authorizeFastPath (contextFor unclassified MechanicalPropagation) record.Outcome with
    | ChangeRefused(NoDecisionToAct outcome) -> Assert.Equal("provider-failure", outcome)
    | other -> failwithf "expected the change to be refused, got %A" other

[<Fact>]
let ``a defective provider that throws does not throw at the domain`` () =
    let request = requestFor changeClassContract unclassified fullEvidence
    let record = run (throwingProvider "adapter bug") request

    match record.Outcome with
    | ProviderFailure(InternalFailure detail) -> Assert.Equal("adapter bug", detail)
    | other -> failwithf "expected a typed internal failure, got %A" other

[<Fact>]
let ``cancelling is not the provider failing`` () =
    let request = requestFor changeClassContract unclassified fullEvidence
    use cancellation = new CancellationTokenSource()
    let running = Resolve.execute options cancellingProvider request cancellation.Token
    cancellation.Cancel()
    let record = running.GetAwaiter().GetResult()

    match record.Outcome with
    | ResolutionCancelled -> ()
    | other -> failwithf "expected ResolutionCancelled, got %A" other

    Assert.Equal("cancelled", record.Observation.Outcome)

// ------------------------------------------------- what reaches the provider

[<Fact>]
let ``the provider sees tokens, redacted state and evidence marked as evidence`` () =
    let view =
        JObject
            [ "site", JString "Transition.fs"
              "verification", JString "unclassified"
              "reviewerEmail", JString "someone@example.com" ]

    let snapshot = snapshotOf view

    let redact =
        function
        | JObject members -> JObject(members |> List.filter (fun (name, _) -> name <> "reviewerEmail"))
        | other -> other

    let request = requestFor changeClassContract snapshot fullEvidence

    let providerRequest =
        Resolve.toProviderRequest (options |> ResolveOptions.withRedaction redact) request

    Assert.Equal<string list>(
        [ "mechanical-propagation"; "semantic-change"; "boundary-change" ],
        providerRequest.LegalChoices
    )

    Assert.Equal<(string * string) list>(
        [ "tool-diagnostic", toolDiagnostic.Description
          "site-diff", siteDiff.Description ],
        providerRequest.RequiredEvidence
    )

    Assert.Equal(Ok None, tryMember "reviewerEmail" providerRequest.StateView)
    Assert.Equal(Ok(Some(JString "Transition.fs")), tryMember "site" providerRequest.StateView)
    Assert.All(providerRequest.Evidence, fun e -> Assert.Equal("direct", e.Kind))
