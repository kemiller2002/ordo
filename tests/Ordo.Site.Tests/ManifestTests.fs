/// The evidence manifest is the site's only source of published numbers, so
/// these tests are about what it refuses rather than what it accepts.
module Ordo.Site.Tests.ManifestTests

open Xunit
open Ordo.Site
open Ordo.Site.Model
open Ordo.Site.Tests.Fixtures

let private read (text: string) = ManifestReader.read text

[<Fact>]
let ``a complete manifest parses`` () =
    let manifest = valueOf (read validManifest)
    Assert.Equal(1, List.length manifest.Experiments)
    Assert.Equal(1, List.length manifest.Metrics)
    Assert.Equal("59 / 59", (List.head manifest.Metrics).Value)

[<Fact>]
let ``a metric without a limitation is rejected`` () =
    let text = validManifest.Replace("\"limitation\": \"Says nothing about what was not tested.\",", "")
    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "limitation")

[<Fact>]
let ``a metric without a source is rejected`` () =
    let text =
        validManifest.Replace("\"source\": { \"repository\": \"repo\", \"path\": \"tests.json\" }", "\"unused\": 0")

    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "source")

[<Fact>]
let ``a metric citing an unknown experiment is rejected`` () =
    let text = validManifest.Replace("\"experiment\": \"exp\"", "\"experiment\": \"nope\"")
    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "unknown experiment 'nope'")

[<Fact>]
let ``a source citing an unknown repository is rejected`` () =
    let text = validManifest.Replace("\"repository\": \"repo\", \"path\": \"tests.json\"", "\"repository\": \"ghost\", \"path\": \"tests.json\"")
    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "unknown repository 'ghost'")

[<Fact>]
let ``an experiment without limitations is rejected`` () =
    let text = validManifest.Replace("\"limitations\": [\"Only one trial.\"],", "\"limitations\": [],")
    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "limitations")

[<Fact>]
let ``an unknown provenance value is rejected rather than defaulted`` () =
    let text = validManifest.Replace("\"provenance\": \"instrumented\"", "\"provenance\": \"probably\"")
    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "unknown provenance 'probably'")

[<Fact>]
let ``duplicate metric ids are rejected`` () =
    let text = validManifest.Replace("\"metrics\": [", "\"metrics\": [\n    { \"id\": \"M-1\", \"label\": \"Copy\", \"value\": \"1\", \"definition\": \"d\", \"measurement\": \"m\", \"experiment\": \"exp\", \"provenance\": \"derived\", \"limitation\": \"l\", \"source\": { \"repository\": \"repo\", \"path\": \"p\" } },")
    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "duplicate metric id 'M-1'")

/// Two independent problems must both be reported. A parser that stops at the
/// first one turns a five-minute fix into five separate build runs.
[<Fact>]
let ``independent problems are all reported in one run`` () =
    let text =
        validManifest
            .Replace("\"experiment\": \"exp\"", "\"experiment\": \"nope\"")
            .Replace("\"confidence\": \"recommended\"", "\"confidence\": \"fairly sure\"")

    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "unknown experiment")
    Assert.Contains(errors, fun message -> message.Contains "unknown confidence class")

[<Fact>]
let ``text that is not JSON reports that plainly`` () =
    let errors = errorsOf (read "not json at all")
    Assert.Contains(errors, fun message -> message.Contains "not valid JSON")
