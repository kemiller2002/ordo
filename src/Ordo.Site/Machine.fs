/// Machine-readable outputs, all derived from the same typed model the HTML is
/// rendered from.
///
/// Nothing here is a second, hand-maintained copy of the site's content. Each
/// artifact is a projection, so an agent reading `/data/evidence.json` and a
/// person reading `/evidence/` cannot disagree.
module Ordo.Site.Machine

open Sde.Core.Json
open Ordo.Site.Model
open Ordo.Site.Content
open Ordo.Site.SiteConfig

let private optional (name: string) (value: string option) : (string * JsonValue) list =
    match value with
    | Some text -> [ name, JString text ]
    | None -> []

let private sourceJson (source: Source) : JsonValue =
    JObject(
        [ "repository", JString source.Repository; "path", JString source.Path ]
        @ optional "ref" source.Reference
        @ optional "section" source.Section
    )

let private provenanceName (provenance: Provenance) =
    match provenance with
    | Instrumented -> "instrumented"
    | SelfReported -> "self-reported"
    | Derived -> "derived"
    | NotObservable -> "not-observable"

let private stateName (state: EvidenceState) =
    match state with
    | Supported -> "supported"
    | Provisional -> "provisional"
    | Contradicted -> "contradicted"
    | OpenQuestion -> "open"

let private confidenceName (confidence: ConfidenceClass) =
    match confidence with
    | Required -> "required"
    | Recommended -> "recommended"
    | Experimental -> "experimental"
    | ResearchOnly -> "research-only"
    | Deprecated -> "deprecated"
    | Unclassified -> "unclassified"

let private metricJson (basePath: string) (manifest: Manifest) (metric: Metric) : JsonValue =
    let anchor =
        tryFindExperiment manifest metric.Experiment
        |> Option.map (fun experiment -> basePath + "/evidence/" + experiment.Slug + "/#" + metric.Id)
        |> Option.defaultValue (basePath + "/evidence/")

    JObject
        [ "id", JString metric.Id
          "label", JString metric.Label
          "value", JString metric.Value
          "definition", JString metric.Definition
          "measurement", JString metric.Measurement
          "experiment", JString metric.Experiment
          "provenance", JString(provenanceName metric.Provenance)
          "limitation", JString metric.Limitation
          "source", sourceJson metric.Source
          "page", JString anchor ]

let private experimentJson (basePath: string) (manifest: Manifest) (experiment: Experiment) : JsonValue =
    JObject(
        [ "id", JString experiment.Id
          "slug", JString experiment.Slug
          "title", JString experiment.Title
          "status", JString experiment.Status
          "summary", JString experiment.Summary
          "repository", JString experiment.Repository ]
        @ optional "branch" experiment.Branch
        @ optional "baselineTag" experiment.BaselineTag
        @ optional "baselineSha" experiment.BaselineSha
        @ optional "endingSha" experiment.EndingSha
        @ optional "agent" experiment.Agent
        @ [ "objective", JString experiment.Objective ]
        @ optional "hypothesis" experiment.Hypothesis
        @ [ "method", JString experiment.Method
            "findings", JArray(experiment.Findings |> List.map JString)
            "contradictions", JArray(experiment.Contradictions |> List.map JString)
            "anomalies", JArray(experiment.Anomalies |> List.map JString)
            "limitations", JArray(experiment.Limitations |> List.map JString)
            "sources", JArray(experiment.Sources |> List.map sourceJson)
            "metrics", JArray(metricsFor manifest experiment.Id |> List.map (fun metric -> JString metric.Id))
            "page", JString(basePath + "/evidence/" + experiment.Slug + "/") ]
    )

let private claimJson (claim: Claim) : JsonValue =
    JObject
        [ "id", JString claim.Id
          "proposition", JString claim.Proposition
          "confidence", JString(confidenceName claim.Confidence)
          "state", JString(stateName claim.State)
          "records", JArray(claim.Records |> List.map JString)
          "limitation", JString claim.Limitation
          "source", sourceJson claim.Source ]

let private repositoryJson (repository: Repository) : JsonValue =
    JObject
        [ "id", JString repository.Id
          "name", JString repository.Name
          "url", JString repository.Url
          "visibility", JString repository.Visibility
          "role", JString repository.Role ]

let private glossaryJson (entry: GlossaryEntry) : JsonValue =
    JObject(
        [ "term", JString entry.Term
          "definition", JString entry.Definition
          "origin", JString entry.Origin ]
        @ (match entry.Source with
           | Some source -> [ "source", sourceJson source ]
           | None -> [])
    )

/// The evidence manifest as published: the same records the pages were
/// rendered from, plus the page each one is rendered on.
let evidenceDocument (config: Config) (manifest: Manifest) : string =
    let basePath = config.PathPrefix

    JObject
        [ "schemaVersion", JString manifest.SchemaVersion
          "site", JString(config.BaseUrl + config.PathPrefix)
          "repositories", JArray(manifest.Repositories |> List.map repositoryJson)
          "experiments", JArray(manifest.Experiments |> List.map (experimentJson basePath manifest))
          "metrics", JArray(manifest.Metrics |> List.map (metricJson basePath manifest))
          "claims", JArray(manifest.Claims |> List.map claimJson) ]
    |> renderDocument

let glossaryDocument (manifest: Manifest) : string =
    JObject [ "terms", JArray(manifest.Glossary |> List.map glossaryJson) ] |> renderDocument

/// A flat page index: path, title, section and one-sentence summary. This is
/// what an agent reads to decide which page to fetch.
let indexDocument (config: Config) (pages: Page list) : string =
    let pageJson (page: Page) =
        JObject
            [ "path", JString(config.PathPrefix + page.Path)
              "url", JString(config.BaseUrl + config.PathPrefix + page.Path)
              "title", JString page.Title
              "section", JString page.Section
              "summary", JString page.Summary ]

    JObject
        [ "name", JString config.Name
          "description", JString config.Description
          "site", JString(config.BaseUrl + config.PathPrefix)
          "pages", JArray(pages |> List.map pageJson) ]
    |> renderDocument

/// llms.txt, following the convention of a title, a one-paragraph summary, and
/// linked sections. Deliberately short: it is a routing document, not a copy of
/// the site.
let llmsText (config: Config) (pages: Page list) (manifest: Manifest) : string =
    let absolute (path: string) = config.BaseUrl + config.PathPrefix + path

    let sections =
        pages
        |> List.map (fun page -> page.Section)
        |> List.distinct
        |> List.sortWith (fun left right -> System.String.CompareOrdinal(left, right))

    let sectionBlock (section: string) =
        let entries =
            pages
            |> List.filter (fun page -> page.Section = section)
            |> List.sortBy (fun page -> (page.Order, page.Path))
            |> List.map (fun page -> "- [" + page.Title + "](" + absolute page.Path + "): " + page.Summary)

        [ "## " + section; "" ] @ entries @ [ "" ]

    let experiments =
        manifest.Experiments
        |> List.map (fun experiment ->
            "- ["
            + experiment.Title
            + "]("
            + absolute ("/evidence/" + experiment.Slug + "/")
            + "): "
            + experiment.Summary)

    [ "# " + config.Name
      ""
      "> " + config.Description
      ""
      "Every quantitative claim on this site is backed by a record in an evidence manifest that names the artifact, the repository and the commit it came from, together with a statement of what the number does not show. Read the manifest first if you want the numbers without the prose."
      ""
      "## Machine-readable"
      ""
      "- [Evidence manifest](" + absolute "/data/evidence.json" + "): every experiment, metric, claim and source, as JSON."
      "- [Glossary](" + absolute "/data/glossary.json" + "): defined terms, as JSON."
      "- [Page index](" + absolute "/data/index.json" + "): every page with its section and summary, as JSON."
      "" ]
    @ (sections |> List.collect sectionBlock)
    @ [ "## Experiments"; "" ]
    @ experiments
    @ [ ""
        "## What this site will not tell you"
        ""
        "No claim here is presented as generally established unless the evidence manifest records it as supported. Propositions this research has refuted are published alongside the ones it supports, with the same prominence. Where telemetry was never captured, the site says so rather than estimating."
        "" ]
    |> String.concat "\n"

let sitemap (config: Config) (pages: Page list) : string =
    let entry (page: Page) =
        "  <url><loc>" + config.BaseUrl + config.PathPrefix + page.Path + "</loc></url>"

    [ "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
      "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" ]
    @ (pages |> List.sortBy (fun page -> page.Path) |> List.map entry)
    @ [ "</urlset>"; "" ]
    |> String.concat "\n"

let robots (config: Config) : string =
    String.concat
        "\n"
        [ "User-agent: *"
          "Allow: /"
          "Sitemap: " + config.BaseUrl + config.PathPrefix + "/sitemap.xml"
          "" ]
