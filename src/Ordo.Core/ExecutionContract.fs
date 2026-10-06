/// The governing execution contract (`ordo.execution-contract/1`): what an
/// execution is authorized to be before it starts (ORD-EXEC-010, 030, 090).
///
/// An envelope (`ordo.execution/1`) describes a running execution. The
/// contract is the authority a host such as Praxis receives before creating
/// one: the semantic role (whose capability set is Ordo's role authority),
/// the semantic mutation boundary with its physical projections, the
/// evaluator closure the host must observe and fingerprint, and the
/// transitions only a human may exercise. A host parses and validates it here
/// rather than re-implementing the rules, so the boundary and evaluator it
/// enforces are traceable to Ordo's semantics.
module Ordo.Core.ExecutionContract

open System
open Ordo.Core.Json
open Ordo.Core.ExecutionRole
open Ordo.Core.Evaluator
open Ordo.Core.MutationBoundary

[<Literal>]
let SchemaId = "ordo.execution-contract/1"

/// One member of the evaluator closure the contract requires. The host
/// observes its content digest; the contract names what and why.
type EvaluatorRequirement =
    { Kind: EvaluatorInputKind
      Reference: string }

type ExecutionContract =
    { Role: ExecutionRole
      Boundary: MutationBoundary
      Evaluator: EvaluatorRequirement list
      HumanOnly: string list }

/// Why a contract is refused. Each case is a semantic rule, not a parse
/// accident, so a host can explain the refusal without inventing wording.
type ContractError =
    | InvalidDocument of JsonError
    | WrongSchema of actual: string
    | UnknownRole of raw: string
    | InvalidScope of raw: string
    | ProjectionOutsideScopes of scope: string
    | UnknownEvaluatorKind of raw: string
    | EvaluatorWithoutGateCode
    | DuplicateEvaluatorReference of reference: string
    | EmptyReference of path: string

[<RequireQualifiedAccess>]
module ContractError =

    let describe error =
        match error with
        | InvalidDocument(MalformedJson message) -> $"the contract is not valid JSON: {message}"
        | InvalidDocument(UnexpectedType(path, expected)) -> $"{path} must be {expected}"
        | InvalidDocument(MissingMember path) -> $"{path} is required"
        | InvalidDocument(UnsupportedNumber path) -> $"{path} is not a supported number"
        | WrongSchema actual -> $"schema must be {SchemaId}, got '{actual}'"
        | UnknownRole raw -> $"unknown execution role '{raw}'"
        | InvalidScope raw -> $"'{raw}' is not a semantic scope (feature:|cluster:|authority:|capability:ID)"
        | ProjectionOutsideScopes scope -> $"a projection names scope '{scope}', which the boundary does not authorize"
        | UnknownEvaluatorKind raw -> $"unknown evaluator input kind '{raw}'"
        | EvaluatorWithoutGateCode -> "an evaluator closure must include its gate code"
        | DuplicateEvaluatorReference reference -> $"evaluator reference '{reference}' is listed more than once"
        | EmptyReference path -> $"{path} must not be empty"

[<RequireQualifiedAccess>]
module ExecutionContract =

    let private json result = result |> Result.mapError InvalidDocument

    let private strings path (value: JsonValue option) =
        match value with
        | None
        | Some JNull -> Ok []
        | Some v -> json (asArray path v |> Result.bind (List.mapi (fun i s -> asString $"{path}[{i}]" s) >> collect))

    let private member' name value = json (tryMember name value)

    let private scope (raw: string) =
        match SemanticScope.fromWire raw with
        | Some s -> Ok s
        | None -> Error(InvalidScope raw)

    let rec private traverse (f: 'a -> Result<'b, ContractError>) (items: 'a list) =
        match items with
        | [] -> Ok []
        | head :: tail -> f head |> Result.bind (fun h -> traverse f tail |> Result.map (fun t -> h :: t))

    let private nonEmpty path (value: string) =
        if String.IsNullOrWhiteSpace value then Error(EmptyReference path) else Ok value

    let private projection index (value: JsonValue) =
        let path = $"$.boundary.projections[{index}]"

        json (requiredMember "scope" value |> Result.bind (asString (path + ".scope")))
        |> Result.bind scope
        |> Result.bind (fun s ->
            member' "patterns" value
            |> Result.bind (strings (path + ".patterns"))
            |> Result.map (fun patterns -> { Scope = s; Patterns = patterns }))

    let private boundary (value: JsonValue option) =
        match value with
        | None
        | Some JNull -> Ok MutationBoundary.empty
        | Some b ->
            member' "scopes" b
            |> Result.bind (strings "$.boundary.scopes")
            |> Result.bind (traverse scope)
            |> Result.bind (fun scopes ->
                member' "projections" b
                |> Result.bind (fun p ->
                    match p with
                    | None
                    | Some JNull -> Ok []
                    | Some v -> json (asArray "$.boundary.projections" v))
                |> Result.bind (List.mapi (fun i v -> i, v) >> traverse (fun (i, v) -> projection i v))
                |> Result.bind (fun projections ->
                    match projections |> List.tryFind (fun p -> not (List.contains p.Scope scopes)) with
                    | Some outside -> Error(ProjectionOutsideScopes(SemanticScope.toWire outside.Scope))
                    | None ->
                        member' "evaluatorReferences" b
                        |> Result.bind (strings "$.boundary.evaluatorReferences")
                        |> Result.map (fun refs ->
                            { Scopes = scopes
                              Projections = projections
                              EvaluatorReferences = refs })))

    let private requirement index (value: JsonValue) =
        let path = $"$.evaluator[{index}]"

        json (requiredMember "kind" value |> Result.bind (asString (path + ".kind")))
        |> Result.bind (fun raw ->
            match EvaluatorInputKind.fromWire raw with
            | None -> Error(UnknownEvaluatorKind raw)
            | Some kind ->
                json (requiredMember "reference" value |> Result.bind (asString (path + ".reference")))
                |> Result.bind (nonEmpty (path + ".reference"))
                |> Result.map (fun reference -> { Kind = kind; Reference = reference }))

    let private evaluator (value: JsonValue option) =
        match value with
        | None
        | Some JNull -> Ok []
        | Some v ->
            json (asArray "$.evaluator" v)
            |> Result.bind (List.mapi (fun i v -> i, v) >> traverse (fun (i, v) -> requirement i v))
            |> Result.bind (fun requirements ->
                let duplicate =
                    requirements |> List.countBy _.Reference |> List.tryFind (fun (_, n) -> n > 1)

                match duplicate with
                | Some(reference, _) -> Error(DuplicateEvaluatorReference reference)
                | None when not requirements.IsEmpty && not (requirements |> List.exists (fun r -> r.Kind = GateCode)) ->
                    Error EvaluatorWithoutGateCode
                | None -> Ok requirements)

    /// The evaluator closure is evaluation authority, so it is always outside
    /// the writable set (ORD-EXEC-126): every closure reference is added to
    /// the boundary's evaluator references.
    let private protectEvaluator (requirements: EvaluatorRequirement list) (b: MutationBoundary) =
        let added = requirements |> List.map _.Reference

        { b with EvaluatorReferences = (b.EvaluatorReferences @ added) |> List.distinct }

    /// Read and validate a contract document.
    let fromJson (value: JsonValue) : Result<ExecutionContract, ContractError> =
        json (requiredMember "schema" value |> Result.bind (asString "$.schema"))
        |> Result.bind (fun schema -> if schema = SchemaId then Ok() else Error(WrongSchema schema))
        |> Result.bind (fun () -> json (requiredMember "role" value |> Result.bind (asString "$.role")))
        |> Result.bind (fun raw ->
            match ExecutionRole.fromWire raw with
            | Some role -> Ok role
            | None -> Error(UnknownRole raw))
        |> Result.bind (fun role ->
            member' "evaluator" value
            |> Result.bind evaluator
            |> Result.bind (fun requirements ->
                member' "boundary" value
                |> Result.bind boundary
                |> Result.bind (fun b ->
                    member' "humanOnly" value
                    |> Result.bind (strings "$.humanOnly")
                    |> Result.map (fun humanOnly ->
                        { Role = role
                          Boundary = protectEvaluator requirements b
                          Evaluator = requirements
                          HumanOnly = humanOnly |> List.distinct }))))

    let parse (text: string) = json (Json.parse text) |> Result.bind fromJson

    /// The role's capability authority, from Ordo's role matrix.
    let authority (contract: ExecutionContract) = RoleAuthority.defaultFor contract.Role

    /// The evaluator identity, given the digest the host observed for each
    /// required reference. A reference the host could not observe has no
    /// identity rather than a guessed one.
    let evaluatorIdentity (observeDigest: string -> string option) (contract: ExecutionContract) =
        contract.Evaluator
        |> List.map (fun r ->
            match observeDigest r.Reference with
            | Some digest -> Ok { EvaluatorInput.Kind = r.Kind; Reference = r.Reference; Digest = digest }
            | None -> Error(InputWithoutDigest r.Reference))
        |> List.fold
            (fun acc next ->
                match acc, next with
                | Ok items, Ok item -> Ok(item :: items)
                | Error e, _
                | _, Error e -> Error e)
            (Ok [])
        |> Result.bind (List.rev >> EvaluatorIdentity.create)

    let toJson (contract: ExecutionContract) : JsonValue =
        JObject
            [ "schema", JString SchemaId
              "role", JString(ExecutionRole.toWire contract.Role)
              "boundary",
              JObject
                  [ "scopes", JArray(contract.Boundary.Scopes |> List.map (SemanticScope.toWire >> JString))
                    "projections",
                    JArray(
                        contract.Boundary.Projections
                        |> List.map (fun p ->
                            JObject
                                [ "scope", JString(SemanticScope.toWire p.Scope)
                                  "patterns", JArray(p.Patterns |> List.map JString) ])
                    )
                    "evaluatorReferences", JArray(contract.Boundary.EvaluatorReferences |> List.map JString) ]
              "evaluator",
              JArray(
                  contract.Evaluator
                  |> List.map (fun r ->
                      JObject [ "kind", JString(EvaluatorInputKind.toWire r.Kind); "reference", JString r.Reference ])
              )
              "humanOnly", JArray(contract.HumanOnly |> List.map JString) ]
