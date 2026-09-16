#!/usr/bin/env node
// LEGACY COMPATIBILITY ENTRY POINT.
//
// Releases 1.0.0 through 1.1.1 exposed this path as the package's bin
// target, and scripts in consuming repositories and in this repository's own
// CI invoke it directly as `node .../bin/sde.mjs <command>`. The package's
// bin mapping now points at bin/sde.js, but this file is kept so that those
// invocations keep working unchanged.
//
// It adds no behaviour of its own: it runs the current launcher, which runs
// the F# CLI. New callers should use `npx @echelon-foundry/sde <command>` or
// the `sde` executable the package installs.
import { createRequire } from "node:module";

createRequire(import.meta.url)("./sde.js");
