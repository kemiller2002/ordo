/// Actor-kind authorization for legal transitions (ORD-EXEC-130..134).
///
/// Who may exercise a transition is a separate question from what role the
/// execution plays. A project maps transition names to an actor requirement;
/// the same state machine can carry different requirements in different
/// projects without the states changing meaning (ORD-EXEC-133).
module Ordo.Core.Authorization

open System
open Ordo.Core.Identifiers

/// What kind of actor is performing an execution. Provider, model and
/// runtime are attributes of an actor, never its role (ORD-EXEC-075).
type ActorKind =
    | Human
    | Agent
    | Automation

[<RequireQualifiedAccess>]
module ActorKind =

    let toWire kind =
        match kind with
        | Human -> "human"
        | Agent -> "agent"
        | Automation -> "automation"

    let fromWire raw =
        match raw with
        | "human" -> Some Human
        | "agent" -> Some Agent
        | "automation" -> Some Automation
        | _ -> None

/// The actor performing an execution. Optional attributes stay `None` when
/// unknown rather than being guessed.
type ExecutionActor =
    { Id: ActorId
      Kind: ActorKind
      Provider: string option
      Model: string option
      Runtime: string option }

/// Which actors may exercise a transition (ORD-EXEC-131).
type ActorRequirement =
    | HumanRequired
    | AgentAllowed
    | AutomationAllowed
    | AnyActor
    /// Allowed only for these specific actors.
    | SpecificActors of ActorId list

type AuthorizationPolicy =
    { /// Transition name → requirement. A transition absent from the map
      /// falls back to `Default`.
      Requirements: Map<string, ActorRequirement>
      Default: ActorRequirement }

/// Evidence that an actor actually exercised a transition (ORD-EXEC-134).
type AuthorizationEvidence =
    { Transition: string
      ExercisedBy: ExecutionActor
      Requirement: ActorRequirement
      At: DateTimeOffset }

type AuthorizationRefusal =
    /// The transition requires a human and the exercising actor is not one.
    /// A claim of human approval carried by a non-human actor is not an
    /// authorization (ORD-EXEC-132).
    | HumanAuthorizationRequired of transition: string * actual: ActorKind
    | ActorKindNotPermitted of transition: string * actual: ActorKind * requirement: ActorRequirement
    | ActorNotPermitted of transition: string * actor: ActorId

[<RequireQualifiedAccess>]
module AuthorizationPolicy =

    let permissive = { Requirements = Map.empty; Default = AnyActor }

    let requirementFor (transition: string) (policy: AuthorizationPolicy) =
        policy.Requirements |> Map.tryFind transition |> Option.defaultValue policy.Default

    let require transition requirement (policy: AuthorizationPolicy) =
        { policy with Requirements = policy.Requirements |> Map.add transition requirement }

    /// Decide whether the exercising actor may perform the transition.
    ///
    /// Only the exercising actor is consulted. There is deliberately no
    /// parameter through which an agent could present "a human approved
    /// this": a human-required transition is exercised by a human actor or it
    /// is not exercised.
    let authorize
        (transition: string)
        (actor: ExecutionActor)
        (now: DateTimeOffset)
        (policy: AuthorizationPolicy)
        : Result<AuthorizationEvidence, AuthorizationRefusal> =
        let requirement = requirementFor transition policy

        let evidence =
            { Transition = transition
              ExercisedBy = actor
              Requirement = requirement
              At = now }

        match requirement, actor.Kind with
        | AnyActor, _ -> Ok evidence
        | HumanRequired, Human -> Ok evidence
        | HumanRequired, kind -> Error(HumanAuthorizationRequired(transition, kind))
        // "Agent allowed" and "automation allowed" name the least-trusted
        // kind admitted; a human may always exercise them.
        | AgentAllowed, (Agent | Human) -> Ok evidence
        | AutomationAllowed, (Automation | Human) -> Ok evidence
        | (AgentAllowed | AutomationAllowed), kind -> Error(ActorKindNotPermitted(transition, kind, requirement))
        | SpecificActors ids, _ when List.contains actor.Id ids -> Ok evidence
        | SpecificActors _, _ -> Error(ActorNotPermitted(transition, actor.Id))

    let requirementToWire requirement =
        match requirement with
        | HumanRequired -> "human-required"
        | AgentAllowed -> "agent-allowed"
        | AutomationAllowed -> "automation-allowed"
        | AnyActor -> "any"
        | SpecificActors _ -> "specific-actors"
