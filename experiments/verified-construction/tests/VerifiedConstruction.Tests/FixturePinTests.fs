/// The fixture is pinned, not forked: the copied handoff must be byte-identical
/// to fixture-v1 and its sources must match the digests the handoff declares.
module VerifiedConstruction.Tests.FixturePinTests

open System.IO
open System.Security.Cryptography
open Xunit
open VerifiedConstruction.Tests.Fixtures

let private hex (bytes: byte array) = (SHA256.HashData bytes |> System.Convert.ToHexString).ToLowerInvariant()

[<Fact>]
let ``the copied handoff is byte-identical to fixture-v1`` () =
    Assert.Equal(PinnedHandoffSha256, hex (handoffBytes ()))

[<Fact>]
let ``every declared source digest matches the copied source`` () =
    let h = Echelon.Construct.Handoff.read (handoffText ()) |> ok "read"
    let sources = (h.Requirements |> List.map _.Source) @ (h.Decisions |> List.map _.Source)
    Assert.NotEmpty sources
    for s in sources do
        let actual = "sha256:" + hex (File.ReadAllBytes(path ("fixture/invoice-slice/" + s.Path)))
        Assert.Equal(s.Sha256, Some actual)
