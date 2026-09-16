/// The deployment target. These tests exist because the first published build
/// got this wrong: the site was generated for a project-page path prefix while
/// GitHub Pages served it from a custom domain's root, so every internal link
/// and the stylesheet resolved to nothing. Nothing in the build objected,
/// because nothing checked that the three fields describing one fact agreed.
module Ordo.Site.Tests.SiteConfigTests

open Xunit
open Ordo.Site
open Ordo.Site.Tests.Fixtures

let private config (baseUrl: string) (pathPrefix: string) (customDomain: string) =
    let domain =
        if customDomain = "" then
            ""
        else
            "\"customDomain\": \"" + customDomain + "\","

    "{\n"
    + "  \"name\": \"Ordo\",\n"
    + "  \"tagline\": \"t\",\n"
    + "  \"description\": \"d\",\n"
    + "  \"baseUrl\": \"" + baseUrl + "\",\n"
    + "  \"pathPrefix\": \"" + pathPrefix + "\",\n"
    + "  " + domain + "\n"
    + "  \"sourceRepository\": \"owner/repo\",\n"
    + "  \"sourceUrl\": \"https://example.invalid/owner/repo\",\n"
    + "  \"footerNote\": \"f\",\n"
    + "  \"copyright\": \"c\",\n"
    + "  \"nav\": [ { \"label\": \"Home\", \"path\": \"/\" } ]\n"
    + "}"

[<Fact>]
let ``a project-page config with no custom domain is accepted`` () =
    let value = valueOf (SiteConfig.read (config "https://owner.github.io" "/repo" ""))
    Assert.Equal("/repo", value.PathPrefix)
    Assert.Equal<string option>(None, value.CustomDomain)

[<Fact>]
let ``a custom-domain config served from the root is accepted`` () =
    let value =
        valueOf (SiteConfig.read (config "https://ordo.example.invalid" "" "ordo.example.invalid"))

    Assert.Equal("", value.PathPrefix)
    Assert.Equal<string option>(Some "ordo.example.invalid", value.CustomDomain)

/// This is the exact shape of the first deployment's defect.
[<Fact>]
let ``a custom domain with a path prefix is rejected`` () =
    let errors = errorsOf (SiteConfig.read (config "https://ordo.example.invalid" "/repo" "ordo.example.invalid"))
    Assert.Contains(errors, fun message -> message.Contains "pathPrefix" && message.Contains "root")

[<Fact>]
let ``a base URL that disagrees with the custom domain is rejected`` () =
    let errors = errorsOf (SiteConfig.read (config "https://owner.github.io" "" "ordo.example.invalid"))
    Assert.Contains(errors, fun message -> message.Contains "baseUrl" && message.Contains "ordo.example.invalid")

/// Both halves of the disagreement are reported together, so a single run
/// tells you everything that has to change.
[<Fact>]
let ``both disagreements are reported in one run`` () =
    let errors = errorsOf (SiteConfig.read (config "https://owner.github.io" "/repo" "ordo.example.invalid"))
    Assert.Contains(errors, fun message -> message.Contains "baseUrl")
    Assert.Contains(errors, fun message -> message.Contains "pathPrefix")

[<Fact>]
let ``a path prefix that does not start with a slash is rejected`` () =
    let errors = errorsOf (SiteConfig.read (config "https://owner.github.io" "repo" ""))
    Assert.Contains(errors, fun message -> message.Contains "pathPrefix")
