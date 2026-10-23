/// `construct <handoff.json> <experiment-root>`: reads the handoff and the
/// source documents it cites (relative to the handoff's directory), runs the
/// pure construction, and writes the generated F# and the trace under the
/// experiment root. Exit codes: 0 written, 2 usage, 3 refused.
module Echelon.Construct.Program

open System
open System.IO
open System.Text

let private sourceDigests (handoffPath: string) (json: string) =
    match Handoff.read json with
    | Error _ -> Map.empty
    | Ok h ->
        let directory = Path.GetDirectoryName(Path.GetFullPath handoffPath) |> Option.ofObj |> Option.defaultValue "."
        (h.Requirements |> List.map _.Source.Path) @ (h.Decisions |> List.map _.Source.Path)
        |> List.distinct
        |> List.choose (fun relative ->
            let full = Path.Combine(directory, relative)
            if File.Exists full then Some(relative, Trace.sha256Hex (File.ReadAllBytes full)) else None)
        |> Map.ofList

let private write (root: string) (path: string, content: string) =
    let full = Path.Combine(root, path)
    Directory.CreateDirectory(Path.GetDirectoryName full |> Option.ofObj |> Option.defaultValue root) |> ignore
    // LF only and no BOM, so output is byte-identical on every platform.
    File.WriteAllBytes(full, UTF8Encoding(false).GetBytes(content.Replace("\r\n", "\n")))

[<EntryPoint>]
let main argv =
    match argv with
    | [| handoffPath; root |] ->
        let bytes = File.ReadAllBytes handoffPath
        let digests = sourceDigests handoffPath (Encoding.UTF8.GetString bytes)
        match Construct.run bytes digests with
        | Ok output ->
            output.Files |> List.iter (write root)
            output.Files |> List.iter (fun (path, content) -> printfn "%s  %s" (Trace.sha256Text content) path)
            0
        | Error e ->
            eprintfn "construction refused:\n%A" e
            3
    | _ ->
        eprintfn "usage: construct <handoff.json> <experiment-root>"
        2
