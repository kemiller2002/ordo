---
id: EX-SDE-2026-0001
title: Agent token and monetary cost under mechanical boundary hardening, with uninterrupted per-condition telemetry
research_area: sde
status: proposed
created: 2026-09-17
author_agent: claude-code
tests_hypotheses: [HY-SDE-2026-0009]
related_theories: [TH-SDE-2026-0003, TH-SDE-2026-0004, TH-SDE-2026-0005]
inputs:
  - EV-HN-2026-0005
  - doctrine/CONTRADICTIONS-AND-DEPRECATED.md
  - kemiller2002/helix-note-application@experiment/agent-cost-baseline-v3 (76d6c25)
  - kemiller2002/helix-note-application@experiment/agent-cost-hardened-v3 (58fcbc1)
  - kemiller2002/helix-note-application@experiment/agent-cost-comparison-v3 (8ac05fd)
outputs:
  - research/evidence/EV-SDE-2026-00NN (on completion)
---

# Experiment

## Research question

Does mechanical boundary hardening reduce the token and monetary cost an
engineering agent incurs to implement an identical, frozen mission?

This is currently **Open**, not merely unmeasured. Experiment 3 produced
figures that appear to answer it and are explicitly disqualified from doing
so. This experiment exists to answer it or to fail to, under conditions that
make either outcome reportable.

## Why this experiment exists

Experiment 3 [EV-HN-2026-0005] ran the correct design and lost the
measurement. Its Condition B telemetry (79 tool uses, 165,288 tokens, 15.01
minutes) covers only a resumed partial run: an infrastructure rate limit
interrupted the condition, and roughly **78% of Condition B's eventual log
already existed before the counted portion began**. Condition A's figures
(195 tool uses, 516,665 tokens, 36.43 minutes) are complete.

The repository's own doctrine records the consequence
(`doctrine/CONTRADICTIONS-AND-DEPRECATED.md`):

> **Status:** Unsupported (Open, not Contradicted — the true figure is
> unknown, not known to be false). … The true total could plausibly match or
> exceed Condition A's. Never cite the raw percentage reductions
> (59%/68%/59%) as a confirmed cost reduction.

Nothing since has replaced it. Every other cost figure in the evidence base
is either `NOT OBSERVABLE`, or a cumulative session total that cannot be
attributed to one task (`M-GF1-COST`, `$19.04`, covering two tasks
mid-session; the HelixNote metrics log's `$759.19`, covering an entire
multi-week session). The HelixNote log states the reason for refusing to
prorate it, and this experiment adopts that standard:

> attempting to prorate it by wall-clock time or message count would be a
> fabricated number dressed as a measured one

## Hypotheses tested

**HY-SDE-2026-0009 (to be registered alongside this experiment):** An agent
implementing an identical frozen mission against a mechanically hardened
boundary architecture incurs lower monetary cost than against the
unhardened baseline architecture.

Directional, and pre-registered as falsifiable in both directions. A null or
reversed result is a publishable outcome of this experiment, not a failed
run. TH-SDE-2026-0003 already establishes that a *higher* mechanical
discovery rate can coexist with lower search cost; nothing rules out a
hardened architecture costing more to work in.

## Variables

| Role | Variable | Operationalisation |
|---|---|---|
| Independent | Boundary architecture | Condition A starts at **4879537**, Condition B at **8d2d789** — each branch's "freeze Experiment 3 mission" commit, which is the state *before* that condition implemented anything. The branch tips (76d6c25, 58fcbc1) are the finished trials and are the answer key, not the starting point. Unmodified. |
| Dependent (primary) | Monetary cost | `external_metadata.usage.cost_usd` for the condition's own session |
| Dependent (secondary) | Token cost | `input_tokens`, `output_tokens`, `cache_read_tokens`, `cache_write_tokens`, reported **separately, never summed** |
| Dependent (secondary) | Wall clock | Session `created_at` to completion timestamp |
| Covariate | Builds executed | Count, beyond the fixed verification sequence |
| Covariate | Required Change Sites | Normalised count from the committed diff |
| Held constant | Mission text, model, effort level, permission mode, tool surface, verification sequence | Identical strings, recorded verbatim in the run manifest |

Token classes are reported separately because they are not interchangeable:
in the one comparable figure this evidence base already holds
(`M-GF1-TOKENS`), 58.8M of 60.3M tokens were cache reads, which differ from
fresh input tokens in both price and meaning. A single summed "tokens"
number would be the fabricated-figure failure this experiment is designed
to avoid.

## Method

Paired A/B controlled trial, **≥3 independent runs per condition**, each run
executed by a freshly spawned agent in its **own isolated session and
container**, with no access to the other condition's work, to this research
programme's prior reports, or to Experiment 3's findings.

### The measurement change that makes this experiment worth running

Experiment 3 measured cost with harness-level telemetry captured by the
*orchestrating* session. That figure is cumulative across everything the
orchestrator did, which is why an interruption inside one condition
contaminated it beyond repair.

Here, **each condition run is a separate Claude Code Remote session**, and
its cost is read from that session's own
`get_session().external_metadata.usage`. Verified available and per-session:
the field returns `cost_usd`, `input_tokens`, `output_tokens`,
`cache_read_tokens` and `cache_write_tokens` scoped to one session. A figure
read this way structurally cannot mix conditions, because the two conditions
never share a session.

This does not make the figure immune to interruption — it makes interruption
**detectable and disqualifying** rather than invisible, which is precisely
what Experiment 3 lacked.

## Acceptance criteria

A run counts only if all hold:

1. The session completed the mission without a rate-limit interruption or
   resume. Checked against the session's own event record, not the agent's
   self-report.
2. A usage reading was taken at run start and at run close, and the session
   performed no work outside that window.
3. The fixed verification sequence was executed exactly once, in full.
4. All acceptance criteria of the frozen mission were met, verified by the
   orchestrator against the committed diff, not accepted on self-report.

A run failing any of these is **VOID and re-run. Its figures are not
reported, not averaged in, and not cited.** This rule is pre-registered here
specifically because Experiment 3's disqualifying figures circulated before
the disqualification did.

## Falsification criteria

Pre-registered before any run:

- **Not supported** if the distribution of per-run `cost_usd` differences
  spans zero, or if the direction is inconsistent across runs.
- **Contradicted** if the hardened condition costs consistently *more*
  across all runs.
- **Supported, scope-limited** only if every run shows the same direction
  and the separation exceeds the within-condition spread.

No effect size will be claimed from three runs. The output is a direction
and a spread, on one codebase and one mutation.

## Controls

Each control below answers a specific, named defect in Experiment 3 rather
than being generic hygiene.

| Experiment 3 defect | Control |
|---|---|
| Condition B interrupted by a rate limit and resumed; telemetry covers ~22% of the run | Per-session telemetry; interruption makes a run VOID, not adjusted. Runs spaced so the account's rate-limit window has headroom, and the window state is recorded before each run starts. |
| Condition B ran second in a **shared container**, possibly on warm NuGet/JIT caches left by Condition A | Every run gets its own fresh container. Run order counterbalanced across the ≥3 pairs and recorded. |
| ~19–21 of Condition A's 36.43 minutes went to three Bolero client builds Condition B never ran | A fixed verification sequence, identical and mandatory for both conditions, specified as exact commands. Any build beyond it is logged as a covariate, not silently absorbed. |
| Two agents with different investigation strategies; the reduction is confounded with that difference | Identical model, effort level, permission mode, tool surface and mission text. Multiple runs per condition to expose strategy variance rather than hide it. This confounder is **reduced, not eliminated** — see Threats. |
| Single run per condition | ≥3 runs per condition. |

## Procedure

1. Register HY-SDE-2026-0009. Freeze this document; do not edit after the
   first run starts.
2. Freeze the mission text. Use Experiment 3's mission verbatim
   (`docs/state-system/experiment-3-mission.md` at 8ac05fd, "add imaging
   finding tracking, end to end" — `ImagingFindingObserved`) so results are
   comparable to the existing record.
3. Record predictions before the first run and do not revise them
   afterwards. Both greenfield trials found their preregistrations correctly
   identified the risk area and were wrong about the specific failure inside
   it, which is only knowable because they were written first.
4. For each of the ≥3 pairs, in counterbalanced order:
   a. Spawn a fresh session on the condition's branch. Record session id,
      container, model, effort, rate-limit window state, and start usage.
   b. Deliver the frozen mission. No orchestrator intervention during the
      run.
   c. On completion, read close usage and the session event record.
   d. Apply the acceptance criteria. VOID and re-run if any fail.
5. Re-verify every condition's claimed build, test and architecture-check
   results by re-running them against the committed diff. Experiment 3's
   `M-HN3-VERIFY` found zero discrepancies doing this; that check is
   repeated, not assumed.
6. Report per-run figures in full. No condition-level mean is published
   without the individual runs beside it.

## Results

Not yet run. See **Execution feasibility** below.

## Analysis

To be completed. The analysis plan is fixed now: per-run `cost_usd` by
condition, token classes reported separately, wall clock reported as a
secondary measure carrying the build-count covariate, and Required Change
Sites reported to confirm the mutation's size did not differ between
conditions (Experiment 3 measured 19 and 19; a departure from that would
itself need explaining before any cost comparison is read).

## Threats to validity

1. **Shared blind spot.** Every agent in every experiment in this evidence
   base comes from one model family. Two systems that fail the same way are
   not two mechanisms, and this design does not fix that.
2. **Agent strategy remains confounded.** Holding the model and prompt
   constant narrows the variance; it does not make two runs the same agent.
   This is the reason for ≥3 runs, and it is a mitigation, not a removal.
3. **One codebase, one mutation.** HelixNote only. Boundary Change
   Amplification has replicated at 4.0 three times *in this codebase*, which
   makes it a stable property of this codebase, not a law.
4. **Platform-reported cost.** `cost_usd` is the platform's own figure and is
   subject to pricing changes between runs. Runs will be executed close
   together and the reading timestamps recorded.
5. **Cache-read dominance.** If cache reads dominate again, the monetary and
   token pictures may diverge. Both are reported; neither is collapsed into
   the other.
6. **Cost of measurement.** The orchestrator's own usage is excluded by
   construction, but it is not free. It is recorded separately for honesty
   about what the experiment cost to run.

## Execution environment

**Resolved. The experiment is runnable.** An earlier draft of this record
stated it was blocked; that is corrected here rather than quietly edited
away, because the blocker was real and the resolution changes what this
record commits to.

The .NET SDK cannot be fetched from the Microsoft CDN —
`builds.dotnet.microsoft.com` returns a policy denial (403 to CONNECT), and
that denial stands. It was not retried or routed around. The Ubuntu archive
and `packages.microsoft.com` are permitted hosts, and Ubuntu 24.04 ships the
SDKs directly:

```
apt-get install -y dotnet-sdk-8.0 dotnet-sdk-10.0
```

Both installed; `dotnet --list-sdks` reports 8.0.131 and 10.0.112. The .NET
10 SDK is required — the test projects target `net10.0`, and .NET 8 alone
fails them with NETSDK1045.

### Verified starting state of both conditions

Measured directly at each condition's start commit, before any run:

| Suite | Condition A (4879537) | Condition B (8d2d789) |
|---|---|---|
| `HelixNote.Semantic.Tests` | 149 passed | 149 passed |
| `HelixNote.RouteContract.Tests` | 6 passed | 6 passed |
| `HelixNote.Api.Http.Tests` | 6 passed, 19 skipped | 6 passed, 19 skipped |
| `HelixNote.Semantic.Host.Wasm.Tests` | 4 passed | 4 passed |
| `HelixNote.Semantic.Host.Postgres.Tests` | 3 passed, 52 skipped (55) | 3 passed, 55 skipped (58) |
| `HelixNote.Api.Tests` | 36 passed, **3 failed** | 36 passed, **3 failed** |

Two facts to carry into the analysis rather than discover mid-run:

1. **The 3 API failures are pre-existing and environmental, not code.** All
   three are Testcontainers tests that cannot reach a Docker daemon: the
   `docker` binary exists but `/var/run/docker.sock` does not. They fail
   identically at both start commits, so they are a constant across
   conditions, not a difference. They are excluded from the fixed
   verification sequence and their status is re-checked at each run close.
2. **Condition B's Postgres suite has 3 more tests than Condition A's** (58
   vs 55). That is the hardening itself — Experiment 2 added constraint
   agreement tests. It is expected, and it is why total test count is not a
   dependent variable here.

### What the missing Docker daemon costs this experiment

Neither condition can run database integration tests. The mission requires
persistence, so **neither agent can verify its persistence work against a
live database.** This is identical for both conditions, so the *internal*
comparison — the dependent variable is agent cost — remains valid.

It narrows external validity, and it does so in a direction worth stating
plainly: across this evidence base, a live integration test is the mechanism
that has repeatedly caught the semantic no-op when every other mechanism
missed it. An agent that cannot run one may finish cheaper and wronger. Cost
is therefore reported **alongside** each run's verification outcome, never as
a standalone figure, and no run is described as "completed the mission" on
the strength of a cost figure.

### Setup cost is excluded by construction

Installing the SDKs and restoring NuGet packages happens inside the
condition's session and would otherwise land in that session's `cost_usd`.
Each run therefore takes its **start usage reading after setup completes and
before the mission is delivered**, and its close reading immediately after
the mission ends. The reported cost is the difference. Setup cost is
recorded separately, not silently absorbed and not silently discarded.

## Replication notes

Everything needed to replicate is pinned: both condition commits, the
mission commit, the fixed verification sequence, and the model and effort
level. The per-run manifest is the unit of replication — a condition-level
summary without its per-run manifests is not replicable and is not to be
published as though it were.

## Conclusion

Pending execution.

## Registry updates required

- Register `HY-SDE-2026-0009` before the first run.
- On completion, create the `EV-SDE-` record and update
  `doctrine/CONTRADICTIONS-AND-DEPRECATED.md`: the "Experiment 3 proves a
  token/cost reduction" entry stays as the permanent record of the
  disqualified figures, with a pointer to whatever this experiment finds.
  **It is not deleted or edited away**, whichever direction the result goes.
- If supported, `/results/` on the Ordo site gains a cost section it
  currently cannot have, and `M-HN3-COST` gains a successor metric. If not
  supported, the open question on `/research/` is updated with a measured
  answer rather than an absence.
