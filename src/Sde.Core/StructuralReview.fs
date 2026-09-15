/// The portable SDE-STRUCT-001 source-size review run by `verify`.
///
/// COMPATIBILITY: `sde.config.json`'s `structuralReview` block, its default
/// extension list, its default excluded directory names, its default
/// thresholds, the band names and the finding code are all public and are
/// reproduced here exactly as releases 1.0.0 through 1.1.1 defined them. A
/// repository whose configuration validated before must validate now, and
/// must produce the same findings.
///
/// `sde.config.json` is Shared: the tool defines the schema, the repository
/// owns the values. Invalid configuration therefore fails rather than being
/// rewritten or silently replaced with defaults — the requested check cannot
/// be trusted, and guessing at intent would be worse than refusing.
module Sde.Core.StructuralReview

open System
open System.IO
open System.Text.RegularExpressions
open Sde.Core.Json

type Thresholds =
    { ReviewAt: int
      StrongReviewAt: int
      JustificationAbove: int
      ConformanceConcernAbove: int }

type Config =
    { Enabled: bool
      Extensions: string list
      ExcludedDirectoryNames: string list
      ExcludePaths: string list
      Thresholds: Thresholds
      /// Where the effective configuration came from, reported verbatim by
      /// `verify` so a surprising result is traceable to a file or to the
      /// built-in defaults.
      Source: string }

type Band =
    | Review
    | StrongReview
    | JustificationRequired
    | ConformanceConcern

let describeBand =
    function
    | Review -> "review"
    | StrongReview -> "strong-review"
    | JustificationRequired -> "justification-required"
    | ConformanceConcern -> "conformance-concern"

type Finding =
    { Code: string
      Band: Band
      Path: string
      LineCount: int }

type Report =
    { Config: Config
      Findings: Finding list
      InspectedFiles: int }

let findingCode = "SDE-STRUCT-001"

let defaultThresholds =
    { ReviewAt = 500
      StrongReviewAt = 1000
      JustificationAbove = 2000
      ConformanceConcernAbove = 4000 }

let defaultExtensions =
    [ ".c"; ".cc"; ".clj"; ".cljs"; ".cpp"; ".cs"; ".cxx"; ".dart"
      ".erl"; ".cjs"; ".cts"; ".ex"; ".exs"; ".fs"; ".fsx"; ".go"
      ".groovy"; ".hrl"; ".hs"; ".java"; ".js"; ".jsx"; ".kt"; ".kts"
      ".lhs"; ".lua"; ".m"; ".mjs"; ".ml"; ".mli"; ".mm"; ".mts"
      ".php"; ".pl"; ".py"; ".r"; ".rb"; ".rs"; ".scala"; ".sh"
      ".sql"; ".svelte"; ".swift"; ".ts"; ".tsx"; ".vue" ]

let defaultExcludedDirectoryNames =
    [ ".git"; ".hg"; ".next"; ".ros"; ".sde"; ".svn"; "build"
      "coverage"; "dist"; "node_modules"; "obj"; "target"; "vendor" ]

let defaultConfig =
    { Enabled = true
      Extensions = defaultExtensions
      ExcludedDirectoryNames = defaultExcludedDirectoryNames
      ExcludePaths = []
      Thresholds = defaultThresholds
      Source = "defaults" }

let private extensionPattern = Regex(@"^\.[A-Za-z0-9]+$", RegexOptions.CultureInvariant)

let private ordinalSortDistinct (items: string list) =
    items
    |> List.distinct
    |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

/// Loads the effective configuration, returning Error with the exact message
/// a user needs to fix their file. Never falls back to defaults on a parse or
/// validation failure.
let loadConfig (projectRoot: string) : Result<Config, string> =
    let configPath = Path.Combine(projectRoot, Ownership.structuralConfigName)

    if not (FileSystem.fileExists configPath) then
        Ok defaultConfig
    else

    let parsed =
        try
            match parse (FileSystem.readAllText configPath) with
            | Ok value -> Ok value
            | Error message -> Error(sprintf "%s is not valid JSON: %s" Ownership.structuralConfigName message)
        with ex ->
            Error(sprintf "%s could not be read: %s" Ownership.structuralConfigName ex.Message)

    match parsed with
    | Error message -> Error message
    | Ok(JObject _ as root) ->
        let review =
            match tryField "structuralReview" root with
            | None -> Ok(JObject [])
            | Some(JObject _ as value) -> Ok value
            | Some _ -> Error "structuralReview must be an object"

        match review with
        | Error message -> Error message
        | Ok review ->

        let reviewFields =
            match review with
            | JObject fields -> fields |> List.map fst
            | _ -> []

        let allowedReviewFields = set [ "enabled"; "extensions"; "excludePaths"; "thresholds" ]

        let unknownReviewFields =
            reviewFields
            |> List.filter (fun name -> not (allowedReviewFields.Contains name))
            |> ordinalSortDistinct

        if not unknownReviewFields.IsEmpty then
            Error(sprintf "structuralReview has unknown field(s): %s" (String.concat ", " unknownReviewFields))
        else

        let enabled =
            match tryField "enabled" review with
            | None -> Ok true
            | Some(JBool b) -> Ok b
            | Some _ -> Error "structuralReview.enabled must be boolean"

        let extensions =
            match tryField "extensions" review with
            | None -> Ok defaultExtensions
            | Some(JArray items) ->
                let values =
                    items
                    |> List.map (fun item ->
                        match item with
                        | JString s when extensionPattern.IsMatch s -> Some s
                        | _ -> None)

                if values |> List.exists Option.isNone then
                    Error "structuralReview.extensions must be an array of valid strings"
                else
                    values
                    |> List.choose id
                    |> ordinalSortDistinct
                    |> List.map (fun s -> s.ToLowerInvariant())
                    |> Ok
            | Some _ -> Error "structuralReview.extensions must be an array of valid strings"

        let excludePaths =
            match tryField "excludePaths" review with
            | None -> Ok []
            | Some(JArray items) ->
                let values =
                    items
                    |> List.map (fun item ->
                        match item with
                        | JString s when Paths.isSafeRelativePath s && s <> "." -> Some s
                        | _ -> None)

                if values |> List.exists Option.isNone then
                    Error "structuralReview.excludePaths must be an array of valid strings"
                else
                    values
                    |> List.choose id
                    |> ordinalSortDistinct
                    |> List.map (fun s -> (Paths.toManifestPath s).TrimEnd '/')
                    |> Ok
            | Some _ -> Error "structuralReview.excludePaths must be an array of valid strings"

        let thresholds =
            match tryField "thresholds" review with
            | None -> Ok defaultThresholds
            | Some(JObject fields) ->
                let allowed = set [ "reviewAt"; "strongReviewAt"; "justificationAbove"; "conformanceConcernAbove" ]

                let unknown =
                    fields |> List.map fst |> List.filter (fun name -> not (allowed.Contains name)) |> ordinalSortDistinct

                if not unknown.IsEmpty then
                    Error(sprintf "structuralReview.thresholds has unknown field(s): %s" (String.concat ", " unknown))
                else
                    let pick name fallback =
                        match tryField name (JObject fields) with
                        | None -> Ok fallback
                        | Some(JInt n) when n >= 1 -> Ok n
                        | Some _ -> Error(sprintf "structuralReview.thresholds.%s must be a positive integer" name)

                    match
                        pick "reviewAt" defaultThresholds.ReviewAt,
                        pick "strongReviewAt" defaultThresholds.StrongReviewAt,
                        pick "justificationAbove" defaultThresholds.JustificationAbove,
                        pick "conformanceConcernAbove" defaultThresholds.ConformanceConcernAbove
                    with
                    | Error m, _, _, _
                    | _, Error m, _, _
                    | _, _, Error m, _
                    | _, _, _, Error m -> Error m
                    | Ok reviewAt, Ok strongReviewAt, Ok justificationAbove, Ok conformanceConcernAbove ->
                        if
                            reviewAt < strongReviewAt
                            && strongReviewAt <= justificationAbove
                            && justificationAbove < conformanceConcernAbove
                        then
                            Ok
                                { ReviewAt = reviewAt
                                  StrongReviewAt = strongReviewAt
                                  JustificationAbove = justificationAbove
                                  ConformanceConcernAbove = conformanceConcernAbove }
                        else
                            Error
                                "structuralReview thresholds must satisfy reviewAt < strongReviewAt <= justificationAbove < conformanceConcernAbove"
            | Some _ -> Error "structuralReview.thresholds must be an object"

        match enabled, extensions, excludePaths, thresholds with
        | Error m, _, _, _
        | _, Error m, _, _
        | _, _, Error m, _
        | _, _, _, Error m -> Error m
        | Ok enabled, Ok extensions, Ok excludePaths, Ok thresholds ->
            Ok
                { Enabled = enabled
                  Extensions = extensions
                  ExcludedDirectoryNames = defaultExcludedDirectoryNames
                  ExcludePaths = excludePaths
                  Thresholds = thresholds
                  Source = Ownership.structuralConfigName }
    | Ok _ -> Error(sprintf "%s must be an object" Ownership.structuralConfigName)

/// Counts physical lines the same way the previous implementation did: the
/// number of line terminators, plus one when the file does not end with one.
/// An empty file is zero lines.
let countPhysicalLines (text: string) : int =
    if text.Length = 0 then
        0
    else
        let breaks = Regex.Matches(text, "\r\n|\r|\n").Count
        let endsWithBreak = text.EndsWith "\n" || text.EndsWith "\r"
        breaks + (if endsWithBreak then 0 else 1)

let private isExcludedPath (relPath: string) (excludePaths: string list) =
    let manifestPath = Paths.toManifestPath relPath

    excludePaths
    |> List.exists (fun excluded -> manifestPath = excluded || manifestPath.StartsWith(excluded + "/", StringComparison.Ordinal))

let private listSourceFiles (projectRoot: string) (config: Config) =
    let results = ResizeArray<string>()
    let extensions = config.Extensions |> List.map (fun e -> e.ToLowerInvariant()) |> Set.ofList
    let excludedNames = Set.ofList config.ExcludedDirectoryNames

    let rec walk (absoluteDir: string) (relativeDir: string) =
        let entries =
            DirectoryInfo(absoluteDir).GetFileSystemInfos()
            |> Array.sortWith (fun a b -> String.CompareOrdinal(a.Name, b.Name))

        for entry in entries do
            let relativePath = if relativeDir = "" then entry.Name else relativeDir + "/" + entry.Name

            if isExcludedPath relativePath config.ExcludePaths then
                ()
            // Symlinks are skipped rather than refused here: unlike the
            // managed installation, an arbitrary repository may legitimately
            // contain them, and following one could walk in a cycle or out
            // of the repository entirely.
            elif entry.Attributes.HasFlag FileAttributes.ReparsePoint then
                ()
            elif entry :? DirectoryInfo then
                if not (excludedNames.Contains entry.Name) then
                    walk entry.FullName relativePath
            elif extensions.Contains((Paths.extension entry.Name).ToLowerInvariant()) then
                results.Add relativePath

    walk projectRoot ""
    results |> List.ofSeq |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

let private bandFor lineCount (thresholds: Thresholds) =
    if lineCount > thresholds.ConformanceConcernAbove then Some ConformanceConcern
    elif lineCount > thresholds.JustificationAbove then Some JustificationRequired
    elif lineCount >= thresholds.StrongReviewAt then Some StrongReview
    elif lineCount >= thresholds.ReviewAt then Some Review
    else None

/// Inspects the repository. Read-only: opens files, never writes.
let inspect (projectRoot: string) (config: Config) : Result<Report, string> =
    if not config.Enabled then
        Ok
            { Config = config
              Findings = []
              InspectedFiles = 0 }
    else
        try
            let files = listSourceFiles projectRoot config

            let findings =
                files
                |> List.choose (fun relativePath ->
                    let text = FileSystem.readAllText (Path.Combine(projectRoot, Paths.toNativePath relativePath))
                    let lineCount = countPhysicalLines text

                    bandFor lineCount config.Thresholds
                    |> Option.map (fun band ->
                        { Code = findingCode
                          Band = band
                          Path = relativePath
                          LineCount = lineCount }))
                |> List.sortWith (fun a b ->
                    if a.LineCount <> b.LineCount then
                        compare b.LineCount a.LineCount
                    else
                        String.CompareOrdinal(a.Path, b.Path))

            Ok
                { Config = config
                  Findings = findings
                  InspectedFiles = files.Length }
        with ex ->
            Error ex.Message
