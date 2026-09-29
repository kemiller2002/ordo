/// Execution roles as capability-bearing state (ORD-EXEC-010 / ORD-EXEC-080).
///
/// A role is not a prompt label. It names a default authority — a set of
/// typed execution capabilities granted and a set explicitly prohibited — and
/// an execution's effective authority is always computed from that typed
/// state. Provider, model and runtime never appear here: a human, Claude,
/// Codex, Gemini or a CI runner performs the same role when it holds the same
/// capabilities (ORD-EXEC-075 / ORD-EXEC-076).
module Ordo.Core.ExecutionRole

/// The semantic role an execution performs.
///
/// `Specialized` exists so that a project can name a future role without
/// Ordo inventing its authority: a specialized role starts with no
/// capabilities and gains only what a governed policy grants (ORD-EXEC-011).
type ExecutionRole =
    | Specification
    | Implementation
    | Verification
    | Review
    | Integration
    | Administration
    | Specialized of name: string

[<RequireQualifiedAccess>]
module ExecutionRole =

    let toWire role =
        match role with
        | Specification -> "specification"
        | Implementation -> "implementation"
        | Verification -> "verification"
        | Review -> "review"
        | Integration -> "integration"
        | Administration -> "administration"
        | Specialized name -> "specialized:" + name

    let fromWire (raw: string) =
        match raw with
        | "specification" -> Some Specification
        | "implementation" -> Some Implementation
        | "verification" -> Some Verification
        | "review" -> Some Review
        | "integration" -> Some Integration
        | "administration" -> Some Administration
        | s when s.StartsWith("specialized:", System.StringComparison.Ordinal) && s.Length > 12 ->
            Some(Specialized(s.Substring 12))
        | _ -> None

/// What an execution may do, as a closed vocabulary rather than free text,
/// so that a check against it is exhaustive and a typo cannot grant
/// anything.
///
/// These are semantic authorities, not credentials (see `Capability.fs`):
/// holding one says what Ordo will treat as legal, not what the host can
/// physically prevent.
type ExecutionCapability =
    // Specification
    | ElaborateAuthorizedScope
    | CreateGoverningPromise
    | ApproveSpecification
    // Implementation
    | ModifyImplementation
    | ModifyImplementationTests
    // Acceptance and evaluation authority
    | ModifyAcceptanceCriteria
    | ModifyEvaluationAuthority
    // Verification
    | InvokeEvaluator
    | ObserveOutcome
    | RecordEvidence
    | RecordVerdict
    // Review
    | InspectEvidence
    | RecordFindings
    | RequestRework
    | AcceptReview
    | RejectReview
    // Integration
    | CombineAuthorizedCandidates
    | ResolveIntegrationConflict
    // Scope
    | ExpandMutationBoundary
    // Administration
    | RegisterInstallation
    | RemoveInstallation
    | AdministerExecutionPolicy

[<RequireQualifiedAccess>]
module ExecutionCapability =

    let all =
        [ ElaborateAuthorizedScope
          CreateGoverningPromise
          ApproveSpecification
          ModifyImplementation
          ModifyImplementationTests
          ModifyAcceptanceCriteria
          ModifyEvaluationAuthority
          InvokeEvaluator
          ObserveOutcome
          RecordEvidence
          RecordVerdict
          InspectEvidence
          RecordFindings
          RequestRework
          AcceptReview
          RejectReview
          CombineAuthorizedCandidates
          ResolveIntegrationConflict
          ExpandMutationBoundary
          RegisterInstallation
          RemoveInstallation
          AdministerExecutionPolicy ]

    let toWire capability =
        match capability with
        | ElaborateAuthorizedScope -> "specification.elaborate"
        | CreateGoverningPromise -> "specification.create-promise"
        | ApproveSpecification -> "specification.approve"
        | ModifyImplementation -> "implementation.modify"
        | ModifyImplementationTests -> "implementation.modify-tests"
        | ModifyAcceptanceCriteria -> "acceptance.modify"
        | ModifyEvaluationAuthority -> "evaluator.modify"
        | InvokeEvaluator -> "evaluator.invoke"
        | ObserveOutcome -> "verification.observe"
        | RecordEvidence -> "evidence.record"
        | RecordVerdict -> "verification.verdict"
        | InspectEvidence -> "review.inspect"
        | RecordFindings -> "review.findings"
        | RequestRework -> "review.request-rework"
        | AcceptReview -> "review.accept"
        | RejectReview -> "review.reject"
        | CombineAuthorizedCandidates -> "integration.combine"
        | ResolveIntegrationConflict -> "integration.resolve-conflict"
        | ExpandMutationBoundary -> "scope.expand"
        | RegisterInstallation -> "installation.register"
        | RemoveInstallation -> "installation.remove"
        | AdministerExecutionPolicy -> "policy.administer"

    let fromWire (raw: string) = all |> List.tryFind (fun c -> toWire c = raw)

/// A role's authority: what it is granted and what it is prohibited.
///
/// Prohibitions are kept rather than being implied by absence so that a
/// project's widening of a role can be checked against what the role is
/// meant never to do (ORD-EXEC-012..015).
type RoleAuthority =
    { Role: ExecutionRole
      Grants: Set<ExecutionCapability>
      Prohibits: Set<ExecutionCapability> }

/// A project's governed change to a role's default authority.
///
/// Narrowing needs no justification. Widening must name the policy decision
/// that authorized it, so that it is an explicit governed act and never an
/// unrecorded prompt edit (ORD-EXEC-086).
type RoleAdjustment =
    | Narrow of removed: Set<ExecutionCapability>
    | Widen of added: Set<ExecutionCapability> * policyDecision: string

type RoleAdjustmentError =
    | WideningWithoutPolicyDecision
    /// Widening into a capability the role prohibits is refused outright:
    /// that would turn, for example, verification into implementation.
    | WideningIntoProhibition of ExecutionCapability list

[<RequireQualifiedAccess>]
module RoleAuthority =

    let private ofLists role grants prohibits =
        { Role = role
          Grants = Set.ofList grants
          Prohibits = Set.ofList prohibits }

    /// The authority-altering capabilities no execution role holds by
    /// default. Changing acceptance or evaluation always needs a separate,
    /// explicitly governed execution (ORD-EXEC-003 / ORD-EXEC-051).
    let private evaluationAuthority = [ ModifyAcceptanceCriteria; ModifyEvaluationAuthority ]

    /// The default authority matrix (ORD-EXEC-081..085).
    let defaultFor role =
        match role with
        | Specification ->
            ofLists
                role
                [ ElaborateAuthorizedScope; InspectEvidence; RecordEvidence ]
                ([ CreateGoverningPromise; ApproveSpecification; ModifyImplementation; ModifyImplementationTests ]
                 @ evaluationAuthority)
        | Implementation ->
            ofLists
                role
                [ ModifyImplementation; ModifyImplementationTests; InvokeEvaluator; ObserveOutcome; RecordEvidence ]
                ([ CreateGoverningPromise; ApproveSpecification; RecordVerdict; AcceptReview ]
                 @ evaluationAuthority)
        | Verification ->
            ofLists
                role
                [ InvokeEvaluator; ObserveOutcome; RecordEvidence; RecordVerdict; InspectEvidence ]
                ([ ModifyImplementation; ModifyImplementationTests; ExpandMutationBoundary ] @ evaluationAuthority)
        | Review ->
            ofLists
                role
                [ InspectEvidence; RecordFindings; RequestRework; AcceptReview; RejectReview; RecordEvidence ]
                ([ ModifyImplementation; ModifyImplementationTests; ExpandMutationBoundary ] @ evaluationAuthority)
        | Integration ->
            ofLists
                role
                [ CombineAuthorizedCandidates
                  ResolveIntegrationConflict
                  InvokeEvaluator
                  ObserveOutcome
                  RecordEvidence ]
                ([ CreateGoverningPromise; ExpandMutationBoundary ] @ evaluationAuthority)
        | Administration ->
            ofLists
                role
                [ RegisterInstallation; RemoveInstallation; RecordEvidence; InspectEvidence ]
                ([ ModifyImplementation; ModifyImplementationTests; RecordVerdict ] @ evaluationAuthority)
        | Specialized _ -> ofLists role [] []

    /// What the role may actually exercise: grants minus prohibitions. A
    /// capability that is both granted and prohibited is not held.
    let effective (authority: RoleAuthority) = Set.difference authority.Grants authority.Prohibits

    let allows capability (authority: RoleAuthority) = effective authority |> Set.contains capability

    /// Apply a project's governed adjustment.
    let adjust (adjustment: RoleAdjustment) (authority: RoleAuthority) : Result<RoleAuthority, RoleAdjustmentError> =
        match adjustment with
        | Narrow removed -> Ok { authority with Grants = Set.difference authority.Grants removed }
        | Widen(_, decision) when System.String.IsNullOrWhiteSpace decision -> Error WideningWithoutPolicyDecision
        | Widen(added, _) ->
            let conflicts = Set.intersect added authority.Prohibits

            if Set.isEmpty conflicts then
                Ok { authority with Grants = Set.union authority.Grants added }
            else
                Error(WideningIntoProhibition(Set.toList conflicts))
