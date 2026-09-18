/// Re-running a recorded decision, without touching anything.
///
/// Replay exists so that a historical decision can be re-evaluated — against
/// the same provider, a different one, or a deterministic verifier — to
/// learn something. It is not a way to re-apply the decision. That is
/// enforced by the types: nothing this module returns can become a
/// `TransitionAuthorization`, because it never calls the gate
/// (ORDO-1802 / ORDO-3-326).
///
/// Replay preserves the historical semantics it is replaying. The original
/// request carries its own contract revision, its own state snapshot and its
/// own evidence, and replay re-uses all three, so a newer contract cannot
/// quietly stand in for the one that actually judged (ORDO-1803).
///
/// What replay cannot promise is that an external provider will answer the
/// same way twice. Logical replay — same contract, same recorded inputs, a
/// fresh call — is what this supports; exact replay of a remote model is not
/// something any caller should be told it has (ORDO-8301 / ORDO-8302).
module Ordo.Decisions.Replay

open System.Threading
open System.Threading.Tasks
open Ordo.Core.Identifiers
open Ordo.Decisions.Request
open Ordo.Decisions.Outcome
open Ordo.Decisions.Provider
open Ordo.Decisions.Resolve

/// How independent a re-evaluation actually is.
///
/// Asking the same model the same question again is not verification, and
/// recording which of these applies is what keeps that distinction from
/// being lost (ORDO-2102 / ORDO-2103 / ORDO-6204). Two frontier models are
/// `DifferentProvider`, which is still not proof of independent evidence
/// (ORDO-6205).
type VerificationIndependence =
    | SameProviderSameModel
    | SameProviderDifferentModel
    | DifferentProvider
    | DeterministicVerifier
    | HumanVerifier

/// Why the decision is being run again. Explicit because a duplicate
/// evaluation is only legitimate when its purpose is (ORDO-7002).
type ReplayPurpose =
    /// Re-evaluate to see what happens now. No claim of verification.
    | LogicalReplay
    | IndependentVerification of VerificationIndependence
    | ProviderComparison

/// What a replay produced. The historical outcome is carried unchanged
/// alongside the fresh one: a replay adds a record, it never edits one
/// (ORDO-7302 / ORDO-6201).
type ReplayRecord<'choice> =
    { Purpose: ReplayPurpose
      Historical: DecisionOutcome<'choice>
      Fresh: ResolutionRecord<'choice> }

[<RequireQualifiedAccess>]
module Replay =

    /// Re-executes a recorded request under a new resolution identity.
    ///
    /// The new identity matters: the replay is its own execution fact,
    /// linked to the original request but not masquerading as the original
    /// execution.
    let execute
        (options: ResolveOptions)
        (provider: DecisionProvider)
        (purpose: ReplayPurpose)
        (replayResolution: ResolutionId)
        (original: DecisionRequest<'choice>)
        (historical: DecisionOutcome<'choice>)
        (cancellation: CancellationToken)
        : Task<ReplayRecord<'choice>> =
        task {
            let request =
                { original with
                    Resolution = replayResolution
                    CausedBy = Some original.Resolution }

            let! fresh = Resolve.execute options provider request cancellation

            return
                { Purpose = purpose
                  Historical = historical
                  Fresh = fresh }
        }

    /// Whether the replay reached the same conclusion as the original.
    ///
    /// Agreement is a fact, not a verdict: two runs agreeing does not make
    /// either right, and does not make the underlying rule deterministic
    /// (ORDO-3-329).
    let agrees (record: ReplayRecord<'choice>) =
        match DecisionOutcome.decision record.Historical, DecisionOutcome.decision record.Fresh.Outcome with
        | Some historical, Some fresh -> Some(historical.Choice = fresh.Choice)
        | _ -> None

    let purposeToWire purpose =
        match purpose with
        | LogicalReplay -> "logical-replay"
        | IndependentVerification _ -> "independent-verification"
        | ProviderComparison -> "provider-comparison"

    let independenceToWire independence =
        match independence with
        | SameProviderSameModel -> "same-provider-same-model"
        | SameProviderDifferentModel -> "same-provider-different-model"
        | DifferentProvider -> "different-provider"
        | DeterministicVerifier -> "deterministic-verifier"
        | HumanVerifier -> "human-verifier"
