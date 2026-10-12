# Ordo deterministic construction engine v1 (`ordo.construction/1`)

Status: **draft / implemented in `src/Ordo.Construction`**. Tracking:
[ordo#65](https://github.com/kemiller2002/ordo/issues/65) (CONSTRUCT-002).
Input contract: [construction plan v1](construction-plan-v1.md).
Decision: [`DF-SDE-2026-0019`](../../research/decisions/DF-SDE-2026-0019--generate-four-tier-workflow-structure-deterministically.md).
Generator version 2 (effect-failure disposition): [`DF-SDE-2026-0020`](../../research/decisions/DF-SDE-2026-0020--require-a-declared-effect-failure-disposition.md).

## What it is

The engine is a pure F# projection. It takes one `ValidatedPlan` and returns
immutable artifact descriptions: paths, tiers, ownership, source text,
digests, and requirement and operation traces. It also returns the handwritten
extension contracts that the generated code requires and the open obligations
that generation cannot discharge.

The engine never writes a file, starts a process, opens a connection, reads
the clock or executes generated code. A metadata test checks the assembly's
type references for this.

`Ordo.Construction` references only `Ordo.Core` and depends on no package.
`Ordo.Core` does not reference it. Its single public entry point is
`Construction.construct : ValidatedPlan -> Result<Construction, ConstructionRefusal list>`.
The emitter is internal, so a fabricated `Workflow` or `ValidatedPlanView`
cannot reach it. A reflection test and a compile probe (FS1092) both check this.

It is not a template language and not a general code generator. It knows
exactly one shape: a finite-state workflow.

## What the engine decides, so the agent does not

| Choice | Engine rule |
|---|---|
| Placement | `generated/<Workflow>/`, one ownership root per workflow |
| Files | `<Workflow>.Model.g.fs` (Tier 1), `.Transition.g.fs` (Tier 2), `.Orchestration.g.fs` (Tier 3, only when the plan declares ports), `construction-manifest.json` |
| Namespace | `Generated.<Workflow>` |
| Type names | `<Workflow>State`, `Command`, `Guard`, `Invariant`, `Effect`, `Refusal`, `Decision`, `Rules`, `I<Workflow>Ports`, `EffectOutcome`, `Result`, and for generator version 2 `EffectFailure` and `FailureDisposition`; modules `<Workflow>Transition` and `<Workflow>Orchestration` |
| Case names | the semantic name's PascalCase key, always accessed qualified (`[<RequireQualifiedAccess>]`) |
| Ordering | initial state first, then states by name; commands, guards and invariants by name; transitions by (from, command) |
| Dependencies | none added. Tier 1 refers to nothing generated; Tier 2 refers only to Tier 1; Tier 3 refers to Tiers 1 and 2. Tier 4 (Host) is never generated. |

**Naming policy.** Names are already ASCII words, so they can never form an
invalid identifier. The engine refuses two further kinds of name, with a typed
reason and without mangling:
- union-case names that F# reserves or for which it generates members:
  `Tag`, `Item`, `None`, `Some`, `Ok`, `Error`, `True`, `False`, `Null`,
  `Unit`, `ValueNone`, `ValueSome`;
- `Is<Upper>…` names, which collide with generated testers.

Collisions after normalization are refused earlier, by plan validation.

## Generated shape

- **Tier 1 (Semantic Model).** Closed unions of states, commands, guards,
  invariants and requested effects. Nothing else.
- **Tier 2 (Transition).** Contains:
  - the refusal union (`InvalidTransition`, plus `MissingCapabilities`,
    `GuardRejected` and `InvariantViolated` when present);
  - the decision record;
  - the `Rules` extension contract;
  - `legal`;
  - an exhaustive `target` that lists every (state, command) pair, with no
    catch-all;
  - `guards`, `invariantsIn`, `requiredCapabilities` and `effects`;
  - `decide`.

  `decide` refuses an illegal pair before any extension code runs. It then
  checks capabilities, guards on the source state and invariants on the target
  state, and returns the requested effects as data.
- **Tier 3 (Orchestration).** Contains:
  - the ports interface: `CurrentTime` and/or `Perform`;
  - `EffectOutcome` (`Completed | Failed | Unknown`);
  - the `Result` type;
  - `handle`.

  `handle` reads the clock, delegates the decision to Tier 2 and asks the host
  to perform the effects. Under generator version 1 (vocabulary 1 plans) it
  reports outcomes and does not decide what a failed or unknown effect means.
  Under generator version 2 (vocabulary 2 plans) it applies the disposition
  the plan declares; see below.

## Generator version 2: the declared effect-failure disposition

A vocabulary 2 plan selects `ordo.construction/2`. The plan cannot select a
generator any other way. Tiers 1 and 2 have the same shape as version 1.
Tier 3 differs:

- `<Workflow>EffectFailure = Failed of effect * reason | Unknown of effect *
  reason`. `Unknown` stays distinct from `Failed`.
- `<Workflow>FailureDisposition`, with only the cases the plan uses
  (`Remain`, `Proceed`, `Unresolved`), and `onFailure : Effect ->
  FailureDisposition`, an exhaustive table generated from the plan.
- `firstFailure`, which returns the first requested effect, in request order,
  that did not complete. Each command requests at most one effect, because
  the effect is named by its command and ports are unique per command.
- `<Workflow>Result`, whose cases carry the post-command `state` wherever the
  plan decides one, so a host never derives it from `decision.Next`:

  | Case | When | `state` |
  |---|---|---|
  | `Accepted(decision, at, effects, state)` | every requested effect completed | target |
  | `Remained(decision, at, effects, failure, state)` | an effect failed or is unknown and the plan says `remain` | source |
  | `Proceeded(decision, at, effects, failure, state)` | an effect failed or is unknown and the plan says `proceed` | target |
  | `Unresolved(decision, at, effects, failure)` | an effect failed or is unknown and the plan says `unresolved` | none: the host must handle it |
  | `Refused(refusal, at, state)` | Tier 2 refused the command; nothing was performed | unchanged source |

  `Remained`, `Proceeded` and `Unresolved` exist only when the plan uses the
  matching disposition. `at` and `effects` are present only when the plan
  declares the clock or an effect, as in version 1.

For the time-entry timer with `onFailure: remain` on Stop, the generated
`handle` ends:

```fsharp
| Ok decision ->
    let effects = decision.Effects |> List.map (fun effect -> effect, ports.Perform(effect, decision))
    match firstFailure effects with
    | None -> TimerRemainResult.Accepted(decision, at, effects, decision.Next)
    | Some failure ->
        match failure with
        | TimerRemainEffectFailure.Failed(effect, _)
        | TimerRemainEffectFailure.Unknown(effect, _) ->
            match onFailure effect with
            | TimerRemainFailureDisposition.Remain -> TimerRemainResult.Remained(decision, at, effects, failure, decision.Previous)
```

With `proceed` the last line is `Proceeded(..., decision.Next)`. With
`unresolved` it is `Unresolved(decision, at, effects, failure)`, and the
construction records the linked obligation as
`RESOLVE:<subject>` (`ResolveEffectFailure`). The manifest of a version 2
construction has an additional `effectFailure` list of `{ effect, onFailure }`,
and a linked `resolve` obligation carries an `effect` member.

## No permissive defaults

Every guard, invariant and port is a handwritten extension contract.
- A missing rule does not compile (FS0764).
- Calling `decide` without rules does not compile (FS0001).
- A host that omits a port member does not compile (FS0366).

Generated code contains no `_ ->` catch-all, `failwith`, `Unchecked`,
`mutable`, `open`, I/O, process, reflection or async code; a test asserts
this. Requirement text, unresolved questions, trace keys and cohort IDs never
reach generated code. The only external values in the output are
grammar-checked names, grammar-checked capability literals and the hex plan
digest.

## Determinism and regeneration

Output is a pure function of the plan's canonical form and the generator
version. Every file uses LF line endings, has no BOM and ends with exactly one
trailing newline. Tests check that:
- 20 repeated constructions are identical;
- reordered or respelled plans produce identical bytes;
- regeneration reproduces the checked-in goldens byte for byte.

**Migration rule.** `ordo.construction/1` output for a given plan digest never
changes. Any change to emitted bytes is a new generator version. The goldens
are regenerated in the same commit, and that commit records the version
change. A golden is never edited by hand to make a test pass. Every generated
file starts with
`// <auto-generated by ordo.construction/<version> from <planDigest>>`.

The plan's vocabulary selects the generator version (`generatorVersionFor`):
vocabulary 1 plans keep `ordo.construction/1` byte for byte, and vocabulary 2
plans use `ordo.construction/2`.

## Ownership and applying artifacts

`construct` does not write. `Construction.planWrites existing construction` is
pure. It takes the current contents of every target path and every file under
the ownership root, and returns either a list of `Create`, `Replace`,
`Unchanged` and `RemoveStale` writes, or refusals:
- `WouldOverwriteHandwritten`: a target path holds a file without the
  generated marker;
- `HandwrittenInGeneratedRoot`: a handwritten file sits inside the generated
  root;
- `OutsideOwnershipRoot`.

Files outside the root are never touched. Extension contracts are always
`Handwritten`. The engine names them but never emits their files.

## Manifest

`construction-manifest.json` records:
- the generator version, plan digest and binding;
- each artifact's path, tier, ownership, SHA-256, operations and requirement
  keys, so every generated file traces to source requirements;
- extension contracts and obligations (implement, resolve, verify
  independently);
- structural checks for every (state, command) pair, labeled
  `"independence": "derived-from-plan"`;
- per-requirement coverage.

Its `notice` field says plainly that it is neither authorization nor evidence
of behavioral verification.

## Evidence (`tests/Ordo.Construction.Tests`)

- **Goldens.** They are compiled into the test assembly under
  `TreatWarningsAsErrors`; that is the compile evidence. Regeneration must
  match them byte for byte.
- **Time-entry timer smoke test.** All 12 (state, command) pairs are checked
  against a table written by hand from the original requirement text, with
  line citations. The test also covers restart after stop, the Stop completion
  effect requested only by Stop, Unknown and Failed effects being reported,
  and contract consistency between the plan, the structural checks and the
  generated `legal`.
- **Door synthetic fixture.** Guards, an invariant, a capability, the clock
  and an effect.
- **Effect-failure dispositions (generator version 2).** Three compiled
  goldens of the timer, one per disposition of the Stop effect
  (`Golden/time-entry-timer-v2-*`), with behavior tests in
  `EffectFailureTests.fs`. They check that a failed or unknown Stop under
  `remain` leaves `Running` and `Paused` in place, that `proceed` reaches
  `NoTimer` and reports the failure, and that `unresolved` picks no state and
  carries a linked `RESOLVE` obligation. The `remain` and `proceed` plans cite
  a test-only decision node, because the plan rules require one for a decided
  disposition. Compile probes check that the
  `Unresolved` case has no state to read and that a failure cannot be built
  as an acceptance.
- **Engine tests.** Naming refusals, justified tiers only, ownership and write
  planning, tier direction, forbidden tokens, metadata purity and architecture.
- **Compile probes.** Each asserts its exact error code.
- **Mutation checks, run during development.**
  - A permissive emitter mutant, which turns illegal pairs into no-op success,
    and a guard-dropping mutant were each caught by the golden check.
  - With the goldens regenerated from the permissive mutant, four independent
    behavior tests failed.

Generated structural checks are **not** independent acceptance evidence.
Behavioral verification of the real application remains a Dokimos (#30)
obligation.

## Requirements for Praxis #242 (trusted controlled writer)

1. **Recheck before construction.** Pin the ECIR manifest digest, the
   blueprint digest and the decision approvals independently. Call
   `prepareCohort` with authorization from the external authority, then
   `ConstructionPlan.validate`, then check `isBoundTo`. Never accept a
   `Construction` or a manifest from an agent as input.
2. **Construct inside the trusted process.** Call `Construction.construct`
   there, never in the agent sandbox. Pin the Ordo.Core and Ordo.Construction
   versions and the generator version (`ordo.construction/1` for vocabulary 1
   plans, `ordo.construction/2` for vocabulary 2 plans).
3. **Plan writes from fresh state.** Read the current content of the target
   paths and of every file under `OwnershipRoot` from the target repository.
   Call `planWrites`. On any refusal, apply nothing.
4. **Apply only the planned writes**, only under `OwnershipRoot`, and
   atomically where the host can, then verify the written bytes against each
   artifact's `Digest`. The engine adds no project references or packages. If
   generated files must be included in a project file, that edit is a separate
   host-policy decision; it is not part of construction.
5. **Carry obligations forward.** Record `Obligations` as Praxis work.
   - Each `IMPLEMENT:*` becomes a handwritten extension task (Praxis #243).
   - Each `RESOLVE:*` becomes a decision request. A `ResolveEffectFailure`
     obligation also names the effect whose failure is undecided; until it is
     resolved, the host handles the generated `Unresolved` result case
     explicitly and records what it did.
   - `VERIFY:*` goes to Dokimos (#30).

   A construction with open obligations is structure, not a completed feature.
6. **Keep regeneration narrow.** Regenerate only when the plan digest or the
   generator version changes. Remove stale generated files only as
   `RemoveStale` reports them.
