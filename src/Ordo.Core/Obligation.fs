/// Required work that is not yet done, recorded so that it cannot be lost.
///
/// An obligation exists to outlive the thing that discovered it: an agent
/// session ending, a provider changing, execution moving machine, or one
/// reasoning attempt failing must not make required work disappear
/// (ORDO-0702).
///
/// This is deliberately not a project-management engine. There is identity,
/// a small explicit lifecycle, and how an obligation was discharged —
/// nothing about scheduling, assignment or priority (ORDO-9404).
module Ordo.Core.Obligation

open System
open Ordo.Core.Identifiers

/// What an obligation is asking for. Open-ended on purpose: the kinds of
/// required work are a domain's business, and the ones named here are the
/// ones Ordo's own resolution model can create.
type ObligationKind =
    | AcquireEvidence of requirementId: string
    | ReacquireStaleEvidence of requirementId: string
    | HumanReview of question: string
    | RunVerification of what: string
    | InvestigateFailure of what: string
    | Custom of label: string

/// Where an obligation stands.
///
/// `Superseded` is separate from `Cancelled` because "this was replaced by
/// other work" and "this is no longer required" have different meanings to
/// anyone auditing why work stopped (ORDO-9402).
type ObligationState =
    | Open
    | Blocked of reason: string
    | Satisfied of how: string * at: DateTimeOffset
    | Superseded of by: ObligationId * at: DateTimeOffset
    | Cancelled of reason: string * at: DateTimeOffset

type Obligation =
    { Id: ObligationId
      Kind: ObligationKind
      State: ObligationState
      CreatedAt: DateTimeOffset
      /// The obligations that must be discharged before this one can be.
      /// A plain list of ids: enough to express a prerequisite, not enough
      /// to become a scheduler.
      DependsOn: ObligationId list }

[<RequireQualifiedAccess>]
module Obligation =

    let create (id: ObligationId) (kind: ObligationKind) (now: DateTimeOffset) =
        { Id = id
          Kind = kind
          State = Open
          CreatedAt = now
          DependsOn = [] }

    let isOutstanding (obligation: Obligation) =
        match obligation.State with
        | Open
        | Blocked _ -> true
        | Satisfied _
        | Superseded _
        | Cancelled _ -> false

    /// Records how an obligation was discharged. The evidence of completion
    /// is required rather than optional: an obligation that went from open
    /// to satisfied with no account of how is indistinguishable from one
    /// that was quietly dropped (ORDO-9403).
    let satisfy (how: string) (now: DateTimeOffset) (obligation: Obligation) =
        { obligation with State = Satisfied(how, now) }

    let block (reason: string) (obligation: Obligation) =
        { obligation with State = Blocked reason }

    let supersede (by: ObligationId) (now: DateTimeOffset) (obligation: Obligation) =
        { obligation with State = Superseded(by, now) }

    let outstanding (obligations: Obligation list) = obligations |> List.filter isOutstanding

    let toWireState (state: ObligationState) =
        match state with
        | Open -> "open"
        | Blocked _ -> "blocked"
        | Satisfied _ -> "satisfied"
        | Superseded _ -> "superseded"
        | Cancelled _ -> "cancelled"
