#!/usr/bin/env node
"use strict";

// The entire Node surface of @echelon-foundry/sde.
//
// Its only job is to start the packaged F# executable for the host platform
// and get out of the way. It deliberately contains NO lifecycle logic: it
// does not know what files SDE installs, what a valid installation is, what
// an upgrade requires, or what any exit code means. Every one of those
// questions is answered by the F# CLI it launches. If this file ever needs
// to know something about SDE itself, that knowledge is in the wrong place.

const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

// Exit codes 3 and 4 are the launcher's own, and are the only codes it
// produces. Everything else is the F# process's exit code, forwarded
// unchanged. See docs/cli.md for the full contract.
const EXIT_UNSUPPORTED_PLATFORM = 3;
const EXIT_PREREQUISITE_FAILURE = 4;

// Windows on ARM64 runs x64 binaries under emulation, so it maps to the
// win-x64 build rather than being reported as unsupported. Every other entry
// is a natively built target.
function runtimeIdentifier(platform, arch, isMusl) {
  if (platform === "win32") {
    if (arch === "x64" || arch === "arm64") return "win-x64";
    return null;
  }
  if (platform === "darwin") {
    if (arch === "x64") return "osx-x64";
    if (arch === "arm64") return "osx-arm64";
    return null;
  }
  if (platform === "linux") {
    if (arch === "x64") return isMusl ? "linux-musl-x64" : "linux-x64";
    if (arch === "arm64") return "linux-arm64";
    return null;
  }
  return null;
}

// A self-contained .NET build links against either glibc or musl and the two
// are not interchangeable, so Alpine and similar musl distributions need
// their own build. Node does not expose the libc directly; the absence of a
// glibc runtime version in its own process report is the established signal.
function detectMusl() {
  try {
    const report = process.report.getReport();
    return !report.header.glibcVersionRuntime;
  } catch {
    return false;
  }
}

function main() {
  const platform = process.platform;
  const arch = process.arch;
  const rid = runtimeIdentifier(platform, arch, platform === "linux" && detectMusl());

  if (!rid) {
    process.stderr.write(
      `@echelon-foundry/sde does not ship an executable for ${platform}-${arch}.\n` +
        "Supported platforms: Windows x64 (and ARM64 under emulation), Linux x64, " +
        "Linux x64 (musl), Linux ARM64, macOS x64, macOS ARM64.\n"
    );
    return EXIT_UNSUPPORTED_PLATFORM;
  }

  const executable = path.join(
    __dirname,
    "..",
    "runtimes",
    rid,
    platform === "win32" ? "sde.exe" : "sde"
  );

  if (!fs.existsSync(executable)) {
    process.stderr.write(
      `This @echelon-foundry/sde installation is missing its ${rid} executable ` +
        `(expected at ${executable}).\n` +
        "If you are developing this package from source, run `npm run build` first.\n"
    );
    return EXIT_PREREQUISITE_FAILURE;
  }

  // Arguments are passed as an array, never through a shell, so nothing in
  // them can be interpreted as a command.
  const result = spawnSync(executable, process.argv.slice(2), { stdio: "inherit" });

  if (result.error) {
    process.stderr.write(`Could not start ${executable}: ${result.error.message}\n`);
    return EXIT_PREREQUISITE_FAILURE;
  }

  // A process killed by a signal has a null status; report that as a
  // prerequisite failure rather than silently succeeding.
  if (result.status === null) {
    process.stderr.write(
      `sde was terminated by signal ${result.signal ?? "unknown"} before it could report a result.\n`
    );
    return EXIT_PREREQUISITE_FAILURE;
  }

  return result.status;
}

process.exitCode = main();
