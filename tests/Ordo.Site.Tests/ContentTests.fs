/// Front matter and token substitution: the two places where an authoring
/// mistake could otherwise reach a published page.
module Ordo.Site.Tests.ContentTests

open Xunit
open Ordo.Site
open Ordo.Site.Validation
open Ordo.Site.Tests.Fixtures

let private document =
    String.concat
        "\n"
        [ "---"
          "path: /concepts/state/"
          "title: Explicit domain state"
          "description: What explicit state means."
          "section: Concepts"
          "order: 10"
          "summary: A named state instead of an inferred combination of flags."
          "---"
          "<section><h1>State</h1></section>" ]

[<Fact>]
let ``a well-formed content file parses`` () =
    let page = valueOf (Content.parseDocument "state.html" document)
    Assert.Equal("/concepts/state/", page.Path)
    Assert.Equal("Concepts", page.Section)
    Assert.Equal(10, page.Order)
    Assert.Contains("<h1>State</h1>", page.Body)

[<Fact>]
let ``a missing front matter key names the key and the file`` () =
    let text = document.Replace("section: Concepts\n", "")
    let errors = errorsOf (Content.parseDocument "state.html" text)
    Assert.Contains(errors, fun message -> message.Contains "state.html" && message.Contains "'section'")

[<Fact>]
let ``a relative path is rejected`` () =
    let text = document.Replace("path: /concepts/state/", "path: concepts/state/")
    let errors = errorsOf (Content.parseDocument "state.html" text)
    Assert.Contains(errors, fun message -> message.Contains "must start with")

[<Fact>]
let ``a path that is not directory-shaped is rejected`` () =
    let text = document.Replace("path: /concepts/state/", "path: /concepts/state.html")
    let errors = errorsOf (Content.parseDocument "state.html" text)
    Assert.Contains(errors, fun message -> message.Contains "must end with")

[<Fact>]
let ``a non-numeric order is rejected`` () =
    let text = document.Replace("order: 10", "order: soon")
    let errors = errorsOf (Content.parseDocument "state.html" text)
    Assert.Contains(errors, fun message -> message.Contains "must be an integer")

[<Fact>]
let ``a file with no front matter is rejected`` () =
    let errors = errorsOf (Content.parseDocument "state.html" "<p>hello</p>")
    Assert.Contains(errors, fun message -> message.Contains "front matter")

// ---------------------------------------------------------------------------
// Tokens
// ---------------------------------------------------------------------------

let private resolver (token: string) : Validation<string> =
    if token = "known" then ok "<b>value</b>" else error ("unknown token '" + token + "'")

[<Fact>]
let ``a known token is replaced in place`` () =
    let result = valueOf (Content.substitute resolver "before {{known}} after")
    Assert.Equal("before <b>value</b> after", result)

[<Fact>]
let ``text with no tokens is returned unchanged`` () =
    Assert.Equal("plain text", valueOf (Content.substitute resolver "plain text"))

/// This is the rule that makes the site's provenance guarantee mechanical: a
/// reference that does not resolve is a failed build, not a rendered blank.
[<Fact>]
let ``an unresolvable token fails the build`` () =
    let errors = errorsOf (Content.substitute resolver "text {{missing}} more")
    Assert.Contains(errors, fun message -> message.Contains "unknown token 'missing'")

[<Fact>]
let ``every unresolvable token is reported, not just the first`` () =
    let errors = errorsOf (Content.substitute resolver "{{one}} and {{two}}")
    Assert.Equal(2, List.length errors)

[<Fact>]
let ``an unterminated token is an error rather than silent passthrough`` () =
    let errors = errorsOf (Content.substitute resolver "text {{never closed")
    Assert.Contains(errors, fun message -> message.Contains "unterminated")
