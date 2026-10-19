/// A strict consumer boundary over ECIR. It publishes the supported version,
/// canonical digest, cohort-scoped authorization rules and requirement-to-
/// verification traceability without inventing a second construction model.
module Ordo.Core.ConstructibilityContract

open Ordo.Core.Json
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionIrWire

[<Literal>]
let DiagnosticsSchemaVersion = "ordo.constructibility-diagnostics/1"

[<Literal>]
let ContractViewSchemaVersion = "ordo.constructibility-contract/1"

type VersionNegotiation =
    | Supported of selected: string
    | Unsupported of requested: string * supported: string list

/// ECIR currently has no safe automatic migration. A new version must add an
/// explicit migration entry before a consumer may reinterpret older bytes.
let supportedVersions = [ SchemaVersion ]
let automaticMigrations : (string * string) list = []

let negotiateVersion requested =
    if List.contains requested supportedVersions then Supported requested
    else Unsupported(requested, supportedVersions)

type Diagnostic =
    { Code: string
      Subject: string option
      Message: string }

type DiagnosticSet =
    { SchemaVersion: string
      Diagnostics: Diagnostic list }

type RequirementTraceability =
    { RequirementKey: string
      RequiredByCohort: bool
      NodeIds: string list
      OwnerCohortIds: string list
      DecisionIds: string list
      ConflictIds: string list
      VerificationObligationIds: string list }

type RequirementContract =
    { RequirementKey: string
      Disposition: string
      Reason: string option
      Supersedes: string option
      NodeIds: string list }

type NodeContract =
    { NodeId: string
      Kind: string
      RequirementKeys: string list
      DependsOn: string list
      Justification: string option }

type ContractView =
    { SchemaVersion: string
      SourceManifestDigest: string
      ContractDigest: string
      CohortId: string
      Requirements: RequirementContract list
      Nodes: NodeContract list
      Traceability: RequirementTraceability list }

type PreparedContract = private PreparedContract of ContractView * PreparedCohort

let contractView (PreparedContract(view, _)) = view

let private diag code subject message =
    { Code = code; Subject = subject; Message = message }

let private violationDiagnostic violation =
    let code, subject =
        match violation with
        | UnsupportedSchema actual -> "ORDO.CONTRACT.UNSUPPORTED_SCHEMA", Some actual
        | EmptyIntake -> "ORDO.CONTRACT.EMPTY_INTAKE", None
        | InvalidField(owner, field) -> "ORDO.CONTRACT.INVALID_FIELD", Some(owner + ":" + field)
        | SourceDigestMismatch -> "ORDO.CONTRACT.SOURCE_DIGEST_MISMATCH", None
        | DuplicateManifestRequirement key -> "ORDO.CONTRACT.DUPLICATE_MANIFEST_REQUIREMENT", Some key
        | DuplicateBlueprintRequirement key -> "ORDO.CONTRACT.DUPLICATE_BLUEPRINT_REQUIREMENT", Some key
        | MissingRequirement key -> "ORDO.CONTRACT.MISSING_REQUIREMENT", Some key
        | InventedRequirement key -> "ORDO.CONTRACT.INVENTED_REQUIREMENT", Some key
        | ChangedRequirement key -> "ORDO.CONTRACT.CHANGED_REQUIREMENT", Some key
        | DuplicateNode id -> "ORDO.CONTRACT.DUPLICATE_NODE", Some id
        | MissingRequirementMapping key -> "ORDO.CONTRACT.MISSING_REQUIREMENT_MAPPING", Some key
        | MissingNode(source, id) -> "ORDO.CONTRACT.MISSING_NODE", Some(source + "->" + id)
        | MissingRequirementReference(id, key) -> "ORDO.CONTRACT.MISSING_REQUIREMENT_REFERENCE", Some(id + "->" + key)
        | NonReciprocalLink(key, id) -> "ORDO.CONTRACT.NON_RECIPROCAL_LINK", Some(key + "<->" + id)
        | DuplicateLink(source, target) -> "ORDO.CONTRACT.DUPLICATE_LINK", Some(source + "->" + target)
        | UnjustifiedNode id -> "ORDO.CONTRACT.UNJUSTIFIED_NODE", Some id
        | MissingDispositionReason key -> "ORDO.CONTRACT.MISSING_DISPOSITION_REASON", Some key
        | MissingDispositionNode(key, kind) -> "ORDO.CONTRACT.MISSING_DISPOSITION_NODE", Some(key + "->" + sprintf "%A" kind)
        | InvalidSupersession(key, target) -> "ORDO.CONTRACT.INVALID_SUPERSESSION", Some(key + "->" + target)
        | MissingVerification key -> "ORDO.CONTRACT.MISSING_VERIFICATION", Some key
        | MissingConstructionCohort key -> "ORDO.CONTRACT.MISSING_OWNER_COHORT", Some key
        | MissingDependency(id, target) -> "ORDO.CONTRACT.MISSING_DEPENDENCY", Some(id + "->" + target)
        | SelfDependency id -> "ORDO.CONTRACT.SELF_DEPENDENCY", Some id
        | DependencyCycle id -> "ORDO.CONTRACT.DEPENDENCY_CYCLE", Some id

    diag code subject (sprintf "%A" violation)

let private readinessDiagnostic violation =
    match violation with
    | InvalidBlueprint problem -> violationDiagnostic problem
    | MissingCohort id -> diag "ORDO.CONTRACT.MISSING_COHORT" (Some id) (sprintf "%A" violation)
    | NotACohort id -> diag "ORDO.CONTRACT.NOT_A_COHORT" (Some id) (sprintf "%A" violation)
    | BlockedRequirement key -> diag "ORDO.CONTRACT.BLOCKED_REQUIREMENT" (Some key) (sprintf "%A" violation)
    | UnknownAuthorization id -> diag "ORDO.CONTRACT.UNKNOWN_AUTHORIZATION" (Some id) (sprintf "%A" violation)
    | AuthorizationKindMismatch(id, kind) ->
        diag "ORDO.CONTRACT.AUTHORIZATION_KIND_MISMATCH" (Some(id + "->" + sprintf "%A" kind)) (sprintf "%A" violation)
    | AuthorizationOutsideCohort id ->
        diag "ORDO.CONTRACT.AUTHORIZATION_OUTSIDE_COHORT" (Some id) (sprintf "%A" violation)
    | MissingDecisionAuthorization id ->
        diag "ORDO.CONTRACT.MISSING_DECISION_AUTHORIZATION" (Some id) (sprintf "%A" violation)
    | MissingRationaleAuthorization id ->
        diag "ORDO.CONTRACT.MISSING_RATIONALE_AUTHORIZATION" (Some id) (sprintf "%A" violation)

let private failure diagnostics =
    { SchemaVersion = DiagnosticsSchemaVersion
      Diagnostics = diagnostics |> List.sortBy (fun item -> item.Code, item.Subject, item.Message) }

let private dispositionContract disposition =
    match disposition with
    | Modeled -> "modeled", None, None
    | Deferred reason -> "deferred", Some reason, None
    | Unresolved reason -> "unresolved", Some reason, None
    | Rejected reason -> "rejected", Some reason, None
    | Superseded(target, reason) -> "superseded", Some reason, Some target

let private nodeKind = function
    | Decision -> "decision"
    | Contract -> "contract"
    | Invariant -> "invariant"
    | Interface -> "interface"
    | Cohort -> "cohort"
    | VerificationObligation -> "verificationObligation"
    | Conflict -> "conflict"
    | Deferral -> "deferral"
    | EngineeringRationale -> "engineeringRationale"

let private requirements (view: PreparedCohortView) =
    view.Blueprint.Requirements
    |> List.map (fun requirement ->
        let disposition, reason, supersedes = dispositionContract requirement.Disposition
        { RequirementKey = requirement.Source.Key
          Disposition = disposition
          Reason = reason
          Supersedes = supersedes
          NodeIds = requirement.NodeIds |> List.distinct |> List.sort })
    |> List.sortBy _.RequirementKey

let private nodes (view: PreparedCohortView) =
    view.Blueprint.Nodes
    |> List.map (fun node ->
        { NodeId = node.Id
          Kind = nodeKind node.Kind
          RequirementKeys = node.RequirementKeys |> List.distinct |> List.sort
          DependsOn = node.DependsOn |> List.distinct |> List.sort
          Justification = node.Justification })
    |> List.sortBy _.NodeId

let private traceability (view: PreparedCohortView) =
    let forKind key kind =
        view.Blueprint.Nodes
        |> List.filter (fun node -> node.Kind = kind && List.contains key node.RequirementKeys)
        |> List.map _.Id
        |> List.distinct
        |> List.sort

    view.Blueprint.Requirements
    |> List.map (fun requirement ->
        let key = requirement.Source.Key
        { RequirementKey = key
          RequiredByCohort = Set.contains key view.RequiredRequirementKeys
          NodeIds = requirement.NodeIds |> List.distinct |> List.sort
          OwnerCohortIds = forKind key Cohort
          DecisionIds = forKind key Decision
          ConflictIds = forKind key Conflict
          VerificationObligationIds = forKind key VerificationObligation })
    |> List.sortBy _.RequirementKey

/// Validate one exact manifest/blueprint/cohort/authorization tuple. The
/// caller supplies the independently pinned blueprint digest; deriving it
/// from the same untrusted bytes would not establish authority.
let prepare
    (manifest: SourceManifest)
    (blueprint: Blueprint)
    (pinnedBlueprintDigest: string)
    (cohortId: string)
    (authorizedNodeIds: Set<string>)
    : Result<PreparedContract, DiagnosticSet> =

    match negotiateVersion blueprint.SchemaVersion with
    | Unsupported(requested, supported) ->
        Error(failure [ diag "ORDO.CONTRACT.UNSUPPORTED_SCHEMA" (Some requested)
                            ("supported schema versions: " + String.concat ", " supported) ])
    | Supported _ ->
        match verifyManifestDigest manifest with
        | Error message -> Error(failure [ diag "ORDO.CONTRACT.INVALID_MANIFEST_DIGEST" None message ])
        | Ok () ->
            match verifyBlueprintDigest pinnedBlueprintDigest blueprint with
            | Error message -> Error(failure [ diag "ORDO.CONTRACT.BLUEPRINT_DIGEST_MISMATCH" None message ])
            | Ok () ->
                match prepareCohort manifest blueprint cohortId authorizedNodeIds with
                | Error violations -> Error(failure (violations |> List.map readinessDiagnostic))
                | Ok cohort ->
                    let view = preparedCohortView cohort
                    Ok(PreparedContract(
                        { SchemaVersion = ContractViewSchemaVersion
                          SourceManifestDigest = manifest.Digest
                          ContractDigest = blueprintDigest blueprint
                          CohortId = cohortId
                          Requirements = requirements view
                          Nodes = nodes view
                          Traceability = traceability view },
                        cohort))

/// The schema version a document claims, read before any other field is
/// interpreted so a future version is refused rather than misparsed.
let private requestedVersion json =
    match Ordo.Core.Json.parse json with
    | Ok(JObject members) ->
        match members |> List.choose (fun (name, value) -> if name = "schemaVersion" then Some value else None) with
        | [ JString version ] -> Some version
        | _ -> None
    | _ -> None

let private unsupportedSchema requested supported =
    failure [ diag "ORDO.CONTRACT.UNSUPPORTED_SCHEMA" (Some requested)
                  ("supported schema versions: " + String.concat ", " supported) ]

/// Strict JSON intake with stable diagnostic codes. Parsing and semantic
/// diagnostics share one machine-readable envelope.
let readAndPrepare manifestJson blueprintJson pinnedBlueprintDigest cohortId authorizedNodeIds =
    match readManifest manifestJson with
    | Error message -> Error(failure [ diag "ORDO.CONTRACT.INVALID_MANIFEST_JSON" None message ])
    | Ok manifest ->
        match requestedVersion blueprintJson |> Option.map negotiateVersion with
        | Some(Unsupported(requested, supported)) -> Error(unsupportedSchema requested supported)
        | _ ->
            match readBlueprint blueprintJson with
            | Error message -> Error(failure [ diag "ORDO.CONTRACT.INVALID_BLUEPRINT_JSON" None message ])
            | Ok blueprint -> prepare manifest blueprint pinnedBlueprintDigest cohortId authorizedNodeIds

let encodeDiagnostics (diagnostics: DiagnosticSet) =
    JObject [
        "schemaVersion", JString diagnostics.SchemaVersion
        "diagnostics", JArray [
            for item in diagnostics.Diagnostics do
                JObject(
                    [ "code", JString item.Code
                      "message", JString item.Message ]
                    @ (item.Subject |> Option.map (fun subject -> [ "subject", JString subject ]) |> Option.defaultValue [])) ] ]
    |> renderCanonical

let encodeContractView (view: ContractView) =
    JObject [
        "schemaVersion", JString view.SchemaVersion
        "sourceManifestDigest", JString view.SourceManifestDigest
        "contractDigest", JString view.ContractDigest
        "cohortId", JString view.CohortId
        "requirements", JArray [
            for item in view.Requirements do
                JObject(
                    [ "requirementKey", JString item.RequirementKey
                      "disposition", JString item.Disposition
                      "nodeIds", JArray(item.NodeIds |> List.map JString) ]
                    @ (item.Reason |> Option.map (fun reason -> [ "reason", JString reason ]) |> Option.defaultValue [])
                    @ (item.Supersedes |> Option.map (fun target -> [ "supersedes", JString target ]) |> Option.defaultValue [])) ]
        "nodes", JArray [
            for item in view.Nodes do
                JObject(
                    [ "nodeId", JString item.NodeId
                      "kind", JString item.Kind
                      "requirementKeys", JArray(item.RequirementKeys |> List.map JString)
                      "dependsOn", JArray(item.DependsOn |> List.map JString) ]
                    @ (item.Justification |> Option.map (fun justification -> [ "justification", JString justification ]) |> Option.defaultValue [])) ]
        "traceability", JArray [
            for item in view.Traceability do
                JObject [
                    "requirementKey", JString item.RequirementKey
                    "requiredByCohort", JBool item.RequiredByCohort
                    "nodeIds", JArray(item.NodeIds |> List.map JString)
                    "ownerCohortIds", JArray(item.OwnerCohortIds |> List.map JString)
                    "decisionIds", JArray(item.DecisionIds |> List.map JString)
                    "conflictIds", JArray(item.ConflictIds |> List.map JString)
                    "verificationObligationIds", JArray(item.VerificationObligationIds |> List.map JString) ] ] ]
    |> renderCanonical

// ---------------------------------------------------------------------------
// Manifest-free consumer admission (REL-ORDO-001 / ORD-REL-003, -005, -006).
//
// A downstream consumer (Praxis work admission, Dokimos authority) often holds
// only the published blueprint bytes and a digest it pinned earlier, not the
// trusted source manifest. `admitContract` applies every rule of `validate`
// that the blueprint can establish about itself: strict decoding, exact
// version negotiation, unique requirement keys and node IDs, resolvable and
// reciprocal requirement/decision/node references, resolvable acyclic
// dependencies and disposition evidence. Intake equality against the real
// source manifest still requires `prepare`; admission is not authority.
// ---------------------------------------------------------------------------

[<Literal>]
let ContractIdentitySchemaVersion = "ordo.constructibility-identity/1"

[<Literal>]
let StaleReferenceCode = "ORDO.CONTRACT.STALE_REFERENCE"

[<Literal>]
let InvalidReferenceDigestCode = "ORDO.CONTRACT.INVALID_REFERENCE_DIGEST"

/// The stable identity of one admitted contract. Every list is ordinal-sorted
/// and duplicate-free, so equal contracts yield byte-identical encodings.
type ContractIdentity =
    { ContractSchemaVersion: string
      ContractDigest: string
      RequirementKeys: string list
      DecisionIds: string list
      ConflictIds: string list }

/// The blueprint's own requirement sources stand in for the manifest, so only
/// manifest-independent rules can fire. The first occurrence of a repeated key
/// is kept, matching `validate`'s own indexing, so a duplicate is reported once
/// as DUPLICATE_BLUEPRINT_REQUIREMENT rather than as a changed requirement.
let private selfManifest (blueprint: Blueprint) : SourceManifest =
    { Digest = blueprint.SourceManifestDigest
      Requirements = blueprint.Requirements |> List.map _.Source |> List.distinctBy _.Key }

let private identityOf (blueprint: Blueprint) =
    let idsOf kind =
        blueprint.Nodes |> List.filter (fun node -> node.Kind = kind) |> List.map _.Id |> List.distinct |> List.sort
    { ContractSchemaVersion = blueprint.SchemaVersion
      ContractDigest = blueprintDigest blueprint
      RequirementKeys = blueprint.Requirements |> List.map _.Source.Key |> List.distinct |> List.sort
      DecisionIds = idsOf Decision
      ConflictIds = idsOf Conflict }

/// Strict, deterministic, side-effect-free admission of published contract
/// bytes. Diagnostics are the same stable `ORDO.CONTRACT.*` codes `prepare`
/// emits, sorted, in the `ordo.constructibility-diagnostics/1` envelope.
let admitContract (blueprintJson: string) : Result<ContractIdentity, DiagnosticSet> =
    match requestedVersion blueprintJson |> Option.map negotiateVersion with
    | Some(Unsupported(requested, supported)) -> Error(unsupportedSchema requested supported)
    | _ ->
        match readBlueprint blueprintJson with
        | Error message -> Error(failure [ diag "ORDO.CONTRACT.INVALID_BLUEPRINT_JSON" None message ])
        | Ok blueprint ->
            match validate (selfManifest blueprint) blueprint with
            | [] -> Ok(identityOf blueprint)
            | violations -> Error(failure (violations |> List.map violationDiagnostic))

let private isSha256Digest (value: string) =
    value.Length = 71
    && value.StartsWith("sha256:", System.StringComparison.Ordinal)
    && value.Substring 7 |> Seq.forall (fun ch -> (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'))

/// Stale-reference detection: a consumer that pinned `expectedDigest` learns
/// that the contract it now reads has a different canonical digest. The
/// subject is `expected->actual` so both sides of the drift are actionable.
let checkReference (expectedDigest: string) (identity: ContractIdentity) : Result<ContractIdentity, DiagnosticSet> =
    if not (isSha256Digest expectedDigest) then
        Error(failure [ diag InvalidReferenceDigestCode (Some expectedDigest)
                            "the expected contract digest must be a lowercase 64-hex SHA-256 value" ])
    elif not (System.String.Equals(expectedDigest, identity.ContractDigest, System.StringComparison.Ordinal)) then
        Error(failure [ diag StaleReferenceCode (Some(expectedDigest + "->" + identity.ContractDigest))
                            "the contract changed since this reference was pinned" ])
    else Ok identity

/// Admission followed, when a pin is held, by stale-reference detection.
let admitPinnedContract (expectedDigest: string option) (blueprintJson: string) =
    match admitContract blueprintJson, expectedDigest with
    | Ok identity, Some expected -> checkReference expected identity
    | outcome, _ -> outcome

let encodeContractIdentity (identity: ContractIdentity) =
    let strings values = JArray(values |> List.map JString)
    JObject [
        "schemaVersion", JString ContractIdentitySchemaVersion
        "contractSchemaVersion", JString identity.ContractSchemaVersion
        "contractDigest", JString identity.ContractDigest
        "requirementKeys", strings identity.RequirementKeys
        "decisionIds", strings identity.DecisionIds
        "conflictIds", strings identity.ConflictIds ]
    |> renderCanonical
