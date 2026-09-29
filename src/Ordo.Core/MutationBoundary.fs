/// Semantic mutation boundaries, their physical projections, and the
/// strength of whatever actually contains an execution
/// (ORD-EXEC-030..033, ORD-EXEC-060..061, ORD-EXEC-120..126).
///
/// A boundary is declared in semantic terms — features, responsibility
/// clusters, authorities, capabilities. A host may project it onto physical
/// resources (paths, packages, repositories) for observation, and every
/// projection names the semantic scope that justified it. A mutation that
/// no projection admits becomes an explicit scope effect; an actor's
/// explanation does not widen anything. Only a legal scope expansion does.
module Ordo.Core.MutationBoundary

open System

/// The semantic unit a boundary authorizes (ORD-EXEC-120).
type SemanticScope =
    | Feature of id: string
    | ResponsibilityCluster of id: string
    | Authority of id: string
    | CapabilityScope of id: string

[<RequireQualifiedAccess>]
module SemanticScope =

    let toWire scope =
        match scope with
        | Feature id -> "feature:" + id
        | ResponsibilityCluster id -> "cluster:" + id
        | Authority id -> "authority:" + id
        | CapabilityScope id -> "capability:" + id

    let fromWire (raw: string) =
        match raw.Split(':', 2) with
        | [| "feature"; id |] when id <> "" -> Some(Feature id)
        | [| "cluster"; id |] when id <> "" -> Some(ResponsibilityCluster id)
        | [| "authority"; id |] when id <> "" -> Some(Authority id)
        | [| "capability"; id |] when id <> "" -> Some(CapabilityScope id)
        | _ -> None

/// A host's physical projection of one semantic scope (ORD-EXEC-121/122).
/// Patterns are slash-separated globs: `*` matches within a segment, `**`
/// matches any number of segments.
type PhysicalProjection =
    { Scope: SemanticScope
      Patterns: string list }

/// The declared boundary of a bounded mutating execution.
type MutationBoundary =
    { Scopes: SemanticScope list
      Projections: PhysicalProjection list
      /// Evaluation-authority references. Always outside the writable set,
      /// even when a projection pattern would admit them (ORD-EXEC-126).
      EvaluatorReferences: string list }

/// Where one observed mutation falls.
type MutationClassification =
    | WithinBoundary of justifiedBy: SemanticScope
    | OutsideBoundary
    | EvaluatorAuthorityMutation

/// An observed mutation outside the effective boundary. It blocks completion
/// until reconciled (ORD-EXEC-031 / ORD-EXEC-123).
type ScopeEffect =
    { Resource: string
      Classification: MutationClassification
      /// What the actor said about it. Recorded; it changes nothing
      /// (ORD-EXEC-124).
      Explanation: string option }

/// How a scope effect was settled.
type ScopeEffectResolution =
    /// The mutation was reverted and the revert observed.
    | Reverted of evidence: string
    /// A legal scope expansion now admits it.
    | AdmittedByExpansion of expansionId: string
    /// Moved to its own governed execution.
    | TransferredToExecution of executionId: string

/// A legal scope expansion (ORD-EXEC-125). It is a recorded transition with
/// its own authorizing actor and justification, not a side effect of an
/// explanation.
type ScopeExpansion =
    { ExpansionId: string
      AddedScopes: SemanticScope list
      AddedProjections: PhysicalProjection list
      Justification: string
      AuthorizedBy: string }

type ScopeExpansionError =
    | ExpansionWithoutJustification
    | ExpansionWithoutAuthorizer
    | ExpansionIntoEvaluatorAuthority of references: string list
    | ProjectionWithoutScope of SemanticScope

/// Glob matching without a regular-expression dependency: `*` and `?` match
/// within a segment, `**` matches any number of whole segments.
[<RequireQualifiedAccess>]
module Glob =

    let rec private segmentMatches (pattern: char list) (text: char list) =
        match pattern, text with
        | [], [] -> true
        | '*' :: rest, _ -> segmentMatches rest text || (not (List.isEmpty text) && segmentMatches pattern (List.tail text))
        | '?' :: rest, _ :: tail -> segmentMatches rest tail
        | p :: rest, c :: tail when p = c -> segmentMatches rest tail
        | _ -> false

    let rec private pathMatches (pattern: string list) (path: string list) =
        match pattern, path with
        | [], [] -> true
        | [ "**" ], _ -> true
        | "**" :: rest, _ -> pathMatches rest path || (not (List.isEmpty path) && pathMatches pattern (List.tail path))
        | p :: rest, segment :: tail -> segmentMatches (List.ofSeq p) (List.ofSeq segment) && pathMatches rest tail
        | _ -> false

    let private split (value: string) =
        value.Replace('\\', '/').Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

    let isMatch (pattern: string) (path: string) = pathMatches (split pattern) (split path)

[<RequireQualifiedAccess>]
module MutationBoundary =

    let empty =
        { Scopes = []
          Projections = []
          EvaluatorReferences = [] }

    let private matchesEvaluator (boundary: MutationBoundary) (resource: string) =
        boundary.EvaluatorReferences
        |> List.exists (fun r -> r = resource || Glob.isMatch r resource)

    /// Classify one observed mutated resource.
    let classify (boundary: MutationBoundary) (resource: string) : MutationClassification =
        if matchesEvaluator boundary resource then
            EvaluatorAuthorityMutation
        else
            boundary.Projections
            |> List.tryFind (fun p ->
                List.contains p.Scope boundary.Scopes
                && p.Patterns |> List.exists (fun pattern -> Glob.isMatch pattern resource))
            |> Option.map (fun p -> WithinBoundary p.Scope)
            |> Option.defaultValue OutsideBoundary

    /// Every observed mutation that needs reconciliation before completion.
    let scopeEffects (boundary: MutationBoundary) (mutated: (string * string option) list) : ScopeEffect list =
        mutated
        |> List.choose (fun (resource, explanation) ->
            match classify boundary resource with
            | WithinBoundary _ -> None
            | other ->
                Some
                    { Resource = resource
                      Classification = other
                      Explanation = explanation })

    /// Apply a legal scope expansion. Refuses anything that would make
    /// evaluator authority writable, and any projection that is not traceable
    /// to a scope the boundary will hold.
    let expand (expansion: ScopeExpansion) (boundary: MutationBoundary) : Result<MutationBoundary, ScopeExpansionError> =
        let scopes = boundary.Scopes @ expansion.AddedScopes |> List.distinct

        let untraceable =
            expansion.AddedProjections |> List.tryFind (fun p -> not (List.contains p.Scope scopes))

        let evaluatorHits =
            boundary.EvaluatorReferences
            |> List.filter (fun r ->
                expansion.AddedProjections
                |> List.exists (fun p -> p.Patterns |> List.exists (fun pattern -> Glob.isMatch pattern r)))

        if String.IsNullOrWhiteSpace expansion.Justification then
            Error ExpansionWithoutJustification
        elif String.IsNullOrWhiteSpace expansion.AuthorizedBy then
            Error ExpansionWithoutAuthorizer
        elif not (List.isEmpty evaluatorHits) then
            Error(ExpansionIntoEvaluatorAuthority evaluatorHits)
        else
            match untraceable with
            | Some p -> Error(ProjectionWithoutScope p.Scope)
            | None ->
                Ok
                    { boundary with
                        Scopes = scopes
                        Projections = boundary.Projections @ expansion.AddedProjections }

/// What an isolation mechanism is (ORD-EXEC-032).
type IsolationMechanism =
    | GitWorktree
    | GitBranch
    | WorkingDirectory
    | RemoteCheckout
    | Container
    | VirtualMachine
    | NoIsolation

/// What host enforcement was actually evidenced (ORD-EXEC-060).
type EnforcedRestriction =
    | FilesystemRestriction
    | ProcessRestriction
    | NetworkRestriction
    | CredentialRestriction
    | SystemCallRestriction

/// How strongly an execution is contained (ORD-EXEC-033 / ORD-EXEC-061).
///
/// `HostEnforced` can only be constructed with evidence. Nothing — least of
/// all a worktree — implies it.
type ContainmentStrength =
    | ContainmentUnknown
    | SemanticOnly of mechanism: IsolationMechanism
    | HostEnforced of mechanism: IsolationMechanism * restrictions: EnforcedRestriction list * evidence: string list

[<RequireQualifiedAccess>]
module Containment =

    /// The containment an isolation mechanism provides by itself: never more
    /// than semantic.
    let ofMechanism mechanism = SemanticOnly mechanism

    /// Attach host-enforcement evidence. Without evidence and at least one
    /// enforced restriction the strength is unchanged.
    let withEnforcement restrictions (evidence: string list) strength =
        let mechanism =
            match strength with
            | ContainmentUnknown -> NoIsolation
            | SemanticOnly m
            | HostEnforced(m, _, _) -> m

        match restrictions, evidence |> List.filter (String.IsNullOrWhiteSpace >> not) with
        | [], _
        | _, [] -> strength
        | rs, ev -> HostEnforced(mechanism, rs, ev)

    let isSecuritySandbox strength =
        match strength with
        | HostEnforced _ -> true
        | ContainmentUnknown
        | SemanticOnly _ -> false

    let toWire strength =
        match strength with
        | ContainmentUnknown -> "unknown"
        | SemanticOnly _ -> "semantic-only"
        | HostEnforced _ -> "host-enforced"
