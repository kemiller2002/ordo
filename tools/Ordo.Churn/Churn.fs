/// Rework attribution: how much of a requirement's work lands on code that an
/// EARLIER requirement wrote.
///
/// This is the measure EX-SDE-2026-0003 exists to take, and the one the
/// complexity tool cannot take at all — complexity reports the state of a tree,
/// not the churn that produced it.
///
/// THE DEFINITION, stated so it can be argued with:
///
///   For a commit C with parent P, every line C removes or replaces sat in P.
///   `git blame` on P says which commit first wrote that line. If that commit is
///   an earlier wave, or the baseline, the line is REWORK — work being redone
///   because a later requirement arrived. Lines C adds that replace nothing are
///   NEW WORK.
///
/// So rework is attributed to the requirement that ORIGINALLY wrote the line,
/// not to the one that changed it. "F2 reworked C3" means F2 changed lines C3
/// wrote. That is the direction the effort log's hand-kept table used, and the
/// direction that answers "what did this new requirement cost the work already
/// done".
///
/// WHAT THIS DOES NOT CAPTURE. A line left alone is not necessarily untouched
/// work: a requirement can invalidate earlier code semantically while editing
/// none of it, and an edit can be cosmetic. Line churn is a proxy for rework,
/// not rework itself. It is used because it is mechanical and auditable, which
/// the existing hand-classified figures are not.
module Ordo.Churn.Attribute

open System
open System.Diagnostics
open System.Collections.Generic

/// One requirement in the sequence: a label and the commit that implemented it.
type Wave = { Label: string; Commit: string }

type WaveChurn =
    { Wave: string
      FilesTouched: int
      LinesAdded: int
      LinesRemoved: int
      /// Removed lines that an earlier wave or the baseline first wrote.
      ReworkLines: int
      /// Removed lines whose originating commit is not the baseline and not any
      /// named wave.
      ///
      /// This must be ZERO for a measurement to be trustworthy. It is non-zero
      /// when the wave list does not cover the history — an unlabelled commit
      /// between waves, a merge, a rebase — and in that case rework is
      /// UNDER-counted, because unattributable lines are silently not rework.
      /// Found by running the tool on real history, where it reported 0% rework
      /// for waves that plainly removed lines.
      UnattributedLines: int
      /// Rework split by the wave whose lines were changed, highest first.
      ReworkByOrigin: (string * int) list }

    /// Whether every removed line was traced to a known wave. A run whose
    /// manifest records `false` here has not been measured, whatever its other
    /// numbers say.
    member this.FullyAttributed = this.UnattributedLines = 0

    /// Rework as a share of everything this wave touched. Reported rather than
    /// the raw count because waves differ in size.
    member this.ReworkShare =
        let touched = this.LinesAdded + this.LinesRemoved
        if touched = 0 then 0.0 else float this.ReworkLines / float touched

let private run (repo: string) (arguments: string list) : string =
    let info = ProcessStartInfo("git", RedirectStandardOutput = true, RedirectStandardError = true)
    info.ArgumentList.Add "-C"
    info.ArgumentList.Add repo
    arguments |> List.iter info.ArgumentList.Add

    // A git that will not start is not an empty diff. Failing loudly is the
    // point: a silent zero looks exactly like a real measurement of no rework.
    match Process.Start info with
    | null -> failwithf "could not start git %s" (String.Join(" ", arguments))
    | started ->
        use proc = started
        let output = proc.StandardOutput.ReadToEnd()
        proc.StandardError.ReadToEnd() |> ignore
        proc.WaitForExit()
        output

/// Resolves a revision to a full SHA so blame output can be matched against it.
let resolve (repo: string) (revision: string) : string =
    (run repo [ "rev-parse"; revision ]).Trim()

/// Whether `candidate` is the baseline or anything the baseline was built on.
///
/// `git blame` names the commit that LAST wrote a line, and a baseline tree's
/// lines were written across the whole history behind it, not by the single
/// commit the experiment starts from. Without this, every line of the starting
/// tree is unattributable and all rework against the baseline vanishes — which
/// is what the first version did. Found before any run, by testing the tool
/// against real history rather than fixtures.
let private isBaselineAncestor (repo: string) (baseline: string) (candidate: string) : bool =
    let info = ProcessStartInfo("git", RedirectStandardOutput = true, RedirectStandardError = true)
    info.ArgumentList.Add "-C"
    info.ArgumentList.Add repo
    [ "merge-base"; "--is-ancestor"; candidate; baseline ] |> List.iter info.ArgumentList.Add

    match Process.Start info with
    | null -> failwith "could not start git merge-base"
    | started ->
        use proc = started
        proc.StandardOutput.ReadToEnd() |> ignore
        proc.StandardError.ReadToEnd() |> ignore
        proc.WaitForExit()
        proc.ExitCode = 0

let private changedFiles (repo: string) (fromRev: string) (toRev: string) : string list =
    run repo [ "diff"; "--name-only"; fromRev; toRev ]
    |> fun text -> text.Split('\n')
    |> Array.toList
    |> List.map (fun line -> line.Trim())
    |> List.filter (fun line -> line <> "")

/// Line number -> the commit that first wrote it, for one file at one revision.
let private blame (repo: string) (revision: string) (path: string) : Dictionary<int, string> =
    let map = Dictionary<int, string>()

    // Porcelain emits "<sha> <orig-line> <final-line> [count]" as the header of
    // each block, which is all that is needed here.
    let output = run repo [ "blame"; "--porcelain"; revision; "--"; path ]

    for line in output.Split('\n') do
        let parts = line.Split(' ')

        if parts.Length >= 3 && parts.[0].Length = 40 then
            match Int32.TryParse parts.[2] with
            | true, finalLine -> map.[finalLine] <- parts.[0]
            | _ -> ()

    map

/// The line numbers a diff removes from the PRE-image, per file.
let private removedLines (repo: string) (fromRev: string) (toRev: string) (path: string) : int list =
    let output = run repo [ "diff"; "--unified=0"; fromRev; toRev; "--"; path ]

    // Hunk headers read @@ -start,count +start,count @@. The minus side names
    // the pre-image lines this hunk removes; count is omitted when it is 1, and
    // is 0 for a pure insertion, which contributes nothing.
    output.Split('\n')
    |> Array.toList
    |> List.filter (fun line -> line.StartsWith "@@")
    |> List.collect (fun line ->
        let fields = line.Split(' ')

        if fields.Length < 2 || not (fields.[1].StartsWith "-") then
            []
        else
            let spec = fields.[1].TrimStart '-'
            let bits = spec.Split(',')

            match Int32.TryParse bits.[0] with
            | false, _ -> []
            | true, start ->
                let count =
                    if bits.Length < 2 then 1
                    else
                        match Int32.TryParse bits.[1] with
                        | true, n -> n
                        | _ -> 1

                [ for offset in 0 .. count - 1 -> start + offset ])

let private countAddedRemoved (repo: string) (fromRev: string) (toRev: string) : int * int =
    run repo [ "diff"; "--numstat"; fromRev; toRev ]
    |> fun text -> text.Split('\n')
    |> Array.toList
    |> List.fold
        (fun (added, removed) line ->
            let fields = line.Split('\t')

            if fields.Length < 2 then
                added, removed
            else
                // A binary file shows "-" for both counts; it contributes nothing.
                match Int32.TryParse fields.[0], Int32.TryParse fields.[1] with
                | (true, a), (true, r) -> added + a, removed + r
                | _ -> added, removed)
        (0, 0)

/// Churn for one wave against the revision immediately before it.
let churnOf (repo: string) (baseline: Wave) (earlier: Wave list) (wave: Wave) : WaveChurn =
    let previous =
        match List.rev earlier with
        | latest :: _ -> latest.Commit
        | [] -> baseline.Commit

    let origin =
        (baseline :: earlier)
        |> List.map (fun w -> resolve repo w.Commit, w.Label)
        |> dict

    let baselineSha = resolve repo baseline.Commit
    let ancestryCache = Dictionary<string, bool>()

    // One merge-base call per distinct commit, not per line.
    let ancestry (sha: string) =
        match ancestryCache.TryGetValue sha with
        | true, known -> known
        | _ ->
            let known = isBaselineAncestor repo baselineSha sha
            ancestryCache.[sha] <- known
            known

    let files = changedFiles repo previous wave.Commit
    let added, removed = countAddedRemoved repo previous wave.Commit

    // Every removed line is classified, including the ones that cannot be traced
    // to a named wave. Dropping those silently is what made the first version
    // report no rework for a wave that removed seventeen lines.
    let classified =
        files
        |> List.collect (fun path ->
            let lines = removedLines repo previous wave.Commit path

            if List.isEmpty lines then
                []
            else
                let authors = blame repo previous path

                lines
                |> List.map (fun line ->
                    match authors.TryGetValue line with
                    | true, sha ->
                        match origin.TryGetValue sha with
                        | true, label -> Some label
                        // Not a named wave. Anything the baseline was built on
                        // is baseline work; anything else is genuinely unknown.
                        | _ when ancestry sha -> Some baseline.Label
                        | _ -> None
                    | _ -> None))

    let attributed = classified |> List.choose id

    let byOrigin =
        attributed
        |> List.countBy id
        |> List.sortByDescending snd

    { Wave = wave.Label
      FilesTouched = List.length files
      LinesAdded = added
      LinesRemoved = removed
      ReworkLines = List.length attributed
      UnattributedLines = classified |> List.filter Option.isNone |> List.length
      ReworkByOrigin = byOrigin }

/// Every wave in order, each measured against the one before it.
let churnSequence (repo: string) (baseline: Wave) (waves: Wave list) : WaveChurn list =
    waves
    |> List.mapi (fun index wave ->
        let earlier = waves |> List.take index
        churnOf repo baseline earlier wave)
