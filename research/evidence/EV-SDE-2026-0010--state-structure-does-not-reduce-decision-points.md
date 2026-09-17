---
id: EV-SDE-2026-0010
title: State structure did not reduce decision points in domain source — the apparent reduction was a test-authoring artifact
research_area: state-directed-engineering
evidence_type: derived
status: accepted
type: evidence
source_repository: kemiller2002/time-entry-state-machine
source_paths:
  - effort-experiment/c-sharp/
  - effort-experiment/c-sharp-state/
  - effort-experiment/f-sharp-state/
  - research/runs/effort-experiment-complexity/
collection_date: 2026-09-17
method: token- and syntax-tree-accurate cyclomatic complexity over three complete implementations of one frozen requirement set — two C# (Roslyn), one F# (FSharp.Compiler.Service)
observation_type: derived measurement over committed source, computed after the trials finished and not part of any pre-registered analysis
confidence: medium
created: 2026-09-17
updated: 2026-09-17
tags: [complexity, cyclomatic, effort-experiment, negative-result, post-hoc, not-preregistered, cross-language]
related_evidence: [EV-SDE-2026-0008]
---

# Evidence: the decision-point reduction is not there

**This is a negative result, and it corrects an impression the evidence base
was drifting toward.** Measured across three complete implementations of the
same frozen requirement set, state structure did **not** reduce decision points
in domain source. The one comparison that needs no cross-language mapping — C#
against C# — has the state-structured arm scoring *higher* in absolute terms.

## Not pre-registered

Taken after all three trials finished, on a question nobody wrote down in
advance. It is descriptive. It tests no hypothesis and cannot confirm one.

## The result

Domain source only, test harnesses excluded:

| Arm | Branch points | Lines | Per 100 lines |
|---|---|---|---|
| Conventional C# | **78** | 850 | 9.2 |
| State-system C# | **88** | 1,095 | 8.0 |
| State-system F# | **130** | 1,104 | 11.8 |

The state-structured C# domain has **more** decision points than the
conventional one, on more lines. Density is modestly lower — 8.0 against 9.2
per hundred lines — which is not a difference this measure can carry any weight
on at n=1 per arm.

The F# arm scores highest of the three. See the cross-language caveat below
before reading anything into that.

## What the headline figure would have been, and why it is wrong

Measured whole-arm, including each arm's test harness, the totals are 226,
102 and 175 branch points. That reads as a **55% reduction** for the
state-structured C# arm and would have been a striking finding.

It is an artifact of how the two test harnesses assert:

| Arm | Test branch points | Test lines | Per 100 lines |
|---|---|---|---|
| Conventional C# | **148** | 800 | 18.5 |
| State-system C# | **14** | 860 | 1.6 |
| State-system F# | 45 | 661 | 6.8 |

The conventional harness writes `if (condition) throw new Exception(...)`
inline at every assertion, so every assertion is a branch point. The
state-system harness declares `static void Assert(bool, string)` once and calls
it, so the branch exists in exactly one place. Same number of assertions, same
verification, one decision point versus a hundred and forty-eight.

**That is a test-authoring style difference and nothing else.** It says nothing
about either architecture. Reporting the whole-arm totals without separating
the harness would have published a claim about architecture that is really a
claim about whether someone factored out an assertion helper.

## Why this does not contradict EV-SDE-2026-0008

`EV-SDE-2026-0008` measured complexity **added by an agent run** against a
fixed start commit in one codebase, and found hardened runs adding fewer branch
points. This measures **total complexity of finished implementations** built
from one requirement set in different architectures. They are different
questions and both results stand.

Together they narrow the claim rather than support it: whatever the hardened
condition did to what an agent *wrote per run*, the finished state-structured
domain here is not smaller in decision points than the conventional one. Anyone
citing EV-SDE-2026-0008 as evidence that state structure produces simpler code
should read this record first.

## Limitations

- **One implementation per arm.** n=1. Two of the three arms were written by
  the same author against the same requirements, and the conventional arm was
  written under a deliberately ordinary persona. Persona is not controlled.
- **Cross-language comparison is weaker than within-language.** The F# figure
  is not safely comparable with either C# figure. F# idiom routes control flow
  through `match` expressions whose every clause counts, where C# may use an
  if-chain or early returns; `Commands.fs` scores 83 against `Commands.cs`'s
  49 for the same commands. The mapping between the two measures is documented
  in `tools/Ordo.Complexity/CSharp.fs` and is a judgment, not a fact. **The
  C#-against-C# row is the only comparison here that needs no mapping, and it
  is the one that shows no reduction.**
- **Decision-point count is not a quality measure.** Fewer branch points is not
  automatically better, and this record makes no claim that it is. A
  `match` that enumerates every case is more decision points and more safety
  than an `if` that handles two of them.
- **Declaration bars are excluded in both languages**, so a union case and an
  enum member count as data rather than control flow. Without that, the F#
  figure would be inflated by 65 and the comparison would be meaningless.
- **`??` is counted in C# and has no F# counterpart** in this corpus. It
  occurs twice in total, so it changes nothing here, but it is reported
  separately in the data files so a reader can subtract it.

## What a successor needs

1. **More than one implementation per arm.** Every figure here is a single
   observation.
2. **A held-constant test-authoring convention**, or the harness excluded by
   design. This measurement nearly produced a false finding on that alone.
3. **A reason to care about decision-point count.** It is a proxy. Nothing in
   this evidence base yet links it to a measured outcome — defects, cost, or
   change difficulty — and until something does, a difference in it is a
   description rather than a result.
