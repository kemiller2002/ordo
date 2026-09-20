/// Shared semantic handling for state-changing external effects whose result
/// may be unknown.
///
/// This module does not execute effects, probe remote systems, retry calls,
/// or compensate anything. It only preserves the distinction between
/// Succeeded, Failed, and Unknown and makes the reconciliation obligation
/// explicit when the outcome is Unknown.
module Ordo.Core.ExternalEffect

open System
open Ordo.Core.Identifiers
open Ordo.Core.Obligation

/// What is known about one attempted external effect.
type ExternalEffectOutcome =
    | Succeeded
    | Failed of reason: string
    | Unknown of reason: string

[<RequireQualifiedAccess>]
module ExternalEffectOutcome =

    let toWire outcome =
        match outcome with
        | Succeeded -> "succeeded"
        | Failed _ -> "failed"
        | Unknown _ -> "unknown"

/// Whether the host has established, from the actual external contract, that
/// repeating an Unknown attempt is semantically safe.
///
/// This is supplied by the host. Ordo never infers idempotency from HTTP
/// method, transport behavior, a provider response, or a hopeful comment.
type UnknownRetrySafety =
    | RetrySafetyNotEstablished
    | RetrySafeByExternalContract of basis: string

/// Pure semantic disposition after observing the outcome of an attempted
/// external effect.
type ExternalEffectDisposition =
    | Settled of ExternalEffectOutcome
    | ReconciliationRequired of outcome: ExternalEffectOutcome * obligation: Obligation

[<RequireQualifiedAccess>]
module ExternalEffect =

    /// Interpret an observed effect outcome.
    ///
    /// Unknown always creates a reconciliation obligation, regardless of
    /// whether a retry is separately known to be safe. Later domain actions
    /// may need to know what actually happened even when repeating the call
    /// cannot duplicate the effect.
    let recordOutcome
        (obligationId: ObligationId)
        (effectId: ExternalEffectId)
        (now: DateTimeOffset)
        (outcome: ExternalEffectOutcome)
        : ExternalEffectDisposition =
        match outcome with
        | Succeeded
        | Failed _ -> Settled outcome
        | Unknown _ ->
            Obligation.create obligationId (ReconcileExternalEffect effectId) now
            |> fun obligation -> ReconciliationRequired(outcome, obligation)

    /// Whether the host has positively established that an Unknown attempt
    /// may be repeated before reconciliation.
    ///
    /// True is permission to consider a retry, not an instruction to retry,
    /// and it never removes the reconciliation obligation.
    let mayRepeatBeforeReconciliation retrySafety =
        match retrySafety with
        | RetrySafetyNotEstablished -> false
        | RetrySafeByExternalContract basis -> not (String.IsNullOrWhiteSpace basis)