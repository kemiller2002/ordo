/// Whether a state change is legal — evaluated, never assumed.
///
/// This module is the narrow place where capability, evidence, obligations,
/// policy and state identity are checked together. It knows nothing about
/// decisions or providers: a decision is one more input a caller may
/// require, not a thing that can authorise a change by existing
/// (ORDO-0603 / ORDO-3-321).
///
/// The result of a successful evaluation is a `TransitionAuthorization`
/// value whose constructor is private to this module. An application that
/// wants to apply a change must hold one, and the only way to obtain one is
/// to have passed every check — so "we decided, therefore we acted" is not
/// expressible.
module Ordo.Core.Transition

open System
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.Capability
open Ordo.Core.Obligation
open Ordo.Core.Policy
open Ordo.Core.StateIdentity

/// Why a transition was refused.
///
/// The cases stay separate because their remedies are different, and
/// collapsing them into one generic failure is exactly the loss of
/// information this library exists to prevent (ORDO-2703).
type TransitionFailure =
    /// The domain's own precondition on the source state was not met.
    | InvalidState of expected: string * actual: string
    | MissingCapability of CapabilityId list
    | MissingEvidence of EvidenceRequirement list
    | StaleEvidence of (EvidenceRequirement * TimeSpan) list
    | WrongEvidenceKind of EvidenceRequirement list
    | PolicyRejected of PolicyIdentity * reason: string
    /// Historical schema-v1 state can be audited, but cannot be the current
    /// state used to authorize a new real-world transition.
    | UnversionedCurrentState
    /// The state changed between the decision being formed and the change
    /// being attempted. Not a provider error and not a defect — the world
    /// moved (ORDO-2802).
    | StaleState of formedAgainst: StateFingerprint * current: StateFingerprint
    | UnsatisfiedObligation of ObligationId list

/// A domain precondition on the source state.
///
/// The predicate decides; the description is what a refusal reports. Both
/// are needed: a bare predicate refuses without saying what it wanted, and a
/// bare description is prose nothing checks.
type SourceStateRequirement =
    { Expected: string
      IsSatisfiedBy: StateSnapshot -> bool }

/// What a caller must satisfy to make a particular change.
type TransitionRequirement =
    { /// The change being requested, in the domain's own words.
      Name: string
      SourceState: SourceStateRequirement
      RequiredCapabilities: CapabilityId list
      RequiredEvidence: EvidenceRequirement list
      /// Obligations that must be discharged first.
      RequiredObligations: ObligationId list }

/// Everything the evaluation is allowed to look at.
type TransitionContext =
    { CurrentState: StateSnapshot
      /// The state identity the caller's plan — typically a decision — was
      /// formed against. When this differs from the current state's
      /// fingerprint, the plan is stale.
      FormedAgainst: StateFingerprint
      Held: CapabilitySet
      Available: Evidence list
      Obligations: Obligation list
      /// The policy's verdict, already evaluated by the domain. Passed in
      /// rather than called, so that this module stays free of the domain's
      /// fact type and the policy stays a pure function the domain owns.
      Policy: PolicyIdentity * PolicyVerdict
      Now: DateTimeOffset }

/// Proof that a specific change was evaluated against a specific state and
/// found legal.
///
/// Constructed only by `evaluate`. Carrying the identities it was granted
/// under means an audit can answer "under what did this happen" from the
/// authorisation itself (ORDO-7301).
type TransitionAuthorization =
    private
        { AuthorizedName: string
          AuthorizedAgainst: StateFingerprint
          AuthorizedBy: PolicyIdentity
          AuthorizedAt: DateTimeOffset }

    member this.Name = this.AuthorizedName
    member this.State = this.AuthorizedAgainst
    member this.Policy = this.AuthorizedBy
    member this.At = this.AuthorizedAt

/// The outcome of evaluating a requested change.
type TransitionEvaluation =
    | TransitionAllowed of TransitionAuthorization
    | TransitionRefused of TransitionFailure list
    /// Legal but for a person's judgment. Distinct from refusal because a
    /// human is a resolution source, not an error (ORDO-1305 / ORDO-6501).
    | TransitionRequiresHumanReview of reason: string * alsoOutstanding: TransitionFailure list

[<RequireQualifiedAccess>]
module TransitionRequirement =

    /// A requirement that accepts any source state. Domains add their own
    /// precondition with `requiringSourceState`.
    let create (name: string) =
        { Name = name
          SourceState =
            { Expected = "any"
              IsSatisfiedBy = fun _ -> true }
          RequiredCapabilities = []
          RequiredEvidence = []
          RequiredObligations = [] }

    let requiringSourceState (expected: string) (predicate: StateSnapshot -> bool) (requirement: TransitionRequirement) =
        { requirement with
            SourceState =
                { Expected = expected
                  IsSatisfiedBy = predicate } }

    let requiringCapabilities ids (requirement: TransitionRequirement) =
        { requirement with RequiredCapabilities = ids }

    let requiringEvidence requirements (requirement: TransitionRequirement) =
        { requirement with RequiredEvidence = requirements }

    let requiringObligations ids (requirement: TransitionRequirement) =
        { requirement with RequiredObligations = ids }

[<RequireQualifiedAccess>]
module Transition =

    /// Evaluates every check and reports every failure, rather than
    /// stopping at the first.
    ///
    /// A caller usually wants to know everything that is wrong: an agent
    /// that must acquire evidence *and* obtain a capability should learn
    /// both in one pass instead of discovering them one round trip at a
    /// time.
    let private failures (requirement: TransitionRequirement) (context: TransitionContext) =
        let source = requirement.SourceState

        let stateFailures =
            [ if not (source.IsSatisfiedBy context.CurrentState) then
                  InvalidState(source.Expected, StateFingerprint.value context.CurrentState.Fingerprint)

              if not (StateSnapshot.isVersioned context.CurrentState) then
                  UnversionedCurrentState

              if StateSnapshot.hasChangedSince context.FormedAgainst context.CurrentState then
                  StaleState(context.FormedAgainst, context.CurrentState.Fingerprint) ]

        let capabilityFailures =
            match CapabilitySet.missing requirement.RequiredCapabilities context.Held with
            | [] -> []
            | missing -> [ MissingCapability missing ]

        let checks =
            Evidence.checkAll context.Now context.Available requirement.RequiredEvidence

        let missingEvidence =
            checks
            |> List.choose (function
                | Unsatisfied requirement -> Some requirement
                | _ -> None)

        let staleEvidence =
            checks
            |> List.choose (function
                | Stale(requirement, _, age) -> Some(requirement, age)
                | _ -> None)

        let wrongKind =
            checks
            |> List.choose (function
                | WrongKind(requirement, _) -> Some requirement
                | _ -> None)

        let evidenceFailures =
            [ if not (List.isEmpty missingEvidence) then
                  MissingEvidence missingEvidence
              if not (List.isEmpty staleEvidence) then
                  StaleEvidence staleEvidence
              if not (List.isEmpty wrongKind) then
                  WrongEvidenceKind wrongKind ]

        let outstandingObligations =
            let outstanding =
                context.Obligations
                |> List.filter Obligation.isOutstanding
                |> List.map (fun o -> o.Id)
                |> Set.ofList

            requirement.RequiredObligations |> List.filter outstanding.Contains

        let obligationFailures =
            match outstandingObligations with
            | [] -> []
            | ids -> [ UnsatisfiedObligation ids ]

        let policyIdentity, verdict = context.Policy

        let policyFailures =
            match verdict with
            | PolicyAllows
            | PolicyRequiresHumanReview _ -> []
            | PolicyRefuses reason -> [ PolicyRejected(policyIdentity, reason) ]

        stateFailures
        @ capabilityFailures
        @ evidenceFailures
        @ obligationFailures
        @ policyFailures

    /// The single entry point. Returns an authorisation only when nothing
    /// is outstanding; a human-review verdict never authorises by itself,
    /// and never hides the other failures alongside it.
    let evaluate (requirement: TransitionRequirement) (context: TransitionContext) : TransitionEvaluation =
        let outstanding = failures requirement context
        let policyIdentity, verdict = context.Policy

        match verdict, outstanding with
        | PolicyRequiresHumanReview reason, _ -> TransitionRequiresHumanReview(reason, outstanding)
        | _, [] ->
            TransitionAllowed
                { AuthorizedName = requirement.Name
                  AuthorizedAgainst = context.CurrentState.Fingerprint
                  AuthorizedBy = policyIdentity
                  AuthorizedAt = context.Now }
        | _, outstanding -> TransitionRefused outstanding

    let failureToWire failure =
        match failure with
        | InvalidState _ -> "invalid-state"
        | MissingCapability _ -> "missing-capability"
        | MissingEvidence _ -> "missing-evidence"
        | StaleEvidence _ -> "stale-evidence"
        | WrongEvidenceKind _ -> "wrong-evidence-kind"
        | PolicyRejected _ -> "policy-rejected"
        | UnversionedCurrentState -> "unversioned-current-state"
        | StaleState _ -> "stale-state"
        | UnsatisfiedObligation _ -> "unsatisfied-obligation"
