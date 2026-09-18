/// The record of a problem moving between resolution modes.
///
/// Escalation is data, not a retry loop. "We asked three times and the third
/// answer looked better" is not escalation; it is the anti-pattern this type
/// replaces (ORDO-1301 / ORDO-6702).
///
/// The chain is kept whole. `Decide -> Deliberate -> Decide -> Human` is
/// four facts about how an answer was reached, and collapsing it to the last
/// one destroys the only evidence that the first classification was wrong
/// (ORDO-1306 / ORDO-3-101).
module Ordo.Decisions.Escalation

open System
open Ordo.Core.Identifiers
open Ordo.Core.Resolution

/// Where a problem went.
///
/// Movement in both directions is representable: deliberation that bounds a
/// problem into a decision, or a decision that turns out to be mechanical,
/// are as real as a decision that needed more room (ORDO-1303 / ORDO-1304).
type EscalationTarget =
    | ToDeliberation
    | ToHumanReview
    | ToDecide
    | ToCompute

type EscalationStep =
    { Resolution: ResolutionId
      From: ResolutionMode
      To: EscalationTarget
      Reason: string
      At: DateTimeOffset }

/// An ordered history of escalations, oldest first.
///
/// Private so that steps can only be appended. A chain that could be
/// rewritten would not be a history.
type EscalationChain =
    private
    | EscalationChain of EscalationStep list

[<RequireQualifiedAccess>]
module EscalationTarget =

    let toWire target =
        match target with
        | ToDeliberation -> "deliberation"
        | ToHumanReview -> "human-review"
        | ToDecide -> "decide"
        | ToCompute -> "compute"

[<RequireQualifiedAccess>]
module EscalationChain =

    let empty = EscalationChain []

    let append (step: EscalationStep) (EscalationChain steps) = EscalationChain(steps @ [ step ])

    let steps (EscalationChain steps) = steps

    let isEmpty (EscalationChain steps) = List.isEmpty steps

    let ofSteps (steps: EscalationStep list) = EscalationChain steps
