/// Evidence as a first-class, referenceable record.
///
/// Evidence that exists only inside a prompt, a chat transcript or an
/// agent's memory cannot be cited, replayed, superseded or audited, so Ordo
/// requires it to be a value with stable identity and provenance
/// (ORDO-0501 / ORDO-0502).
///
/// Three distinctions in this module carry most of its weight:
///
/// * Evidence being *available* is not evidence being *true* (ORDO-0505).
///   Nothing here scores or ranks evidence; what is trustworthy enough is a
///   domain question (ORDO-5902).
/// * A model's inference is not an observation. `Inferred` exists so that a
///   claim produced by reasoning can never be recorded as a witnessed fact
///   (ORDO-5904).
/// * Missing evidence is not stale evidence and neither is low confidence
///   (ORDO-9502 / ORDO-1003). They are separate types here and separate
///   outcomes downstream.
module Ordo.Core.Evidence

open System
open Ordo.Core.Identifiers

/// How a piece of evidence came to exist.
type EvidenceKind =
    /// Observed or measured directly: a tool's output, a file's contents, a
    /// test result, a recorded event.
    | Direct
    /// Mechanically derived from other evidence by a named, repeatable
    /// computation. The derivation is carried on the evidence itself so the
    /// chain stays reconstructable (ORDO-5903 / ORDO-7903).
    | Derived of computation: string * fromEvidence: EvidenceId list
    /// Produced by judgment or reasoning — including a model's. Never
    /// interchangeable with `Direct`.
    | Inferred of by: ProviderId

/// Where a piece of evidence came from, in the producing system's own terms.
type EvidenceSource =
    { /// The system or actor that produced it: a tool name, a repository, a
      /// person, a service.
      System: string
      /// An optional stable pointer into that system — a path, a URL, a
      /// commit, a record id. Ordo never dereferences it; it exists so that
      /// an external store can be reached without Ordo depending on one
      /// (ORDO-0502).
      Reference: string option }

/// A single piece of evidence.
///
/// `Content` is deliberately `JsonValue`-free text-or-structure agnostic:
/// it is carried as a wire value so that evidence can cross a process
/// boundary without Ordo owning a schema for every domain's facts.
type Evidence =
    { Id: EvidenceId
      /// What kind of claim this is. See `EvidenceKind`.
      Kind: EvidenceKind
      Source: EvidenceSource
      /// When the underlying fact was observed or produced — not when this
      /// record was written. Freshness is judged against this.
      ObservedAt: DateTimeOffset
      /// The evidence itself, as a wire value.
      Content: Ordo.Core.Json.JsonValue }


/// Why the provenance chain of a supplied evidence set is structurally
/// invalid.
///
/// These are integrity failures, not judgments about whether the evidence is
/// true or persuasive. Ordo only checks whether a Derived record can be
/// reconstructed from a closed, acyclic set of identified inputs.
type EvidenceDependencyError =
    /// Two records claim the same stable identity, so a reference to that id
    /// is ambiguous before dependency traversal even begins.
    | DuplicateEvidenceId of EvidenceId
    /// A requested closure root was not present in the supplied set.
    | SelectedEvidenceMissing of EvidenceId
    /// A Derived record names an input that is not present in the supplied
    /// closed evidence set.
    | MissingEvidenceDependency of dependent: EvidenceId * missing: EvidenceId
    /// A Derived-from chain eventually depends on itself. The cycle repeats
    /// its first id at the end, for example A -> B -> A.
    | EvidenceDependencyCycle of EvidenceId list

/// Pure validation and closure over EvidenceKind.Derived relationships.
///
/// This is deliberately not a general graph abstraction. Correction,
/// supersession, historical observation and arbitrary domain relationships
/// have their own semantics and are not inspected here.
[<RequireQualifiedAccess>]
module EvidenceDependency =

    let private key (id: EvidenceId) = EvidenceId.value id

    let private dependencies (evidence: Evidence) =
        match evidence.Kind with
        | Derived(_, fromEvidence) ->
            fromEvidence
            |> List.distinctBy key
            |> List.sortBy key
        | Direct
        | Inferred _ -> []

    let private index (available: Evidence list) =
        available
        |> List.fold
            (fun state evidence ->
                match state with
                | Error error -> Error error
                | Ok byId ->
                    let evidenceKey = key evidence.Id

                    if Map.containsKey evidenceKey byId then
                        Error(DuplicateEvidenceId evidence.Id)
                    else
                        Ok(Map.add evidenceKey evidence byId))
            (Ok Map.empty)

    /// Returns the selected evidence and every transitive Derived input.
    ///
    /// Ordering is deterministic and dependency-first. Roots, dependencies
    /// and caller input order cannot change the result order for the same
    /// identified evidence relation.
    let closure
        (available: Evidence list)
        (selected: EvidenceId list)
        : Result<Evidence list, EvidenceDependencyError> =
        index available
        |> Result.bind (fun byId ->
            let rec visit
                (path: string list)
                (visited: Set<string>)
                (ordered: Evidence list)
                (id: EvidenceId)
                =
                let evidenceKey = key id

                if Set.contains evidenceKey visited then
                    Ok(visited, ordered)
                elif List.contains evidenceKey path then
                    let cycleKeys =
                        path
                        |> List.skipWhile (fun item -> item <> evidenceKey)
                        |> fun cycle -> cycle @ [ evidenceKey ]

                    let cycle =
                        cycleKeys
                        |> List.map (fun item -> (Map.find item byId).Id)

                    Error(EvidenceDependencyCycle cycle)
                else
                    match Map.tryFind evidenceKey byId with
                    | None -> Error(SelectedEvidenceMissing id)
                    | Some evidence ->
                        let nextPath = path @ [ evidenceKey ]

                        let rec visitDependencies deps visited ordered =
                            match deps with
                            | [] -> Ok(visited, ordered)
                            | dependency :: rest ->
                                let dependencyKey = key dependency

                                if not (Map.containsKey dependencyKey byId) then
                                    Error(MissingEvidenceDependency(evidence.Id, dependency))
                                else
                                    match visit nextPath visited ordered dependency with
                                    | Error error -> Error error
                                    | Ok(visited, ordered) ->
                                        visitDependencies rest visited ordered

                        match visitDependencies (dependencies evidence) visited ordered with
                        | Error error -> Error error
                        | Ok(visited, ordered) ->
                            Ok(Set.add evidenceKey visited, evidence :: ordered)

            let roots =
                selected
                |> List.distinctBy key
                |> List.sortBy key

            let rec visitRoots roots visited ordered =
                match roots with
                | [] -> Ok(List.rev ordered)
                | root :: rest ->
                    match Map.tryFind (key root) byId with
                    | None -> Error(SelectedEvidenceMissing root)
                    | Some _ ->
                        match visit [] visited ordered root with
                        | Error error -> Error error
                        | Ok(visited, ordered) ->
                            visitRoots rest visited ordered

            visitRoots roots Set.empty [])

    /// Validates that the entire supplied evidence set is closed and acyclic
    /// under Derived-from references.
    let validateClosedSet (available: Evidence list) : Result<unit, EvidenceDependencyError> =
        available
        |> List.map (fun evidence -> evidence.Id)
        |> closure available
        |> Result.map (fun _ -> ())

/// A statement that some evidence is required, whether or not it is
/// available.
///
/// This type is why "I could not decide because I lack X" is expressible
/// without pretending to be uncertainty (ORDO-0504).
type EvidenceRequirement =
    { /// The requirement's own stable identity, so that a decision record
      /// can say precisely which requirement went unmet.
      Id: string
      /// What must be established, in domain terms.
      Description: string
      /// How recent the satisfying evidence must be, if the domain cares.
      /// `None` means freshness is not part of this requirement — Ordo
      /// imposes no universal expiry (ORDO-9504).
      MaximumAge: TimeSpan option
      /// Kinds of evidence that may satisfy this requirement. An empty list
      /// means any kind may. A requirement that only a direct observation
      /// will satisfy is how a domain refuses to be talked into an
      /// inference.
      AcceptableKinds: EvidenceKindPattern list }

/// Which kinds of evidence a requirement will accept. A pattern rather than
/// an `EvidenceKind` because a requirement cares about the category, not
/// about which particular computation or provider produced it.
and EvidenceKindPattern =
    | AnyDirect
    | AnyDerived
    | AnyInferred

/// The result of checking one requirement against the evidence on hand.
///
/// `Stale` is separate from `Unsatisfied` because the remedies differ: stale
/// evidence needs reacquiring, missing evidence needs acquiring, and
/// conflating them hides which one the caller faces (ORDO-9502).
type RequirementCheck =
    | Satisfied of EvidenceRequirement * by: Evidence
    | Stale of EvidenceRequirement * newest: Evidence * age: TimeSpan
    | WrongKind of EvidenceRequirement * rejected: Evidence list
    | Unsatisfied of EvidenceRequirement

[<RequireQualifiedAccess>]
module EvidenceKind =

    let toWire kind =
        match kind with
        | Direct -> "direct"
        | Derived _ -> "derived"
        | Inferred _ -> "inferred"

    let matchesPattern (pattern: EvidenceKindPattern) (kind: EvidenceKind) =
        match pattern, kind with
        | AnyDirect, Direct -> true
        | AnyDerived, Derived _ -> true
        | AnyInferred, Inferred _ -> true
        | _ -> false

[<RequireQualifiedAccess>]
module EvidenceRequirement =

    /// A requirement with no freshness rule and no kind restriction.
    let create (id: string) (description: string) =
        { Id = id
          Description = description
          MaximumAge = None
          AcceptableKinds = [] }

    let withMaximumAge (age: TimeSpan) (requirement: EvidenceRequirement) =
        { requirement with MaximumAge = Some age }

    let acceptingOnly (kinds: EvidenceKindPattern list) (requirement: EvidenceRequirement) =
        { requirement with AcceptableKinds = kinds }

[<RequireQualifiedAccess>]
module Evidence =

    /// Checks one requirement against available evidence at a given instant.
    ///
    /// Evidence satisfies a requirement when its id matches the requirement
    /// id — the domain decides what it is calling things — and its kind is
    /// acceptable. Of the candidates that remain, the newest is used, and it
    /// is reported as stale rather than as satisfying when it is older than
    /// the requirement allows.
    let checkRequirement (now: DateTimeOffset) (available: Evidence list) (requirement: EvidenceRequirement) =
        let named =
            available |> List.filter (fun e -> EvidenceId.value e.Id = requirement.Id)

        let kindAcceptable (e: Evidence) =
            match requirement.AcceptableKinds with
            | [] -> true
            | patterns -> patterns |> List.exists (fun p -> EvidenceKind.matchesPattern p e.Kind)

        let acceptable, rejected = named |> List.partition kindAcceptable

        match acceptable |> List.sortByDescending (fun e -> e.ObservedAt) with
        | [] when not (List.isEmpty rejected) -> WrongKind(requirement, rejected)
        | [] -> Unsatisfied requirement
        | newest :: _ ->
            match requirement.MaximumAge with
            | Some limit when now - newest.ObservedAt > limit -> Stale(requirement, newest, now - newest.ObservedAt)
            | _ -> Satisfied(requirement, newest)

    /// Checks every requirement, returning the checks in requirement order.
    /// The caller decides what to do with them; this function neither
    /// short-circuits nor ranks, because a caller usually wants the full
    /// list of what is missing rather than the first thing that failed.
    let checkAll (now: DateTimeOffset) (available: Evidence list) (requirements: EvidenceRequirement list) =
        requirements |> List.map (checkRequirement now available)

    let unmet (checks: RequirementCheck list) =
        checks
        |> List.choose (function
            | Satisfied _ -> None
            | Stale(requirement, _, _) -> Some requirement
            | WrongKind(requirement, _) -> Some requirement
            | Unsatisfied requirement -> Some requirement)
