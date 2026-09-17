/// Tests for the C# measure, and for the two places where it and the F# measure
/// deliberately disagree.
///
/// Every expected value is hand-countable from the source literal beside it. The
/// cross-language pair at the bottom is the important part: it pins the claim
/// that the two measures agree on equivalent code, which is the only thing that
/// makes an F#-against-C# figure worth printing.
module Ordo.Complexity.Tests.CSharpTests

open Xunit
open Ordo.Complexity

let private branchesIn (source: string) = (CSharp.complexityOf "T.cs" source).Branches

let private fsharpControlFlow (source: string) =
    (Measure.complexityOf "T.fs" source).ControlFlowBranches

[<Fact>]
let ``an if-else-if counts each conditional once`` () =
    let source =
        """
class C {
    string Classify(int n) {
        if (n < 0) return "negative";
        else if (n == 0) return "zero";
        else return "positive";
    }
}
"""

    Assert.Equal(2, branchesIn source)

[<Fact>]
let ``switch case labels count as the alternatives they are`` () =
    let source =
        """
class C {
    string Name(int v) {
        switch (v) {
            case 0: return "zero";
            case 1: return "one";
            default: return "many";
        }
    }
}
"""

    // `default` is not a decision: it is the fall-through, like F#'s `| _`.
    // F# counts that wildcard bar, so this is a known one-off difference and
    // the cross-language test below is written to avoid relying on it.
    Assert.Equal(2, branchesIn source)

[<Fact>]
let ``switch expression arms and their guards both count`` () =
    let source =
        """
class C {
    string Name(int v) => v switch {
        < 0 => "negative",
        0 when DateTime.Now.Hour > 12 => "afternoon zero",
        _ => "other",
    };
}
"""

    // three arms plus one `when` guard
    Assert.Equal(4, branchesIn source)

[<Fact>]
let ``loops count once each, whatever their shape`` () =
    let source =
        """
class C {
    void M(int[] xs) {
        for (var i = 0; i < 10; i++) { }
        foreach (var x in xs) { }
        while (true) { break; }
        do { } while (false);
    }
}
"""

    Assert.Equal(4, branchesIn source)

[<Fact>]
let ``catch clauses and exception filters both count`` () =
    let source =
        """
class C {
    void M() {
        try { }
        catch (InvalidOperationException) { }
        catch (Exception e) when (e.Message.Length > 0) { }
    }
}
"""

    // two catch clauses, one filter
    Assert.Equal(3, branchesIn source)

[<Fact>]
let ``short-circuit operators count as the branches they are`` () =
    let source = "class C { bool M(bool a, bool b, bool c) => a && b || c; }"
    Assert.Equal(2, branchesIn source)

/// The mapping's weakest join, pinned so it stays visible. `??` is a real C#
/// branch with no counterpart in the F# arm's idiom, so it is reported
/// separately and can be subtracted.
[<Fact>]
let ``null-coalescing is counted but separable`` () =
    let source = "class C { string M(string? a, string? b) => a ?? b ?? \"fallback\"; }"
    let measured = CSharp.complexityOf "T.cs" source

    Assert.Equal(2, measured.Branches)
    Assert.Equal(2, measured.NullCoalescing)
    Assert.Equal(0, measured.BranchesWithoutNullCoalescing)

/// Type declarations are data in both languages, so neither counts them. This is
/// what makes a cross-language figure defensible at all: without it the F# side
/// scores every union case as a branch and C# scores none.
[<Fact>]
let ``declared cases count as branches in neither language`` () =
    let csharp =
        """
enum Colour { Red, Green, Blue }
class C { }
"""

    let fsharp =
        """
module S

type Colour =
    | Red
    | Green
    | Blue
"""

    Assert.Equal(0, branchesIn csharp)
    Assert.Equal(0, fsharpControlFlow fsharp)

    // The raw F# token count still sees them; that is the figure the adjustment
    // exists to correct, and it stays available for F#-to-F# comparison.
    Assert.Equal(3, (Measure.complexityOf "T.fs" fsharp).Branches)
    Assert.Equal(3, (Measure.complexityOf "T.fs" fsharp).DeclaredCases)

/// The claim the whole cross-language exercise rests on: equivalent control flow
/// scores the same in both languages. Written to avoid the known wildcard
/// difference by using no catch-all arm.
[<Fact>]
let ``equivalent control flow scores the same in both languages`` () =
    let csharp =
        """
class C {
    int M(int n, bool flag) {
        if (n > 0 && flag) return 1;
        for (var i = 0; i < n; i++) { }
        while (flag) { break; }
        return 0;
    }
}
"""

    let fsharp =
        """
module S

let m (n: int) (flag: bool) =
    if n > 0 && flag then 1
    else
        for _ in 1 .. n do ()
        while flag do ()
        0
"""

    // if, &&, for, while — four in each.
    Assert.Equal(4, branchesIn csharp)
    Assert.Equal(4, fsharpControlFlow fsharp)

[<Fact>]
let ``keywords inside C# strings and comments are not branches`` () =
    let source =
        """
class C {
    // if while for && || case when catch
    string M() => "if while for && || case when catch";
}
"""

    Assert.Equal(0, branchesIn source)

/// A single-case union declares one case and emits no `Bar` at all. An earlier
/// version counted cases from the parse tree and subtracted them from the token
/// count, which drove `Ids.fs` in the measured corpus to MINUS FIVE. Declaration
/// bars are now identified positionally — a `Bar` inside a union's source range
/// — so the adjusted figure is a subset of the raw one and cannot go negative.
[<Fact>]
let ``single-case unions do not drive the adjusted count negative`` () =
    let source =
        """
module S

type PersonId = PersonId of string
type ProjectId = ProjectId of string
type LabelId = LabelId of string

let unwrap (PersonId value) = value
"""

    let measured = Measure.complexityOf "T.fs" source

    Assert.Equal(0, measured.DeclaredCases)
    Assert.True(measured.ControlFlowBranches >= 0, "adjusted count went negative")
    Assert.Equal(measured.Branches, measured.ControlFlowBranches)

/// The inline form declares three cases from two bars, so a count-and-subtract
/// approach is wrong in this direction too. Only the two bars are separators.
[<Fact>]
let ``an inline union subtracts its separators, not its case count`` () =
    let source = "module S\ntype Colour = Red | Green | Blue\n"
    let measured = Measure.complexityOf "T.fs" source

    Assert.Equal(2, measured.Branches)
    Assert.Equal(2, measured.DeclaredCases)
    Assert.Equal(0, measured.ControlFlowBranches)

/// The adjusted figure is a subset of the raw one by construction. Pinned
/// because a violation means the two are being derived independently again,
/// which is the defect above.
[<Fact>]
let ``the adjusted count never exceeds the raw count`` () =
    let sources =
        [ "module S\ntype C = A | B\nlet f x = match x with | A -> 1 | B -> 2\n"
          "module S\ntype Id = Id of int\nlet g x = if x then 1 else 2\n"
          "module S\nlet h x = x && true || false\n" ]

    sources
    |> List.iter (fun source ->
        let measured = Measure.complexityOf "T.fs" source
        Assert.True(measured.DeclaredCases <= measured.Branches, "declared cases exceeded total branches")
        Assert.True(measured.ControlFlowBranches >= 0, "adjusted count went negative"))
