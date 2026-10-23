/// Typed model and strict reader for `echelon.authority-handoff/0.1`.
///
/// The reader is structural only: it refuses unknown or duplicate members,
/// missing required members, wrong JSON types and unknown enumeration values,
/// each with its JSON path. It deliberately keeps two claims as raw text so
/// that admission (Admission.fs), not the reader, decides them: an
/// observation's `authority` and a policy question's `status`. Refusing them
/// here would hide *why* the handoff is inadmissible.
module Echelon.Construct.Handoff

open System
open Ordo.Core.Json

[<Literal>]
let SchemaName = "echelon.authority-handoff"

[<Literal>]
let SchemaVersion = "0.1"

[<RequireQualifiedAccess>]
type ActorKind =
    | Human
    | Agent
    | Automation

type Actor =
    { Kind: ActorKind
      Id: string
      Role: string option }

type SourceRef =
    { Path: string
      Locator: string option
      Revision: string
      Sha256: string option }

[<RequireQualifiedAccess>]
type RequirementStatus =
    | Accepted
    | Superseded

type Requirement =
    { Id: string
      Statement: string
      Status: RequirementStatus
      Owner: string
      Source: SourceRef
      Supersedes: string list
      SupersededBy: string option }

[<RequireQualifiedAccess>]
type DecisionStatus =
    | Accepted
    | Superseded
    | Proposed
    | Rejected

[<RequireQualifiedAccess>]
type DecisionKind =
    | BusinessRule
    | PolicyLimit
    | Exception
    | Boundary
    | DefaultRule
    | Interpretation
    | Presentation

type Decision =
    { Id: string
      Statement: string
      Status: DecisionStatus
      Kind: DecisionKind
      Owner: string
      AuthorizedBy: Actor option
      AuthorizedAt: string option
      RequirementIds: string list
      Source: SourceRef
      Supersedes: string list
      SupersededBy: string option
      /// Typed values the decision fixes. Values are text in the wire (e.g.
      /// "10000.00") so that no decimal passes through a binary float.
      Parameters: (string * string) list }

[<RequireQualifiedAccess>]
type PermittedOutcome =
    | RefuseAndRecord
    | DeferScope

type PolicyQuestion =
    { Id: string
      Question: string
      Owner: string
      /// Raw claim; admission requires "unresolved".
      Status: string
      Affects: string list
      PermittedOutcome: PermittedOutcome
      RaisedBy: Actor option }

[<RequireQualifiedAccess>]
type ObservationKind =
    | InheritedRule
    | Exemption
    | Default
    | SupersededRule
    | Behaviour

type ObservationLocation =
    { Path: string
      Symbol: string option
      Revision: string }

type Observation =
    { Id: string
      Description: string
      Kind: ObservationKind
      Location: ObservationLocation
      /// Raw claim; admission requires "unverified".
      Authority: string
      QuestionId: string option }

type Invariant =
    { Id: string
      Statement: string
      DerivedFrom: string list }

type Produced =
    { By: Actor
      At: string
      Repository: string
      Revision: string }

type AuthorityContract = { Id: string; Sha256: string }

type Handoff =
    { HandoffId: string
      Produced: Produced
      AuthorityContract: AuthorityContract
      Requirements: Requirement list
      Decisions: Decision list
      PolicyQuestions: PolicyQuestion list
      Observations: Observation list
      Invariants: Invariant list }

/// A structural defect, located by JSON path.
type DecodeError = { Path: string; Message: string }

// ---------------------------------------------------------------------------
// A small strict decoder over Ordo.Core.Json values.
// ---------------------------------------------------------------------------

type private Decoded<'a> = Result<'a, DecodeError list>

let private fail path message : Decoded<'a> = Error [ { Path = path; Message = message } ]

let private bind f (r: Decoded<'a>) : Decoded<'b> = Result.bind f r

/// Applicative combination: both sides' errors are kept.
let private both (a: Decoded<'a>) (b: Decoded<'b>) : Decoded<'a * 'b> =
    match a, b with
    | Ok x, Ok y -> Ok(x, y)
    | Error e, Ok _ | Ok _, Error e -> Error e
    | Error e1, Error e2 -> Error(e1 @ e2)

let private traverse (f: int -> 'a -> Decoded<'b>) (items: 'a list) : Decoded<'b list> =
    items
    |> List.mapi f
    |> List.fold
        (fun acc item ->
            match acc, item with
            | Ok xs, Ok x -> Ok(x :: xs)
            | Error e, Ok _ | Ok _, Error e -> Error e
            | Error e1, Error e2 -> Error(e1 @ e2))
        (Ok [])
    |> Result.map List.rev

/// An object's members, refusing duplicates, unknown members and missing
/// required members.
let private members path (required: string list) (optional: string list) (value: JsonValue) : Decoded<Map<string, JsonValue>> =
    match value with
    | JObject pairs ->
        let names = pairs |> List.map fst
        let duplicates = names |> List.countBy id |> List.filter (fun (_, n) -> n > 1) |> List.map fst
        let allowed = Set.ofList (required @ optional)
        let unknown = names |> List.filter (fun n -> not (Set.contains n allowed)) |> List.distinct
        let missing = required |> List.filter (fun n -> not (List.contains n names))
        let errors =
            (duplicates |> List.map (fun n -> { Path = path + "." + n; Message = "duplicate member" }))
            @ (unknown |> List.map (fun n -> { Path = path + "." + n; Message = "unknown member" }))
            @ (missing |> List.map (fun n -> { Path = path + "." + n; Message = "missing required member" }))
        if List.isEmpty errors then Ok(Map.ofList pairs) else Error errors
    | _ -> fail path "expected an object"

let private text path (value: JsonValue) : Decoded<string> =
    match value with
    | JString s when not (String.IsNullOrWhiteSpace s) -> Ok s
    | JString _ -> fail path "expected a non-blank string"
    | _ -> fail path "expected a string"

let private required name decoder path (m: Map<string, JsonValue>) = decoder (path + "." + name) m[name]

let private optional name decoder path (m: Map<string, JsonValue>) : Decoded<'a option> =
    match Map.tryFind name m with
    | None -> Ok None
    | Some v -> decoder (path + "." + name) v |> Result.map Some

let private array (item: string -> JsonValue -> Decoded<'a>) path (value: JsonValue) : Decoded<'a list> =
    match value with
    | JArray items -> items |> traverse (fun i v -> item (sprintf "%s[%d]" path i) v)
    | _ -> fail path "expected an array"

let private texts = array text

let private enumeration (cases: (string * 'a) list) path (value: JsonValue) : Decoded<'a> =
    text path value
    |> bind (fun raw ->
        match cases |> List.tryFind (fst >> (=) raw) with
        | Some(_, case) -> Ok case
        | None -> fail path (sprintf "unknown value '%s'; expected one of %s" raw (cases |> List.map fst |> String.concat ", ")))

let private constant expected path value =
    text path value
    |> bind (fun raw -> if raw = expected then Ok raw else fail path (sprintf "expected '%s', found '%s'" expected raw))

let private actor path value : Decoded<Actor> =
    members path [ "kind"; "id" ] [ "role" ] value
    |> bind (fun m ->
        both (required "kind" (enumeration [ "human", ActorKind.Human; "agent", ActorKind.Agent; "automation", ActorKind.Automation ]) path m)
             (both (required "id" text path m) (optional "role" text path m))
        |> Result.map (fun (kind, (id, role)) -> { Kind = kind; Id = id; Role = role }))

let private sourceRef path value : Decoded<SourceRef> =
    members path [ "path"; "revision" ] [ "locator"; "sha256" ] value
    |> bind (fun m ->
        both (both (required "path" text path m) (optional "locator" text path m))
             (both (required "revision" text path m) (optional "sha256" text path m))
        |> Result.map (fun ((p, locator), (revision, sha)) -> { Path = p; Locator = locator; Revision = revision; Sha256 = sha }))

let private parameters path value : Decoded<(string * string) list> =
    match value with
    | JObject pairs ->
        let duplicates = pairs |> List.countBy fst |> List.filter (fun (_, n) -> n > 1) |> List.map fst
        if not (List.isEmpty duplicates) then
            Error(duplicates |> List.map (fun n -> { Path = path + "." + n; Message = "duplicate member" }))
        else
            pairs
            |> traverse (fun _ (name, v) ->
                match v with
                | JString s -> Ok(name, s)
                | _ -> fail (path + "." + name) "decision parameters must be strings (decimal values are carried as text)")
            |> Result.map (List.sortBy fst)
    | _ -> fail path "expected an object"

let private requirement path value : Decoded<Requirement> =
    members path [ "id"; "statement"; "status"; "owner"; "source"; "supersedes" ] [ "supersededBy" ] value
    |> bind (fun m ->
        both
            (both (required "id" text path m) (required "statement" text path m))
            (both
                (both (required "status" (enumeration [ "accepted", RequirementStatus.Accepted; "superseded", RequirementStatus.Superseded ]) path m)
                      (required "owner" text path m))
                (both (required "source" sourceRef path m)
                      (both (required "supersedes" texts path m) (optional "supersededBy" text path m))))
        |> Result.map (fun ((id, statement), ((status, owner), (source, (supersedes, supersededBy)))) ->
            { Id = id; Statement = statement; Status = status; Owner = owner; Source = source
              Supersedes = supersedes; SupersededBy = supersededBy }))

let private decisionStatuses =
    [ "accepted", DecisionStatus.Accepted; "superseded", DecisionStatus.Superseded
      "proposed", DecisionStatus.Proposed; "rejected", DecisionStatus.Rejected ]

let private decisionKinds =
    [ "business-rule", DecisionKind.BusinessRule; "policy-limit", DecisionKind.PolicyLimit
      "exception", DecisionKind.Exception; "boundary", DecisionKind.Boundary
      "default-rule", DecisionKind.DefaultRule; "interpretation", DecisionKind.Interpretation
      "presentation", DecisionKind.Presentation ]

let private decision path value : Decoded<Decision> =
    members path
        [ "id"; "statement"; "status"; "kind"; "owner"; "requirementIds"; "source"; "supersedes" ]
        [ "authorizedBy"; "authorizedAt"; "supersededBy"; "parameters" ]
        value
    |> bind (fun m ->
        both
            (both (both (required "id" text path m) (required "statement" text path m))
                  (both (required "status" (enumeration decisionStatuses) path m) (required "kind" (enumeration decisionKinds) path m)))
            (both
                (both (both (required "owner" text path m) (optional "authorizedBy" actor path m))
                      (both (optional "authorizedAt" text path m) (required "requirementIds" texts path m)))
                (both (both (required "source" sourceRef path m) (required "supersedes" texts path m))
                      (both (optional "supersededBy" text path m) (optional "parameters" parameters path m))))
        |> Result.map (fun (((id, statement), (status, kind)), (((owner, by), (at, reqs)), ((source, supersedes), (supersededBy, ps)))) ->
            { Id = id; Statement = statement; Status = status; Kind = kind; Owner = owner
              AuthorizedBy = by; AuthorizedAt = at; RequirementIds = reqs; Source = source
              Supersedes = supersedes; SupersededBy = supersededBy
              Parameters = ps |> Option.defaultValue [] }))

let private policyQuestion path value : Decoded<PolicyQuestion> =
    members path [ "id"; "question"; "owner"; "status"; "affects"; "permittedOutcome" ] [ "raisedBy" ] value
    |> bind (fun m ->
        both
            (both (both (required "id" text path m) (required "question" text path m))
                  (both (required "owner" text path m) (required "status" text path m)))
            (both (required "affects" texts path m)
                  (both (required "permittedOutcome" (enumeration [ "refuse-and-record", PermittedOutcome.RefuseAndRecord; "defer-scope", PermittedOutcome.DeferScope ]) path m)
                        (optional "raisedBy" actor path m)))
        |> Result.map (fun (((id, question), (owner, status)), (affects, (outcome, raisedBy))) ->
            { Id = id; Question = question; Owner = owner; Status = status; Affects = affects
              PermittedOutcome = outcome; RaisedBy = raisedBy }))

let private observationKinds =
    [ "inherited-rule", ObservationKind.InheritedRule; "exemption", ObservationKind.Exemption
      "default", ObservationKind.Default; "superseded-rule", ObservationKind.SupersededRule
      "behaviour", ObservationKind.Behaviour ]

let private location path value : Decoded<ObservationLocation> =
    members path [ "path"; "revision" ] [ "symbol" ] value
    |> bind (fun m ->
        both (both (required "path" text path m) (optional "symbol" text path m)) (required "revision" text path m)
        |> Result.map (fun ((p, symbol), revision) -> { Path = p; Symbol = symbol; Revision = revision }))

let private observation path value : Decoded<Observation> =
    members path [ "id"; "description"; "kind"; "location"; "authority" ] [ "questionId" ] value
    |> bind (fun m ->
        both
            (both (required "id" text path m) (required "description" text path m))
            (both (both (required "kind" (enumeration observationKinds) path m) (required "location" location path m))
                  (both (required "authority" text path m) (optional "questionId" text path m)))
        |> Result.map (fun ((id, description), ((kind, loc), (authority, questionId))) ->
            { Id = id; Description = description; Kind = kind; Location = loc; Authority = authority; QuestionId = questionId }))

let private invariant path value : Decoded<Invariant> =
    members path [ "id"; "statement"; "derivedFrom" ] [] value
    |> bind (fun m ->
        both (both (required "id" text path m) (required "statement" text path m)) (required "derivedFrom" texts path m)
        |> Result.map (fun ((id, statement), derived) -> { Id = id; Statement = statement; DerivedFrom = derived }))

let private produced path value : Decoded<Produced> =
    members path [ "by"; "at"; "repository"; "revision" ] [] value
    |> bind (fun m ->
        both (both (required "by" actor path m) (required "at" text path m))
             (both (required "repository" text path m) (required "revision" text path m))
        |> Result.map (fun ((by, at), (repository, revision)) -> { By = by; At = at; Repository = repository; Revision = revision }))

let private authorityContract path value : Decoded<AuthorityContract> =
    members path [ "id"; "sha256" ] [] value
    |> bind (fun m ->
        both (required "id" text path m) (required "sha256" text path m)
        |> Result.map (fun (id, sha) -> { Id = id; Sha256 = sha }))

let private handoff (value: JsonValue) : Decoded<Handoff> =
    let path = "$"
    members path
        [ "schema"; "schemaVersion"; "handoffId"; "produced"; "authorityContract"; "requirements"
          "decisions"; "policyQuestions"; "observations"; "invariants" ]
        []
        value
    |> bind (fun m ->
        both
            (both (both (required "schema" (constant SchemaName) path m) (required "schemaVersion" (constant SchemaVersion) path m))
                  (both (required "handoffId" text path m)
                        (both (required "produced" produced path m) (required "authorityContract" authorityContract path m))))
            (both (both (required "requirements" (array requirement) path m) (required "decisions" (array decision) path m))
                  (both (required "policyQuestions" (array policyQuestion) path m)
                        (both (required "observations" (array observation) path m) (required "invariants" (array invariant) path m))))
        |> Result.map (fun ((_, (id, (prod, contract))), ((reqs, decs), (questions, (observations, invariants)))) ->
            { HandoffId = id; Produced = prod; AuthorityContract = contract; Requirements = reqs; Decisions = decs
              PolicyQuestions = questions; Observations = observations; Invariants = invariants }))

/// Strictly decodes handoff JSON text. Pure.
let read (json: string) : Result<Handoff, DecodeError list> =
    match parse json with
    | Error e -> Error [ { Path = "$"; Message = sprintf "malformed JSON: %A" e } ]
    | Ok value -> handoff value
