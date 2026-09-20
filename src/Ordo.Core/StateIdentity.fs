/// Identity for the state a decision was made against.
///
/// A decision is only ever about the state it saw. Without a stable
/// identity for that state there is no way to tell, at the moment of acting,
/// whether the world still resembles the one that was judged — so every
/// decision would silently authorise action against whatever happens to be
/// true later (ORDO-0904 / ORDO-2801).
///
/// State identity includes both the domain-defined view schema/version and
/// the complete selected view. Ordo fingerprints all of that selected
/// semantic input; it never tries to infer which fields are relevant.
module Ordo.Core.StateIdentity

open System
open System.Security.Cryptography
open System.Text
open Ordo.Core.Json

/// Why a domain state-view schema could not be constructed.
type StateViewSchemaError =
    | StateViewSchemaIdIsEmpty
    | StateViewSchemaVersionMustBePositive of supplied: int

/// Identity of the domain projection used for a bounded decision.
///
/// This is not the Ordo wire-schema version. It belongs to the domain/contract
/// that defines what state is semantically complete for the decision.
type StateViewSchema =
    private
        { SchemaId: string
          SchemaVersion: int }

    member this.Id = this.SchemaId
    member this.Version = this.SchemaVersion

[<RequireQualifiedAccess>]
module StateViewSchema =

    let create (id: string) (version: int) : Result<StateViewSchema, StateViewSchemaError> =
        if String.IsNullOrWhiteSpace id then
            Error StateViewSchemaIdIsEmpty
        elif version <= 0 then
            Error(StateViewSchemaVersionMustBePositive version)
        else
            Ok
                { SchemaId = id
                  SchemaVersion = version }

    let id (schema: StateViewSchema) = schema.Id
    let version (schema: StateViewSchema) = schema.Version

/// A content fingerprint over a canonical, versioned state view.
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
///
/// `ViewSchema = None` exists only for decoding immutable schema-v1
/// historical records. Such a snapshot is audit/replay provenance, not
/// current authorisation input. New snapshots are always versioned.
type StateSnapshot =
    { ViewSchema: StateViewSchema option
      View: JsonValue
      /// Identity of the schema plus the entire selected View — never a
      /// redacted view and never a selectively fingerprinted subset.
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

    let private hashCanonical (value: JsonValue) : StateFingerprint =
        let canonical = renderCanonical value
        let bytes = SHA256.HashData(Encoding.UTF8.GetBytes canonical)
        StateFingerprint("sha256:" + Convert.ToHexString(bytes).ToLowerInvariant())

    /// Fingerprints the full selected view together with the domain-defined
    /// schema identity/version. Two byte-identical views under different
    /// semantic schemas are intentionally different state identities.
    let ofView (schema: StateViewSchema) (view: JsonValue) : StateFingerprint =
        JObject
            [ "viewSchema",
              JObject
                  [ "id", JString(StateViewSchema.id schema)
                    "version", JInt(int64 (StateViewSchema.version schema)) ]
              "view", view ]
        |> hashCanonical

    /// The schema-v1 algorithm, retained only so immutable historical records
    /// can be verified while decoding. New state must never be created with
    /// this identity form.
    let internal ofLegacyV1View (view: JsonValue) : StateFingerprint = hashCanonical view

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

    /// Captures current state under an explicit domain view schema.
    let take (schema: StateViewSchema) (now: DateTimeOffset) (view: JsonValue) =
        { ViewSchema = Some schema
          View = view
          Fingerprint = StateFingerprint.ofView schema view
          Revision = None
          TakenAt = now }

    let withRevision (revision: string) (snapshot: StateSnapshot) =
        { snapshot with Revision = Some revision }

    /// Whether this snapshot was captured under an explicit state-view schema
    /// understood by the current executable semantics.
    let isVersioned (snapshot: StateSnapshot) = snapshot.ViewSchema.IsSome

    /// The view as an external provider may see it.
    ///
    /// Separate from `View` because what may be retained for audit and what
    /// may be transmitted to a third party are different questions
    /// (ORDO-7402), and because the answer is the domain's, not Ordo's.
    let providerView (redact: Redaction) (snapshot: StateSnapshot) = redact snapshot.View

    /// Whether the state has changed since a decision was formed against it.
    ///
    /// Current fingerprints already include the view schema/version, so a
    /// schema evolution is a state-identity change just like a relevant
    /// semantic field change.
    let hasChangedSince (formedAgainst: StateFingerprint) (snapshot: StateSnapshot) =
        snapshot.Fingerprint <> formedAgainst
