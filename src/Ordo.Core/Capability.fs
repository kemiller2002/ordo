/// Authority, kept separate from every kind of evidence about correctness.
///
/// A capability is something an actor is permitted to request or perform.
/// Nothing in this module can be produced from a confidence value, a
/// provider's identity, a model's quality, or a decision's content: a
/// provider returning 0.999999 gains no permission it did not already have
/// (ORDO-0602 / ORDO-3-323), and no provider output may mint one
/// (ORDO-3-172).
module Ordo.Core.Capability

open Ordo.Core.Identifiers

/// A named authority. The name is the whole of it — capabilities are
/// granted and checked, never computed.
type Capability = { Id: CapabilityId; Description: string }

/// The set of capabilities held by whoever is acting.
///
/// A distinct type rather than a bare list so that "the capabilities in
/// force" cannot be confused with "the capabilities required", which is the
/// mistake that turns an authority check into a tautology.
type CapabilitySet =
    private
    | CapabilitySet of Set<string>

[<RequireQualifiedAccess>]
module CapabilitySet =

    let empty = CapabilitySet Set.empty

    let ofList (capabilities: Capability list) =
        capabilities |> List.map (fun c -> CapabilityId.value c.Id) |> Set.ofList |> CapabilitySet

    let ofIds (ids: CapabilityId list) =
        ids |> List.map CapabilityId.value |> Set.ofList |> CapabilitySet

    let grants (id: CapabilityId) (CapabilitySet held) = held.Contains(CapabilityId.value id)

    /// The required capabilities that are not held, in the order required.
    let missing (required: CapabilityId list) (held: CapabilitySet) =
        required |> List.filter (fun id -> not (grants id held))

    let toList (CapabilitySet held) = held |> Set.toList
