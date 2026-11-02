# Indy Init rehearsals: Ordo / Echelon evidence matrix (read-only audit, 2026-11-02)

Method: `git log --all -S` / `git grep` across every commit and ref of each clone, plus GitHub MCP (Actions, issues) for 003/004/006/007 only. Pickaxe terms with zero hits in every repo's full history: `ecir recognize`, `sde ecir`, `pattern-recognition/1`, `delegation-contract/1`, `ordo.state-machine`, `decisionRequired`, `ordo.patterns`, `requirements-ledger`, `Sde.Cli`, `./ordo`. The "constructible" hits in R3 and R7 are false positives (F# type-design prose). The `ecir` hits are only the installed `schemas/ecir-approval-v1.schema.json` from the Praxis/ROS bootstrap.

Ordo pattern recognition (`ecir recognize`) already existed in the pinned versions: it was introduced in ordo `c9a0b27` on 2026-10-13, before 1.15.0, and the rehearsals pinned 1.17.0 to 1.19.0. The capability was therefore available in principle, but `.sde/` installs only method docs (20 files, `.sde/MANIFEST.json`), and the CLI is a host tool (`.conditor/lock.json`, `"distribution": "host-tool"`).

## Classification

Two separate questions:
- **A. Ordo pattern recognition** (`ecir recognize`, recognition reports, delegation packets). This is the question asked.
- **B. Any Ordo CLI use.** Recorded for context.

| Rehearsal | A. Pattern recognition | B. Any Ordo CLI | Ordo version | Ordo evidence | Pattern outcomes | Praxis items | Dokimos | Claude telemetry | Actions | Timing | Defects |
|---|---|---|---|---|---|---|---|---|---|---|---|
| R2 | UNKNOWN (conf. high that no recognition artifact or log exists in git; no complete trace) | CONFIRMED_USE, install only (high) | 1.17.0 | `ordo init` and `ordo verify --integrity-only --json` run by Conditor (`evidence/stack/logs/conditor-init.log`); `ordo 1.17.0` qualified (`evidence/stack/compatibility-matrix.json`) | none recorded | 10 queue items; 6 executions; tokens and tool calls "supported-unavailable"/"unknown" | `evidence/quality/dokimos-snapshot.json` (dokimos 0.6.0 structural snapshot) | none; report says "token usage is not exposed to the session" | not retrieved (no API access) | commits 10-23 12:24Z to 10-24 00:26Z; accepted at T+721.2 min | 6 defect classes DC-001..006, 5 recurred; 12 review rounds; 7 candidates |
| R3 | UNKNOWN (conf. high, no artifact; transcripts not committed) | CONFIRMED_USE, install and verify (high) | 1.17.0 | `evidence/preflight/conditor-init.log` (`ordo init`, `ordo verify`); preflight step 11 "`ordo verify` 1.17.0, 19 managed files, passed" (`research/rehearsal-003-stack-preflight.md`) | none recorded | 14 items, all complete; 14 executions; wall time and git metrics only | `dokimos.yml` quality-gate workflow; 58 snapshots on branch `dokimos-evidence` (e5ccfb3) | **yes**: transcript-derived tokens (`evidence/telemetry/tokens-final.json`); no tool-call counts | 345 runs; 6 workflows; no Ordo step | preflight 21.6 min + timed T+127 min (07:32:22 to 09:39:09Z on 10-24); remediation through 10-27 | 22 defects (D-01..07, H1, M1-5, L1-8, N1); `research/rehearsal-003-defect-analysis.md`; NOT ACCEPTED |
| R4 | UNKNOWN (conf. high, no artifact) | CONFIRMED_USE, install only (medium: `.sde` install plus commit text; no log) | 1.17.0 | e0cc043 "conditor init --preset indy-init … conditor verify: exit 0"; `.sde/` present | none recorded | 8 items (1 complete); 4 executions (3 left active) | none found | none; "Token usage is not measurable" (`evidence/remediation/measurements.json` @711c0d9; report) | 421 runs; 7 workflows; no Ordo step | 296 min (06:04:39 to 11:00:31Z on 10-27); remediation to 10-27 20:35Z | 17 (T30) + 14 (T33) + PF-015 + D-HIST-WASM; computed FAILED |
| R5 | UNKNOWN (conf. high, no artifact) | **CONFIRMED_USE** (high) | 1.18.0 | `ordo verification schedule` in `tools/acceptance/ci/acceptance.sh`; output `ordo.verification-schedule/1` committed (`evidence/acceptance/run-40620099844-1b9fb56/verification-schedule.{json,log,exit}`, exit 0); CI step "Install Ordo" | Not a recognition report. Schedule: 108 requirements, 72 dependencies (12 unknown), 0 cycles, 326 tasks, 6 waves | 10 items; 8 executions (7 left active) | `dokimos evidence plan` reuse in `build-and-test.yml` (Dokimos 0.8.0) | none | not retrieved (no API access) | 392 min (20:08:38Z on 10-27 to 02:41:07Z on 10-28) | defect classes DC-001..006 + R3-LESSON-3, all PASS; verdict INCOMPLETE (77/108 P0) |
| R6 | UNKNOWN (conf. high, no artifact) | **CONFIRMED_USE** (high) | 1.19.0 | `evidence/kickoff/conditor-init.txt` (`ordo init`, `ordo verify`); `acceptance/stack/release-freshness.json` ("ordo --version -> 1.19.0; ordo status --json"); CI step 14 "Install Ordo…" succeeded in final run 40803128103; schedule output only in artifact 12867077209 (not retrieved) | none recorded | 14 items; 15 executions; 4 sessions | `install-dokimos.sh`; `dokimos identity` in VER tests | none | 427 runs; 7 workflows; Ordo installed in `build-and-test.yml` and `acceptance-final.yml` | kickoff 20:02:19Z; verdict T+353 (01:55Z on 10-29) | 13 D-items in results; R6-D-002/003 critical open; FAILED |
| R7 | UNKNOWN (conf. medium-high that it was not used; still not proven) | UNKNOWN | none | No Conditor, `.sde` or `.echelon`. Preflight PF-ECH-01 PARTIAL: Ordo "locally unavailable" (`evidence/kickoff/preflight-report.json`) | none | `.praxis/autonomy/policy.json` only; no `.ros` | none | **yes**: tokens **and tool-call counts** (`evidence/telemetry/*.json`) | 88 runs; 5 workflows; no Ordo | T0 22:49:55Z on 10-29; deadline 05:49:55Z; report 05:53Z | 7 build defects (D-01..07) + 30 review findings; 3 FAIL reviews (`evidence/defects.md`) |

### Why no rehearsal is CONFIRMED_NON_USE

No repository holds a complete command trace for its whole run.
- Praxis execution telemetry marks `tool.calls` and `tool.shell_commands` as "supported-unavailable" or "unknown".
- The R3 and R7 token telemetry is derived from Claude Code transcripts, but the transcripts themselves are not committed.
- R7 counts 638 tool calls but does not break them down by command.
- Actions logs cover CI only, not the agent's local shell.

`.claude/settings.json` in R2–R6 allows `Bash(ordo *)`, so the agent could have run Ordo locally without leaving a trace.

## Per-rehearsal source links

R2 (`b053d1da4ef533fba7d6728c369c58a29a1e2440`):
- https://github.com/kemiller2002/indy-init-rehearsal-002/blob/b053d1da4ef533fba7d6728c369c58a29a1e2440/evidence/stack/logs/conditor-init.log
- https://github.com/kemiller2002/indy-init-rehearsal-002/blob/b053d1da4ef533fba7d6728c369c58a29a1e2440/evidence/stack/compatibility-matrix.json
- https://github.com/kemiller2002/indy-init-rehearsal-002/blob/b053d1da4ef533fba7d6728c369c58a29a1e2440/docs/REHEARSAL-002-REPORT.md
- https://github.com/kemiller2002/indy-init-rehearsal-002/blob/b053d1da4ef533fba7d6728c369c58a29a1e2440/evidence/acceptance/run.json
- https://github.com/kemiller2002/indy-init-rehearsal-002/blob/b053d1da4ef533fba7d6728c369c58a29a1e2440/evidence/quality/dokimos-snapshot.json
- https://github.com/kemiller2002/indy-init-rehearsal-002/tree/b053d1da4ef533fba7d6728c369c58a29a1e2440/docs/decisions (DC-001..006)
- https://github.com/kemiller2002/indy-init-rehearsal-002/tree/b053d1da4ef533fba7d6728c369c58a29a1e2440/.ros/telemetry/executions

R3 (`59cf4d215688eca52a0c6b41d791a33cdb89e35b`):
- https://github.com/kemiller2002/indy-init-rehearsal-003/blob/59cf4d215688eca52a0c6b41d791a33cdb89e35b/evidence/preflight/conditor-init.log
- https://github.com/kemiller2002/indy-init-rehearsal-003/blob/59cf4d215688eca52a0c6b41d791a33cdb89e35b/research/rehearsal-003-stack-preflight.md
- https://github.com/kemiller2002/indy-init-rehearsal-003/blob/59cf4d215688eca52a0c6b41d791a33cdb89e35b/evidence/telemetry/tokens-final.json
- https://github.com/kemiller2002/indy-init-rehearsal-003/blob/59cf4d215688eca52a0c6b41d791a33cdb89e35b/docs/REHEARSAL-003-REPORT.md
- https://github.com/kemiller2002/indy-init-rehearsal-003/blob/59cf4d215688eca52a0c6b41d791a33cdb89e35b/research/rehearsal-003-defect-analysis.md
- https://github.com/kemiller2002/indy-init-rehearsal-003/tree/59cf4d215688eca52a0c6b41d791a33cdb89e35b/research/decisions (DF-CAUSA-2026-0001..0003)
- https://github.com/kemiller2002/indy-init-rehearsal-003/tree/e5ccfb3f3d892cf3c50d29449187d384ae546b00/snapshots (Dokimos)

R4 (`78a1b15aade0b882e39d5f985b6719d3493f3c09`; the latest commit is `711c0d991cdf06f0c3cf40bbbc2d564d91de7db0` on `remediation/r4-acceptance`):
- https://github.com/kemiller2002/indy-init-rehearsal-004/blob/78a1b15aade0b882e39d5f985b6719d3493f3c09/docs/rehearsal/REHEARSAL-004-REPORT.md
- https://github.com/kemiller2002/indy-init-rehearsal-004/blob/711c0d991cdf06f0c3cf40bbbc2d564d91de7db0/evidence/remediation/measurements.json
- https://github.com/kemiller2002/indy-init-rehearsal-004/blob/78a1b15aade0b882e39d5f985b6719d3493f3c09/.conditor/lock.json
- https://github.com/kemiller2002/indy-init-rehearsal-004/tree/78a1b15aade0b882e39d5f985b6719d3493f3c09/docs/decisions (R4-DEC-001, R4-REM-OWNER-DECISIONS)

R5 (`b972cb446de543018a65d8726cae6f8283821791`):
- https://github.com/kemiller2002/indy-init-rehearsal-005/blob/b972cb446de543018a65d8726cae6f8283821791/tools/acceptance/ci/acceptance.sh (the `ordo verification schedule` call is around line 77)
- https://github.com/kemiller2002/indy-init-rehearsal-005/blob/b972cb446de543018a65d8726cae6f8283821791/evidence/acceptance/run-40620099844-1b9fb56/verification-schedule.json
- https://github.com/kemiller2002/indy-init-rehearsal-005/blob/b972cb446de543018a65d8726cae6f8283821791/evidence/acceptance/run-40620099844-1b9fb56/verification-schedule.log
- https://github.com/kemiller2002/indy-init-rehearsal-005/blob/b972cb446de543018a65d8726cae6f8283821791/evidence/rehearsal/final.json
- https://github.com/kemiller2002/indy-init-rehearsal-005/blob/b972cb446de543018a65d8726cae6f8283821791/.github/workflows/build-and-test.yml (around line 1160)
- https://github.com/kemiller2002/indy-init-rehearsal-005/tree/b972cb446de543018a65d8726cae6f8283821791/research/decisions

R6 (`41fb6d62ee6bd09bb973d093282f7cd17b09d5cf`):
- https://github.com/kemiller2002/indy-init-rehearsal-006/blob/41fb6d62ee6bd09bb973d093282f7cd17b09d5cf/evidence/kickoff/conditor-init.txt
- https://github.com/kemiller2002/indy-init-rehearsal-006/blob/41fb6d62ee6bd09bb973d093282f7cd17b09d5cf/acceptance/stack/release-freshness.json
- https://github.com/kemiller2002/indy-init-rehearsal-006/blob/41fb6d62ee6bd09bb973d093282f7cd17b09d5cf/.github/workflows/acceptance-final.yml
- https://github.com/kemiller2002/indy-init-rehearsal-006/actions/runs/40803128103 (artifact 12867077209)
- https://github.com/kemiller2002/indy-init-rehearsal-006/tree/41fb6d62ee6bd09bb973d093282f7cd17b09d5cf/research/decisions (DF-CAUSA-2026-0601..0603)
- Results: https://github.com/kemiller2002/Indy-init/blob/main/docs/REHEARSAL-006-RESULTS.md

R7 (`f61493fa04f896977bb099c15870912a6990194d`):
- https://github.com/kemiller2002/indy-init-rehearsal-007/blob/f61493fa04f896977bb099c15870912a6990194d/evidence/kickoff/preflight-report.json
- https://github.com/kemiller2002/indy-init-rehearsal-007/blob/f61493fa04f896977bb099c15870912a6990194d/evidence/telemetry/README.md
- https://github.com/kemiller2002/indy-init-rehearsal-007/blob/f61493fa04f896977bb099c15870912a6990194d/evidence/telemetry/main-agent.json
- https://github.com/kemiller2002/indy-init-rehearsal-007/blob/f61493fa04f896977bb099c15870912a6990194d/evidence/FINAL-REPORT.md
- https://github.com/kemiller2002/indy-init-rehearsal-007/blob/f61493fa04f896977bb099c15870912a6990194d/evidence/defects.md
- https://github.com/kemiller2002/indy-init-rehearsal-007/blob/f61493fa04f896977bb099c15870912a6990194d/evidence/DECISIONS.md (KD-001.., 24 IDs)

## Caveats

- The queue status in `.ros/work/queue.json` is the state at HEAD. Many executions were never finalized (R4: 3 of 4; R5: 7 of 8; R2: 1 of 6), so their wall times are missing.
- R3's `tokens-final.json` ends at the experiment end (09:39:09Z on 10-24). It does not cover the later remediation work items WI-0007..0014.
- R7's "from T0" row runs to 05:52Z, about 2 minutes past the deadline. Subagent rows are listed separately.
- GitHub issues: 0 in 003, 004, 006 and 007 (MCP). Not retrieved for 002 and 005.
