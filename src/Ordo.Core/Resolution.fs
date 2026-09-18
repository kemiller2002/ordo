/// How a piece of unresolved work is allowed to be resolved.
///
/// This is the vocabulary the rest of executable Ordo is organised around.
/// The names describe engineering behaviour rather than a cognitive theory:
/// "System 1 / System 2" and "fast / slow" may inform the research record,
/// but they are not what the code means, so they are not what the code says
/// (ORDO-0401 / ORDO-3-010 / ORDO-4902).
module Ordo.Core.Resolution

/// The three resolution modes, narrowest first.
///
/// The ordering matters: work should be resolved by the least flexible mode
/// that can resolve it correctly, and the long-term direction of travel is
/// `Deliberate -> Decide -> Compute` as understanding improves (ORDO-0405).
/// Nothing in this library performs that promotion automatically — observed
/// regularity is a research finding, not a licence to rewrite behaviour
/// (ORDO-3103).
type ResolutionMode =
    /// The answer follows mechanically from information already available.
    /// A provider is not required merely because one is convenient, and a
    /// failure to compute stays a failure: it is never quietly retried as a
    /// decision (ORDO-0406).
    | Compute
    /// Judgment is required, the legal output space is known, and the
    /// answer can be expressed as one of a fixed set of typed choices.
    | Decide
    /// The work requires exploration, decomposition, or reasoning across
    /// sources, and its output space is not known in advance.
    | Deliberate

[<RequireQualifiedAccess>]
module ResolutionMode =

    /// The stable wire token for a mode. Independent of the F# case name so
    /// that renaming the case cannot break a persisted record.
    let toWire mode =
        match mode with
        | Compute -> "compute"
        | Decide -> "decide"
        | Deliberate -> "deliberate"

    let fromWire token =
        match token with
        | "compute" -> Some Compute
        | "decide" -> Some Decide
        | "deliberate" -> Some Deliberate
        | _ -> None

    /// How much freedom a mode grants. Used only to express "is this mode
    /// narrower than that one"; it is not a quality ranking.
    let flexibility mode =
        match mode with
        | Compute -> 0
        | Decide -> 1
        | Deliberate -> 2

    /// True when `candidate` is at least as narrow as `required`. A caller
    /// asking for `Decide` is satisfied by `Compute`; it is not satisfied by
    /// `Deliberate`.
    let isAtLeastAsNarrowAs (required: ResolutionMode) (candidate: ResolutionMode) =
        flexibility candidate <= flexibility required
