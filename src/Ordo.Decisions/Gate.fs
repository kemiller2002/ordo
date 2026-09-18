/// Where a decision meets authority — and stops.
///
/// A decision is information. This module is the only place that information
/// is allowed to contribute to a state change, and it contributes as one
/// input among capability, evidence, obligations, policy and state identity,
/// never as a substitute for them (ORDO-0603 / ORDO-3-321).
///
/// Two properties are structural rather than conventional:
///
/// * The state a decision was made against is taken from the decision
///   itself, not from the caller. A caller cannot present a decision reached
///   against one state as though it had been reached against another
///   (ORDO-0904 / ORDO-4505).
/// * The only way out with an authorisation is through
///   `Ordo.Core.Transition.evaluate`, whose authorisation type has a private
///   constructor. There is no path from "the provider said Safe" to a legal
///   transition that does not pass every check.
module Ordo.Decisions.Gate

open Ordo.Core.StateIdentity
open Ordo.Core.Transition
open Ordo.Decisions.Outcome

/// Why a decision did not authorise a change.
type GateRefusal =
    /// There was no decision to act on. Carries the outcome's own wire token
    /// so that "the provider failed" is never reported as "the change was
    /// refused".
    | NoDecisionToAct of outcome: string
    /// The decision was reached against a state that no longer exists. Not a
    /// provider error and not a policy refusal — the world moved
    /// (ORDO-2802).
    | DecisionIsStale of decidedAgainst: StateFingerprint * current: StateFingerprint
    | RequirementsNotMet of TransitionFailure list

/// The result of asking whether a decision may be acted on.
type GateOutcome =
    | ChangeAuthorized of TransitionAuthorization
    | ChangeRefused of GateRefusal
    | ChangeNeedsHumanReview of reason: string * alsoOutstanding: TransitionFailure list

[<RequireQualifiedAccess>]
module Gate =

    /// Evaluates whether a decision, in context, authorises a change.
    ///
    /// `context.FormedAgainst` is overwritten with the state the decision
    /// actually saw, so supplying the wrong value cannot weaken the
    /// staleness check.
    ///
    /// Note what this function does not read: the confidence on the result.
    /// A magnitude cannot appear in an authority check here, because that is
    /// exactly how confidence would quietly become permission. A domain that
    /// wants a threshold expresses it in its own policy, and the policy's
    /// verdict arrives through the context (ORDO-1105 / ORDO-0602).
    let evaluate
        (requirement: TransitionRequirement)
        (context: TransitionContext)
        (outcome: DecisionOutcome<'choice>)
        : GateOutcome =
        match outcome with
        | Decided result ->
            let context =
                { context with
                    FormedAgainst = result.DecidedAgainst }

            if StateSnapshot.hasChangedSince result.DecidedAgainst context.CurrentState then
                ChangeRefused(DecisionIsStale(result.DecidedAgainst, context.CurrentState.Fingerprint))
            else
                match Transition.evaluate requirement context with
                | TransitionAllowed authorization -> ChangeAuthorized authorization
                | TransitionRefused failures -> ChangeRefused(RequirementsNotMet failures)
                | TransitionRequiresHumanReview(reason, outstanding) -> ChangeNeedsHumanReview(reason, outstanding)

        | InsufficientEvidence _
        | RequiresDeliberation _
        | RequiresHumanReview _
        | ProviderFailure _
        | ResolutionCancelled -> ChangeRefused(NoDecisionToAct(DecisionOutcome.toWire outcome))

    let toWire outcome =
        match outcome with
        | ChangeAuthorized _ -> "authorized"
        | ChangeRefused(NoDecisionToAct _) -> "refused:no-decision"
        | ChangeRefused(DecisionIsStale _) -> "refused:stale-decision"
        | ChangeRefused(RequirementsNotMet _) -> "refused:requirements-not-met"
        | ChangeNeedsHumanReview _ -> "human-review"
