/// Scoped completeness claims for the context supplied to a bounded decision.
///
/// Evidence says what was observed. Coverage says how completely a declared
/// observation method covered one named domain/contract scope. Keeping those
/// concepts separate prevents a large pile of evidence, a successful call, or
/// provider confidence from silently becoming a claim of completeness.
module Ordo.Core.Coverage

open System
open Ordo.Core.Identifiers
open Ordo.Core.Evidence

type CoverageScopeError =
    | CoverageScopeEmpty
    | CoverageScopeNotTrimmed of raw: string
    | CoverageScopeTooLong of length: int * limit: int
    | CoverageScopeHasControlCharacter of raw: string

[<Literal>]
let MaxCoverageScopeLength = 200

type CoverageScope =
    private
    | CoverageScope of string

    member this.Value =
        let (CoverageScope value) = this
        value

    override this.ToString() = this.Value

[<RequireQualifiedAccess>]
module CoverageScope =

    let create (raw: string) : Result<CoverageScope, CoverageScopeError> =
        if String.IsNullOrEmpty raw then
            Error CoverageScopeEmpty
        elif raw.Trim() <> raw then
            Error(CoverageScopeNotTrimmed raw)
        elif raw.Length > MaxCoverageScopeLength then
            Error(CoverageScopeTooLong(raw.Length, MaxCoverageScopeLength))
        elif raw |> Seq.exists Char.IsControl then
            Error(CoverageScopeHasControlCharacter raw)
        else
            Ok(CoverageScope raw)

    let value (scope: CoverageScope) = scope.Value

/// Completeness for exactly one declared scope.
type CoverageStatus =
    | Complete
    | Partial
    | Unknown

[<RequireQualifiedAccess>]
module CoverageStatus =

    let toWire status =
        match status with
        | Complete -> "complete"
        | Partial -> "partial"
        | Unknown -> "unknown"

    let fromWire raw =
        match raw with
        | "complete" -> Some Complete
        | "partial" -> Some Partial
        | "unknown" -> Some Unknown
        | _ -> None

/// A caller/domain assertion about one scope.
///
/// Provenance points at Evidence records that establish why this claim was
/// made. The claim does not duplicate source, time, method, exclusions, or
/// errors already carried by those evidence records.
type ContextCoverageClaim =
    private
        { ScopeValue: CoverageScope
          StatusValue: CoverageStatus
          ProvenanceValue: EvidenceId list }

    member this.Scope = this.ScopeValue
    member this.Status = this.StatusValue
    member this.Provenance = this.ProvenanceValue

type CoverageClaimError =
    | CoverageProvenanceRequired of CoverageScope
    | DuplicateCoverageScope of CoverageScope
    | MissingCoverageProvenance of scope: CoverageScope * evidence: EvidenceId

[<RequireQualifiedAccess>]
module ContextCoverageClaim =

    let create
        (scope: CoverageScope)
        (status: CoverageStatus)
        (provenance: EvidenceId list)
        : Result<ContextCoverageClaim, CoverageClaimError> =
        let provenance = provenance |> List.distinct

        if List.isEmpty provenance then
            Error(CoverageProvenanceRequired scope)
        else
            Ok
                { ScopeValue = scope
                  StatusValue = status
                  ProvenanceValue = provenance }

    let scope (claim: ContextCoverageClaim) = claim.Scope
    let status (claim: ContextCoverageClaim) = claim.Status
    let provenance (claim: ContextCoverageClaim) = claim.Provenance

/// A contract requirement means exactly one thing: this scope must be
/// Complete before bounded resolution may run.
type CoverageRequirement =
    { Scope: CoverageScope
      Description: string }

[<RequireQualifiedAccess>]
module CoverageRequirement =

    let complete (scope: CoverageScope) (description: string) =
        { Scope = scope
          Description = description }

/// Why a required coverage scope did not satisfy the contract.
type CoverageFailure =
    | CoverageMissing of CoverageRequirement
    | CoveragePartial of CoverageRequirement * ContextCoverageClaim
    | CoverageUnknown of CoverageRequirement * ContextCoverageClaim

[<RequireQualifiedAccess>]
module ContextCoverage =

    /// Checks claim-set integrity against the evidence carried by the same
    /// request. One scope has one current claim; every provenance reference
    /// must resolve inside the request's closed evidence set.
    let validateClaims
        (availableEvidence: Evidence list)
        (claims: ContextCoverageClaim list)
        : Result<unit, CoverageClaimError> =
        let evidenceIds = availableEvidence |> List.map (fun evidence -> evidence.Id) |> Set.ofList

        let rec validate
            (seen: Set<CoverageScope>)
            (remaining: ContextCoverageClaim list)
            : Result<unit, CoverageClaimError> =
            match remaining with
            | [] -> Ok ()
            | (claim: ContextCoverageClaim) :: rest ->
                if Set.contains claim.Scope seen then
                    Error(DuplicateCoverageScope claim.Scope)
                else
                    match claim.Provenance |> List.tryFind (fun id -> not (Set.contains id evidenceIds)) with
                    | Some missing -> Error(MissingCoverageProvenance(claim.Scope, missing))
                    | None -> validate (Set.add claim.Scope seen) rest

        validate Set.empty claims

    let tryFind (scope: CoverageScope) (claims: ContextCoverageClaim list) =
        claims |> List.tryFind (fun (claim: ContextCoverageClaim) -> claim.Scope = scope)

    let checkRequirement
        (claims: ContextCoverageClaim list)
        (requirement: CoverageRequirement)
        : CoverageFailure option =
        match tryFind requirement.Scope claims with
        | None -> Some(CoverageMissing requirement)
        | Some claim ->
            match claim.Status with
            | Complete -> None
            | Partial -> Some(CoveragePartial(requirement, claim))
            | Unknown -> Some(CoverageUnknown(requirement, claim))

    let checkAll
        (claims: ContextCoverageClaim list)
        (requirements: CoverageRequirement list)
        : CoverageFailure list =
        requirements |> List.choose (checkRequirement claims)