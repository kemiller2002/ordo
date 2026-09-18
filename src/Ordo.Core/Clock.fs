/// Time as an explicit input.
///
/// Anything in Ordo that depends on "now" — evidence freshness, execution
/// timing, obligation age — takes a `Clock` rather than reading the ambient
/// system clock, so that a freshness rule can be tested without waiting and
/// a recorded execution can be reproduced (ORDO-9601 / ORDO-9602).
module Ordo.Core.Clock

open System

/// A source of the current instant. A function rather than an interface
/// because that is the whole contract, and because a test clock is then a
/// lambda rather than a class.
type Clock = unit -> DateTimeOffset

/// The real clock, in UTC. Local time never enters a persisted Ordo record:
/// an offset-bearing UTC instant is unambiguous wherever it is read
/// (ORDO-9603).
let system: Clock = fun () -> DateTimeOffset.UtcNow

/// A clock frozen at a chosen instant.
let fixedAt (instant: DateTimeOffset) : Clock = fun () -> instant

/// Renders an instant in the one format Ordo persists: ISO-8601, UTC, with
/// an explicit `Z`.
let toWire (instant: DateTimeOffset) : string =
    instant.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", Globalization.CultureInfo.InvariantCulture)

/// Reads an instant previously written by `toWire`. Refuses anything it
/// cannot read rather than substituting a default, because a record whose
/// timestamp silently became `DateTimeOffset.MinValue` is worse than one
/// that failed to load.
let fromWire (text: string) : DateTimeOffset option =
    match
        DateTimeOffset.TryParse(
            text,
            Globalization.CultureInfo.InvariantCulture,
            Globalization.DateTimeStyles.AssumeUniversal ||| Globalization.DateTimeStyles.AdjustToUniversal
        )
    with
    | true, value -> Some value
    | _ -> None
