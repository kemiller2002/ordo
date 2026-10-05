/// Boundary amplification: implementation scope that crosses architectural
/// boundaries nobody declared (GH-52, DF-SDE-2026-0016, ORDO-QUAL-001/008).
///
/// A consuming repository declares a *boundary map* (path globs to
/// architectural boundaries, each in a layer) and, per work item, the
/// boundaries it *expects* to touch. A host supplies what was actually
/// changed. This module compares the two and reports evidence:
///
/// - every changed path is either classified into a declared boundary or
///   reported as unclassified — never silently dropped (Unknown is not
///   Failed, and it is not "fine" either);
/// - signals have stable identifiers (ORDO-BA-001..010) and each carries the
///   evidence that fired it;
/// - the risk level is derived from signal strength, not from change size —
///   a large change inside one boundary fires nothing;
/// - the decomposition recommendation is advisory output. Gating is the
///   consumer's decision;
/// - an approved scope expansion (the `ScopeExpansion` of
///   `MutationBoundary`: justification + authorizer) downgrades the
///   recommendation but never hides the crossing or lowers the risk.
///
/// Everything here is pure and deterministic: identical inputs produce an
/// identical assessment, in identical order.
module Ordo.Core.BoundaryAmplification

open System
open Ordo.Core.MutationBoundary

// ---------------------------------------------------------------------------
// Vocabulary
// ---------------------------------------------------------------------------

/// The architectural layer a boundary belongs to. Tests, documentation and
/// build files are *supporting*: they are observed and reported but are not
/// architectural crossings.
type Layer =
    | Cli
    | Application
    | Domain
    | Persistence
    | Provider
    | Rendering
    | Infrastructure
    | Tests
    | Documentation
    | Build
    | CustomLayer of name: string

[<RequireQualifiedAccess>]
module Layer =

    let toWire layer =
        match layer with
        | Cli -> "cli"
        | Application -> "application"
        | Domain -> "domain"
        | Persistence -> "persistence"
        | Provider -> "provider"
        | Rendering -> "rendering"
        | Infrastructure -> "infrastructure"
        | Tests -> "tests"
        | Documentation -> "documentation"
        | Build -> "build"
        | CustomLayer name -> name

    /// Any non-empty name is a layer; unknown names become `CustomLayer`
    /// rather than an error, because layer vocabularies are repository-owned.
    let fromWire (raw: string) =
        match raw with
        | s when String.IsNullOrWhiteSpace s -> None
        | "cli" -> Some Cli
        | "application" -> Some Application
        | "domain" -> Some Domain
        | "persistence" -> Some Persistence
        | "provider" -> Some Provider
        | "rendering" -> Some Rendering
        | "infrastructure" -> Some Infrastructure
        | "tests" -> Some Tests
        | "documentation" -> Some Documentation
        | "build" -> Some Build
        | other -> Some(CustomLayer other)

    let isSupporting layer =
        match layer with
        | Tests
        | Documentation
        | Build -> true
        | Cli
        | Application
        | Domain
        | Persistence
        | Provider
        | Rendering
        | Infrastructure
        | CustomLayer _ -> false

    /// The layers whose co-occurrence in one change marks a requirement that
    /// crossed the stack (ORDO-BA-004), as in the Praxis usage-pacing
    /// incident.
    let defaultCore = [ Cli; Persistence; Provider; Domain; Rendering ]

type BoundaryId =
    | BoundaryId of string

    member this.Value =
        let (BoundaryId value) = this
        value

/// One declared architectural boundary. `Patterns` use `MutationBoundary`'s
/// glob dialect (`*` within a segment, `**` across segments).
type ArchitecturalBoundary =
    { Id: BoundaryId
      Layer: Layer
      Patterns: string list }

/// Imports that indicate a file hosts a boundary's responsibility regardless
/// of where the file lives (an `open System.Net.Http` in a CLI module hosts
/// provider HTTP). A pattern matches one namespace exactly; a trailing `*`
/// matches any name with that prefix (`System.Net.Http*`).
///
/// Indicators name *external effects* (HTTP, filesystem, processes,
/// locking). Importing an inner layer — a CLI opening its domain — is using
/// that layer, not hosting it, and should not be declared as an indicator.
type ResponsibilityIndicator =
    { Boundary: BoundaryId
      Imports: string list }

/// The repository-owned map. Boundary order matters: the first boundary
/// whose pattern matches a path classifies it.
type BoundaryMap =
    { Boundaries: ArchitecturalBoundary list
      Indicators: ResponsibilityIndicator list
      /// Globs naming orchestration files — the modules where accumulated
      /// responsibility is most expensive (ORDO-BA-005/006).
      Orchestration: string list }

/// Thresholds. Every one is versioned configuration with a default; none is
/// a line count, because size alone is not a boundary signal.
type AmplificationPolicy =
    { /// ORDO-BA-002 fires when more distinct architectural boundaries than
      /// this are observed.
      MaxBoundaries: int
      /// ORDO-BA-003 fires when more distinct production modules than this
      /// are touched.
      MaxModules: int
      /// Directory depth that identifies a module (`src/Praxis.Cli` = 2).
      ModuleDepth: int
      CoreLayers: Layer list
      /// ORDO-BA-004 fires when at least this many core layers are observed.
      CoreLayerSpan: int
      /// ORDO-BA-005 fires when a file was changed by at least this many
      /// distinct work items, the current one included.
      HotspotWorkItems: int
      /// ORDO-BA-006 fires when an orchestration file was modified at least
      /// this many times within the current change.
      OrchestrationChurn: int
      /// ORDO-BA-007 fires when one file hosts at least this many boundaries
      /// other than the one it lives in.
      ConcentrationBoundaries: int
      /// ORDO-BA-008 fires when observed/expected architectural boundaries
      /// exceeds this ratio.
      MaxExpansionRatio: float
      ElevatedScore: int
      HighScore: int }

[<RequireQualifiedAccess>]
module AmplificationPolicy =

    let defaults =
        { MaxBoundaries = 3
          MaxModules = 4
          ModuleDepth = 2
          CoreLayers = Layer.defaultCore
          CoreLayerSpan = 3
          HotspotWorkItems = 3
          OrchestrationChurn = 3
          ConcentrationBoundaries = 2
          MaxExpansionRatio = 2.0
          ElevatedScore = 2
          HighScore = 4 }

/// A recorded, authorized widening of what a work item may touch. It reuses
/// `ScopeExpansion` (justification + authorizer, evaluator authority refused)
/// and names the architectural boundaries it admits.
type ApprovedExpansion =
    { Expansion: ScopeExpansion
      AddedBoundaries: BoundaryId list }

/// What a work item declared before implementation.
type Expectation =
    { WorkItem: string
      Requirement: string option
      ExpectedBoundaries: BoundaryId list
      /// The execution's declared write set, when one exists. Changed files
      /// it does not admit are ORDO-BA-009.
      WriteSet: MutationBoundary option
      Expansions: ApprovedExpansion list }

type ChangedFile =
    { Path: string
      /// How many times the file was modified within this change (for
      /// example, commits touching it). Unknown when absent.
      ChangeCount: int option
      /// Namespaces the file imports, when the host extracted them.
      Imports: string list }

/// A prior work item and the paths it changed.
type PriorWork = { WorkItem: string; Paths: string list }

type ChangeSet =
    { WorkItem: string option
      Files: ChangedFile list
      History: PriorWork list }

// ---------------------------------------------------------------------------
// Observation
// ---------------------------------------------------------------------------

type ObservationSource =
    /// The file lives inside the boundary.
    | ByPath of path: string
    /// The file imports a namespace the boundary's indicator names.
    | ByImport of path: string * importName: string

type ObservedBoundary =
    { Boundary: BoundaryId
      Layer: Layer
      Sources: ObservationSource list }

type Strength =
    | Informational
    | Weak
    | Strong

/// Every signal with the evidence that fired it.
type Signal =
    /// ORDO-BA-001: observed architectural boundaries the work item did not
    /// declare.
    | UnexpectedBoundaryCrossing of boundaries: BoundaryId list
    /// ORDO-BA-002
    | BoundaryBreadth of boundaries: BoundaryId list * threshold: int
    /// ORDO-BA-003: one requirement touching many production modules.
    | ModuleScatter of modules: string list * threshold: int
    /// ORDO-BA-004
    | CoreLayerSpan of layers: Layer list * threshold: int
    /// ORDO-BA-005: the same file changed by many work items.
    | HotspotRecurrence of path: string * workItems: string list * threshold: int * orchestration: bool
    /// ORDO-BA-006: one orchestration file modified repeatedly in this change.
    | OrchestrationChurn of path: string * changes: int * threshold: int
    /// ORDO-BA-007: one file hosting several boundaries' responsibilities.
    | ResponsibilityConcentration of path: string * home: BoundaryId option * hosted: BoundaryId list * threshold: int
    /// ORDO-BA-008
    | ScopeExpansionRatio of ratio: float * threshold: float
    /// ORDO-BA-009: changed files outside the declared write set.
    | WriteSetEscape of effects: ScopeEffect list
    /// ORDO-BA-010: paths no boundary classifies. Informational: unknown,
    /// not failed, and visible.
    | UnclassifiedPaths of paths: string list

[<RequireQualifiedAccess>]
module Signal =

    let id signal =
        match signal with
        | UnexpectedBoundaryCrossing _ -> "ORDO-BA-001"
        | BoundaryBreadth _ -> "ORDO-BA-002"
        | ModuleScatter _ -> "ORDO-BA-003"
        | CoreLayerSpan _ -> "ORDO-BA-004"
        | HotspotRecurrence _ -> "ORDO-BA-005"
        | OrchestrationChurn _ -> "ORDO-BA-006"
        | ResponsibilityConcentration _ -> "ORDO-BA-007"
        | ScopeExpansionRatio _ -> "ORDO-BA-008"
        | WriteSetEscape _ -> "ORDO-BA-009"
        | UnclassifiedPaths _ -> "ORDO-BA-010"

    let name signal =
        match signal with
        | UnexpectedBoundaryCrossing _ -> "unexpected-boundary-crossing"
        | BoundaryBreadth _ -> "boundary-breadth"
        | ModuleScatter _ -> "module-scatter"
        | CoreLayerSpan _ -> "core-layer-span"
        | HotspotRecurrence _ -> "hotspot-recurrence"
        | OrchestrationChurn _ -> "orchestration-churn"
        | ResponsibilityConcentration _ -> "responsibility-concentration"
        | ScopeExpansionRatio _ -> "scope-expansion-ratio"
        | WriteSetEscape _ -> "write-set-escape"
        | UnclassifiedPaths _ -> "unclassified-paths"

    let strength signal =
        match signal with
        | UnexpectedBoundaryCrossing _
        | CoreLayerSpan _
        | OrchestrationChurn _
        | ResponsibilityConcentration _
        | WriteSetEscape _ -> Strong
        | HotspotRecurrence(_, _, _, orchestration) -> if orchestration then Strong else Weak
        | BoundaryBreadth _
        | ModuleScatter _
        | ScopeExpansionRatio _ -> Weak
        | UnclassifiedPaths _ -> Informational

    let weight signal =
        match strength signal with
        | Strong -> 2
        | Weak -> 1
        | Informational -> 0

    let private ids (boundaries: BoundaryId list) =
        boundaries |> List.map (fun b -> b.Value) |> String.concat ", "

    /// One sentence of evidence a human can act on.
    let describe signal =
        match signal with
        | UnexpectedBoundaryCrossing bs -> sprintf "changed boundaries that were not declared: %s" (ids bs)
        | BoundaryBreadth(bs, t) -> sprintf "%d architectural boundaries crossed (threshold %d): %s" bs.Length t (ids bs)
        | ModuleScatter(ms, t) ->
            sprintf "%d production modules touched by one requirement (threshold %d): %s" ms.Length t (String.concat ", " ms)
        | CoreLayerSpan(ls, t) ->
            sprintf
                "one change spans %d core layers (threshold %d): %s"
                ls.Length
                t
                (ls |> List.map Layer.toWire |> String.concat ", ")
        | HotspotRecurrence(p, wis, t, o) ->
            sprintf
                "%s%s changed by %d work items (threshold %d): %s"
                (if o then "orchestration file " else "")
                p
                wis.Length
                t
                (String.concat ", " wis)
        | OrchestrationChurn(p, n, t) -> sprintf "orchestration file %s modified %d times in this change (threshold %d)" p n t
        | ResponsibilityConcentration(p, home, hosted, t) ->
            sprintf
                "%s (in %s) hosts responsibilities of %d other boundaries (threshold %d): %s"
                p
                (home |> Option.map (fun b -> b.Value) |> Option.defaultValue "no boundary")
                hosted.Length
                t
                (ids hosted)
        | ScopeExpansionRatio(r, t) ->
            sprintf
                "observed/expected boundary ratio %s exceeds %s"
                (r.ToString("0.##", Globalization.CultureInfo.InvariantCulture))
                (t.ToString("0.##", Globalization.CultureInfo.InvariantCulture))
        | WriteSetEscape effects ->
            sprintf
                "%d changed files outside the declared write set: %s"
                effects.Length
                (effects |> List.map (fun e -> e.Resource) |> String.concat ", ")
        | UnclassifiedPaths ps -> sprintf "%d paths no boundary classifies: %s" ps.Length (String.concat ", " ps)

type RiskLevel =
    | Low
    | Elevated
    | High

[<RequireQualifiedAccess>]
module RiskLevel =

    let toWire level =
        match level with
        | Low -> "low"
        | Elevated -> "elevated"
        | High -> "high"

type Risk =
    { Level: RiskLevel
      Score: int
      /// The signals that contributed to the score, in signal-id order.
      Evidence: Signal list }

type Recommendation =
    | NoAction
    | ConsiderSplitAlong of boundaries: BoundaryId list
    | RequireDesignReview of splitAlong: BoundaryId list

[<RequireQualifiedAccess>]
module Recommendation =

    let toWire recommendation =
        match recommendation with
        | NoAction -> "no-action"
        | ConsiderSplitAlong _ -> "consider-split-along"
        | RequireDesignReview _ -> "require-design-review"

    let boundaries recommendation =
        match recommendation with
        | NoAction -> []
        | ConsiderSplitAlong bs
        | RequireDesignReview bs -> bs

    /// One step less demanding. An approval answers the question a design
    /// review would ask; it does not make the crossing disappear.
    let downgrade recommendation =
        match recommendation with
        | RequireDesignReview bs -> ConsiderSplitAlong bs
        | ConsiderSplitAlong _
        | NoAction -> NoAction

type ExpansionRejection =
    | InvalidExpansion of ScopeExpansionError
    | ExpansionNamesUnknownBoundary of BoundaryId list

type ExceptionCoverage =
    /// No unexpected crossing exists, so there is nothing to approve.
    | NotApplicable
    | NoCoverage
    | PartialCoverage
    | FullCoverage

[<RequireQualifiedAccess>]
module ExceptionCoverage =

    let toWire coverage =
        match coverage with
        | NotApplicable -> "not-applicable"
        | NoCoverage -> "none"
        | PartialCoverage -> "partial"
        | FullCoverage -> "full"

type ExceptionRationale =
    { Coverage: ExceptionCoverage
      Applied: ApprovedExpansion list
      Rejected: (ApprovedExpansion * ExpansionRejection) list
      /// Unexpected crossings an applied expansion admits, with its id.
      ApprovedCrossings: (BoundaryId * string) list
      UnapprovedCrossings: BoundaryId list
      /// The recommendation before the approval was taken into account,
      /// when the approval changed it.
      DowngradedFrom: Recommendation option }

type Assessment =
    { WorkItem: string
      Requirement: string option
      Policy: AmplificationPolicy
      Expected: BoundaryId list
      /// Architectural boundaries observed, by path or by import.
      Observed: ObservedBoundary list
      /// Supporting boundaries (tests, documentation, build) observed.
      Supporting: ObservedBoundary list
      Unclassified: string list
      UnexpectedCrossings: BoundaryId list
      /// Expected boundaries the change did not touch. Reported, not judged.
      Unexercised: BoundaryId list
      /// Observed / expected architectural boundaries. `None` when nothing
      /// architectural was expected: the ratio is unknown, not zero.
      AmplificationRatio: float option
      Signals: Signal list
      Risk: Risk
      Recommendation: Recommendation
      Exception: ExceptionRationale }

type AssessmentError =
    | DuplicateBoundary of BoundaryId
    | BoundaryWithoutPatterns of BoundaryId
    | UnknownBoundary of context: string * BoundaryId
    | EmptyPath of context: string
    | WorkItemMismatch of expected: string * changed: string
    | MissingWorkItem
    | InvalidPolicy of field: string * reason: string

// ---------------------------------------------------------------------------
// Pure helpers
// ---------------------------------------------------------------------------

/// Repository-relative, forward-slash, no leading `./`.
let normalizePath (raw: string) =
    let p = raw.Trim().Replace('\\', '/')
    let p = if p.StartsWith "./" then p.Substring 2 else p
    p.TrimStart('/')

let private ordinal (values: string list) =
    values |> List.distinct |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

let private sortIds (ids: BoundaryId list) =
    ids
    |> List.distinct
    |> List.sortWith (fun a b -> String.CompareOrdinal(a.Value, b.Value))

let importMatches (pattern: string) (importName: string) =
    if pattern.EndsWith "*" then
        importName.StartsWith(pattern.TrimEnd('*'), StringComparison.Ordinal)
    else
        importName = pattern

/// The first declared boundary whose pattern matches the path.
let classify (map: BoundaryMap) (path: string) : ArchitecturalBoundary option =
    map.Boundaries
    |> List.tryFind (fun b -> b.Patterns |> List.exists (fun pattern -> Glob.isMatch pattern path))

let private moduleOf depth (path: string) =
    let segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

    match segments with
    | []
    | [ _ ] -> "(root)"
    | _ ->
        segments
        |> List.take (segments.Length - 1)
        |> List.truncate depth
        |> String.concat "/"

let private validatePolicy (policy: AmplificationPolicy) =
    [ if policy.MaxBoundaries < 1 then InvalidPolicy("maxBoundaries", "must be at least 1")
      if policy.MaxModules < 1 then InvalidPolicy("maxModules", "must be at least 1")
      if policy.ModuleDepth < 1 then InvalidPolicy("moduleDepth", "must be at least 1")
      if policy.CoreLayerSpan < 2 then InvalidPolicy("coreLayerSpan", "must be at least 2")
      if policy.HotspotWorkItems < 2 then InvalidPolicy("hotspotWorkItems", "must be at least 2")
      if policy.OrchestrationChurn < 2 then InvalidPolicy("orchestrationChurn", "must be at least 2")
      if policy.ConcentrationBoundaries < 1 then InvalidPolicy("concentrationBoundaries", "must be at least 1")
      if not (Double.IsFinite policy.MaxExpansionRatio) || policy.MaxExpansionRatio < 1.0 then
          InvalidPolicy("maxExpansionRatio", "must be a finite number of at least 1")
      if policy.ElevatedScore < 1 then InvalidPolicy("elevatedScore", "must be at least 1")
      if policy.HighScore <= policy.ElevatedScore then InvalidPolicy("highScore", "must exceed elevatedScore") ]

let private validate (map: BoundaryMap) (policy: AmplificationPolicy) (expectation: Expectation) (changes: ChangeSet) =
    let known = map.Boundaries |> List.map (fun b -> b.Id) |> Set.ofList

    let unknown context ids =
        ids |> List.filter (fun id -> not (Set.contains id known)) |> List.map (fun id -> UnknownBoundary(context, id))

    [ yield!
          map.Boundaries
          |> List.countBy (fun b -> b.Id)
          |> List.filter (fun (_, n) -> n > 1)
          |> List.map (fst >> DuplicateBoundary)
      yield!
          map.Boundaries
          |> List.filter (fun b -> b.Patterns |> List.forall String.IsNullOrWhiteSpace)
          |> List.map (fun b -> BoundaryWithoutPatterns b.Id)
      yield! map.Indicators |> List.map (fun i -> i.Boundary) |> unknown "indicator"
      yield! unknown "expectation" expectation.ExpectedBoundaries
      if String.IsNullOrWhiteSpace expectation.WorkItem then
          yield MissingWorkItem
      match changes.WorkItem with
      | Some w when not (String.IsNullOrWhiteSpace w) && w <> expectation.WorkItem ->
          yield WorkItemMismatch(expectation.WorkItem, w)
      | _ -> ()
      yield!
          changes.Files
          |> List.filter (fun f -> String.IsNullOrWhiteSpace(normalizePath f.Path))
          |> List.map (fun _ -> EmptyPath "changed file")
      yield! validatePolicy policy ]

// ---------------------------------------------------------------------------
// Assessment
// ---------------------------------------------------------------------------

type private FileView =
    { Path: string
      Home: ArchitecturalBoundary option
      ChangeCount: int option
      Imports: string list }

let private mergeFiles (files: ChangedFile list) : FileView list =
    files
    |> List.groupBy (fun f -> normalizePath f.Path)
    |> List.map (fun (path, group) ->
        let counts = group |> List.choose (fun f -> f.ChangeCount)

        { Path = path
          Home = None
          ChangeCount = if counts.IsEmpty then None else Some(List.sum counts)
          Imports = group |> List.collect (fun f -> f.Imports) |> ordinal })
    |> List.sortWith (fun a b -> String.CompareOrdinal(a.Path, b.Path))

let private isProduction (view: FileView) =
    match view.Home with
    | Some b -> not (Layer.isSupporting b.Layer)
    | None -> true

let private observe (map: BoundaryMap) (views: FileView list) =
    let byId = map.Boundaries |> List.map (fun b -> b.Id, b) |> Map.ofList

    let pathSources =
        views
        |> List.choose (fun v -> v.Home |> Option.map (fun b -> b.Id, ByPath v.Path))

    let importSources =
        views
        |> List.filter isProduction
        |> List.collect (fun v ->
            v.Imports
            |> List.collect (fun importName ->
                map.Indicators
                |> List.filter (fun i -> i.Imports |> List.exists (fun p -> importMatches p importName))
                |> List.map (fun i -> i.Boundary, ByImport(v.Path, importName))))

    let sourceKey source =
        match source with
        | ByPath p -> p + "\u0000"
        | ByImport(p, i) -> p + "\u0001" + i

    pathSources @ importSources
    |> List.groupBy fst
    |> List.map (fun (id, sources) ->
        { Boundary = id
          Layer = (Map.find id byId).Layer
          Sources =
            sources
            |> List.map snd
            |> List.distinct
            |> List.sortWith (fun a b -> String.CompareOrdinal(sourceKey a, sourceKey b)) })
    |> List.sortWith (fun a b -> String.CompareOrdinal(a.Boundary.Value, b.Boundary.Value))

let private hostedBy (map: BoundaryMap) (view: FileView) =
    let home = view.Home |> Option.map (fun b -> b.Id)

    view.Imports
    |> List.collect (fun importName ->
        map.Indicators
        |> List.filter (fun i -> i.Imports |> List.exists (fun p -> importMatches p importName))
        |> List.map (fun i -> i.Boundary))
    |> List.filter (fun b -> Some b <> home)
    |> sortIds

let private applyExpansions (map: BoundaryMap) (expectation: Expectation) =
    let known = map.Boundaries |> List.map (fun b -> b.Id) |> Set.ofList

    let folder (writeSet, applied, rejected) (approved: ApprovedExpansion) =
        let unknown = approved.AddedBoundaries |> List.filter (fun b -> not (Set.contains b known))

        if not unknown.IsEmpty then
            writeSet, applied, (approved, ExpansionNamesUnknownBoundary(sortIds unknown)) :: rejected
        else
            match MutationBoundary.expand approved.Expansion writeSet with
            | Ok widened -> widened, approved :: applied, rejected
            | Error e -> writeSet, applied, (approved, InvalidExpansion e) :: rejected

    let start = expectation.WriteSet |> Option.defaultValue MutationBoundary.empty

    let writeSet, applied, rejected =
        expectation.Expansions
        |> List.sortWith (fun a b -> String.CompareOrdinal(a.Expansion.ExpansionId, b.Expansion.ExpansionId))
        |> List.fold folder (start, [], [])

    (if expectation.WriteSet.IsSome then Some writeSet else None), List.rev applied, List.rev rejected

let private riskOf (policy: AmplificationPolicy) (signals: Signal list) =
    let evidence = signals |> List.filter (fun s -> Signal.strength s <> Informational)
    let score = evidence |> List.sumBy Signal.weight

    let level =
        if score >= policy.HighScore then High
        elif score >= policy.ElevatedScore then Elevated
        else Low

    { Level = level
      Score = score
      Evidence = evidence }

/// Assess one work item's change against its declared expectation.
let assess
    (map: BoundaryMap)
    (policy: AmplificationPolicy)
    (expectation: Expectation)
    (changes: ChangeSet)
    : Result<Assessment, AssessmentError list> =
    match validate map policy expectation changes with
    | _ :: _ as errors -> Error errors
    | [] ->

    let views =
        mergeFiles changes.Files
        |> List.map (fun v -> { v with Home = classify map v.Path })

    let unclassified = views |> List.filter (fun v -> v.Home.IsNone) |> List.map (fun v -> v.Path)
    let observedAll = observe map views
    let observed = observedAll |> List.filter (fun o -> not (Layer.isSupporting o.Layer))
    let supporting = observedAll |> List.filter (fun o -> Layer.isSupporting o.Layer)
    let observedIds = observed |> List.map (fun o -> o.Boundary)

    let layerOf =
        let byId = map.Boundaries |> List.map (fun b -> b.Id, b.Layer) |> Map.ofList
        fun id -> Map.find id byId

    let expected = sortIds expectation.ExpectedBoundaries
    let expectedArchitectural = expected |> List.filter (layerOf >> Layer.isSupporting >> not)
    let unexpected = observedIds |> List.filter (fun b -> not (List.contains b expected))
    let unexercised = expectedArchitectural |> List.filter (fun b -> not (List.contains b observedIds))

    let ratio =
        if expectedArchitectural.IsEmpty then
            None
        else
            Some(float observedIds.Length / float expectedArchitectural.Length)

    let production = views |> List.filter isProduction

    let orchestrationFile (path: string) =
        map.Orchestration |> List.exists (fun pattern -> Glob.isMatch pattern path)

    let history =
        changes.History
        |> List.map (fun w -> w.WorkItem, w.Paths |> List.map normalizePath |> Set.ofList)

    let writeSet, applied, rejected = applyExpansions map expectation

    let escapes =
        match writeSet with
        | None -> []
        | Some boundary -> views |> List.map (fun v -> v.Path, None) |> MutationBoundary.scopeEffects boundary

    let modules = production |> List.map (fun v -> moduleOf policy.ModuleDepth v.Path) |> ordinal

    let coreLayers =
        observed
        |> List.map (fun o -> o.Layer)
        |> List.filter (fun l -> List.contains l policy.CoreLayers)
        |> List.distinct
        |> List.sortBy (fun l -> List.findIndex ((=) l) policy.CoreLayers)

    let signals =
        [ if not unexpected.IsEmpty then UnexpectedBoundaryCrossing unexpected
          if observedIds.Length > policy.MaxBoundaries then BoundaryBreadth(observedIds, policy.MaxBoundaries)
          if modules.Length > policy.MaxModules then ModuleScatter(modules, policy.MaxModules)
          if coreLayers.Length >= policy.CoreLayerSpan then CoreLayerSpan(coreLayers, policy.CoreLayerSpan)
          for v in production do
              let workItems =
                  expectation.WorkItem
                  :: (history |> List.filter (fun (_, paths) -> Set.contains v.Path paths) |> List.map fst)
                  |> ordinal

              if workItems.Length >= policy.HotspotWorkItems then
                  HotspotRecurrence(v.Path, workItems, policy.HotspotWorkItems, orchestrationFile v.Path)
          for v in production do
              match v.ChangeCount with
              | Some n when n >= policy.OrchestrationChurn && orchestrationFile v.Path ->
                  OrchestrationChurn(v.Path, n, policy.OrchestrationChurn)
              | _ -> ()
          for v in production do
              let hosted = hostedBy map v

              if hosted.Length >= policy.ConcentrationBoundaries then
                  ResponsibilityConcentration(v.Path, v.Home |> Option.map (fun b -> b.Id), hosted, policy.ConcentrationBoundaries)
          match ratio with
          | Some r when r > policy.MaxExpansionRatio -> ScopeExpansionRatio(r, policy.MaxExpansionRatio)
          | _ -> ()
          if not escapes.IsEmpty then WriteSetEscape escapes
          if not unclassified.IsEmpty then UnclassifiedPaths unclassified ]

    let risk = riskOf policy signals

    let splitAlong =
        let concentrated =
            signals
            |> List.collect (fun s ->
                match s with
                | ResponsibilityConcentration(_, _, hosted, _) -> hosted
                | _ -> [])

        match sortIds (unexpected @ concentrated) with
        | [] -> observedIds
        | candidates -> candidates

    let raw =
        match risk.Level with
        | Low -> NoAction
        | Elevated -> ConsiderSplitAlong splitAlong
        | High -> RequireDesignReview splitAlong

    let approvedCrossings =
        unexpected
        |> List.choose (fun b ->
            applied
            |> List.tryFind (fun a -> List.contains b a.AddedBoundaries)
            |> Option.map (fun a -> b, a.Expansion.ExpansionId))

    let unapproved = unexpected |> List.filter (fun b -> not (List.exists (fst >> (=) b) approvedCrossings))

    let coverage =
        if unexpected.IsEmpty then NotApplicable
        elif unapproved.IsEmpty && escapes.IsEmpty then FullCoverage
        elif approvedCrossings.IsEmpty then NoCoverage
        else PartialCoverage

    let recommendation, downgradedFrom =
        match coverage with
        | FullCoverage when raw <> NoAction -> Recommendation.downgrade raw, Some raw
        | _ -> raw, None

    Ok
        { WorkItem = expectation.WorkItem
          Requirement = expectation.Requirement
          Policy = policy
          Expected = expected
          Observed = observed
          Supporting = supporting
          Unclassified = unclassified
          UnexpectedCrossings = unexpected
          Unexercised = unexercised
          AmplificationRatio = ratio
          Signals = signals
          Risk = risk
          Recommendation = recommendation
          Exception =
            { Coverage = coverage
              Applied = applied
              Rejected = rejected
              ApprovedCrossings = approvedCrossings
              UnapprovedCrossings = unapproved
              DowngradedFrom = downgradedFrom } }
