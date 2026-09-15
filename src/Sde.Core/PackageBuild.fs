/// Producing the execution package from canonical repository sources.
///
/// This is a release-time operation, not a consumer-facing one, but it is
/// still lifecycle knowledge — it decides which canonical documents become
/// installed files — so it lives in F# with everything else that decides,
/// not in the npm scripts that merely invoke it.
///
/// Ported from the previous JavaScript implementation with its behaviour
/// preserved exactly: the same distribution map, the same front-matter
/// version extraction, the same `related_documents` and Markdown-link
/// rewriting, the same failure on a missing canonical source, and the same
/// resulting MANIFEST.json bytes.
module Sde.Core.PackageBuild

open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions
open Sde.Core.Json

type MapEntry =
    { Source: string option
      Destination: string
      Authored: bool }

type DistributionMap =
    { MethodVersionSource: string
      Files: MapEntry list }

let distributionMapFileName = "DISTRIBUTION-MAP.json"

let loadDistributionMap (mapPath: string) : Result<DistributionMap, string> =
    try
        match parse (FileSystem.readAllText mapPath) with
        | Error detail -> Error(sprintf "%s is not valid JSON: %s" distributionMapFileName detail)
        | Ok root ->
            match tryArray (tryField "files" root) with
            | None
            | Some [] -> Error(sprintf "%s has no files[] entries" distributionMapFileName)
            | Some entries ->
                let parsed =
                    entries
                    |> List.map (fun entry ->
                        let destination = tryString (tryField "destination" entry)
                        let source = tryString (tryField "source" entry)
                        let authored = tryBool (tryField "authored" entry) |> Option.defaultValue false

                        match destination with
                        | None -> Error(sprintf "%s entry is missing 'destination'" distributionMapFileName)
                        | Some destination when not (Paths.isSafeRelativePath destination) ->
                            Error(
                                sprintf
                                    "%s entry has an unsafe destination: %s"
                                    distributionMapFileName
                                    (Json.quote destination)
                            )
                        | Some destination ->
                            if not authored && source.IsNone then
                                Error(
                                    sprintf
                                        "%s entry for %s is missing 'source' and is not marked 'authored'"
                                        distributionMapFileName
                                        destination
                                )
                            else
                                Ok
                                    { Source = source
                                      Destination = destination
                                      Authored = authored })

                match parsed |> List.tryPick (function
                          | Error detail -> Some detail
                          | Ok _ -> None) with
                | Some detail -> Error detail
                | None ->
                    match tryString (tryField "methodVersionSource" root) with
                    | None -> Error(sprintf "%s is missing 'methodVersionSource'" distributionMapFileName)
                    | Some methodVersionSource ->
                        Ok
                            { MethodVersionSource = methodVersionSource
                              Files = parsed |> List.choose (function
                                          | Ok entry -> Some entry
                                          | Error _ -> None) }
    with ex ->
        Error(sprintf "%s could not be read: %s" distributionMapFileName ex.Message)

let private frontMatterPattern = Regex(@"^---\r?\n([\s\S]*?)\r?\n---", RegexOptions.CultureInvariant)
let private versionFieldPattern = Regex(@"^version:\s*(\S+)\s*$", RegexOptions.Multiline ||| RegexOptions.CultureInvariant)

let readFrontMatterVersion (path: string) : Result<string, string> =
    let text = FileSystem.readAllText path
    let m = frontMatterPattern.Match text

    if not m.Success then
        Error(sprintf "no front matter found in %s" path)
    else
        let versionMatch = versionFieldPattern.Match(m.Groups.[1].Value)

        if not versionMatch.Success then
            Error(sprintf "no 'version' field in front matter of %s" path)
        else
            Ok versionMatch.Groups.[1].Value

let toMajorMinor (version: string) : Result<string, string> =
    let m = Regex.Match(version, @"^(\d+)\.(\d+)\.", RegexOptions.CultureInvariant)

    if m.Success then
        Ok(sprintf "%s.%s" m.Groups.[1].Value m.Groups.[2].Value)
    else
        Error(sprintf "cannot derive major.minor from version %s" (Json.quote version))

/// Records the canonical repository's commit, marking it "-dirty" when the
/// working tree has uncommitted changes. Without the marker, a build from a
/// dirty tree would record a real, resolvable SHA whose committed content
/// does not match the packaged bytes — false provenance a controlled
/// engineering trial cannot tolerate.
let readSourceRevision (repoRoot: string) : string option =
    let run (arguments: string list) =
        try
            let startInfo = ProcessStartInfo("git")
            startInfo.ArgumentList.Add "-C"
            startInfo.ArgumentList.Add repoRoot

            for argument in arguments do
                startInfo.ArgumentList.Add argument

            startInfo.RedirectStandardOutput <- true
            startInfo.RedirectStandardError <- true
            startInfo.UseShellExecute <- false

            match Process.Start startInfo |> Option.ofObj with
            | None -> None
            | Some started ->
                use process' = started
                let output = process'.StandardOutput.ReadToEnd()
                process'.WaitForExit()

                if process'.ExitCode = 0 then Some(output.Trim()) else None
        with _ ->
            None

    match run [ "rev-parse"; "HEAD" ] with
    | None -> None
    | Some sha ->
        match run [ "status"; "--porcelain" ] with
        | None -> Some sha
        | Some status -> Some(if status.Length > 0 then sha + "-dirty" else sha)

/// Canonical documents cite `related_documents` written for a reader of the
/// full canonical repository. Copied verbatim into the execution package
/// those entries dangle: they name a path that was renamed on the way in, or
/// one deliberately excluded from distribution. Each entry is rewritten to
/// its distributed destination when the target is itself distributed, and
/// dropped otherwise, so a cross-reference inside the installed package never
/// points at a file that was never installed.
///
/// Inline prose citations such as "[EV-HN-2026-0005]" are left alone: they
/// read as evidence citations rather than navigable links, and rewriting
/// free-form prose safely is out of scope for a build step.
let rewriteRelatedDocuments (text: string) (sourceToDestination: Map<string, string>) : string =
    let m = frontMatterPattern.Match text

    if not m.Success then
        text
    else
        let original = m.Groups.[1].Value
        let lines = Regex.Split(original, @"\r?\n")
        let keyIndex = lines |> Array.tryFindIndex (fun line -> Regex.IsMatch(line, @"^related_documents:\s*$"))

        match keyIndex with
        | None -> text
        | Some keyIndex ->
            let mutable endIndex = keyIndex + 1
            let kept = ResizeArray<string>()
            let mutable scanning = true

            while scanning && endIndex < lines.Length do
                let itemMatch = Regex.Match(lines.[endIndex], @"^\s*-\s*(\S.*)$")

                if not itemMatch.Success then
                    scanning <- false
                else
                    let target = itemMatch.Groups.[1].Value.Trim()

                    match sourceToDestination.TryFind target with
                    | Some rewritten -> kept.Add("  - " + rewritten)
                    | None -> ()

                    endIndex <- endIndex + 1

            let replacement =
                if kept.Count > 0 then
                    Array.append [| "related_documents:" |] (kept.ToArray())
                else
                    [| "related_documents: []" |]

            let newFrontMatter =
                Array.concat [ lines.[.. keyIndex - 1]; replacement; lines.[endIndex ..] ]
                |> String.concat "\n"

            let blockStart = m.Index + m.Value.IndexOf(original, StringComparison.Ordinal)
            let blockEnd = blockStart + original.Length
            text.Substring(0, blockStart) + newFrontMatter + text.Substring blockEnd

/// Rewrites Markdown links whose canonical target is itself distributed, so
/// that a renamed directory (doctrine/ becomes architecture/) does not leave
/// a broken link. Links to excluded material are left as citations to the
/// canonical repository; this does not pretend those files were installed.
let rewriteDistributedMarkdownLinks
    (text: string)
    (sourcePath: string)
    (destinationPath: string)
    (sourceToDestination: Map<string, string>)
    : string =
    let posixNormalize (path: string) =
        let segments = path.Split '/'

        let stack =
            segments
            |> Array.fold
                (fun (stack: string list) segment ->
                    match segment with
                    | "." -> stack
                    | ".." ->
                        match stack with
                        | _ :: rest -> rest
                        | [] -> []
                    | "" -> stack
                    | other -> other :: stack)
                []

        stack |> List.rev |> String.concat "/"

    let posixDirname (path: string) =
        let index = path.LastIndexOf '/'
        if index < 0 then "" else path.Substring(0, index)

    let posixRelative (fromDir: string) (target: string) =
        let fromSegments =
            if fromDir = "" then [||] else fromDir.Split '/'

        let targetSegments = target.Split '/'

        let common =
            Seq.zip fromSegments targetSegments
            |> Seq.takeWhile (fun (a, b) -> a = b)
            |> Seq.length

        let ups = Array.create (fromSegments.Length - common) ".."
        let downs = targetSegments.[common..]
        let joined = Array.append ups downs |> String.concat "/"
        if joined = "" then Paths.fileName target else joined

    Regex.Replace(
        text,
        @"\]\(([^)\s]+)\)",
        fun (m: Match) ->
            let rawTarget = m.Groups.[1].Value

            if rawTarget.StartsWith "#" || Regex.IsMatch(rawTarget, @"^[A-Za-z][A-Za-z0-9+.-]*:") then
                m.Value
            else
                let hashIndex = rawTarget.IndexOf '#'
                let targetPath = if hashIndex < 0 then rawTarget else rawTarget.Substring(0, hashIndex)
                let fragment = if hashIndex < 0 then "" else rawTarget.Substring hashIndex

                let canonicalTarget =
                    let dir = posixDirname sourcePath
                    posixNormalize (if dir = "" then targetPath else dir + "/" + targetPath)

                match sourceToDestination.TryFind canonicalTarget with
                | None -> m.Value
                | Some distributedTarget ->
                    sprintf "](%s%s)" (posixRelative (posixDirname destinationPath) distributedTarget) fragment
    )

type BuildInputs =
    { RepositoryRoot: string
      DistributionDirectory: string
      OutputDirectory: string
      PackageName: string
      PackageVersion: string }

type BuildResult =
    { OutputDirectory: string
      Manifest: Manifest.Manifest }

/// Builds the execution package. Fails before writing anything visible if a
/// canonical source named by the map is missing: silently skipping one would
/// ship an incomplete methodology with no indication that it was incomplete.
let build (inputs: BuildInputs) : Result<BuildResult, string> =
    let mapPath = Path.Combine(inputs.DistributionDirectory, distributionMapFileName)

    match loadDistributionMap mapPath with
    | Error detail -> Error detail
    | Ok map ->

    let missingSources =
        map.Files
        |> List.filter (fun entry -> not entry.Authored)
        |> List.choose (fun entry ->
            entry.Source
            |> Option.bind (fun source ->
                let absolute = Path.Combine(inputs.RepositoryRoot, Paths.toNativePath source)
                if FileSystem.fileExists absolute then None else Some source))

    if not missingSources.IsEmpty then
        Error(
            "cannot build SDE execution package: missing canonical source file(s):\n"
            + (missingSources |> List.map (sprintf "  - %s") |> String.concat "\n")
        )
    else

    match readFrontMatterVersion (Path.Combine(inputs.RepositoryRoot, Paths.toNativePath map.MethodVersionSource)) with
    | Error detail -> Error detail
    | Ok rawMethodVersion ->

    match toMajorMinor rawMethodVersion with
    | Error detail -> Error detail
    | Ok methodVersion ->

    try
        FileSystem.removeTree inputs.OutputDirectory
        Directory.CreateDirectory inputs.OutputDirectory |> ignore

        let sourceToDestination =
            map.Files
            |> List.filter (fun entry -> not entry.Authored)
            |> List.choose (fun entry -> entry.Source |> Option.map (fun source -> source, entry.Destination))
            |> Map.ofList

        for entry in map.Files do
            let destinationPath = Paths.safeJoinOrFail inputs.OutputDirectory entry.Destination

            if entry.Authored then
                let authoredPath =
                    Path.Combine(inputs.DistributionDirectory, "authored", Paths.fileName entry.Destination)

                FileSystem.copyFileInto authoredPath destinationPath
            else
                let source = entry.Source |> Option.defaultValue ""
                FileSystem.copyFileInto (Path.Combine(inputs.RepositoryRoot, Paths.toNativePath source)) destinationPath

                if destinationPath.EndsWith ".md" then
                    let original = FileSystem.readAllText destinationPath

                    let rewritten =
                        original
                        |> fun text -> rewriteRelatedDocuments text sourceToDestination
                        |> fun text -> rewriteDistributedMarkdownLinks text source entry.Destination sourceToDestination

                    if rewritten <> original then
                        File.WriteAllText(destinationPath, rewritten, Text.UTF8Encoding false)

        FileSystem.copyFileInto
            (Path.Combine(inputs.DistributionDirectory, "authored", "README.template.md"))
            (Path.Combine(inputs.OutputDirectory, "README.md"))

        FileSystem.writeFileInto
            (Path.Combine(inputs.OutputDirectory, Ownership.versionFileName))
            (inputs.PackageVersion + "\n")

        let sourceRevision = readSourceRevision inputs.RepositoryRoot

        match
            Manifest.build inputs.PackageName inputs.PackageVersion (Some methodVersion) sourceRevision inputs.OutputDirectory
        with
        | Error detail -> Error detail
        | Ok manifest ->
            FileSystem.writeFileInto
                (Path.Combine(inputs.OutputDirectory, Ownership.manifestName))
                (Manifest.serialize manifest)

            Ok
                { OutputDirectory = inputs.OutputDirectory
                  Manifest = manifest }
    with ex ->
        Error ex.Message
