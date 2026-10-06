/// `.echelon/toolchain.json` names the Echelon tool versions a repository
/// expects (`echelon setup` installs them; `echelon doctor` checks them).
/// Ordo owns exactly one property in it, `ordo`, and keeps it equal to the
/// release that `init` or `upgrade` installs. Every other property belongs to
/// the repository or to another tool (Praxis owns `praxis`) and survives
/// byte for byte in value and position.
module Sde.Tests.ToolchainPinTests

open System.IO
open Xunit
open Sde.Core
open Sde.Core.Lifecycle
open Sde.Tests.Fixtures

let private toolchainPath (project: string) =
    Path.Combine(project, ".echelon", "toolchain.json")

let private writeToolchain (project: string) (content: string) =
    Directory.CreateDirectory(Path.Combine(project, ".echelon")) |> ignore
    File.WriteAllText(toolchainPath project, content)

let private readToolchain (project: string) : Json.JsonValue =
    match Json.parse (File.ReadAllText(toolchainPath project)) with
    | Ok document -> document
    | Error detail -> failwithf ".echelon/toolchain.json is not JSON: %s" detail

let private pinOf (key: string) (document: Json.JsonValue) = Json.tryString (Json.tryField key document)

let private keysOf (document: Json.JsonValue) =
    match document with
    | Json.JObject fields -> fields |> List.map fst
    | _ -> []

let private withProject (body: string -> unit) =
    let project = makeTempDir "sde-toolchain-"

    try
        body project
    finally
        cleanup [ project ]

let private stalePraxisAndOrdo =
    "{\n  \"schemaVersion\": 1,\n  \"ordo\": \"0.0.1\",\n  \"praxis\": \"3.7.1\"\n}\n"

[<Fact>]
let ``init creates the toolchain manifest pinning the release it installs`` () =
    withProject (fun project ->
        initialize project payload false |> ignore

        let toolchain = readToolchain project
        Assert.Equal(Some payload.Version, pinOf "ordo" toolchain)
        Assert.Equal(Some 1, Json.tryInt (Json.tryField "schemaVersion" toolchain)))

[<Fact>]
let ``the pinned version is the release version, the same one recorded as installed`` () =
    withProject (fun project ->
        initialize project payload false |> ignore

        match InstallationRecord.read project with
        | InstallationRecord.Present record -> Assert.Equal(Some record.InstalledVersion, pinOf "ordo" (readToolchain project))
        | other -> failwithf "expected an installation record, got %A" other

        Assert.Equal(packageJsonVersion, payload.Version))

[<Fact>]
let ``init adds the ordo pin to an existing manifest and keeps every other property`` () =
    withProject (fun project ->
        writeToolchain project "{\n  \"schemaVersion\": 1,\n  \"praxis\": \"3.7.1\",\n  \"custom\": {\"kept\": [1, 2.5]}\n}\n"

        initialize project payload false |> ignore

        let toolchain = readToolchain project
        Assert.Equal(Some payload.Version, pinOf "ordo" toolchain)
        Assert.Equal(Some "3.7.1", pinOf "praxis" toolchain)
        Assert.Equal<string list>([ "schemaVersion"; "praxis"; "custom"; "ordo" ], keysOf toolchain)
        // A non-integer number is carried through as a number, not rewritten.
        Assert.Contains("2.5", File.ReadAllText(toolchainPath project))
        Assert.DoesNotContain("\"2.5\"", File.ReadAllText(toolchainPath project)))

[<Fact>]
let ``upgrade re-pins only the ordo property`` () =
    withProject (fun project ->
        installHistoricalVersion "0.0.1" project
        writeToolchain project stalePraxisAndOrdo

        match (performUpgrade project payload false).Outcome with
        | Applied _ -> ()
        | other -> failwithf "expected Applied, got %A" other

        let toolchain = readToolchain project
        Assert.Equal(Some payload.Version, pinOf "ordo" toolchain)
        Assert.Equal(Some "3.7.1", pinOf "praxis" toolchain)
        Assert.Equal<string list>([ "schemaVersion"; "ordo"; "praxis" ], keysOf toolchain))

[<Fact>]
let ``upgrade corrects a stale pin on an otherwise current installation, then has nothing to do`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        writeToolchain project stalePraxisAndOrdo

        match (performUpgrade project payload false).Outcome with
        | Applied _ -> ()
        | other -> failwithf "expected Applied, got %A" other

        Assert.Equal(Some payload.Version, pinOf "ordo" (readToolchain project))
        Assert.Equal(Some "3.7.1", pinOf "praxis" (readToolchain project))

        let before = snapshotWithTimestamps project

        match (performUpgrade project payload false).Outcome with
        | AlreadyCurrent _ -> ()
        | other -> failwithf "expected AlreadyCurrent, got %A" other

        Assert.Equal<(string * string * System.DateTime) list>(before, snapshotWithTimestamps project))

[<Fact>]
let ``upgrade --check reports a stale pin as required change and writes nothing`` () =
    withProject (fun project ->
        initialize project payload false |> ignore
        writeToolchain project stalePraxisAndOrdo
        let before = snapshot project

        let report = performUpgrade project payload true

        Assert.Equal(ExitCodes.changesRequired, changeExitCode true report)
        Assert.Equal<(string * string) list>(before, snapshot project))

[<Fact>]
let ``a toolchain manifest that is not a plain JSON object is preserved untouched`` () =
    withProject (fun project ->
        let commented = "{\n  // pinned by hand\n  \"ordo\": \"0.0.1\"\n}\n"
        writeToolchain project commented

        initialize project payload false |> ignore

        Assert.Equal(commented, File.ReadAllText(toolchainPath project)))

[<Fact>]
let ``init leaves the pin alone when it does not upgrade an older installation`` () =
    withProject (fun project ->
        installHistoricalVersion "0.0.1" project
        writeToolchain project stalePraxisAndOrdo

        initialize project payload false |> ignore

        Assert.Equal(stalePraxisAndOrdo, File.ReadAllText(toolchainPath project)))

[<Fact>]
let ``the ordo repository pins the Ordo release it is about to publish`` () =
    let toolchain =
        match Json.parse (File.ReadAllText(Path.Combine(repositoryRoot, ".echelon", "toolchain.json"))) with
        | Ok document -> document
        | Error detail -> failwith detail

    Assert.Equal(Some packageJsonVersion, pinOf "ordo" toolchain)

[<Fact>]
let ``the release bump moves Ordo's own toolchain pin with the package version`` () =
    // Without this the release commit would publish a version the
    // repository's own toolchain manifest no longer names.
    let bump = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "ordo-release-bump.sh"))
    Assert.Contains(".echelon/toolchain.json", bump)
    Assert.Contains("git add distribution/package.json .echelon/toolchain.json .ros", bump)
