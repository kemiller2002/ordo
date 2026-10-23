/// Admission of a decoded handoff: the semantic rules that decide whether a
/// handoff may be constructed from at all (VC-002 requirements 1, 2 and 5).
///
/// What admission refuses is a *mechanically* invalid handoff: authority
/// claimed where none can exist, a question used as a rule, a broken
/// supersession history. What it never refuses is honest uncertainty: an
/// unresolved policy question with a permitted outcome of refuse-and-record
/// or defer-scope is valid input (R-ADM-1); a question raised by the agent
/// itself is valid input (R-ADM-2); a presentation decision is not held to
/// the exception rules of policy (R-ADM-3).
module Echelon.Construct.Admission

open System.Text.RegularExpressions
open Echelon.Construct.Handoff

type AdmissionError =
    /// The JSON is not a structurally valid echelon.authority-handoff/0.1.
    | Malformed of path: string * message: string
    | InvalidId of id: string * where: string
    | DuplicateId of id: string
    /// An accepted decision must name the human who authorized it. An agent's
    /// or an automation's word is never authority (VC-002 req. 2).
    | AcceptedDecisionWithoutHumanAuthorization of decisionId: string * authorizedBy: ActorKind option
    | AcceptedDecisionWithoutAuthorizationTime of decisionId: string
    /// Observed behaviour carries no authority until a requirement or decision supports it.
    | ObservationClaimsAuthority of observationId: string * claimed: string
    /// A policy question is never a rule, so nothing may derive from it or cite it as one.
    | QuestionReferencedAsRule of questionId: string * referencedBy: string
    | QuestionNotUnresolved of questionId: string * status: string
    | SupersessionCycle of ids: string list
    /// A record cites, as current authority, a record that has been superseded.
    | SupersededCitedAsCurrent of citedId: string * citedBy: string
    /// The two directions of a supersession disagree, or a status contradicts its links.
    | InconsistentSupersession of id: string * detail: string
    | UnknownReference of id: string * referencedBy: string

/// A handoff that passed admission. Constructible only by `admit`.
type AdmittedHandoff = private AdmittedHandoff of Handoff

let handoffOf (AdmittedHandoff h) = h

let private idPattern = Regex(@"^[A-Z][A-Z0-9]*(-[A-Z0-9]+)+$", RegexOptions.CultureInvariant)

// ---- Indexes ---------------------------------------------------------------

type private RecordKind =
    | Req of Requirement
    | Dec of Decision
    | Question of PolicyQuestion
    | Obs of Observation
    | Inv of Invariant

let private allIds (h: Handoff) =
    (h.Requirements |> List.map (fun r -> r.Id, Req r))
    @ (h.Decisions |> List.map (fun d -> d.Id, Dec d))
    @ (h.PolicyQuestions |> List.map (fun q -> q.Id, Question q))
    @ (h.Observations |> List.map (fun o -> o.Id, Obs o))
    @ (h.Invariants |> List.map (fun i -> i.Id, Inv i))

let private isSuperseded =
    function
    | Req r -> r.Status = RequirementStatus.Superseded
    | Dec d -> d.Status = DecisionStatus.Superseded
    | _ -> false

let private isQuestion =
    function
    | Question _ -> true
    | _ -> false

// ---- Rules -------------------------------------------------------------------

let private idErrors (h: Handoff) =
    let ids = allIds h
    let invalid =
        (h.HandoffId, "handoffId") :: (ids |> List.map (fun (id, _) -> id, "record id"))
        |> List.filter (fun (id, _) -> not (idPattern.IsMatch id))
        |> List.map InvalidId
    let duplicates =
        ids |> List.countBy fst |> List.filter (fun (_, n) -> n > 1) |> List.map (fst >> DuplicateId)
    invalid @ duplicates

let private authorizationErrors (h: Handoff) =
    h.Decisions
    |> List.filter (fun d -> d.Status = DecisionStatus.Accepted)
    |> List.collect (fun d ->
        [ match d.AuthorizedBy with
          | Some a when a.Kind = ActorKind.Human -> ()
          | other -> AcceptedDecisionWithoutHumanAuthorization(d.Id, other |> Option.map _.Kind)
          if d.AuthorizedAt.IsNone then AcceptedDecisionWithoutAuthorizationTime d.Id ])

let private observationErrors (h: Handoff) =
    h.Observations
    |> List.filter (fun o -> o.Authority <> "unverified")
    |> List.map (fun o -> ObservationClaimsAuthority(o.Id, o.Authority))

let private questionStatusErrors (h: Handoff) =
    h.PolicyQuestions
    |> List.filter (fun q -> q.Status <> "unresolved")
    |> List.map (fun q -> QuestionNotUnresolved(q.Id, q.Status))

/// Every citation in the handoff that asserts the cited record as *authority*
/// for the citing one, as (cited, citing, field).
let private ruleCitations (h: Handoff) =
    (h.Decisions |> List.collect (fun d -> d.RequirementIds |> List.map (fun r -> r, d.Id, "requirementIds")))
    @ (h.Invariants |> List.collect (fun i -> i.DerivedFrom |> List.map (fun r -> r, i.Id, "derivedFrom")))

/// Every citation that is a reference but not an authority claim.
let private plainCitations (h: Handoff) =
    (h.Requirements |> List.collect (fun r -> (r.Supersedes @ Option.toList r.SupersededBy) |> List.map (fun x -> x, r.Id)))
    @ (h.Decisions |> List.collect (fun d -> (d.Supersedes @ Option.toList d.SupersededBy) |> List.map (fun x -> x, d.Id)))
    @ (h.PolicyQuestions |> List.collect (fun q -> q.Affects |> List.map (fun x -> x, q.Id)))
    @ (h.Observations |> List.collect (fun o -> o.QuestionId |> Option.toList |> List.map (fun x -> x, o.Id)))

let private referenceErrors (h: Handoff) =
    let index = allIds h |> Map.ofList
    let unknown =
        (ruleCitations h |> List.map (fun (cited, citing, _) -> cited, citing)) @ plainCitations h
        |> List.filter (fun (cited, _) -> not (Map.containsKey cited index))
        |> List.map UnknownReference
    let questionAsRule =
        ruleCitations h
        |> List.filter (fun (cited, _, _) -> index |> Map.tryFind cited |> Option.exists isQuestion)
        |> List.map (fun (cited, citing, field) -> QuestionReferencedAsRule(cited, sprintf "%s.%s" citing field))
    let questionInSupersession =
        plainCitations h
        |> List.filter (fun (cited, citing) ->
            index |> Map.tryFind cited |> Option.exists isQuestion
            && (h.Requirements |> List.exists (fun r -> r.Id = citing) || h.Decisions |> List.exists (fun d -> d.Id = citing)))
        |> List.map (fun (cited, citing) -> QuestionReferencedAsRule(cited, citing + ".supersession"))
    let supersededAsCurrent =
        ruleCitations h
        |> List.filter (fun (cited, citing, _) ->
            let citingIsCurrent =
                match Map.tryFind citing index with
                | Some(Dec d) -> d.Status = DecisionStatus.Accepted || d.Status = DecisionStatus.Proposed
                | Some(Inv _) -> true
                | _ -> false
            citingIsCurrent && (index |> Map.tryFind cited |> Option.exists isSuperseded))
        |> List.map (fun (cited, citing, _) -> SupersededCitedAsCurrent(cited, citing))
    unknown @ questionAsRule @ questionInSupersession @ supersededAsCurrent

/// Supersession links between requirements and between decisions: each
/// direction must be stated by both sides, a superseded record must name its
/// successor, and the successor must not itself be superseded-without-successor.
let private supersessionErrors (h: Handoff) =
    let links =
        (h.Requirements |> List.map (fun r -> r.Id, r.Supersedes, r.SupersededBy, r.Status = RequirementStatus.Superseded))
        @ (h.Decisions |> List.map (fun d -> d.Id, d.Supersedes, d.SupersededBy, d.Status = DecisionStatus.Superseded))
    let byId = links |> List.map (fun ((id, _, _, _) as l) -> id, l) |> Map.ofList
    let consistency =
        links
        |> List.collect (fun (id, supersedes, supersededBy, superseded) ->
            [ if superseded && supersededBy.IsNone then
                  InconsistentSupersession(id, "status is superseded but supersededBy is missing")
              if not superseded && supersededBy.IsSome then
                  InconsistentSupersession(id, "supersededBy is set but status is not superseded")
              for older in supersedes do
                  match Map.tryFind older byId with
                  | Some(_, _, olderBy, olderSuperseded) ->
                      if not olderSuperseded then
                          InconsistentSupersession(id, sprintf "supersedes %s, which is not marked superseded" older)
                      elif olderBy <> Some id then
                          InconsistentSupersession(id, sprintf "supersedes %s, whose supersededBy does not name %s" older id)
                  | None -> ()
              match supersededBy with
              | Some newer ->
                  match Map.tryFind newer byId with
                  | Some(_, newerSupersedes, _, _) when not (List.contains id newerSupersedes) ->
                      InconsistentSupersession(id, sprintf "supersededBy %s, which does not list %s in supersedes" newer id)
                  | _ -> ()
              | None -> () ])
    // A cycle over "supersedes" edges (both directions folded into one edge set).
    let edges =
        links
        |> List.collect (fun (id, supersedes, supersededBy, _) ->
            (supersedes |> List.map (fun older -> id, older))
            @ (supersededBy |> Option.toList |> List.map (fun newer -> newer, id)))
        |> List.distinct
        |> List.filter (fun (_, older) -> Map.containsKey older byId)
    let successors node = edges |> List.filter (fst >> (=) node) |> List.map snd
    let rec unpeelable (remaining: Set<string>) =
        let peelable = remaining |> Set.filter (fun n -> successors n |> List.forall (fun s -> not (Set.contains s remaining)))
        if Set.isEmpty peelable then remaining else unpeelable (Set.difference remaining peelable)
    let cyclic = edges |> List.collect (fun (a, b) -> [ a; b ]) |> Set.ofList |> unpeelable
    consistency @ (if Set.isEmpty cyclic then [] else [ SupersessionCycle(Set.toList cyclic) ])

/// Pure admission. Every rule runs; all violations are reported together, sorted.
let admit (handoff: Handoff) : Result<AdmittedHandoff, AdmissionError list> =
    let errors =
        [ idErrors; authorizationErrors; observationErrors; questionStatusErrors; referenceErrors; supersessionErrors ]
        |> List.collect (fun rule -> rule handoff)
        |> List.distinct
        |> List.sortBy (sprintf "%A")
    if List.isEmpty errors then Ok(AdmittedHandoff handoff) else Error errors

/// Decode then admit.
let readAndAdmit (json: string) : Result<AdmittedHandoff, AdmissionError list> =
    match read json with
    | Error decodeErrors -> Error(decodeErrors |> List.map (fun e -> Malformed(e.Path, e.Message)))
    | Ok handoff -> admit handoff
