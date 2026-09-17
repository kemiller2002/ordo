# Decision-point complexity across the three effort-experiment arms

One CSV per arm, per file, produced by `tools/Ordo.Complexity`:

    dotnet run --project tools/Ordo.Complexity -- --csharp <files>   # Roslyn
    dotnet run --project tools/Ordo.Complexity -- <files>            # FCS

The C# columns are `cyclomatic, branches, branches_without_null_coalescing,
null_coalescing, lines`. The F# columns are `cyclomatic, branches,
declared_cases, control_flow_branches, lines`.

**Use `control_flow_branches` for F# against C#, and `branches` for F# against
F#.** Union and enum case separators are declarations, not control flow; C# has
no construct that inflates its count the same way, so leaving them in would
flatter C# in any cross-language reading.

**Separate the test harnesses before comparing arms.** The conventional arm
asserts with inline `if (...) throw`, the state-system arm with a shared
`Assert` helper. That alone accounts for 148 branch points against 14, and
reading the whole-arm totals without splitting them produces a claim about
architecture that is really a claim about assertion style.

The reading of these numbers, including what they do not show, is
`research/evidence/EV-SDE-2026-0010--state-structure-does-not-reduce-decision-points.md`.

**Measured after the trials finished. Not pre-registered. Descriptive only.**
