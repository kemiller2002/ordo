/// Command-line surface over `Ordo.Churn.Attribute`.
///
///   ordo-churn <repo> <baseline-rev> <label>=<rev> [<label>=<rev> ...]
///
/// Waves are given in the order the requirements arrived. Output is CSV on
/// stdout, so a measurement can be committed beside the record that cites it.
module Ordo.Churn.Program

open Ordo.Churn.Attribute

let private parseWave (argument: string) : Wave option =
    match argument.Split('=') with
    | [| label; revision |] when label <> "" && revision <> "" -> Some { Label = label; Commit = revision }
    | _ -> None

[<EntryPoint>]
let main argv =
    match Array.toList argv with
    | repo :: baseline :: rest when not (List.isEmpty rest) ->
        match rest |> List.map parseWave |> List.fold (fun acc w -> Option.map2 (fun xs x -> xs @ [ x ]) acc w) (Some []) with
        | None ->
            eprintfn "each wave must be <label>=<revision>"
            2
        | Some waves ->
            let results = churnSequence repo { Label = "baseline"; Commit = baseline } waves

            printfn "wave,files_touched,lines_added,lines_removed,rework_lines,unattributed_lines,rework_share,rework_by_origin"

            results
            |> List.iter (fun r ->
                let origins =
                    r.ReworkByOrigin
                    |> List.map (fun (label, count) -> label + ":" + string count)
                    |> String.concat " "

                printfn
                    "%s,%d,%d,%d,%d,%d,%.3f,%s"
                    r.Wave
                    r.FilesTouched
                    r.LinesAdded
                    r.LinesRemoved
                    r.ReworkLines
                    r.UnattributedLines
                    r.ReworkShare
                    origins)

            let rework = results |> List.sumBy (fun r -> r.ReworkLines)
            let added = results |> List.sumBy (fun r -> r.LinesAdded)
            let removed = results |> List.sumBy (fun r -> r.LinesRemoved)
            let waved = results |> List.filter (fun r -> r.ReworkLines > 0) |> List.length

            let unattributed = results |> List.sumBy (fun r -> r.UnattributedLines)

            printfn "TOTAL,,%d,%d,%d,%d,%.3f," added removed rework unattributed (if added + removed = 0 then 0.0 else float rework / float (added + removed))
            printfn "WAVES_CAUSING_REWORK,%d of %d" waved (List.length results)

            // A partial attribution is not a low rework figure. Say so on the
            // way out rather than letting a reader take the zeros at face value.
            if unattributed > 0 then
                printfn "UNATTRIBUTED,%d" unattributed
                eprintfn "WARNING: %d removed lines trace to commits outside the named waves." unattributed
                eprintfn "Rework is UNDER-counted. Name every commit between the baseline and the last wave."
                1
            else
                printfn "FULLY_ATTRIBUTED,yes"
                0
    | _ ->
        eprintfn "usage: ordo-churn <repo> <baseline-rev> <label>=<rev> [<label>=<rev> ...]"
        2
