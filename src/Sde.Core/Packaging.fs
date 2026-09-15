/// The package's public identity.
///
/// These strings are part of the product interface: they appear in npm
/// metadata, in help text, in the installed MANIFEST.json's packageName, and
/// in the installation record. They live in one module so that the CLI, the
/// package builder and the installed artifacts cannot disagree about what
/// this tool is called.
module Sde.Core.Packaging

/// The npm package name.
let packageName = "@echelon-foundry/sde"

/// The executable name exposed by the package's bin mapping.
let executableName = "sde"

/// The directory inside the npm package holding the built execution package
/// that `init` installs.
let payloadDirectoryName = "dist"

/// The directory inside the npm package holding the per-platform native
/// executables the Node launcher chooses between.
let runtimesDirectoryName = "runtimes"
