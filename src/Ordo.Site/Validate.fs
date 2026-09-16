/// The gate between a generated site and a deployed one.
///
/// Every check here runs against the bytes that would actually be published,
/// in memory, before anything is written or uploaded. A failure is a failed
/// build, not a warning: a site whose whole argument is "check our numbers"
/// cannot ship a broken citation.
module Ordo.Site.Validate

open System
open System.Text.RegularExpressions
open Ordo.Site.Validation
open Ordo.Site.SiteConfig

/// One file as it would be written to `dist/`.
type Output = { Path: string; Text: string }

let private isHtml (output: Output) = output.Path.EndsWith ".html"

// ---------------------------------------------------------------------------
// Links
// ---------------------------------------------------------------------------

let private hrefPattern = Regex("(?:href|src)=\"([^\"]*)\"", RegexOptions.Compiled)
let private idPattern = Regex("\\sid=\"([^\"]*)\"", RegexOptions.Compiled)
let private headingPattern = Regex("<h([1-6])[\\s>]", RegexOptions.Compiled)
let private titlePattern = Regex("<title>([^<]*)</title>", RegexOptions.Compiled)

/// Maps a site link onto the file that would have to exist for it to resolve.
let private targetFile (config: Config) (href: string) : string option =
    let path = href.Split('#').[0]

    if path.Length = 0 then
        None
    elif not (path.StartsWith "/") then
        None
    else
        let withoutPrefix =
            if config.PathPrefix.Length > 0 && path.StartsWith config.PathPrefix then
                path.Substring config.PathPrefix.Length
            else
                path

        let trimmed = withoutPrefix.TrimStart '/'

        if withoutPrefix.EndsWith "/" then
            Some(trimmed + "index.html")
        else
            Some trimmed

let private isExternal (href: string) =
    href.StartsWith "http://"
    || href.StartsWith "https://"
    || href.StartsWith "mailto:"
    || href.StartsWith "#"

let private anchorsIn (content: string) =
    idPattern.Matches content
    |> Seq.map (fun m -> m.Groups.[1].Value)
    |> Set.ofSeq

/// Internal links must resolve to a generated file, and a fragment must
/// resolve to an id on the page it points at — including fragments that point
/// at another page, which is where citation links break silently.
let private checkLinks (config: Config) (outputs: Output list) (assets: string list) : Validation<unit> =
    let generated = outputs |> List.map (fun output -> output.Path) |> Set.ofList
    let available = Set.union generated (Set.ofList assets)

    let anchorIndex =
        outputs
        |> List.filter isHtml
        |> List.map (fun output -> output.Path, anchorsIn output.Text)
        |> Map.ofList

    let checkOne (output: Output) (href: string) =
        if isExternal href then
            []
        else
            match targetFile config href with
            | None -> [ output.Path + ": link '" + href + "' is neither site-absolute nor external" ]
            | Some file ->
                if not (Set.contains file available) then
                    [ output.Path + ": link '" + href + "' resolves to '" + file + "', which is not generated" ]
                else
                    let parts = href.Split '#'

                    if parts.Length > 1 && parts.[1].Length > 0 then
                        (match Map.tryFind file anchorIndex with
                         | Some anchors when Set.contains parts.[1] anchors -> []
                         | Some _ -> [ output.Path + ": link '" + href + "' points at an id that '" + file + "' does not define" ]
                         | None -> [])
                    else
                        []

    let failures =
        outputs
        |> List.filter isHtml
        |> List.collect (fun output ->
            hrefPattern.Matches output.Text
            |> Seq.toList
            |> List.collect (fun m -> checkOne output m.Groups.[1].Value))

    if List.isEmpty failures then ok () else Error failures

// ---------------------------------------------------------------------------
// Structure
// ---------------------------------------------------------------------------

let private checkStructure (outputs: Output list) : Validation<unit> =
    let checkOne (output: Output) =
        let content = output.Text

        let levels =
            headingPattern.Matches content
            |> Seq.map (fun m -> Int32.Parse(m.Groups.[1].Value, Globalization.CultureInfo.InvariantCulture))
            |> Seq.toList

        let h1Count = levels |> List.filter (fun level -> level = 1) |> List.length

        let skips =
            levels
            |> List.pairwise
            |> List.filter (fun (previous, next) -> next > previous + 1)
            |> List.map (fun (previous, next) ->
                output.Path
                + ": heading level jumps from h"
                + string previous
                + " to h"
                + string next)

        [ (if content.StartsWith "<!DOCTYPE html>" then [] else [ output.Path + ": missing doctype" ])
          (if content.Contains "<html lang=\"en\">" then [] else [ output.Path + ": <html> has no lang attribute" ])
          (if titlePattern.IsMatch content then [] else [ output.Path + ": no <title>" ])
          (if content.Contains "name=\"description\"" then
               []
           else
               [ output.Path + ": no meta description" ])
          (if h1Count = 1 then
               []
           else
               [ output.Path + ": expected exactly one h1, found " + string h1Count ])
          (if content.Contains "id=\"main-content\"" then
               []
           else
               [ output.Path + ": no main landmark" ])
          skips ]
        |> List.concat

    let failures = outputs |> List.filter isHtml |> List.collect checkOne
    if List.isEmpty failures then ok () else Error failures

// ---------------------------------------------------------------------------
// Content hygiene
// ---------------------------------------------------------------------------

let private placeholderPattern =
    Regex("\\b(TODO|TBD|FIXME|XXX|lorem ipsum|coming soon|placeholder text)\\b", RegexOptions.IgnoreCase ||| RegexOptions.Compiled)

/// Shapes of credential that must never reach a published page. This is a
/// backstop, not a substitute for not putting secrets in a repository.
let private secretPatterns =
    [ "GitHub token", Regex("\\bgh[pousr]_[A-Za-z0-9]{16,}")
      "AWS access key", Regex("\\bAKIA[0-9A-Z]{16}\\b")
      "private key block", Regex("-----BEGIN [A-Z ]*PRIVATE KEY-----")
      "Slack token", Regex("\\bxox[baprs]-[A-Za-z0-9-]{10,}")
      "API secret key", Regex("\\bsk-[A-Za-z0-9]{24,}") ]

let private checkHygiene (outputs: Output list) : Validation<unit> =
    let placeholders =
        outputs
        |> List.collect (fun output ->
            placeholderPattern.Matches output.Text
            |> Seq.map (fun m -> output.Path + ": placeholder text '" + m.Value + "' left in published output")
            |> Seq.toList)

    let secrets =
        outputs
        |> List.collect (fun output ->
            secretPatterns
            |> List.filter (fun (_, pattern) -> pattern.IsMatch output.Text)
            |> List.map (fun (label, _) -> output.Path + ": looks like a " + label))

    match placeholders @ secrets with
    | [] -> ok ()
    | failures -> Error failures

// ---------------------------------------------------------------------------
// Required output
// ---------------------------------------------------------------------------

let requiredFiles =
    [ "index.html"
      "results/index.html"
      "evidence/index.html"
      "research/index.html"
      "glossary/index.html"
      "data/evidence.json"
      "data/glossary.json"
      "data/index.json"
      "llms.txt"
      "sitemap.xml"
      "robots.txt" ]

let private checkRequired (outputs: Output list) : Validation<unit> =
    let generated = outputs |> List.map (fun output -> output.Path) |> Set.ofList

    let missing =
        requiredFiles
        |> List.filter (fun file -> not (Set.contains file generated))
        |> List.map (fun file -> "required output '" + file + "' was not generated")

    if List.isEmpty missing then ok () else Error missing

/// Published JSON has to parse. A manifest the site cannot read back is a
/// manifest no agent can read either.
let private checkJson (outputs: Output list) : Validation<unit> =
    let failures =
        outputs
        |> List.filter (fun output -> output.Path.EndsWith ".json")
        |> List.choose (fun output ->
            match Sde.Core.Json.parse output.Text with
            | Ok _ -> None
            | Error message -> Some(output.Path + ": generated JSON does not parse — " + message))

    if List.isEmpty failures then ok () else Error failures

/// Runs every check and reports every failure from all of them, so one bad
/// link does not hide a missing page.
let run (config: Config) (outputs: Output list) (assets: string list) : Validation<unit> =
    [ checkRequired outputs
      checkLinks config outputs assets
      checkStructure outputs
      checkHygiene outputs
      checkJson outputs ]
    |> sequence
    |> map ignore
