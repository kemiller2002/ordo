/// Execution-governance semantics (ORD-EXEC-001..144), tested where their
/// rules bite. Invariants are checked with exhaustive loops over the closed
/// vocabularies rather than a property-testing dependency (ORDO-10002).
module Ordo.Tests.ExecutionGovernanceTests

open System
open Xunit
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.ExternalEffect
open Ordo.Core.ExecutionRole
open Ordo.Core.Evaluator
open Ordo.Core.Receipt
open Ordo.Core.MutationBoundary
open Ordo.Core.Authorization
open Ordo.Core.Execution
open Ordo.Core.ExecutionWire

let private ok result =
    match result with
    | Ok value -> value
    | Error error -> failwithf "expected Ok, got %A" error

let private t0 = DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero)

let private host = { Source = HostObserved "praxis"; Facts = []; Narrative = None }

let private observed facts = { host with Facts = facts }

let private actor kind =
    { Id = ok (ActorId.create ("actor-" + ActorKind.toWire kind))
      Kind = kind
      Provider = (if kind = Agent then Some "anthropic" else None)
      Model = None
      Runtime = (if kind = Agent then Some "claude-code" else None) }

let private allExpectedKinds =
    [ ArtifactExists "a"
      ArtifactIdentity("a", "sha256:1")
      ArtifactAbsent "a"
      CommandSucceeded "dotnet test"
      StateEquals("k", "v")
      ConformsToContract("a", "schema")
      VerificationSatisfied "sha256:e"
      TransitionObserved "work.complete"
      AllOf [ ArtifactExists "a"; CommandSucceeded "c" ] ]

// ---------------------------------------------------------------- receipts

[<Fact>]
let ``a receipt matches when the observed fact satisfies the postcondition`` () =
    let result = Receipt.compare (ArtifactIdentity("out.json", "sha256:aa")) (observed [ ArtifactObserved("out.json", Some "sha256:aa") ])
    Assert.Equal(Matched, Receipt.outcome result)
    Assert.True(Receipt.permitsAdvancement result)

[<Fact>]
let ``a receipt mismatches when the observed fact contradicts it and keeps the evidence`` () =
    let fact = ArtifactObserved("out.json", Some "sha256:bb")
    let result = Receipt.compare (ArtifactIdentity("out.json", "sha256:aa")) (observed [ fact ])

    match result with
    | Mismatch(_, _, because) -> Assert.Equal<ObservedFact list>([ fact ], because)
    | other -> failwithf "expected mismatch, got %A" other

    Assert.False(Receipt.permitsAdvancement result)

[<Fact>]
let ``no evidence is indeterminate for every receipt kind, never match or mismatch`` () =
    for expected in allExpectedKinds do
        let result = Receipt.compare expected host
        Assert.Equal(Undetermined, Receipt.outcome result)

[<Fact>]
let ``an unknown command outcome is indeterminate, not failure`` () =
    let result =
        Receipt.compare (CommandSucceeded "publish") (observed [ CommandOutcomeUnknown("publish", "connection reset") ])

    Assert.Equal(Undetermined, Receipt.outcome result)

[<Fact>]
let ``a composite partial match preserves each constituent and is not satisfaction`` () =
    let expected = AllOf [ ArtifactExists "a"; ArtifactExists "b"; CommandSucceeded "c" ]
    let result = Receipt.compare expected (observed [ ArtifactObserved("a", None); CommandExited("c", 0) ])

    match result with
    | Composite(_, parts) ->
        Assert.Equal(3, parts.Length)
        Assert.Equal<ReceiptOutcome list>([ Matched; Undetermined; Matched ], parts |> List.map Receipt.outcome)
    | other -> failwithf "expected composite, got %A" other

    Assert.Equal(Undetermined, Receipt.outcome result)

    let withMismatch = Receipt.compare expected (observed [ ArtifactObserved("a", None); ArtifactNotFound "b"; CommandExited("c", 0) ])
    Assert.Equal(Mismatched, Receipt.outcome withMismatch)

[<Fact>]
let ``a self-reported claim can never produce a match but its reported failure is believed`` () =
    let self = { Source = SelfReported "agent"; Facts = [ CommandExited("test", 0) ]; Narrative = Some "all green" }
    Assert.Equal(Undetermined, Receipt.outcome (Receipt.compare (CommandSucceeded "test") self))

    let failed = { self with Facts = [ CommandExited("test", 1) ] }
    Assert.Equal(Mismatched, Receipt.outcome (Receipt.compare (CommandSucceeded "test") failed))

[<Fact>]
let ``narrative never participates in comparison`` () =
    for expected in allExpectedKinds do
        let quiet = Receipt.compare expected host
        let loud = Receipt.compare expected { host with Narrative = Some "done, trust me" }
        Assert.Equal(Receipt.outcome quiet, Receipt.outcome loud)

[<Fact>]
let ``a verdict under a different evaluator is indeterminate for this evaluator`` () =
    let result = Receipt.compare (VerificationSatisfied "sha256:new") (observed [ VerificationObserved("sha256:old", true) ])
    Assert.Equal(Undetermined, Receipt.outcome result)

// ------------------------------------------------------ evaluator identity

let private closure =
    [ { Kind = GateCode; Reference = "tests/gate.fs"; Digest = "d1" }
      { Kind = Configuration; Reference = "ros.json"; Digest = "c1" }
      { Kind = TestSelection; Reference = "tests/select.txt"; Digest = "s1" }
      { Kind = GeneratedInput; Reference = "generated/fixtures.json"; Digest = "g1" } ]

let private replace reference digest =
    closure |> List.map (fun i -> if i.Reference = reference then { i with Digest = digest } else i)

[<Fact>]
let ``an unchanged evaluator yields an ordinary verdict`` () =
    let baseline = ok (EvaluatorIdentity.create closure)
    let again = ok (EvaluatorIdentity.create (List.rev closure))
    Assert.Equal(baseline.Fingerprint, again.Fingerprint)

    match Evaluation.judge baseline (Ok again) VerdictPassed with
    | Passed fp -> Assert.Equal(baseline.Fingerprint, fp)
    | other -> failwithf "expected pass, got %A" other

[<Fact>]
let ``a direct, indirect or generated evaluator change is neither pass nor fail`` () =
    let baseline = ok (EvaluatorIdentity.create closure)

    for changed in [ "tests/gate.fs"; "ros.json"; "generated/fixtures.json"; "tests/select.txt" ] do
        let current = ok (EvaluatorIdentity.create (replace changed "different"))

        for verdict in [ VerdictPassed; VerdictFailed "boom" ] do
            match Evaluation.judge baseline (Ok current) verdict with
            | EvaluatorChanged(b, c, changes) ->
                Assert.NotEqual<string>(b, c)
                Assert.Equal(1, changes.Length)
            | other -> failwithf "expected changed-evaluator for %s, got %A" changed other

[<Fact>]
let ``a stale verdict is invalidated when the evaluator changes`` () =
    let baseline = ok (EvaluatorIdentity.create closure)
    let verdict = Evaluation.judge baseline (Ok baseline) VerdictPassed
    Assert.True(Evaluation.permitsAcceptance baseline verdict)

    let repaired = ok (EvaluatorIdentity.create (replace "ros.json" "c2"))
    Assert.False(Evaluation.isCurrent repaired verdict)
    Assert.False(Evaluation.permitsAcceptance repaired verdict)

[<Fact>]
let ``a missing evaluator blocks rather than being substituted`` () =
    let baseline = ok (EvaluatorIdentity.create closure)

    match Evaluation.judge baseline (Error "gate script missing") VerdictPassed with
    | EvaluatorUnavailable _ -> ()
    | other -> failwithf "expected unavailable, got %A" other

[<Fact>]
let ``an evaluator identity needs gate code and digests`` () =
    Assert.Equal(Error NoEvaluatorInputs, EvaluatorIdentity.create [] |> Result.map (fun i -> i.Fingerprint))

    Assert.Equal(
        Error NoGateCode,
        EvaluatorIdentity.create [ { Kind = Schema; Reference = "s"; Digest = "x" } ] |> Result.map (fun i -> i.Fingerprint)
    )

    Assert.Equal(
        Error(InputWithoutDigest "g"),
        EvaluatorIdentity.create [ { Kind = GateCode; Reference = "g"; Digest = " " } ] |> Result.map (fun i -> i.Fingerprint)
    )

// ------------------------------------------------------------------- roles

let private defaultRoles = [ Specification; Implementation; Verification; Review; Integration; Administration ]

[<Fact>]
let ``no default role holds acceptance or evaluator authority`` () =
    for role in defaultRoles do
        let authority = RoleAuthority.defaultFor role
        Assert.False(RoleAuthority.allows ModifyEvaluationAuthority authority, ExecutionRole.toWire role)
        Assert.False(RoleAuthority.allows ModifyAcceptanceCriteria authority, ExecutionRole.toWire role)

[<Fact>]
let ``default role boundaries hold`` () =
    let v = RoleAuthority.defaultFor Verification
    Assert.True(RoleAuthority.allows InvokeEvaluator v)
    Assert.False(RoleAuthority.allows ModifyImplementation v)
    Assert.False(RoleAuthority.allows ModifyImplementationTests v)

    let i = RoleAuthority.defaultFor Implementation
    Assert.True(RoleAuthority.allows ModifyImplementation i)
    Assert.False(RoleAuthority.allows RecordVerdict i)

    let r = RoleAuthority.defaultFor Review
    Assert.True(RoleAuthority.allows RequestRework r)
    Assert.False(RoleAuthority.allows ModifyImplementation r)

    let s = RoleAuthority.defaultFor Specification
    Assert.True(RoleAuthority.allows ElaborateAuthorizedScope s)
    Assert.False(RoleAuthority.allows CreateGoverningPromise s)
    Assert.False(RoleAuthority.allows ApproveSpecification s)

    let g = RoleAuthority.defaultFor Integration
    Assert.True(RoleAuthority.allows CombineAuthorizedCandidates g)
    Assert.False(RoleAuthority.allows ModifyAcceptanceCriteria g)

    Assert.True(Set.isEmpty (RoleAuthority.effective (RoleAuthority.defaultFor(Specialized "security-audit"))))

[<Fact>]
let ``widening a role is a governed decision and cannot cross a prohibition`` () =
    let v = RoleAuthority.defaultFor Verification
    Assert.Equal(Error WideningWithoutPolicyDecision, RoleAuthority.adjust (Widen(Set.ofList [ RecordFindings ], "")) v)

    match RoleAuthority.adjust (Widen(Set.ofList [ ModifyImplementation ], "DF-1")) v with
    | Error(WideningIntoProhibition [ ModifyImplementation ]) -> ()
    | other -> failwithf "expected prohibition refusal, got %A" other

    let widened = ok (RoleAuthority.adjust (Widen(Set.ofList [ RecordFindings ], "DF-1")) v)
    Assert.True(RoleAuthority.allows RecordFindings widened)

    let narrowed = ok (RoleAuthority.adjust (Narrow(Set.ofList [ RecordVerdict ])) v)
    Assert.False(RoleAuthority.allows RecordVerdict narrowed)

[<Fact>]
let ``role and capability wire tokens round trip`` () =
    for role in Specialized "x" :: defaultRoles do
        Assert.Equal(Some role, ExecutionRole.fromWire (ExecutionRole.toWire role))

    for capability in ExecutionCapability.all do
        Assert.Equal(Some capability, ExecutionCapability.fromWire (ExecutionCapability.toWire capability))

// -------------------------------------------------------- mutation boundary

let private boundary =
    { Scopes = [ Feature "installation" ]
      Projections = [ { Scope = Feature "installation"; Patterns = [ "src/Installation/**"; "tests/Installation*.fs" ] } ]
      EvaluatorReferences = [ "tests/Installation.Gate.fs" ] }

[<Fact>]
let ``mutations are classified against the semantic boundary`` () =
    Assert.Equal(WithinBoundary(Feature "installation"), MutationBoundary.classify boundary "src/Installation/Register.fs")
    Assert.Equal(WithinBoundary(Feature "installation"), MutationBoundary.classify boundary "src/Installation/deep/x/Y.fs")
    Assert.Equal(OutsideBoundary, MutationBoundary.classify boundary "src/Other.fs")
    // Colocated evaluator authority stays outside the writable set.
    Assert.Equal(EvaluatorAuthorityMutation, MutationBoundary.classify boundary "tests/Installation.Gate.fs")

[<Fact>]
let ``an explanation does not widen the boundary`` () =
    let effects = MutationBoundary.scopeEffects boundary [ "README.md", Some "needed a doc tweak"; "src/Installation/A.fs", None ]
    Assert.Equal(1, effects.Length)
    Assert.Equal(OutsideBoundary, effects.Head.Classification)
    Assert.Equal(Some "needed a doc tweak", effects.Head.Explanation)

[<Fact>]
let ``scope expansion is an explicit legal transition`` () =
    let expansion =
        { ExpansionId = "EXP-1"
          AddedScopes = [ Feature "docs" ]
          AddedProjections = [ { Scope = Feature "docs"; Patterns = [ "README.md" ] } ]
          Justification = "installation docs"
          AuthorizedBy = "human:kem" }

    let expanded = ok (MutationBoundary.expand expansion boundary)
    Assert.Equal(WithinBoundary(Feature "docs"), MutationBoundary.classify expanded "README.md")

    Assert.Equal(Error ExpansionWithoutJustification, MutationBoundary.expand { expansion with Justification = "" } boundary)
    Assert.Equal(Error ExpansionWithoutAuthorizer, MutationBoundary.expand { expansion with AuthorizedBy = "" } boundary)

    match MutationBoundary.expand { expansion with AddedProjections = [ { Scope = Feature "docs"; Patterns = [ "tests/**" ] } ] } boundary with
    | Error(ExpansionIntoEvaluatorAuthority _) -> ()
    | other -> failwithf "expected evaluator refusal, got %A" other

[<Fact>]
let ``a worktree is never reported as a security sandbox`` () =
    for mechanism in [ GitWorktree; GitBranch; WorkingDirectory; RemoteCheckout; Container; VirtualMachine; NoIsolation ] do
        Assert.False(Containment.isSecuritySandbox (Containment.ofMechanism mechanism))
        // Enforcement without evidence changes nothing.
        Assert.False(Containment.isSecuritySandbox (Containment.withEnforcement [ FilesystemRestriction ] [] (Containment.ofMechanism mechanism)))

    let enforced = Containment.withEnforcement [ FilesystemRestriction ] [ "landlock:policy-7" ] (Containment.ofMechanism Container)
    Assert.True(Containment.isSecuritySandbox enforced)

// ------------------------------------------------------------ authorization

[<Fact>]
let ``human-required cannot be satisfied by an agent or automation`` () =
    let policy = AuthorizationPolicy.permissive |> AuthorizationPolicy.require "release.approve" HumanRequired

    Assert.True(Result.isOk (AuthorizationPolicy.authorize "release.approve" (actor Human) t0 policy))

    for kind in [ Agent; Automation ] do
        match AuthorizationPolicy.authorize "release.approve" (actor kind) t0 policy with
        | Error(HumanAuthorizationRequired(_, k)) -> Assert.Equal(kind, k)
        | other -> failwithf "expected refusal, got %A" other

[<Fact>]
let ``authorization evidence records the actor that exercised the transition`` () =
    let policy = AuthorizationPolicy.permissive |> AuthorizationPolicy.require "deploy" AutomationAllowed
    let evidence = ok (AuthorizationPolicy.authorize "deploy" (actor Automation) t0 policy)
    Assert.Equal(Automation, evidence.ExercisedBy.Kind)
    Assert.True(Result.isError (AuthorizationPolicy.authorize "deploy" (actor Agent) t0 policy))

// --------------------------------------------------------- envelope + steps

let private id raw = ok (ExecutionId.create raw)
let private workItem = ok (WorkItemId.create "kemiller2002/ordo#44")

let private envelope () =
    ExecutionEnvelope.create
        (id "EXE-1")
        workItem
        (actor Agent)
        (RoleAuthority.defaultFor Implementation)
        "abc123"
        boundary
        (Some(ok (EvaluatorIdentity.create closure)))
        t0
    |> ok

[<Fact>]
let ``an envelope excludes its evaluator closure from the writable boundary`` () =
    let e = envelope ()
    Assert.Equal(EvaluatorAuthorityMutation, MutationBoundary.classify e.Boundary "ros.json")
    Assert.Equal(Implementation, e.Role)

    let leaky = { boundary with Projections = [ { Scope = Feature "installation"; Patterns = [ "**" ] } ] }

    match
        ExecutionEnvelope.create (id "EXE-2") workItem (actor Agent) (RoleAuthority.defaultFor Implementation) "abc" leaky (Some(ok (EvaluatorIdentity.create closure))) t0
    with
    | Error(BoundaryAdmitsEvaluator refs) -> Assert.Equal(closure.Length, refs.Length)
    | other -> failwithf "expected refusal, got %A" other

[<Fact>]
let ``provider identity is an actor attribute, not the role`` () =
    let human = ExecutionEnvelope.create (id "EXE-H") workItem (actor Human) (RoleAuthority.defaultFor Implementation) "abc" boundary None t0 |> ok
    let agent = ExecutionEnvelope.create (id "EXE-A") workItem (actor Agent) (RoleAuthority.defaultFor Implementation) "abc" boundary None t0 |> ok
    Assert.Equal(human.Role, agent.Role)
    Assert.Equal<Set<ExecutionCapability>>(ExecutionEnvelope.capabilities human, ExecutionEnvelope.capabilities agent)

[<Fact>]
let ``a competing attempt gets a new identity and a successor links its parent`` () =
    let first = envelope ()
    Assert.True(Result.isError (ExecutionEnvelope.requireNewIdentity [ first ] first.Id))
    Assert.True(Result.isOk (ExecutionEnvelope.requireNewIdentity [ first ] (id "EXE-9")))

    let predecessor, successor = ok (ExecutionEnvelope.succeed (id "EXE-9") (actor Human) (RoleAuthority.defaultFor Implementation) t0 first)
    Assert.Equal(Interrupted, predecessor.State)
    Assert.Equal(Some first.Id, successor.Parent)
    Assert.Equal(first.WorkItem, successor.WorkItem)
    Assert.Equal(Error ParentIsSelf, ExecutionEnvelope.succeed first.Id (actor Human) (RoleAuthority.defaultFor Implementation) t0 first |> Result.map fst)

let private stepId raw = ok (StepId.create raw)

let private ledger safety =
    StepLedger.empty (id "EXE-1")
    |> StepLedger.add (Step.create (stepId "build") 1 "build" [] (CommandSucceeded "build") RetrySafetyNotEstablished)
    |> Result.bind (StepLedger.add (Step.create (stepId "publish") 2 "publish" [ stepId "build" ] (CommandSucceeded "publish") safety))
    |> ok

let private action l s = StepLedger.nextAction l (StepLedger.tryFind (stepId s) l).Value

[<Fact>]
let ``a resumed execution reuses matched steps and waits on dependencies`` () =
    let l = ledger RetrySafetyNotEstablished
    Assert.Equal(ExecuteStep, action l "build")
    Assert.Equal(AwaitDependencies [ stepId "build" ], action l "publish")

    let built =
        l
        |> StepLedger.start (stepId "build") t0
        |> Result.bind (StepLedger.observe (stepId "build") None (observed [ CommandExited("build", 0) ]) t0)
        |> ok

    Assert.Equal(ReuseCompletedStep, action built "build")
    Assert.Equal(ExecuteStep, action built "publish")

[<Fact>]
let ``an unknown non-idempotent effect refuses retry until reconciled`` () =
    let l = ledger RetrySafetyNotEstablished

    let dispatched =
        l
        |> StepLedger.start (stepId "build") t0
        |> Result.bind (StepLedger.observe (stepId "build") None (observed [ CommandExited("build", 0) ]) t0)
        |> Result.bind (StepLedger.start (stepId "publish") t0)
        |> ok

    // The process died after dispatch: the effect is unknown.
    Assert.Equal(ReconcileStep, action dispatched "publish")

    match StepLedger.start (stepId "publish") t0 dispatched with
    | Error(StepActionNotLegal(_, "start", ReconcileStep)) -> ()
    | other -> failwithf "expected refusal, got %A" other

    let stillUnknown = ok (StepLedger.reconcile (stepId "publish") (EffectStillUnknown "registry unreachable") t0 dispatched)
    Assert.Equal(ReconcileStep, action stillUnknown "publish")

    let notOccurred = ok (StepLedger.reconcile (stepId "publish") (EffectDidNotOccur "version absent from registry") t0 stillUnknown)
    Assert.Equal(RetryStep, action notOccurred "publish")

    let retried = ok (StepLedger.start (stepId "publish") t0 notOccurred)
    let step = (StepLedger.tryFind (stepId "publish") retried).Value
    Assert.Equal(2, Step.attempts step)
    // Append-only: every earlier entry is still there, in order.
    Assert.Equal(4, step.Entries.Length)

[<Fact>]
let ``a reconciled occurred effect is observed rather than repeated`` () =
    let l = ledger RetrySafetyNotEstablished

    let occurred =
        l
        |> StepLedger.start (stepId "build") t0
        |> Result.bind (StepLedger.reconcile (stepId "build") (EffectOccurred "artifact exists") t0)
        |> ok

    Assert.Equal(ObserveStep, action occurred "build")
    Assert.True(Result.isError (StepLedger.start (stepId "build") t0 occurred))

    let observedAgain = ok (StepLedger.observe (stepId "build") None (observed [ CommandExited("build", 0) ]) t0 occurred)
    Assert.Equal(ReuseCompletedStep, action observedAgain "build")

[<Fact>]
let ``a host-established retry-safe effect may be retried before reconciliation`` () =
    let l = ledger (RetrySafeByExternalContract "PUT with idempotency key")

    let dispatched =
        l
        |> StepLedger.start (stepId "build") t0
        |> Result.bind (StepLedger.observe (stepId "build") None (observed [ CommandExited("build", 0) ]) t0)
        |> Result.bind (StepLedger.start (stepId "publish") t0)
        |> ok

    Assert.Equal(RetryStep, action dispatched "publish")

// ----------------------------------------------------------- legal actions

let private snapshot l effects verification =
    { Envelope = envelope ()
      Ledger = l
      ScopeEffects = effects
      Verification = verification
      Policy = AuthorizationPolicy.permissive }

let private availability snap who kind =
    LegalActions.compute snap who |> List.find (fun a -> a.Action = kind)

let private finished () =
    ledger RetrySafetyNotEstablished
    |> StepLedger.start (stepId "build") t0
    |> Result.bind (StepLedger.observe (stepId "build") None (observed [ CommandExited("build", 0) ]) t0)
    |> Result.bind (StepLedger.start (stepId "publish") t0)
    |> Result.bind (StepLedger.observe (stepId "publish") None (observed [ CommandExited("publish", 0) ]) t0)
    |> ok

[<Fact>]
let ``completion is legal only when receipts match and no effect is unresolved`` () =
    let incomplete = availability (snapshot (ledger RetrySafetyNotEstablished) [] None) (actor Agent) Complete
    Assert.False(incomplete.Available)

    let complete = availability (snapshot (finished ()) [] None) (actor Agent) Complete
    Assert.True(complete.Available, String.concat "; " complete.Reasons)

    let effects = MutationBoundary.scopeEffects boundary [ "README.md", Some "it was quick" ]
    let blocked = availability (snapshot (finished ()) effects None) (actor Agent) Complete
    Assert.False(blocked.Available)

[<Fact>]
let ``a changed evaluator blocks completion`` () =
    let old = ok (EvaluatorIdentity.create (replace "ros.json" "old"))
    let stale = Evaluation.judge old (Ok old) VerdictPassed
    let result = availability (snapshot (finished ()) [] (Some stale)) (actor Agent) Complete
    Assert.False(result.Available)

[<Fact>]
let ``human-only transitions are unavailable to agents in the same computation`` () =
    let snap = snapshot (finished ()) [] None
    let snap = { snap with Envelope = ExecutionEnvelope.withHumanOnly [ "execution.complete" ] snap.Envelope }
    Assert.False((availability snap (actor Agent) Complete).Available)
    Assert.True((availability snap (actor Human) Complete).Available)
    Assert.True(LegalActions.isLegal snap (actor Human) Complete)
    Assert.False(LegalActions.isLegal snap (actor Agent) Complete)

[<Fact>]
let ``a verification execution may not expand its mutation boundary`` () =
    let v =
        ExecutionEnvelope.create (id "EXE-V") workItem (actor Automation) (RoleAuthority.defaultFor Verification) "abc" boundary None t0
        |> ok

    let snap = { snapshot (finished ()) [] None with Envelope = v }
    Assert.False((availability snap (actor Automation) ExpandScope).Available)

// ----------------------------------------------------------------- wire

[<Fact>]
let ``expected receipts and observed facts round trip through the wire`` () =
    for expected in allExpectedKinds do
        Assert.Equal(Ok expected, expectedFromJson (expectedToJson expected))

    let facts =
        [ ArtifactObserved("a", Some "d")
          ArtifactObserved("a", None)
          ArtifactNotFound "a"
          CommandExited("c", 3)
          CommandOutcomeUnknown("c", "timeout")
          StateObserved("k", "v")
          ContractChecked("a", "s", false)
          VerificationObserved("e", true)
          TransitionRecorded "t"
          Unobservable("x", "no access") ]

    for fact in facts do
        Assert.Equal(Ok fact, factFromJson (factToJson fact))

[<Fact>]
let ``the envelope wire form states containment honestly`` () =
    let e =
        envelope ()
        |> ExecutionEnvelope.withWorkspace (ok (WorkspaceId.create "wt-EXE-1")) (Containment.ofMechanism GitWorktree)

    let json = envelopeToJson e
    Assert.Equal(Ok(Some(JBool false)), tryMember "securitySandbox" json)
    Assert.Equal(Ok(Some(JString "semantic-only")), tryMember "containment" json)
    Assert.Equal(Ok(Some(JString "implementation")), tryMember "role" json)
