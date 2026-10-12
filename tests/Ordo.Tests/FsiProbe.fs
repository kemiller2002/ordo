/// Compile-negative evidence. A probe is an F# script compiled against the
/// real built assemblies by the real compiler (`dotnet fsi`). A probe that is
/// expected to fail must fail with the named compiler error, so a probe that
/// fails for an unrelated reason (a typo, a missing reference) is not counted
/// as proof. Every group carries a positive control that must compile.
module Ordo.Tests.FsiProbe

open System
open System.Diagnostics
open System.IO

type ProbeResult = { ExitCode: int; Output: string }

let private dotnet () =
    match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
    | null | "" -> "dotnet"
    | path -> path

/// Runs one script in a fresh temporary directory and removes it afterwards.
let run (references: string list) (body: string) : ProbeResult =
    let directory = Directory.CreateTempSubdirectory("ordo-probe-").FullName
    try
        let script = Path.Combine(directory, "probe.fsx")
        let header = references |> List.map (sprintf "#r @\"%s\"") |> String.concat "\n"
        File.WriteAllText(script, header + "\n" + body + "\n")
        let start = ProcessStartInfo(dotnet (), sprintf "fsi --nologo --quiet \"%s\"" script)
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        start.WorkingDirectory <- directory
        use probe =
            match Process.Start start with
            | null -> failwith "could not start dotnet fsi"
            | started -> started
        let stdout = probe.StandardOutput.ReadToEndAsync()
        let stderr = probe.StandardError.ReadToEnd()
        probe.WaitForExit()
        { ExitCode = probe.ExitCode; Output = stdout.Result + stderr }
    finally
        Directory.Delete(directory, true)

/// Runs probes concurrently; each is (label, script, expected error code or None for "must compile").
let check (references: string list) (probes: (string * string * string option) list) =
    probes
    |> List.map (fun (label, body, expected) -> async { return label, expected, run references body })
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Array.toList
    |> List.choose (fun (label, expected, result) ->
        match expected with
        | None when result.ExitCode <> 0 -> Some(sprintf "%s: positive control failed to compile:\n%s" label result.Output)
        | None -> None
        | Some code when result.ExitCode = 0 -> Some(sprintf "%s: compiled, but %s was expected" label code)
        | Some code when not (result.Output.Contains code) -> Some(sprintf "%s: failed without %s:\n%s" label code result.Output)
        | Some _ -> None)
