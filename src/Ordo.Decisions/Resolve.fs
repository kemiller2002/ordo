/// Running one bounded decision, end to end.
///
/// This is the only place a provider is actually called, and it is
/// deliberately thin: check what can be checked without spending anything,
/// build the provider's view of the request, call once, validate what comes
/// back, and record the facts. There is no orchestration, no scheduling, no
/// routing and no fallback chain — a caller holds the provider it chose, and
/// a hidden switch to a different one is prohibited (ORDO-9101 /
/// ORDO-3-156).
///
/// The order of the checks is the point. Capability and evidence are settled
/// before anything leaves the process, so a request that cannot succeed
/// costs nothing and, more importantly, an absent requirement is reported as
/// absent rather than arriving back as a provider's guess (ORDO-9202).
module Ordo.Decisions.Resolve

open System
open System.Threading
open System.Threading.Tasks
open Ordo.Core.Json
open Ordo.Core.Clock
open Ordo.Core.Evidence
open Ordo.Core.Coverage
open Ordo.Core.Resolution
open Ordo.Core.StateIdentity
open Ordo.Decisions.Contract
open Ordo.Decisions.Request
open Ordo.Decisions.Outcome
open Ordo.Decisions.Provider
open Ordo.Decisions.Escalation
open Ordo.Decisions.Observation

/// How a caller wants this resolution run.
type ResolveOptions =
    { Clock: Clock
      /// Applied to the state view before it crosses the provider boundary.
      /// The default is `id`, which sends the view unchanged — a domain
      /// with anything sensitive in its state supplies a real one
      /// (ORDO-5805 / ORDO-7401).
      Redact: Redaction
      /// Whether to ask for a confidence value at all.
      RequestConfidence: bool
      /// Capabilities this contract needs from whatever provider answers it.
      RequiredCapabilities: ProviderCapability list
      ExperimentReference: string option }

/// Everything one resolution produced.
type ResolutionRecord<'choice> =
    { Outcome: DecisionOutcome<'choice>
      Observation: ResolutionObservation
      Escalation: EscalationChain }

[<RequireQualifiedAccess>]
module ResolveOptions =

    /// Defaults that hide nothing: the real clock, no redaction, confidence
    /// asked for, and bounded-choice selection required of the provider.
    let standard: ResolveOptions =
        { Clock = system
          Redact = id
          RequestConfidence = true
          RequiredCapabilities = [ BoundedChoiceSelection ]
          ExperimentReference = None }

    let withRedaction (redact: Redaction) (options: ResolveOptions) = { options with Redact = redact }

    let withClock (clock: Clock) (options: ResolveOptions) = { options with Clock = clock }

    let withoutConfidence (options: ResolveOptions) = { options with RequestConfidence = false }

[<RequireQualifiedAccess>]
module Resolve =

    /// The provider's view of a request: tokens, strings and wire values.
    let toProviderRequest (options: ResolveOptions) (request: DecisionRequest<'choice>) : ProviderRequest =
        { Resolution = request.Resolution
          Request = request.Id
          Contract = request.Contract.Id
          ContractVersion = request.Contract.Version
          Question = request.Contract.Question
          Scope = request.Contract.Scope
          LegalChoices = ChoiceSpace.tokens request.Contract.Choices
          RequiredEvidence = request.Contract.RequiredEvidence |> List.map (fun r -> r.Id, r.Description)
          RequiredCoverage =
            request.Contract.RequiredCoverage
            |> List.map (fun r -> CoverageScope.value r.Scope, r.Description)
          Coverage =
            request.Coverage
            |> List.map (fun claim ->
                { Scope = claim.Scope |> CoverageScope.value
                  Status = claim.Status |> CoverageStatus.toWire
                  ProvenanceEvidenceIds = claim.Provenance |> List.map Ordo.Core.Identifiers.EvidenceId.value })
          Evidence =
            request.Evidence
            |> List.map (fun e ->
                { Id = Ordo.Core.Identifiers.EvidenceId.value e.Id
                  Kind = EvidenceKind.toWire e.Kind
                  Source = e.Source.System
                  ObservedAt = toWire e.ObservedAt
                  Content = e.Content })
          StateView = StateSnapshot.providerView options.Redact request.State
          ConfidenceRequested = options.RequestConfidence }

    let private escalationFor (request: DecisionRequest<'choice>) (now: DateTimeOffset) (outcome: DecisionOutcome<'choice>) =
        let step target reason =
            EscalationChain.append
                { Resolution = request.Resolution
                  From = Decide
                  To = target
                  Reason = reason
                  At = now }
                EscalationChain.empty

        match outcome with
        | RequiresDeliberation reason ->
            let described =
                match reason with
                | EvidenceRequiresInvestigation requirements ->
                    sprintf "evidence requires investigation: %s" (String.Join("; ", requirements |> List.map (fun r -> r.Id)))
                | OutsideContractScope note -> sprintf "outside contract scope: %s" note
                | ProviderDeclined note -> sprintf "provider declined: %s" note

            step ToDeliberation described
        | RequiresHumanReview reason ->
            let described =
                match reason with
                | PolicyRequiresPerson reason -> sprintf "policy requires a person: %s" reason
                | ConsequenceRequiresPerson consequence -> sprintf "consequence requires a person: %s" consequence
                | ProviderRequestedPerson note -> sprintf "provider requested a person: %s" note

            step ToHumanReview described
        | Decided _
        | InsufficientEvidence _
        | InsufficientCoverage _
        | ProviderFailure _
        | ResolutionCancelled -> EscalationChain.empty

    let private observe
        (options: ResolveOptions)
        (request: DecisionRequest<'choice>)
        (startedAt: DateTimeOffset)
        (completedAt: DateTimeOffset)
        (identity: ProviderIdentity option)
        (usage: ProviderUsage)
        (retries: int)
        (escalation: EscalationChain)
        (outcome: DecisionOutcome<'choice>)
        =
        let selected =
            DecisionOutcome.decision outcome
            |> Option.bind (fun result -> ChoiceSpace.tokenOf result.Choice request.Contract.Choices)

        // DecisionRequest.create refuses unversioned legacy snapshots, so a
        // live execution reaching this point always has current view identity.
        let stateViewSchema =
            request.State.ViewSchema
            |> Option.defaultWith (fun () -> invalidOp "a live DecisionRequest must carry a state-view schema")

        { Resolution = request.Resolution
          Correlation = request.Correlation
          CausedBy = request.CausedBy
          Mode = Decide
          Contract = request.Contract.Id
          ContractVersion = request.Contract.Version
          Request = request.Id
          State = request.State.Fingerprint
          StateViewSchema = stateViewSchema
          Coverage = request.Coverage
          Provider = identity
          StartedAt = startedAt
          CompletedAt = completedAt
          Outcome = DecisionOutcome.toWire outcome
          SelectedChoice = selected
          Confidence =
            DecisionOutcome.decision outcome
            |> Option.bind (fun result -> ResolutionObservation.confidenceFacts result.Confidence)
          EvidenceUsed =
            DecisionOutcome.decision outcome
            |> Option.map (fun result -> result.EvidenceUsed)
            |> Option.defaultValue []
          Escalation = EscalationChain.steps escalation
          Transition = None
          Policy = None
          Usage = usage
          TransportRetries = retries
          ExperimentReference = options.ExperimentReference
          // Copied from the host-built request. Nothing the provider
          // returned can reach this field (DF-SDE-2026-D68A).
          RequestedBy = request.RequestedBy }

    /// Runs one decision request against one provider.
    ///
    /// Never throws for an expected failure: a provider that is unreachable,
    /// times out, breaks its contract or throws an unexpected exception all
    /// arrive back as a typed outcome, because a defect in an intelligence
    /// provider must not become an exception in a domain (ORDO-2701).
    let execute
        (options: ResolveOptions)
        (provider: DecisionProvider)
        (request: DecisionRequest<'choice>)
        (cancellation: CancellationToken)
        : Task<ResolutionRecord<'choice>> =
        task {
            let startedAt = options.Clock()

            let finish identity usage retries outcome =
                let completedAt = options.Clock()
                let escalation = escalationFor request completedAt outcome

                { Outcome = outcome
                  Observation = observe options request startedAt completedAt identity usage retries escalation outcome
                  Escalation = escalation }

            let missingCapabilities =
                DecisionProvider.missingCapabilities options.RequiredCapabilities provider

            let checks = DecisionRequest.checkEvidence startedAt request
            let unmet = Evidence.unmet checks
            let coverageFailures = DecisionRequest.checkCoverage request

            let satisfyingEvidence =
                checks
                |> List.choose (function
                    | Satisfied(_, evidence) -> Some evidence.Id
                    | Stale _
                    | WrongKind _
                    | Unsatisfied _ -> None)

            match missingCapabilities, unmet, coverageFailures with
            | missing :: _, _, _ ->
                return
                    finish
                        (Some provider.Identity)
                        ProviderUsage.unreported
                        0
                        (ProviderFailure(UnsupportedCapability(ProviderCapability.toWire missing)))
            | [], _ :: _, _ ->
                // Required evidence is absent, stale or of the wrong kind.
                // The provider is not asked: an answer given without the
                // evidence the contract requires would be a guess wearing a
                // decision's clothes.
                return finish None ProviderUsage.unreported 0 (InsufficientEvidence unmet)
            | [], [], _ :: _ ->
                // Required context coverage is absent, known-partial, or
                // unknown. A provider is not allowed to turn that into an
                // answer by guessing beyond the observed scope.
                return finish None ProviderUsage.unreported 0 (InsufficientCoverage coverageFailures)
            | [], [], [] ->
                let providerRequest = toProviderRequest options request

                let! outcome =
                    task {
                        try
                            let! result = provider.Resolve providerRequest cancellation

                            let validated =
                                Validation.validate
                                    request.Contract
                                    request.State
                                    satisfyingEvidence
                                    result.Identity
                                    (options.Clock())
                                    result.Response

                            return Ok(result, validated)
                        with
                        | :? OperationCanceledException -> return Error(None, ResolutionCancelled)
                        | ex -> return Error(None, ProviderFailure(InternalFailure ex.Message))
                    }

                match outcome with
                | Ok(result, validated) -> return finish (Some result.Identity) result.Usage result.TransportRetries validated
                | Error(_, failure) -> return finish (Some provider.Identity) ProviderUsage.unreported 0 failure
        }
