/// The publication gate. Each test breaks exactly one thing in an otherwise
/// publishable site and asserts that the build refuses it.
module Ordo.Site.Tests.ValidateTests

open Xunit
open Ordo.Site
open Ordo.Site.SiteConfig
open Ordo.Site.Validate
open Ordo.Site.Tests.Fixtures

let private config: SiteConfig.Config =
    { Name = "Ordo"
      Tagline = "State-Directed Engineering"
      Description = "A methodology."
      BaseUrl = "https://example.invalid"
      PathPrefix = ""
      CustomDomain = None
      SourceRepository = "owner/repo"
      SourceUrl = "https://example.invalid/owner/repo"
      Nav = [ { Label = "Home"; Path = "/" } ]
      FooterNote = "A note."
      Copyright = "© Echelon Foundry" }

let private page (heading: string) (body: string) =
    String.concat
        "\n"
        [ "<!DOCTYPE html>"
          "<html lang=\"en\">"
          "<head>"
          "<title>A page</title>"
          "<meta name=\"description\" content=\"A page.\" />"
          "</head>"
          "<body>"
          "<main id=\"main-content\">"
          "<h1>" + heading + "</h1>"
          body
          "</main>"
          "</body>"
          "</html>" ]

/// A site that passes every check, used as the baseline each test breaks.
let private publishableSite () =
    [ { Path = "index.html"; Text = page "Ordo" "<p>Hello.</p>" }
      { Path = "results/index.html"; Text = page "Results" "<p>Results.</p>" }
      { Path = "evidence/index.html"; Text = page "Evidence" "<p>Evidence.</p>" }
      { Path = "research/index.html"; Text = page "Research" "<p>Research.</p>" }
      { Path = "glossary/index.html"; Text = page "Glossary" "<p>Glossary.</p>" }
      { Path = "data/evidence.json"; Text = "{}" }
      { Path = "data/glossary.json"; Text = "{}" }
      { Path = "data/index.json"; Text = "{}" }
      { Path = "llms.txt"; Text = "# Ordo" }
      { Path = "sitemap.xml"; Text = "<urlset />" }
      { Path = "robots.txt"; Text = "User-agent: *" } ]

let private check (outputs: Output list) = Validate.run config outputs [ "assets/css/ordo.css" ]

let private replace (path: string) (text: string) (outputs: Output list) =
    outputs
    |> List.map (fun output -> if output.Path = path then { output with Text = text } else output)

[<Fact>]
let ``a complete site passes`` () = Assert.Empty(errorsOf (check (publishableSite ())))

[<Fact>]
let ``a missing required page fails the build`` () =
    let outputs = publishableSite () |> List.filter (fun output -> output.Path <> "glossary/index.html")
    let errors = errorsOf (check outputs)
    Assert.Contains(errors, fun message -> message.Contains "glossary/index.html")

[<Fact>]
let ``a link to a page that was never generated fails the build`` () =
    let outputs =
        publishableSite ()
        |> replace "index.html" (page "Ordo" "<a href=\"/nowhere/\">Nowhere</a>")

    let errors = errorsOf (check outputs)
    Assert.Contains(errors, fun message -> message.Contains "nowhere/index.html")

[<Fact>]
let ``a link to a generated page passes`` () =
    let outputs =
        publishableSite ()
        |> replace "index.html" (page "Ordo" "<a href=\"/evidence/\">Evidence</a>")

    Assert.Empty(errorsOf (check outputs))

/// Citation links carry fragments. A fragment pointing at an id that no page
/// defines is exactly the silent break this site cannot afford.
[<Fact>]
let ``a cross-page fragment that does not exist fails the build`` () =
    let outputs =
        publishableSite ()
        |> replace "index.html" (page "Ordo" "<a href=\"/evidence/#M-999\">A metric</a>")

    let errors = errorsOf (check outputs)
    Assert.Contains(errors, fun message -> message.Contains "M-999")

[<Fact>]
let ``a cross-page fragment that exists passes`` () =
    let outputs =
        publishableSite ()
        |> replace "evidence/index.html" (page "Evidence" "<p id=\"M-1\">A metric.</p>")
        |> replace "index.html" (page "Ordo" "<a href=\"/evidence/#M-1\">A metric</a>")

    Assert.Empty(errorsOf (check outputs))

[<Fact>]
let ``an external link is not checked`` () =
    let outputs =
        publishableSite ()
        |> replace "index.html" (page "Ordo" "<a href=\"https://example.invalid/anything\">Out</a>")

    Assert.Empty(errorsOf (check outputs))

[<Fact>]
let ``a link to a declared asset passes`` () =
    let outputs =
        publishableSite ()
        |> replace "index.html" (page "Ordo" "<img src=\"/assets/css/ordo.css\" alt=\"\" />")

    Assert.Empty(errorsOf (check outputs))

[<Fact>]
let ``placeholder text left in a page fails the build`` () =
    let outputs = publishableSite () |> replace "index.html" (page "Ordo" "<p>TODO: write this.</p>")
    let errors = errorsOf (check outputs)
    Assert.Contains(errors, fun message -> message.Contains "placeholder text")

[<Fact>]
let ``something shaped like a credential fails the build`` () =
    let outputs =
        publishableSite ()
        |> replace "index.html" (page "Ordo" "<p>ghp_abcdefghijklmnopqrstuvwxyz0123456789</p>")

    let errors = errorsOf (check outputs)
    Assert.Contains(errors, fun message -> message.Contains "GitHub token")

[<Fact>]
let ``two h1 elements on one page fail the build`` () =
    let outputs = publishableSite () |> replace "index.html" (page "Ordo" "<h1>Second</h1>")
    let errors = errorsOf (check outputs)
    Assert.Contains(errors, fun message -> message.Contains "exactly one h1")

[<Fact>]
let ``a skipped heading level fails the build`` () =
    let outputs = publishableSite () |> replace "index.html" (page "Ordo" "<h3>Jumped</h3>")
    let errors = errorsOf (check outputs)
    Assert.Contains(errors, fun message -> message.Contains "heading level jumps")

[<Fact>]
let ``generated JSON that does not parse fails the build`` () =
    let outputs = publishableSite () |> replace "data/evidence.json" "{ not json"
    let errors = errorsOf (check outputs)
    Assert.Contains(errors, fun message -> message.Contains "does not parse")

[<Fact>]
let ``a page with no title fails the build`` () =
    let outputs = publishableSite () |> replace "index.html" "<!DOCTYPE html>\n<html lang=\"en\"><body><main id=\"main-content\"><h1>x</h1></main></body></html>"
    let errors = errorsOf (check outputs)
    Assert.Contains(errors, fun message -> message.Contains "no <title>")

/// Every failure from every check, in one run. Fixing a site one message at a
/// time is how a five-minute correction becomes an afternoon.
[<Fact>]
let ``unrelated failures are all reported together`` () =
    let outputs =
        publishableSite ()
        |> List.filter (fun output -> output.Path <> "robots.txt")
        |> replace "index.html" (page "Ordo" "<p>TBD</p><a href=\"/missing/\">x</a>")

    let errors = errorsOf (check outputs)
    Assert.Contains(errors, fun message -> message.Contains "robots.txt")
    Assert.Contains(errors, fun message -> message.Contains "placeholder text")
    Assert.Contains(errors, fun message -> message.Contains "missing/index.html")
