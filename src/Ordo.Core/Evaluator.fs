/// Evaluation authority as the identified closure of everything that can
/// change an acceptance verdict (ORD-EXEC-090..096).
///
/// A gate command alone is not the evaluator. The evaluator is the gate code
/// together with its configuration, test-selection logic, schemas, fixtures,
/// generated inputs, referenced policies and any other declared dependency.
/// The host observes each input's content digest; this module turns the set
/// into one stable, content-derived identity and decides what a verdict may
/// honestly claim when that identity moved.
module Ordo.Core.Evaluator

open System
open System.Security.Cryptography
open System.Text
open Ordo.Core.Json

/// Why an input participates in acceptance (ORD-EXEC-091).
type EvaluatorInputKind =
    | GateCode
    | Configuration
    | TestSelection
    | Schema
    | Fixture
    | GeneratedInput
    | ReferencedPolicy
    | DeclaredDependency

[<RequireQualifiedAccess>]
module EvaluatorInputKind =

    let toWire kind =
        match kind with
        | GateCode -> "gate-code"
        | Configuration -> "configuration"
        | TestSelection -> "test-selection"
        | Schema -> "schema"
        | Fixture -> "fixture"
        | GeneratedInput -> "generated-input"
        | ReferencedPolicy -> "policy"
        | DeclaredDependency -> "dependency"

    let fromWire raw =
        [ GateCode; Configuration; TestSelection; Schema; Fixture; GeneratedInput; ReferencedPolicy; DeclaredDependency ]
        |> List.tryFind (fun k -> toWire k = raw)

/// One observed member of the evaluator closure. `Reference` is whatever the
/// host uses to locate it (a repository path, a package id, a URL);
/// `Digest` is the host-observed content digest.
type EvaluatorInput =
    { Kind: EvaluatorInputKind
      Reference: string
      Digest: string }

/// The immutable, content-derived identity of an effective evaluator
/// (ORD-EXEC-092). Private so the fingerprint cannot disagree with the
/// inputs it names.
type EvaluatorIdentity =
    private
        { inputs: EvaluatorInput list
          fingerprint: string }

    member this.Inputs = this.inputs
    member this.Fingerprint = this.fingerprint

type EvaluatorIdentityError =
    | NoEvaluatorInputs
    | NoGateCode
    | InputWithoutDigest of reference: string
    | DuplicateInputReference of reference: string

/// How two evaluator identities differ, input by input.
type EvaluatorChange =
    | InputAdded of EvaluatorInput
    | InputRemoved of EvaluatorInput
    | InputChanged of before: EvaluatorInput * after: EvaluatorInput

[<RequireQualifiedAccess>]
module EvaluatorIdentity =

    let private canonical (inputs: EvaluatorInput list) =
        inputs
        |> List.map (fun i ->
            JObject
                [ "kind", JString(EvaluatorInputKind.toWire i.Kind)
                  "reference", JString i.Reference
                  "digest", JString i.Digest ])
        |> fun items -> JObject [ "schema", JString "ordo.evaluator-identity/1"; "inputs", JArray items ]
        |> renderCanonical

    /// Build the identity of an evaluator closure. Order of the supplied
    /// inputs does not matter; content does.
    let create (inputs: EvaluatorInput list) : Result<EvaluatorIdentity, EvaluatorIdentityError> =
        let sorted = inputs |> List.sortBy (fun i -> i.Reference, EvaluatorInputKind.toWire i.Kind)

        let duplicate =
            sorted
            |> List.countBy (fun i -> i.Reference)
            |> List.tryFind (fun (_, n) -> n > 1)

        if List.isEmpty inputs then
            Error NoEvaluatorInputs
        elif not (inputs |> List.exists (fun i -> i.Kind = GateCode)) then
            Error NoGateCode
        else
            match inputs |> List.tryFind (fun i -> String.IsNullOrWhiteSpace i.Digest), duplicate with
            | Some missing, _ -> Error(InputWithoutDigest missing.Reference)
            | None, Some(reference, _) -> Error(DuplicateInputReference reference)
            | None, None ->
                let hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical sorted))

                Ok
                    { inputs = sorted
                      fingerprint = "sha256:" + Convert.ToHexString(hash).ToLowerInvariant() }

    let fingerprint (identity: EvaluatorIdentity) = identity.Fingerprint

    let sameAs (other: EvaluatorIdentity) (identity: EvaluatorIdentity) = identity.Fingerprint = other.Fingerprint

    /// Every input that differs between a baseline and a current identity.
    let changes (baseline: EvaluatorIdentity) (current: EvaluatorIdentity) : EvaluatorChange list =
        let index (xs: EvaluatorInput list) = xs |> List.map (fun i -> i.Reference, i) |> Map.ofList
        let before = index baseline.Inputs
        let after = index current.Inputs

        let removedOrChanged =
            before
            |> Map.toList
            |> List.choose (fun (reference, b) ->
                match after.TryFind reference with
                | None -> Some(InputRemoved b)
                | Some a when a <> b -> Some(InputChanged(b, a))
                | Some _ -> None)

        let added =
            after
            |> Map.toList
            |> List.choose (fun (reference, a) -> if before.ContainsKey reference then None else Some(InputAdded a))

        removedOrChanged @ added

    /// Whether a path-like reference belongs to the evaluator closure. Used
    /// by mutation-boundary checks so evaluator artifacts stay outside an
    /// implementation's writable set even when colocated (ORD-EXEC-126).
    let contains (reference: string) (identity: EvaluatorIdentity) =
        identity.Inputs |> List.exists (fun i -> i.Reference = reference)

/// What a verifier observed, before Ordo decides what it may be reported as.
type RawVerdict =
    | VerdictPassed
    | VerdictFailed of reason: string

/// What an evaluation may honestly claim (ORD-EXEC-094 / ORD-EXEC-095 /
/// ORD-EXEC-050).
///
/// `EvaluatorChanged` and `EvaluatorUnavailable` are neither pass nor fail.
/// They cannot be converted into either: a new verification execution under
/// an identified evaluator is the only way forward (ORD-EXEC-052).
type EvaluationOutcome =
    | Passed of evaluator: string
    | Failed of evaluator: string * reason: string
    | EvaluatorChanged of baseline: string * current: string * changes: EvaluatorChange list
    | EvaluatorUnavailable of reason: string

[<RequireQualifiedAccess>]
module Evaluation =

    /// Judge a verdict against the evaluator identity declared at baseline
    /// and the identity observed at verdict time.
    ///
    /// No explanation parameter exists on purpose: prose that a change was
    /// harmless cannot turn a changed evaluator into a pass (ORD-EXEC-096).
    let judge
        (baseline: EvaluatorIdentity)
        (atVerdict: Result<EvaluatorIdentity, string>)
        (verdict: RawVerdict)
        : EvaluationOutcome =
        match atVerdict with
        | Error reason -> EvaluatorUnavailable reason
        | Ok current when not (EvaluatorIdentity.sameAs baseline current) ->
            EvaluatorChanged(baseline.Fingerprint, current.Fingerprint, EvaluatorIdentity.changes baseline current)
        | Ok current ->
            match verdict with
            | VerdictPassed -> Passed current.Fingerprint
            | VerdictFailed reason -> Failed(current.Fingerprint, reason)

    /// A recorded verdict is only current while the evaluator it names is
    /// still the effective one. A stale verdict must be re-evaluated, never
    /// inherited by the new evaluator (ORD-EXEC-003).
    let isCurrent (current: EvaluatorIdentity) (outcome: EvaluationOutcome) =
        match outcome with
        | Passed fp
        | Failed(fp, _) -> fp = current.Fingerprint
        | EvaluatorChanged _
        | EvaluatorUnavailable _ -> false

    /// Only a current pass permits acceptance.
    let permitsAcceptance (current: EvaluatorIdentity) (outcome: EvaluationOutcome) =
        match outcome with
        | Passed _ -> isCurrent current outcome
        | Failed _
        | EvaluatorChanged _
        | EvaluatorUnavailable _ -> false

    let toWire outcome =
        match outcome with
        | Passed _ -> "passed"
        | Failed _ -> "failed"
        | EvaluatorChanged _ -> "evaluator-changed"
        | EvaluatorUnavailable _ -> "evaluator-unavailable"
