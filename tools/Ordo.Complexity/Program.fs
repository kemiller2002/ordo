/// Command-line surface over `Ordo.Complexity.Measure`.
///
///   ordo-complexity <file>...                     complexity of files on disk
///   ordo-complexity --delta <repo> <base> <head>  complexity ADDED by a range
///   ordo-complexity --tokens <file>               every counted token, with its line
///   ordo-complexity --csharp <file>...            the same for C#, via Roslyn
///
/// Output is CSV on stdout in every mode, so a measurement can be committed
/// beside the record that cites it.
module Ordo.Complexity.Program

open System.IO
open Ordo.Complexity.Measure
open Ordo.Complexity

/// C# files on disk. Reported separately from the F# table rather than merged
/// into it, because the two measures are not the same measure — see the header
/// of `Ordo.Complexity.CSharp` for the mapping and its weakest join.
let private reportCSharp (paths: string array) =
    let results =
        paths
        |> Array.filter CSharp.isCSharp
        |> Array.filter File.Exists
        |> Array.map (fun path -> CSharp.complexityOf path (File.ReadAllText path))
        |> Array.sortBy (fun result -> result.Path)

    printfn "path,cyclomatic,branches,branches_without_null_coalescing,null_coalescing,lines"

    results
    |> Array.iter (fun r ->
        printfn "%s,%d,%d,%d,%d,%d" r.Path (r.Branches + 1) r.Branches r.BranchesWithoutNullCoalescing r.NullCoalescing r.Lines)

    let branches = results |> Array.sumBy (fun r -> r.Branches)
    let strict = results |> Array.sumBy (fun r -> r.BranchesWithoutNullCoalescing)
    let coalescing = results |> Array.sumBy (fun r -> r.NullCoalescing)
    let lines = results |> Array.sumBy (fun r -> r.Lines)
    printfn "TOTAL,%d,%d,%d,%d,%d" (branches + Array.length results) branches strict coalescing lines

let private reportFiles (paths: string array) =
    let results =
        paths
        |> Array.filter isFSharp
        |> Array.filter File.Exists
        |> Array.map (fun path -> complexityOf path (File.ReadAllText path))
        |> Array.sortBy (fun result -> result.Path)

    printfn "path,cyclomatic,branches,declared_cases,control_flow_branches,lines"

    results
    |> Array.iter (fun r ->
        printfn "%s,%d,%d,%d,%d,%d" r.Path r.Cyclomatic r.Branches r.DeclaredCases r.ControlFlowBranches r.Lines)

    let branches = results |> Array.sumBy (fun r -> r.Branches)
    let declared = results |> Array.sumBy (fun r -> r.DeclaredCases)
    let control = results |> Array.sumBy (fun r -> r.ControlFlowBranches)
    let lines = results |> Array.sumBy (fun r -> r.Lines)
    printfn "TOTAL,%d,%d,%d,%d,%d" (branches + Array.length results) branches declared control lines

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
    | "--csharp" :: rest ->
        reportCSharp (Array.ofList rest)
        0
    | _ ->
        reportFiles argv
        0
