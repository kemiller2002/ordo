/// `.echelon/sde.json` — the Echelon Foundry installation record.
///
/// `.echelon/` is the shared root every Echelon Foundry tool writes its own
/// record into; SDE owns exactly one file there and never touches another
/// tool's. The record exists so that installation state is a declared fact
/// rather than something inferred from which files happen to be present.
///
/// COMPATIBILITY: releases up to and including 1.1.1 wrote no record at all.
/// An installation without one is not broken — it is configuration version 1,
/// and `upgrade` migrates it to configuration version 2 by writing the record
/// from the existing MANIFEST.json. Nothing is deleted or rewritten to do it.
module Sde.Core.InstallationRecord

open Sde.Core.Json

let schemaVersion = 1

/// The configuration version this release installs. Bump this only together
/// with a migration in Migrations.fs that moves an installation to it.
let currentConfigurationVersion = 2

/// The configuration version an installation is at when it has a valid
/// .sde/ payload but no .echelon/sde.json, which is every installation
/// produced by a release before this one.
let legacyConfigurationVersion = 1

let toolName = "sde"

type Record =
    { SchemaVersion: int
      Tool: string
      Package: string
      InstalledVersion: string
      ConfigurationVersion: int
      InstallRoot: string
      /// Repository-relative paths this tool manages. A consumer, another
      /// Echelon tool or an agent can read this to learn what SDE owns
      /// without knowing anything about SDE's internals.
      ManagedArtifacts: string list }

/// Deliberately absent from the schema: timestamps, absolute paths, user or
/// machine identifiers, and anything resembling a credential. The record is
/// committed to consumers' repositories, so it must be reproducible on any
/// machine and must never carry a secret.
let create (packageName: string) (installedVersion: string) : Record =
    { SchemaVersion = schemaVersion
      Tool = toolName
      Package = packageName
      InstalledVersion = installedVersion
      ConfigurationVersion = currentConfigurationVersion
      InstallRoot = Ownership.installRootName
      ManagedArtifacts = [ Ownership.installRootName; Ownership.installationRecordName ] }

let toJson (record: Record) : JsonValue =
    JObject
        [ "schemaVersion", JInt record.SchemaVersion
          "tool", JString record.Tool
          "package", JString record.Package
          "installedVersion", JString record.InstalledVersion
          "configurationVersion", JInt record.ConfigurationVersion
          "installRoot", JString record.InstallRoot
          "managedArtifacts", JArray [ for path in record.ManagedArtifacts -> JString path ] ]

let serialize (record: Record) : string = renderDocument (toJson record)

let validateShape (value: JsonValue) : string list =
    match value with
    | JObject _ ->
        let problems = ResizeArray<string>()

        match tryInt (tryField "schemaVersion" value) with
        | Some v when v = schemaVersion -> ()
        | _ -> problems.Add(sprintf "unsupported schemaVersion (expected %d)" schemaVersion)

        match tryString (tryField "tool" value) with
        | Some t when t = toolName -> ()
        | other ->
            problems.Add(
                sprintf "tool must be %s, not %s" (Json.quote toolName) (other |> Option.map Json.quote |> Option.defaultValue "absent")
            )

        match tryString (tryField "installedVersion" value) with
        | Some v when SemVer.tryParse v |> Option.isSome -> ()
        | _ -> problems.Add "installedVersion must be a major.minor.patch version"

        match tryInt (tryField "configurationVersion" value) with
        | Some v when v >= 1 -> ()
        | _ -> problems.Add "configurationVersion must be a positive integer"

        List.ofSeq problems
    | _ -> [ "installation record is not an object" ]

let private fromJson (value: JsonValue) : Record =
    { SchemaVersion = tryInt (tryField "schemaVersion" value) |> Option.defaultValue schemaVersion
      Tool = tryString (tryField "tool" value) |> Option.defaultValue toolName
      Package = tryString (tryField "package" value) |> Option.defaultValue ""
      InstalledVersion = tryString (tryField "installedVersion" value) |> Option.defaultValue ""
      ConfigurationVersion =
        tryInt (tryField "configurationVersion" value)
        |> Option.defaultValue currentConfigurationVersion
      InstallRoot =
        tryString (tryField "installRoot" value)
        |> Option.defaultValue Ownership.installRootName
      ManagedArtifacts =
        tryArray (tryField "managedArtifacts" value)
        |> Option.defaultValue []
        |> List.choose (fun item ->
            match item with
            | JString s -> Some s
            | _ -> None) }

let recordPath (projectRoot: string) : string =
    System.IO.Path.Combine(projectRoot, Paths.toNativePath Ownership.installationRecordName)

type ReadResult =
    /// No record on disk. For a repository with a valid .sde/, this means
    /// configuration version 1, not a fault.
    | Absent
    | Present of Record
    | Unreadable of string

let read (projectRoot: string) : ReadResult =
    let path = recordPath projectRoot

    if not (FileSystem.fileExists path) then
        Absent
    else
        try
            match parse (FileSystem.readAllText path) with
            | Error message -> Unreadable(sprintf "malformed %s: %s" Ownership.installationRecordName message)
            | Ok value ->
                match validateShape value with
                | [] -> Present(fromJson value)
                | problems -> Unreadable(sprintf "invalid %s: %s" Ownership.installationRecordName (String.concat "; " problems))
        with ex ->
            Unreadable(sprintf "unreadable %s: %s" Ownership.installationRecordName ex.Message)
