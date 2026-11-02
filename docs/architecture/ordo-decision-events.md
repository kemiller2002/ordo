# Ordo decision events (ORDO-OBS-001)

Status: released in Ordo 1.20.0 (tag `v1.20.0`). Work item WI-0048.
Code: `src/Ordo.Core/DecisionEvents.fs` (pure), `src/Sde.Cli/DecisionLog.fs`
(append-only writer), `src/Sde.Cli/Events.fs` (`sde events`).
Tests: `tests/Ordo.Construction.Tests/DecisionEventsTests.fs`,
`tests/Sde.Core.Tests/DecisionEventCommandTests.fs`.

## Why

Before this, a pattern recognition (`sde ecir recognize`) left no record unless
its caller saved stdout. The Rehearsal 2–7 audit
([`docs/observability/rehearsal-evidence-matrix.md`](../observability/rehearsal-evidence-matrix.md))
could therefore neither confirm nor rule out that recognition was ever used.
Ordo had no logging or tracing to reuse: `Ordo.Decisions.Observation`
covers model-backed `Decide` resolutions, not pattern recognition, and
nothing in `src/` wrote a log. This adds the smallest durable record that
can answer "was a pattern evaluated, what happened, and was it verified".

## Format

One compact JSON object per line (JSONL), schema `ordo.decision-event/1`,
appended to a file the caller names. There is no service and no database.

```json
{"schemaVersion":"ordo.decision-event/1","eventId":"ode-…","kind":"pattern.evaluated",
 "occurredAt":"2026-11-02T11:53:31.214Z","ordoVersion":"1.19.0","evidenceSource":"ordo.recognize",
 "correlation":{"recognitionId":"orr-…","runId":"…","sessionId":"…","workItemId":"WI-0048",
   "cohortId":"COH-TIMER","requirementKey":"…","requirementId":"…","decisionId":"odc-…","packetId":"…"},
 "data":{"matchStatus":"matched","patterns":[{"id":"ordo.state-machine","version":1,
   "family":"state.machine-guarded-transition","claim":"constructs"}],"gapKinds":[],"blockerKinds":[]}}
```

| kind | written by | what it records |
|---|---|---|
| `recognition.run` | `ecir recognize` | registry version, input digests, the report's counts, measured duration (ms) |
| `pattern.evaluated` | `ecir recognize` | per requirement: `matchStatus` and the claiming patterns (id, version, family, claim kind), gap and blocker *kinds* |
| `pattern.selection` | `ecir recognize` | per requirement: `disposition` and the selected / generating patterns |
| `agent.execution` | `sde events record-execution` | what an executor *reports* it did: `succeeded`, `failed` or `abandoned`, duration, executor id |
| `verification.result` | `sde events record-verification` | an independent verdict: `passed`, `failed` or `indeterminate`, verifier, scope, receipt digest |

`matchStatus` is derived from the recognition outcome and its claims:
`matched` (constructible by its claimed patterns), `partial` (a pattern
generated part, an agent or a decision owns the rest), `unmatched` (no
pattern claimed it), `ambiguous` (`PatternSelectionRequired`), `rejected`
(`Blocked`). `disposition` is `pattern-selected` (Constructible),
`fallback-agent` (Delegate, with the delegation packet id), `escalated`
(DecisionRequired) or `rejected` (Blocked).

## What is never inferred

- A selection is not an execution. Ordo never writes `agent.execution`;
  only an executor that ran the work can, by calling `record-execution`.
- An execution is not a verification. `record-verification` takes a typed
  verdict or reads a Dokimos receipt (`dokimos.completion-verification`
  maps `verified`→`passed`, `rejected`→`failed`;
  `dokimos.recognition-audit/1` is emitted by Dokimos only on a clean audit).
  An unknown Dokimos contract is refused, never guessed.
- Missing is unknown. `sde events summarize` reports `executionUnknown`
  (selected or delegated, no execution reported), `executedButUnverified`,
  `uncorrelated` (an execution or verdict naming no known decision),
  `incompleteRuns` (fewer evaluation events than the run says it evaluated),
  `corruptLines` and `duplicateEvents`. `matchRate` is `null`, not `0`, when
  nothing was evaluated. A missing log file is exit 3 "unavailable", never an
  empty summary.

## Correlation

- `decisionId` (`odc-…`) is the verification correlation id: SHA-256 over
  the manifest, blueprint and decision-record digests, the registry version,
  the cohort and the requirement key. Equal inputs give an equal id, so a
  verifier holding the same inputs can derive it.
- `packetId` is the `ordo.delegation-contract/1` packet id the agent already
  receives, so an agent can report against the id it holds
  (`--packet`).
- `workItemId` joins to Praxis. It comes from `ORDO_WORK_ITEM`, then
  `PRAXIS_WORK_ITEM`; `runId` from `ORDO_RUN_ID`, `PRAXIS_TELEMETRY_RUN_ID`,
  `ROS_TELEMETRY_RUN_ID`, `GITHUB_RUN_ID`; `sessionId` from
  `ORDO_SESSION_ID`, `PRAXIS_TELEMETRY_SESSION_ID`,
  `ROS_TELEMETRY_SESSION_ID`, `CLAUDE_CODE_SESSION_ID`. An absent identifier
  is written as `null`.
- `eventId` (`ode-…`) is unique per invocation; `recognitionId` (`orr-…`)
  groups one recognition's events.

## Privacy

Every field is an identifier, enumeration, digest, count or duration.
Requirement prose, decision questions, blocker reasons, prompts and source
content are never written; tests assert that the recognition's own
violation and question text do not appear on the wire. Caller-supplied and
ambient identifiers must match `[A-Za-z0-9][A-Za-z0-9._:/@#+-]{0,159}`, and
values shaped like credentials (`sk-…`, `ghp_…`, `github_pat_…`, JWTs,
`password=`…) are refused (ambient ones are dropped).

## Failure behaviour

- Off by default. `--events FILE` or `ORDO_DECISION_EVENTS` turns it on.
  Without either, `ecir recognize` writes nothing new.
- Recognition output (stdout, exit code) is byte-identical with or without
  events (test: *a real recognition with --events produces identifiable
  events and an unchanged report*).
- If the log cannot be written, recognition still succeeds and prints one
  `warning: Ordo decision events not recorded …` line on stderr.
- Appends take an exclusive file lock and write whole lines; lock contention
  is retried with bounded backoff (test: 16 concurrent writers × 25 events,
  no lost or interleaved line).
- `record-execution` and `record-verification` are explicit acts, so they do
  fail (exit 1) when the event is refused or cannot be written.

## Verification

```sh
dotnet test tests/Ordo.Construction.Tests/Ordo.Construction.Tests.fsproj --filter FullyQualifiedName~DecisionEvents
(cd distribution && npm test)        # builds the payload, then Sde.Core.Tests incl. DecisionEventCommandTests
dotnet test tests/Ordo.Tests/Ordo.Tests.fsproj   # ArchitectureTests: Ordo.Core gained no dependency
scripts/ordo-events-acceptance.sh /tmp/ordo-events
```
