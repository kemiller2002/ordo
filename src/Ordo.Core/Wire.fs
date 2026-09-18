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
open Ordo.Core.Obligation
open Ordo.Core.StateIdentity

/// The schema version this build writes, and the only one it reads.
///
/// A single number for the whole module: these records are written and read
/// together, and per-record versions would be version proliferation without
/// a semantic difference to justify it (ORDO-2202).
[<Literal>]
let SchemaVersion = 1

let private supportedVersions = [ SchemaVersion ]

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

let private envelope (schema: string) (members: (string * JsonValue) list) =
    JObject(
        [ field "schema" (JString schema)
          field "schemaVersion" (JInt(int64 SchemaVersion)) ]
        @ members
    )

let private readEnvelope (expectedSchema: string) (document: JsonValue) : Result<JsonValue, WireError> =
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
                if List.contains version supportedVersions then
                    Ok document
                else
                    Error(UnsupportedSchemaVersion(version, supportedVersions))))

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

// ---------------------------------------------------------- state snapshot

let encodeStateSnapshot (snapshot: StateSnapshot) =
    envelope
        "ordo.state-snapshot"
        [ field "fingerprint" (JString(StateFingerprint.value snapshot.Fingerprint))
          field
              "revision"
              (match snapshot.Revision with
               | Some r -> JString r
               | None -> JNull)
          field "takenAt" (JString(Clock.toWire snapshot.TakenAt))
          field "view" snapshot.View ]

/// Decoding recomputes the fingerprint from the view and refuses a record
/// whose stored fingerprint disagrees.
///
/// The alternative — trusting the stored value — would let a tampered or
/// corrupted view travel under the identity of the state that was actually
/// judged, which is the one thing state identity exists to prevent.
let decodeStateSnapshot (document: JsonValue) : Result<StateSnapshot, WireError> =
    readEnvelope "ordo.state-snapshot" document
    |> Result.bind (fun document ->
        let stored = requiredString "fingerprint" document
        let view = required "view" document

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

        match stored, view, takenAt, revision with
        | Ok stored, Ok view, Ok takenAt, Ok revision ->
            let recomputed = StateFingerprint.ofView view

            if StateFingerprint.value recomputed <> stored then
                Error(InvalidField("$.fingerprint", "stored fingerprint does not match the stored view"))
            else
                Ok
                    { View = view
                      Fingerprint = recomputed
                      Revision = revision
                      TakenAt = takenAt }
        | Error e, _, _, _
        | _, Error e, _, _
        | _, _, Error e, _
        | _, _, _, Error e -> Error e)

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
