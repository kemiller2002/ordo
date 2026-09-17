/// Command-line surface over `Ordo.Complexity.Measure`.
///
///   ordo-complexity <file>...                     complexity of files on disk
///   ordo-complexity --delta <repo> <base> <head>  complexity ADDED by a range
///   ordo-complexity --tokens <file>               every counted token, with its line
///
/// Output is CSV on stdout in every mode, so a measurement can be committed
/// beside the record that cites it.
module Ordo.Complexity.Program

open System.IO
open Ordo.Complexity.Measure

let private reportFiles (paths: string array) =
    let results =
        paths
        |> Array.filter isFSharp
        |> Array.filter File.Exists
        |> Array.map (fun path -> complexityOf path (File.ReadAllText path))
        |> Array.sortBy (fun result -> result.Path)

    printfn "path,cyclomatic,branches,lines"
    results |> Array.iter (fun r -> printfn "%s,%d,%d,%d" r.Path r.Cyclomatic r.Branches r.Lines)

    let branches = results |> Array.sumBy (fun r -> r.Branches)
    let lines = results |> Array.sumBy (fun r -> r.Lines)
    printfn "TOTAL,%d,%d,%d" (branches + Array.length results) branches lines

let private reportDelta (repo: string) (baseRev: string) (headRev: string) =
    let deltas =
        changedFSharpFiles repo baseRev headRev
        |> List.map (deltaOf repo baseRev headRev)

    printfn "path,base_branches,head_branches,added_branches,base_lines,head_lines,added_lines"

    deltas
    |> List.iter (fun d ->
        printfn
            "%s,%d,%d,%d,%d,%d,%d"
            d.Path
            d.Base.Branches
            d.Head.Branches
            d.Added
            d.Base.Lines
            d.Head.Lines
            d.LinesAdded)

    let added = deltas |> List.sumBy (fun d -> d.Added)
    let addedLines = deltas |> List.sumBy (fun d -> d.LinesAdded)
    let touched = List.length deltas
    let created = deltas |> List.filter (fun d -> d.Base.Lines = 0) |> List.length

    printfn "TOTAL,,,%d,,,%d" added addedLines
    printfn "FILES_TOUCHED,%d" touched
    printfn "FILES_CREATED,%d" created

[<EntryPoint>]
let main argv =
    match Array.toList argv with
    | "--delta" :: repo :: baseRev :: headRev :: _ ->
        reportDelta repo baseRev headRev
        0
    | "--delta" :: _ ->
        eprintfn "usage: ordo-complexity --delta <repo> <base-rev> <head-rev>"
        2
    | "--tokens" :: path :: _ ->
        reportTokens path
        0
    | "--tokens" :: _ ->
        eprintfn "usage: ordo-complexity --tokens <file>"
        2
    | _ ->
        reportFiles argv
        0
