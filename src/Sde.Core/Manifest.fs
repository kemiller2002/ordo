/// `.sde/MANIFEST.json` — the per-installation integrity record.
///
/// COMPATIBILITY: schema version 1 is already installed in consumers'
/// repositories by @echelon-foundry/sde 1.0.0 through 1.1.1. Its shape, its
/// field names, its field order and its exact serialized bytes are a public
/// contract and are reproduced here unchanged. A manifest written by this
/// implementation is byte-identical to one written by the JavaScript
/// implementation it replaces, for the same payload.
module Sde.Core.Manifest

open System.Text.RegularExpressions
open Sde.Core.Json

let schemaVersion = 1

type ManagedFile = { Path: string; Sha256: string }

type Manifest =
    { SchemaVersion: int
      SdeVersion: string
      MethodVersion: string option
      SourceRevision: string option
      PackageName: string
      Files: ManagedFile list }

let private hexPattern = Regex(@"^[0-9a-f]{64}$", RegexOptions.CultureInvariant)

/// MANIFEST.json is never listed inside its own files[]: hashing a file
/// whose content includes that hash is not meaningful. VERSION and README.md
/// are ordinary managed content and are listed like anything else, so
/// editing them is detected the same way.
let private selfExcluded = set [ Ownership.manifestName ]

let toJson (manifest: Manifest) : JsonValue =
    JObject
        [ "schemaVersion", JInt manifest.SchemaVersion
          "sdeVersion", JString manifest.SdeVersion
          "methodVersion",
          (match manifest.MethodVersion with
           | Some v -> JString v
           | None -> JNull)
          "sourceRevision",
          (match manifest.SourceRevision with
           | Some v -> JString v
           | None -> JNull)
          "packageName", JString manifest.PackageName
          "files",
          JArray
              [ for file in manifest.Files ->
                    JObject [ "path", JString file.Path; "sha256", JString file.Sha256 ] ] ]

let serialize (manifest: Manifest) : string = renderDocument (toJson manifest)

/// Validates a parsed manifest against schema version 1, accumulating every
/// problem rather than stopping at the first. A manifest that fails this is
/// never repaired automatically: the commands refuse to act on an
/// installation whose own record they cannot trust.
let validateShape (value: JsonValue) : string list =
    match value with
    | JObject _ ->
        let problems = ResizeArray<string>()

        match tryInt (tryField "schemaVersion" value) with
        | Some v when v = schemaVersion -> ()
        | other ->
            let rendered =
                match other with
                | Some v -> string v
                | None ->
                    match tryField "schemaVersion" value with
                    | Some v -> render v
                    | None -> "undefined"

            problems.Add(sprintf "unsupported schemaVersion %s" rendered)

        match tryString (tryField "sdeVersion" value) with
        | Some s when s.Length > 0 -> ()
        | _ -> problems.Add "sdeVersion must be a non-empty string"

        match tryString (tryField "packageName" value) with
        | Some s when s.Length > 0 -> ()
        | _ -> problems.Add "packageName must be a non-empty string"

        match tryArray (tryField "files" value) with
        | None ->
            problems.Add "files must be an array"
            List.ofSeq problems
        | Some entries ->
            let seen = System.Collections.Generic.HashSet<string>()

            for entry in entries do
                match entry with
                | JObject _ ->
                    let path = tryString (tryField "path" entry)

                    match path with
                    | Some p when Paths.isSafeRelativePath p ->
                        match tryString (tryField "sha256" entry) with
                        | Some hash when hexPattern.IsMatch hash -> ()
                        | _ -> problems.Add(sprintf "files[] entry %s has an invalid sha256" p)

                        if not (seen.Add p) then
                            problems.Add(sprintf "files[] entry %s is duplicated" p)
                    | _ ->
                        let rendered =
                            match tryField "path" entry with
                            | Some v -> render v
                            | None -> "undefined"

                        problems.Add(sprintf "files[] entry has an unsafe or missing path: %s" rendered)
                | _ -> problems.Add "a files[] entry is not an object"

            List.ofSeq problems
    | _ -> [ "manifest is not an object" ]

let private fromJson (value: JsonValue) : Manifest =
    { SchemaVersion = tryInt (tryField "schemaVersion" value) |> Option.defaultValue schemaVersion
      SdeVersion = tryString (tryField "sdeVersion" value) |> Option.defaultValue ""
      MethodVersion = tryString (tryField "methodVersion" value)
      SourceRevision = tryString (tryField "sourceRevision" value)
      PackageName = tryString (tryField "packageName" value) |> Option.defaultValue ""
      Files =
        tryArray (tryField "files" value)
        |> Option.defaultValue []
        |> List.map (fun entry ->
            { Path = tryString (tryField "path" entry) |> Option.defaultValue ""
              Sha256 = tryString (tryField "sha256" entry) |> Option.defaultValue "" }) }

/// Reads and validates the manifest of an installation or a packaged
/// payload. The error strings match the previous implementation's wording,
/// because they are surfaced verbatim to users and to CI logs.
let read (installDir: string) : Result<Manifest, string> =
    let manifestPath = System.IO.Path.Combine(installDir, Ownership.manifestName)

    if not (FileSystem.fileExists manifestPath) then
        Error(sprintf "missing %s" Ownership.manifestName)
    else
        let text =
            try
                Ok(FileSystem.readAllText manifestPath)
            with ex ->
                Error(sprintf "unreadable %s: %s" Ownership.manifestName ex.Message)

        match text with
        | Error message -> Error message
        | Ok text ->
            match parse text with
            | Error message -> Error(sprintf "malformed %s: %s" Ownership.manifestName message)
            | Ok value ->
                match validateShape value with
                | [] -> Ok(fromJson value)
                | problems -> Error(sprintf "invalid %s: %s" Ownership.manifestName (String.concat "; " problems))

/// Builds a manifest describing every file currently under `dir`.
let build
    (packageName: string)
    (sdeVersion: string)
    (methodVersion: string option)
    (sourceRevision: string option)
    (dir: string)
    : Result<Manifest, string> =
    match FileSystem.listManagedFiles dir with
    | Error message -> Error message
    | Ok paths ->
        let files =
            paths
            |> List.filter (fun path -> not (selfExcluded.Contains path))
            |> List.map (fun path ->
                { Path = path
                  Sha256 = FileSystem.sha256File (Paths.safeJoinOrFail dir path) })

        Ok
            { SchemaVersion = schemaVersion
              SdeVersion = sdeVersion
              MethodVersion = methodVersion
              SourceRevision = sourceRevision
              PackageName = packageName
              Files = files }
