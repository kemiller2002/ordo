/// Tests for the complexity measure that experiment records cite.
///
/// A measurement tool is only worth as much as the argument that its numbers
/// are the numbers it claims. Each test here pins one such claim, and every
/// expected value is hand-countable from the source literal beside it.
module Ordo.Complexity.Tests.ComplexityTests

open System
open System.Diagnostics
open System.IO
open Xunit
open Ordo.Complexity.Measure

let private branchesIn (source: string) = (complexityOf "test.fs" source).Branches

/// The reason this tool lexes instead of matching text. Both the comment and
/// the string literal are full of branch keywords; neither is control flow.
[<Fact>]
let ``keywords inside comments and strings are not branches`` () =
    let source =
        """
module Sample

// if match | || && while for elif when
let message = "if match | || && while for elif when"
"""

    Assert.Equal(0, branchesIn source)

[<Fact>]
let ``an if-elif-else counts both conditionals and nothing else`` () =
    let source =
        """
module Sample

let classify n =
    if n < 0 then "negative"
    elif n = 0 then "zero"
    else "positive"
"""

    Assert.Equal(2, branchesIn source)

/// `match` is not counted, because its clauses already are. Counting the
/// keyword as well would add one to every match expression in the codebase.
[<Fact>]
let ``a match counts its clauses once, not its clauses plus the keyword`` () =
    let source =
        """
module Sample

let name value =
    match value with
    | 0 -> "zero"
    | 1 -> "one"
    | _ -> "many"
"""

    Assert.Equal(3, branchesIn source)

[<Fact>]
let ``a guard counts beyond the clause it guards`` () =
    let source =
        """
module Sample

let name value =
    match value with
    | n when n < 0 -> "negative"
    | _ -> "other"
"""

    Assert.Equal(3, branchesIn source)

[<Fact>]
let ``short-circuit operators count as the branches they are`` () =
    let source =
        """
module Sample

let ok a b c = a && b || c
"""

    Assert.Equal(2, branchesIn source)

/// The documented imprecision, pinned so that it stays a known quantity rather
/// than becoming a surprise. A union declaration has no control flow, and this
/// measure scores it as though it had.
[<Fact>]
let ``union declarations inflate the count, which is why no absolute claim is made`` () =
    let source =
        """
module Sample

type Colour =
    | Red
    | Green
    | Blue
"""

    Assert.Equal(3, branchesIn source)

/// The nullness rule, in the shape that motivated it: a type annotation is not
/// a branch, and FCS 43.8.400 would count it as one without this exclusion.
[<Fact>]
let ``a nullable type annotation is not a branch`` () =
    let source =
        """
module Sample

type Dto =
    { title: string
      status: string | null }
"""

    Assert.Equal(0, branchesIn source)

/// The cost of that rule, stated as a test rather than left in a comment. This
/// clause is real control flow and the measure misses it. It is accepted
/// because the alternative miscounts type annotations, which this codebase
/// writes far more often, and because the loss falls on both experimental
/// conditions alike.
[<Fact>]
let ``a null match clause is also missed, and that is the accepted trade`` () =
    let source =
        """
module Sample

let supplied (v: string | null) =
    match v with
    | null -> None
    | v -> Some v
"""

    // Two clauses are written; only the second is counted.
    Assert.Equal(1, branchesIn source)

[<Fact>]
let ``cyclomatic complexity is one more than the branch count`` () =
    let source =
        """
module Sample

let f a = if a then 1 else 2
"""

    let measured = complexityOf "test.fs" source
    Assert.Equal(measured.Branches + 1, measured.Cyclomatic)

/// Absent code has no entry path, so it scores zero rather than one. A file a
/// run created must contribute its whole complexity as added, not one less.
[<Fact>]
let ``an absent file scores zero, not one`` () =
    let measured = absent "gone.fs"
    Assert.Equal(0, measured.Cyclomatic)
    Assert.Equal(0, measured.Branches)

[<Fact>]
let ``the audit listing and the total are built from the same tokens`` () =
    let source =
        """
module Sample

let f a b =
    match a with
    | 0 when b -> 1
    | _ -> if b then 2 else 3
"""

    Assert.Equal(branchesIn source, List.length (countedTokens source))

// ---------------------------------------------------------------------------
// Delta mode, against a real git repository rather than a stub, because the
// part worth testing is the interaction with `git show`.
// ---------------------------------------------------------------------------

let private run (directory: string) (command: string) (arguments: string list) =
    let info = ProcessStartInfo(command, RedirectStandardOutput = true, RedirectStandardError = true)
    info.WorkingDirectory <- directory
    arguments |> List.iter info.ArgumentList.Add

    match Process.Start info with
    | null -> failwithf "could not start %s" command
    | started ->
        use proc = started
        proc.StandardOutput.ReadToEnd() |> ignore
        proc.StandardError.ReadToEnd() |> ignore
        proc.WaitForExit()

let private temporaryRepository (build: string -> unit) =
    let directory = Path.Combine(Path.GetTempPath(), "ordo-complexity-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory directory |> ignore

    try
        run directory "git" [ "init"; "-q"; "-b"; "main" ]
        run directory "git" [ "config"; "user.email"; "test@example.com" ]
        run directory "git" [ "config"; "user.name"; "test" ]
        build directory
    finally
        try
            Directory.Delete(directory, true)
        with _ ->
            ()

let private commit (directory: string) (message: string) =
    run directory "git" [ "add"; "-A" ]
    run directory "git" [ "commit"; "-q"; "-m"; message ]

[<Fact>]
let ``a range that changes nothing adds no complexity`` () =
    temporaryRepository (fun directory ->
        File.WriteAllText(Path.Combine(directory, "a.fs"), "module A\nlet f x = if x then 1 else 2\n")
        commit directory "one"

        let deltas = changedFSharpFiles directory "HEAD" "HEAD" |> List.map (deltaOf directory "HEAD" "HEAD")
        Assert.Empty(deltas))

[<Fact>]
let ``a file the range creates contributes its whole complexity`` () =
    temporaryRepository (fun directory ->
        File.WriteAllText(Path.Combine(directory, "a.fs"), "module A\nlet f x = x\n")
        commit directory "base"
        File.WriteAllText(Path.Combine(directory, "b.fs"), "module B\nlet g x = if x then 1 else 2\n")
        commit directory "head"

        let deltas =
            changedFSharpFiles directory "HEAD~1" "HEAD"
            |> List.map (deltaOf directory "HEAD~1" "HEAD")

        let created = deltas |> List.find (fun delta -> delta.Path = "b.fs")
        Assert.Equal(0, created.Base.Branches)
        Assert.Equal(1, created.Head.Branches)
        Assert.Equal(1, created.Added))

/// Removing control flow must be able to show up as a negative number. A
/// measure that can only go up would report a simplifying run as a neutral one.
[<Fact>]
let ``a range that removes a branch reports a negative delta`` () =
    temporaryRepository (fun directory ->
        File.WriteAllText(Path.Combine(directory, "a.fs"), "module A\nlet f x = if x then 1 else 2\n")
        commit directory "base"
        File.WriteAllText(Path.Combine(directory, "a.fs"), "module A\nlet f x = 1\n")
        commit directory "head"

        let delta = deltaOf directory "HEAD~1" "HEAD" "a.fs"
        Assert.Equal(-1, delta.Added))

[<Fact>]
let ``non-F-sharp files in the range are ignored`` () =
    temporaryRepository (fun directory ->
        File.WriteAllText(Path.Combine(directory, "a.fs"), "module A\nlet f x = x\n")
        commit directory "base"
        File.WriteAllText(Path.Combine(directory, "schema.sql"), "-- if match | || &&\nselect 1;\n")
        commit directory "head"

        Assert.Empty(changedFSharpFiles directory "HEAD~1" "HEAD"))
