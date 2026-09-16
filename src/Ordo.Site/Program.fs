/// Command line for the Ordo site generator.
///
///     dotnet run --project src/Ordo.Site -- build     writes ./dist
///     dotnet run --project src/Ordo.Site -- validate  checks without writing
///
/// `build` validates first and writes only on success, so `validate` and
/// `build` can never disagree about whether the site is publishable.
module Ordo.Site.Program

open System
open Ordo.Site.Build

type private Command =
    | Build
    | Validate
    | Help
    | Unknown of string

type private Options =
    { Command: Command
      SiteRoot: string
      OutputDirectory: string }

let private defaults =
    { Command = Help
      SiteRoot = "site"
      OutputDirectory = "dist" }

let rec private parseArgs (options: Options) (arguments: string list) : Result<Options, string> =
    match arguments with
    | [] -> Ok options
    | "build" :: rest -> parseArgs { options with Command = Build } rest
    | "validate" :: rest -> parseArgs { options with Command = Validate } rest
    | ("--help" | "-h" | "help") :: rest -> parseArgs { options with Command = Help } rest
    | "--site" :: value :: rest -> parseArgs { options with SiteRoot = value } rest
    | "--out" :: value :: rest -> parseArgs { options with OutputDirectory = value } rest
    | ("--site" | "--out") :: [] -> Error "option needs a value"
    | other :: _ -> Ok { options with Command = Unknown other }

let private helpText =
    String.concat
        "\n"
        [ "Ordo site generator"
          ""
          "Usage:"
          "  ordo-site build [--site DIR] [--out DIR]     generate and write the site"
          "  ordo-site validate [--site DIR]              generate in memory and check it"
          ""
          "Every build runs the full validation suite first. Nothing is written unless"
          "every page, link, citation and generated JSON document passes." ]

let private report (write: string -> unit) (messages: string list) =
    write ("Site build failed with " + string (List.length messages) + " problem(s):")
    messages |> List.iter (fun message -> write ("  - " + message))

/// Runs one invocation. `out` and `err` are passed in rather than written
/// directly so the test suite can assert on them without capturing a console.
let run (out: string -> unit) (err: string -> unit) (argv: string list) : int =
    match parseArgs defaults argv with
    | Error message ->
        err message
        2
    | Ok options ->
        match options.Command with
        | Help ->
            out helpText
            0
        | Unknown argument ->
            err ("unrecognised argument '" + argument + "'")
            err helpText
            2
        | Validate ->
            (match assemble (layoutFor options.SiteRoot options.OutputDirectory) with
             | Ok(outputs, assets) ->
                 out (
                     "Validated "
                     + string (List.length outputs)
                     + " generated files and "
                     + string (List.length assets)
                     + " assets. Nothing written."
                 )

                 0
             | Error messages ->
                 report err messages
                 1)
        | Build ->
            let layout = layoutFor options.SiteRoot options.OutputDirectory

            match assemble layout with
            | Ok(outputs, assets) ->
                write layout outputs assets

                out (
                    "Wrote "
                    + string (List.length outputs)
                    + " files and "
                    + string (List.length assets)
                    + " assets to "
                    + options.OutputDirectory
                )

                0
            | Error messages ->
                report err messages
                1

[<EntryPoint>]
let main argv =
    run (fun line -> Console.Out.WriteLine line) (fun line -> Console.Error.WriteLine line) (List.ofArray argv)
