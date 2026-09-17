/// Decision-point complexity for F# sources, counted from the compiler's own
/// token stream rather than by regular expression.
///
/// The measure is cyclomatic complexity in McCabe's decision-counting form:
/// one plus the number of branch points, computed per file. Tokens come from
/// the F# lexer, so a keyword inside a string literal or a comment cannot be
/// counted — the failure mode that makes naive text matching useless on real
/// source.
///
/// What counts as a branch point, stated exactly so the number can be argued
/// with:
///
///   If, Elif                    a conditional branch
///   When                        a guard on a match clause or exception filter
///   While, WhileBang, For       a loop back-edge
///   Bar                         a match clause or union-case alternative
///   AmpersandAmpersand, BarBar  a short-circuit operator, a hidden branch
///
/// `Match` itself is deliberately NOT counted: its clauses are already counted
/// through `Bar`, and counting both would double-count every match expression.
///
/// `Bar` is the known imprecision. It fires on discriminated-union definitions
/// as well as on match clauses, so a file declaring many union cases scores
/// higher than its control flow alone warrants. That inflation applies
/// identically to both experimental conditions, so it does not bias the
/// between-condition comparison it was built for.
///
/// It WOULD bias a comparison against C#, which has no construct that inflates
/// the same way. So the inflation is now measured rather than merely declared:
/// `DeclaredCases` counts union and enum cases from the parse tree, and
/// `ControlFlowBranches` is the figure with them removed. Use the raw
/// `Branches` to compare F# against F#, and `ControlFlowBranches` to compare
/// F# against C#.
///
/// A `Bar` immediately followed by `Null` is excluded — see `isNullTypeBar` for
/// why, and for what that exclusion costs.
///
/// SCOPE: this module measures `.fs` and `.fsi`. C# is measured by the
/// companion module `Ordo.Complexity.CSharp`. The
/// codebases it has been pointed at are not F#-only — the agent-cost runs also
/// wrote SQL containing triggers, function bodies and CHECK constraints, and
/// the effort experiment elsewhere in this programme has two C# arms. A total
/// produced here is a LOWER BOUND on the complexity of a change, never the
/// whole of it, and anything citing it must say which languages it covers.
///
/// `deltaOf` is the function the experiment needs. Each run starts from its own
/// condition's start commit, and the two conditions are structurally different
/// codebases, so their absolute totals are not comparable. What is comparable is
/// how much decision-point complexity each run ADDED relative to the tree it was
/// handed. A file the run never touched contributes zero by construction, because
/// its base and head texts are identical.
module Ordo.Complexity.Measure

// FSharpLexer.Tokenize is marked experimental by FCS. It is used anyway,
// deliberately: the alternative is matching keywords by regular expression,
// which counts `if` inside a string literal and inside a comment. A pinned FCS
// version makes "subject to change" a versioning question, not a correctness
// one, and the tool's output is validated against a hand-counted sample before
// any measurement is taken from it.
#nowarn "57"

open System
open System.Diagnostics
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open FSharp.Compiler.Tokenization

/// Declared union and enum cases, counted from the parse tree rather than the
/// token stream.
///
/// This exists to make the F# figure comparable with the C# one. `Bar` fires on
/// a union-case declaration as well as on a match clause, and a declaration is
/// data, not control flow. C# has no construct that inflates its count the same
/// way, so leaving the inflation in would flatter C# in any cross-language
/// reading — and the effort experiment's arms are two C# against one F#.
///
/// Counting them separately turns a documented imprecision into a measured
/// quantity that a reader can subtract, instead of a caveat they have to trust.
let private parser = lazy FSharpChecker.Create()

/// The source ranges of union and enum type representations.
///
/// Ranges rather than a count of cases, because counting cases and subtracting
/// is wrong in both directions. `type PersonId = PersonId of string` declares
/// one case and emits no `Bar` at all, so subtracting its count drives the
/// figure negative — which is how this was found. `type X = A | B | C` declares
/// three cases from two bars. There is no fixed ratio, so the only correct
/// approach is positional: a `Bar` inside one of these ranges is separating
/// declared cases, and a `Bar` outside them is control flow.
let rec private unionRangesInDecls (decls: SynModuleDecl list) : FSharp.Compiler.Text.range list =
    decls
    |> List.collect (fun decl ->
        match decl with
        | SynModuleDecl.Types(typeDefns, _) ->
            typeDefns
            |> List.choose (fun (SynTypeDefn(typeRepr = repr)) ->
                match repr with
                | SynTypeDefnRepr.Simple(SynTypeDefnSimpleRepr.Union(range = range), _) -> Some range
                | SynTypeDefnRepr.Simple(SynTypeDefnSimpleRepr.Enum(range = range), _) -> Some range
                | _ -> None)
        | SynModuleDecl.NestedModule(decls = inner) -> unionRangesInDecls inner
        | _ -> [])

let declarationRanges (path: string) (source: string) : FSharp.Compiler.Text.range list =
    let options = { FSharpParsingOptions.Default with SourceFiles = [| path |] }

    let parsed =
        parser.Value.ParseFile(path, SourceText.ofString source, options)
        |> Async.RunSynchronously

    match parsed.ParseTree with
    | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
        modules |> List.collect (fun (SynModuleOrNamespace(decls = decls)) -> unionRangesInDecls decls)
    | ParsedInput.SigFile _ -> []

type FileComplexity =
    { Path: string
      Cyclomatic: int
      Branches: int
      DeclaredCases: int
      Lines: int }

    /// Branch points with union and enum case separators removed. This is the
    /// figure to use against a C# measurement; `Branches` is the raw token
    /// count and stays comparable with earlier F#-only figures. It can never
    /// exceed `Branches`, because `DeclaredCases` counts a subset of the same
    /// tokens rather than a separately-derived quantity.
    member this.ControlFlowBranches = this.Branches - this.DeclaredCases

/// Complexity of an absent file. A file added by a run has this at base; a file
/// deleted by a run has it at head. `Cyclomatic` is 0 rather than 1 because the
/// "+1" of McCabe's formula counts a single entry path, and absent code has none.
let absent (path: string) =
    { Path = path
      Cyclomatic = 0
      Branches = 0
      DeclaredCases = 0
      Lines = 0 }

let isBranch (kind: FSharpTokenKind) =
    match kind with
    | FSharpTokenKind.If
    | FSharpTokenKind.Elif
    | FSharpTokenKind.When
    | FSharpTokenKind.While
    | FSharpTokenKind.WhileBang
    | FSharpTokenKind.For
    | FSharpTokenKind.Bar
    | FSharpTokenKind.AmpersandAmpersand
    | FSharpTokenKind.BarBar -> true
    | _ -> false

/// A `Bar` immediately followed by `Null` is never counted.
///
/// This rule exists because `|` before `null` is ambiguous to a lexer, which has
/// no context to tell `status : string | null` — a nullable type annotation,
/// carrying no control flow at all — from `| null -> None`, a genuine match
/// clause. The measured codebase writes far more of the first than the second:
/// in the one file where the two FCS versions disagreed, 23 of the 24 disputed
/// tokens were type annotations and one was a real match clause. Excluding all
/// of them trades a small, known undercount for a large, known overcount.
///
/// It also makes the tool's output independent of the compiler-service version.
/// FCS 43.8.400 emits `Bar` before `Null`; the version shipping with the .NET 10
/// SDK does not. Measured under this rule, both agree token for token, so a
/// figure recorded today can be reproduced later without pinning a lexer.
let isNullTypeBar (kind: FSharpTokenKind) (next: FSharpTokenKind option) =
    match kind, next with
    | FSharpTokenKind.Bar, Some FSharpTokenKind.Null -> true
    | _ -> false

type Counted =
    { Line: int
      Column: int
      Kind: FSharpTokenKind }

/// Every token the measure counts, in source order. Collection is separated from
/// counting so that `--tokens` prints exactly what the totals are built from: the
/// audit and the number cannot disagree.
let countedTokens (source: string) : Counted list =
    let all = ResizeArray<Counted>()

    FSharpLexer.Tokenize(
        SourceText.ofString source,
        (fun token ->
            all.Add
                { Line = token.Range.StartLine
                  Column = token.Range.StartColumn
                  Kind = token.Kind })
    )

    let kinds = all |> Seq.map (fun t -> t.Kind) |> Array.ofSeq

    all
    |> Seq.mapi (fun index token ->
        let next = if index + 1 < kinds.Length then Some kinds[index + 1] else None
        token, next)
    |> Seq.filter (fun (token, next) -> isBranch token.Kind && not (isNullTypeBar token.Kind next))
    |> Seq.map fst
    |> List.ofSeq

let private within (ranges: FSharp.Compiler.Text.range list) (token: Counted) =
    ranges
    |> List.exists (fun range ->
        let afterStart =
            token.Line > range.StartLine
            || (token.Line = range.StartLine && token.Column >= range.StartColumn)

        let beforeEnd =
            token.Line < range.EndLine
            || (token.Line = range.EndLine && token.Column <= range.EndColumn)

        afterStart && beforeEnd)

let complexityOf (path: string) (source: string) : FileComplexity =
    let tokens = countedTokens source
    let ranges = declarationRanges path source

    let declaration =
        tokens
        |> List.filter (fun token -> token.Kind = FSharpTokenKind.Bar && within ranges token)
        |> List.length

    { Path = path
      Cyclomatic = List.length tokens + 1
      Branches = List.length tokens
      DeclaredCases = declaration
      Lines = source.Split('\n').Length }

let isFSharp (path: string) =
    let extension = Path.GetExtension path
    extension = ".fs" || extension = ".fsi"

// ---------------------------------------------------------------------------
// git, read-only, through the porcelain rather than by parsing object files
// ---------------------------------------------------------------------------

/// Runs git and returns its stdout with the exit code, so a caller can tell a
/// path that does not exist at a revision (exit 128) from one that is empty.
let git (repo: string) (arguments: string list) : int * string =
    let info = ProcessStartInfo("git", RedirectStandardOutput = true, RedirectStandardError = true)
    info.ArgumentList.Add "-C"
    info.ArgumentList.Add repo
    arguments |> List.iter info.ArgumentList.Add

    // A git that will not start is not a file with no branches in it. Failing
    // loudly is the point: the alternative is a measurement of zero that looks
    // exactly like a real one.
    match Process.Start info with
    | null -> failwithf "could not start git %s" (String.Join(" ", arguments))
    | started ->
        use proc = started
        let output = proc.StandardOutput.ReadToEnd()
        proc.StandardError.ReadToEnd() |> ignore
        proc.WaitForExit()
        proc.ExitCode, output

/// The file's text at a revision, or None when the path does not exist there.
let showAt (repo: string) (revision: string) (path: string) : string option =
    match git repo [ "show"; revision + ":" + path ] with
    | 0, text -> Some text
    | _ -> None

let changedFSharpFiles (repo: string) (baseRev: string) (headRev: string) : string list =
    git repo [ "diff"; "--name-only"; baseRev; headRev ]
    |> snd
    |> fun text -> text.Split('\n')
    |> Array.toList
    |> List.map (fun line -> line.Trim())
    |> List.filter (fun line -> line <> "" && isFSharp line)
    |> List.sort

type Delta =
    { Path: string
      Base: FileComplexity
      Head: FileComplexity }

    member this.Added = this.Head.Branches - this.Base.Branches
    member this.LinesAdded = this.Head.Lines - this.Base.Lines

let deltaOf (repo: string) (baseRev: string) (headRev: string) (path: string) : Delta =
    let at revision =
        showAt repo revision path
        |> Option.map (complexityOf path)
        |> Option.defaultValue (absent path)

    { Path = path; Base = at baseRev; Head = at headRev }

// ---------------------------------------------------------------------------

/// Prints every token the file mode would count, one per line, so a disputed
/// number can be traced to the exact source lines that produced it rather than
/// argued about in the abstract.
let reportTokens (path: string) =
    printfn "line,column,kind"

    File.ReadAllText path
    |> countedTokens
    |> List.iter (fun token -> printfn "%d,%d,%A" token.Line token.Column token.Kind)
