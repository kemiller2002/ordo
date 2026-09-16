/// Comparison for the plain "major.minor.patch" versions SDE releases use.
///
/// Deliberately not a full semantic-version implementation: there is no
/// prerelease or build-metadata handling, because no SDE release has ever
/// carried either and taking a dependency to compare three integers would
/// break the minimal-dependency rule. A version that does not parse is an
/// error the caller must report, never a silently-tolerated zero.
module Sde.Core.SemVer

open System.Text.RegularExpressions

[<Struct>]
type Version =
    { Major: int
      Minor: int
      Patch: int }

    override this.ToString() =
        sprintf "%d.%d.%d" this.Major this.Minor this.Patch

let private pattern = Regex(@"^(\d+)\.(\d+)\.(\d+)$", RegexOptions.CultureInvariant)

let tryParse (text: string) : Version option =
    if isNull (box text) then
        None
    else
        let m = pattern.Match(text.Trim())

        if not m.Success then
            None
        else
            Some
                { Major = int m.Groups.[1].Value
                  Minor = int m.Groups.[2].Value
                  Patch = int m.Groups.[3].Value }

/// Returns -1, 0 or 1, or reports which side failed to parse.
let compare (a: string) (b: string) : Result<int, string> =
    match tryParse a, tryParse b with
    | None, _ -> Error(sprintf "not a valid semantic version: %s" (Json.quote a))
    | _, None -> Error(sprintf "not a valid semantic version: %s" (Json.quote b))
    // Structural comparison on the record orders by Major, then Minor, then
    // Patch, which is exactly the ordering wanted here.
    | Some va, Some vb -> Ok(Operators.compare va vb)
