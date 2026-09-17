/// Tests for rework attribution, against real git repositories rather than a
/// stub, because the part worth testing is the interaction with blame and diff.
///
/// Every expected value is hand-countable from the file contents written in the
/// test. This measure is the one EX-SDE-2026-0003 turns on, and the existing
/// hand-classified rework figures it replaces are unauditable — so the
/// replacement is pinned line by line.
module Ordo.Churn.Tests.ChurnTests

open System
open System.Diagnostics
open System.IO
open Xunit
open Ordo.Churn.Attribute

let private git (directory: string) (arguments: string list) =
    let info = ProcessStartInfo("git", RedirectStandardOutput = true, RedirectStandardError = true)
    info.WorkingDirectory <- directory
    arguments |> List.iter info.ArgumentList.Add

    match Process.Start info with
    | null -> failwith "could not start git"
    | started ->
        use proc = started
        proc.StandardOutput.ReadToEnd() |> ignore
        proc.StandardError.ReadToEnd() |> ignore
        proc.WaitForExit()

let private commit (directory: string) (message: string) =
    git directory [ "add"; "-A" ]
    git directory [ "commit"; "-q"; "-m"; message ]
    // The tag gives each wave a stable name to pass to the tool.
    git directory [ "tag"; message ]

let private repository (build: string -> unit) =
    let directory = Path.Combine(Path.GetTempPath(), "ordo-churn-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory directory |> ignore

    try
        git directory [ "init"; "-q"; "-b"; "main" ]
        git directory [ "config"; "user.email"; "test@example.com" ]
        git directory [ "config"; "user.name"; "test" ]
        build directory
    finally
        try
            Directory.Delete(directory, true)
        with _ ->
            ()

let private write (directory: string) (name: string) (lines: string list) =
    File.WriteAllText(Path.Combine(directory, name), String.Join("\n", lines) + "\n")

/// A wave that only adds files does no rework, however much it writes.
[<Fact>]
let ``a purely additive wave reports no rework`` () =
    repository (fun dir ->
        write dir "a.txt" [ "one"; "two" ]
        commit dir "baseline"
        write dir "b.txt" [ "three"; "four"; "five" ]
        commit dir "F1"

        let result =
            churnOf dir { Label = "baseline"; Commit = "baseline" } [] { Label = "F1"; Commit = "F1" }

        Assert.Equal(3, result.LinesAdded)
        Assert.Equal(0, result.LinesRemoved)
        Assert.Equal(0, result.ReworkLines)
        Assert.Empty(result.ReworkByOrigin))

/// The core case: a wave that edits lines the baseline wrote is reworking the
/// baseline, and the rework is attributed to the baseline rather than to itself.
[<Fact>]
let ``editing baseline lines is rework attributed to the baseline`` () =
    repository (fun dir ->
        write dir "a.txt" [ "one"; "two"; "three" ]
        commit dir "baseline"
        write dir "a.txt" [ "ONE"; "two"; "THREE" ]
        commit dir "F1"

        let result =
            churnOf dir { Label = "baseline"; Commit = "baseline" } [] { Label = "F1"; Commit = "F1" }

        Assert.Equal(2, result.ReworkLines)
        Assert.Equal<(string * int) list>([ "baseline", 2 ], result.ReworkByOrigin))

/// Rework is attributed to whichever wave ORIGINALLY wrote the line, which is
/// the direction that answers "what did this new requirement cost work already
/// done". Here F2 changes one baseline line and one F1 line, and the split is
/// reported rather than a single total.
[<Fact>]
let ``rework is split by the wave whose lines were changed`` () =
    repository (fun dir ->
        write dir "a.txt" [ "base-1"; "base-2"; "base-3" ]
        commit dir "baseline"

        write dir "a.txt" [ "base-1"; "base-2"; "base-3"; "f1-1"; "f1-2" ]
        commit dir "F1"

        // Change one baseline line and one F1 line; leave the rest alone.
        write dir "a.txt" [ "base-1"; "CHANGED-2"; "base-3"; "f1-1"; "CHANGED-f1-2" ]
        commit dir "F2"

        let result =
            churnOf
                dir
                { Label = "baseline"; Commit = "baseline" }
                [ { Label = "F1"; Commit = "F1" } ]
                { Label = "F2"; Commit = "F2" }

        Assert.Equal(2, result.ReworkLines)
        Assert.Equal<(string * int) list>([ "F1", 1; "baseline", 1 ], List.sortBy fst result.ReworkByOrigin))

/// A deletion is rework too: removing what an earlier requirement wrote is the
/// clearest case of a later requirement invalidating earlier work.
[<Fact>]
let ``deleting earlier lines counts as rework`` () =
    repository (fun dir ->
        write dir "a.txt" [ "keep"; "drop-1"; "drop-2"; "keep-2" ]
        commit dir "baseline"
        write dir "a.txt" [ "keep"; "keep-2" ]
        commit dir "F1"

        let result =
            churnOf dir { Label = "baseline"; Commit = "baseline" } [] { Label = "F1"; Commit = "F1" }

        Assert.Equal(2, result.LinesRemoved)
        Assert.Equal(2, result.ReworkLines))

/// The whole sequence, measured wave by wave, which is how a run is reported.
[<Fact>]
let ``a sequence reports each wave against the one before it`` () =
    repository (fun dir ->
        write dir "a.txt" [ "base-1"; "base-2" ]
        commit dir "baseline"

        write dir "a.txt" [ "base-1"; "base-2"; "f1" ]
        commit dir "F1"

        write dir "a.txt" [ "base-1"; "CHANGED"; "f1" ]
        commit dir "F2"

        let results =
            churnSequence
                dir
                { Label = "baseline"; Commit = "baseline" }
                [ { Label = "F1"; Commit = "F1" }; { Label = "F2"; Commit = "F2" } ]

        Assert.Equal(2, List.length results)

        let f1 = results |> List.find (fun r -> r.Wave = "F1")
        let f2 = results |> List.find (fun r -> r.Wave = "F2")

        Assert.Equal(0, f1.ReworkLines)
        Assert.Equal(1, f2.ReworkLines)
        Assert.Equal<(string * int) list>([ "baseline", 1 ], f2.ReworkByOrigin))

/// The share is the reported figure because waves differ in size; a raw count
/// favours whichever wave happened to be bigger.
[<Fact>]
let ``rework share is measured against everything the wave touched`` () =
    repository (fun dir ->
        write dir "a.txt" [ "base-1"; "base-2" ]
        commit dir "baseline"
        // One baseline line replaced (1 removed, 1 added) plus two new lines.
        write dir "a.txt" [ "base-1"; "CHANGED"; "new-1"; "new-2" ]
        commit dir "F1"

        let result =
            churnOf dir { Label = "baseline"; Commit = "baseline" } [] { Label = "F1"; Commit = "F1" }

        Assert.Equal(1, result.ReworkLines)
        Assert.Equal(3, result.LinesAdded)
        Assert.Equal(1, result.LinesRemoved)
        // 1 rework line out of 4 lines touched.
        Assert.Equal(0.25, result.ReworkShare, 3))

/// A baseline tree's lines were written across the whole history behind it, not
/// by the one commit an experiment starts from. Blame names those historical
/// commits, and the first version treated every one of them as unattributable —
/// which would have made ALL rework against the baseline vanish in the real
/// experiment. Anything the baseline was built on is baseline work.
[<Fact>]
let ``lines predating the baseline commit count as baseline work`` () =
    repository (fun dir ->
        // Written by an earlier commit, then carried into the baseline untouched.
        write dir "a.txt" [ "ancient-1"; "ancient-2" ]
        commit dir "history"

        write dir "b.txt" [ "baseline-only" ]
        commit dir "baseline"

        // The wave edits a line written before the baseline commit existed.
        write dir "a.txt" [ "ancient-1"; "CHANGED" ]
        commit dir "F1"

        let result =
            churnOf dir { Label = "baseline"; Commit = "baseline" } [] { Label = "F1"; Commit = "F1" }

        Assert.Equal(1, result.ReworkLines)
        Assert.Equal(0, result.UnattributedLines)
        Assert.Equal<(string * int) list>([ "baseline", 1 ], result.ReworkByOrigin))

/// Found by running the tool on real repository history, where it reported 0%
/// rework for a wave that plainly removed seventeen lines and dropped them
/// silently — exactly the false "no rework here" this tool exists to avoid.
/// A commit that is neither a named wave nor behind the baseline is genuinely
/// unknown, and says so rather than reading as zero.
[<Fact>]
let ``lines from unnamed commits after the baseline are reported, not dropped`` () =
    repository (fun dir ->
        write dir "a.txt" [ "base-1"; "base-2"; "base-3" ]
        commit dir "baseline"

        // An unlabelled commit AFTER the baseline — a hotfix, a stray edit.
        write dir "a.txt" [ "base-1"; "interloper"; "base-3" ]
        commit dir "unnamed"

        write dir "a.txt" [ "base-1"; "interloper"; "base-3"; "f1" ]
        commit dir "F1"

        // F2 edits the line the unnamed commit wrote.
        write dir "a.txt" [ "base-1"; "CHANGED"; "base-3"; "f1" ]
        commit dir "F2"

        let result =
            churnOf
                dir
                { Label = "baseline"; Commit = "baseline" }
                [ { Label = "F1"; Commit = "F1" } ]
                { Label = "F2"; Commit = "F2" }

        Assert.Equal(1, result.LinesRemoved)
        Assert.Equal(0, result.ReworkLines)
        Assert.Equal(1, result.UnattributedLines)
        Assert.False(result.FullyAttributed, "a partial attribution must not claim to be complete"))

/// The complement: when every commit is a named wave, attribution is complete
/// and the run is measurable. This is the state a real run must reach.
[<Fact>]
let ``a fully named sequence reports complete attribution`` () =
    repository (fun dir ->
        write dir "a.txt" [ "base-1"; "base-2" ]
        commit dir "baseline"
        write dir "a.txt" [ "base-1"; "CHANGED" ]
        commit dir "F1"

        let result =
            churnOf dir { Label = "baseline"; Commit = "baseline" } [] { Label = "F1"; Commit = "F1" }

        Assert.Equal(1, result.ReworkLines)
        Assert.Equal(0, result.UnattributedLines)
        Assert.True(result.FullyAttributed))
