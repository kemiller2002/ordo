/// Open-ended reasoning, asked for explicitly and bounded on purpose.
///
/// Deliberation is where a system stops knowing the shape of the answer, so
/// it is exactly where undeclared scope, unbounded cost and unearned
/// authority would otherwise creep in. Three things keep that in check:
///
/// * The objective is stated, so the investigation has edges (ORDO-7701).
/// * The capabilities granted are listed, and they are the ones the request
///   grants — deliberating is not permission (ORDO-1203 / ORDO-3-327).
/// * The result is typed. Prose may accompany it, but nothing downstream
///   discovers the authoritative answer by reading prose (ORDO-1204).
///
/// A deliberation result proposes. It never applies: there is no transition
/// anywhere in this module's types (ORDO-1205).
module Ordo.Decisions.Deliberation

open System
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.Capability
open Ordo.Core.Obligation

/// Limits on what an investigation may spend.
///
/// Every limit is optional; a request with no budget is unbounded, and that
/// is a choice the caller makes visibly rather than one the library makes
/// for it (ORDO-5503).
type DeliberationBudget =
    { MaxDuration: TimeSpan option
      MaxToolCalls: int option
      /// Expressed in the provider's own units, uninterpreted.
      MaxProviderCost: string option }

type DeliberationRequest =
    { Resolution: ResolutionId
      Correlation: CorrelationId option
      CausedBy: ResolutionId option
      /// What this investigation is authorised to find out. Anything else
      /// discovered becomes an obligation, not a new objective the agent
      /// grants itself (ORDO-7704).
      Objective: string
      Known: JsonValue
      Evidence: Evidence list
      Unknowns: string list
      /// What the deliberating provider may do. Absent from this list means
      /// not permitted (ORDO-7503 / ORDO-3-174).
      AllowedCapabilities: CapabilityId list
      Constraints: string list
      RequestedOutputs: string list
      Budget: DeliberationBudget
      CreatedAt: DateTimeOffset }

/// How an investigation ended.
///
/// `BudgetExhausted` is deliberately not `Failed`: stopping because the
/// money or the time ran out says nothing about whether the problem was
/// solvable, and recording it as failure would slander the question
/// (ORDO-7802).
type DeliberationStatus =
    | Resolved
    | RequiresDecision of proposedContract: string
    | RequiresEvidence of EvidenceRequirement list
    | RequiresHuman of question: string
    | BudgetExhausted of limit: string
    | Blocked of reason: string
    | Failed of reason: string
    | DeliberationCancelled

/// The structured handoff a deliberation produces.
type DeliberationResult =
    { Status: DeliberationStatus
      /// Evidence the investigation established. Whether any of it is an
      /// observation or an inference is carried on each piece, so a model's
      /// conclusion cannot enter the record as a witnessed fact
      /// (ORDO-5904).
      NewEvidence: Evidence list
      RemainingUnknowns: string list
      /// Work the investigation discovered and did not do.
      ProposedObligations: Obligation list
      /// Free-text reasoning. Supplemental only.
      Narrative: string option
      CompletedAt: DateTimeOffset }

[<RequireQualifiedAccess>]
module DeliberationBudget =

    let unbounded =
        { MaxDuration = None
          MaxToolCalls = None
          MaxProviderCost = None }

[<RequireQualifiedAccess>]
module DeliberationRequest =

    let create (resolution: ResolutionId) (objective: string) (now: DateTimeOffset) =
        { Resolution = resolution
          Correlation = None
          CausedBy = None
          Objective = objective
          Known = JNull
          Evidence = []
          Unknowns = []
          AllowedCapabilities = []
          Constraints = []
          RequestedOutputs = []
          Budget = DeliberationBudget.unbounded
          CreatedAt = now }

    let granting (capabilities: CapabilityId list) (request: DeliberationRequest) =
        { request with AllowedCapabilities = capabilities }

    let withBudget (budget: DeliberationBudget) (request: DeliberationRequest) = { request with Budget = budget }

    /// Whether an action the investigation wants to take is within what it
    /// was granted. The check is the caller's to make before acting; this
    /// function is how it is made without inventing a second authority
    /// model.
    let permits (capability: CapabilityId) (request: DeliberationRequest) =
        CapabilitySet.ofIds request.AllowedCapabilities |> CapabilitySet.grants capability

[<RequireQualifiedAccess>]
module DeliberationStatus =

    let toWire status =
        match status with
        | Resolved -> "resolved"
        | RequiresDecision _ -> "requires-decision"
        | RequiresEvidence _ -> "requires-evidence"
        | RequiresHuman _ -> "requires-human"
        | BudgetExhausted _ -> "budget-exhausted"
        | Blocked _ -> "blocked"
        | Failed _ -> "failed"
        | DeliberationCancelled -> "cancelled"
