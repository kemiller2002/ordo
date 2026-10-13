# Time-entry timer construction fixture

The smallest meaningful real slice of an existing application, used to
exercise the construction path end to end. In order:

1. pinned source manifest;
2. ECIR blueprint;
3. prepared cohort;
4. typed construction plan;
5. generated F# (#65).

## Provenance

The source repository is `kemiller2002/time-entry-state-machine`, at commit
`73a63f1ac319b97965d4a9e1607a975ebbe2bd81`, read-only. Each file in `sources/`
is a verbatim `sed -n 'A,Bp'` excerpt with LF line endings, and each
requirement's `contentDigest` is the SHA-256 of its excerpt. A test recomputes
those digests.

| Excerpt | Source path | Lines | Disposition |
|---|---|---|---|
| `timer-transitions.txt` | `docs/architecture/business-activity-ledger-state-model.md` | 116-129 | modeled |
| `timer-stop-completion.txt` | same | 131-144 | modeled (Stop effect port; arithmetic is handwritten) |
| `timer-session-scoped.txt` | same | 145-148 | modeled (no persistence port) |
| `timer-single-active.txt` | `requirements-reconstruction/fsharp-business-requirements-beyond-csharp.md` | 104 | modeled |
| `timer-paused-time-excluded.txt` | same | 105-107 | modeled (current-time port; arithmetic is handwritten) |
| `timer-under-thirty-seconds.txt` | same | 113-114 | **unresolved**: the code rejects such a stop with `TIMER_TOO_SHORT`, while `STATE-MODEL.md` returns to NoTimer |
| `timer-command-rejection.txt` | `effort-experiment/f-sharp-state/REQUIREMENTS.md` | 367-368 | modeled |

The source has no formal requirement IDs, so each `originalId` is the line
range and is labeled as one. The reconstructed requirements file was itself
derived from the F# code. The architecture note (§5) is the most original text,
and the code cites it. The expectations here are independent of the code
text, not of its authors.

## Fixture assumptions

- `DEC-TIMER-STATES` is approved by a **test input** that stands in for the
  external authority. No approval is recorded anywhere, and none is implied.
- The plan generates three flat states (NoTimer, Running, Paused) as drawn in
  the architecture note. Stop returns to NoTimer, as the requirement text says.
  The code never constructs that state explicitly.
- Two questions are declared unresolved and not guessed:
  - what happens to the timer when entry creation refuses the stopped timer's
    entry;
  - whether the single timer is scoped per person or per session.

## Vocabulary 2 variants

`plan.v2-remain.json`, `plan.v2-proceed.json` and `plan.v2-unresolved.json`
are the same plan in `ordo.construction-vocabulary/2`, against the same
manifest, blueprint and cohort. They differ only in the Stop effect's
`onFailure` disposition and in the workflow name, which keeps the three
compiled goldens in separate namespaces (`TimerRemain`, `TimerProceed`,
`TimerUnresolved`).

- `plan.v2-unresolved.json` is the faithful plan for this source. The
  requirements do not say what happens to the timer when entry creation
  refuses the stopped timer's entry, so Stop declares `onFailure: unresolved`,
  and the existing "Stop completion refused" obligation names the effect
  (`"effect": "Stop"`).
- `plan.v2-remain.json` and `plan.v2-proceed.json` choose an answer **as test
  inputs only**, to exercise generation. They drop the "Stop completion
  refused" obligation, because a decided effect has no open question. Neither
  is a resolution of the source question, and no decision is recorded.
- A decided disposition must cite a decision the cohort requires
  (`EffectDispositionWithoutDecision`). The source has no such decision, and
  citing `DEC-TIMER-STATES` would be false: it decides the three states, and
  it does not anchor the Stop-completion requirement (L131-L144). So the
  decided variants bind test-only blueprints, `blueprint.v2-remain.json` and
  `blueprint.v2-proceed.json`. Each is `blueprint.json` plus a decision node
  `DEC-TIMER-STOP-FAILURE` on L131-L144, and that node's justification
  begins "TEST INPUT ONLY, not a recorded decision". The tests approve it as a
  test input that stands in for the external authority, exactly as they
  approve `DEC-TIMER-STATES`. `plan.v2-unresolved.json` binds the original
  blueprint and needs no decision.

They were produced from `plan.json` and `blueprint.json` with `encodePlan`
and `encodeBlueprint`, like the originals.

## Construction decision records

`construction-decisions.json` is an `ordo.construction-decisions/1` record set
for `COH-TIMER`, bound to the canonical blueprint digest of `blueprint.json`.
`ConstructionDefaultPlan.generateDefaultPlan` turns it into the default plan
without agent input.
- `DEC-TIMER-STATES` (`stateModel`): the states NoTimer (initial), Running and
  Paused, and the transitions Start NoTimer->Running, Pause Running->Paused,
  Resume Paused->Running, and Stop from Running and from Paused to NoTimer.
  All of them come from L116-L129. Every transition also cites L367-L368
  (wrong-state commands are rejected), and Start cites L104 (one active
  timer).
- `IFC-TIMER-PORTS` (`portContract`):
  - `currentTime`, citing L131-L144 and L105-L107;
  - a Stop `transitionEffect` citing L131-L144, with `onFailure: null`. The
    sources do not decide what a failed entry creation means, so the
    generator emits an obligation and does not choose.

Like the `DEC-TIMER-STATES` approval in the tests, this record set is a
**test input**. It stands in for an operator-approved set; no approval is
recorded anywhere. The file is written in canonical form
(`encodeDecisionRecords`), so its SHA-256 equals
`ConstructionDecisions.digest`. Do not edit it to make a test pass.

## Regenerating

`source-manifest.json`, `blueprint.json` and `plan.json` were produced by
Ordo's own encoders (`encodeManifest`, `encodeBlueprint`, `encodePlan`). The
tests read them back strictly and pin the plan digest. Do not edit them to make
a test pass. A change of meaning needs a new plan, which gets a new digest.
