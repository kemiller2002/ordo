/// `sde boundary assess` (`ordo boundary assess`): parsing, exit codes, the
/// `ordo.boundary-amplification/1` document and import extraction. The
/// judgement itself is tested in Ordo.Tests; this suite covers the host.
module Sde.Tests.BoundaryCommandTests

open System.IO
open Xunit
open Sde.Core
open Sde.Core.Lifecycle
open Sde.Cli
open Sde.Tests.Fixtures

let private fixtures = Path.Combine(repositoryRoot, "tests", "fixtures", "boundary-amplification")
let private mapFile = Path.Combine(fixtures, "praxis-boundaries.json")
let private fixture name file = Path.Combine(fixtures, name, file)

let private assess (extra: string list) name changed =
    [ "boundary"; "assess"; "--map"; mapFile; "--expected"; fixture name "expectation.json"; "--changed"; fixture name changed ]
    @ extra

let private parseJson (stdout: string list) =
    Assert.Equal(1, stdout.Length)

    match Json.parse (List.head stdout) with
    | Ok document -> document
    | Error detail -> failwithf "stdout was not valid JSON: %s" detail

[<Fact>]
let ``a high-risk assessment is still a successful assessment`` () =
    let exitCode, stdout, stderr =
        Program.run (assess [ "--json" ] "praxis-usage-pacing" "changed.json") repositoryRoot

    Assert.Equal(ExitCodes.success, exitCode)
    Assert.Empty stderr
    let document = parseJson stdout
    Assert.Equal(Some "ordo.boundary-amplification/1", Json.tryString (Json.tryField "schema" document))

    let risk = Json.tryField "risk" document
    Assert.Equal(Some "high", risk |> Option.bind (fun r -> Json.tryString (Json.tryField "level" r)))

    let recommendation = Json.tryField "recommendation" document

    Assert.Equal(
        Some "require-design-review",
        recommendation |> Option.bind (fun r -> Json.tryString (Json.tryField "kind" r))
    )

[<Fact>]
let ``the human rendering names risk, recommendation and approval`` () =
    let exitCode, stdout, _ =
        Program.run (assess [] "approved-cross-cutting" "changed.txt") repositoryRoot

    Assert.Equal(ExitCodes.success, exitCode)
    Assert.Contains(stdout, fun (l: string) -> l.Contains "risk:" && l.Contains "high")
    Assert.Contains(stdout, fun (l: string) -> l.Contains "consider-split-along")
    Assert.Contains(stdout, fun (l: string) -> l.Contains "approved EXP-CORRELATION-001 by architecture-owner")
    Assert.Contains(stdout, fun (l: string) -> l.Contains "downgraded from require-design-review")

[<Fact>]
let ``an invalid input document exits 6 with a reason, in either mode`` () =
    let dir = makeTempDir "sde-boundary-bad-"

    try
        let bad = Path.Combine(dir, "expectation.json")
        File.WriteAllText(bad, """{"schema":"ordo.boundary-expectation/1","workItem":"W","expectedBoundaries":["ghost"]}""")

        let argv = [ "boundary"; "assess"; "--map"; mapFile; "--expected"; bad; "--changed"; fixture "approved-cross-cutting" "changed.txt" ]

        let exitCode, stdout, stderr = Program.run argv dir
        Assert.Equal(ExitCodes.invalidInput, exitCode)
        Assert.Empty stdout
        Assert.Contains(stderr, fun (l: string) -> l.Contains "ghost")

        let exitCode, stdout, _ = Program.run (argv @ [ "--json" ]) dir
        Assert.Equal(ExitCodes.invalidInput, exitCode)
        let document = parseJson stdout
        Assert.Equal(Some "error", Json.tryString (Json.tryField "outcome" document))

        let missing, _, _ = Program.run [ "boundary"; "assess"; "--map"; Path.Combine(dir, "nope.json"); "--expected"; bad; "--changed"; bad ] dir
        Assert.Equal(ExitCodes.invalidInput, missing)
    finally
        cleanup [ dir ]

[<Theory>]
[<InlineData("boundary")>]
[<InlineData("boundary assess --map a --expected b")>]
[<InlineData("boundary assess --map a --expected b --changed c --bogus")>]
[<InlineData("boundary assess --map a --map b --expected b --changed c")>]
[<InlineData("boundary assess --map")>]
[<InlineData("boundary split --map a --expected b --changed c")>]
let ``malformed boundary arguments exit 2`` (argv: string) =
    let exitCode, stdout, stderr = Program.run (argv.Split ' ' |> List.ofArray) repositoryRoot
    Assert.Equal(ExitCodes.invalidArguments, exitCode)
    Assert.Empty stdout
    Assert.NotEmpty stderr

[<Fact>]
let ``boundary help exists and every option it documents is accepted`` () =
    let exitCode, stdout, _ = Program.run [ "boundary"; "--help" ] repositoryRoot
    Assert.Equal(ExitCodes.success, exitCode)
    Assert.Contains(stdout, fun (l: string) -> l.StartsWith "sde boundary assess")

    let optionPattern = System.Text.RegularExpressions.Regex(@"^\s+(--[a-z-]+)")

    let documented =
        Help.forCommand "boundary" payload.Version
        |> List.choose (fun line ->
            let m = optionPattern.Match line
            if m.Success then Some m.Groups.[1].Value else None)

    Assert.NotEmpty documented

    for option in documented do
        match Args.parse [ "boundary"; "assess"; option; "x" ] with
        | Args.ParseFailed message when message.StartsWith "Unknown option" ->
            failwithf "help documents %s but the parser rejects it" option
        | _ -> ()

[<Fact>]
let ``--root extracts imports so a misplaced effect becomes visible`` () =
    let dir = makeTempDir "sde-boundary-root-"

    try
        let source = Path.Combine(dir, "src", "Praxis.Cli")
        Directory.CreateDirectory source |> ignore

        File.WriteAllLines(
            Path.Combine(source, "PacingCommands.fs"),
            [ "module Praxis.Cli.PacingCommands"; "open System"; "open System.IO"; "open System.Net.Http // provider"; "open type System.Math" ]
        )

        let changed = Path.Combine(dir, "changed.txt")
        File.WriteAllText(changed, "src/Praxis.Cli/PacingCommands.fs\n")

        let expectation = Path.Combine(dir, "expectation.json")
        File.WriteAllText(expectation, """{"schema":"ordo.boundary-expectation/1","workItem":"W","expectedBoundaries":["cli"]}""")

        let argv = [ "boundary"; "assess"; "--map"; mapFile; "--expected"; expectation; "--changed"; changed; "--json" ]

        let _, withoutRoot, _ = Program.run argv dir
        let _, withRoot, _ = Program.run (argv @ [ "--root"; dir ]) dir

        let crossings stdout =
            parseJson stdout
            |> Json.tryField "unexpectedCrossings"
            |> Option.map (fun v ->
                match v with
                | Json.JArray items -> items |> List.choose (fun i -> Json.tryString (Some i))
                | _ -> [])

        Assert.Equal(Some [], crossings withoutRoot)
        Assert.Equal(Some [ "persistence"; "provider" ], crossings withRoot)
    finally
        cleanup [ dir ]

[<Fact>]
let ``imports are extracted from F# and C#, skipping aliases and type opens`` () =
    Assert.Equal<string list>(
        [ "System.IO"; "System.Net.Http" ],
        Boundary.extractImports "a.fs" [ "open System.IO"; "  open System.Net.Http // x"; "open type System.Math"; "let x = 1" ]
    )

    Assert.Equal<string list>(
        [ "System.IO" ],
        Boundary.extractImports "a.cs" [ "using System.IO;"; "using static System.Math;"; "using X = System.Text;"; "using (var s = f())" ]
    )

    Assert.Empty(Boundary.extractImports "a.md" [ "open System.IO" ])
