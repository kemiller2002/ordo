/// Turns the typed model and the authored content into HTML.
///
/// Provenance rendering lives here and nowhere else: every metric, claim and
/// experiment reaches the page through one of the functions below, so there is
/// exactly one place that decides what a citation looks like and exactly one
/// place that could ever omit one.
module Ordo.Site.Render

open Ordo.Site.Html
open Ordo.Site.Model
open Ordo.Site.Validation
open Ordo.Site.Content
open Ordo.Site.SiteConfig

type Context =
    { Config: Config
      Manifest: Manifest
      Pages: Page list }

/// Rewrites a site-absolute path to include the deployment prefix. Content is
/// authored against "/" so it reads the same whether the site is served from a
/// domain root or from a GitHub project path.
let link (context: Context) (path: string) : string =
    if path.StartsWith "/" then context.Config.PathPrefix + path else path

// ---------------------------------------------------------------------------
// Provenance
// ---------------------------------------------------------------------------

let private sourceUrl (manifest: Manifest) (source: Source) : string option =
    tryFindRepository manifest source.Repository
    |> Option.map (fun repository ->
        let reference = source.Reference |> Option.defaultValue "HEAD"
        repository.Url + "/blob/" + reference + "/" + source.Path)

let private sourceDescription (manifest: Manifest) (source: Source) : string =
    let repositoryName =
        tryFindRepository manifest source.Repository
        |> Option.map (fun repository -> repository.Name)
        |> Option.defaultValue source.Repository

    let reference =
        source.Reference
        |> Option.map (fun value -> " at " + shortSha value)
        |> Option.defaultValue ""

    let section = source.Section |> Option.map (fun value -> " — " + value) |> Option.defaultValue ""
    repositoryName + " / " + source.Path + reference + section

/// A citation a reader can actually follow. Private repositories are labelled
/// as such before the click rather than after the 404.
let citation (manifest: Manifest) (source: Source) : string =
    let label = sourceDescription manifest source

    let visibility =
        tryFindRepository manifest source.Repository
        |> Option.map (fun repository -> repository.Visibility)
        |> Option.defaultValue "unknown"

    let note =
        if visibility = "private" then
            text "span" [ "class", "src-private" ] "private repository"
        else
            ""

    let body =
        match sourceUrl manifest source with
        | Some url -> element "a" [ "href", url; "rel", "noopener" ] (escape label)
        | None -> escape label

    element "span" [ "class", "src" ] (concat [ text "span" [ "class", "src-key" ] "Source"; body; note ])

// ---------------------------------------------------------------------------
// Metrics
// ---------------------------------------------------------------------------

let private metricAnchor (context: Context) (metric: Metric) : string =
    match tryFindExperiment context.Manifest metric.Experiment with
    | Some experiment -> link context ("/evidence/" + experiment.Slug + "/") + "#" + metric.Id
    | None -> link context "/evidence/"

/// A number in prose. It is always a link to the place the number is defined,
/// and it always carries its provenance class, so a reader never meets a bare
/// figure with no way back to its source.
let metricInline (context: Context) (metric: Metric) : string =
    element
        "a"
        [ "class", "metric-ref"
          "href", metricAnchor context metric
          "title", metric.Label + " — " + provenanceLabel metric.Provenance ]
        (concat
            [ text "span" [ "class", "metric-ref-value" ] metric.Value
              text "span" [ "class", "metric-ref-label" ] metric.Label ])

/// The full record: value, what it measures, how it was collected, what it
/// does not show, and where to check it.
let metricCard (context: Context) (metric: Metric) : string =
    element
        "article"
        [ "class", "metric-card"; "id", metric.Id ]
        (concat
            [ text "p" [ "class", "metric-value" ] metric.Value
              text "h3" [ "class", "metric-label" ] metric.Label
              element
                  "p"
                  [ "class", "metric-provenance" ]
                  (concat
                      [ text
                            "span"
                            [ "class", "badge badge-" + slugify (provenanceLabel metric.Provenance)
                              "title", provenanceMeaning metric.Provenance ]
                            (provenanceLabel metric.Provenance) ])
              definitionList
                  "metric-detail"
                  [ "Measures", metric.Definition
                    "Collected by", metric.Measurement
                    "Does not show", metric.Limitation ]
              citation context.Manifest metric.Source ])

let metricGrid (context: Context) (metrics: Metric list) : string =
    if List.isEmpty metrics then
        ""
    else
        element "div" [ "class", "metric-grid" ] (metrics |> List.map (metricCard context) |> concat)

// ---------------------------------------------------------------------------
// Claims
// ---------------------------------------------------------------------------

/// The evidence-to-engineering map, rendered as data rather than retyped. The
/// "Do not conclude" column is not an appendix: it is the column that makes
/// the rest of the table safe to read.
let claimsTable (context: Context) (claims: Claim list) : string =
    let row (claim: Claim) =
        element
            "tr"
            [ "id", claim.Id ]
            (concat
                [ text "th" [ "scope", "row" ] claim.Proposition
                  element
                      "td"
                      []
                      (text
                          "span"
                          [ "class", "badge badge-state badge-" + evidenceStateSlug claim.State ]
                          (evidenceStateLabel claim.State))
                  text "td" [] (confidenceLabel claim.Confidence)
                  element
                      "td"
                      [ "class", "records" ]
                      (claim.Records |> List.map (fun record -> text "code" [] record) |> String.concat " ")
                  text "td" [ "class", "misses" ] claim.Limitation
                  element "td" [ "class", "cell-src" ] (citation context.Manifest claim.Source) ])

    let head =
        element
            "tr"
            []
            (concat
                [ text "th" [ "scope", "col" ] "Proposition"
                  text "th" [ "scope", "col" ] "Evidence state"
                  text "th" [ "scope", "col" ] "Confidence class"
                  text "th" [ "scope", "col" ] "Records"
                  text "th" [ "scope", "col" ] "Do not conclude"
                  text "th" [ "scope", "col" ] "Source" ])

    element
        "div"
        [ "class", "table-scroll" ]
        (element
            "table"
            [ "class", "matrix matrix-claims" ]
            (concat
                [ text "caption" [] "Every engineering proposition Ordo rests on, with its current evidence state"
                  element "thead" [] head
                  element "tbody" [] (claims |> List.map row |> concat) ]))

// ---------------------------------------------------------------------------
// External research
// ---------------------------------------------------------------------------

/// An inline citation of independent work: author and year, linked out, with
/// the finding in the title attribute. Deliberately lighter than `citation`,
/// which carries a repository path — this is someone else's study, not our
/// artifact, and it should read like a citation rather than a provenance trail.
let referenceInline (reference: Reference) : string =
    element
        "a"
        [ "class", "ref"
          "href", reference.Url
          "rel", "noopener"
          "title", reference.Title + " — " + reference.Finding ]
        (escape (reference.Author + " " + reference.Year))

/// Independent research, with what each study does and does not establish.
let referenceTable (context: Context) : string =
    let row (reference: Reference) =
        element
            "tr"
            [ "id", reference.Id ]
            (concat
                [ element
                      "th"
                      [ "scope", "row" ]
                      (element
                          "a"
                          [ "href", reference.Url; "rel", "noopener" ]
                          (escape reference.Title))
                  text "td" [] (reference.Author + ", " + reference.Year)
                  text "td" [] reference.Finding
                  text "td" [ "class", "misses" ] reference.Limitation ])

    let head =
        element
            "tr"
            []
            (concat
                [ text "th" [ "scope", "col" ] "Study"
                  text "th" [ "scope", "col" ] "Source"
                  text "th" [ "scope", "col" ] "Finding"
                  text "th" [ "scope", "col" ] "Does not establish" ])

    element
        "div"
        [ "class", "table-scroll" ]
        (element
            "table"
            [ "class", "matrix matrix-refs" ]
            (concat
                [ text "caption" [] "Independent research this site relies on"
                  element "thead" [] head
                  element "tbody" [] (context.Manifest.References |> List.map row |> concat) ]))

// ---------------------------------------------------------------------------
// Experiments
// ---------------------------------------------------------------------------

/// A section that is omitted entirely rather than rendered empty.
let private sectionOrNothing (className: string) (body: string) : string =
    if body.Trim().Length = 0 then ""
    elif className.Length = 0 then element "section" [] body
    else element "section" [ "class", className ] body

let private experimentPath (context: Context) (experiment: Experiment) =
    link context ("/evidence/" + experiment.Slug + "/")

let experimentIndex (context: Context) : string =
    let row (experiment: Experiment) =
        let repositoryName =
            tryFindRepository context.Manifest experiment.Repository
            |> Option.map (fun repository -> repository.Name)
            |> Option.defaultValue experiment.Repository

        element
            "tr"
            []
            (concat
                [ element
                      "th"
                      [ "scope", "row" ]
                      (element "a" [ "href", experimentPath context experiment ] (escape experiment.Title))
                  text "td" [] experiment.Status
                  element "td" [] (text "code" [] repositoryName)
                  text "td" [] (string (List.length (metricsFor context.Manifest experiment.Id)))
                  text "td" [] experiment.Summary ])

    let head =
        element
            "tr"
            []
            (concat
                [ text "th" [ "scope", "col" ] "Experiment"
                  text "th" [ "scope", "col" ] "Status"
                  text "th" [ "scope", "col" ] "Repository"
                  text "th" [ "scope", "col" ] "Metrics"
                  text "th" [ "scope", "col" ] "What it examined" ])

    element
        "div"
        [ "class", "table-scroll" ]
        (element
            "table"
            [ "class", "matrix matrix-experiments" ]
            (concat
                [ text "caption" [] "Every experiment this site draws on"
                  element "thead" [] head
                  element "tbody" [] (context.Manifest.Experiments |> List.map row |> concat) ]))

let repositoryTable (context: Context) : string =
    let row (repository: Repository) =
        element
            "tr"
            []
            (concat
                [ element
                      "th"
                      [ "scope", "row" ]
                      (element "a" [ "href", repository.Url; "rel", "noopener" ] (element "code" [] (escape repository.Name)))
                  text "td" [] repository.Visibility
                  text "td" [] repository.Role ])

    let head =
        element
            "tr"
            []
            (concat
                [ text "th" [ "scope", "col" ] "Repository"
                  text "th" [ "scope", "col" ] "Visibility"
                  text "th" [ "scope", "col" ] "Role in the evidence base" ])

    element
        "div"
        [ "class", "table-scroll" ]
        (element
            "table"
            [ "class", "matrix" ]
            (concat
                [ text "caption" [] "Repositories cited by this site"
                  element "thead" [] head
                  element "tbody" [] (context.Manifest.Repositories |> List.map row |> concat) ]))

let glossaryList (context: Context) : string =
    let entry (item: GlossaryEntry) =
        element
            "div"
            [ "class", "glossary-entry"; "id", "term-" + slugify item.Term ]
            (concat
                [ text "h3" [] item.Term
                  text "p" [ "class", "glossary-origin" ] item.Origin
                  text "p" [] item.Definition
                  item.Source |> Option.map (citation context.Manifest) |> Option.defaultValue "" ])

    element
        "div"
        [ "class", "glossary" ]
        (context.Manifest.Glossary
         |> List.sortWith (fun left right -> System.String.CompareOrdinal(left.Term, right.Term))
         |> List.map entry
         |> concat)

/// A generated experiment page. Findings, contradictions, anomalies and
/// limitations are separate sections on purpose: collapsing them into one
/// narrative is how a research record turns into an advertisement.
let experimentPage (context: Context) (experiment: Experiment) (narrative: string) : string =
    let repositoryName =
        tryFindRepository context.Manifest experiment.Repository
        |> Option.map (fun repository -> repository.Name)
        |> Option.defaultValue experiment.Repository

    let optionalRow (label: string) (value: string option) =
        value |> Option.map (fun item -> (label, item)) |> Option.toList

    let identity =
        definitionList
            "experiment-identity"
            ([ ("Status", experiment.Status); ("Repository", repositoryName) ]
             @ optionalRow "Branch" experiment.Branch
             @ optionalRow "Baseline tag" experiment.BaselineTag
             @ optionalRow "Baseline commit" (experiment.BaselineSha |> Option.map shortSha)
             @ optionalRow "Ending commit" (experiment.EndingSha |> Option.map shortSha)
             @ optionalRow "Executed by" experiment.Agent)

    let metrics = metricsFor context.Manifest experiment.Id

    join
        [ element
              "section"
              [ "class", "page-intro" ]
              (concat
                  [ text "p" [ "class", "eyebrow" ] "Evidence"
                    text "h1" [] experiment.Title
                    text "p" [ "class", "lead" ] experiment.Summary ])
          element "section" [] (join [ text "h2" [ "id", "identity" ] "What this was"; identity ])
          element
              "section"
              []
              (join
                  [ text "h2" [ "id", "objective" ] "Objective"
                    paragraph experiment.Objective
                    (experiment.Hypothesis
                     |> Option.map (fun value ->
                         join [ text "h3" [] "Hypothesis"; paragraph value ])
                     |> Option.defaultValue "") ])
          element "section" [] (join [ text "h2" [ "id", "method" ] "Method"; paragraph experiment.Method ])
          element
              "section"
              []
              (join
                  [ text "h2" [ "id", "metrics" ] "Recorded metrics"
                    (if List.isEmpty metrics then
                         paragraph
                             "No quantitative metric from this experiment met this site's publication rule, which requires a definition, a collection method, a stated limitation and a resolvable source artifact."
                     else
                         metricGrid context metrics) ])
          sectionOrNothing "" (sectionWhen "Findings" "findings" (textList "findings" experiment.Findings))
          sectionOrNothing
              "contradictions"
              (sectionWhen "What this refuted" "contradictions" (textList "contradictions" experiment.Contradictions))
          sectionOrNothing
              ""
              (sectionWhen "Anomalies, failures and confounders" "anomalies" (textList "anomalies" experiment.Anomalies))
          element
              "section"
              [ "class", "limitations" ]
              (join [ text "h2" [ "id", "limitations" ] "Limitations"; textList "limits" experiment.Limitations ])
          (if narrative.Trim().Length = 0 then
               ""
           else
               element "section" [ "class", "interpretation" ] narrative)
          element
              "section"
              []
              (join
                  [ text "h2" [ "id", "sources" ] "Source artifacts"
                    unorderedList "source-list" (experiment.Sources |> List.map (citation context.Manifest)) ]) ]

// ---------------------------------------------------------------------------
// Tokens
// ---------------------------------------------------------------------------

let private splitIds (argument: string) =
    argument.Split ','
    |> Array.toList
    |> List.map (fun item -> item.Trim())
    |> List.filter (fun item -> item.Length > 0)

let private resolveMetrics (context: Context) (argument: string) : Validation<Metric list> =
    splitIds argument
    |> traverse (fun id ->
        match tryFindMetric context.Manifest id with
        | Some metric -> ok metric
        | None -> error ("unknown metric id '" + id + "'"))

let private pagesInSection (context: Context) (section: string) =
    context.Pages
    |> List.filter (fun page -> page.Section = section)
    |> List.sortBy (fun page -> (page.Order, page.Path))

let private sectionIndex (context: Context) (section: string) : string =
    let card (page: Page) =
        element
            "a"
            [ "class", "card card-link"; "href", link context page.Path ]
            (concat [ text "h3" [] page.Title; text "p" [] page.Summary ])

    match pagesInSection context section with
    | [] -> ""
    | pages -> element "div" [ "class", "grid" ] (pages |> List.map card |> concat)

/// Resolves one `{{token}}`. An unknown token name, or a known name with an
/// unknown argument, fails the build.
let resolveToken (context: Context) (token: string) : Validation<string> =
    let name, argument =
        match token.IndexOf ':' with
        | index when index >= 0 -> token.Substring(0, index).Trim(), token.Substring(index + 1).Trim()
        | _ -> token.Trim(), ""

    match name with
    | "metric" ->
        resolveMetrics context argument
        |> bind (fun metrics ->
            match metrics with
            | [ metric ] -> ok (metricInline context metric)
            | _ -> error "'metric' takes exactly one id; use 'metrics' for several")
    | "metrics" -> resolveMetrics context argument |> map (metricGrid context)
    | "claims" ->
        let claims =
            if argument.Length = 0 then
                context.Manifest.Claims
            else
                let wanted = splitIds argument |> Set.ofList
                context.Manifest.Claims |> List.filter (fun claim -> Set.contains claim.Id wanted)

        if argument.Length > 0 && List.isEmpty claims then
            error ("no claim matched '" + argument + "'")
        else
            ok (claimsTable context claims)
    | "ref" ->
        (match tryFindReference context.Manifest argument with
         | Some reference -> ok (referenceInline reference)
         | None -> error ("unknown reference id '" + argument + "'"))
    | "references" ->
        if List.isEmpty context.Manifest.References then
            error "'references' was used but the manifest holds none"
        else
            ok (referenceTable context)
    | "experiments" -> ok (experimentIndex context)
    | "repositories" -> ok (repositoryTable context)
    | "glossary" -> ok (glossaryList context)
    | "section" ->
        if List.isEmpty (pagesInSection context argument) then
            error ("no pages in section '" + argument + "'")
        else
            ok (sectionIndex context argument)
    | "diagram" ->
        (match argument with
         | "transition" -> ok (Diagrams.transition ())
         | "engineering" -> ok (Diagrams.engineering ())
         | "verification" -> ok (Diagrams.verification ())
         | "lifecycle" -> ok (Diagrams.lifecycle ())
         | other -> error ("unknown diagram '" + other + "'"))
    | "link" ->
        if argument.StartsWith "/" then
            ok (link context argument)
        else
            error ("'link' needs a site-absolute path, got '" + argument + "'")
    | other -> error ("unknown token '" + other + "'")

// ---------------------------------------------------------------------------
// Layout
// ---------------------------------------------------------------------------

let private navigation (context: Context) (currentPath: string) : string =
    let item (nav: NavItem) =
        let isCurrent =
            currentPath = nav.Path
            || (nav.Path <> "/" && currentPath.StartsWith nav.Path)

        let attributes =
            if isCurrent then
                [ "href", link context nav.Path; "aria-current", "page" ]
            else
                [ "href", link context nav.Path ]

        element "a" attributes (escape nav.Label)

    element "nav" [ "aria-label", "Primary" ] (context.Config.Nav |> List.map item |> concat)

let private header (context: Context) (currentPath: string) : string =
    element
        "header"
        [ "class", "site-header" ]
        (element
            "div"
            [ "class", "nav-shell" ]
            (concat
                [ element
                      "div"
                      [ "class", "brand" ]
                      (concat
                          [ element
                                "a"
                                [ "class", "brand-link"; "href", link context "/" ]
                                (concat
                                    [ text "span" [ "class", "brand-mark"; "aria-hidden", "true" ] "EF"
                                      text "span" [ "class", "brand-name" ] context.Config.Name ])
                            text "span" [ "class", "brand-subtitle" ] context.Config.Tagline ])
                  navigation context currentPath ]))

let private footer (context: Context) : string =
    element
        "footer"
        [ "class", "site-footer" ]
        (concat
            [ element
                  "div"
                  [ "class", "footer-grid" ]
                  (concat
                      [ element
                            "div"
                            []
                            (concat
                                [ text "p" [ "class", "eyebrow" ] "Echelon / Foundry"
                                  text "p" [] context.Config.FooterNote ])
                        element
                            "div"
                            [ "class", "footer-actions" ]
                            (concat
                                [ element
                                      "a"
                                      [ "class", "button ghost"; "href", context.Config.SourceUrl; "rel", "noopener" ]
                                      "Read the source repository"
                                  element
                                      "a"
                                      [ "class", "muted-link"; "href", link context "/llms.txt" ]
                                      "llms.txt" ]) ])
              element
                  "p"
                  [ "class", "footer-note" ]
                  (concat
                      [ escape context.Config.Copyright
                        text "span" [] "Indianapolis, Indiana" ]) ])

/// Wraps rendered page content in the document shell.
let document (context: Context) (page: Page) (body: string) : string =
    let canonical = context.Config.BaseUrl + context.Config.PathPrefix + page.Path
    let title = if page.Path = "/" then page.Title else page.Title + " | " + context.Config.Name

    join
        [ "<!DOCTYPE html>"
          "<html lang=\"en\">"
          "<head>"
          "<meta charset=\"utf-8\" />"
          "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />"
          text "title" [] title
          void' "meta" [ "name", "description"; "content", page.Description ]
          void' "link" [ "rel", "canonical"; "href", canonical ]
          void' "meta" [ "property", "og:type"; "content", "website" ]
          void' "meta" [ "property", "og:title"; "content", title ]
          void' "meta" [ "property", "og:description"; "content", page.Description ]
          void' "meta" [ "property", "og:url"; "content", canonical ]
          void' "meta" [ "property", "og:site_name"; "content", context.Config.Name ]
          void' "meta" [ "name", "twitter:card"; "content", "summary" ]
          // The same three families the Echelon Foundry site loads, from the
          // same source, so the two sites render as one design system rather
          // than as a near-match. Fallbacks are declared in the stylesheet, so
          // the page is fully readable if the font request fails.
          void' "link" [ "rel", "preconnect"; "href", "https://fonts.googleapis.com" ]
          "<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin />"
          void'
              "link"
              [ "rel", "stylesheet"
                "href",
                "https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&family=Manrope:wght@400;500;600;700&family=Newsreader:opsz,wght@6..72,500;6..72,650&display=swap" ]
          void' "link" [ "rel", "stylesheet"; "href", link context "/assets/css/ordo.css" ]
          void' "link" [ "rel", "icon"; "href", link context "/assets/mark.svg"; "type", "image/svg+xml" ]
          "</head>"
          "<body>"
          element "a" [ "class", "skip-link"; "href", "#main-content" ] "Skip to main content"
          header context page.Path
          element "main" [ "id", "main-content" ] body
          footer context
          "</body>"
          "</html>"
          "" ]
