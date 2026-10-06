/// `.echelon/toolchain.json`: the Echelon tool versions a repository expects.
///
/// The file is shared. `echelon setup` installs what it names and `echelon
/// doctor` checks it; Praxis owns its `praxis` property and the repository
/// owns everything else it chooses to record. Ordo owns exactly one property,
/// `ordo`, and keeps it equal to the release `init` or `upgrade` installs, so
/// the pin is derived from the release itself rather than from a literal that
/// goes stale. Nothing else in the file is ever changed.
module Sde.Core.ToolchainPin

/// The property Ordo owns.
let key = "ordo"

let schemaVersion = 1

/// How a repository's toolchain manifest relates to the release installing it.
type Reconciliation =
    /// The pin already names this release; nothing is written.
    | Current
    /// `content` is the manifest with the pin set to this release.
    /// `previous` is the pin it replaces (None when absent).
    | Repin of previous: string option * content: string
    /// Present but not a plain JSON object; preserved untouched, exactly as
    /// any other shared file a tool cannot safely rewrite.
    | Unrecognised

/// The manifest a repository without one receives.
let seed (version: string) : string =
    Json.renderDocument(Json.JObject [ "schemaVersion", Json.JInt schemaVersion; key, Json.JString version ])

/// Decides what the manifest must contain for `version`, given its current
/// text (None when the file is absent). Pure.
let reconcile (version: string) (existing: string option) : Reconciliation =
    match existing with
    | None -> Repin(None, seed version)
    | Some text ->
        match Json.setStringProperty key version text with
        | Json.Unchanged -> Current
        | Json.Rewritten(previous, content) -> Repin(previous, content)
        | Json.NotAnObject -> Unrecognised

let path (projectRoot: string) =
    System.IO.Path.Combine(projectRoot, Ownership.toolchainManifestName)

/// The manifest's current text, if the file exists. Read-only.
let read (projectRoot: string) : string option =
    let file = path projectRoot

    if FileSystem.fileExists file then
        Some(FileSystem.readAllText file)
    else
        None
