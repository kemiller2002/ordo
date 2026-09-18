/// The boundary an intelligence provider sits behind.
///
/// Everything a provider sees is in this module's vocabulary: tokens,
/// strings and wire values. No provider SDK type, no HTTP type, no F# domain
/// type crosses it (ORDO-1601 / ORDO-3-242). That is what makes a provider
/// replaceable, and what makes the fake provider used by the test suite
/// exactly as legitimate a provider as a frontier model.
///
/// Note the shape of `DecisionProvider`: a record of functions, not an
/// interface hierarchy and not something resolved from a container
/// (ORDO-0206 / ORDO-3-078 / ORDO-3-245). Composition is explicit; a caller
/// holds the provider it chose.
module Ordo.Decisions.Provider

open System.Threading
open System.Threading.Tasks
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Decisions.Confidence
open Ordo.Decisions.Outcome

/// Something a provider can do. Declared rather than assumed, so that a
/// contract needing a capability the provider lacks fails before a request
/// is sent (ORDO-1603 / ORDO-9201 / ORDO-9202).
type ProviderCapability =
    | BoundedChoiceSelection
    /// The provider can be constrained to emit a structured answer, rather
    /// than having one parsed out of prose.
    | NativeStructuredOutput
    | SelfReportedConfidence
    /// The provider supplies probabilities measured against outcomes. Almost
    /// nothing does; declaring it is a strong claim.
    | CalibratedProbability
    | LocalExecution
    | CooperativeCancellation

/// One piece of evidence as a provider may see it.
///
/// Flat and stringly on purpose: this is the outside of the boundary. The
/// `Kind` is carried so a provider can be told that something is an
/// inference rather than an observation, and `Content` is a wire value so
/// that instructions and evidence stay structurally separate rather than
/// being concatenated into one blob of prose (ORDO-7603).
type ProviderEvidence =
    { Id: string
      Kind: string
      Source: string
      ObservedAt: string
      Content: JsonValue }

/// What an adapter is given to work with.
type ProviderRequest =
    { Resolution: ResolutionId
      Request: DecisionRequestId
      Contract: DecisionContractId
      ContractVersion: ContractVersion
      Question: string
      Scope: string
      /// Every legal answer, as a token. The answer must be one of these
      /// exactly; nothing else parses.
      LegalChoices: string list
      /// Requirement id and description for each piece of evidence the
      /// contract requires, so the provider can say which one it lacks
      /// rather than saying it is unsure.
      RequiredEvidence: (string * string) list
      Evidence: ProviderEvidence list
      /// The state as the provider may see it — already redacted by the
      /// caller's own rule. The unredacted view never reaches here
      /// (ORDO-5805 / ORDO-7401).
      StateView: JsonValue
      /// Whether a confidence value is wanted at all. A provider is never
      /// obliged to produce one (ORDO-1101).
      ConfidenceRequested: bool }

/// What an adapter may return.
///
/// Note that there is no "here is some prose, work it out" case. A provider
/// that cannot produce one of these has failed the contract, and that is a
/// `ProviderFailed(ContractViolation ...)`, not something to be salvaged.
type ProviderResponse =
    | ChoiceSelected of token: string * confidence: Confidence option * rationale: string option
    /// The provider says it lacks specific required evidence. The ids are
    /// checked against the contract before being believed.
    | EvidenceInsufficient of requirementIds: string list
    | DeliberationRequested of note: string
    | HumanReviewRequested of note: string
    | ProviderFailed of ProviderError
    | ResponseCancelled

/// A response together with the execution facts the adapter observed.
type ProviderOutcome =
    { Response: ProviderResponse
      Identity: ProviderIdentity
      Usage: ProviderUsage
      /// How many times the adapter re-sent an equivalent request after a
      /// transient transport failure.
      ///
      /// Transport retries are legitimate and must be visible (ORDO-6703).
      /// Semantic retries — asking again because the answer was not liked —
      /// are not performed by any adapter in this library.
      TransportRetries: int }

/// A provider.
///
/// `Resolve` takes a cancellation token explicitly rather than capturing
/// one, so cancellation is part of the contract every implementation must
/// honour (ORDO-6601).
type DecisionProvider =
    { Identity: ProviderIdentity
      Capabilities: ProviderCapability list
      Resolve: ProviderRequest -> CancellationToken -> Task<ProviderOutcome> }

[<RequireQualifiedAccess>]
module ProviderCapability =

    let toWire capability =
        match capability with
        | BoundedChoiceSelection -> "bounded-choice-selection"
        | NativeStructuredOutput -> "native-structured-output"
        | SelfReportedConfidence -> "self-reported-confidence"
        | CalibratedProbability -> "calibrated-probability"
        | LocalExecution -> "local-execution"
        | CooperativeCancellation -> "cooperative-cancellation"

[<RequireQualifiedAccess>]
module DecisionProvider =

    let supports (capability: ProviderCapability) (provider: DecisionProvider) =
        provider.Capabilities |> List.contains capability

    let missingCapabilities (required: ProviderCapability list) (provider: DecisionProvider) =
        required |> List.filter (fun c -> not (supports c provider))
