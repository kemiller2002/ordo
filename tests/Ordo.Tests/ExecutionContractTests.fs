/// `ordo.execution-contract/1`: the governing contract a host such as Praxis
/// reads before it creates an execution (ORDO-CORE-PACKAGE).
module Ordo.Tests.ExecutionContractTests

open Xunit
open Ordo.Core.ExecutionRole
open Ordo.Core.Evaluator
open Ordo.Core.MutationBoundary
open Ordo.Core.ExecutionContract

let private valid =
    """{
  "schema": "ordo.execution-contract/1",
  "role": "implementation",
  "boundary": {
    "scopes": ["feature:checkout"],
    "projections": [{ "scope": "feature:checkout", "patterns": ["src/checkout/**"] }],
    "evaluatorReferences": ["tests/acceptance/**"]
  },
  "evaluator": [
    { "kind": "gate-code", "reference": "scripts/gate.sh" },
    { "kind": "configuration", "reference": "gate.json" }
  ],
  "humanOnly": ["complete"]
}"""

let private refused (text: string) =
    match ExecutionContract.parse text with
    | Ok contract -> failwithf "expected a refusal, got %A" contract
    | Error error -> error

[<Fact>]
let ``a valid contract supplies the role, boundary, evaluator closure and human-only transitions`` () =
    match ExecutionContract.parse valid with
    | Error error -> failwith (ContractError.describe error)
    | Ok contract ->
        Assert.Equal(Implementation, contract.Role)
        Assert.Equal<SemanticScope list>([ Feature "checkout" ], contract.Boundary.Scopes)
        Assert.Equal<string list>([ "complete" ], contract.HumanOnly)
        Assert.Equal(2, contract.Evaluator.Length)
        // The evaluator closure is evaluation authority and never writable.
        Assert.Equal<string list>(
            [ "tests/acceptance/**"; "scripts/gate.sh"; "gate.json" ],
            contract.Boundary.EvaluatorReferences
        )
        Assert.Equal(EvaluatorAuthorityMutation, MutationBoundary.classify contract.Boundary "scripts/gate.sh")

[<Fact>]
let ``the contract's authority is Ordo's role matrix`` () =
    for role in [ Specification; Implementation; Verification; Review; Integration; Administration ] do
        let text = valid.Replace("\"implementation\"", $"\"{ExecutionRole.toWire role}\"")

        match ExecutionContract.parse text with
        | Error error -> failwith (ContractError.describe error)
        | Ok contract -> Assert.Equal(RoleAuthority.defaultFor role, ExecutionContract.authority contract)

[<Fact>]
let ``the evaluator identity is built from host-observed digests and refuses an unobserved input`` () =
    match ExecutionContract.parse valid with
    | Error error -> failwith (ContractError.describe error)
    | Ok contract ->
        let observed = Map [ "scripts/gate.sh", "sha256:aa"; "gate.json", "sha256:bb" ]

        match ExecutionContract.evaluatorIdentity (fun r -> Map.tryFind r observed) contract with
        | Error error -> failwithf "%A" error
        | Ok identity -> Assert.StartsWith("sha256:", EvaluatorIdentity.fingerprint identity)

        Assert.Equal(
            Error(InputWithoutDigest "gate.json"),
            ExecutionContract.evaluatorIdentity (fun r -> if r = "gate.json" then None else Some "sha256:aa") contract
            |> Result.map EvaluatorIdentity.fingerprint
        )

[<Fact>]
let ``semantic violations are typed refusals`` () =
    Assert.Equal(WrongSchema "ordo.execution/1", refused (valid.Replace("ordo.execution-contract/1", "ordo.execution/1")))
    Assert.Equal(UnknownRole "wizard", refused (valid.Replace("\"implementation\"", "\"wizard\"")))
    Assert.Equal(InvalidScope "checkout", refused (valid.Replace("[\"feature:checkout\"]", "[\"checkout\"]")))

    Assert.Equal(
        ProjectionOutsideScopes "feature:checkout",
        refused (valid.Replace("\"scopes\": [\"feature:checkout\"]", "\"scopes\": [\"feature:other\"]"))
    )

    Assert.Equal(UnknownEvaluatorKind "magic", refused (valid.Replace("\"configuration\"", "\"magic\"")))
    Assert.Equal(EvaluatorWithoutGateCode, refused (valid.Replace("\"gate-code\"", "\"fixture\"")))
    Assert.Equal(DuplicateEvaluatorReference "gate.json", refused (valid.Replace("scripts/gate.sh", "gate.json")))

    match refused "{ not json" with
    | InvalidDocument _ -> ()
    | other -> failwithf "expected InvalidDocument, got %A" other

[<Fact>]
let ``a contract round-trips through its wire shape`` () =
    match ExecutionContract.parse valid with
    | Error error -> failwith (ContractError.describe error)
    | Ok contract ->
        let text = Ordo.Core.Json.render (ExecutionContract.toJson contract)
        Assert.Equal(Ok contract, ExecutionContract.parse text)

[<Fact>]
let ``Ordo.Core is packaged as EchelonFoundry.Ordo.Core`` () =
    let rec root (dir: System.IO.DirectoryInfo) =
        if System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Directory.Build.props")) then dir.FullName
        else
            match dir.Parent with
            | null -> failwith "repository root not found"
            | parent -> root parent

    let project =
        System.IO.File.ReadAllText(
            System.IO.Path.Combine(root (System.IO.DirectoryInfo(System.AppContext.BaseDirectory)), "src", "Ordo.Core", "Ordo.Core.fsproj")
        )

    Assert.Contains("<IsPackable>true</IsPackable>", project)
    Assert.Contains("<PackageId>EchelonFoundry.Ordo.Core</PackageId>", project)
