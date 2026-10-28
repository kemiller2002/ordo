/// `sde verification schedule`: the host side of policy-aware freshness
/// (DF-SDE-2026-0026). The judgement is tested in Ordo.Tests; this suite
/// covers `--reuse-context` parsing and the file adapter end to end.
module Sde.Tests.VerificationCommandTests

open System
open System.IO
open System.Security.Cryptography
open Xunit
open Sde.Core
open Sde.Cli
open Sde.Tests.Fixtures

let private planFixture = Path.Combine(repositoryRoot, "tests", "fixtures", "verification-plan", "plan.json")

let private declared =
    """"freshnessPolicies": [{"id":"FP-LIVE-PER-COMMIT","reusable":false},{"id":"FP-REVIEW-PER-COMMIT","reusable":false},{"id":"FP-DETERMINISTIC-INPUT-DIGEST","reusable":true}]"""

let private evidence =
    sprintf
        """{"schema":"causa.acceptance-evidence/1","run_id":"run0","source_sha":"src0","subject_type":"requirement","subject_id":"AR-002","status":"PASS","exit_code":0,"input_digest":"sha256:in1","plan_digest":"%s","rehearsal_id":"rehearsal-005"}"""
        (String('a', 64))

let private sha256 (text: string) =
    Convert.ToHexString(SHA256.HashData(Text.Encoding.UTF8.GetBytes text)).ToLowerInvariant()

/// Evidence paths resolve against the current directory, so the workspace
/// lives under it and is addressed relatively.
let private withWorkspace (body: string -> string list -> int * string list * string list) =
    let name = "verification-command-" + Guid.NewGuid().ToString("N")
    let dir = Path.Combine(Environment.CurrentDirectory, name)
    Directory.CreateDirectory dir |> ignore

    try
        File.WriteAllText(Path.Combine(dir, "plan.json"), File.ReadAllText(planFixture).Replace("\"freshnessPolicies\": []", declared))
        File.WriteAllText(Path.Combine(dir, "AR-002.json"), evidence)

        File.WriteAllText(
            Path.Combine(dir, "manifest.json"),
            sprintf """{"schema":"causa.acceptance-evidence-manifest/1","entries":[{"path":"%s/AR-002.json","sha256":"%s"}]}""" name (sha256 evidence)
        )

        File.WriteAllText(
            Path.Combine(dir, "reuse.json"),
            """{"schema":"ordo.verification-reuse-context/1","rehearsalId":"rehearsal-005","inputDigests":{"AR-002":"sha256:in1"}}"""
        )

        let argv =
            [ "verification"; "schedule"; "--plan"; Path.Combine(dir, "plan.json"); "--evidence-manifest"; Path.Combine(dir, "manifest.json")
              "--source-sha"; "src1"; "--run-id"; "run1"; "--json" ]

        body dir argv
    finally
        Directory.Delete(dir, true)

let private stateOf (stdout: string list) =
    Assert.Equal(1, stdout.Length)

    match Json.parse (List.head stdout) with
    | Error detail -> failwithf "stdout was not valid JSON: %s" detail
    | Ok document ->
        Json.tryArray (Json.tryField "requirements" document)
        |> Option.defaultValue []
        |> List.find (fun r -> Json.tryString (Json.tryField "id" r) = Some "AR-002")
        |> fun r -> Json.tryArray (Json.tryField "obligations" r) |> Option.defaultValue [] |> List.head
        |> fun o -> Json.tryString (Json.tryField "state" o)

[<Fact>]
let ``deterministic evidence of an earlier run is reused only with a reuse context`` () =
    let without, with' =
        withWorkspace (fun dir argv ->
            let code, stdout, stderr = Program.run argv repositoryRoot
            Assert.Equal(0, code)
            Assert.Empty stderr
            let reusedCode, reused, reusedErr = Program.run (argv @ [ "--reuse-context"; Path.Combine(dir, "reuse.json") ]) repositoryRoot
            Assert.Equal(0, reusedCode)
            Assert.Empty reusedErr
            0, stdout, reused)
        |> fun (_, a, b) -> a, b

    Assert.Equal(Some "stale", stateOf without)
    Assert.Equal(Some "satisfied", stateOf with')

[<Fact>]
let ``--reuse-context without evidence is a usage error`` () =
    match Args.parse [ "verification"; "schedule"; "--plan"; "p.json"; "--reuse-context"; "r.json" ] with
    | Args.ParseFailed message -> Assert.Contains("--reuse-context", message)
    | other -> failwithf "expected a usage error, got %A" other

[<Fact>]
let ``a malformed reuse context refuses the schedule`` () =
    withWorkspace (fun dir argv ->
        File.WriteAllText(Path.Combine(dir, "reuse.json"), """{"schema":"ordo.verification-reuse-context/9"}""")
        let code, stdout, _ = Program.run (argv @ [ "--reuse-context"; Path.Combine(dir, "reuse.json") ]) repositoryRoot
        Assert.Equal(1, code)
        Assert.Contains("ordo.verification-schedule-refusal/1", List.head stdout)
        Assert.Contains("reuse context", List.head stdout)
        code, stdout, [])
    |> ignore

[<Fact>]
let ``every option the verification help documents is accepted by the parser`` () =
    let pattern = Text.RegularExpressions.Regex(@"^\s+(--[a-z-]+)")

    let documented =
        Help.forCommand "verification" "0.0.0"
        |> List.choose (fun line ->
            let m = pattern.Match line
            if m.Success then Some m.Groups.[1].Value else None)

    Assert.Contains("--reuse-context", documented)

    for option in documented do
        match Args.parse [ "verification"; "schedule"; option; "x" ] with
        | Args.ParseFailed message when message.StartsWith "Unknown option" -> failwithf "help documents %s but the parser rejects it" option
        | _ -> ()
