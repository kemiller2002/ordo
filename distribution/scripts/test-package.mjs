#!/usr/bin/env node
// Exercises the REAL packed npm artifact, not the source tree.
//
// `dotnet test` proves the F# lifecycle is correct. It proves nothing about
// whether the published tarball contains the right files, whether the bin
// mapping resolves, whether the launcher finds its executable from the
// installed layout, or whether the documented commands work for someone who
// ran `npx @echelon-foundry/sde`. That is what this script checks: it takes a
// tarball — packing one, or the artifact named by SDE_TEST_TARBALL — installs
// it into a throwaway project, and drives the installed `sde` executable
// against clean temporary repositories.

import { spawnSync } from "node:child_process";
import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import zlib from "node:zlib";
import { fileURLToPath } from "node:url";

const DISTRIBUTION_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const PACKAGE_JSON = JSON.parse(fs.readFileSync(path.join(DISTRIBUTION_DIR, "package.json"), "utf8"));

let failures = 0;
let checks = 0;

function check(name, body) {
  checks += 1;
  try {
    body();
    console.log(`  ok   ${name}`);
  } catch (error) {
    failures += 1;
    console.log(`  FAIL ${name}`);
    console.log(`       ${error.message.split("\n").join("\n       ")}`);
  }
}

function section(title) {
  console.log(`\n${title}`);
}

function tempDir(prefix) {
  return fs.mkdtempSync(path.join(os.tmpdir(), prefix));
}

const isWindows = process.platform === "win32";

function run(command, args, options = {}) {
  const result = spawnSync(command, args, { encoding: "utf8", ...options });
  return {
    status: result.status,
    stdout: result.stdout ?? "",
    // A spawn that never started reports its reason in `error` with no
    // stderr at all. Folding it in here means a failure is always
    // diagnosable from the output rather than showing up as silence.
    stderr: (result.stderr ?? "") + (result.error ? `spawn ${command} failed: ${result.error.message}` : ""),
  };
}

// npm's Windows entry point is npm.cmd, and since the fix for
// CVE-2024-27980 Node refuses to spawn a .cmd without a shell. Arguments are
// quoted because a runner temp path can contain characters the shell would
// otherwise split on.
function runNpm(args, cwd) {
  if (!isWindows) return run("npm", args, { cwd });
  return run("npm.cmd", args.map((arg) => `"${arg}"`), { cwd, shell: true });
}

// Lists the regular files in a .tgz without shelling out to `tar`.
//
// Calling `tar` meant depending on an external binary being on PATH, which
// silently produced an empty listing on Windows and turned every
// "nothing unexpected is published" check into a false pass. Node has gzip
// built in and a tar header is a fixed 512-byte record, so reading the
// archive directly is both shorter and platform-independent.
function listTarballEntries(tgzPath) {
  const buffer = zlib.gunzipSync(fs.readFileSync(tgzPath));
  const entries = [];
  let offset = 0;

  while (offset + 512 <= buffer.length) {
    const header = buffer.subarray(offset, offset + 512);
    if (header.every((byte) => byte === 0)) break;

    const field = (start, length) =>
      header.subarray(start, start + length).toString("utf8").replace(/\0.*$/s, "").trim();

    const name = field(0, 100);
    const prefix = field(345, 155);
    const typeFlag = String.fromCharCode(header[156]);
    const size = parseInt(field(124, 12) || "0", 8);

    // '0' and NUL both mean a regular file; directories and metadata records
    // are not part of what gets published.
    if (typeFlag === "0" || typeFlag === "\0") {
      entries.push(prefix ? `${prefix}/${name}` : name);
    }

    offset += 512 + Math.ceil(size / 512) * 512;
  }

  return entries;
}

// A repository snapshot: every file's path and content hash. Two snapshots
// being equal is the operational definition of "changed nothing".
function snapshot(root) {
  const entries = [];
  const walk = (dir, prefix) => {
    for (const name of fs.readdirSync(dir).sort()) {
      const full = path.join(dir, name);
      const rel = prefix ? `${prefix}/${name}` : name;
      if (fs.statSync(full).isDirectory()) walk(full, rel);
      else entries.push(`${rel}:${crypto.createHash("sha256").update(fs.readFileSync(full)).digest("hex")}`);
    }
  };
  walk(root, "");
  return entries.join("\n");
}

// ---------------------------------------------------------------------------

section("Packing");

// SDE_TEST_TARBALL points at an already-packed artifact. CI uses it so that
// every platform exercises the one tarball that would actually be published,
// rather than each rebuilding its own — testing the exact artifact is the
// whole point of this script.
const suppliedTarball = process.env.SDE_TEST_TARBALL;
const packDir = suppliedTarball ? null : tempDir("sde-pack-");
let tarball;

if (suppliedTarball) {
  if (!fs.existsSync(suppliedTarball)) {
    console.error(`SDE_TEST_TARBALL is set to ${suppliedTarball} but no such file exists.`);
    process.exit(1);
  }
  tarball = path.resolve(suppliedTarball);
  console.log(`  using ${path.basename(tarball)} (${(fs.statSync(tarball).size / (1024 * 1024)).toFixed(1)} MB)`);
} else {
  const dryRun = runNpm(["pack", "--dry-run", "--json"], DISTRIBUTION_DIR);
  if (dryRun.status !== 0) {
    console.error("npm pack --dry-run failed:\n" + dryRun.stderr);
    process.exit(1);
  }

  const packed = runNpm(["pack", "--pack-destination", packDir, "--json"], DISTRIBUTION_DIR);
  if (packed.status !== 0) {
    console.error("npm pack failed:\n" + packed.stderr);
    process.exit(1);
  }

  const packedName = JSON.parse(packed.stdout)[0].filename;
  tarball = path.join(packDir, packedName);
  console.log(`  packed ${packedName} (${(fs.statSync(tarball).size / (1024 * 1024)).toFixed(1)} MB)`);
}

const contents = listTarballEntries(tarball).map((entry) => entry.replace(/^package\//, ""));

section("Package contents");

// Without this, an empty listing would make every "nothing unexpected is
// published" assertion below pass vacuously.
check("the tarball listing is readable", () =>
  assert.ok(contents.length > 0, `no entries could be read from ${tarball}`));

check("the launcher is published", () => assert.ok(contents.includes("bin/sde.js")));
check("the legacy entry point is published", () => assert.ok(contents.includes("bin/sde.mjs")));
check("the execution package payload is published", () =>
  assert.ok(contents.includes("dist/MANIFEST.json"), "dist/MANIFEST.json missing"));
check("the README is published", () => assert.ok(contents.includes("README.md")));
check("at least one platform executable is published", () =>
  assert.ok(contents.some((entry) => /^runtimes\/[^/]+\/sde(\.exe)?$/.test(entry)), "no runtimes/<rid>/sde found"));

check("no source, test or build-input files are published", () => {
  const unexpected = contents.filter(
    (entry) =>
      entry.startsWith("src/") ||
      entry.startsWith("test/") ||
      entry.startsWith("scripts/") ||
      entry.startsWith("authored/") ||
      entry.startsWith("obj/") ||
      entry.endsWith(".fsproj")
  );
  assert.deepEqual(unexpected, [], `unexpected published files: ${unexpected.join(", ")}`);
});

check("no debug symbols or dependency manifests are published", () => {
  const unexpected = contents.filter((entry) => entry.endsWith(".pdb") || entry.endsWith(".deps.json") || entry.endsWith(".runtimeconfig.json"));
  assert.deepEqual(unexpected, [], `unexpected published files: ${unexpected.join(", ")}`);
});

check("nothing that looks like a secret or local configuration is published", () => {
  const suspicious = contents.filter((entry) =>
    /(^|\/)(\.env|\.npmrc|\.git|node_modules|coverage)(\/|$)/.test(entry) || /\.(pem|key)$/.test(entry)
  );
  assert.deepEqual(suspicious, [], `suspicious published files: ${suspicious.join(", ")}`);
});

// ---------------------------------------------------------------------------

section("Installing the packed artifact");

const consumer = tempDir("sde-consumer-");
fs.writeFileSync(path.join(consumer, "package.json"), JSON.stringify({ name: "consumer", version: "1.0.0", private: true }, null, 2));

const install = runNpm(["install", "--no-audit", "--no-fund", tarball], consumer);
if (install.status !== 0) {
  console.error("installing the tarball failed:\n" + install.stdout + install.stderr);
  process.exit(1);
}

const sdeBin = path.join(consumer, "node_modules", ".bin", isWindows ? "sde.cmd" : "sde");

check("the bin mapping produced an executable", () => assert.ok(fs.existsSync(sdeBin), `${sdeBin} does not exist`));

check("npm install did not create an installation in the consumer", () => {
  assert.ok(!fs.existsSync(path.join(consumer, ".sde")), "npm install must not mutate the consuming repository");
  assert.ok(!fs.existsSync(path.join(consumer, ".echelon")), "npm install must not mutate the consuming repository");
});

// The bin shim is a .cmd on Windows, so it needs the same shell treatment
// as npm itself.
const sde = (args, cwd) =>
  isWindows
    ? run(`"${sdeBin}"`, args, { cwd, shell: true })
    : run(sdeBin, args, { cwd });

// ---------------------------------------------------------------------------

section("Version and help");

check("--version prints exactly the package version", () => {
  const result = sde(["--version"], consumer);
  assert.equal(result.status, 0);
  assert.equal(result.stdout.trim(), PACKAGE_JSON.version);
});

check("--help works and names every documented command", () => {
  const result = sde(["--help"], consumer);
  assert.equal(result.status, 0);
  for (const command of ["init", "status", "verify", "upgrade", "doctor"]) {
    assert.match(result.stdout, new RegExp(`\\b${command}\\b`), `--help does not mention ${command}`);
  }
});

for (const command of ["init", "status", "verify", "upgrade", "doctor"]) {
  check(`${command} --help works`, () => {
    const result = sde([command, "--help"], consumer);
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, new RegExp(`^sde ${command}`, "m"));
  });
}

check("an unknown command exits 2 with a usage message", () => {
  const result = sde(["bogus"], consumer);
  assert.equal(result.status, 2);
  assert.match(result.stderr, /Usage: sde/);
});

// ---------------------------------------------------------------------------

section("Lifecycle against a clean repository");

const project = tempDir("sde-project-");

check("init --dry-run changes nothing", () => {
  const before = snapshot(project);
  const result = sde(["init", "--dry-run"], project);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(snapshot(project), before);
});

check("init --dry-run --json emits one valid JSON document on stdout", () => {
  const result = sde(["init", "--dry-run", "--json"], project);
  assert.equal(result.status, 0, result.stderr);
  const document = JSON.parse(result.stdout);
  assert.equal(document.command, "init");
  assert.equal(document.dryRun, true);
  assert.ok(Array.isArray(document.changes) && document.changes.length > 0);
  assert.equal(result.stderr, "");
});

check("init --check exits 5 when the repository is not initialized", () => {
  assert.equal(sde(["init", "--check"], project).status, 5);
});

check("init installs the execution package", () => {
  const result = sde(["init"], project);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /installed successfully/);
  assert.ok(fs.existsSync(path.join(project, ".sde", "MANIFEST.json")));
  assert.ok(fs.existsSync(path.join(project, ".sde", "method", "CONSTRUCTION-METHOD.md")));
  assert.ok(fs.existsSync(path.join(project, ".echelon", "sde.json")));
});

check("decision-and-evidence doctrine is installed", () => {
  assert.ok(
    fs.existsSync(path.join(project, ".sde", "architecture", "DECISION-AND-EVIDENCE-SEMANTICS.md")),
    "1.3.0 must install the governed decision/evidence semantics"
  );
});

check("a second init produces no meaningful change", () => {
  const before = snapshot(project);
  const result = sde(["init"], project);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /already installed/);
  assert.equal(snapshot(project), before, "a second init must not change the repository");
});

check("init --check exits 0 once the repository is current", () => {
  assert.equal(sde(["init", "--check"], project).status, 0);
});

check("status reports the installed version and changes nothing", () => {
  const before = snapshot(project);
  const result = sde(["status"], project);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, new RegExp(`Installed version:\\s+${PACKAGE_JSON.version.replace(/\./g, "\\.")}`));
  assert.equal(snapshot(project), before);
});

check("status --json is valid JSON with no decorative output", () => {
  const result = sde(["status", "--json"], project);
  const document = JSON.parse(result.stdout);
  assert.equal(document.command, "status");
  assert.equal(document.state, "installed");
  assert.equal(document.installed.version, PACKAGE_JSON.version);
  assert.equal(document.exitCode, result.status);
  assert.equal(result.stderr, "");
});

check("verify passes and changes nothing", () => {
  const before = snapshot(project);
  const result = sde(["verify"], project);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(snapshot(project), before);
});

check("verify --strict passes on a fresh installation", () => {
  assert.equal(sde(["verify", "--strict"], project).status, 0);
});

check("verify --json is valid JSON", () => {
  const document = JSON.parse(sde(["verify", "--json"], project).stdout);
  assert.equal(document.passed, true);
  assert.deepEqual(document.problems, []);
});

check("doctor succeeds on a healthy repository", () => {
  const result = sde(["doctor"], project);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /0 error\(s\)/);
});

check("doctor --json is valid JSON", () => {
  const document = JSON.parse(sde(["doctor", "--json"], project).stdout);
  assert.equal(document.errors, 0);
  assert.ok(Array.isArray(document.diagnoses));
});

check("upgrade on a current installation is a no-op", () => {
  const before = snapshot(project);
  const result = sde(["upgrade"], project);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(snapshot(project), before);
});

check("update is accepted as a legacy alias for upgrade", () => {
  assert.equal(sde(["update"], project).status, 0);
});

// ---------------------------------------------------------------------------

section("Damaged and modified installations");

const damaged = tempDir("sde-damaged-");
sde(["init"], damaged);

check("an invalid installation fails verification with a non-zero exit code", () => {
  fs.rmSync(path.join(damaged, ".sde", "reference", "GLOSSARY.md"));
  const result = sde(["verify"], damaged);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /GLOSSARY\.md/);
});

check("doctor identifies the damage and explains it", () => {
  const document = JSON.parse(sde(["doctor", "--json"], damaged).stdout);
  assert.ok(document.errors > 0);
  const missing = document.diagnoses.find((d) => d.code === "SDE-DOCTOR-003");
  assert.ok(missing, "expected SDE-DOCTOR-003 for a missing managed file");
  assert.ok(missing.remedy, "a diagnosis must carry a remedy");
});

check("init refuses to overwrite a locally modified installation", () => {
  const modified = tempDir("sde-modified-");
  sde(["init"], modified);
  const target = path.join(modified, ".sde", "method", "CONSTRUCTION-METHOD.md");
  fs.appendFileSync(target, "\nlocal edit\n");
  const before = fs.readFileSync(target, "utf8");

  const result = sde(["init"], modified);
  assert.notEqual(result.status, 0);
  assert.equal(fs.readFileSync(target, "utf8"), before, "a refused init must not touch the modified file");
  fs.rmSync(modified, { recursive: true, force: true });
});

check("a user-owned file is never touched", () => {
  const owned = tempDir("sde-owned-");
  const config = path.join(owned, "sde.config.json");
  const content = JSON.stringify({ structuralReview: { thresholds: { reviewAt: 250 } } }, null, 2);
  fs.writeFileSync(config, content);

  sde(["init"], owned);
  sde(["upgrade"], owned);

  assert.equal(fs.readFileSync(config, "utf8"), content);
  fs.rmSync(owned, { recursive: true, force: true });
});

check("another Echelon tool's record in .echelon/ is left alone", () => {
  const shared = tempDir("sde-shared-");
  fs.mkdirSync(path.join(shared, ".echelon"), { recursive: true });
  const other = path.join(shared, ".echelon", "ros.json");
  const content = JSON.stringify({ tool: "ros", installedVersion: "1.2.1" }, null, 2);
  fs.writeFileSync(other, content);

  sde(["init"], shared);

  assert.equal(fs.readFileSync(other, "utf8"), content);
  assert.ok(fs.existsSync(path.join(shared, ".echelon", "sde.json")));
  fs.rmSync(shared, { recursive: true, force: true });
});

// ---------------------------------------------------------------------------

section("Upgrade from a historical installation");

check("an installation from a release before .echelon/ is migrated in place", () => {
  const legacy = tempDir("sde-legacy-");
  sde(["init"], legacy);

  // Reproduce what a 1.0.0-1.1.1 installation looked like: a valid .sde/
  // payload with no installation record at all.
  fs.rmSync(path.join(legacy, ".echelon"), { recursive: true, force: true });

  const userFile = path.join(legacy, "NOTES.md");
  fs.writeFileSync(userFile, "my notes\n");

  assert.equal(sde(["verify"], legacy).status, 0, "a legacy installation must still verify by default");
  assert.notEqual(sde(["verify", "--strict"], legacy).status, 0, "strict mode must flag the older configuration version");

  const planned = JSON.parse(sde(["upgrade", "--dry-run", "--json"], legacy).stdout);
  assert.ok(planned.changes.some((change) => change.change === "run-migration"), "expected a migration in the plan");

  const result = sde(["upgrade"], legacy);
  assert.equal(result.status, 0, result.stderr);

  const record = JSON.parse(fs.readFileSync(path.join(legacy, ".echelon", "sde.json"), "utf8"));
  assert.equal(record.configurationVersion, 2);
  assert.equal(record.installedVersion, PACKAGE_JSON.version);
  assert.equal(fs.readFileSync(userFile, "utf8"), "my notes\n", "user data must survive the upgrade");
  assert.equal(sde(["verify", "--strict"], legacy).status, 0);

  fs.rmSync(legacy, { recursive: true, force: true });
});

check("the legacy bin/sde.mjs entry point still works", () => {
  const legacyEntry = path.join(consumer, "node_modules", "@echelon-foundry", "sde", "bin", "sde.mjs");
  assert.ok(fs.existsSync(legacyEntry), "bin/sde.mjs must remain for callers that invoke it by path");
  const result = run(process.execPath, [legacyEntry, "--version"], { cwd: project });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout.trim(), PACKAGE_JSON.version);
});

// ---------------------------------------------------------------------------

if (packDir) fs.rmSync(packDir, { recursive: true, force: true });
fs.rmSync(consumer, { recursive: true, force: true });
fs.rmSync(project, { recursive: true, force: true });
fs.rmSync(damaged, { recursive: true, force: true });

console.log(`\n${checks - failures}/${checks} package checks passed.`);
process.exit(failures === 0 ? 0 : 1);
