# Boundary amplification assessment

Status: implemented (detection), 2026-10-05. Decision: `DF-SDE-2026-0016`.
Work item: GH-52. Method: ORDO-QUAL-001 and ORDO-QUAL-008.

This assessment makes implementation scope that crosses architectural
boundaries visible *before* the crossing becomes accumulated debt. It
compares what a work item declared with what its change touched, and
reports evidence. It does not split work because the work is large, and it
does not gate. The consumer decides what to do with a High result.

## Placement

| Part | Where | Responsibility |
|---|---|---|
| `Ordo.Core.BoundaryAmplification` | `src/Ordo.Core/BoundaryAmplification.fs` | Pure domain: classification, signals, risk, recommendation, approvals. Reuses `MutationBoundary` (glob dialect, write set, `ScopeExpansion`, `expand`, `scopeEffects`). |
| `Ordo.Core.BoundaryAmplificationWire` | `src/Ordo.Core/BoundaryAmplificationWire.fs` | Versioned input decoders and the output contract. Strict: a wrong schema, malformed element or unknown policy member fails the document. |
| `boundary assess` | `src/Sde.Cli/Boundary.fs` | Adapter: reads files, optionally extracts imports (`--root`), renders JSON or text, maps outcomes to exit codes. |

Ordo.Core still references no Sde/ROS assembly and performs no I/O. The CLI
references Ordo.Core, so the dependency points inward only.

## Inputs

All four are repository-owned. Every document carries a `schema` member
naming its contract and version.

**Boundary map** (`ordo.boundary-map/1`, conventionally `.ordo/boundaries.json`):

```json
{
  "schema": "ordo.boundary-map/1",
  "boundaries": [
    { "id": "build", "layer": "build", "patterns": ["**/*.fsproj"] },
    { "id": "tests", "layer": "tests", "patterns": ["tests/**"] },
    { "id": "cli", "layer": "cli", "patterns": ["src/App.Cli/**"] },
    { "id": "domain", "layer": "domain", "patterns": ["src/App.Domain/**"] }
  ],
  "indicators": [
    { "boundary": "provider", "imports": ["System.Net.Http*"] }
  ],
  "orchestration": ["src/App.Cli/*Commands.fs"]
}
```

- The first boundary whose glob matches a path classifies it, so put the
  specific globs before the general ones.
- Layers are `cli`, `application`, `domain`, `persistence`, `provider`,
  `rendering`, `infrastructure`, `tests`, `documentation` and `build`. Any
  other name is a custom layer.
- `tests`, `documentation` and `build` are *supporting* layers. They are
  observed and reported, but they are not architectural crossings.
- *Indicators* map imported namespaces to the boundary whose responsibility
  they represent. A name matches exactly unless the pattern ends in `*`.
- Indicators should name **external effects** (HTTP, filesystem, processes,
  locking). A CLI opening its own domain is using that layer, not hosting
  it.
- `orchestration` globs name the files where accumulated responsibility is
  most expensive. They matter to signals ORDO-BA-005 and ORDO-BA-006.

**Expectation** (`ordo.boundary-expectation/1`), declared per work item:

```json
{
  "schema": "ordo.boundary-expectation/1",
  "workItem": "PRX-123",
  "requirement": "optional one-line statement",
  "expectedBoundaries": ["domain", "cli"],
  "writeSet": {
    "scopes": ["feature:pacing"],
    "projections": [{ "scope": "feature:pacing", "patterns": ["src/App.Domain/Pacing/**"] }],
    "evaluatorReferences": ["tests/Acceptance/**"]
  },
  "approvedExpansions": [
    {
      "expansionId": "EXP-1",
      "addedBoundaries": ["persistence"],
      "justification": "why the crossing is right",
      "authorizedBy": "who approved it",
      "addedScopes": [],
      "addedProjections": []
    }
  ]
}
```

- `writeSet` is optional. It is a `MutationBoundary`.
- `approvedExpansions` reuse `ScopeExpansion` and go through
  `MutationBoundary.expand`. An expansion is rejected, and the rejection is
  reported, when it:
  - has no justification;
  - has no authorizer;
  - would make evaluator authority writable;
  - names a boundary the map does not declare.

**Changed files** (`ordo.changed-files/1`). Plain text with one path per
line is also accepted, for example the output of
`git diff --name-only base...HEAD`:

```json
{
  "schema": "ordo.changed-files/1",
  "workItem": "PRX-123",
  "files": [
    "src/App.Domain/Model.fs",
    { "path": "src/App.Cli/RunCommands.fs", "changeCount": 6, "imports": ["System.IO"] }
  ],
  "history": [{ "workItem": "PRX-100", "paths": ["src/App.Cli/RunCommands.fs"] }]
}
```

`changeCount` is how many times the file was modified within this change,
for example the number of commits. `history` lists prior work items and the
paths they touched. When `imports` is absent and `--root DIR` is given, the
CLI reads F# `open` and C# `using` lines from the file under `DIR`.

**Policy** (`ordo.boundary-policy/1`) is optional. These are the defaults:

```json
{
  "schema": "ordo.boundary-policy/1",
  "maxBoundaries": 3, "maxModules": 4, "moduleDepth": 2,
  "coreLayers": ["cli", "persistence", "provider", "domain", "rendering"],
  "coreLayerSpan": 3, "hotspotWorkItems": 3, "orchestrationChurn": 3,
  "concentrationBoundaries": 2, "maxExpansionRatio": 2.0,
  "elevatedScore": 2, "highScore": 4
}
```

Unknown members are refused, so a misspelt threshold cannot silently fall
back to its default.

## Signal catalog

| ID | Name | Fires when | Strength |
|---|---|---|---|
| ORDO-BA-001 | unexpected-boundary-crossing | an observed architectural boundary (by path or import) is not in `expectedBoundaries` | strong |
| ORDO-BA-002 | boundary-breadth | distinct architectural boundaries observed > `maxBoundaries` | weak |
| ORDO-BA-003 | module-scatter | distinct production modules (first `moduleDepth` directories) > `maxModules` | weak |
| ORDO-BA-004 | core-layer-span | observed layers include ≥ `coreLayerSpan` of `coreLayers` | strong |
| ORDO-BA-005 | hotspot-recurrence | a production file was changed by ≥ `hotspotWorkItems` distinct work items, the current one included | strong for an orchestration file, weak otherwise |
| ORDO-BA-006 | orchestration-churn | an orchestration file's `changeCount` ≥ `orchestrationChurn` | strong |
| ORDO-BA-007 | responsibility-concentration | one file's imports indicate ≥ `concentrationBoundaries` boundaries other than its own | strong |
| ORDO-BA-008 | scope-expansion-ratio | observed ÷ expected architectural boundaries > `maxExpansionRatio` (unknown, so it does not fire, when nothing architectural was expected) | weak |
| ORDO-BA-009 | write-set-escape | a changed file is outside the declared `writeSet` after approved expansions (`MutationBoundary.scopeEffects`) | strong |
| ORDO-BA-010 | unclassified-paths | some changed path matches no boundary | informational (weight 0) |

Supporting files are reported but never count towards ORDO-BA-002..008.
None of the signals is a file or line count.

## Risk, recommendation and approval

- **Score.** strong = 2, weak = 1, informational = 0.
- **Risk.** `high` when the score ≥ `highScore`, `elevated` when it is
  ≥ `elevatedScore`, otherwise `low`. `risk.evidence` lists the
  contributing signal ids.
- **Recommendation.**
  - `low` → `no-action`.
  - `elevated` → `consider-split-along`.
  - `high` → `require-design-review`.
  - The split candidates are the unexpected crossings plus any boundaries a
    concentrated file hosts. When there are none, they are the observed
    boundaries.
- **Approval.** When applied expansions cover *every* unexpected crossing
  and no write-set escape remains, `exception.coverage` is `full` and the
  recommendation goes down one step. `exception.downgradedFrom` records the
  original recommendation. The risk, the signals and the crossings are
  unchanged, so an approval is visible and never silent.
- **Determinism.** Identical inputs produce byte-identical output whatever
  the input order. All lists are sorted ordinally, and a test asserts this.

## CLI

```
ordo boundary assess --map FILE --expected FILE --changed FILE [--policy FILE] [--root DIR] [--json]
```

The command is `sde boundary assess` in the npm distribution. Exit codes:

| Code | Meaning |
|---|---|
| 0 | Assessed, whatever the risk. A High result is a successful assessment. |
| 2 | Arguments not understood. |
| 6 | An input document is unreadable, malformed or inconsistent, for example it names an undeclared boundary or the work items do not match. |

With `--json`, stdout carries exactly one `ordo.boundary-amplification/1`
document on exit 0. On exit 6 it carries the CLI's common error document
(`outcome: "error"`, `message`, `exitCode`). The schema is
`schemas/ordo-boundary-amplification.v1.schema.json`.

### Consuming it from Praxis

```
git diff --name-only "$BASE"...HEAD > "$TMP/changed.txt"
ordo boundary assess \
  --map .ordo/boundaries.json \
  --expected "$TMP/expectation.json" \
  --changed "$TMP/changed.txt" \
  --root . --json > "$TMP/boundary.json"
```

Praxis owns the gating decision. A reasonable start is:

- branch on `risk.level`;
- surface `signals[].description`;
- require a recorded approval (an `approvedExpansions` entry) when
  `recommendation.kind` is `require-design-review` and
  `exception.coverage` is not `full`.

## Fixtures

`tests/fixtures/boundary-amplification/` holds two fixtures that use the
same map:

- `praxis-usage-pacing` characterizes the incident that motivated this work
  and yields `high` and `require-design-review`.
- `approved-cross-cutting` shows a visible-but-approved cross-cutting
  change.

## Known limits

- Only paths and imports are observed. An effect reached through an
  abstraction, or without an import, is invisible to this assessment.
- BCA as boundary files per semantic decision (`doctrine/GLOSSARY.md`) is
  not computed, because there is no semantic-decision input yet.
- The GH-52 exception shape is not modelled. Approvals carry only a
  justification and an authorizer, with no owner, prohibited growth, expiry
  or revisit trigger.
- The default thresholds have not been calibrated across repositories.
