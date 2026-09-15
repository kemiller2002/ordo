#!/usr/bin/env node
// Release-time orchestration: runs `dotnet publish` once per supported
// platform and stages the resulting executables under runtimes/<rid>/.
//
// This script makes no decision about what SDE installs, what state means,
// or what goes into the execution package — it only invokes the .NET SDK and
// moves the files it produces. The package payload is built by the F#
// package builder (see `npm run build:payload`).

import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const DISTRIBUTION_DIR = path.resolve(HERE, "..");
const REPO_ROOT = path.resolve(DISTRIBUTION_DIR, "..");
const PROJECT = path.join(REPO_ROOT, "src", "Sde.Cli", "Sde.Cli.fsproj");
const RUNTIMES_DIR = path.join(DISTRIBUTION_DIR, "runtimes");

// The platforms the package ships executables for. Keep this list, the
// launcher's mapping in bin/sde.js, and the supported-platforms section of
// README.md in agreement.
const RUNTIME_IDENTIFIERS = [
  "win-x64",
  "linux-x64",
  "linux-musl-x64",
  "linux-arm64",
  "osx-x64",
  "osx-arm64",
];

const only = process.argv.includes("--only")
  ? process.argv[process.argv.indexOf("--only") + 1]?.split(",").filter(Boolean)
  : null;

const targets = only ?? RUNTIME_IDENTIFIERS;

for (const rid of targets) {
  if (!RUNTIME_IDENTIFIERS.includes(rid)) {
    console.error(`Unknown runtime identifier: ${rid}`);
    console.error(`Supported: ${RUNTIME_IDENTIFIERS.join(", ")}`);
    process.exit(1);
  }
}

fs.mkdirSync(RUNTIMES_DIR, { recursive: true });

for (const rid of targets) {
  const outputDir = path.join(RUNTIMES_DIR, rid);
  fs.rmSync(outputDir, { recursive: true, force: true });

  console.log(`Publishing ${rid} ...`);

  const result = spawnSync(
    "dotnet",
    ["publish", PROJECT, "-c", "Release", "-r", rid, "-o", outputDir, "--nologo", "-v", "quiet"],
    { stdio: "inherit" }
  );

  if (result.status !== 0) {
    console.error(`dotnet publish failed for ${rid}.`);
    process.exit(result.status ?? 1);
  }

  const executable = path.join(outputDir, rid.startsWith("win-") ? "sde.exe" : "sde");

  if (!fs.existsSync(executable)) {
    console.error(`dotnet publish reported success for ${rid} but produced no executable at ${executable}.`);
    process.exit(1);
  }

  // Anything other than the executable itself is build residue that must not
  // be published: debug symbols, dependency manifests, staged assemblies.
  for (const entry of fs.readdirSync(outputDir)) {
    if (path.join(outputDir, entry) !== executable) {
      fs.rmSync(path.join(outputDir, entry), { recursive: true, force: true });
    }
  }

  // npm archives do not preserve the execute bit reliably on every platform,
  // but setting it here keeps a local `npm pack` faithful on POSIX hosts.
  if (!rid.startsWith("win-")) {
    fs.chmodSync(executable, 0o755);
  }

  const sizeMb = (fs.statSync(executable).size / (1024 * 1024)).toFixed(1);
  console.log(`  ${rid}: ${path.relative(DISTRIBUTION_DIR, executable)} (${sizeMb} MB)`);
}

console.log(`Staged ${targets.length} runtime(s) under ${path.relative(REPO_ROOT, RUNTIMES_DIR)}/.`);
