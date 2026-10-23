/// `construct` is deterministic: two runs write byte-identical trees, and the
/// committed generated files and out/ are exactly what it writes.
module VerifiedConstruction.Tests.DeterminismTests

open System.IO
open Xunit
open VerifiedConstruction.Tests.Fixtures

let private tree (directory: string) =
    Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
    |> Array.map (fun f -> Path.GetRelativePath(directory, f).Replace('\\', '/'), File.ReadAllBytes f)
    |> Array.sortBy fst
    |> List.ofArray

let private constructInto () =
    let directory = Directory.CreateTempSubdirectory("vc-construct-").FullName
    let code = Echelon.Construct.Program.main [| handoffPath; directory |]
    Assert.Equal(0, code)
    directory

[<Fact>]
let ``two runs of construct write byte-identical files`` () =
    let a = constructInto ()
    let b = constructInto ()
    try
        let ta, tb = tree a, tree b
        Assert.Equal<string list>(ta |> List.map fst, tb |> List.map fst)
        for (pa, ba), (_, bb) in List.zip ta tb do
            Assert.True((ba = bb), pa + " differs between runs")
    finally
        Directory.Delete(a, true)
        Directory.Delete(b, true)

[<Fact>]
let ``the committed generated files and out/ are exactly what construct writes`` () =
    let fresh = constructInto ()
    try
        for relative, bytes in tree fresh do
            let committed = path relative
            Assert.True(File.Exists committed, relative + " is not committed")
            Assert.True((File.ReadAllBytes committed = bytes), relative + " differs from construct's output; rerun ./run.sh")
    finally
        Directory.Delete(fresh, true)

[<Fact>]
let ``output uses LF line endings, no BOM and no timestamps`` () =
    let fresh = constructInto ()
    try
        for relative, bytes in tree fresh do
            Assert.False(bytes |> Array.contains 13uy, relative + " contains CR")
            Assert.False(bytes.Length >= 3 && bytes[0] = 0xEFuy && bytes[1] = 0xBBuy, relative + " has a BOM")
            let text = System.Text.Encoding.UTF8.GetString bytes
            // The only date-time in any output is the handoff's own fixed authorizedAt, copied into comments.
            Assert.False(text.Contains(System.DateTime.UtcNow.ToString "yyyy-MM-ddTHH"), relative + " contains the current time")
    finally
        Directory.Delete(fresh, true)
