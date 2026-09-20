/// One invocation of a contract against specific state and evidence.
///
/// A request is the unit that is executed, recorded, audited and replayed,
/// so it carries everything needed to understand the decision later: which
/// contract revision asked, which state was seen, which evidence was on
/// hand, and what caused the request to exist (ORDO-1801).
///
/// What it does *not* carry is anything the contract did not ask for.
/// Context is selected deliberately; a provider does not receive a
/// repository, a conversation or a database because they happened to be
/// reachable (ORDO-0902 / ORDO-7401).
module Ordo.Decisions.Request

open System
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.StateIdentity
open Ordo.Decisions.Contract

/// Why a request could not be formed.
///
/// Note what is *not* here: missing evidence. A request with unmet evidence
/// requirements is a perfectly well-formed request whose outcome is
/// `InsufficientEvidence` — refusing to build it would collapse a resolution
/// outcome into a construction error (ORDO-0504).
type RequestError =
    | ContractRetired of DecisionContractId * ContractVersion
    /// A schema-v1 historical snapshot may be inspected or replayed as
    /// history, but a new live decision must be formed against state captured
    /// under an explicit current view schema.
    | UnversionedStateSnapshot of DecisionContractId
    /// The supplied evidence set is not a closed, acyclic reconstruction of
    /// its Derived-from provenance.
    | InvalidEvidenceDependencies of EvidenceDependencyError
    | NoEvidenceForContractRequiringIt of DecisionContractId

type DecisionRequest<'choice when 'choice: equality> =
    { Id: DecisionRequestId
      /// Identity of this resolution execution. Distinct from the request
      /// id, because one request may be executed more than once — a replay,
      /// an independent verification — and each execution is its own fact.
      Resolution: ResolutionId
      /// The wider execution this belongs to. May span several causal
      /// branches, which is why it is not the same as `CausedBy`
      /// (ORDO-7102).
      Correlation: CorrelationId option
      /// The resolution that caused this one, if any (ORDO-7101).
      CausedBy: ResolutionId option
      Contract: DecisionContract<'choice>
      /// The state the decision is about, with its identity.
      State: StateSnapshot
      /// The evidence available to the decision. Availability is not truth
      /// and not sufficiency; both are judged elsewhere (ORDO-0505).
      Evidence: Evidence list
      CreatedAt: DateTimeOffset }

[<RequireQualifiedAccess>]
module DecisionRequest =

    /// Builds a request, refusing only what is structurally impossible.
    ///
    /// A retired contract is refused here rather than at execution, because
    /// "this question is no longer asked" is knowable before anything is
    /// sent anywhere (ORDO-5603 / ORDO-9202).
    let create
        (id: DecisionRequestId)
        (resolution: ResolutionId)
        (contract: DecisionContract<'choice>)
        (state: StateSnapshot)
        (evidence: Evidence list)
        (now: DateTimeOffset)
        : Result<DecisionRequest<'choice>, RequestError> =
        if not (ContractLifecycle.acceptsNewRequests contract.Lifecycle) then
            Error(ContractRetired(contract.Id, contract.Version))
        elif not (StateSnapshot.isVersioned state) then
            Error(UnversionedStateSnapshot contract.Id)
        else
            match EvidenceDependency.validateClosedSet evidence with
            | Error error -> Error(InvalidEvidenceDependencies error)
            | Ok () ->
                Ok
                    { Id = id
                      Resolution = resolution
                      Correlation = None
                      CausedBy = None
                      Contract = contract
                      State = state
                      Evidence = evidence
                      CreatedAt = now }

    let correlatedWith (correlation: CorrelationId) (request: DecisionRequest<'choice>) =
        { request with Correlation = Some correlation }

    let causedBy (cause: ResolutionId) (request: DecisionRequest<'choice>) =
        { request with CausedBy = Some cause }

    /// Checks the contract's evidence requirements against what the request
    /// carries. Pure; the caller decides what an unmet requirement means.
    let checkEvidence (now: DateTimeOffset) (request: DecisionRequest<'choice>) =
        Evidence.checkAll now request.Evidence request.Contract.RequiredEvidence
