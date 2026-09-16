/// Release-time tool that builds the SDE execution package from canonical
/// repository sources into distribution/dist.
///
/// It is deliberately NOT part of the published npm package and not a
/// subcommand of `sde`: consumers never build the payload, so putting it on
/// the public CLI would add a command to the product interface that no user
/// of the product can use. The npm `build` script invokes this; the decisions
/// it makes live in Sde.Core.PackageBuild.
module Sde.PackageBuilder.Program

open System
open System.IO
open Sde.Core

let private findRepositoryRoot () =
    // Walk up from the executable until the distribution directory that
    // carries the distribution map is found, so the tool works from a build
    // output directory as well as from a checkout.
    let rec search (directory: DirectoryInfo option) =
        match directory with
        | None -> None
        | Some directory ->
            let candidate =
                Path.Combine(directory.FullName, "distribution", PackageBuild.distributionMapFileName)

            if File.Exists candidate then
                Some directory.FullName
            else
                search (Option.ofObj directory.Parent)

    search (Some(DirectoryInfo(Directory.GetCurrentDirectory())))
    |> Option.orElseWith (fun () ->
        search (Option.ofObj (DirectoryInfo(AppContext.BaseDirectory).Parent)))

let private readPackageJsonField (packageJsonPath: string) (field: string) =
    match Json.parse (File.ReadAllText packageJsonPath) with
    | Error detail -> Error(sprintf "%s is not valid JSON: %s" packageJsonPath detail)
    | Ok root ->
        match Json.tryString (Json.tryField field root) with
        | Some value -> Ok value
        | None -> Error(sprintf "%s has no string %s field" packageJsonPath field)

[<EntryPoint>]
let main argv =
    let outputOverride =
        argv
        |> Array.tryFindIndex (fun arg -> arg = "--output")
        |> Option.bind (fun index -> Array.tryItem (index + 1) argv)

    match findRepositoryRoot () with
    | None ->
        eprintfn "Could not locate the repository root (no distribution/%s found above the working directory)." PackageBuild.distributionMapFileName
        1
    | Some repositoryRoot ->

    let distributionDirectory = Path.Combine(repositoryRoot, "distribution")
    let packageJsonPath = Path.Combine(distributionDirectory, "package.json")

    match readPackageJsonField packageJsonPath "name", readPackageJsonField packageJsonPath "version" with
    | Error detail, _
    | _, Error detail ->
        eprintfn "%s" detail
        1
    | Ok packageName, Ok packageVersion ->

    let outputDirectory =
        outputOverride
        |> Option.map (fun value -> Path.GetFullPath(Path.Combine(distributionDirectory, value)))
        |> Option.defaultValue (Path.Combine(distributionDirectory, Packaging.payloadDirectoryName))

    let inputs: PackageBuild.BuildInputs =
        { RepositoryRoot = repositoryRoot
          DistributionDirectory = distributionDirectory
          OutputDirectory = outputDirectory
          PackageName = packageName
          PackageVersion = packageVersion }

    match PackageBuild.build inputs with
    | Error detail ->
        eprintfn "%s" detail
        1
    | Ok result ->
        printfn
            "Built SDE execution package v%s (method v%s) at %s"
            result.Manifest.SdeVersion
            (result.Manifest.MethodVersion |> Option.defaultValue "unknown")
            result.OutputDirectory

        printfn
            "%d managed files, source revision %s."
            result.Manifest.Files.Length
            (result.Manifest.SourceRevision |> Option.defaultValue "unavailable")

        0
