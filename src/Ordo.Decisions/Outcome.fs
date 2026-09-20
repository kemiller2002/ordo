/// What a resolution attempt produced.
///
/// The cases below are the reason this library exists. "I chose Unsafe",
/// "I lack the evidence to choose anything", "this question is not a bounded
/// choice", "a person must look at this", "the provider broke" and "we
/// cancelled" are five different situations with five different remedies,
/// and a type that collapses any of them into the others has thrown away the
/// information its caller needs (ORDO-1003 / ORDO-2703 / ORDO-3-325).
module Ordo.Decisions.Outcome

open System
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.Coverage
open Ordo.Core.StateIdentity
open Ordo.Decisions.Confidence

/// Who produced a result, in enough detail to compare two runs.
///
/// `Model` and `ModelVersion` are optional because not every provider is a
/// model, and `AdapterVersion` is not, because the adapter's own behaviour —
/// how it renders a contract, what it accepts back — is part of what
/// produced the answer (ORDO-1602 / ORDO-6005).
type ProviderIdentity =
    { Provider: ProviderId
      Model: string option
      ModelVersion: string option
      AdapterVersion: string }

/// What the provider cost, as the provider itself reported it.
///
/// Every field is optional and none is ever invented: a provider that does
/// not report token counts leaves them `None` rather than receiving an
/// estimate (ORDO-1904). No price appears here — normalised cost is an
/// observing system's calculation, not a fact the adapter knows
/// (ORDO-4202).
type ProviderUsage =
    { InputTokens: int option
      OutputTokens: int option
      CachedInputTokens: int option
      ProviderReportedCost: string option }

/// Why a provider call failed, in categories a caller can act on without
/// parsing an error string (ORDO-1604).
type ProviderError =
    | Unavailable of detail: string
    | ProviderTimeout of after: TimeSpan
    | RateLimited of retryAfter: TimeSpan option
    | AuthenticationFailure of detail: string
    /// The response could not be read at all.
    | InvalidResponse of detail: string
    /// The response was readable and broke the contract — an undeclared
    /// choice, a requirement the contract never stated, a confidence value
    /// outside the scale.
    | ContractViolation of detail: string
    | UnsupportedCapability of capability: string
    | InternalFailure of detail: string

/// Why a bounded decision could not stay bounded.
type DeliberationReason =
    /// The evidence on hand does not support any legal choice, and acquiring
    /// it is itself open-ended.
    | EvidenceRequiresInvestigation of EvidenceRequirement list
    /// The question asked is not the question this contract covers — the
    /// work was classified into the wrong resolution mode.
    | OutsideContractScope of note: string
    /// The provider said it could not answer within the contract. Recorded
    /// as an escalation, never as a failure and never as a reason to ask
    /// again (ORDO-6702).
    | ProviderDeclined of note: string

/// Why a person is needed.
///
/// None of these is an error. A human is a resolution source (ORDO-3001).
type HumanReviewReason =
    | PolicyRequiresPerson of reason: string
    | ConsequenceRequiresPerson of consequence: string
    | ProviderRequestedPerson of note: string

/// A decision that was actually made.
type DecisionResult<'choice> =
    { Choice: 'choice
      Provider: ProviderIdentity
      /// Absent whenever the provider could not supply a meaningful one.
      /// Absence is a valid, common answer and is never filled in
      /// (ORDO-6302 / ORDO-6303).
      Confidence: Confidence option
      /// The evidence the result was reached on, by reference.
      EvidenceUsed: EvidenceId list
      /// Optional prose. Supplemental diagnostics only: the typed `Choice`
      /// is the authoritative result, and nothing downstream may re-derive
      /// the answer by reading this (ORDO-6103).
      Rationale: string option
      /// Declared choice tokens the rationale mentions that are not the
      /// choice taken.
      ///
      /// Recorded so that a rationale arguing for a different answer than
      /// the one returned is *visible* rather than silently accepted. It
      /// never changes `Choice`: surfacing a conflict is not reinterpreting
      /// the result (ORDO-6104).
      RationaleMentionsOtherChoices: string list
      /// The state identity this decision was reached against. What makes
      /// staleness detectable later (ORDO-0904).
      DecidedAgainst: StateFingerprint
      DecidedAt: DateTimeOffset }

/// The provider-neutral result of a decision request.
type DecisionOutcome<'choice> =
    | Decided of DecisionResult<'choice>
    /// Required evidence is absent, stale or of the wrong kind. Not a low
    /// confidence, not a negative answer (ORDO-0504 / ORDO-1003).
    | InsufficientEvidence of EvidenceRequirement list
    /// One or more contract-required context scopes were missing, Partial, or
    /// Unknown. Distinct from evidence absence because the remedy is to
    /// establish scope completeness, not merely acquire another fact.
    | InsufficientCoverage of CoverageFailure list
    | RequiresDeliberation of DeliberationReason
    | RequiresHumanReview of HumanReviewReason
    | ProviderFailure of ProviderError
    /// The caller cancelled. Distinct from a provider failure, because the
    /// provider did not fail (ORDO-6602).
    | ResolutionCancelled

[<RequireQualifiedAccess>]
module ProviderUsage =

    /// What an adapter reports when the provider tells it nothing.
    let unreported =
        { InputTokens = None
          OutputTokens = None
          CachedInputTokens = None
          ProviderReportedCost = None }

[<RequireQualifiedAccess>]
module ProviderError =

    let toWire error =
        match error with
        | Unavailable _ -> "unavailable"
        | ProviderTimeout _ -> "timeout"
        | RateLimited _ -> "rate-limited"
        | AuthenticationFailure _ -> "authentication-failure"
        | InvalidResponse _ -> "invalid-response"
        | ContractViolation _ -> "contract-violation"
        | UnsupportedCapability _ -> "unsupported-capability"
        | InternalFailure _ -> "internal-failure"

    /// Whether repeating the identical request is a reasonable response to
    /// this failure.
    ///
    /// True only for transport-level failures. A contract violation is never
    /// retryable: asking again until the provider returns something legal is
    /// the retry-until-preferred-answer anti-pattern (ORDO-6702 /
    /// ORDO-3-149).
    let isTransportTransient error =
        match error with
        | Unavailable _
        | ProviderTimeout _
        | RateLimited _ -> true
        | AuthenticationFailure _
        | InvalidResponse _
        | ContractViolation _
        | UnsupportedCapability _
        | InternalFailure _ -> false

[<RequireQualifiedAccess>]
module DecisionOutcome =

    let toWire outcome =
        match outcome with
        | Decided _ -> "decided"
        | InsufficientEvidence _ -> "insufficient-evidence"
        | InsufficientCoverage _ -> "insufficient-coverage"
        | RequiresDeliberation _ -> "requires-deliberation"
        | RequiresHumanReview _ -> "requires-human-review"
        | ProviderFailure _ -> "provider-failure"
        | ResolutionCancelled -> "cancelled"

    /// The decision, when there is one. Deliberately the only way to reach
    /// inside the outcome, so that callers cannot treat "no decision" as a
    /// default-valued decision.
    let decision outcome =
        match outcome with
        | Decided result -> Some result
        | InsufficientEvidence _
        | InsufficientCoverage _
        | RequiresDeliberation _
        | RequiresHumanReview _
        | ProviderFailure _
        | ResolutionCancelled -> None
