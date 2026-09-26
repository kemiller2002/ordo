/// Execution facts, for whoever is watching.
///
/// Ordo exposes what happened. It does not rank providers, compute
/// calibration, judge whether a decision was worth its cost, or recommend
/// anything: those are interpretations, and they belong to the observing
/// system that has the history to make them (ORDO-7201 / ORDO-3-102).
///
/// Nothing in this module references ROS, and nothing in this library
/// requires an observer to exist (ORDO-3201 / ORDO-8604). An observation is
/// a value the caller may persist, forward, or drop.
module Ordo.Decisions.Observation

open System
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.Clock
open Ordo.Core.Resolution
open Ordo.Core.Coverage
open Ordo.Core.Policy
open Ordo.Core.StateIdentity
open Ordo.Core.Provenance
open Ordo.Core.Wire
open Ordo.Decisions.Confidence
open Ordo.Decisions.Outcome
open Ordo.Decisions.Escalation

/// Schema v3 added `requestedBy` (DF-SDE-2026-D68A). v2 records remain
/// readable and decode with no requester; nothing is invented for them.
[<Literal>]
let SchemaVersion = 3

/// Versions this build reads. Anything else is refused, not guessed at.
let SupportedSchemaVersions = [ 2; SchemaVersion ]

/// One resolution execution, as facts.
///
/// `SelectedChoice` is the wire token rather than the domain choice, because
/// an observer is not generic in a domain's types and must be able to read
/// the record without them.
type ResolutionObservation =
    { Resolution: ResolutionId
      Correlation: CorrelationId option
      CausedBy: ResolutionId option
      Mode: ResolutionMode
      Contract: DecisionContractId
      ContractVersion: ContractVersion
      Request: DecisionRequestId
      /// Which state was judged. Without this an observer cannot tell a
      /// stale decision from a wrong one.
      State: StateFingerprint
      /// Domain-defined identity of the semantically complete state view used
      /// for this execution. New live requests always carry one.
      StateViewSchema: StateViewSchema
      /// Scoped completeness claims supplied to this execution. These remain
      /// audit facts even when incomplete coverage stops the provider call.
      Coverage: ContextCoverageClaim list
      /// Absent when no provider was called — a request refused for missing
      /// evidence or a missing capability never reaches one.
      Provider: ProviderIdentity option
      StartedAt: DateTimeOffset
      CompletedAt: DateTimeOffset
      Outcome: string
      SelectedChoice: string option
      /// Magnitude and provenance travel together here as they do
      /// everywhere else: a number alone would let a self-report be read as
      /// a measured probability (ORDO-1103).
      Confidence: (float * string) option
      EvidenceUsed: EvidenceId list
      Escalation: EscalationStep list
      /// Filled in once a transition has been evaluated against this
      /// decision. Absent means no transition was attempted — not that one
      /// was refused.
      Transition: string option
      Policy: PolicyIdentity option
      Usage: ProviderUsage
      TransportRetries: int
      /// An external experiment or work-item identifier, carried verbatim.
      /// Lets a research system correlate records without Ordo depending on
      /// one (ORDO-4403 / ORDO-8204).
      ExperimentReference: string option
      /// Who asked for this execution, copied from the host-built request.
      /// Self-reported provenance, not authority and not evidence. `None`
      /// when the host supplied none, and always `None` for a schema-v2
      /// record, which predates the field.
      RequestedBy: Requester option }

[<RequireQualifiedAccess>]
module ResolutionObservation =

    let duration (observation: ResolutionObservation) =
        observation.CompletedAt - observation.StartedAt

    let withTransition (result: string) (observation: ResolutionObservation) =
        { observation with Transition = Some result }

    let withPolicy (policy: PolicyIdentity) (observation: ResolutionObservation) =
        { observation with Policy = Some policy }

    let withExperimentReference (reference: string) (observation: ResolutionObservation) =
        { observation with ExperimentReference = Some reference }

    let private optionalString value =
        match value with
        | Some text -> JString text
        | None -> JNull

    let private optionalInt value =
        match value with
        | Some n -> JInt(int64 (n: int))
        | None -> JNull

    /// The wire form. Hand-written for the same reason as every other wire
    /// shape here: an observer's stored history must not break because a
    /// field was renamed in F# (ORDO-7202 / ORDO-8402).
    let private encodeMembers (version: int) (observation: ResolutionObservation) : JsonValue =
        JObject(
            [ "schema", JString "ordo.resolution-observation"
              "schemaVersion", JInt(int64 version)
              "resolutionId", JString(ResolutionId.value observation.Resolution)
              "correlationId", optionalString (observation.Correlation |> Option.map CorrelationId.value)
              "causedBy", optionalString (observation.CausedBy |> Option.map ResolutionId.value)
              "mode", JString(ResolutionMode.toWire observation.Mode)
              "contractId", JString(DecisionContractId.value observation.Contract)
              "contractVersion", JInt(int64 (ContractVersion.value observation.ContractVersion))
              "requestId", JString(DecisionRequestId.value observation.Request)
              "stateFingerprint", JString(StateFingerprint.value observation.State)
              "stateViewSchema",
              JObject
                  [ "id", JString(StateViewSchema.id observation.StateViewSchema)
                    "version", JInt(int64 (StateViewSchema.version observation.StateViewSchema)) ]
              "coverage",
              JArray(
                  observation.Coverage
                  |> List.map (fun claim ->
                      JObject
                          [ "scope", JString(CoverageScope.value claim.Scope)
                            "status", JString(CoverageStatus.toWire claim.Status)
                            "provenanceEvidenceIds",
                            JArray(claim.Provenance |> List.map (EvidenceId.value >> JString)) ])
              )
              "provider",
              (match observation.Provider with
               | None -> JNull
               | Some identity ->
                   JObject
                       [ "id", JString(ProviderId.value identity.Provider)
                         "model", optionalString identity.Model
                         "modelVersion", optionalString identity.ModelVersion
                         "adapterVersion", JString identity.AdapterVersion ])
              "startedAt", JString(toWire observation.StartedAt)
              "completedAt", JString(toWire observation.CompletedAt)
              "durationMilliseconds", JFloat (duration observation).TotalMilliseconds
              "outcome", JString observation.Outcome
              "selectedChoice", optionalString observation.SelectedChoice
              "confidence",
              (match observation.Confidence with
               | None -> JNull
               | Some(magnitude, provenance) ->
                   JObject
                       [ "magnitude", JFloat magnitude
                         "provenance", JString provenance ])
              "evidenceUsed", JArray(observation.EvidenceUsed |> List.map (EvidenceId.value >> JString))
              "escalation",
              JArray(
                  observation.Escalation
                  |> List.map (fun step ->
                      JObject
                          [ "resolutionId", JString(ResolutionId.value step.Resolution)
                            "from", JString(ResolutionMode.toWire step.From)
                            "to", JString(EscalationTarget.toWire step.To)
                            "reason", JString step.Reason
                            "at", JString(toWire step.At) ])
              )
              "transition", optionalString observation.Transition
              "policy",
              (match observation.Policy with
               | None -> JNull
               | Some policy ->
                   JObject
                       [ "id", JString(PolicyId.value policy.Id)
                         "version", JInt(int64 (PolicyVersion.value policy.Version))
                         "experimental", JBool policy.Experimental ])
              "usage",
              JObject
                  [ "inputTokens", optionalInt observation.Usage.InputTokens
                    "outputTokens", optionalInt observation.Usage.OutputTokens
                    "cachedInputTokens", optionalInt observation.Usage.CachedInputTokens
                    "providerReportedCost", optionalString observation.Usage.ProviderReportedCost ]
              "transportRetries", JInt(int64 observation.TransportRetries)
              "experimentReference", optionalString observation.ExperimentReference ]
            @ (if version >= 3 then
                   [ "requestedBy",
                     (match observation.RequestedBy with
                      | Some requester -> encodeRequester requester
                      | None -> JNull) ]
               else
                   [])
        )

    /// Writes a record at a specific supported schema version, so a v2
    /// record read from history can be written back as v2 rather than being
    /// silently upgraded. v2 has no place for a requester, so an observation
    /// that carries one is refused at v2 rather than having it dropped.
    let encodeAtVersion (version: int) (observation: ResolutionObservation) : Result<JsonValue, WireError> =
        match version, observation.RequestedBy with
        | version, _ when not (List.contains version SupportedSchemaVersions) ->
            Error(UnsupportedSchemaVersion(version, SupportedSchemaVersions))
        | 2, Some _ -> Error(InvalidField("$.requestedBy", "schema v2 cannot carry a requester; encode at v3"))
        | version, _ -> Ok(encodeMembers version observation)

    /// The current wire form (schema v3).
    let encode (observation: ResolutionObservation) : JsonValue = encodeMembers SchemaVersion observation

    /// Confidence as an observer should record it: never a bare number.
    let confidenceFacts (confidence: Confidence option) =
        confidence
        |> Option.map (fun c -> c.Magnitude, Confidence.provenanceToWire c.Provenance)

    type private ResultBuilder() =
        member _.Bind(result: Result<'a, WireError>, next: 'a -> Result<'b, WireError>) = Result.bind next result
        member _.Return(value: 'a) : Result<'a, WireError> = Ok value
        member _.ReturnFrom(result: Result<'a, WireError>) = result

    let private decoding = ResultBuilder()

    let private json result = result |> Result.mapError MalformedDocument

    let private member' (name: string) (document: JsonValue) = requiredMember name document |> json

    let private text (path: string) (document: JsonValue) (name: string) =
        member' name document |> Result.bind (asString (path + name) >> json)

    let private optionalText (path: string) (document: JsonValue) (name: string) =
        tryMember name document
        |> json
        |> Result.bind (function
            | None
            | Some JNull -> Ok None
            | Some(JString value) -> Ok(Some value)
            | Some _ -> Error(InvalidField(path + name, "must be a string or null")))

    let private readOptionalInt (path: string) (document: JsonValue) (name: string) =
        tryMember name document
        |> json
        |> Result.bind (function
            | None
            | Some JNull -> Ok None
            | Some value -> asInt (path + name) value |> json |> Result.map Some)

    let private validated (path: string) (create: string -> Result<'id, 'error>) (raw: string) =
        create raw |> Result.mapError (fun error -> InvalidField(path, sprintf "%A" error))

    let private instant (path: string) (raw: string) =
        match fromWire raw with
        | Some value -> Ok value
        | None -> Error(InvalidField(path, "not an ISO-8601 instant"))

    let private token (path: string) (parse: string -> 'a option) (raw: string) =
        match parse raw with
        | Some value -> Ok value
        | None -> Error(UnknownVariant(path, raw))

    let private each (path: string) (decode: JsonValue -> Result<'a, WireError>) (value: JsonValue) =
        asArray path value
        |> json
        |> Result.bind (fun items ->
            items
            |> List.fold
                (fun state item ->
                    match state with
                    | Error error -> Error error
                    | Ok decoded -> decode item |> Result.map (fun value -> value :: decoded))
                (Ok [])
            |> Result.map List.rev)

    let private evidenceId (path: string) (value: JsonValue) =
        asString path value |> json |> Result.bind (validated path EvidenceId.create)

    let private decodeCoverage (claim: JsonValue) =
        decoding {
            let! scope = text "$.coverage[]." claim "scope" |> Result.bind (validated "$.coverage[].scope" CoverageScope.create)
            let! status = text "$.coverage[]." claim "status" |> Result.bind (token "coverage[].status" CoverageStatus.fromWire)
            let! provenance = member' "provenanceEvidenceIds" claim |> Result.bind (each "$.coverage[].provenanceEvidenceIds" (evidenceId "$.coverage[].provenanceEvidenceIds[]"))
            return! ContextCoverageClaim.create scope status provenance |> Result.mapError (fun error -> InvalidField("$.coverage[]", sprintf "%A" error))
        }

    let private decodeProvider (value: JsonValue) =
        match value with
        | JNull -> Ok None
        | identity ->
            decoding {
                let! provider = text "$.provider." identity "id" |> Result.bind (validated "$.provider.id" ProviderId.create)
                let! model = optionalText "$.provider." identity "model"
                let! modelVersion = optionalText "$.provider." identity "modelVersion"
                let! adapterVersion = text "$.provider." identity "adapterVersion"

                return
                    Some
                        { Provider = provider
                          Model = model
                          ModelVersion = modelVersion
                          AdapterVersion = adapterVersion }
            }

    let private decodeConfidence (value: JsonValue) =
        match value with
        | JNull -> Ok None
        | confidence ->
            decoding {
                let! magnitude = member' "magnitude" confidence |> Result.bind (asFloat "$.confidence.magnitude" >> json)
                let! provenance = text "$.confidence." confidence "provenance"
                return Some(magnitude, provenance)
            }

    let private decodeEscalation (step: JsonValue) =
        decoding {
            let! resolution = text "$.escalation[]." step "resolutionId" |> Result.bind (validated "$.escalation[].resolutionId" ResolutionId.create)
            let! from = text "$.escalation[]." step "from" |> Result.bind (token "escalation[].from" ResolutionMode.fromWire)
            let! target = text "$.escalation[]." step "to" |> Result.bind (token "escalation[].to" EscalationTarget.fromWire)
            let! reason = text "$.escalation[]." step "reason"
            let! at = text "$.escalation[]." step "at" |> Result.bind (instant "$.escalation[].at")

            return
                { Resolution = resolution
                  From = from
                  To = target
                  Reason = reason
                  At = at }
        }

    let private decodePolicy (value: JsonValue) =
        match value with
        | JNull -> Ok None
        | policy ->
            decoding {
                let! id = text "$.policy." policy "id" |> Result.bind (validated "$.policy.id" PolicyId.create)
                let! version = member' "version" policy |> Result.bind (asInt "$.policy.version" >> json) |> Result.bind (fun raw -> PolicyVersion.create raw |> Result.mapError (fun error -> InvalidField("$.policy.version", sprintf "%A" error)))

                let! experimental =
                    member' "experimental" policy
                    |> Result.bind (function
                        | JBool flag -> Ok flag
                        | _ -> Error(InvalidField("$.policy.experimental", "must be a boolean")))

                return
                    Some
                        { Id = id
                          Version = version
                          Experimental = experimental }
            }

    let private decodeUsage (usage: JsonValue) =
        decoding {
            let! input = readOptionalInt "$.usage." usage "inputTokens"
            let! output = readOptionalInt "$.usage." usage "outputTokens"
            let! cached = readOptionalInt "$.usage." usage "cachedInputTokens"
            let! cost = optionalText "$.usage." usage "providerReportedCost"

            return
                { InputTokens = input
                  OutputTokens = output
                  CachedInputTokens = cached
                  ProviderReportedCost = cost }
        }

    /// The requester is read only from a schema that has it. A v2 record
    /// decodes with `None`, never an invented requester, and a v2 record that
    /// nevertheless contains `requestedBy` is refused rather than read under
    /// semantics its version never had (DF-SDE-2026-0013).
    let private decodeRequestedBy (version: int) (document: JsonValue) =
        tryMember "requestedBy" document
        |> json
        |> Result.bind (fun found ->
            match version, found with
            | 2, None -> Ok None
            | 2, Some _ -> Error(InvalidField("$.requestedBy", "schema v2 has no requestedBy; refusing to reinterpret"))
            | _, None -> Error(MalformedDocument(MissingMember "$.requestedBy"))
            | _, Some JNull -> Ok None
            | _, Some requester -> decodeRequester requester |> Result.map Some)

    /// A decoded observation together with the schema version that wrote it,
    /// so audit tooling keeps showing which version produced each record and
    /// can write it back unchanged with `encodeAtVersion`.
    type DecodedObservation =
        { SchemaVersion: int
          Observation: ResolutionObservation }

    /// Reads a stored observation of any supported schema version.
    ///
    /// `durationMilliseconds` is derived from the two instants and is not
    /// read back.
    let decode (document: JsonValue) : Result<DecodedObservation, WireError> =
        decoding {
            let! schema = text "$." document "schema"

            if schema <> "ordo.resolution-observation" then
                return! Error(UnexpectedSchema(schema, "ordo.resolution-observation"))
            else
                let! version = member' "schemaVersion" document |> Result.bind (asInt "$.schemaVersion" >> json)

                if not (List.contains version SupportedSchemaVersions) then
                    return! Error(UnsupportedSchemaVersion(version, SupportedSchemaVersions))
                else
                    let! resolution = text "$." document "resolutionId" |> Result.bind (validated "$.resolutionId" ResolutionId.create)

                    let! correlation =
                        optionalText "$." document "correlationId"
                        |> Result.bind (function
                            | None -> Ok None
                            | Some raw -> validated "$.correlationId" CorrelationId.create raw |> Result.map Some)

                    let! causedBy =
                        optionalText "$." document "causedBy"
                        |> Result.bind (function
                            | None -> Ok None
                            | Some raw -> validated "$.causedBy" ResolutionId.create raw |> Result.map Some)

                    let! mode = text "$." document "mode" |> Result.bind (token "mode" ResolutionMode.fromWire)
                    let! contract = text "$." document "contractId" |> Result.bind (validated "$.contractId" DecisionContractId.create)

                    let! contractVersion =
                        member' "contractVersion" document
                        |> Result.bind (asInt "$.contractVersion" >> json)
                        |> Result.bind (fun raw -> ContractVersion.create raw |> Result.mapError (fun error -> InvalidField("$.contractVersion", sprintf "%A" error)))

                    let! request = text "$." document "requestId" |> Result.bind (validated "$.requestId" DecisionRequestId.create)

                    let! state =
                        text "$." document "stateFingerprint"
                        |> Result.bind (fun raw ->
                            match StateFingerprint.parse raw with
                            | Some fingerprint -> Ok fingerprint
                            | None -> Error(InvalidField("$.stateFingerprint", "not a state fingerprint")))

                    let! viewSchema = member' "stateViewSchema" document

                    let! stateViewSchema =
                        decoding {
                            let! id = text "$.stateViewSchema." viewSchema "id"
                            let! version = member' "version" viewSchema |> Result.bind (asInt "$.stateViewSchema.version" >> json)
                            return! StateViewSchema.create id version |> Result.mapError (fun error -> InvalidField("$.stateViewSchema", sprintf "%A" error))
                        }

                    let! coverage = member' "coverage" document |> Result.bind (each "$.coverage" decodeCoverage)
                    let! provider = member' "provider" document |> Result.bind decodeProvider
                    let! startedAt = text "$." document "startedAt" |> Result.bind (instant "$.startedAt")
                    let! completedAt = text "$." document "completedAt" |> Result.bind (instant "$.completedAt")
                    let! outcome = text "$." document "outcome"
                    let! selectedChoice = optionalText "$." document "selectedChoice"
                    let! confidence = member' "confidence" document |> Result.bind decodeConfidence
                    let! evidenceUsed = member' "evidenceUsed" document |> Result.bind (each "$.evidenceUsed" (evidenceId "$.evidenceUsed[]"))
                    let! escalation = member' "escalation" document |> Result.bind (each "$.escalation" decodeEscalation)
                    let! transition = optionalText "$." document "transition"
                    let! policy = member' "policy" document |> Result.bind decodePolicy
                    let! usage = member' "usage" document |> Result.bind decodeUsage
                    let! retries = member' "transportRetries" document |> Result.bind (asInt "$.transportRetries" >> json)
                    let! experimentReference = optionalText "$." document "experimentReference"
                    let! requestedBy = decodeRequestedBy version document

                    return
                        { SchemaVersion = version
                          Observation =
                            { Resolution = resolution
                              Correlation = correlation
                              CausedBy = causedBy
                              Mode = mode
                              Contract = contract
                              ContractVersion = contractVersion
                              Request = request
                              State = state
                              StateViewSchema = stateViewSchema
                              Coverage = coverage
                              Provider = provider
                              StartedAt = startedAt
                              CompletedAt = completedAt
                              Outcome = outcome
                              SelectedChoice = selectedChoice
                              Confidence = confidence
                              EvidenceUsed = evidenceUsed
                              Escalation = escalation
                              Transition = transition
                              Policy = policy
                              Usage = usage
                              TransportRetries = retries
                              ExperimentReference = experimentReference
                              RequestedBy = requestedBy } }
        }
