/// Who is allowed to change what.
///
/// Every path this tool knows about carries an ownership classification, and
/// that classification — not the fact that `init` happened to create the
/// file — decides whether a later `upgrade` may replace it. This is the rule
/// that makes "init never destroys local work" checkable rather than
/// aspirational.
module Sde.Core.Ownership

type Ownership =
    /// Controlled by the tool. Replaced wholesale by an upgrade, but only
    /// when the installed copy is byte-identical to what the tool last
    /// installed; a modified ToolOwned file blocks the upgrade instead.
    | ToolOwned
    /// Derived from authoritative inputs and safe to regenerate, because
    /// nothing a user could write into it would survive being recomputed
    /// anyway (MANIFEST.json, VERSION, the installation record).
    | Generated
    /// Controlled by the repository. Never created, never modified and never
    /// deleted by this tool.
    | UserOwned
    /// The tool defines the schema, the repository owns the values. The tool
    /// reads it and validates it, but will not rewrite it; a Shared file that
    /// is present and invalid is a conflict the tool reports rather than
    /// resolves.
    | Shared

let describe =
    function
    | ToolOwned -> "tool-owned"
    | Generated -> "generated"
    | UserOwned -> "user-owned"
    | Shared -> "shared"

/// The directory an SDE execution package is installed into.
let installRootName = ".sde"

/// The shared root for Echelon Foundry tooling installation records. The
/// directory itself is shared across tools — SDE owns exactly one file in
/// it and must never remove or rewrite another tool's record.
let echelonRootName = ".echelon"

/// SDE's own installation record inside the shared root.
let installationRecordName = ".echelon/sde.json"

/// The repository-owned structural-review configuration.
let structuralConfigName = "sde.config.json"

/// Generated bookkeeping files inside the install root.
let manifestName = "MANIFEST.json"
let versionFileName = "VERSION"

/// Classifies a repository-relative manifest path. Paths inside the install
/// root are the tool's methodology payload; the two bookkeeping files it
/// writes alongside them are Generated, not ToolOwned, because they are
/// recomputed from the payload rather than shipped as content.
let classify (repositoryRelativePath: string) : Ownership =
    match repositoryRelativePath with
    | path when path = structuralConfigName -> Shared
    | path when path = installationRecordName -> Generated
    | path when path = installRootName + "/" + manifestName -> Generated
    | path when path = installRootName + "/" + versionFileName -> Generated
    | path when path.StartsWith(installRootName + "/") -> ToolOwned
    | _ -> UserOwned

/// Classifies a path relative to the install root, as manifest entries are
/// recorded.
let classifyManaged (installRootRelativePath: string) : Ownership =
    classify (installRootName + "/" + installRootRelativePath)
