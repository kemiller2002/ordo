/// Reads `site/data/evidence.json` into the typed model.
///
/// Reuses `Sde.Core.Json` rather than introducing a second JSON reader: the
/// repository already owns one, it is trim-safe and reflection-free, and a
/// second one would be exactly the uncoordinated duplication SDE's own
/// boundary doctrine warns about.
module Ordo.Site.ManifestReader

open Sde.Core.Json
open Ordo.Site.Model
open Ordo.Site.Validation

// ---------------------------------------------------------------------------
// Field readers
// ---------------------------------------------------------------------------

let private isBlank (value: string) = value.Trim().Length = 0

let private requiredString (name: string) (value: JsonValue) : Validation<string> =
    match tryString (tryField name value) with
    | Some text when not (isBlank text) -> ok text
    | Some _ -> error ("field '" + name + "' is present but empty")
    | None -> error ("required field '" + name + "' is missing or not a string")

let private optionalString (name: string) (value: JsonValue) : Validation<string option> =
    match tryField name value with
    | None -> ok None
    | Some JNull -> ok None
    | Some (JString text) when isBlank text -> ok None
    | Some (JString text) -> ok (Some text)
    | Some _ -> error ("field '" + name + "' must be a string or absent")

/// An absent list is an empty list. A present non-list, or a list holding
/// anything but strings, is an error rather than a silent drop.
let private stringList (name: string) (value: JsonValue) : Validation<string list> =
    match tryField name value with
    | None -> ok []
    | Some JNull -> ok []
    | Some (JArray items) ->
        items
        |> traverse (fun item ->
            match item with
            | JString text when not (isBlank text) -> ok text
            | JString _ -> error ("field '" + name + "' contains an empty string")
            | _ -> error ("field '" + name + "' must contain only strings"))
    | Some _ -> error ("field '" + name + "' must be an array")

let private requiredStringList (name: string) (value: JsonValue) : Validation<string list> =
    stringList name value
    |> bind (fun items ->
        if List.isEmpty items then
            error ("field '" + name + "' must list at least one entry")
        else
            ok items)

let private objects (name: string) (value: JsonValue) : Validation<JsonValue list> =
    match tryField name value with
    | None -> ok []
    | Some (JArray items) ->
        items
        |> traverse (fun item ->
            match item with
            | JObject _ -> ok item
            | _ -> error ("field '" + name + "' must contain only objects"))
    | Some _ -> error ("field '" + name + "' must be an array")

// ---------------------------------------------------------------------------
// Enumerations
// ---------------------------------------------------------------------------

let private readProvenance (value: JsonValue) : Validation<Provenance> =
    requiredString "provenance" value
    |> bind (fun text ->
        match text with
        | "instrumented" -> ok Instrumented
        | "self-reported" -> ok SelfReported
        | "derived" -> ok Derived
        | "not-observable" -> ok NotObservable
        | other ->
            error (
                "unknown provenance '"
                + other
                + "' (expected instrumented, self-reported, derived or not-observable)"
            ))

let private readEvidenceState (value: JsonValue) : Validation<EvidenceState> =
    requiredString "state" value
    |> bind (fun text ->
        match text with
        | "supported" -> ok Supported
        | "provisional" -> ok Provisional
        | "contradicted" -> ok Contradicted
        | "open" -> ok OpenQuestion
        | other ->
            error (
                "unknown evidence state '"
                + other
                + "' (expected supported, provisional, contradicted or open)"
            ))

let private readConfidence (value: JsonValue) : Validation<ConfidenceClass> =
    requiredString "confidence" value
    |> bind (fun text ->
        match text with
        | "required" -> ok Required
        | "recommended" -> ok Recommended
        | "experimental" -> ok Experimental
        | "research-only" -> ok ResearchOnly
        | "deprecated" -> ok Deprecated
        | "unclassified" -> ok Unclassified
        | other -> error ("unknown confidence class '" + other + "'"))

// ---------------------------------------------------------------------------
// Records
// ---------------------------------------------------------------------------

let private makeSource repository path reference section : Source =
    { Repository = repository
      Path = path
      Reference = reference
      Section = section }

let private readSource (value: JsonValue) : Validation<Source> =
    ok makeSource
    <*> requiredString "repository" value
    <*> requiredString "path" value
    <*> optionalString "ref" value
    <*> optionalString "section" value

let private readSourceField (name: string) (value: JsonValue) : Validation<Source> =
    match tryField name value with
    | Some (JObject _ as source) -> readSource source |> withContext name
    | Some _ -> error ("field '" + name + "' must be an object")
    | None -> error ("required field '" + name + "' is missing")

let private makeRepository id name url visibility role : Repository =
    { Id = id
      Name = name
      Url = url
      Visibility = visibility
      Role = role }

let private readRepository (value: JsonValue) : Validation<Repository> =
    ok makeRepository
    <*> requiredString "id" value
    <*> requiredString "name" value
    <*> requiredString "url" value
    <*> requiredString "visibility" value
    <*> requiredString "role" value

let private makeMetric id label metricValue definition measurement experiment provenance limitation source : Metric =
    { Id = id
      Label = label
      Value = metricValue
      Definition = definition
      Measurement = measurement
      Experiment = experiment
      Provenance = provenance
      Limitation = limitation
      Source = source }

/// Note what is *not* optional here: `limitation` and `source`. A metric that
/// states no limitation, or that points at no artifact, fails the build.
let private readMetric (value: JsonValue) : Validation<Metric> =
    ok makeMetric
    <*> requiredString "id" value
    <*> requiredString "label" value
    <*> requiredString "value" value
    <*> requiredString "definition" value
    <*> requiredString "measurement" value
    <*> requiredString "experiment" value
    <*> readProvenance value
    <*> requiredString "limitation" value
    <*> readSourceField "source" value

let private makeExperiment
    id
    slug
    title
    status
    summary
    repository
    branch
    baselineTag
    baselineSha
    endingSha
    agent
    objective
    hypothesis
    method'
    findings
    contradictions
    anomalies
    limitations
    sources
    : Experiment =
    { Id = id
      Slug = slug
      Title = title
      Status = status
      Summary = summary
      Repository = repository
      Branch = branch
      BaselineTag = baselineTag
      BaselineSha = baselineSha
      EndingSha = endingSha
      Agent = agent
      Objective = objective
      Hypothesis = hypothesis
      Method = method'
      Findings = findings
      Contradictions = contradictions
      Anomalies = anomalies
      Limitations = limitations
      Sources = sources }

/// `limitations` is required and non-empty for the same reason a metric's is:
/// an experiment page that states no limitation is a marketing page.
let private readExperiment (value: JsonValue) : Validation<Experiment> =
    ok makeExperiment
    <*> requiredString "id" value
    <*> requiredString "slug" value
    <*> requiredString "title" value
    <*> requiredString "status" value
    <*> requiredString "summary" value
    <*> requiredString "repository" value
    <*> optionalString "branch" value
    <*> optionalString "baselineTag" value
    <*> optionalString "baselineSha" value
    <*> optionalString "endingSha" value
    <*> optionalString "agent" value
    <*> requiredString "objective" value
    <*> optionalString "hypothesis" value
    <*> requiredString "method" value
    <*> stringList "findings" value
    <*> stringList "contradictions" value
    <*> stringList "anomalies" value
    <*> requiredStringList "limitations" value
    <*> (objects "sources" value |> bind (traverse readSource))

let private makeClaim id proposition confidence state records limitation source : Claim =
    { Id = id
      Proposition = proposition
      Confidence = confidence
      State = state
      Records = records
      Limitation = limitation
      Source = source }

let private readClaim (value: JsonValue) : Validation<Claim> =
    ok makeClaim
    <*> requiredString "id" value
    <*> requiredString "proposition" value
    <*> readConfidence value
    <*> readEvidenceState value
    <*> stringList "records" value
    <*> requiredString "limitation" value
    <*> readSourceField "source" value

let private makeGlossaryEntry term definition origin source : GlossaryEntry =
    { Term = term
      Definition = definition
      Origin = origin
      Source = source }

let private readGlossaryEntry (value: JsonValue) : Validation<GlossaryEntry> =
    let source =
        match tryField "source" value with
        | None -> ok None
        | Some JNull -> ok None
        | Some (JObject _ as inner) -> readSource inner |> map Some
        | Some _ -> error "field 'source' must be an object or absent"

    ok makeGlossaryEntry
    <*> requiredString "term" value
    <*> requiredString "definition" value
    <*> requiredString "origin" value
    <*> source

// ---------------------------------------------------------------------------
// Cross-record integrity
// ---------------------------------------------------------------------------

let private duplicates (ids: string list) : string list =
    ids
    |> List.countBy id
    |> List.filter (fun (_, count) -> count > 1)
    |> List.map fst

let private checkUnique (label: string) (ids: string list) : Validation<unit> =
    match duplicates ids with
    | [] -> ok ()
    | repeated -> Error(repeated |> List.map (fun value -> "duplicate " + label + " id '" + value + "'"))

/// Every cross-reference is resolved here, not at render time. A metric naming
/// an experiment that does not exist, or a source naming a repository that
/// does not exist, fails the build rather than rendering a broken citation.
let private checkReferences (manifest: Manifest) : Validation<unit> =
    let repositoryIds = manifest.Repositories |> List.map (fun repository -> repository.Id) |> Set.ofList
    let experimentIds = manifest.Experiments |> List.map (fun experiment -> experiment.Id) |> Set.ofList

    let checkSource (owner: string) (source: Source) =
        if Set.contains source.Repository repositoryIds then
            ok ()
        else
            error (owner + " cites unknown repository '" + source.Repository + "'")

    let metricChecks =
        manifest.Metrics
        |> List.collect (fun metric ->
            [ (if Set.contains metric.Experiment experimentIds then
                   ok ()
               else
                   error ("metric '" + metric.Id + "' cites unknown experiment '" + metric.Experiment + "'"))
              checkSource ("metric '" + metric.Id + "'") metric.Source ])

    let experimentChecks =
        manifest.Experiments
        |> List.collect (fun experiment ->
            let owner = "experiment '" + experiment.Id + "'"

            (if Set.contains experiment.Repository repositoryIds then
                 ok ()
             else
                 error (owner + " cites unknown repository '" + experiment.Repository + "'"))
            :: (experiment.Sources |> List.map (checkSource owner)))

    let claimChecks =
        manifest.Claims
        |> List.map (fun claim -> checkSource ("claim '" + claim.Id + "'") claim.Source)

    let glossaryChecks =
        manifest.Glossary
        |> List.choose (fun entry ->
            entry.Source |> Option.map (checkSource ("glossary term '" + entry.Term + "'")))

    metricChecks @ experimentChecks @ claimChecks @ glossaryChecks
    |> sequence
    |> map ignore

let private makeManifest schemaVersion repositories experiments metrics claims glossary : Manifest =
    { SchemaVersion = schemaVersion
      Repositories = repositories
      Experiments = experiments
      Metrics = metrics
      Claims = claims
      Glossary = glossary }

let private readManifest (value: JsonValue) : Validation<Manifest> =
    let structural =
        ok makeManifest
        <*> requiredString "schemaVersion" value
        <*> (objects "repositories" value
             |> bind (traverse (fun item -> readRepository item |> withContext "repository")))
        <*> (objects "experiments" value
             |> bind (traverse (fun item -> readExperiment item |> withContext "experiment")))
        <*> (objects "metrics" value
             |> bind (traverse (fun item -> readMetric item |> withContext "metric")))
        <*> (objects "claims" value |> bind (traverse (fun item -> readClaim item |> withContext "claim")))
        <*> (objects "glossary" value
             |> bind (traverse (fun item -> readGlossaryEntry item |> withContext "glossary")))

    structural
    |> bind (fun manifest ->
        let uniqueness =
            [ checkUnique "repository" (manifest.Repositories |> List.map (fun r -> r.Id))
              checkUnique "experiment" (manifest.Experiments |> List.map (fun e -> e.Id))
              checkUnique "experiment slug" (manifest.Experiments |> List.map (fun e -> e.Slug))
              checkUnique "metric" (manifest.Metrics |> List.map (fun m -> m.Id))
              checkUnique "claim" (manifest.Claims |> List.map (fun c -> c.Id))
              checkUnique "glossary term" (manifest.Glossary |> List.map (fun g -> g.Term)) ]
            |> sequence
            |> map ignore

        [ uniqueness; checkReferences manifest ] |> sequence |> map (fun _ -> manifest))

/// Parses evidence manifest text. Returns every problem found, not the first.
let read (text: string) : Validation<Manifest> =
    match parse text with
    | Error message -> error ("evidence manifest is not valid JSON: " + message)
    | Ok (JObject _ as value) -> readManifest value
    | Ok _ -> error "evidence manifest must be a JSON object"
