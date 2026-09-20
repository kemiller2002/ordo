/// Reusable negative observations without a universal negative-knowledge ontology.
///
/// A NegativeObservation is deliberately narrow: an observation/search method
/// actually ran against a named scope/state and did not find one named target.
///
/// It does not represent unavailable, not-searched, not-compared, unsupported,
/// unverifiable, or a domain field whose source simply omitted a value. Those
/// states remain distinct.
///
/// Evidence owns identity, source, observed time, and epistemic basis.
/// ContextCoverageClaim owns how completely the named scope was observed.
/// NegativeObservation owns the minimum typed payload needed to make a
/// reusable "not found" statement auditable.
module Ordo.Core.NegativeKnowledge

open System
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.Coverage

type NegativeObservationError =
    | NegativeTargetRequired
    | NegativeMethodRequired
    | NegativeStateReferenceRequired
    | NegativeQueryMustNotBeBlank
    | NegativeExclusionMustNotBeBlank
    | NegativeErrorMustNotBeBlank

type NegativeObservation =
    private
        { TargetValue: string
          ScopeValue: CoverageScope
          MethodValue: string
          QueryValue: string option
          StateReferenceValue: string
          ExclusionsValue: string list
          ErrorsValue: string list }

    member this.Target = this.TargetValue
    member this.Scope = this.ScopeValue
    member this.Method = this.MethodValue
    member this.Query = this.QueryValue
    member this.StateReference = this.StateReferenceValue
    member this.Exclusions = this.ExclusionsValue
    member this.Errors = this.ErrorsValue

[<RequireQualifiedAccess>]
module NegativeObservation =

    let private validRequired error (value: string) =
        if String.IsNullOrWhiteSpace value then Error error else Ok value

    let private validOptional error value =
        match value with
        | None -> Ok None
        | Some text when String.IsNullOrWhiteSpace text -> Error error
        | Some text -> Ok(Some text)

    let private validList error values =
        match values |> List.tryFind String.IsNullOrWhiteSpace with
        | Some _ -> Error error
        | None -> Ok values

    let create
        (target: string)
        (scope: CoverageScope)
        (methodName: string)
        (query: string option)
        (stateReference: string)
        (exclusions: string list)
        (errors: string list)
        : Result<NegativeObservation, NegativeObservationError> =
        match
            validRequired NegativeTargetRequired target,
            validRequired NegativeMethodRequired methodName,
            validOptional NegativeQueryMustNotBeBlank query,
            validRequired NegativeStateReferenceRequired stateReference,
            validList NegativeExclusionMustNotBeBlank exclusions,
            validList NegativeErrorMustNotBeBlank errors
        with
        | Ok target, Ok methodName, Ok query, Ok stateReference, Ok exclusions, Ok errors ->
            Ok
                { TargetValue = target
                  ScopeValue = scope
                  MethodValue = methodName
                  QueryValue = query
                  StateReferenceValue = stateReference
                  ExclusionsValue = exclusions
                  ErrorsValue = errors }
        | Error error, _, _, _, _, _
        | _, Error error, _, _, _, _
        | _, _, Error error, _, _, _
        | _, _, _, Error error, _, _
        | _, _, _, _, Error error, _
        | _, _, _, _, _, Error error -> Error error

/// Why one negative observation is not structurally sufficient to support a
/// scoped absence conclusion.
type AbsenceSupportFailure =
    | NegativeObservationScopeMismatch of observation: CoverageScope * coverage: CoverageScope
    | NegativeObservationNotCoverageProvenance of evidence: EvidenceId
    | NegativeObservationCoverageNotComplete of CoverageStatus

[<RequireQualifiedAccess>]
module NegativeKnowledge =

    /// Checks only structural eligibility for a domain absence conclusion.
    ///
    /// The caller still decides what "absent" means in its domain. Ordo does
    /// not turn a negative observation into truth, and it never infers
    /// completeness from the observation itself.
    let supportsAbsence
        (evidence: Evidence)
        (coverage: ContextCoverageClaim)
        (observation: NegativeObservation)
        : Result<unit, AbsenceSupportFailure> =
        if coverage.Scope <> observation.Scope then
            Error(NegativeObservationScopeMismatch(observation.Scope, coverage.Scope))
        elif not (coverage.Provenance |> List.contains evidence.Id) then
            Error(NegativeObservationNotCoverageProvenance evidence.Id)
        else
            match coverage.Status with
            | Complete -> Ok ()
            | Partial -> Error(NegativeObservationCoverageNotComplete Partial)
            | Unknown -> Error(NegativeObservationCoverageNotComplete Unknown)
