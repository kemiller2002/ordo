/// The example domain the vertical slice is proved against, and the
/// deterministic providers the suite runs on.
///
/// The domain is a real open question from this repository's own method.
/// `method/CHANGE-CLASSIFICATION.md` says outright that it "does not yet
/// specify a mechanical test for 'is this Mechanical Propagation or a
/// disguised Semantic Change'". That is exactly a `Decide`: judgment is
/// required, the output space is known and small, and the consequence of
/// getting it wrong is taking the wrong verification path rather than
/// anything destructive.
///
/// It lives in the test project rather than in a production project because
/// this repository holds methodology, not an application: the domain states
/// and choices belong to an application, and Ordo never defines them
/// (ORDO-0806 / ORDO-3-030).
module Ordo.Tests.Fixtures

open System
open System.Threading
open System.Threading.Tasks
open Ordo.Core.Json
open Ordo.Core.Clock
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.Capability
open Ordo.Core.Policy
open Ordo.Core.StateIdentity
open Ordo.Core.Transition
open Ordo.Decisions.Confidence
open Ordo.Decisions.Contract
open Ordo.Decisions.Request
open Ordo.Decisions.Outcome
open Ordo.Decisions.Provider

/// Unwraps a smart constructor in test setup, where a failure is a defect in
/// the test rather than a case under test.
let ok result =
    match result with
    | Ok value -> value
    | Error error -> failwithf "fixture construction failed: %A" error

let at (text: string) = DateTimeOffset.Parse(text, Globalization.CultureInfo.InvariantCulture)

/// A fixed instant every time-dependent test is anchored to, so that nothing
/// in the suite depends on the wall clock (ORDO-9602).
let now = at "2026-09-18T12:00:00Z"

let testClock: Clock = fixedAt now

// ------------------------------------------------------------ the domain

/// The three classes a change site exposed by a mechanical obligation can
/// fall into, in a repository with no presentation tier.
type ChangeClass =
    | MechanicalPropagation
    | SemanticChange
    | BoundaryChange

let changeClassToken choice =
    match choice with
    | MechanicalPropagation -> "mechanical-propagation"
    | SemanticChange -> "semantic-change"
    | BoundaryChange -> "boundary-change"

/// Where a change site is in its verification life.
type SiteVerification =
    | Unclassified
    | FastPathAuthorized
    | FullPathRequired

let siteVerificationToken state =
    match state with
    | Unclassified -> "unclassified"
    | FastPathAuthorized -> "fast-path-authorized"
    | FullPathRequired -> "full-path-required"

/// The domain's own state, and its view.
let siteState (path: string) (verification: SiteVerification) (revision: string) =
    JObject
        [ "site", JString path
          "verification", JString(siteVerificationToken verification)
          "revision", JString revision ]

let changeSiteViewSchema = ok (StateViewSchema.create "sde.change-site-state" 1)

let snapshotOf view = StateSnapshot.take changeSiteViewSchema now view

let toolDiagnostic =
    EvidenceRequirement.create "tool-diagnostic" "The compiler or architecture-check output that exposed this site."
    |> EvidenceRequirement.acceptingOnly [ AnyDirect ]

let siteDiff =
    EvidenceRequirement.create "site-diff" "The diff at the site, and the semantic authority it touches."
    |> EvidenceRequirement.acceptingOnly [ AnyDirect ]

let changeClassContract =
    DecisionContract.create
        (ok (DecisionContractId.create "sde.change-site-class"))
        (ok (ContractVersion.create 1))
        "A mechanical obligation exposed this change site. Is filling it in a mechanical propagation of a decision already made, a semantic change in disguise, or a change to a representation crossing a boundary?"
        "One change site in a repository with no presentation tier, exposed by a compiler or architecture-check obligation. Not whole work items, and not presentation-only work."
        (ok (ChoiceSpace.create changeClassToken [ MechanicalPropagation; SemanticChange; BoundaryChange ]))
    |> DecisionContract.requiring [ toolDiagnostic; siteDiff ]
    |> DecisionContract.withConsequence "low: selects a verification path"
    |> DecisionContract.activated

let evidence (id: string) (kind: EvidenceKind) (observedAt: DateTimeOffset) (content: JsonValue) : Evidence =
    { Id = ok (EvidenceId.create id)
      Kind = kind
      Source =
        { System = "repository"
          Reference = Some "tests/Ordo.Tests" }
      ObservedAt = observedAt
      Content = content }

let diagnosticEvidence =
    evidence "tool-diagnostic" Direct now (JString "FS0025: incomplete pattern match at Transition.fs(88,9)")

let diffEvidence =
    evidence "site-diff" Direct now (JString "+ | WrongEvidenceKind requirements -> reportKind requirements")

let fullEvidence: Evidence list = [ diagnosticEvidence; diffEvidence ]

let authorizeVerificationPath =
    { Id = ok (CapabilityId.create "authorize-verification-path")
      Description = "May decide which verification path a change site takes." }

/// The domain's policy over a classification.
///
/// This is where the domain says what a class means for it, and it is the
/// only place a confidence threshold could legitimately live. It does not
/// use one: the method's own evidence says a mechanically-exposed site can
/// be filled in with a value that compiles and is still wrong, so a
/// confident `MechanicalPropagation` still buys only the Fast Path, never a
/// skipped check.
let fastPathPolicy =
    Policy.create
        (ok (PolicyId.create "sde.verification-path"))
        (ok (PolicyVersion.create 1))
        (fun (choice: ChangeClass) ->
            match choice with
            | MechanicalPropagation -> PolicyAllows
            | BoundaryChange -> PolicyRequiresHumanReview "a boundary change needs a contract decision before a path is chosen"
            | SemanticChange -> PolicyRefuses "a semantic change goes through the full path, not the fast path")

let authorizeFastPath =
    TransitionRequirement.create "authorize-fast-path"
    |> TransitionRequirement.requiringSourceState "verification is unclassified" (fun snapshot ->
        match tryMember "verification" snapshot.View with
        | Ok(Some(JString "unclassified")) -> true
        | _ -> false)
    |> TransitionRequirement.requiringCapabilities [ authorizeVerificationPath.Id ]
    |> TransitionRequirement.requiringEvidence [ toolDiagnostic; siteDiff ]

/// A transition context with everything satisfied. Individual tests take
/// one thing away and assert on what is refused.
let contextFor (snapshot: StateSnapshot) (choice: ChangeClass) =
    { CurrentState = snapshot
      FormedAgainst = snapshot.Fingerprint
      Held = CapabilitySet.ofList [ authorizeVerificationPath ]
      Available = fullEvidence
      Obligations = []
      Policy = fastPathPolicy.Identity, fastPathPolicy.Evaluate choice
      Now = now
      RequestedBy = None }

// ---------------------------------------------------------- fake providers

let fakeIdentity =
    { Provider = ok (ProviderId.create "fake")
      Model = Some "scripted"
      ModelVersion = Some "1"
      AdapterVersion = "test" }

let fakeUsage =
    { InputTokens = Some 100
      OutputTokens = Some 20
      CachedInputTokens = None
      ProviderReportedCost = None }

let allCapabilities =
    [ BoundedChoiceSelection
      NativeStructuredOutput
      SelfReportedConfidence
      LocalExecution
      CooperativeCancellation ]

/// A provider that answers from a script.
///
/// Deterministic, offline and free: the normal suite must never need a
/// network, an API key or a paid call (ORDO-3607 / ORDO-3-191). The counter
/// is the one piece of mutation in the fixture, and it exists because
/// "answer differently on the second call" is the behaviour a replay test
/// needs to observe.
let scriptedProvider (capabilities: ProviderCapability list) (responses: ProviderResponse list) : DecisionProvider =
    let remaining = ref responses

    let next () =
        match remaining.Value with
        | [] -> failwith "the scripted provider was called more times than it has answers"
        | head :: tail ->
            remaining.Value <- tail
            head

    { Identity = fakeIdentity
      Capabilities = capabilities
      Resolve =
        fun _ _ ->
            Task.FromResult
                { Response = next ()
                  Identity = fakeIdentity
                  Usage = fakeUsage
                  TransportRetries = 0 } }

let answering (response: ProviderResponse) = scriptedProvider allCapabilities [ response ]

/// A provider that never returns until its caller gives up.
let cancellingProvider: DecisionProvider =
    { Identity = fakeIdentity
      Capabilities = allCapabilities
      Resolve =
        fun _ cancellation ->
            task {
                do! Task.Delay(Timeout.Infinite, cancellation)

                return
                    { Response = ResponseCancelled
                      Identity = fakeIdentity
                      Usage = ProviderUsage.unreported
                      TransportRetries = 0 }
            } }

/// A provider whose implementation is defective and throws.
let throwingProvider (message: string) : DecisionProvider =
    { Identity = fakeIdentity
      Capabilities = allCapabilities
      Resolve = fun _ _ -> raise (InvalidOperationException message) }

let confidenceOf value = ok (Confidence.providerReported value)

let requestFor (contract: DecisionContract<ChangeClass>) (snapshot: StateSnapshot) (available: Evidence list) : DecisionRequest<ChangeClass> =
    ok (
        DecisionRequest.create
            (ok (DecisionRequestId.create "req-1"))
            (ok (ResolutionId.create "res-1"))
            contract
            snapshot
            available
            now
    )
