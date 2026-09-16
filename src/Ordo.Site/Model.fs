/// The typed model behind every number the Ordo site publishes.
///
/// The site's central rule is that a quantitative public claim must be
/// traceable to an artifact a reader can open. That rule is enforced here by
/// making an untraceable metric unrepresentable: `Metric` has no constructor
/// that omits `Source`, and `Manifest` rejects an empty `Limitation`. A number
/// that nobody can check, or that carries no statement of what it does not
/// show, cannot reach a rendered page.
module Ordo.Site.Model

/// Where a claim can actually be inspected.
type Source =
    { /// Repository id, resolved against `Manifest.Repositories`.
      Repository: string
      /// Repository-relative path of the artifact.
      Path: string
      /// Commit SHA, tag, or branch the path should be read at.
      Reference: string option
      /// Heading or section within the artifact, where one applies.
      Section: string option }

/// How a recorded value came to exist. This is deliberately not a quality
/// ranking: a self-reported figure is not automatically worse than an
/// instrument reading, but a reader is entitled to know which one they are
/// looking at.
type Provenance =
    /// Produced by an instrument: a test runner, a build wrapper, a git count,
    /// a platform usage API.
    | Instrumented
    /// An executing agent's own record of its own work.
    | SelfReported
    /// Computed from other recorded values by a stated formula.
    | Derived
    /// The telemetry genuinely does not exist for the environment that
    /// produced the work. Never estimated, never silently omitted.
    | NotObservable

/// The evidence vocabulary already used by
/// `doctrine/EVIDENCE-TO-ENGINEERING-MAP.md`. The site does not invent a
/// second scheme.
type EvidenceState =
    | Supported
    | Provisional
    | Contradicted
    | OpenQuestion

/// The confidence classes defined by `doctrine/STATE-DIRECTED-ENGINEERING.md`.
type ConfidenceClass =
    | Required
    | Recommended
    | Experimental
    | ResearchOnly
    | Deprecated
    /// The map's own "—": a proposition carried for the record, holding no
    /// confidence class because its evidence state is Contradicted or Open.
    | Unclassified

/// A repository the site cites.
type Repository =
    { Id: string
      Name: string
      Url: string
      /// "public" or "private" — a reader following a citation into a private
      /// repository is told so before they click.
      Visibility: string
      Role: string }

/// One recorded quantity.
type Metric =
    { Id: string
      Label: string
      /// Rendered exactly as recorded, including ranges and arrows. The site
      /// never reformats a measured value into something more impressive.
      Value: string
      /// What the number measures.
      Definition: string
      /// How it was collected.
      Measurement: string
      /// Experiment id, resolved against `Manifest.Experiments`.
      Experiment: string
      Provenance: Provenance
      /// What a reader must not conclude from this number. Required.
      Limitation: string
      Source: Source }

/// One experiment or trial.
type Experiment =
    { Id: string
      /// URL segment under /evidence/.
      Slug: string
      Title: string
      /// Short human status, e.g. "Complete", "Requirements only".
      Status: string
      Summary: string
      Repository: string
      Branch: string option
      BaselineTag: string option
      BaselineSha: string option
      EndingSha: string option
      /// The agent or model that executed it, where recorded.
      Agent: string option
      Objective: string
      Hypothesis: string option
      Method: string
      /// What the experiment established.
      Findings: string list
      /// What it refuted, including its own preregistered predictions.
      Contradictions: string list
      /// Failures, anomalies and confounders recorded by the experiment.
      Anomalies: string list
      /// What a reader must not conclude from the experiment as a whole.
      Limitations: string list
      Sources: Source list }

/// One row of the evidence-to-engineering map: a proposition, what supports
/// it, and what it is not allowed to claim.
type Claim =
    { Id: string
      Proposition: string
      Confidence: ConfidenceClass
      State: EvidenceState
      /// Record ids (EV-, HY-, TH-, DF-).
      Records: string list
      Limitation: string
      Source: Source }

type GlossaryEntry =
    { Term: string
      Definition: string
      /// Which body of work the term comes from.
      Origin: string
      Source: Source option }

type Manifest =
    { SchemaVersion: string
      Repositories: Repository list
      Experiments: Experiment list
      Metrics: Metric list
      Claims: Claim list
      Glossary: GlossaryEntry list }

// ---------------------------------------------------------------------------
// Presentation-neutral lookups and labels
// ---------------------------------------------------------------------------

let provenanceLabel (provenance: Provenance) : string =
    match provenance with
    | Instrumented -> "Instrumented"
    | SelfReported -> "Self-reported"
    | Derived -> "Derived"
    | NotObservable -> "Not observable"

let provenanceMeaning (provenance: Provenance) : string =
    match provenance with
    | Instrumented -> "Recorded by a test runner, build wrapper, git count, or platform usage API."
    | SelfReported -> "Recorded by the executing agent about its own work."
    | Derived -> "Computed from other recorded values by a stated formula."
    | NotObservable -> "The telemetry does not exist for the environment that produced this work."

let evidenceStateLabel (state: EvidenceState) : string =
    match state with
    | Supported -> "Supported"
    | Provisional -> "Provisional"
    | Contradicted -> "Contradicted"
    | OpenQuestion -> "Open"

let confidenceLabel (confidence: ConfidenceClass) : string =
    match confidence with
    | Required -> "REQUIRED"
    | Recommended -> "RECOMMENDED"
    | Experimental -> "EXPERIMENTAL"
    | ResearchOnly -> "RESEARCH ONLY"
    | Deprecated -> "DEPRECATED"
    | Unclassified -> "No class"

/// A CSS modifier, kept next to the label so a new case cannot be rendered
/// with no styling and no warning.
let evidenceStateSlug (state: EvidenceState) : string =
    match state with
    | Supported -> "supported"
    | Provisional -> "provisional"
    | Contradicted -> "contradicted"
    | OpenQuestion -> "open"

let tryFindExperiment (manifest: Manifest) (id: string) : Experiment option =
    manifest.Experiments |> List.tryFind (fun experiment -> experiment.Id = id)

let tryFindMetric (manifest: Manifest) (id: string) : Metric option =
    manifest.Metrics |> List.tryFind (fun metric -> metric.Id = id)

let tryFindRepository (manifest: Manifest) (id: string) : Repository option =
    manifest.Repositories |> List.tryFind (fun repository -> repository.Id = id)

let metricsFor (manifest: Manifest) (experimentId: string) : Metric list =
    manifest.Metrics |> List.filter (fun metric -> metric.Experiment = experimentId)
