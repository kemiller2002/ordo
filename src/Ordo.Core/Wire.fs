/// Persisted and transmitted representations of the core primitives.
///
/// Three rules hold everywhere in this module.
///
/// * The wire shape is written out by hand. No encoding is derived from an
///   F# type or case name, so renaming anything in this library cannot break
///   a record written by an earlier release (ORDO-8402 / ORDO-3-181).
/// * Every record carries a schema name and version, and a version this
///   build does not understand is refused rather than guessed at
///   (ORDO-2302 / ORDO-2303 / ORDO-3-183).
/// * Decoding is total: every failure is a `WireError` naming where and why,
///   because the usual reader is an agent diagnosing a rejected record
///   (ORDO-9902).
module Ordo.Core.Wire

open System
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.Coverage
open Ordo.Core.Obligation
open Ordo.Core.StateIdentity

/// Schema version for core records whose semantics have not changed.
[<Literal>]
let SchemaVersion = 1

/// State snapshots gained explicit domain view-schema identity in GH-20.
/// That is a semantic wire change, so state snapshots evolve independently
/// rather than making unrelated evidence/obligation records pretend to be v2.
[<Literal>]
let StateSnapshotSchemaVersion = 2

let private supportedVersions = [ SchemaVersion ]
let private stateSnapshotSupportedVersions = [ 1; StateSnapshotSchemaVersion ]

/// Why a record could not be encoded or decoded.
type WireError =
    | MalformedDocument of JsonError
    | UnsupportedSchemaVersion of found: int * supported: int list
    | UnexpectedSchema of found: string * expected: string
    /// A union case token this build does not know. Refused rather than
    /// ignored: a reader that silently drops a variant it does not
    /// recognise has reinterpreted the record (ORDO-8404).
    | UnknownVariant of field: string * token: string
    | InvalidField of path: string * why: string

let private field name value = (name, value)

let private envelopeAt (version: int) (schema: string) (members: (string * JsonValue) list) =
    JObject(
        [ field "schema" (JString schema)
          field "schemaVersion" (JInt(int64 version)) ]
        @ members
    )

let private envelope (schema: string) (members: (string * JsonValue) list) =
    envelopeAt SchemaVersion schema members

let private readEnvelopeWithVersions
    (expectedSchema: string)
    (versions: int list)
    (document: JsonValue)
    : Result<int * JsonValue, WireError> =
    let bind f r = Result.bind f r
    let json e = MalformedDocument e

    requiredMember "schema" document
    |> Result.mapError json
    |> bind (asString "$.schema" >> Result.mapError json)
    |> bind (fun schema ->
        if schema <> expectedSchema then
            Error(UnexpectedSchema(schema, expectedSchema))
        else
            requiredMember "schemaVersion" document
            |> Result.mapError json
            |> bind (asInt "$.schemaVersion" >> Result.mapError json)
            |> bind (fun version ->
                if List.contains version versions then
                    Ok(version, document)
                else
                    Error(UnsupportedSchemaVersion(version, versions))))

let private readEnvelope (expectedSchema: string) (document: JsonValue) : Result<JsonValue, WireError> =
    readEnvelopeWithVersions expectedSchema supportedVersions document
    |> Result.map snd

let private required (name: string) (document: JsonValue) =
    requiredMember name document |> Result.mapError MalformedDocument

let private optional (name: string) (document: JsonValue) =
    tryMember name document |> Result.mapError MalformedDocument

let private requiredString (name: string) (document: JsonValue) =
    required name document
    |> Result.bind (asString ("$." + name) >> Result.mapError MalformedDocument)

let private identifier (name: string) (create: string -> Result<'id, IdentifierError>) (document: JsonValue) =
    requiredString name document
    |> Result.bind (fun raw ->
        create raw
        |> Result.mapError (fun e -> InvalidField("$." + name, sprintf "%A" e)))

// ---------------------------------------------------------------- evidence

let encodeEvidenceKind (kind: EvidenceKind) =
    match kind with
    | Direct -> JObject [ field "kind" (JString "direct") ]
    | Derived(computation, from) ->
        JObject
            [ field "kind" (JString "derived")
              field "computation" (JString computation)
              field "from" (JArray(from |> List.map (EvidenceId.value >> JString))) ]
    | Inferred by ->
        JObject
            [ field "kind" (JString "inferred")
              field "by" (JString(ProviderId.value by)) ]

let decodeEvidenceKind (document: JsonValue) : Result<EvidenceKind, WireError> =
    requiredString "kind" document
    |> Result.bind (fun token ->
        match token with
        | "direct" -> Ok Direct
        | "derived" ->
            let computation = requiredString "computation" document

            let from =
                required "from" document
                |> Result.bind (asArray "$.from" >> Result.mapError MalformedDocument)
                |> Result.bind (fun items ->
                    items
                    |> List.map (fun item ->
                        asString "$.from[]" item
                        |> Result.mapError MalformedDocument
                        |> Result.bind (
                            EvidenceId.create
                            >> Result.mapError (fun e -> InvalidField("$.from[]", sprintf "%A" e))
                        ))
                    |> List.fold
                        (fun acc item ->
                            match acc, item with
                            | Error e, _ -> Error e
                            | _, Error e -> Error e
                            | Ok acc, Ok value -> Ok(value :: acc))
                        (Ok [])
                    |> Result.map List.rev)

            match computation, from with
            | Ok computation, Ok from -> Ok(Derived(computation, from))
            | Error e, _ -> Error e
            | _, Error e -> Error e
        | "inferred" ->
            identifier "by" ProviderId.create document |> Result.map Inferred
        | other -> Error(UnknownVariant("kind", other)))

let encodeEvidence (evidence: Evidence) =
    envelope
        "ordo.evidence"
        [ field "id" (JString(EvidenceId.value evidence.Id))
          field "kind" (encodeEvidenceKind evidence.Kind)
          field "source" (
              JObject
                  [ field "system" (JString evidence.Source.System)
                    field
                        "reference"
                        (match evidence.Source.Reference with
                         | Some r -> JString r
                         | None -> JNull) ]
          )
          field "observedAt" (JString(Clock.toWire evidence.ObservedAt))
          field "content" evidence.Content ]

let decodeEvidence (document: JsonValue) : Result<Evidence, WireError> =
    readEnvelope "ordo.evidence" document
    |> Result.bind (fun document ->
        let id = identifier "id" EvidenceId.create document
        let kind = required "kind" document |> Result.bind decodeEvidenceKind

        let source =
            required "source" document
            |> Result.bind (fun source ->
                let system = requiredString "system" source

                let reference =
                    optional "reference" source
                    |> Result.map (
                        Option.bind (function
                            | JString r -> Some r
                            | _ -> None)
                    )

                match system, reference with
                | Ok system, Ok reference -> Ok { System = system; Reference = reference }
                | Error e, _ -> Error e
                | _, Error e -> Error e)

        let observedAt =
            requiredString "observedAt" document
            |> Result.bind (fun raw ->
                match Clock.fromWire raw with
                | Some instant -> Ok instant
                | None -> Error(InvalidField("$.observedAt", "not an ISO-8601 instant")))

        let content = required "content" document

        match id, kind, source, observedAt, content with
        | Ok id, Ok kind, Ok source, Ok observedAt, Ok content ->
            Ok
                { Id = id
                  Kind = kind
                  Source = source
                  ObservedAt = observedAt
                  Content = content }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e)

// ---------------------------------------------------- evidence requirement

let private encodeKindPattern (pattern: EvidenceKindPattern) =
    match pattern with
    | AnyDirect -> "direct"
    | AnyDerived -> "derived"
    | AnyInferred -> "inferred"

let private decodeKindPattern (token: string) =
    match token with
    | "direct" -> Ok AnyDirect
    | "derived" -> Ok AnyDerived
    | "inferred" -> Ok AnyInferred
    | other -> Error(UnknownVariant("acceptableKinds", other))

let encodeEvidenceRequirement (requirement: EvidenceRequirement) =
    envelope
        "ordo.evidence-requirement"
        [ field "id" (JString requirement.Id)
          field "description" (JString requirement.Description)
          field
              "maximumAgeSeconds"
              (match requirement.MaximumAge with
               | Some age -> JFloat age.TotalSeconds
               | None -> JNull)
          field "acceptableKinds" (JArray(requirement.AcceptableKinds |> List.map (encodeKindPattern >> JString))) ]

let decodeEvidenceRequirement (document: JsonValue) : Result<EvidenceRequirement, WireError> =
    readEnvelope "ordo.evidence-requirement" document
    |> Result.bind (fun document ->
        let id = requiredString "id" document
        let description = requiredString "description" document

        let maximumAge =
            optional "maximumAgeSeconds" document
            |> Result.bind (function
                | None
                | Some JNull -> Ok None
                | Some value ->
                    asFloat "$.maximumAgeSeconds" value
                    |> Result.mapError MalformedDocument
                    |> Result.map (TimeSpan.FromSeconds >> Some))

        let acceptableKinds =
            optional "acceptableKinds" document
            |> Result.bind (function
                | None
                | Some JNull -> Ok []
                | Some value ->
                    asArray "$.acceptableKinds" value
                    |> Result.mapError MalformedDocument
                    |> Result.bind (fun items ->
                        items
                        |> List.fold
                            (fun acc item ->
                                match acc with
                                | Error e -> Error e
                                | Ok acc ->
                                    asString "$.acceptableKinds[]" item
                                    |> Result.mapError MalformedDocument
                                    |> Result.bind decodeKindPattern
                                    |> Result.map (fun pattern -> pattern :: acc))
                            (Ok [])
                        |> Result.map List.rev))

        match id, description, maximumAge, acceptableKinds with
        | Ok id, Ok description, Ok maximumAge, Ok acceptableKinds ->
            Ok
                { Id = id
                  Description = description
                  MaximumAge = maximumAge
                  AcceptableKinds = acceptableKinds }
        | Error e, _, _, _
        | _, Error e, _, _
        | _, _, Error e, _
        | _, _, _, Error e -> Error e)

// ------------------------------------------------------------- coverage

let encodeCoverageRequirement (requirement: CoverageRequirement) =
    envelope
        "ordo.coverage-requirement"
        [ field "scope" (JString(CoverageScope.value requirement.Scope))
          field "description" (JString requirement.Description) ]

let decodeCoverageRequirement (document: JsonValue) : Result<CoverageRequirement, WireError> =
    readEnvelope "ordo.coverage-requirement" document
    |> Result.bind (fun document ->
        let scope =
            requiredString "scope" document
            |> Result.bind (fun raw ->
                CoverageScope.create raw
                |> Result.mapError (fun error -> InvalidField("$.scope", sprintf "%A" error)))

        let description = requiredString "description" document

        match scope, description with
        | Ok scope, Ok description -> Ok(CoverageRequirement.complete scope description)
        | Error error, _ -> Error error
        | _, Error error -> Error error)

let encodeContextCoverageClaim (claim: ContextCoverageClaim) =
    envelope
        "ordo.context-coverage"
        [ field "scope" (JString(claim.Scope |> CoverageScope.value))
          field "status" (JString(claim.Status |> CoverageStatus.toWire))
          field "provenanceEvidenceIds" (JArray(claim.Provenance |> List.map (EvidenceId.value >> JString))) ]

let decodeContextCoverageClaim (document: JsonValue) : Result<ContextCoverageClaim, WireError> =
    readEnvelope "ordo.context-coverage" document
    |> Result.bind (fun document ->
        let scope =
            requiredString "scope" document
            |> Result.bind (fun raw ->
                CoverageScope.create raw
                |> Result.mapError (fun error -> InvalidField("$.scope", sprintf "%A" error)))

        let status =
            requiredString "status" document
            |> Result.bind (fun raw ->
                match CoverageStatus.fromWire raw with
                | Some status -> Ok status
                | None -> Error(UnknownVariant("status", raw)))

        let provenance =
            required "provenanceEvidenceIds" document
            |> Result.bind (asArray "$.provenanceEvidenceIds" >> Result.mapError MalformedDocument)
            |> Result.bind (fun items ->
                items
                |> List.fold
                    (fun state item ->
                        match state with
                        | Error error -> Error error
                        | Ok ids ->
                            asString "$.provenanceEvidenceIds[]" item
                            |> Result.mapError MalformedDocument
                            |> Result.bind (fun raw ->
                                EvidenceId.create raw
                                |> Result.mapError (fun error ->
                                    InvalidField("$.provenanceEvidenceIds[]", sprintf "%A" error)))
                            |> Result.map (fun id -> id :: ids))
                    (Ok [])
                |> Result.map List.rev)

        match scope, status, provenance with
        | Ok scope, Ok status, Ok provenance ->
            ContextCoverageClaim.create scope status provenance
            |> Result.mapError (fun error -> InvalidField("$.provenanceEvidenceIds", sprintf "%A" error))
        | Error error, _, _ -> Error error
        | _, Error error, _ -> Error error
        | _, _, Error error -> Error error)

// ---------------------------------------------------------- state snapshot

let private encodeStateViewSchema (schema: StateViewSchema) =
    JObject
        [ field "id" (JString(StateViewSchema.id schema))
          field "version" (JInt(int64 (StateViewSchema.version schema))) ]

let private decodeStateViewSchema (document: JsonValue) : Result<StateViewSchema, WireError> =
    let id = requiredString "id" document

    let version =
        required "version" document
        |> Result.bind (asInt "$.viewSchema.version" >> Result.mapError MalformedDocument)

    match id, version with
    | Ok id, Ok version ->
        StateViewSchema.create id version
        |> Result.mapError (fun error -> InvalidField("$.viewSchema", sprintf "%A" error))
    | Error error, _ -> Error error
    | _, Error error -> Error error

let private stateSnapshotMembers (snapshot: StateSnapshot) =
    [ field "fingerprint" (JString(StateFingerprint.value snapshot.Fingerprint))
      field
          "revision"
          (match snapshot.Revision with
           | Some r -> JString r
           | None -> JNull)
      field "takenAt" (JString(Clock.toWire snapshot.TakenAt))
      field "view" snapshot.View ]

let encodeStateSnapshot (snapshot: StateSnapshot) =
    match snapshot.ViewSchema with
    | Some schema ->
        envelopeAt
            StateSnapshotSchemaVersion
            "ordo.state-snapshot"
            (field "viewSchema" (encodeStateViewSchema schema) :: stateSnapshotMembers snapshot)
    | None ->
        // Legacy schema-v1 records stay schema-v1 when re-encoded. Audit
        // tooling may preserve them, but no new live snapshot is constructed
        // this way.
        envelopeAt 1 "ordo.state-snapshot" (stateSnapshotMembers snapshot)

/// Decoding recomputes the fingerprint from the exact semantics of the wire
/// version and refuses a record whose stored fingerprint disagrees.
///
/// Schema-v1 snapshots decode with ViewSchema=None. They remain readable
/// historical facts but are not valid current authorisation input.
let decodeStateSnapshot (document: JsonValue) : Result<StateSnapshot, WireError> =
    readEnvelopeWithVersions "ordo.state-snapshot" stateSnapshotSupportedVersions document
    |> Result.bind (fun (wireVersion, document) ->
        let stored = requiredString "fingerprint" document
        let view = required "view" document

        let viewSchema =
            match wireVersion with
            | 1 -> Ok None
            | version when version = StateSnapshotSchemaVersion ->
                required "viewSchema" document
                |> Result.bind decodeStateViewSchema
                |> Result.map Some
            | other -> Error(UnsupportedSchemaVersion(other, stateSnapshotSupportedVersions))

        let takenAt =
            requiredString "takenAt" document
            |> Result.bind (fun raw ->
                match Clock.fromWire raw with
                | Some instant -> Ok instant
                | None -> Error(InvalidField("$.takenAt", "not an ISO-8601 instant")))

        let revision =
            optional "revision" document
            |> Result.map (
                Option.bind (function
                    | JString r -> Some r
                    | _ -> None)
            )

        match stored, viewSchema, view, takenAt, revision with
        | Ok stored, Ok viewSchema, Ok view, Ok takenAt, Ok revision ->
            let recomputed =
                match viewSchema with
                | Some schema -> StateFingerprint.ofView schema view
                | None -> StateFingerprint.ofLegacyV1View view

            if StateFingerprint.value recomputed <> stored then
                Error(InvalidField("$.fingerprint", "stored fingerprint does not match the stored view and view schema"))
            else
                Ok
                    { ViewSchema = viewSchema
                      View = view
                      Fingerprint = recomputed
                      Revision = revision
                      TakenAt = takenAt }
        | Error e, _, _, _, _
        | _, Error e, _, _, _
        | _, _, Error e, _, _
        | _, _, _, Error e, _
        | _, _, _, _, Error e -> Error e)

// -------------------------------------------------------------- obligation

let encodeObligationKind (kind: ObligationKind) =
    match kind with
    | AcquireEvidence requirementId ->
        JObject
            [ field "kind" (JString "acquire-evidence")
              field "requirementId" (JString requirementId) ]
    | ReacquireStaleEvidence requirementId ->
        JObject
            [ field "kind" (JString "reacquire-stale-evidence")
              field "requirementId" (JString requirementId) ]
    | HumanReview question ->
        JObject
            [ field "kind" (JString "human-review")
              field "question" (JString question) ]
    | RunVerification what ->
        JObject
            [ field "kind" (JString "run-verification")
              field "what" (JString what) ]
    | InvestigateFailure what ->
        JObject
            [ field "kind" (JString "investigate-failure")
              field "what" (JString what) ]
    | Custom label ->
        JObject
            [ field "kind" (JString "custom")
              field "label" (JString label) ]

let decodeObligationKind (document: JsonValue) : Result<ObligationKind, WireError> =
    requiredString "kind" document
    |> Result.bind (fun token ->
        let withField name build =
            requiredString name document |> Result.map build

        match token with
        | "acquire-evidence" -> withField "requirementId" AcquireEvidence
        | "reacquire-stale-evidence" -> withField "requirementId" ReacquireStaleEvidence
        | "human-review" -> withField "question" HumanReview
        | "run-verification" -> withField "what" RunVerification
        | "investigate-failure" -> withField "what" InvestigateFailure
        | "custom" -> withField "label" Custom
        | other -> Error(UnknownVariant("kind", other)))
