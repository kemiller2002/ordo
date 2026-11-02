# Ordo observability and rehearsal audit (WI-0048)

Date: 2026-11-02. Ordo audited at `1e8a957` (1.19.0, `main`); implementation
on branch `claude/ordo-decision-events`. Specula change on
`kemiller2002/specula` branch `claude/ordo-observability`.

**Verdict.** No Indy Init rehearsal (2–7) recorded a single Ordo pattern
recognition, and none recorded enough of its commands to prove one did not
happen. Ordo pattern use is therefore **Unknown for every rehearsal**. It
is not zero. The rehearsals did use other Ordo commands (R2–R6;
`ordo verification schedule` in R5 CI). There is **no data to compare
matched with unmatched decisions**, so this audit makes no efficiency or
token-saving claim. Ordo now records each pattern evaluation as an
identifiable event, can correlate it with reported execution and Dokimos
verification, and Specula displays it without filling gaps. The acceptance
criterion is met on fixture data. It has not yet been exercised in a
rehearsal.

## 1. Implementation inventory (Phase 1)

| Capability | Status | Where | Tests |
|---|---|---|---|
| Pattern registry | **3 patterns**, registry `ordo.patterns/1`, schema `ordo.pattern-registry/1`; families: `state.machine-guarded-transition` (`ordo.state-machine@1`), `integration.external-adapter` (`ordo.transition-effect-port@1`, `ordo.current-time-port@1`) | `src/Ordo.Core/PatternRegistry.fs` (`BuiltIn.definitions`) | `tests/Ordo.Construction.Tests/PatternRecognitionTests.fs` |
| Matching / selection | Deterministic: every pinned pattern's pure `Claim` runs on structured, approved facts (no prose, no model). Outcomes `Constructible`, `DecisionRequired` (incl. `PatternSelectionRequired` = ambiguous), `Delegate` (agent packet), `Blocked`. Pins bind contract digests | `src/Ordo.Core/PatternRecognition.fs`, `PatternRecognitionWire.fs` | same, 40+ cases (order independence, ambiguity, pins, refusals) |
| Agent interface | `sde ecir recognize --manifest --blueprint --cohort [--decisions --approvals --pin --packets DIR]` → `ordo.pattern-recognition/1` report + `ordo.delegation-contract/1` packets; completion schema `ordo.delegation-completion/1` | `src/Sde.Cli/Ecir.fs`, `Args.fs` | `tests/Sde.Core.Tests` |
| Logging / tracing / telemetry (before) | **None for recognition.** The report went to stdout only. `Ordo.Decisions.Observation` covers model-backed Decide resolutions, not patterns. `telemetry/metrics.json` defines Praxis metric ids only | `src/Ordo.Decisions/Observation.fs` | `tests/Ordo.Tests` |
| Decision / execution receipts | Typed expected vs observed receipts (`Match`/`Mismatch`/`Indeterminate`); delegation completion validation | `src/Ordo.Core/Receipt.fs`, `PatternRecognition.fs` (`validateCompletion`) | `ExecutionContractTests.fs`, `PatternRecognitionTests.fs` |
| Praxis correlation | Praxis ingests executable-Ordo `ResolutionObservation`s (not recognition reports); exposes `PRAXIS_WORK_ITEM`, `ROS_TELEMETRY_RUN_ID/SESSION_ID` | praxis `src/Praxis.Domain/Ordo/Model.fs`, `src/Praxis.Application/Ordo/Operations.fs` | — (not changed) |
| Dokimos correlation | `dokimos ecir recognition-audit` independently re-derives a recognition report (`dokimos.recognition-audit/1`); `dokimos completion verify` → `dokimos.completion-verification` (verdict `verified`/`rejected`) | dokimos `src/Dokimos.Core/RecognitionAudit.fs`, `CompletionWire.fs` | — (not changed) |
| Enforcement | **Advisory.** Recognition grants nothing ("Execution authorization not granted"); nothing requires an agent to run it, and rehearsal kickoffs did not. Pins are enforced (a changed contract is refused) | `Ecir.fs` output, `PatternRegistry.Registry.select` | pin tests |

Added in this work (no duplicate of the above):

- `ordo.decision-event/1` — `src/Ordo.Core/DecisionEvents.fs` (pure), JSONL sink `src/Sde.Cli/DecisionLog.fs`, `sde events` (`src/Sde.Cli/Events.fs`), spec [`docs/architecture/ordo-decision-events.md`](../architecture/ordo-decision-events.md).
- One additive helper: `Ordo.Core.Json.renderCompact`.

## 2. Rehearsal 2–7 evidence matrix (Phase 2)

Full matrix with per-file links and caveats:
[`rehearsal-evidence-matrix.md`](rehearsal-evidence-matrix.md). The extracted
numbers are in [`rehearsal-metrics.json`](rehearsal-metrics.json), with `null`
plus a reason where a value is unavailable.

| Rehearsal @ HEAD | Pattern recognition | Other Ordo use | Why not "confirmed non-use" |
|---|---|---|---|
| R2 @ `b053d1d` | Unknown | Confirmed: `ordo init`, `ordo verify` (1.17.0) | tool calls "supported-unavailable"; no Actions access |
| R3 @ `59cf4d2` | Unknown | Confirmed: init / verify (1.17.0) | tokens recorded, transcripts not committed |
| R4 @ `78a1b15` | Unknown | Confirmed: install (1.17.0), medium confidence | no command trace |
| R5 @ `b972cb4` | Unknown | Confirmed: `ordo verification schedule` in CI (1.18.0) | local shell not traced |
| R6 @ `41fb6d6` | Unknown | Confirmed: init, status, CI install (1.19.0) | local shell not traced |
| R7 @ `f61493f` | Unknown (medium-high that it was unused) | Unknown; preflight records Ordo "locally unavailable" | 638 tool calls, not broken down by command |

Method: `git log --all -S` over every ref for `ecir recognize`, `sde ecir`,
`pattern-recognition/1`, `delegation-contract/1`, `ordo.state-machine`,
`ordo.patterns`, `requirements-ledger` and related terms. There were **0 hits
in all six**; this was independently re-checked for `ecir recognize` and
`pattern-recognition/1`. Recognition was available to every rehearsal: it
shipped in `c9a0b27` (2026-10-13), and the rehearsals pinned 1.17.0–1.19.0.
R2–R6 allow `Bash(ordo *)`, so an unlogged local run cannot be excluded.

## 3. Implementation and tests (Phases 3–4)

| Required case | Test |
|---|---|
| successful match | `DecisionEventsTests`: *a successful match is an identifiable evaluation and a separate pattern-selected selection* |
| unmatched + fallback to agent | *an unmatched requirement falls back to agent reasoning through its packet* |
| ambiguous | *an ambiguous match is recorded as ambiguous and escalated, never resolved* |
| rejected | *a rejected decision records blocker kinds and no reason text* |
| executed, then fails verification | *an executed pattern that then fails verification is counted as failed, not inferred to pass*; CLI: *execution and a failing Dokimos completion receipt are recorded as separate correlated events* |
| missing / corrupted telemetry | *corrupt and truncated lines are reported by line number…*, *an empty log has no match rate…*, *summarizing a missing log reports it unavailable…* |
| concurrent recording | *concurrent writers never interleave or lose a line* (16 × 25) |
| telemetry cannot be written | *recognition succeeds with identical output when the event log cannot be written* |
| existing behaviour preserved | *a real recognition with --events produces … an unchanged report*; *recognition without --events writes nothing*; full suites unchanged |
| no sensitive content | *no prose from the inputs reaches the wire*; *identifiers that look like credentials or prose are refused* |

Results on this branch (local, .NET SDK 10.0.403 building net8.0):
Ordo.Tests 575 passed, 1 skipped (pre-existing); Ordo.Construction.Tests
129 passed (13 new); Sde.Core.Tests 165 passed (7 new). Specula: engine
73 passed (7 new); Chromium browser suite 46/46 (4 new: two Ordo
acceptance tests, plus layout at the iPhone widths of 320 and 390 px). The iPhone WebKit suite (`npm run test:mobile`) **was not run
locally** because WebKit is not installed in this container. Specula CI
installs WebKit and runs it.

Acceptance on real Ordo output:
[`acceptance/`](acceptance/README.md) (fixture cohort, labeled as such).

## 4. Efficiency analysis (Phase 6)

**No comparison is possible, and none is made.** A matched-vs-unmatched
comparison needs decisions classified by Ordo in the same run, with
per-decision time, tool calls, tokens, rework, verification and defects.
No rehearsal has any recognized decision. Token data exists only for R3
(transcript-derived, timed phase 116,804,825 total tokens) and R7 (607,478
output tokens, 638 tool calls). Neither ran recognition, their scope and
rules differ, and neither has a per-decision breakdown. Elapsed times (R2
721.2 min to acceptance, R3 127 min timed, R4 296, R5 392, R6 T+353, R7 a
420-min window) describe whole rehearsals with different scopes and
outcomes. They cannot be attributed to Ordo.

The only prior Ordo-specific efficiency evidence is CONSTRUCT-008, cited in
[`pattern-registry.md`](../architecture/pattern-registry.md). It found
mechanical-first construction cost 8–34% *more* than agent coding where the
vocabulary covered few requirements (6 of 46 on the expanded time-entry
slice, 4 of 18 on Chrona). That is an association on two slices, not a
causal estimate. It argues against claiming savings without coverage.

## 5. Ranked pattern-coverage gaps

1. **Adoption: recognition never ran in a rehearsal.** No rehearsal built an
   ECIR blueprint for its app, so the registry was never consulted. Every
   other gap is unmeasurable until this changes.
2. **Observability: no command-level trace.** Fixed for recognition by this
   work, but only when `--events` / `ORDO_DECISION_EVENTS` is set and the log
   is committed.
3. **Vocabulary breadth: 3 patterns in 2 families.** On the timer fixture,
   3 of 6 requirements matched with approved records and 0 of 6 without them
   (acceptance run). CONSTRUCT-008 measured 6/46 and 4/18 coverage. Nothing
   covers UI, persistence, HTTP APIs or validation, which make up most of an
   Indy Init app.
4. **Decision gaps dominate partial matches.** `effectFailureUndecided` and
   `decisionNotAuthorized` turn claims into escalations. The approvals and
   decision records must exist before recognition can construct anything.
5. **Execution and verification are opt-in reports.** Nothing yet calls
   `sde events record-execution` from a delegation completion, or
   `record-verification` from a Dokimos run.

## 6. Recommended before Indy Init (smallest first)

1. In the Indy Init kickoff, export
   `ORDO_DECISION_EVENTS=evidence/ordo/decision-events.jsonl` and commit that
   file. Specula then shows it with no further change. This is cheap and
   additive.
2. Wherever an agent receives a delegation packet, have it run
   `sde events record-execution --packet <id> --status …` when it finishes.
   In the Dokimos CI step, add
   `sde events record-verification --packet <id> --dokimos-receipt <file>`.
3. Commit Claude Code usage per work item (tokens and tool calls) with
   `PRAXIS_WORK_ITEM` set, so a later comparison has a denominator.
4. Do **not** present Ordo as saving tokens or time. The evidence does not
   support it, and CONSTRUCT-008 points the other way at low coverage.

## 7. Judge-ready explanation

Ordo is the methodology and the deterministic construction tools behind the
Echelon stack. Its pattern recognition classifies each requirement as
constructible by a verified pattern, as needing a human decision, or as
delegated to an agent through a bounded packet. It never guesses, and it
never authorizes execution. We audited six rehearsals and found Ordo
installed and verified in five, and used for verification scheduling in
CI. We found no record of pattern recognition in any of them. Without a
complete trace, we report that as unknown, not as unused. We then made
every recognition produce an append-only, secret-free event: what matched,
what was chosen, what the agent later reported, and what an independent
verifier concluded. Each is kept separate and joined by stable ids. Specula
shows these counts read-only and labels everything it cannot know.
Verified numbers: 3 registered patterns; 6 rehearsals audited; 0 recorded
recognitions; on the fixture acceptance run, 12 evaluated decisions, 3
matched, 4 handed to an agent, 1 reported execution, and 1 independent
verdict (failed, from a hand-written receipt).

## Reproduce

```sh
# Ordo (branch claude/ordo-decision-events)
dotnet test tests/Ordo.Tests/Ordo.Tests.fsproj
dotnet test tests/Ordo.Construction.Tests/Ordo.Construction.Tests.fsproj
(cd distribution && npm test)
scripts/ordo-events-acceptance.sh /tmp/ordo-events
# Specula (branch claude/ordo-observability)
npm ci && npm run test:unit && npm run build && npx playwright test && npm run test:mobile
# Rehearsal check (any clone)
git log --all --oneline -S'ecir recognize' | wc -l     # 0 in R2–R7
```

## Unresolved

- Actions logs for R2 and R5 were not retrieved (no API access in this session).
- R6 artifact 12867077209 (schedule output) was not downloaded.
- Recognition has not run on Indy Init requirements, so real coverage on the target app is unknown.
- iPhone WebKit tests not run locally (see §3).
