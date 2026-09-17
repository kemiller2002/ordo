/// Decision-point complexity for C# sources, counted from Roslyn's syntax tree.
///
/// The companion to `Ordo.Complexity.Measure`, which does the same for F#. The
/// point of having both is the effort experiment, whose three arms are two C#
/// and one F# — a comparison the F#-only tool could not make at all.
///
/// CROSS-LANGUAGE COMPARISON IS WEAKER THAN WITHIN-LANGUAGE COMPARISON, and
/// this file is where that weakness lives. Two languages do not express
/// branching the same way, so mapping one set of constructs onto the other
/// embeds a judgment. The judgment is written down here so it can be argued
/// with rather than discovered later:
///
///   C# construct                     counted because
///   ---------------------------------------------------------------------
///   if                               conditional branch; `else if` is a
///                                    nested if, so it counts once each
///   while, do, for, foreach          loop back-edge
///   case label, switch-expression    one alternative, the equivalent of an
///     arm                            F# match clause
///   when clause, catch filter        a guard. Roslyn models a switch guard
///                                    (WhenClause) and an exception filter
///                                    (CatchFilterClause) as different nodes,
///                                    and both are guards, so both count.
///   catch clause                     an alternative path, the equivalent of
///                                    an F# `try ... with` arm
///   &&, ||                           short-circuit, a hidden branch
///   ?:                               a conditional, written in F# as
///                                    `if/then/else`, which is counted
///   ??                               a branch on null. F# has no direct
///                                    equivalent in the measured code, which
///                                    is the clearest single asymmetry in
///                                    this mapping and is reported separately
///                                    so it can be subtracted.
///
/// Counted in NEITHER language: type declarations. An F# union case and a C#
/// enum member are data, not control flow. The F# side subtracts its union
/// cases for exactly this reason — see `Measure.unionCaseCount`.
module Ordo.Complexity.CSharp

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax

/// Split out from the total because it is this mapping's weakest join: `??`
/// is a real branch in C# with no counterpart in the F# arm's idiom, so a
/// reader who thinks it should not count can subtract it.
type CSharpComplexity =
    { Path: string
      Branches: int
      NullCoalescing: int
      Lines: int }

    /// Branch points excluding `??`, for the strictest cross-language reading.
    member this.BranchesWithoutNullCoalescing = this.Branches - this.NullCoalescing

let private isDecision (node: SyntaxNode) =
    match node with
    | :? IfStatementSyntax
    | :? WhileStatementSyntax
    | :? DoStatementSyntax
    | :? ForStatementSyntax
    | :? ForEachStatementSyntax
    | :? CaseSwitchLabelSyntax
    | :? CasePatternSwitchLabelSyntax
    | :? SwitchExpressionArmSyntax
    | :? WhenClauseSyntax
    | :? CatchFilterClauseSyntax
    | :? CatchClauseSyntax
    | :? ConditionalExpressionSyntax -> true
    | :? BinaryExpressionSyntax as binary ->
        binary.IsKind SyntaxKind.LogicalAndExpression
        || binary.IsKind SyntaxKind.LogicalOrExpression
        || binary.IsKind SyntaxKind.CoalesceExpression
    | _ -> false

let private isNullCoalescing (node: SyntaxNode) =
    match node with
    | :? BinaryExpressionSyntax as binary -> binary.IsKind SyntaxKind.CoalesceExpression
    | _ -> false

let complexityOf (path: string) (source: string) : CSharpComplexity =
    let decisions =
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
        |> Seq.filter isDecision
        |> List.ofSeq

    { Path = path
      Branches = List.length decisions
      NullCoalescing = decisions |> List.filter isNullCoalescing |> List.length
      Lines = source.Split('\n').Length }

let isCSharp (path: string) =
    System.IO.Path.GetExtension path = ".cs"
