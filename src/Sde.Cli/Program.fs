/// The CLI entry point.
///
/// Its whole job: parse argv, locate the bundled execution package, call one
/// lifecycle function, render the result, return an exit code. No lifecycle
/// decision is made in this file — if a question about repository state is
/// being answered here, it is in the wrong place.
module Sde.Cli.Program

open System
open System.IO
open System.Reflection
open Sde.Core
open Sde.Core.Lifecycle

/// The released version, taken from the assembly's informational version,
/// which MSBuild derives from distribution/package.json. There is no second
/// version constant anywhere in the codebase to drift from it.
let releaseVersion =
    let assembly = Assembly.GetExecutingAssembly()

    let informational =
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        |> Option.ofObj
        |> Option.map (fun attribute -> attribute.InformationalVersion)

    match informational with
    | Some value when not (String.IsNullOrWhiteSpace value) ->
        // Strip any build metadata MSBuild appends (for example the source
        // revision id) so the reported version equals the npm version.
        match value.Split '+' with
        | [||] -> value
        | parts -> parts.[0]
    | Some _
    | None ->
        match assembly.GetName().Version with
        | null -> "0.0.0"
        | version -> sprintf "%d.%d.%d" version.Major version.Minor version.Build

/// Finds the execution package this CLI carries.
///
/// In the published npm package the executable lives at
/// runtimes/<rid>/sde and the payload at dist/, two levels up. SDE_PAYLOAD_DIR
/// overrides the search and exists for development and tests, where the
/// executable is somewhere under bin/ instead.
let locatePayloadDirectory () : Result<string, string> =
    let fromEnvironment =
        Environment.GetEnvironmentVariable "SDE_PAYLOAD_DIR"
        |> Option.ofObj
        |> Option.filter (fun value -> not (String.IsNullOrWhiteSpace value))

    match fromEnvironment with
    | Some directory ->
        if File.Exists(Path.Combine(directory, Ownership.manifestName)) then
            Ok directory
        else
            Error(
                sprintf
                    "SDE_PAYLOAD_DIR is set to %s but there is no %s there."
                    (Json.quote directory)
                    Ownership.manifestName
            )
    | None ->

    let executableDirectory =
        Environment.ProcessPath
        |> Option.ofObj
        |> Option.defaultValue (Assembly.GetExecutingAssembly().Location)
        |> Paths.directoryName

    let candidates =
        [ Path.Combine(executableDirectory, "..", "..", Packaging.payloadDirectoryName)
          Path.Combine(executableDirectory, Packaging.payloadDirectoryName) ]

    match candidates |> List.tryFind (fun candidate -> File.Exists(Path.Combine(candidate, Ownership.manifestName))) with
    | Some found -> Ok(Path.GetFullPath found)
    | None ->
        Error(
            sprintf
                "This %s installation has no built execution package (%s/%s is missing). If you are developing this package from source, run `npm run build` first."
                Packaging.packageName
                Packaging.payloadDirectoryName
                Ownership.manifestName
        )

// ---------------------------------------------------------------------------

type private Emission =
    { ExitCode: int
      /// Lines for stdout. In --json mode this is exactly one JSON document.
      Stdout: string list
      /// Lines for stderr. Diagnostics go here so that --json stdout stays
      /// a clean document even when something went wrong.
      Stderr: string list }

let private ok lines =
    { ExitCode = ExitCodes.success
      Stdout = lines
      Stderr = [] }

let private emit (globals: Args.GlobalOptions) (exitCode: int) (json: unit -> Json.JsonValue) (human: unit -> string list) =
    if globals.Json then
        { ExitCode = exitCode
          Stdout = [ Json.render (json ()) ]
          Stderr = [] }
    elif exitCode = ExitCodes.success then
        { ExitCode = exitCode
          Stdout = human ()
          Stderr = [] }
    else
        // Non-zero results go to stderr, matching the behaviour every
        // released version has had: a failing command's output must not be
        // consumed as if it were a successful result.
        { ExitCode = exitCode
          Stdout = []
          Stderr = human () }

let private runCommand (invocation: Args.Invocation) (projectRoot: string) : Emission =
    let globals = invocation.Global

    match invocation.Command with
    | Args.Version ->
        if globals.Json then
            ok
                [ Json.render (
                      Json.JObject
                          [ "schemaVersion", Json.JInt Output.schemaVersion
                            "command", Json.JString "version"
                            "tool", Json.JString InstallationRecord.toolName
                            "package", Json.JString Packaging.packageName
                            "version", Json.JString releaseVersion ]
                  ) ]
        else
            ok [ releaseVersion ]

    | Args.Help None -> ok (Help.overview releaseVersion)
    | Args.Help(Some topic) -> ok (Help.forCommand topic releaseVersion)

    | Args.Init _
    | Args.Status _
    | Args.Verify _
    | Args.Upgrade _
    | Args.Doctor _ ->

        let commandName =
            match invocation.Command with
            | Args.Init _ -> "init"
            | Args.Status _ -> "status"
            | Args.Verify _ -> "verify"
            | Args.Upgrade _ -> "upgrade"
            | Args.Doctor _ -> "doctor"
            | Args.Help _
            | Args.Version -> "sde"

        match locatePayloadDirectory () |> Result.bind readPayload with
        | Error message ->
            emit
                globals
                ExitCodes.failure
                (fun () -> Output.errorToJson commandName message ExitCodes.failure)
                (fun () -> [ message ])
        | Ok payload ->

        match invocation.Command with
        | Args.Status _ ->
            let report = getStatus projectRoot payload
            emit globals (statusExitCode report) (fun () -> Output.statusToJson report) (fun () -> Render.status globals.Verbose report)

        | Args.Verify options ->
            let report = verify projectRoot payload options.Strict
            emit globals (verifyExitCode report) (fun () -> Output.verifyToJson report) (fun () -> Render.verify globals.Verbose report)

        | Args.Doctor _ ->
            let report = diagnose projectRoot payload
            emit globals (doctorExitCode report) (fun () -> Output.doctorToJson report) (fun () -> Render.doctor report)

        | Args.Init options ->
            let report = initialize projectRoot payload options.DryRun

            emit
                globals
                (changeExitCode options.Check report)
                (fun () -> Output.changeReportToJson options.Check options.DryRun report)
                (fun () -> Render.change globals.Verbose options.Check options.DryRun report)

        | Args.Upgrade options ->
            let report = performUpgrade projectRoot payload options.DryRun

            emit
                globals
                (changeExitCode options.Check report)
                (fun () -> Output.changeReportToJson options.Check options.DryRun report)
                (fun () -> Render.change globals.Verbose options.Check options.DryRun report)

        | Args.Help _
        | Args.Version -> ok []

/// Runs one invocation. Exposed separately from `main` so tests can drive the
/// whole CLI without spawning a process or capturing console output.
let run (argv: string list) (projectRoot: string) : int * string list * string list =
    let emission =
        match Args.parse argv with
        | Args.ParseFailed message ->
            { ExitCode = ExitCodes.invalidArguments
              Stdout = []
              Stderr = [ message ] }
        | Args.Parsed invocation ->
            // Any command that touches an existing installation can meet a
            // filesystem state this tool deliberately refuses to handle: a
            // managed path replaced by a symlink, a permission error. Those
            // are reported as a clean command failure, never a raw stack
            // trace, so exit-code-only automation and a human both get an
            // actionable message.
            try
                runCommand invocation projectRoot
            with ex ->
                let message = sprintf "sde failed: %s" ex.Message

                if invocation.Global.Json then
                    { ExitCode = ExitCodes.failure
                      Stdout = [ Json.render (Output.errorToJson "sde" message ExitCodes.failure) ]
                      Stderr = [] }
                else
                    { ExitCode = ExitCodes.failure
                      Stdout = []
                      Stderr = [ message ] }

    emission.ExitCode, emission.Stdout, emission.Stderr

[<EntryPoint>]
let main argv =
    let exitCode, stdout, stderr = run (List.ofArray argv) (Directory.GetCurrentDirectory())

    for line in stdout do
        Console.Out.WriteLine line

    for line in stderr do
        Console.Error.WriteLine line

    exitCode
