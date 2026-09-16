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

/// Two problems in the same phase must both be reported. A parser that stops
/// at the first one turns a five-minute fix into five separate build runs.
[<Fact>]
let ``independent field problems are all reported in one run`` () =
    let text =
        validManifest
            .Replace("\"provenance\": \"instrumented\"", "\"provenance\": \"probably\"")
            .Replace("\"confidence\": \"recommended\"", "\"confidence\": \"fairly sure\"")

    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "unknown provenance")
    Assert.Contains(errors, fun message -> message.Contains "unknown confidence class")

/// Field validation and cross-reference resolution are deliberately sequenced
/// rather than accumulated together: resolving an id on a record whose own
/// fields have not parsed would report a second failure caused by the first.
/// Within the cross-reference phase, errors still accumulate. This test pins
/// both halves of that decision so the sequencing stays a choice.
[<Fact>]
let ``cross-reference errors accumulate once the records themselves parse`` () =
    let text =
        validManifest
            .Replace("\"experiment\": \"exp\"", "\"experiment\": \"nope\"")
            .Replace("\"repository\": \"repo\", \"path\": \"map.md\"", "\"repository\": \"ghost\", \"path\": \"map.md\"")

    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "unknown experiment 'nope'")
    Assert.Contains(errors, fun message -> message.Contains "unknown repository 'ghost'")

// ---------------------------------------------------------------------------
// External research
// ---------------------------------------------------------------------------

let private withReference (body: string) =
    validManifest.Replace("\"glossary\": [", "\"references\": [ " + body + " ],\n  \"glossary\": [")

let private validReference =
    """{ "id": "R-1", "title": "A study", "author": "Someone", "year": "2025",
        "url": "https://example.invalid/study", "finding": "Something was measured.",
        "limitation": "One sample." }"""

[<Fact>]
let ``a manifest with no references parses`` () =
    let manifest = valueOf (read validManifest)
    Assert.Empty(manifest.References)

[<Fact>]
let ``a complete reference parses`` () =
    let manifest = valueOf (read (withReference validReference))
    Assert.Equal(1, List.length manifest.References)
    Assert.Equal("Someone", (List.head manifest.References).Author)

/// A third-party study cited without its own boundary is being used as
/// authority rather than as evidence, so the limitation is mandatory here for
/// the same reason it is on a metric.
[<Fact>]
let ``a reference without a limitation is rejected`` () =
    let text = withReference (validReference.Replace("\"limitation\": \"One sample.\"", "\"unused\": 0"))
    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "limitation")

[<Fact>]
let ``a reference without a url is rejected`` () =
    let text = withReference (validReference.Replace("\"url\": \"https://example.invalid/study\",", ""))
    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "url")

[<Fact>]
let ``duplicate reference ids are rejected`` () =
    let text = withReference (validReference + ", " + validReference)
    let errors = errorsOf (read text)
    Assert.Contains(errors, fun message -> message.Contains "duplicate reference id 'R-1'")

[<Fact>]
let ``text that is not JSON reports that plainly`` () =
    let errors = errorsOf (read "not json at all")
    Assert.Contains(errors, fun message -> message.Contains "not valid JSON")
