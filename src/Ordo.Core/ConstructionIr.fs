/// A lossless, provider-neutral construction handoff over an independently
/// pinned requirements manifest. Structural JSON validation is separate from
/// these semantic rules. No agent's claim about intake cardinality is trusted.
/// See docs/architecture/ecir-v1.md (ECIR-001).
module Ordo.Core.ConstructionIr

open System

/// Qualified identity prevents two unrelated source documents that both
/// contain e.g. "R-001" from collapsing into one requirement.
type SourceRequirement =
    { Key: string
      OriginalId: string
      Document: string
      Location: string
      Revision: string
      ContentDigest: string }

/// This is supplied by trusted intake, NOT taken from the blueprint output.
type SourceManifest =
    { Digest: string
      Requirements: SourceRequirement list }

type Disposition =
    | Modeled
    | Deferred of reason: string
    | Unresolved of reason: string
    | Rejected of reason: string
    | Superseded of targetKey: string * reason: string

type RequirementRecord =
    { Source: SourceRequirement
      Disposition: Disposition
      NodeIds: string list }

type NodeKind =
    | Decision
    | Contract
    | Invariant
    | Interface
    | Cohort
    | VerificationObligation
    | Conflict
    | Deferral
    | EngineeringRationale

type ConstructionNode =
    { Id: string
      Kind: NodeKind
      RequirementKeys: string list
      DependsOn: string list
      Justification: string option }

type Blueprint =
    { SchemaVersion: string
      SourceManifestDigest: string
      Requirements: RequirementRecord list
      Nodes: ConstructionNode list }

/// Failure is data with a stable, inspectable failure category, never a
/// boolean "valid" that hides the missing requirement or reference.
type Violation =
    | UnsupportedSchema of actual: string
    | EmptyIntake
    | InvalidField of owner: string * field: string
    | SourceDigestMismatch
    | DuplicateManifestRequirement of key: string
    | DuplicateBlueprintRequirement of key: string
    | MissingRequirement of key: string
    | InventedRequirement of key: string
    | ChangedRequirement of key: string
    | DuplicateNode of id: string
    | MissingRequirementMapping of key: string
    | MissingNode of source: string * nodeId: string
    | MissingRequirementReference of nodeId: string * key: string
    | NonReciprocalLink of requirementKey: string * nodeId: string
    | DuplicateLink of source: string * target: string
    | UnjustifiedNode of nodeId: string
    | MissingDispositionReason of requirementKey: string
    | MissingDispositionNode of requirementKey: string * requiredKind: NodeKind
    | InvalidSupersession of requirementKey: string * target: string
    | MissingVerification of requirementKey: string
    | MissingDependency of nodeId: string * target: string
    | SelfDependency of nodeId: string
    | DependencyCycle of nodeId: string

type ReadinessViolation =
    | InvalidBlueprint of Violation
    | MissingCohort of cohortId: string
    | NotACohort of nodeId: string
    | BlockedRequirement of key: string
    | MissingDecisionAuthorization of nodeId: string

[<Literal>]
let SchemaVersion = "ecir/1"

let private isBlank (value: string) = String.IsNullOrWhiteSpace value

let private duplicateValues (values: string list) =
    values
    |> List.groupBy id
    |> List.choose (fun (key, values) -> if List.length values > 1 then Some key else None)

let private firstBy (key: 'a -> string) (items: 'a list) =
    items
    |> List.fold (fun result value ->
        let id = key value
        if Map.containsKey id result then result else Map.add id value result) Map.empty

let private invalidSourceFields prefix (source: SourceRequirement) =
    [ "key", source.Key
      "originalId", source.OriginalId
      "document", source.Document
      "location", source.Location
      "revision", source.Revision
      "contentDigest", source.ContentDigest ]
    |> List.choose (fun (field, value) ->
        if isBlank value then Some(InvalidField(prefix, field)) else None)

/// Full intake equality and reciprocal traceability. This deliberately does
/// not claim that a modeled requirement is implemented or that tests passed.
let validate (manifest: SourceManifest) (blueprint: Blueprint) : Violation list =
    let errors = ResizeArray<Violation>()
    let add error = errors.Add error

    if blueprint.SchemaVersion <> SchemaVersion then
        add (UnsupportedSchema blueprint.SchemaVersion)

    if List.isEmpty manifest.Requirements then add EmptyIntake

    if isBlank manifest.Digest then add (InvalidField("manifest", "digest"))
    if isBlank blueprint.SourceManifestDigest then add (InvalidField("blueprint", "sourceManifestDigest"))

    if not (String.Equals(manifest.Digest, blueprint.SourceManifestDigest, StringComparison.Ordinal)) then
        add SourceDigestMismatch

    manifest.Requirements
    |> List.iter (fun r -> invalidSourceFields ("manifest:" + r.Key) r |> List.iter add)

    blueprint.Requirements
    |> List.iter (fun r -> invalidSourceFields ("blueprint:" + r.Source.Key) r.Source |> List.iter add)

    let originals = manifest.Requirements |> List.map (fun r -> r.Key)
    let modeled = blueprint.Requirements |> List.map (fun r -> r.Source.Key)

    originals |> duplicateValues |> List.iter (DuplicateManifestRequirement >> add)
    modeled |> duplicateValues |> List.iter (DuplicateBlueprintRequirement >> add)

    let originalIndex = firstBy (fun (r: SourceRequirement) -> r.Key) manifest.Requirements
    let modeledIndex = firstBy (fun (r: RequirementRecord) -> r.Source.Key) blueprint.Requirements
    let nodeIndex = firstBy (fun (n: ConstructionNode) -> n.Id) blueprint.Nodes

    for item in manifest.Requirements do
        match Map.tryFind item.Key modeledIndex with
        | None -> add (MissingRequirement item.Key)
        | Some mapped when mapped.Source <> item -> add (ChangedRequirement item.Key)
        | Some _ -> ()

    for item in blueprint.Requirements do
        if not (Map.containsKey item.Source.Key originalIndex) then
            add (InventedRequirement item.Source.Key)

    blueprint.Nodes
    |> List.map (fun n -> n.Id)
    |> duplicateValues
    |> List.iter (DuplicateNode >> add)

    for requirement in blueprint.Requirements do
        let key = requirement.Source.Key

        if List.isEmpty requirement.NodeIds then
            add (MissingRequirementMapping key)

        requirement.NodeIds
        |> duplicateValues
        |> List.iter (fun id -> add (DuplicateLink(key, id)))

        for nodeId in requirement.NodeIds do
            match Map.tryFind nodeId nodeIndex with
            | None -> add (MissingNode(key, nodeId))
            | Some node when not (List.contains key node.RequirementKeys) ->
                add (NonReciprocalLink(key, nodeId))
            | Some _ -> ()

        let hasKind kind =
            requirement.NodeIds
            |> List.exists (fun id ->
                match Map.tryFind id nodeIndex with
                | Some node -> node.Kind = kind && List.contains key node.RequirementKeys
                | None -> false)

        match requirement.Disposition with
        | Modeled ->
            if not (hasKind VerificationObligation) then add (MissingVerification key)
        | Deferred reason ->
            if isBlank reason then add (MissingDispositionReason key)
            if not (hasKind Deferral) then add (MissingDispositionNode(key, Deferral))
        | Unresolved reason ->
            if isBlank reason then add (MissingDispositionReason key)
            if not (hasKind Conflict) then add (MissingDispositionNode(key, Conflict))
        | Rejected reason ->
            if isBlank reason then add (MissingDispositionReason key)
            if not (hasKind EngineeringRationale) then
                add (MissingDispositionNode(key, EngineeringRationale))
        | Superseded(target, reason) ->
            if isBlank reason then add (MissingDispositionReason key)
            if key = target || not (Map.containsKey target originalIndex) then
                add (InvalidSupersession(key, target))

    for node in blueprint.Nodes do
        if isBlank node.Id then add (InvalidField("node", "id"))

        node.RequirementKeys
        |> duplicateValues
        |> List.iter (fun key -> add (DuplicateLink(node.Id, key)))

        node.DependsOn
        |> duplicateValues
        |> List.iter (fun id -> add (DuplicateLink(node.Id, id)))

        if List.isEmpty node.RequirementKeys then
            // An engineering rationale can exist without an originating
            // requirement, but cannot grant itself approval or authorize
            // an unrelated contract. External approval remains mandatory.
            if node.Kind <> EngineeringRationale
               || (node.Justification |> Option.forall isBlank) then
                add (UnjustifiedNode node.Id)

        for key in node.RequirementKeys do
            match Map.tryFind key modeledIndex with
            | None -> add (MissingRequirementReference(node.Id, key))
            | Some requirement when not (List.contains node.Id requirement.NodeIds) ->
                add (NonReciprocalLink(key, node.Id))
            | Some _ -> ()

        for dependency in node.DependsOn do
            if dependency = node.Id then add (SelfDependency node.Id)
            elif not (Map.containsKey dependency nodeIndex) then
                add (MissingDependency(node.Id, dependency))

    let rec reachesCycle (visited: Set<string>) (nodeId: string) =
        if Set.contains nodeId visited then true
        else
            match Map.tryFind nodeId nodeIndex with
            | None -> false
            | Some node ->
                node.DependsOn
                |> List.exists (reachesCycle (Set.add nodeId visited))

    for node in blueprint.Nodes do
        if reachesCycle Set.empty node.Id then add (DependencyCycle node.Id)

    errors
    |> Seq.distinct
    |> Seq.sortBy (sprintf "%A")
    |> Seq.toList

/// Execution readiness checks ONLY one cohort after whole-artifact semantic
/// validation. The caller must obtain authorizedDecisionIds from an external,
/// verified authority, not from an agent-authored "approved" field.
let validateCohort
    (manifest: SourceManifest)
    (blueprint: Blueprint)
    (cohortId: string)
    (authorizedDecisionIds: Set<string>)
    : ReadinessViolation list =

    match validate manifest blueprint with
    | structural when not (List.isEmpty structural) ->
        structural |> List.map InvalidBlueprint
    | _ ->
        match blueprint.Nodes |> List.tryFind (fun n -> n.Id = cohortId) with
        | None -> [ MissingCohort cohortId ]
        | Some node when node.Kind <> Cohort -> [ NotACohort cohortId ]
        | Some node ->
            let requirements =
                blueprint.Requirements
                |> List.filter (fun r -> List.contains r.Source.Key node.RequirementKeys)

            let blocked =
                requirements
                |> List.choose (fun r ->
                    match r.Disposition with
                    | Modeled -> None
                    | _ -> Some (BlockedRequirement r.Source.Key))

            let missingDecisions =
                blueprint.Nodes
                |> List.filter (fun n ->
                    n.Kind = Decision
                    && n.RequirementKeys |> List.exists (fun key -> List.contains key node.RequirementKeys)
                    && not (Set.contains n.Id authorizedDecisionIds))
                |> List.map (fun n -> MissingDecisionAuthorization n.Id)

            blocked @ missingDecisions
            |> List.distinct
            |> List.sortBy (sprintf "%A")
