/// Identity for the state a decision was made against.
///
/// A decision is only ever about the state it saw. Without a stable
/// identity for that state there is no way to tell, at the moment of acting,
/// whether the world still resembles the one that was judged — so every
/// decision would silently authorise action against whatever happens to be
/// true later (ORDO-0904 / ORDO-2801).
///
/// A fingerprint says that two normalised serialised views are identical.
/// It does **not** say that two situations mean the same thing: equal
/// hashes imply equal input, nothing more (ORDO-5804).
module Ordo.Core.StateIdentity

open System
open System.Security.Cryptography
open System.Text
open Ordo.Core.Json

/// A content fingerprint over a normalised state view.
type StateFingerprint =
    private
    | StateFingerprint of string

    member this.Value =
        let (StateFingerprint value) = this
        value

    override this.ToString() = this.Value

/// A state view, its identity, and when it was taken.
///
/// `View` is the representation the contract is evaluated against and
/// nothing more. Serialising an entire object graph because it was
/// convenient defeats both the fingerprint and the data-minimisation rule
/// (ORDO-5801 / ORDO-7401).
type StateSnapshot =
    { View: JsonValue
      /// Identity of `View`, always computed over the full view — never over
      /// a redacted one, so that redacting for a provider cannot change
      /// which state a decision is recorded against.
      Fingerprint: StateFingerprint
      /// The domain's own concurrency marker, when it has one: a revision,
      /// an ETag, a commit. Carried alongside rather than instead of the
      /// fingerprint, because a domain marker is authoritative for the
      /// domain and the fingerprint is authoritative for what was seen.
      Revision: string option
      TakenAt: DateTimeOffset }

/// A deterministic transformation applied to a state view before it leaves
/// the trust boundary.
///
/// A function, so that it is testable in isolation and so that "what was
/// removed" is answerable by running it rather than by reading prose
/// (ORDO-7403).
type Redaction = JsonValue -> JsonValue

[<RequireQualifiedAccess>]
module StateFingerprint =

    /// Fingerprints a view over its canonical rendering, so that field order
    /// cannot change identity.
    let ofView (view: JsonValue) : StateFingerprint =
        let canonical = renderCanonical view
        let bytes = SHA256.HashData(Encoding.UTF8.GetBytes canonical)
        StateFingerprint("sha256:" + Convert.ToHexString(bytes).ToLowerInvariant())

    /// Rehydrates a fingerprint read from a persisted record. Refuses
    /// anything not in the shape this module writes, so a corrupted record
    /// cannot become a fingerprint that silently matches nothing.
    let parse (raw: string) : StateFingerprint option =
        if
            not (String.IsNullOrEmpty raw)
            && raw.StartsWith("sha256:", StringComparison.Ordinal)
            && raw.Length = 7 + 64
            && raw.Substring 7 |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))
        then
            Some(StateFingerprint raw)
        else
            None

    let value (fingerprint: StateFingerprint) = fingerprint.Value

[<RequireQualifiedAccess>]
module StateSnapshot =

    let take (now: DateTimeOffset) (view: JsonValue) =
        { View = view
          Fingerprint = StateFingerprint.ofView view
          Revision = None
          TakenAt = now }

    let withRevision (revision: string) (snapshot: StateSnapshot) =
        { snapshot with Revision = Some revision }

    /// The view as an external provider may see it.
    ///
    /// Separate from `View` because what may be retained for audit and what
    /// may be transmitted to a third party are different questions
    /// (ORDO-7402), and because the answer is the domain's, not Ordo's.
    let providerView (redact: Redaction) (snapshot: StateSnapshot) = redact snapshot.View

    /// Whether the state has changed since a decision was formed against it.
    let hasChangedSince (formedAgainst: StateFingerprint) (snapshot: StateSnapshot) =
        snapshot.Fingerprint <> formedAgainst
