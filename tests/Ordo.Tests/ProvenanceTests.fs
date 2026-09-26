/// Requester provenance: who asked is recorded, and nothing that decides
/// reads it (ORDO-NEXT-07, DF-SDE-2026-D68A).
///
/// The invariance tests run every check over a small closed set of
/// requesters (agent, human, automation, unknown, an `x-` extension, and
/// none) and assert identical verdicts. The set is exhaustive over the actor
/// kinds, which is the dimension that could plausibly leak into authority.
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
open Ordo.Core.Provenance
open Ordo.Core.StateIdentity
open Ordo.Core.Transition
open Ordo.Core.Wire
open Ordo.Decisions.Outcome
open Ordo.Decisions.Provider
open Ordo.Decisions.Request
open Ordo.Decisions.Resolve
open Ordo.Decisions.Gate
open Ordo.Decisions.Observation
open Ordo.Tests.Fixtures

// ------------------------------------------------------------ requesters

let private exe = ok (ExecutionKey.create "EXE-20260926T080000000Z-a1a1a1a1")

let private agent =
    ok (Requester.create (ok (Actor.agent "anthropic/claude-code" "anthropic" "unknown" "claude-code")) (Some exe))

let private human =
    ok (Requester.create (ok (Actor.human "kevin")) (Some(ok (ExecutionKey.create "CTB-20260926-5f2e19aa"))))

let private automation =
    ok (Requester.create (ok (Actor.automation "github/github-actions" "github" "unknown" "github-actions")) (Some(ok (ExecutionKey.foreign "ordo" "gh-run-9001"))))

let private unknownRequester = ok (Requester.create Actor.unknown None)

let private extension =
    ok (
        Requester.create
            (ok (Actor.create (ActorKind.Extension "x-bot") "acme/triage-bot" (Some "acme") (Some "unknown") (Some "triage") [ "x-team", JString "platform" ]))
            None
    )

let private everyRequester: Requester option list =
    [ None; Some agent; Some human; Some automation; Some unknownRequester; Some extension ]

let private roundTrip (encode: 'a -> JsonValue) (decode: JsonValue -> Result<'a, WireError>) (value: 'a) =
    match parse (render (encode value)) with
    | Error error -> failwithf "did not render as JSON: %A" error
    | Ok document -> decode document

let private parsed (text: string) =
    match parse text with
    | Ok value -> value
    | Error error -> failwithf "test JSON did not parse: %A" error

// ---------------------------------------------------------------- actor

[<Fact>]
let ``every actor kind round-trips through the wire unchanged`` () =
    for requester in everyRequester |> List.choose id do
        match roundTrip encodeActor decodeActor requester.Actor with
        | Ok decoded -> Assert.Equal(requester.Actor, decoded)
        | Error error -> failwithf "actor %s did not round-trip: %A" (Actor.describe requester.Actor) error

        match roundTrip encodeRequester decodeRequester requester with
        | Ok decoded -> Assert.Equal(requester, decoded)
        | Error error -> failwithf "requester did not round-trip: %A" error

[<Fact>]
let ``the actor is written in contract key order with absent attributes omitted`` () =
    let names value =
        match value with
        | JObject members -> members |> List.map fst
        | other -> failwithf "expected an object, got %A" other

    Assert.Equal<string list>([ "kind"; "id"; "provider"; "model"; "runtime" ], names (encodeActor agent.Actor))
    Assert.Equal<string list>([ "kind"; "id" ], names (encodeActor human.Actor))
    Assert.Equal<string list>([ "kind"; "id"; "provider"; "model"; "runtime"; "x-team" ], names (encodeActor extension.Actor))

    Assert.Equal(
        """{"kind":"agent","id":"anthropic/claude-code","provider":"anthropic","model":"unknown","runtime":"claude-code"}""",
        (render (encodeActor agent.Actor)).Replace("\n", "").Replace("  ", "").Replace(": ", ":")
    )

    Assert.Equal<string list>([ "actor"; "execution" ], names (encodeRequester agent))
    Assert.Equal(Ok(Some JNull), tryMember "execution" (encodeRequester unknownRequester))

[<Fact>]
let ``unknown actor fields are preserved verbatim and in order`` () =
    let document =
        parsed """{"kind":"agent","id":"a/b","x-team":"platform","provider":"p","model":"unknown","runtime":"r","attestation":{"type":"x-future","value":[1,2]}}"""

    match decodeActor document with
    | Error error -> failwithf "an actor with unknown fields was refused: %A" error
    | Ok actor ->
        Assert.Equal<(string * JsonValue) list>(
            [ "x-team", JString "platform"
              "attestation", JObject [ "type", JString "x-future"; "value", JArray [ JInt 1L; JInt 2L ] ] ],
            actor.Extensions
        )

        // Re-encoding keeps every field; only the modelled ones move into
        // contract order.
        Assert.Equal(renderCanonical document, renderCanonical (encodeActor actor))

[<Fact>]
let ``an unknown kind token is refused loudly and an x- kind is an extension`` () =
    match decodeActor (parsed """{"kind":"robot","id":"r2"}""") with
    | Error(UnknownVariant(_, "robot")) -> ()
    | other -> failwithf "expected the unknown kind to be refused, got %A" other

    for bad in [ "Agent"; "x-"; "x-Bot"; "x_bot"; "" ] do
        Assert.Equal(None, ActorKind.fromWire bad)

    match decodeActor (parsed """{"kind":"x-bot","id":"acme/triage-bot"}""") with
    | Ok actor -> Assert.Equal(ActorKind.Extension "x-bot", actor.Kind)
    | Error error -> failwithf "an x- kind was refused: %A" error

[<Fact>]
let ``malformed actors are refused rather than repaired`` () =
    let refuses (text: string) =
        match decodeActor (parsed text) with
        | Error _ -> ()
        | Ok actor -> failwithf "expected refusal of %s, got %A" text actor

    refuses """{"kind":"agent","id":"a/b","provider":"p","runtime":"r"}"""
    refuses """{"kind":"agent","id":"","provider":"p","model":"m","runtime":"r"}"""
    refuses """{"kind":"agent","id":"a/b","provider":"p","model":"","runtime":"r"}"""
    refuses """{"kind":"human"}"""
    refuses """{"id":"kevin"}"""
    refuses """{"kind":"human","id":"kevin","kind":"agent"}"""
    refuses """{"kind":"human","id":"kevin","provider":null}"""
    refuses """{"kind":"agent","id":"a/b","provider":"p","model":"sk-ant-api03-AAAAAAAAAAAAAAAAAAAAAAAA","runtime":"r"}"""
    refuses """{"kind":"human","id":"kevin","x-note":{"auth":"Bearer abcdefghijklmnopqrstuvwxyz"}}"""
    refuses """{"kind":"human","id":"password=hunter2hunter2"}"""

    Assert.Equal(Error(ReservedExtensionField "id"), Actor.create ActorKind.Human "kevin" None None None [ "id", JString "x" ] |> Result.map ignore)

[<Fact>]
let ``the credential guard recognises common shapes and leaves identities alone`` () =
    for secret in
        [ "sk-proj-abcdefghijklmnop1234"
          "ghp_abcdefghijklmnopqrstuvwxyz"
          "github_pat_abcdefghijklmnopqrstuvwxyz"
          "xoxb-1234567890-abc"
          "AKIAABCDEFGHIJKLMNOP"
          "AIzaabcdefghijklmnopqrstuvwxyz0123456"
          "-----BEGIN RSA PRIVATE KEY-----"
          "bearer abcdefghijklmnop1234"
          "eyJhbGciOiJIUzI1.eyJzdWIiOiIxMjM0.SflKxwRJSMeKKF2QT4"
          "api_key: 12345678abc" ] do
        Assert.True(Credentials.looksLikeCredential secret, secret)

    for identity in [ "anthropic/claude-code"; "openai/codex"; "kevin"; "unknown"; "github/github-actions"; "task-sk-1"; "secret-agent" ] do
        Assert.False(Credentials.looksLikeCredential identity, identity)

// ------------------------------------------------------------- execution

[<Fact>]
let ``execution keys are EXE or CTB keys and an agent must name its run`` () =
    for good in [ "EXE-20260926T080000000Z-a1a1a1a1"; "EXE-dokimos.gh-run-9001"; "CTB-20260926-5f2e19aa" ] do
        Assert.Equal(Ok good, ExecutionKey.create good |> Result.map ExecutionKey.value)

    for bad in [ ""; "RUN-123"; "EXE-"; "exe-1"; "EXE-a b"; "EXE-1/2" ] do
        Assert.Equal(Error(InvalidExecutionKey bad), ExecutionKey.create bad |> Result.map ignore)

    let claude = ok (Actor.agent "anthropic/claude-code" "anthropic" "unknown" "claude-code")
    Assert.Equal(Error AgentRequiresExecution, Requester.create claude None |> Result.map ignore)
    Assert.Equal(Error AgentRequiresExecution, Requester.create claude (Some(ok (ExecutionKey.create "CTB-1"))) |> Result.map ignore)

    Assert.Equal(Ok "EXE-ordo.run-7", ExecutionKey.foreign "ordo" "run-7" |> Result.map ExecutionKey.value)

    for system, run in [ "Ordo", "1"; "1ordo", "1"; "ordo", ""; "ordo", ".hidden"; "ordo", "a b" ] do
        Assert.Equal(Error(InvalidForeignRun(system, run)), ExecutionKey.foreign system run |> Result.map ignore)

[<Fact>]
let ``a requester wrapper with an unexpected member is refused`` () =
    match decodeRequester (parsed """{"actor":{"kind":"human","id":"kevin"},"execution":null,"role":"admin"}""") with
    | Error(InvalidField(path, _)) -> Assert.Equal("$.requestedBy.role", path)
    | other -> failwithf "expected refusal, got %A" other

// ------------------------------------------------- identity from variables

let private lookupFrom (pairs: (string * string) list) =
    let consulted = Collections.Generic.List<string>()

    let lookup name =
        consulted.Add name
        pairs |> List.tryFind (fst >> (=) name) |> Option.map snd

    lookup, consulted

[<Fact>]
let ``identity variables resolve without guessing`` () =
    let nothing, _ = lookupFrom []

    match Requester.fromIdentityVariables nothing None with
    | Ok requester ->
        Assert.Equal(Actor.unknown, requester.Actor)
        Assert.Equal(None, requester.Execution)
    | Error error -> failwithf "an empty environment must resolve to unknown, got %A" error

    let ci, _ = lookupFrom [ "GITHUB_ACTIONS", "true" ]

    match Requester.fromIdentityVariables ci (Some("ordo", "gh-run-9001")) with
    | Ok requester ->
        Assert.Equal("""{"kind":"automation","id":"github/github-actions","provider":"github","model":"unknown","runtime":"github-actions"}""", (render (encodeActor requester.Actor)).Replace("\n", "").Replace("  ", "").Replace(": ", ":"))
        Assert.Equal(Some "EXE-ordo.gh-run-9001", requester.Execution |> Option.map ExecutionKey.value)
    | Error error -> failwithf "GitHub Actions default failed: %A" error

    let declared, consulted =
        lookupFrom
            [ "ROS_ACTOR_KIND", "agent"
              "ROS_TELEMETRY_PROVIDER", "openai"
              "ROS_TELEMETRY_RUNTIME", "codex"
              "ROS_EXECUTION_ID", "EXE-20260926T090000000Z-b2b2b2b2"
              "GITHUB_ACTIONS", "true"
              "OPENAI_API_KEY", "sk-proj-abcdefghijklmnop1234" ]

    match Requester.fromIdentityVariables declared (Some("ordo", "ignored-when-propagated")) with
    | Ok requester ->
        Assert.Equal(ActorKind.Agent, requester.Actor.Kind)
        Assert.Equal("openai/codex", requester.Actor.Id)
        Assert.Equal(Some "unknown", requester.Actor.Model)
        Assert.Equal(Some "EXE-20260926T090000000Z-b2b2b2b2", requester.Execution |> Option.map ExecutionKey.value)
    | Error error -> failwithf "declared agent identity failed: %A" error

    Assert.True(consulted |> Seq.forall (fun name -> List.contains name Requester.identityVariables), sprintf "consulted %A" (List.ofSeq consulted))

    let agentWithoutRun, _ = lookupFrom [ "ROS_ACTOR_KIND", "agent" ]
    Assert.Equal(Error AgentRequiresExecution, Requester.fromIdentityVariables agentWithoutRun None |> Result.map ignore)

    let badKind, _ = lookupFrom [ "ROS_ACTOR_KIND", "robot" ]
    Assert.Equal(Error(UnknownActorKind "robot"), Requester.fromIdentityVariables badKind None |> Result.map ignore)

    let humanWithAttributes, _ = lookupFrom [ "ROS_ACTOR_KIND", "human"; "ROS_ACTOR", "kevin"; "ROS_TELEMETRY_PROVIDER", "anthropic" ]

    match Requester.fromIdentityVariables humanWithAttributes None with
    | Ok requester -> Assert.Equal(ok (Actor.human "kevin"), requester.Actor)
    | Error error -> failwithf "human identity failed: %A" error

// -------------------------------------------------------------- invariance

type private Verdict =
    | Allowed of name: string * state: StateFingerprint * policy: Ordo.Core.Policy.PolicyIdentity * at: DateTimeOffset
    | Refused of TransitionFailure list
    | Review of string * TransitionFailure list

let private verdictOf evaluation =
    match evaluation with
    | TransitionAllowed authorization -> Allowed(authorization.Name, authorization.State, authorization.Policy, authorization.At)
    | TransitionRefused failures -> Refused failures
    | TransitionRequiresHumanReview(reason, outstanding) -> Review(reason, outstanding)

let private unclassified = snapshotOf (siteState "Transition.fs" Unclassified "r1")

/// Contexts that exercise every verdict shape: allowed, refused for a
/// missing capability, for missing evidence, for policy, for stale state,
/// and held for human review.
let private contexts =
    let full = contextFor unclassified MechanicalPropagation

    [ "allowed", full
      "missing capability", { full with Held = CapabilitySet.empty }
      "missing evidence", { full with Available = [] }
      "policy refuses", contextFor unclassified SemanticChange
      "human review", contextFor unclassified BoundaryChange
      "stale", { full with FormedAgainst = (snapshotOf (siteState "Transition.fs" Unclassified "r0")).Fingerprint } ]

[<Fact>]
let ``who asked never changes a transition verdict or a capability outcome`` () =
    for label, context in contexts do
        let baseline = Transition.evaluate authorizeFastPath { context with RequestedBy = None } |> verdictOf

        for requester in everyRequester do
            let evaluation = Transition.evaluate authorizeFastPath { context with RequestedBy = requester }
            Assert.True((baseline = verdictOf evaluation), sprintf "%s: requester %A changed the verdict" label requester)

            // The capability check itself is identity-free: the same held set
            // answers the same way whoever is asking.
            Assert.Equal<CapabilityId list>(
                CapabilitySet.missing authorizeFastPath.RequiredCapabilities context.Held,
                CapabilitySet.missing authorizeFastPath.RequiredCapabilities { context with RequestedBy = requester }.Held
            )

            // The authorisation records who asked, verbatim, and nothing else
            // about it.
            match evaluation with
            | TransitionAllowed authorization -> Assert.Equal(requester, authorization.RequestedBy)
            | _ -> ()

[<Fact>]
let ``an authorisation never exists for an actor alone`` () =
    // Every requester, with nothing else satisfied, is refused for exactly the
    // same reasons as no requester at all.
    let bare =
        { contextFor unclassified MechanicalPropagation with
            Held = CapabilitySet.empty
            Available = [] }

    for requester in everyRequester do
        match Transition.evaluate authorizeFastPath { bare with RequestedBy = requester } with
        | TransitionRefused failures ->
            Assert.Contains(MissingCapability [ authorizeVerificationPath.Id ], failures)
            Assert.Contains(failures, fun failure -> match failure with MissingEvidence _ -> true | _ -> false)
        | other -> failwithf "requester %A obtained %A without capability or evidence" requester other

let private options =
    ResolveOptions.standard |> ResolveOptions.withClock testClock

let private run provider request =
    (Resolve.execute options provider request CancellationToken.None).GetAwaiter().GetResult()

let private withRequester requester request =
    match requester with
    | Some requester -> DecisionRequest.requestedBy requester request
    | None -> request

[<Fact>]
let ``who asked never changes what a provider sees or what a decision request resolves to`` () =
    for available in [ fullEvidence; [] ] do
        let request = requestFor changeClassContract unclassified available
        let baselineProviderRequest = Resolve.toProviderRequest options request
        let baselineRecord = run (answering (ChoiceSelected("mechanical-propagation", Some(confidenceOf 0.5), None))) request

        for requester in everyRequester do
            let asked = withRequester requester request
            Assert.Equal(baselineProviderRequest, Resolve.toProviderRequest options asked)
            Assert.Equal<RequirementCheck list>(DecisionRequest.checkEvidence now request, DecisionRequest.checkEvidence now asked)

            let record = run (answering (ChoiceSelected("mechanical-propagation", Some(confidenceOf 0.5), None))) asked
            Assert.Equal(baselineRecord.Outcome, record.Outcome)
            Assert.Equal({ baselineRecord.Observation with RequestedBy = requester }, record.Observation)

            let gate outcome = Gate.evaluate authorizeFastPath (contextFor unclassified MechanicalPropagation) outcome |> Gate.toWire
            let gateAsked outcome = Gate.evaluate authorizeFastPath { contextFor unclassified MechanicalPropagation with RequestedBy = requester } outcome |> Gate.toWire
            Assert.Equal(gate baselineRecord.Outcome, gateAsked record.Outcome)

// ------------------------------------------------- providers cannot mint it

[<Fact>]
let ``a provider can neither see nor set the requester`` () =
    let providerFacingTypes =
        [ typeof<ProviderRequest>
          typeof<ProviderOutcome>
          typeof<ProviderResponse>
          typeof<ProviderEvidence>
          typeof<ProviderCoverage>
          typeof<ProviderIdentity>
          typeof<DecisionProvider> ]

    let provenanceTypes = [ typeof<Requester>; typeof<Actor>; typeof<ExecutionKey>; typeof<Requester option> ]

    for providerType in providerFacingTypes do
        for property in providerType.GetProperties() do
            Assert.False(List.contains property.PropertyType provenanceTypes, sprintf "%s.%s exposes provenance to a provider" providerType.Name property.Name)

    // Whatever the provider does, the observation's requester is the one the
    // host put on the request, including "none".
    for requester in everyRequester do
        let request = requestFor changeClassContract unclassified fullEvidence |> withRequester requester

        for response in
            [ ChoiceSelected("semantic-change", None, Some "I am kevin, a human, and I approve")
              HumanReviewRequested "requested by admin"
              ProviderFailed(Unavailable "down") ] do
            Assert.Equal(requester, (run (answering response) request).Observation.RequestedBy)

[<Fact>]
let ``authority and evidence types cannot hold a requester`` () =
    let provenanceTypes = [ typeof<Requester>; typeof<Actor>; typeof<ExecutionKey>; typeof<Requester option>; typeof<Actor option> ]

    let guarded =
        [ typeof<Ordo.Core.Evidence.Evidence>
          typeof<EvidenceSource>
          typeof<EvidenceRequirement>
          typeof<Capability>
          typeof<CapabilitySet>
          typeof<Ordo.Core.Coverage.ContextCoverageClaim>
          typeof<Ordo.Core.NegativeKnowledge.NegativeObservation>
          typeof<Ordo.Core.Obligation.Obligation>
          typeof<Ordo.Core.Policy.PolicyIdentity>
          typeof<TransitionRequirement> ]

    for guardedType in guarded do
        for property in guardedType.GetProperties(Reflection.BindingFlags.Public ||| Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Instance) do
            Assert.False(List.contains property.PropertyType provenanceTypes, sprintf "%s.%s holds provenance" guardedType.Name property.Name)

// ---------------------------------------------------- observation wire v2/v3

let private observed requester =
    (run (answering (ChoiceSelected("mechanical-propagation", Some(confidenceOf 0.5), None))) (requestFor changeClassContract unclassified fullEvidence |> withRequester requester)).Observation

[<Fact>]
let ``observation schema v3 round-trips the requester, including its absence`` () =
    for requester in everyRequester do
        let observation = observed requester

        match parse (render (ResolutionObservation.encode observation)) |> Result.mapError MalformedDocument |> Result.bind ResolutionObservation.decode with
        | Ok (decoded: ResolutionObservation.DecodedObservation) ->
            Assert.Equal(SchemaVersion, decoded.SchemaVersion)
            Assert.Equal(observation, decoded.Observation)
        | Error error -> failwithf "v3 observation did not round-trip: %A" error

    Assert.Equal(Ok(Some JNull), tryMember "requestedBy" (ResolutionObservation.encode (observed None)))

/// A schema-v2 observation exactly as the pre-requester encoder wrote it.
let private legacyV2 =
    """{
  "schema": "ordo.resolution-observation",
  "schemaVersion": 2,
  "resolutionId": "res-1",
  "correlationId": null,
  "causedBy": null,
  "mode": "decide",
  "contractId": "sde.change-site-class",
  "contractVersion": 1,
  "requestId": "req-1",
  "stateFingerprint": "sha256:0000000000000000000000000000000000000000000000000000000000000000",
  "stateViewSchema": {
    "id": "sde.change-site-state",
    "version": 1
  },
  "coverage": [],
  "provider": {
    "id": "fake",
    "model": "scripted",
    "modelVersion": "1",
    "adapterVersion": "test"
  },
  "startedAt": "2026-09-18T12:00:00.0000000Z",
  "completedAt": "2026-09-18T12:00:00.0000000Z",
  "durationMilliseconds": 0,
  "outcome": "decided",
  "selectedChoice": "mechanical-propagation",
  "confidence": {
    "magnitude": 0.5,
    "provenance": "provider-reported"
  },
  "evidenceUsed": [
    "tool-diagnostic",
    "site-diff"
  ],
  "escalation": [],
  "transition": null,
  "policy": null,
  "usage": {
    "inputTokens": 100,
    "outputTokens": 20,
    "cachedInputTokens": null,
    "providerReportedCost": null
  },
  "transportRetries": 0,
  "experimentReference": null
}"""

[<Fact>]
let ``a v2 observation decodes with the requester absent and stays v2`` () =
    let document = parsed legacyV2

    match ResolutionObservation.decode document with
    | Error error -> failwithf "the v2 observation was refused: %A" error
    | Ok decoded ->
        Assert.Equal(2, decoded.SchemaVersion)
        Assert.Equal(None, decoded.Observation.RequestedBy)
        Assert.Equal("fake", decoded.Observation.Provider |> Option.map (fun p -> ProviderId.value p.Provider) |> Option.defaultValue "")

        match ResolutionObservation.encodeAtVersion decoded.SchemaVersion decoded.Observation with
        | Ok reencoded ->
            Assert.Equal(renderCanonical document, renderCanonical reencoded)
            Assert.Equal(Ok None, tryMember "requestedBy" reencoded)
        | Error error -> failwithf "the v2 observation could not be written back as v2: %A" error

[<Fact>]
let ``v2 cannot carry a requester and unknown observation versions are refused`` () =
    let withMember name value =
        match parsed legacyV2 with
        | JObject members -> JObject(members @ [ name, value ])
        | other -> other

    match ResolutionObservation.decode (withMember "requestedBy" (encodeRequester agent)) with
    | Error(InvalidField("$.requestedBy", _)) -> ()
    | other -> failwithf "a v2 record with requestedBy must be refused, got %A" other

    match ResolutionObservation.encodeAtVersion 2 (observed (Some agent)) with
    | Error(InvalidField("$.requestedBy", _)) -> ()
    | other -> failwithf "encoding a requester at v2 must be refused, got %A" other

    let atVersion version =
        match parsed legacyV2 with
        | JObject members -> JObject(members |> List.map (fun (name, value) -> if name = "schemaVersion" then name, JInt version else name, value))
        | other -> other

    for version in [ 1L; 4L ] do
        match ResolutionObservation.decode (atVersion version) with
        | Error(UnsupportedSchemaVersion(found, supported)) ->
            Assert.Equal(int version, found)
            Assert.Equal<int list>([ 2; 3 ], supported)
        | other -> failwithf "version %d must be refused, got %A" version other

    // v3 requires the member to be present; absence is spelled null.
    match ResolutionObservation.decode (atVersion 3L) with
    | Error(MalformedDocument(MissingMember "$.requestedBy")) -> ()
    | other -> failwithf "a v3 record without requestedBy must be refused, got %A" other

// -------------------------------------------- Praxis conformance fixtures

let private fixtureRoot = Path.Combine(AppContext.BaseDirectory, "fixtures", "praxis-provenance-record")

let private readFixture (relative: string) = parsed (File.ReadAllText(Path.Combine(fixtureRoot, relative)))

let private stringMember name value =
    match tryMember name value with
    | Ok(Some(JString text)) -> Some text
    | _ -> None

[<Fact>]
let ``vendored Praxis fixtures match their recorded digests`` () =
    let source = readFixture "SOURCE.json"
    Assert.Equal(Some "kemiller2002/praxis", stringMember "repository" source)
    Assert.Equal(Some "58cf46a", stringMember "commit" source)

    let recorded =
        match tryMember "files" source with
        | Ok(Some(JObject files)) -> files |> List.map (fun (path, digest) -> path, (match digest with JString d -> d | _ -> ""))
        | other -> failwithf "SOURCE.json has no files map: %A" other

    let present =
        Directory.GetFiles(fixtureRoot, "*", SearchOption.AllDirectories)
        |> Array.map (fun path -> Path.GetRelativePath(fixtureRoot, path).Replace('\\', '/'))
        |> Array.filter ((<>) "SOURCE.json")
        |> Array.sort
        |> List.ofArray

    Assert.Equal<string list>(recorded |> List.map fst |> List.sort, present)

    for path, digest in recorded do
        let actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtureRoot, path)))).ToLowerInvariant()
        Assert.True((digest = actual), sprintf "%s digest changed" path)

/// Every contribution key and actor in a record, including those inside
/// lineage snapshots, which Ordo's actor and execution-key codec can judge.
let rec private contributionsIn (record: JsonValue) : (string * JsonValue) list =
    let own =
        match tryMember "contributions" record with
        | Ok(Some(JObject entries)) ->
            entries
            |> List.map (fun (key, entry) ->
                match tryMember "actor" entry with
                | Ok(Some actor) -> key, actor
                | _ -> key, JNull)
        | _ -> []

    let sources =
        match tryMember "sources" record with
        | Ok(Some(JObject snapshots)) -> snapshots |> List.collect (snd >> contributionsIn)
        | _ -> []

    own @ sources

/// Ordo's verdict on one record, restricted to what Ordo implements: the
/// actor codec and the execution-key rules for a requester.
let private codecAccepts (record: JsonValue) =
    contributionsIn record
    |> List.forall (fun (key, actorJson) ->
        match decodeActor actorJson, ExecutionKey.create key with
        | Ok actor, Ok execution ->
            (Requester.create actor (Some execution) |> Result.isOk)
            // Lossless: the actor re-encodes to exactly what was read.
            && renderCanonical (encodeActor actor) = renderCanonical actorJson
        | _ -> false)

/// Invalid cases whose defect is in the actor or the contribution key: the
/// part of the contract Ordo implements. Ordo must refuse them.
let private actorLevelInvalid =
    set
        [ "invalid/agent-missing-model.json"
          "invalid/agent-without-execution.json"
          "invalid/bad-key.json"
          "invalid/unknown-kind.json"
          "invalid/empty-actor-id.json"
          "invalid/credential-in-actor.json" ]

/// Invalid cases whose defect is in the interchange record around the actor
/// (envelope, operations, timestamps, reasons, lineage). Ordo does not read or
/// write interchange records (RP-SDE-2026-DE93 non-requirements), so these
/// are outside its codec. They are listed so that a new fixture forces a
/// decision rather than silently falling outside the test.
let private recordLevelInvalid =
    set
        [ "invalid/wrong-contract.json"
          "invalid/bad-version.json"
          "invalid/two-created.json"
          "invalid/created-after-modified.json"
          "invalid/unknown-operation.json"
          "invalid/bad-timestamp.json"
          "invalid/missing-contributions.json"
          "invalid/credential-in-reason.json"
          "invalid/source-not-in-lineage.json"
          "invalid/self-lineage.json"
          "invalid/malformed-source-snapshot.json" ]

[<Fact>]
let ``Ordo's actor codec agrees with every Praxis conformance case it covers`` () =
    let manifest = readFixture "manifest.json"

    let cases =
        match tryMember "cases" manifest with
        | Ok(Some(JArray cases)) -> cases |> List.map (fun case -> stringMember "file" case |> Option.get, stringMember "expect" case |> Option.get)
        | other -> failwithf "manifest has no cases: %A" other

    Assert.NotEmpty cases

    let invalid = cases |> List.filter (snd >> (=) "invalid") |> List.map fst |> Set.ofList
    Assert.Equal<Set<string>>(invalid, Set.union actorLevelInvalid recordLevelInvalid)

    for file, expect in cases do
        let record = readFixture file

        match expect with
        | "valid"
        | "valid-unversioned" -> Assert.True(codecAccepts record, sprintf "%s should be accepted" file)
        | "invalid" when actorLevelInvalid.Contains file -> Assert.False(codecAccepts record, sprintf "%s should be refused" file)
        | "invalid" -> ()
        // An unsupported major is carried verbatim, never interpreted. Ordo
        // never interprets it: its actor shape is not the v1 actor.
        | "unsupported-version" -> Assert.Equal(Some "2", stringMember "version" record |> Option.map (fun v -> v.Split('.')[0]))
        | other -> failwithf "unexpected manifest expectation %s" other

[<Fact>]
let ``every actor in the Praxis successor and end-to-end fixtures survives Ordo's codec losslessly`` () =
    let manifest = readFixture "manifest.json"

    let successorFiles =
        match tryMember "successors" manifest with
        // Every `before` is a well-formed record. An `after` is only
        // well-formed when the pair expects preservation; a destructive
        // `after` may be deliberately malformed (for example a rewritten
        // snapshot that re-keys an agent by CTB).
        | Ok(Some(JArray pairs)) ->
            pairs
            |> List.collect (fun pair ->
                [ stringMember "before" pair
                  if stringMember "expect" pair = Some "preserved" then stringMember "after" pair ]
                |> List.choose id)
        | other -> failwithf "manifest has no successors: %A" other

    let e2eFiles =
        match tryMember "e2e" manifest |> Result.map (Option.map (tryMember "steps")) with
        | Ok(Some(Ok(Some(JArray steps)))) -> steps |> List.choose (stringMember "file")
        | other -> failwithf "manifest has no e2e steps: %A" other

    let supportedMajor record =
        stringMember "version" record |> Option.forall (fun version -> version.StartsWith("1.", StringComparison.Ordinal))

    let files = successorFiles @ e2eFiles |> List.distinct
    Assert.True(List.length files >= 20)

    for file in files do
        let record = readFixture file

        if supportedMajor record then
            Assert.True(codecAccepts record, sprintf "%s: an actor was refused or not preserved" file)
