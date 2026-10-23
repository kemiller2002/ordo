namespace Echelon.Provenance

open System

/// Marks a rule-bearing symbol with the handoff records (requirement,
/// decision, question or invariant IDs) it was built from. Dokimos scans
/// source for `[<Authority(` and cross-checks each marker with the
/// construction trace (out/construction-trace.json); the trace test in this
/// experiment checks the same correspondence by reflection.
///
/// It is a marker, not authority: writing it grants nothing, and a marker
/// that cites an ID the handoff does not hold makes `construct` refuse.
[<AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)>]
[<Sealed>]
type AuthorityAttribute([<ParamArray>] ids: string array) =
    inherit Attribute()
    member _.Ids = ids
