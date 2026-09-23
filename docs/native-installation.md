# Native Ordo installation

Ordo can be consumed without npm, Node.js, or a machine-wide .NET runtime.

Each stable release publishes a platform bundle containing the self-contained F# executable, the exact versioned SDE execution payload that init installs, a VERSION marker, and release-level SHA-256 checksums.

The installer places immutable versions under ~/.echelon/tools/ordo/<version> and exposes both ordo and sde from ~/.echelon/bin. The sde name remains a compatibility alias. The repository installation contract is unchanged: Ordo still owns .sde/ and .echelon/sde.json.

## Install

macOS or Linux:

    curl -fsSL https://raw.githubusercontent.com/kemiller2002/ordo/main/scripts/install-native.sh | sh

Pin a version:

    curl -fsSL https://raw.githubusercontent.com/kemiller2002/ordo/main/scripts/install-native.sh | sh -s -- --version 1.4.0

Windows PowerShell:

    irm https://raw.githubusercontent.com/kemiller2002/ordo/main/scripts/install-native.ps1 | iex

After installation:

    ordo init
    ordo status
    ordo verify
    ordo doctor

Existing automation can continue to use sde init, sde verify, and the other established command names.

## Distribution rule

npm remains a compatibility distribution channel. It is no longer required for native consumers. GitHub Releases are the native binary authority, and every downloaded bundle is checked against the release checksum before installation.
