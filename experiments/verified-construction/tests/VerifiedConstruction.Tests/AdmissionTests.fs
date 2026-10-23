/// Admission accepts the fixture and honest uncertainty; it refuses
/// mechanically invalid authority claims (VC-002 reqs. 1, 2, 5; R-ADM-1..3).
module VerifiedConstruction.Tests.AdmissionTests

open System.Text.Json.Nodes
open Xunit
open Echelon.Construct.Handoff
open Echelon.Construct.Admission
open VerifiedConstruction.Tests.Fixtures

let private admitText text = readAndAdmit text

let private errorsOf text =
    match admitText text with
    | Ok _ -> failwith "expected the handoff to be refused"
    | Error e -> e

let private assertAdmitted text =
    match admitText text with
    | Ok _ -> ()
    | Error e -> failwithf "expected admission, got %A" e

// ---- Accepted -----------------------------------------------------------------

[<Fact>]
let ``the fixture handoff is admitted`` () = assertAdmitted (handoffText ())

[<Fact>]
let ``R-ADM-1: an unresolved question with refuse-and-record is valid input`` () =
    let admitted = admitText (handoffText ()) |> ok "admit"
    let q = (handoffOf admitted).PolicyQuestions |> List.exactlyOne
    Assert.Equal("Q-FIN-001", q.Id)
    Assert.Equal(PermittedOutcome.RefuseAndRecord, q.PermittedOutcome)

[<Fact>]
let ``R-ADM-1: a defer-scope question is also valid input`` () =
    mutate (fun d -> (record d "policyQuestions" "Q-FIN-001")["permittedOutcome"] <- JsonValue.Create "defer-scope")
    |> assertAdmitted

[<Fact>]
let ``R-ADM-2: a question raised by the agent itself is valid input`` () =
    mutate (fun d ->
        let by = JsonObject()
        by["kind"] <- JsonValue.Create "agent"
        by["id"] <- JsonValue.Create "anthropic/claude-code"
        (record d "policyQuestions" "Q-FIN-001")["raisedBy"] <- by)
    |> assertAdmitted

[<Fact>]
let ``R-ADM-3: a proposed presentation decision needs no human authorization`` () =
    mutate (fun d ->
        let p = JsonObject()
        p["id"] <- JsonValue.Create "DEC-UI-001"
        p["statement"] <- JsonValue.Create "Refusal codes are shown in upper snake case."
        p["status"] <- JsonValue.Create "proposed"
        p["kind"] <- JsonValue.Create "presentation"
        p["owner"] <- JsonValue.Create "product"
        p["requirementIds"] <- jsonArray [ "REQ-INV-002" ]
        let s = JsonObject()
        s["path"] <- JsonValue.Create "sources/finance-decisions.md"
        s["revision"] <- JsonValue.Create "fixture-v1"
        p["source"] <- s
        p["supersedes"] <- jsonArray []
        match d["decisions"] with
        | null -> failwith "decisions"
        | a -> a.AsArray().Add p)
    |> assertAdmitted

// ---- Refused ------------------------------------------------------------------

[<Fact>]
let ``an accepted decision without authorizedBy is refused`` () =
    let errors = mutate (fun d -> (record d "decisions" "DEC-FIN-001").Remove "authorizedBy" |> ignore) |> errorsOf
    Assert.Contains(AcceptedDecisionWithoutHumanAuthorization("DEC-FIN-001", None), errors)

[<Fact>]
let ``an accepted decision authorized by an agent is refused`` () =
    let errors =
        mutate (fun d ->
            match (record d "decisions" "DEC-FIN-001")["authorizedBy"] with
            | null -> failwith "authorizedBy"
            | a -> a["kind"] <- JsonValue.Create "agent")
        |> errorsOf
    Assert.Contains(AcceptedDecisionWithoutHumanAuthorization("DEC-FIN-001", Some ActorKind.Agent), errors)

[<Fact>]
let ``an accepted decision without authorizedAt is refused`` () =
    let errors = mutate (fun d -> (record d "decisions" "DEC-FIN-002").Remove "authorizedAt" |> ignore) |> errorsOf
    Assert.Contains(AcceptedDecisionWithoutAuthorizationTime "DEC-FIN-002", errors)

[<Fact>]
let ``an observation claiming authority is refused`` () =
    let errors = mutate (fun d -> (record d "observations" "OBS-LEGACY-001")["authority"] <- JsonValue.Create "accepted") |> errorsOf
    Assert.Equal<AdmissionError list>([ ObservationClaimsAuthority("OBS-LEGACY-001", "accepted") ], errors)

[<Fact>]
let ``a policy question cited as a decision's requirement is refused`` () =
    let errors = mutate (fun d -> (record d "decisions" "DEC-FIN-001")["requirementIds"] <- jsonArray [ "REQ-INV-002"; "Q-FIN-001" ]) |> errorsOf
    Assert.Contains(QuestionReferencedAsRule("Q-FIN-001", "DEC-FIN-001.requirementIds"), errors)

[<Fact>]
let ``an invariant derived from a policy question is refused`` () =
    let errors = mutate (fun d -> (record d "invariants" "INV-INV-004")["derivedFrom"] <- jsonArray [ "REQ-INV-001"; "Q-FIN-001" ]) |> errorsOf
    Assert.Contains(QuestionReferencedAsRule("Q-FIN-001", "INV-INV-004.derivedFrom"), errors)

[<Fact>]
let ``a question marked resolved is refused`` () =
    let errors = mutate (fun d -> (record d "policyQuestions" "Q-FIN-001")["status"] <- JsonValue.Create "resolved") |> errorsOf
    Assert.Contains(QuestionNotUnresolved("Q-FIN-001", "resolved"), errors)

[<Fact>]
let ``a supersession cycle is refused`` () =
    let errors = mutate (fun d -> (record d "decisions" "DEC-FIN-000")["supersedes"] <- jsonArray [ "DEC-FIN-001" ]) |> errorsOf
    Assert.Contains(SupersessionCycle [ "DEC-FIN-000"; "DEC-FIN-001" ], errors)

[<Fact>]
let ``an invariant citing a superseded decision as current is refused`` () =
    let errors = mutate (fun d -> (record d "invariants" "INV-INV-003")["derivedFrom"] <- jsonArray [ "REQ-INV-002"; "DEC-FIN-000" ]) |> errorsOf
    Assert.Contains(SupersededCitedAsCurrent("DEC-FIN-000", "INV-INV-003"), errors)

[<Fact>]
let ``a superseded decision that does not name its successor is refused`` () =
    let errors = mutate (fun d -> (record d "decisions" "DEC-FIN-000").Remove "supersededBy" |> ignore) |> errorsOf
    Assert.Contains(InconsistentSupersession("DEC-FIN-000", "status is superseded but supersededBy is missing"), errors)

[<Fact>]
let ``an unknown reference is refused`` () =
    let errors = mutate (fun d -> (record d "decisions" "DEC-FIN-002")["requirementIds"] <- jsonArray [ "REQ-INV-009" ]) |> errorsOf
    Assert.Contains(UnknownReference("REQ-INV-009", "DEC-FIN-002"), errors)

[<Fact>]
let ``an unknown member is refused by the strict reader`` () =
    let errors = mutate (fun d -> (record d "decisions" "DEC-FIN-001")["approved"] <- JsonValue.Create true) |> errorsOf
    Assert.Contains(Malformed("$.decisions[1].approved", "unknown member"), errors)

[<Fact>]
let ``an unknown decision kind is refused by the strict reader`` () =
    let errors = mutate (fun d -> (record d "decisions" "DEC-FIN-001")["kind"] <- JsonValue.Create "override") |> errorsOf
    Assert.True(errors |> List.exists (function Malformed("$.decisions[1].kind", _) -> true | _ -> false), sprintf "%A" errors)

[<Fact>]
let ``several violations are reported together`` () =
    let errors =
        mutate (fun d ->
            (record d "decisions" "DEC-FIN-001").Remove "authorizedBy" |> ignore
            (record d "observations" "OBS-LEGACY-002")["authority"] <- JsonValue.Create "accepted")
        |> errorsOf
    Assert.Contains(AcceptedDecisionWithoutHumanAuthorization("DEC-FIN-001", None), errors)
    Assert.Contains(ObservationClaimsAuthority("OBS-LEGACY-002", "accepted"), errors)
