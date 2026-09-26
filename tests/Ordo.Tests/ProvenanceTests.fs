/// Requester identity and provenance, kept apart from capability and
/// evidence (ORDO-PROV-01..08 / DF-SDE-2026-0016), at Praxis contract
/// revision 1.2.
///
/// Three kinds of test live here:
///
/// * conformance with the Praxis contract: every vendored case in
///   `fixtures/praxis-provenance/cases.json` reaches the same verdict and
///   warning count as Praxis, the end-to-end Echelon chain replays to the
///   same expectations, and the vendored files are byte-identical to the
///   Praxis commit recorded in `SOURCE.json`;
/// * metamorphic invariants: for the same capabilities, evidence,
///   obligations, policy and state, every requester — agent, human,
///   automation, unknown, extension, any provider, or none — receives the
///   same verdict, the same failures, the same decision, and the same
///   confidence. The requester space is small and closed, so the loops are
///   exhaustive rather than sampled;
/// * boundary and round-trip behaviour: request provenance on the
///   observation wire, attributed evidence and obligations, unsupported
///   major versions carried verbatim, malformed provenance refused, and
///   legacy records without provenance left exactly as they were.
module Ordo.Tests.ProvenanceTests

open System
open System.IO
open System.Security.Cryptography
open System.Threading
open Xunit
open Ordo.Core.Json
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.Capability
open Ordo.Core.Obligation
open Ordo.Core.ExternalEffect
open Ordo.Core.StateIdentity
open Ordo.Core.Transition
open Ordo.Core.Provenance
open Ordo.Core.Wire
open Ordo.Decisions.Request
open Ordo.Decisions.Outcome
open Ordo.Decisions.Provider
open Ordo.Decisions.Observation
open Ordo.Decisions.Resolve
open Ordo.Tests.Fixtures

// ---------------------------------------------------------------- fixtures

let private fixtureDirectory =
    Path.Combine(AppContext.BaseDirectory, "fixtures", "praxis-provenance")

let private fixture (name: string) =
    match parse (File.ReadAllText(Path.Combine(fixtureDirectory, name))) with
    | Ok value -> value
    | Error error -> failwithf "fixture %s is not JSON: %A" name error

let private get (name: string) (value: JsonValue) =
    match tryMember name value with
    | Ok(Some found) -> found
    | _ -> failwithf "missing member %s" name

let private text value =
    match value with
    | JString s -> s
    | other -> failwithf "expected a string, got %A" other

let private items value =
    match value with
    | JArray values -> values
    | other -> failwithf "expected an array, got %A" other

let private members value =
    match value with
    | JObject values -> values
    | other -> failwithf "expected an object, got %A" other

let private supported (node: JsonValue) =
    match ProvenanceBlock.classify node with
    | Supported(block, _) -> block
    | other -> failwithf "expected a supported block, got %A" other

let private requester actor execution = ok (Requester.create actor execution)

/// Every kind of requester the contract admits, several providers, and the
/// absent requester.
let private everyRequester: Requester option list =
    [ None
      Some(requester (Actor.agent "openai/codex" (Some "openai") (Some "gpt-5-codex") (Some "codex")) (Some "EXE-20260926T080000000Z-aaaa0001"))
      Some(requester (Actor.agent "anthropic/claude-code" (Some "anthropic") None (Some "claude-code")) (Some "EXE-20260926T080000000Z-bbbb0002"))
      Some(requester (Actor.agent "google/gemini-cli" (Some "google") None (Some "gemini-cli")) (Some "EXT-vigila.run-7"))
      // An agent whose identity text names the very capability it lacks.
      Some(requester (Actor.agent "authorize-verification-path" (Some "fake") (Some "scripted") None) None)
      Some(requester (Actor.human "kevin") None)
      Some(requester (Actor.human "admin") (Some "EXE-20260926T090000000Z-cccc0003"))
      Some(requester (Actor.automation "github/github-actions" (Some "github") None (Some "github-actions")) (Some "EXT-github-actions.run-777-1"))
      Some(requester Actor.unknown None)
      Some(
          requester
              { Kind = ExtensionActor "x-scheduler"
                Id = "cron/nightly"
                Provider = None
                Model = None
                Runtime = None }
              None
      ) ]

// --------------------------------------------------- vendored conformance

[<Fact>]
let ``vendored Praxis fixtures are byte-identical to the recorded source commit`` () =
    let source = fixture "SOURCE.json"
    Assert.Equal("kemiller2002/praxis", text (get "repository" source))
    Assert.Equal("b0037183389c8b9392919f58521b9487d1b4d5c6", text (get "commit" source))
    Assert.Equal("1.2", text (get "contractRevision" source))

    let files = members (get "files" source)

    Assert.Equal<string list>(
        [ "cases.json"
          "echelon-chain.json"
          "envelope-key-cases.json"
          "identity-environment.json"
          "lineage-cases.json"
          "text-cases.json" ],
        files |> List.map fst
    )

    for name, expected in files do
        use stream = File.OpenRead(Path.Combine(fixtureDirectory, name))
        let actual = Convert.ToHexString(SHA256.HashData stream).ToLowerInvariant()
        Assert.True((actual = text expected), sprintf "%s was edited locally: sha256 %s, SOURCE.json records %s" name actual (text expected))

[<Fact>]
let ``every Praxis conformance case reaches the same verdict and warning count`` () =
    let cases = items (get "cases" (fixture "cases.json"))
    Assert.Equal(70, cases.Length)

    let mismatches =
        cases
        |> List.choose (fun case ->
            let name = text (get "name" case)
            let expected = text (get "expect" case)

            let expectedWarnings =
                match get "warnings" case with
                | JInt n -> int n
                | other -> failwithf "warnings must be an integer, got %A" other

            let verdict, warnings =
                match ProvenanceBlock.classify (get "block" case) with
                | Supported(_, warnings) -> "supported", warnings.Length
                | Unsupported _ -> "unsupported", 0
                | Malformed _ -> "malformed", 0

            if verdict = expected && warnings = expectedWarnings then None
            else Some(sprintf "%s: expected %s/%d, got %s/%d" name expected expectedWarnings verdict warnings))

    Assert.True(mismatches.IsEmpty, String.Join(Environment.NewLine, mismatches))

[<Fact>]
let ``supported conformance blocks round-trip through the Ordo serializer unchanged`` () =
    for case in items (get "cases" (fixture "cases.json")) do
        match ProvenanceBlock.classify (get "block" case) with
        | Supported(block, _) ->
            let written = render (ProvenanceBlock.toJson block)
            Assert.Equal(render (get "block" case), written)

            match parse written with
            | Ok reread ->
                match ProvenanceBlock.classify reread with
                | Supported(again, _) ->
                    Assert.Equal<Contribution list>(ProvenanceBlock.contributions block, ProvenanceBlock.contributions again)
                    Assert.Equal<string list>(ProvenanceBlock.derivedFrom block, ProvenanceBlock.derivedFrom again)
                | other -> failwithf "%s no longer supported after round trip: %A" (text (get "name" case)) other
            | Error error -> failwithf "rendered block is not JSON: %A" error
        | Unsupported(_, verbatim) -> Assert.Equal(get "block" case, verbatim)
        | Malformed _ -> ()

[<Fact>]
let ``the Echelon chain replays to the recorded expectations`` () =
    let chain = fixture "echelon-chain.json"

    let blocks =
        items (get "steps" chain)
        |> List.fold
            (fun (blocks: Map<string, ProvenanceBlock>) step ->
                let record = text (get "record" step)
                let current = blocks |> Map.tryFind record |> Option.defaultValue ProvenanceBlock.empty

                let next =
                    match tryMember "lineage" step with
                    | Ok(Some references) ->
                        match ProvenanceBlock.addLineage (items references |> List.map text) current with
                        | Ok(block, _) -> block
                        | Error error -> failwithf "lineage for %s refused: %s" record error
                    | _ ->
                        let append = get "append" step

                        match ProvenanceBlock.appendJson (text (get "key" append)) (get "contribution" append) current with
                        | Ok(block, changed) ->
                            Assert.True changed
                            block
                        | Error error -> failwithf "append to %s refused: %s" record error

                Map.add record next blocks)
            Map.empty

    let expect = get "expect" chain

    for record, expected in members (get "originators" expect) do
        match ProvenanceBlock.originator blocks[record] with
        | Some origin ->
            Assert.Equal(text (get "key" expected), origin.Key)
            Assert.Equal(text (get "actorId" expected), origin.Actor.Id)
        | None -> failwithf "%s lost its originator" record

    for record, roles in members (get "roles" expect) do
        for role, keys in members roles do
            Assert.Equal<string list>(
                items keys |> List.map text,
                ProvenanceBlock.withRole role blocks[record] |> List.map (fun item -> item.Key)
            )

    let rec reachable (seen: Set<string>) (record: string) =
        match Map.tryFind record blocks with
        | None -> seen
        | Some block ->
            ProvenanceBlock.derivedFrom block
            |> List.filter (fun reference -> not (seen.Contains reference))
            |> List.fold (fun seen reference -> reachable (Set.add reference seen) reference) seen

    let reached = reachable Set.empty (text (get "lineageFrom" expect))

    for reference in items (get "lineageReaches" expect) do
        Assert.Contains(text reference, reached)

    let distinct = get "distinctExecutionsOfOneAgent" expect

    let keysOfAgent =
        blocks
        |> Map.toList
        |> List.collect (fun (_, block) -> ProvenanceBlock.contributions block)
        |> List.filter (fun item -> item.Actor.Id = text (get "actorId" distinct))
        |> List.map (fun item -> item.Key)
        |> List.distinct
        |> List.sort

    Assert.Equal<string list>(items (get "keys" distinct) |> List.map text |> List.sort, keysOfAgent)

    let originators = blocks |> Map.toList |> List.choose (fun (_, block) -> ProvenanceBlock.originator block)

    match get "chainOriginatorCount" expect with
    | JInt count -> Assert.Equal(int count, originators.Length)
    | other -> failwithf "expected a count, got %A" other

// ------------------------------------------ identity is not capability

let private unclassified = snapshotOf (siteState "Transition.fs" Unclassified "r1")

let private reconcile = ok (ObligationId.create "reconcile-effect-1")

let private freshDiagnostic =
    toolDiagnostic |> EvidenceRequirement.withMaximumAge (TimeSpan.FromHours 1.0)

/// One requirement/context pair per verdict the transition can reach.
let private scenarios: (string * TransitionRequirement * TransitionContext) list =
    let full = contextFor unclassified MechanicalPropagation

    [ "everything satisfied", authorizeFastPath, full
      "no capability", authorizeFastPath, { full with Held = CapabilitySet.empty }
      "no evidence", authorizeFastPath, { full with Available = [] }
      "inferred evidence where only direct is accepted",
      authorizeFastPath,
      { full with
          Available =
              [ evidence "tool-diagnostic" (Inferred(ok (ProviderId.create "fake"))) now (JString "a model's guess")
                diffEvidence ] }
      "stale evidence",
      authorizeFastPath |> TransitionRequirement.requiringEvidence [ freshDiagnostic; siteDiff ],
      { full with
          Available = [ evidence "tool-diagnostic" Direct (now.AddDays -2.0) (JString "old"); diffEvidence ] }
      "outstanding obligation",
      authorizeFastPath |> TransitionRequirement.requiringObligations [ reconcile ],
      { full with
          Obligations = [ Obligation.create reconcile (ReconcileExternalEffect(ok (ExternalEffectId.create "effect-1"))) now ] }
      "policy refuses", authorizeFastPath, contextFor unclassified SemanticChange
      "policy requires a person", authorizeFastPath, contextFor unclassified BoundaryChange
      "state moved",
      authorizeFastPath,
      { full with FormedAgainst = (snapshotOf (siteState "Transition.fs" Unclassified "r0")).Fingerprint }
      "nothing held and nothing available",
      authorizeFastPath,
      { full with
          Held = CapabilitySet.empty
          Available = [] } ]

/// The verdict without the audit field the requester fills in.
let private verdictOf evaluation =
    match evaluation with
    | TransitionAllowed authorization -> sprintf "allowed %s %A %A %O" authorization.Name authorization.State authorization.Policy authorization.At
    | TransitionRefused failures -> sprintf "refused %A" failures
    | TransitionRequiresHumanReview(reason, failures) -> sprintf "review %s %A" reason failures

[<Fact>]
let ``every requester receives exactly the verdict the context alone earns`` () =
    for name, requirement, context in scenarios do
        let baseline = verdictOf (Transition.evaluate requirement context)

        for requestedBy in everyRequester do
            let evaluated =
                Transition.evaluateRequest requirement { Context = context; RequestedBy = requestedBy }

            Assert.True(
                (verdictOf evaluated = baseline),
                sprintf "%s: requester %A changed the verdict" name (requestedBy |> Option.map (fun r -> r.Actor))
            )

[<Fact>]
let ``no identity grants a capability that is not held`` () =
    let withoutCapability =
        { contextFor unclassified MechanicalPropagation with
            Held = CapabilitySet.empty }

    for requestedBy in everyRequester do
        match Transition.evaluateRequest authorizeFastPath { Context = withoutCapability; RequestedBy = requestedBy } with
        | TransitionRefused [ MissingCapability [ missing ] ] -> Assert.Equal(authorizeVerificationPath.Id, missing)
        | other -> failwithf "%A was not refused for the missing capability: %A" requestedBy other

[<Fact>]
let ``a held capability works without any requester, and the absent requester stays unknown`` () =
    match Transition.evaluateRequest authorizeFastPath { Context = contextFor unclassified MechanicalPropagation; RequestedBy = None } with
    | TransitionAllowed authorization -> Assert.Equal(None, authorization.RequestedBy)
    | other -> failwithf "expected the change to be allowed, got %A" other

    // The legacy entry point is unchanged and records no requester.
    match Transition.evaluate authorizeFastPath (contextFor unclassified MechanicalPropagation) with
    | TransitionAllowed authorization -> Assert.Equal(None, authorization.RequestedBy)
    | other -> failwithf "expected the change to be allowed, got %A" other

[<Fact>]
let ``an authorisation records who asked, for audit only`` () =
    let agent = requester (Actor.agent "openai/codex" (Some "openai") None (Some "codex")) (Some "EXE-20260926T080000000Z-aaaa0001")

    match Transition.evaluateRequest authorizeFastPath { Context = contextFor unclassified MechanicalPropagation; RequestedBy = Some agent } with
    | TransitionAllowed authorization ->
        Assert.Equal(Some agent, authorization.RequestedBy)
        Assert.Equal(fastPathPolicy.Identity, authorization.Policy)
    | other -> failwithf "expected the change to be allowed, got %A" other

// ------------------------------------------- identity is not evidence

[<Fact>]
let ``evidence provenance never changes what the evidence satisfies`` () =
    let attributions =
        everyRequester
        |> List.map (Option.map (fun r -> ok (Requester.toBlock "evidence-producer" now None r) |> Understood))

    let baseline = Evidence.checkAll now fullEvidence [ toolDiagnostic; siteDiff ]

    for provenance in attributions do
        let attributed = fullEvidence |> List.map (fun e -> { Record = e; Provenance = provenance })
        Assert.Equal<RequirementCheck list>(baseline, Evidence.checkAll now (Attributed.records attributed) [ toolDiagnostic; siteDiff ])

    // An inference stays an inference whoever is recorded as producing it.
    let inferred = evidence "tool-diagnostic" (Inferred(ok (ProviderId.create "fake"))) now (JString "guess")
    let human = requester (Actor.human "kevin") None

    let attributedInference =
        Attributed.withBlock (ok (Requester.toBlock "evidence-producer" now None human)) inferred

    match Evidence.checkRequirement now (Attributed.records [ attributedInference ]) toolDiagnostic with
    | WrongKind _ -> ()
    | other -> failwithf "a human-attributed inference satisfied a direct-only requirement: %A" other

let private options = ResolveOptions.standard |> ResolveOptions.withClock testClock

let private run provider request =
    (Resolve.execute options provider request CancellationToken.None).GetAwaiter().GetResult()

let private responses =
    [ ChoiceSelected("mechanical-propagation", Some(confidenceOf 0.62), None)
      ChoiceSelected("semantic-change", Some(confidenceOf 0.99), None)
      ChoiceSelected("boundary-change", None, None)
      DeliberationRequested "not bounded yet"
      EvidenceInsufficient [ "site-diff" ]
      HumanReviewRequested "owner should look" ]

[<Fact>]
let ``decision outcome, confidence and provider view are identical for every requester`` () =
    for evidenceSet in [ fullEvidence; [ diagnosticEvidence ] ] do
        let legacy = requestFor changeClassContract unclassified evidenceSet

        for response in responses do
            let baseline = (run (answering response) legacy).Outcome
            let baselineView = Resolve.toProviderRequest options legacy

            for requestedBy in everyRequester do
                let request = { legacy with RequestedBy = requestedBy }
                let record = run (answering response) request
                Assert.Equal(baseline, record.Outcome)

                Assert.Equal(
                    DecisionOutcome.decision baseline |> Option.bind (fun d -> d.Confidence),
                    DecisionOutcome.decision record.Outcome |> Option.bind (fun d -> d.Confidence)
                )

                // Identity never reaches the provider.
                Assert.Equal(baselineView, Resolve.toProviderRequest options request)

[<Fact>]
let ``the requester is recorded apart from the provider and never derived from it`` () =
    let human = requester (Actor.human "kevin") None
    let request = requestFor changeClassContract unclassified fullEvidence |> DecisionRequest.requestedBy human
    let record = run (answering (ChoiceSelected("mechanical-propagation", None, None))) request

    Assert.Equal(Some fakeIdentity, record.Observation.Provider)

    match ResolutionObservation.requestedBy record.Observation with
    | Some recorded ->
        Assert.Equal(HumanActor, recorded.Actor.Kind)
        Assert.Equal("kevin", recorded.Actor.Id)
        Assert.Equal(None, recorded.Actor.Provider)
        Assert.Equal(None, recorded.Execution)
    | None -> failwith "the requester was lost"

    // An unattributed request records no requester, and nothing is guessed
    // from the provider that answered it.
    let legacy = run (answering (ChoiceSelected("mechanical-propagation", None, None))) (requestFor changeClassContract unclassified fullEvidence)
    Assert.Equal(None, legacy.Observation.RequestProvenance)
    Assert.Equal(None, ResolutionObservation.requestedBy legacy.Observation)

// ----------------------------------------------------- observation wire

[<Fact>]
let ``request provenance is additive on observation v2 and absent for legacy requests`` () =
    let answer = ChoiceSelected("mechanical-propagation", Some(confidenceOf 0.7), None)
    let legacyRequest = requestFor changeClassContract unclassified fullEvidence
    let legacy = ResolutionObservation.encode (run (answering answer) legacyRequest).Observation

    Assert.Equal(Ok None, tryMember "requestProvenance" legacy)

    let agent = requester (Actor.agent "openai/codex" (Some "openai") (Some "gpt-5-codex") (Some "codex")) None
    let attributed = ResolutionObservation.encode (run (answering answer) (legacyRequest |> DecisionRequest.requestedBy agent)).Observation

    // Same schema and version; every legacy member unchanged and in order.
    let legacyMembers = members legacy
    let attributedMembers = members attributed
    Assert.Equal<(string * JsonValue) list>(legacyMembers, attributedMembers |> List.filter (fun (name, _) -> name <> "requestProvenance"))
    Assert.Equal("requestProvenance", fst (List.last attributedMembers))

    match parse (render attributed) with
    | Ok reread ->
        let block = supported (get "requestProvenance" reread)

        match ProvenanceBlock.originator block with
        | Some origin ->
            // No execution was declared, so the key is the operation.
            Assert.Equal("EXT-op.req-1", origin.Key)
            Assert.Equal<string list>([ "created" ], origin.Operations)
            Assert.Equal("2026-09-18T12:00:00.000Z", origin.At)
            Assert.Equal(agent.Actor, origin.Actor)
        | None -> failwith "the requester's creation was lost"
    | Error error -> failwithf "encoded observation is not JSON: %A" error

// --------------------------------------------------------- requesters

[<Fact>]
let ``every requester serializes to a supported block and reads back unchanged`` () =
    for requestedBy in everyRequester |> List.choose id do
        let block = ok (Requester.toBlock "req-9" now (Some "asked for a verification path") requestedBy)

        match parse (render (ProvenanceBlock.toJson block)) with
        | Ok reread ->
            match ProvenanceBlock.classify reread with
            | Supported(again, []) ->
                match Requester.ofBlock again with
                | Some roundTripped ->
                    Assert.Equal(requestedBy.Actor, roundTripped.Actor)
                    Assert.Equal(requestedBy.Execution, roundTripped.Execution)
                | None -> failwith "the requester was lost"
            | other -> failwithf "%A did not serialize to a supported block: %A" requestedBy.Actor other
        | Error error -> failwithf "not JSON: %A" error

[<Fact>]
let ``a requester refuses credentials, non-execution keys and invalid actors`` () =
    let refused actor execution =
        match Requester.create actor execution with
        | Error _ -> ()
        | Ok accepted -> failwithf "accepted %A" accepted

    refused (Actor.human "ghp_abcdefghijklmnopqrstuvwxyz0123456789") None
    refused (Actor.agent "openai/codex" (Some "sk-abcdefghijklmnopqrstuvwxyz0123") None None) None
    refused (Actor.human "kevin") (Some "CTB-20260926-5f2e19aa")
    refused (Actor.human "kevin") (Some "EXE-")
    refused (Actor.human "") None
    refused { Actor.unknown with Kind = ExtensionActor "robot" } None

[<Fact>]
let ``an unknown requester is recorded as unknown, not replaced with a guess`` () =
    let unknown = requester Actor.unknown None
    let block = ok (Requester.toBlock "req-1" now None unknown)

    match ProvenanceBlock.originator block with
    | Some origin ->
        Assert.Equal(UnknownActor, origin.Actor.Kind)
        Assert.Equal("unknown", origin.Actor.Id)
        Assert.Equal(Some "unknown", origin.Actor.Provider)
    | None -> failwith "the unknown requester was dropped"

// ------------------------------------------------ appending and history

let private agentA = Actor.agent "openai/codex" (Some "openai") (Some "gpt-5-codex") (Some "codex")
let private agentB = Actor.agent "anthropic/claude-code" (Some "anthropic") None (Some "claude-code")

let private contribution key operations at actor =
    { Key = key
      Operations = operations
      At = at
      Last = None
      Actor = actor
      Reason = None
      Evidence = [] }

let private appended item block =
    match ProvenanceBlock.append item block with
    | Ok(next, _) -> next
    | Error error -> failwithf "append refused: %s" error

[<Fact>]
let ``contributions accumulate without re-attribution, second creation, or merging executions`` () =
    let created =
        ProvenanceBlock.empty
        |> appended (contribution "EXE-1" [ "created" ] "2026-09-26T08:00:00.000Z" agentA)
        |> appended (contribution "EXE-2" [ "modified" ] "2026-09-26T08:10:00.000Z" agentB)
        // The same agent in a second execution is a second contribution.
        |> appended (contribution "EXE-3" [ "modified" ] "2026-09-26T08:20:00.000Z" agentA)
        |> appended (contribution "CTB-20260926-5f2e19aa" [ "approved" ] "2026-09-26T09:00:00.000Z" (Actor.human "kevin"))
        |> appended (contribution "EXT-github-actions.run-1" [ "validated" ] "2026-09-26T09:30:00.000Z" (Actor.automation "github/github-actions" (Some "github") None (Some "github-actions")))

    Assert.Equal<string list>(
        [ "EXE-1"; "EXE-2"; "EXE-3"; "CTB-20260926-5f2e19aa"; "EXT-github-actions.run-1" ],
        ProvenanceBlock.contributions created |> List.map (fun item -> item.Key)
    )

    Assert.Equal(Some "EXE-1", ProvenanceBlock.originator created |> Option.map (fun item -> item.Key))

    match ProvenanceBlock.append (contribution "EXE-2" [ "modified" ] "2026-09-26T10:00:00.000Z" agentA) created with
    | Error message -> Assert.Contains("re-attribute", message)
    | Ok _ -> failwith "re-attributed another agent's contribution"

    match ProvenanceBlock.append (contribution "EXE-4" [ "created" ] "2026-09-26T10:00:00.000Z" agentB) created with
    | Error message -> Assert.Contains("originator", message)
    | Ok _ -> failwith "accepted a second creation"

    // Identical re-recording is idempotent; a later one advances `last`.
    match ProvenanceBlock.append (contribution "EXE-2" [ "modified" ] "2026-09-26T08:10:00.000Z" agentB) created with
    | Ok(_, changed) -> Assert.False changed
    | Error error -> failwithf "idempotent append refused: %s" error

    match ProvenanceBlock.append (contribution "EXE-2" [ "modified" ] "2026-09-26T11:00:00.000Z" agentB) created with
    | Ok(next, true) ->
        let entry = ProvenanceBlock.contributions next |> List.find (fun item -> item.Key = "EXE-2")
        Assert.Equal(Some "2026-09-26T11:00:00.000Z", entry.Last)
        Assert.Equal("2026-09-26T08:10:00.000Z", entry.At)
    | other -> failwithf "expected a changed block, got %A" other

[<Fact>]
let ``unknown fields and unknown operations survive appending`` () =
    let received =
        JObject
            [ "schema", JString SchemaTag
              "x-attestation", JObject [ "signer", JString "future" ]
              "contributions",
              JObject
                  [ "EXE-1",
                    JObject
                        [ "operations", JArray [ JString "created"; JString "archived" ]
                          "at", JString "2026-09-26T08:00:00.000Z"
                          "actor", Actor.encode agentA
                          "signature", JString "opaque" ] ] ]

    match ProvenanceBlock.classify received with
    | Supported(block, [ _ ]) ->
        let next = block |> appended (contribution "EXE-2" [ "reviewed" ] "2026-09-26T09:00:00.000Z" agentB)
        let json = ProvenanceBlock.toJson next
        Assert.Equal(get "x-attestation" received, get "x-attestation" json)
        Assert.Equal(get "EXE-1" (get "contributions" received), get "EXE-1" (get "contributions" json))
    | other -> failwithf "expected one tolerated warning, got %A" other

// --------------------------------------- attributed records on the wire

[<Fact>]
let ``attributed evidence round-trips and still decodes as the same evidence for legacy readers`` () =
    let producer = requester agentA (Some "EXE-20260926T080000000Z-aaaa0001")

    let block =
        ok (Requester.toBlock "tool-diagnostic" now (Some "ran the compiler") producer)
        |> appended (contribution "EXE-20260926T080000000Z-aaaa0001" [ "measured" ] "2026-09-18T12:00:00.000Z" agentA)
        |> ProvenanceBlock.addLineage [ "dokimos:snapshot/S-1" ]
        |> ok
        |> fst

    let attributed = Attributed.withBlock block diagnosticEvidence
    let document = encodeAttributed encodeEvidence attributed

    match parse (render document) with
    | Ok reread ->
        Assert.Equal(Ok diagnosticEvidence, decodeEvidence reread)

        match decodeAttributed decodeEvidence reread with
        | Ok roundTripped ->
            Assert.Equal(diagnosticEvidence, roundTripped.Record)

            match roundTripped.Provenance with
            | Some(Understood carried) ->
                Assert.Equal(render (ProvenanceBlock.toJson block), render (ProvenanceBlock.toJson carried))
                Assert.Equal<string list>([ "dokimos:snapshot/S-1" ], ProvenanceBlock.derivedFrom carried)
            | other -> failwithf "expected understood provenance, got %A" other
        | Error error -> failwithf "attributed evidence did not decode: %A" error
    | Error error -> failwithf "not JSON: %A" error

    // Legacy evidence without provenance is unattributed, not an error.
    match decodeAttributed decodeEvidence (encodeEvidence diagnosticEvidence) with
    | Ok legacy -> Assert.Equal(None, legacy.Provenance)
    | Error error -> failwithf "legacy evidence refused: %A" error

[<Fact>]
let ``an unsupported major is carried verbatim and a malformed block is refused`` () =
    let future =
        JObject
            [ "schema", JString "praxis.provenance/2"
              "authors", JArray [ JString "whatever v2 means" ] ]

    let document = encodeEvidence diagnosticEvidence |> withProvenance (Some(CarriedVerbatim("praxis.provenance/2", future)))

    match decodeAttributed decodeEvidence document with
    | Ok item ->
        Assert.Equal(Some(CarriedVerbatim("praxis.provenance/2", future)), item.Provenance)
        Assert.Equal(future, get "provenance" (encodeAttributed encodeEvidence item))

        match Attributed.contribute (contribution "EXE-9" [ "reviewed" ] "2026-09-26T08:00:00.000Z" agentA) item with
        | Error message -> Assert.Contains("unsupported", message)
        | Ok _ -> failwith "merged into a major version this build does not interpret"
    | Error error -> failwithf "unsupported provenance was refused instead of carried: %A" error

    let malformed =
        encodeEvidence diagnosticEvidence
        |> withProvenance (Some(CarriedVerbatim("praxis.provenance/1", JObject [ "schema", JString SchemaTag; "contributions", JArray [] ])))

    match decodeAttributed decodeEvidence malformed with
    | Error(InvalidField("$.provenance", _)) -> ()
    | other -> failwithf "malformed provenance was not refused: %A" other

[<Fact>]
let ``obligations and unknown effects record who created and who settled them without changing their meaning`` () =
    let effect = ok (ExternalEffectId.create "effect-1")
    let attempted = requester agentA (Some "EXE-20260926T080000000Z-aaaa0001")

    match ExternalEffect.recordOutcome reconcile effect now (ExternalEffectOutcome.Unknown "timeout") with
    | ReconciliationRequired(_, obligation) ->
        let attributed = Attributed.withBlock (ok (Requester.toBlock "reconcile-effect-1" now None attempted)) obligation

        let requirement = authorizeFastPath |> TransitionRequirement.requiringObligations [ reconcile ]

        let context obligations =
            { contextFor unclassified MechanicalPropagation with
                Obligations = Attributed.records obligations }

        // Outstanding whoever created it.
        match Transition.evaluate requirement (context [ attributed ]) with
        | TransitionRefused [ UnsatisfiedObligation [ id ] ] -> Assert.Equal(reconcile, id)
        | other -> failwithf "expected the obligation to block, got %A" other

        // A human settles it: the contribution is recorded, the originator
        // stays the agent, and only the state change (not the identity)
        // discharges the obligation.
        let settledBy = contribution "CTB-20260926-5f2e19aa" [ "resolved" ] "2026-09-26T09:00:00.000Z" (Actor.human "kevin")

        match Attributed.contribute settledBy attributed with
        | Ok recorded ->
            match Transition.evaluate requirement (context [ recorded ]) with
            | TransitionRefused [ UnsatisfiedObligation _ ] -> ()
            | other -> failwithf "a resolving identity discharged an open obligation: %A" other

            let satisfied = recorded |> Attributed.map (Obligation.satisfy "observed the remote system" now)

            match Transition.evaluate requirement (context [ satisfied ]) with
            | TransitionAllowed _ -> ()
            | other -> failwithf "expected the satisfied obligation to clear, got %A" other

            match satisfied.Provenance with
            | Some(Understood block) ->
                Assert.Equal(Some "EXE-20260926T080000000Z-aaaa0001", ProvenanceBlock.originator block |> Option.map (fun o -> o.Key))
                Assert.Equal<string list>([ "CTB-20260926-5f2e19aa" ], ProvenanceBlock.withRole "resolved" block |> List.map (fun o -> o.Key))
            | other -> failwithf "provenance was lost: %A" other
        | Error error -> failwithf "recording the resolution was refused: %s" error
    | other -> failwithf "an unknown effect must require reconciliation, got %A" other

// ------------------------------------------- contract revision 1.1 rules

let private t (minute: int) = sprintf "2026-09-26T08:%02d:00.000Z" minute

let private appendRaw key (contribution: JsonValue) block =
    ProvenanceBlock.appendJson key contribution block

let private entry (operations: string list) (at: string) (actor: Actor) (extra: (string * JsonValue) list) =
    JObject(
        [ "operations", JArray(operations |> List.map JString)
          "at", JString at
          "actor", Actor.encode actor ]
        @ extra
    )

let private refusedWith (fragment: string) result =
    match result with
    | Error(message: string) -> Assert.Contains(fragment, message)
    | Ok(block: ProvenanceBlock, _) -> failwithf "accepted: %s" (render (ProvenanceBlock.toJson block))

[<Fact>]
let ``rule 1: keys, kinds and codes with a trailing newline do not match`` () =
    Assert.Equal(InvalidKey, ContributionKey.kind "EXE-1\n")
    Assert.Equal(InvalidKey, ContributionKey.kind "EXT-vigila.run-7\n")
    Assert.Equal(None, ActorKind.tryParse "agent\n")
    Assert.Equal(None, ActorKind.tryParse "x-robot\n")

    match Requester.create (Actor.human "kevin") (Some "EXE-1\n") with
    | Error _ -> ()
    | Ok accepted -> failwithf "accepted a key with a trailing newline: %A" accepted

[<Fact>]
let ``rule 4: an append never admits a credential, a backdated contribution, or a second originator`` () =
    let created = appended (contribution "EXE-1" [ "created" ] (t 30) agentA) ProvenanceBlock.empty

    appendRaw "CTB-20260926-5f2e19aa" (entry [ "reviewed" ] (t 35) (Actor.human "ghp_0123456789abcdefghijABCDEFGHIJ0123") []) created
    |> refusedWith "credential-like"

    appendRaw "EXE-2" (entry [ "modified" ] (t 35) agentB [ "reason", JString "Bearer abcdefghijklmnopqrstuvwxyz012345" ]) created
    |> refusedWith "credential-like"

    appendRaw "EXE-2" (entry [ "modified" ] (t 5) agentB []) created
    |> refusedWith "precedes the recorded creation"

    // `created` merged into a later entry when earlier history exists.
    let history =
        ProvenanceBlock.empty
        |> appended (contribution "EXE-1" [ "modified" ] (t 0) agentA)
        |> appended (contribution "EXE-2" [ "modified" ] (t 5) agentB)

    appendRaw "EXE-2" (entry [ "created" ] (t 6) agentB []) history
    |> refusedWith "originator"

[<Fact>]
let ``rule 5: an unknown actor cannot extend a known actor's entry, and a merge keeps incoming fields and the latest time`` () =
    let created = appended (contribution "EXT-run.7" [ "created" ] (t 0) agentB) ProvenanceBlock.empty
    let unknownAgent = Actor.agent "unknown" None None None

    appendRaw "EXT-run.7" (entry [ "transformed" ] (t 5) unknownAgent []) created
    |> refusedWith "unknown identity"

    let human = Actor.human "kevin"

    let first =
        match appendRaw "CTB-1" (entry [ "created" ] (t 0) human [ "evidence", JArray [ JString "e1" ] ]) ProvenanceBlock.empty with
        | Ok(block, _) -> block
        | Error error -> failwith error

    match appendRaw "CTB-1" (entry [ "modified" ] (t 1) human [ "last", JString(t 9); "x-ticket", JString "T-9" ]) first with
    | Ok(block, true) ->
        let merged = get "CTB-1" (get "contributions" (ProvenanceBlock.toJson block))
        Assert.Equal(JString "T-9", get "x-ticket" merged)
        Assert.Equal(JString(t 9), get "last" merged)
        Assert.Equal(JString(t 0), get "at" merged)
        Assert.Equal(JArray [ JString "e1" ], get "evidence" merged)
        Assert.Equal(JArray [ JString "created"; JString "modified" ], get "operations" merged)
    | other -> failwithf "expected a changed block, got %A" other

[<Fact>]
let ``rule 2: a sub-millisecond difference does not order two contributions`` () =
    // Both instants truncate to the same millisecond, so neither precedes
    // the other and the creation is not "late".
    let block =
        ProvenanceBlock.empty
        |> appended (contribution "EXE-1" [ "modified" ] "2026-09-26T08:00:00.0009Z" agentA)

    match ProvenanceBlock.append (contribution "EXE-2" [ "created" ] "2026-09-26T08:00:00.0001Z" agentB) block with
    | Ok _ -> ()
    | Error error -> failwithf "sub-millisecond ordering was applied: %s" error

[<Fact>]
let ``rule 6: operation keys are escaped injectively`` () =
    Assert.Equal(Ok "EXT-op.op_201", ContributionKey.ofOperation "op 1")
    Assert.Equal(Ok "EXT-op.gh_2f99", ContributionKey.ofOperation "gh/99")
    Assert.Equal(Ok "EXT-op.req-1", ContributionKey.ofOperation "req-1")

    let keys = [ "a/b"; "a:b"; "a_b"; "a-b"; "a_2fb"; "é" ] |> List.map (ContributionKey.ofOperation >> ok)
    Assert.Equal(keys.Length, (List.distinct keys).Length)

    for key in keys do
        Assert.Equal(ForeignExecution "op", ContributionKey.kind key)

[<Fact>]
let ``rule 8: Ordo never takes a requester or an execution from the environment`` () =
    let variables = items (get "variables" (fixture "identity-environment.json")) |> List.map text
    Assert.Contains("ROS_EXECUTION_ID", variables)

    let saved = variables |> List.map (fun name -> name, Environment.GetEnvironmentVariable name)

    try
        for name in variables do
            Environment.SetEnvironmentVariable(name, if name = "ROS_ACTOR_KIND" then "agent" else "EXE-20260926T080000000Z-ffff0001")

        let request = requestFor changeClassContract unclassified fullEvidence
        Assert.Equal(None, request.RequestedBy)

        let record = run (answering (ChoiceSelected("mechanical-propagation", None, None))) request
        Assert.Equal(None, record.Observation.RequestProvenance)

        match Transition.evaluateRequest authorizeFastPath { Context = contextFor unclassified MechanicalPropagation; RequestedBy = None } with
        | TransitionAllowed authorization -> Assert.Equal(None, authorization.RequestedBy)
        | other -> failwithf "expected the change to be allowed, got %A" other
    finally
        for name, value in saved do
            Environment.SetEnvironmentVariable(name, value)

// ------------------------------------------- contract revision 1.2

/// Lone surrogates built from code units: the F# compiler replaces an
/// unpaired `\uD800` escape in a string literal with U+FFFD.
let private loneHigh = string (char 0xD800)
let private loneLow = string (char 0xDC00)
let private highOfPair = string (char 0xD83D)

let private verdictName verdict =
    match verdict with
    | Supported _ -> "supported"
    | Unsupported _ -> "unsupported"
    | Malformed _ -> "malformed"

/// Runs a check over every case of a vendored fixture and reports all
/// mismatches at once.
let private conform (name: string) (expected: int) (check: JsonValue -> string option) =
    let cases = items (get "cases" (fixture name))
    Assert.Equal(expected, cases.Length)
    let mismatches = cases |> List.choose (fun case -> check case |> Option.map (sprintf "%s: %s" (text (get "name" case))))
    Assert.True(mismatches.IsEmpty, String.Join(Environment.NewLine, mismatches))

[<Fact>]
let ``every Praxis text case reaches the same verdict, and classifying text never throws`` () =
    conform "text-cases.json" 14 (fun case ->
        let expected = text (get "expect" case)
        let actual = verdictName (ProvenanceBlock.classifyText (text (get "text" case)))
        if actual = expected then None else Some(sprintf "expected %s, got %s" expected actual))

[<Fact>]
let ``every Praxis envelope key case derives the same key or is rejected`` () =
    conform "envelope-key-cases.json" 12 (fun case ->
        let envelope =
            match tryMember "envelopeText" case with
            | Ok(Some(JString envelopeText)) -> parse envelopeText |> Result.mapError (sprintf "%A")
            | _ -> Ok(get "envelope" case)

        let actual = envelope |> Result.bind ContributionKey.ofEnvelopeV1

        match tryMember "error" case, actual with
        | Ok(Some(JBool true)), Error _ -> None
        | Ok(Some(JBool true)), Ok key -> Some(sprintf "expected a rejection, got %s" key)
        | _, Ok key when key = text (get "key" case) -> None
        | _, other -> Some(sprintf "expected %s, got %A" (text (get "key" case)) other))

[<Fact>]
let ``every Praxis lineage case is added or refused as the reference does`` () =
    conform "lineage-cases.json" 8 (fun case ->
        let expectedOk =
            match get "ok" case with
            | JBool value -> value
            | other -> failwithf "ok must be a boolean, got %A" other

        match ProvenanceBlock.addLineageJson (get "references" case) (get "block" case), expectedOk with
        | Ok(block, _), true ->
            let expected = get "derivedFrom" case |> items |> List.map text

            if ProvenanceBlock.derivedFrom block = expected then None
            else Some(sprintf "expected %A, got %A" expected (ProvenanceBlock.derivedFrom block))
        | Error _, false -> None
        | Ok(block, _), false -> Some(sprintf "expected a refusal, got %s" (render (ProvenanceBlock.toJson block)))
        | Error error, true -> Some(sprintf "expected lineage to be added, refused: %s" error))

let private smuggledOriginator =
    """{"schema":"praxis.provenance/1","contributions":{"EXE-A":{"operations":["created"],"at":"2026-09-26T08:00:00.000Z","actor":{"kind":"human","id":"mallory"}},"EXE-A":{"operations":["modified"],"at":"2026-09-26T09:00:00.000Z","actor":{"kind":"human","id":"alice"}}}}"""

[<Fact>]
let ``finding 5: a repeated member name is malformed at parse time, so no originator can be smuggled`` () =
    match parse smuggledOriginator with
    | Error(MalformedJson message) -> Assert.Contains("repeated", message)
    | other -> failwithf "Ordo's JSON reader accepted duplicate members: %A" other

    match ProvenanceBlock.classifyText smuggledOriginator with
    | Malformed _ -> ()
    | other -> failwithf "expected malformed, got %A" other

    // Duplicates are refused for every Ordo record, not only provenance.
    match parse """{"schema":"ordo.x","a":1,"nested":[{"b":1,"b":2}]}""" with
    | Error(MalformedJson message) -> Assert.Contains("$.nested[0].b", message)
    | other -> failwithf "expected a nested duplicate to be refused, got %A" other

    // The same name in different objects is not a duplicate.
    Assert.True((parse """{"a":{"id":1},"b":{"id":2}}""" |> Result.isOk))

    // A stored record with a duplicated provenance member never decodes.
    match parse ("""{"provenance":""" + smuggledOriginator + "}") with
    | Error _ -> ()
    | Ok document -> failwithf "a record with smuggled provenance parsed: %A" (readProvenance document)

[<Fact>]
let ``finding 10: an unpaired surrogate is malformed and parsing and classifying never throw`` () =
    let texts =
        [ "{\"schema\":\"praxis.provenance/2\",\"x-a\":\"\\ud800\"}"
          "{\"schema\":\"praxis.provenance/1\",\"contributions\":{},\"x-\\udc00\":1}"
          "{\"schema\":\"praxis.provenance/1\",\"contributions\":{},\"x-a\":\"" + loneHigh + "\"}"
          "{\"x-" + loneHigh + "\":1}"
          loneHigh
          "not json"
          "" ]

    for candidate in texts do
        match parse candidate with
        | Error(MalformedJson _) -> ()
        | other -> failwithf "expected MalformedJson for %A, got %A" candidate other

        Assert.Equal("malformed", verdictName (ProvenanceBlock.classifyText candidate))

    // A well-formed pair is content.
    Assert.Equal(Ok(JString "\U0001F600"), parse "\"\\ud83d\\ude00\"")

    // A lone surrogate inside an already-built value is malformed too.
    let built =
        JObject
            [ "schema", JString SchemaTag
              "contributions", JObject []
              "x-note", JString loneHigh ]

    match ProvenanceBlock.classify built with
    | Malformed [ problem ] -> Assert.Contains("unpaired UTF-16 surrogate", problem)
    | other -> failwithf "expected malformed, got %A" other

    Assert.Equal("malformed", verdictName (ProvenanceBlock.classify (JObject [ "schema", JString "praxis.provenance/2"; "x-" + loneLow, JInt 1L ])))

    match Requester.create (Actor.human ("kev" + loneHigh)) None with
    | Error problems -> Assert.Contains("unpaired", String.Join("; ", problems))
    | Ok accepted -> failwithf "accepted %A" accepted

    Assert.Equal(Error(IdentifierNotWellFormed("req-" + loneHigh)), DecisionRequestId.create ("req-" + loneHigh))

[<Fact>]
let ``finding 11: blank means ASCII whitespace only and credential patterns use ASCII classes`` () =
    let human id =
        JObject
            [ "schema", JString SchemaTag
              "contributions",
              JObject
                  [ "CTB-1",
                    JObject
                        [ "operations", JArray [ JString "created" ]
                          "at", JString "2026-09-26T08:00:00.000Z"
                          "actor", JObject [ "kind", JString "human"; "id", JString id ] ] ] ]

    for content in [ "\u0085"; "\ufeff"; "\u001c"; "\u00a0"; "\u2028" ] do
        Assert.Equal("supported", verdictName (ProvenanceBlock.classify (human content)))

    for blank in [ ""; " "; "\t\n\u000b\f\r " ] do
        Assert.Equal("malformed", verdictName (ProvenanceBlock.classify (human blank)))

    let token = "abcdefghijklmnopqrstuvwxyz"

    for credential in [ "Bearer " + token; "x bEaReR\t" + token; "\u00e9bearer " + token; "(bearer\u000b" + token + ")" ] do
        Assert.True(isCredentialLike credential, credential)

    for ordinary in
        [ "xbearer " + token
          "_bearer " + token
          "bearer\u0085" + token
          "bearer\u00a0" + token
          "bearer " + String('\u212a', 20)
          "BEARER short" ] do
        Assert.False(isCredentialLike ordinary, ordinary)

[<Fact>]
let ``rule 3: lineage is checked like a contribution and refusals leave nothing behind`` () =
    let block = ok (Requester.toBlock "req-1" now None (requester (Actor.human "kevin") None))

    ProvenanceBlock.addLineage [ "ghp_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" ] block |> refusedWith "credential-like"
    ProvenanceBlock.addLineage [ " \t" ] block |> refusedWith "non-empty"
    ProvenanceBlock.addLineage [ "RQ-" + loneHigh ] block |> refusedWith "unpaired"
    ProvenanceBlock.addLineageJson (JString "RQ-1") (ProvenanceBlock.toJson block) |> refusedWith "must be an array"
    ProvenanceBlock.addLineageJson (JArray [ JString "RQ-1" ]) (JObject [ "schema", JString "praxis.provenance/2" ]) |> refusedWith "unsupported"
    ProvenanceBlock.addLineageJson (JArray [ JString "RQ-1" ]) JNull |> refusedWith "malformed"

    // The block itself is unchanged by a refusal.
    Assert.Empty(ProvenanceBlock.derivedFrom block)

    match ProvenanceBlock.addLineage [ "RQ-1"; "RQ-2"; "RQ-1" ] block with
    | Ok(next, true) ->
        Assert.Equal<string list>([ "RQ-1"; "RQ-2" ], ProvenanceBlock.derivedFrom next)

        match ProvenanceBlock.addLineage [ "RQ-2" ] next with
        | Ok(again, false) -> Assert.Equal(ProvenanceBlock.toJson next, ProvenanceBlock.toJson again)
        | other -> failwithf "expected an unchanged block, got %A" other
    | other -> failwithf "expected lineage to be added, got %A" other

[<Fact>]
let ``rule 4: key segments escape per code point, escape '.', and refuse what cannot form a key`` () =
    Assert.Equal(Ok "_f0_9f_98_80", ContributionKey.escapeSegment "\U0001F600")
    Assert.Equal(Ok "a_2eb", ContributionKey.escapeSegment "a.b")
    Assert.Equal(Ok "a_5fb", ContributionKey.escapeSegment "a_b")
    Assert.Equal(Ok "EXT-op.req_2e1", ContributionKey.ofOperation "req.1")

    // Two astral ids never collide (they did when each surrogate half
    // became U+FFFD), and a dotted id never equals a namespaced key.
    Assert.NotEqual(ContributionKey.ofOperation "op-\U0001F600", ContributionKey.ofOperation "op-\U0001F601")

    let namespaced = ok (ContributionKey.escapeSegment "vigila") + "." + ok (ContributionKey.escapeSegment "7")
    Assert.NotEqual(Ok("EXT-run." + namespaced), ContributionKey.ofEnvelopeV1 (JObject [ "operationId", JString "o"; "actor", JObject [ "runId", JObject [ "state", JString "known"; "value", JString "vigila.7" ] ] ]))

    for unusable in [ ""; "op-" + highOfPair; loneLow ] do
        match ContributionKey.ofOperation unusable with
        | Error _ -> ()
        | Ok key -> failwithf "formed %s from %A" key unusable

    // A requester without an execution cannot be keyed by an unusable
    // operation id; the block is refused, never keyed by an invented id.
    let human = requester (Actor.human "kevin") None

    match Requester.toBlock "" now None human, Requester.toBlock ("op-" + highOfPair) now None human with
    | Error _, Error _ -> ()
    | other -> failwithf "expected both to be refused, got %A" other

    match Requester.toBlock "req.1" now None human with
    | Ok block ->
        Assert.Equal(Some "EXT-op.req_2e1", ProvenanceBlock.originator block |> Option.map (fun origin -> origin.Key))
        Assert.Equal(None, (ok (Requester.toBlock "req.1" now None human) |> Requester.ofBlock |> Option.bind (fun r -> r.Execution)))
    | Error error -> failwith error

    // A reason that would make the block malformed is refused, not stored.
    match Requester.toBlock "req-1" now (Some "Bearer abcdefghijklmnopqrstuvwxyz") human with
    | Error error -> Assert.Contains("credential-like", error)
    | Ok block -> failwithf "stored %s" (render (ProvenanceBlock.toJson block))

[<Fact>]
let ``rule 6: a stored provenance null is malformed, not absent`` () =
    match parse """{"provenance":null}""" |> Result.mapError (sprintf "%A") |> Result.bind (readProvenance >> Result.mapError (sprintf "%A")) with
    | Error problem -> Assert.Contains("provenance", problem)
    | Ok read -> failwithf "a null provenance was read as %A" read

    Assert.Equal(Ok None, readProvenance (JObject [ "id", JString "legacy" ]))
