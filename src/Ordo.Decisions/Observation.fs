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
open Ordo.Decisions.Confidence
open Ordo.Decisions.Outcome
open Ordo.Decisions.Escalation

[<Literal>]
let SchemaVersion = 2

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
      /// Who requested the decision, as the request's `praxis.provenance/1`
      /// block: the requester's `created` contribution. Distinct from
      /// `Provider`, which is who answered: a requester is never derived
      /// from, or collapsed into, the provider identity (ORDO-PROV-02).
      /// `None` means the requester is unknown; nothing is inferred.
      RequestProvenance: ProvenanceBlock option }

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

    /// The recorded requester, or `None` when unknown.
    let requestedBy (observation: ResolutionObservation) =
        observation.RequestProvenance |> Option.bind Requester.ofBlock

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
    let encode (observation: ResolutionObservation) : JsonValue =
        JObject(
            [ "schema", JString "ordo.resolution-observation"
              "schemaVersion", JInt(int64 SchemaVersion)
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
            // Additive and omitted when unknown, so a legacy observation
            // encodes byte-for-byte as before and v2 readers that ignore
            // unknown members (Praxis `ros ordo ingest`) accept it
            // unchanged (ORDO-PROV-07).
            @ (observation.RequestProvenance
               |> Option.map (fun block -> "requestProvenance", ProvenanceBlock.toJson block)
               |> Option.toList)
        )

    /// Confidence as an observer should record it: never a bare number.
    let confidenceFacts (confidence: Confidence option) =
        confidence
        |> Option.map (fun c -> c.Magnitude, Confidence.provenanceToWire c.Provenance)
