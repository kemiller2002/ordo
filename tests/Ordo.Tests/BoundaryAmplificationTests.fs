/// Boundary amplification (GH-52, DF-SDE-2026-0016): every signal fires on
/// its evidence and stays silent without it, the risk is derived from
/// evidence rather than size, approvals stay visible, and the assessment is
/// deterministic. The two characterization fixtures under
/// tests/fixtures/boundary-amplification are shared with the CLI tests.
module Ordo.Tests.BoundaryAmplificationTests

open System
open System.IO
open Xunit
open Ordo.Core.Json
open Ordo.Core.MutationBoundary
open Ordo.Core.BoundaryAmplification
open Ordo.Core.BoundaryAmplificationWire

let private ok result =
    match result with
    | Ok value -> value
    | Error error -> failwithf "expected Ok, got %A" error

let private b = BoundaryId

let private boundary id layer patterns =
    { Id = b id
      Layer = layer
      Patterns = patterns }

let private map =
    { Boundaries =
        [ boundary "tests" Tests [ "tests/**" ]
          boundary "docs" Documentation [ "docs/**" ]
          boundary "rendering" Rendering [ "src/App.Cli/*View.fs" ]
          boundary "cli" Cli [ "src/App.Cli/**" ]
          boundary "application" Application [ "src/App.Application/**" ]
          boundary "domain" Domain [ "src/App.Domain/**" ]
          boundary "persistence" Persistence [ "src/App.Persistence/**" ]
          boundary "provider" Provider [ "src/App.Provider/**" ]
          boundary "infrastructure" Infrastructure [ "src/App.Infrastructure/**" ] ]
      Indicators =
        [ { Boundary = b "provider"; Imports = [ "System.Net.Http*" ] }
          { Boundary = b "persistence"; Imports = [ "System.IO" ] }
          { Boundary = b "infrastructure"; Imports = [ "System.Threading" ] } ]
      Orchestration = [ "src/App.Cli/*Commands.fs" ] }

let private policy = AmplificationPolicy.defaults

let private expect boundaries =
    { WorkItem = "WI-1"
      Requirement = None
      ExpectedBoundaries = boundaries |> List.map b
      WriteSet = None
      Expansions = [] }

let private file path =
    { Path = path
      ChangeCount = None
      Imports = [] }

let private changes files =
    { WorkItem = None
      Files = files
      History = [] }

let private run expectation files =
    assess map policy expectation (changes files) |> ok

let private fired id (a: Assessment) = a.Signals |> List.exists (fun s -> Signal.id s = id)

let private signal id (a: Assessment) = a.Signals |> List.find (fun s -> Signal.id s = id)

let private ids (bs: BoundaryId list) = bs |> List.map (fun x -> x.Value)

// ---------------------------------------------------------------------------
// Classification
// ---------------------------------------------------------------------------

[<Fact>]
let ``unmapped paths are reported as unclassified, never dropped`` () =
    let a = run (expect [ "domain" ]) [ file "src/App.Domain/Model.fs"; file "scripts/build.sh"; file "./README" ]
    Assert.Equal<string list>([ "README"; "scripts/build.sh" ], a.Unclassified)
    Assert.True(fired "ORDO-BA-010" a)
    Assert.Equal(Informational, Signal.strength (signal "ORDO-BA-010" a))
    // Unknown is visible but is not evidence of amplification.
    Assert.Equal(Low, a.Risk.Level)
    Assert.Equal(0, a.Risk.Score)

[<Fact>]
let ``the first matching boundary classifies a path`` () =
    let a = run (expect [ "cli" ]) [ file "src/App.Cli/StatusView.fs" ]
    Assert.Equal<string list>([ "rendering" ], a.Observed |> List.map (fun o -> o.Boundary.Value))

[<Fact>]
let ``supporting layers are observed but are not crossings`` () =
    let a = run (expect [ "domain" ]) [ file "src/App.Domain/Model.fs"; file "tests/ModelTests.fs"; file "docs/model.md" ]
    Assert.Empty a.UnexpectedCrossings
    Assert.Equal<string list>([ "docs"; "tests" ], a.Supporting |> List.map (fun o -> o.Boundary.Value))
    Assert.Empty a.Signals

[<Fact>]
let ``windows separators and duplicate entries normalize to one path`` () =
    let a =
        run
            (expect [ "domain" ])
            [ { file "src\\App.Domain\\Model.fs" with ChangeCount = Some 1 }
              { file "src/App.Domain/Model.fs" with ChangeCount = Some 2 } ]

    let observed = a.Observed |> List.exactlyOne
    Assert.Equal<ObservationSource list>([ ByPath "src/App.Domain/Model.fs" ], observed.Sources)

// ---------------------------------------------------------------------------
// ORDO-BA-001 unexpected crossing
// ---------------------------------------------------------------------------

[<Fact>]
let ``BA-001 fires for an observed boundary that was not declared`` () =
    let a = run (expect [ "domain" ]) [ file "src/App.Domain/Model.fs"; file "src/App.Persistence/Store.fs" ]
    Assert.Equal<string list>([ "persistence" ], ids a.UnexpectedCrossings)
    Assert.Equal(UnexpectedBoundaryCrossing [ b "persistence" ], signal "ORDO-BA-001" a)

[<Fact>]
let ``BA-001 is silent when every observed boundary was declared`` () =
    let a = run (expect [ "domain"; "persistence" ]) [ file "src/App.Domain/Model.fs"; file "src/App.Persistence/Store.fs" ]
    Assert.False(fired "ORDO-BA-001" a)

[<Fact>]
let ``BA-001 sees a crossing made by an import inside a declared boundary`` () =
    let a =
        run (expect [ "cli" ]) [ { file "src/App.Cli/Run.fs" with Imports = [ "System.Net.Http.Headers" ] } ]

    Assert.Equal<string list>([ "provider" ], ids a.UnexpectedCrossings)

    let provider = a.Observed |> List.find (fun o -> o.Boundary = b "provider")
    Assert.Equal<ObservationSource list>([ ByImport("src/App.Cli/Run.fs", "System.Net.Http.Headers") ], provider.Sources)

[<Fact>]
let ``an indicator without a wildcard matches one namespace exactly`` () =
    Assert.True(importMatches "System.IO" "System.IO")
    Assert.False(importMatches "System.IO" "System.IO.Compression")
    Assert.True(importMatches "System.Net.Http*" "System.Net.Http.Headers")
    Assert.False(importMatches "System.Net.Http*" "System.Net")

// ---------------------------------------------------------------------------
// ORDO-BA-002 breadth
// ---------------------------------------------------------------------------

[<Fact>]
let ``BA-002 fires above the boundary threshold and not at it`` () =
    let three = [ file "src/App.Cli/A.fs"; file "src/App.Application/B.fs"; file "src/App.Domain/C.fs" ]
    let atThreshold = run (expect [ "cli"; "application"; "domain" ]) three
    Assert.False(fired "ORDO-BA-002" atThreshold)

    let four =
        run (expect [ "cli"; "application"; "domain"; "infrastructure" ]) (three @ [ file "src/App.Infrastructure/D.fs" ])

    Assert.True(fired "ORDO-BA-002" four)
    // Declared breadth is weak evidence on its own: no recommendation to split.
    Assert.Equal(Low, four.Risk.Level)
    Assert.Equal(NoAction, four.Recommendation)

// ---------------------------------------------------------------------------
// ORDO-BA-003 module scatter
// ---------------------------------------------------------------------------

[<Fact>]
let ``BA-003 fires when one requirement touches more modules than the threshold`` () =
    let files = [ for m in 1..5 -> file (sprintf "src/App.Domain%d/X.fs" m) ]

    let scattered =
        { map with
            Boundaries = map.Boundaries @ [ boundary "domains" Domain [ "src/App.Domain*/**" ] ] }

    let a = assess scattered policy (expect [ "domains" ]) (changes files) |> ok
    Assert.True(fired "ORDO-BA-003" a)

    let four = assess scattered policy (expect [ "domains" ]) (changes (List.take 4 files)) |> ok
    Assert.False(fired "ORDO-BA-003" four)

[<Fact>]
let ``BA-003 ignores supporting files`` () =
    let files = file "src/App.Domain/X.fs" :: [ for m in 1..6 -> file (sprintf "tests/Suite%d/T.fs" m) ]
    let a = run (expect [ "domain"; "tests" ]) files
    Assert.False(fired "ORDO-BA-003" a)

// ---------------------------------------------------------------------------
// ORDO-BA-004 core layer span
// ---------------------------------------------------------------------------

[<Fact>]
let ``BA-004 fires when one change spans three core layers`` () =
    let a =
        run
            (expect [ "cli"; "domain"; "persistence" ])
            [ file "src/App.Cli/A.fs"; file "src/App.Domain/B.fs"; file "src/App.Persistence/C.fs" ]

    Assert.Equal(CoreLayerSpan([ Cli; Persistence; Domain ], 3), signal "ORDO-BA-004" a)

[<Fact>]
let ``BA-004 is silent for two core layers plus non-core layers`` () =
    let a =
        run
            (expect [ "cli"; "domain"; "application"; "infrastructure" ])
            [ file "src/App.Cli/A.fs"
              file "src/App.Domain/B.fs"
              file "src/App.Application/C.fs"
              file "src/App.Infrastructure/D.fs" ]

    Assert.False(fired "ORDO-BA-004" a)

// ---------------------------------------------------------------------------
// ORDO-BA-005 hotspot recurrence
// ---------------------------------------------------------------------------

let private history items =
    items |> List.map (fun (w, paths) -> { WorkItem = w; Paths = paths })

[<Fact>]
let ``BA-005 fires for a file changed by three work items and is strong for orchestration`` () =
    let path = "src/App.Cli/RunCommands.fs"

    let a =
        assess
            map
            policy
            (expect [ "cli" ])
            { changes [ file path ] with History = history [ "WI-0", [ path ]; "WI-00", [ "./" + path ] ] }
        |> ok

    Assert.Equal(HotspotRecurrence(path, [ "WI-0"; "WI-00"; "WI-1" ], 3, true), signal "ORDO-BA-005" a)
    Assert.Equal(Strong, Signal.strength (signal "ORDO-BA-005" a))

[<Fact>]
let ``BA-005 is weak for an ordinary file and silent below the threshold`` () =
    let path = "src/App.Domain/Model.fs"

    let three =
        assess map policy (expect [ "domain" ]) { changes [ file path ] with History = history [ "A", [ path ]; "B", [ path ] ] }
        |> ok

    Assert.Equal(Weak, Signal.strength (signal "ORDO-BA-005" three))

    let two =
        assess map policy (expect [ "domain" ]) { changes [ file path ] with History = history [ "A", [ path ]; "WI-1", [ path ] ] }
        |> ok

    // The current work item appearing in history is not a second item.
    Assert.False(fired "ORDO-BA-005" two)

// ---------------------------------------------------------------------------
// ORDO-BA-006 orchestration churn
// ---------------------------------------------------------------------------

[<Fact>]
let ``BA-006 fires when an orchestration file is modified repeatedly in one change`` () =
    let a = run (expect [ "cli" ]) [ { file "src/App.Cli/RunCommands.fs" with ChangeCount = Some 3 } ]
    Assert.Equal(OrchestrationChurn("src/App.Cli/RunCommands.fs", 3, 3), signal "ORDO-BA-006" a)

[<Fact>]
let ``BA-006 ignores churn in files that are not orchestration, and churn below threshold`` () =
    let a =
        run
            (expect [ "cli"; "domain" ])
            [ { file "src/App.Domain/Model.fs" with ChangeCount = Some 40 }
              { file "src/App.Cli/RunCommands.fs" with ChangeCount = Some 2 } ]

    Assert.False(fired "ORDO-BA-006" a)

// ---------------------------------------------------------------------------
// ORDO-BA-007 responsibility concentration
// ---------------------------------------------------------------------------

[<Fact>]
let ``BA-007 fires when one file hosts two other boundaries' effects`` () =
    let a =
        run (expect [ "cli"; "provider"; "persistence" ]) [ { file "src/App.Cli/Run.fs" with Imports = [ "System.IO"; "System.Net.Http" ] } ]

    Assert.Equal(
        ResponsibilityConcentration("src/App.Cli/Run.fs", Some(b "cli"), [ b "persistence"; b "provider" ], 2),
        signal "ORDO-BA-007" a
    )

    // Even when every boundary was declared: placement, not permission.
    Assert.False(fired "ORDO-BA-001" a)

[<Fact>]
let ``BA-007 is silent for one hosted effect, for a file's own boundary, and for tests`` () =
    let a =
        run
            (expect [ "cli"; "persistence"; "provider"; "tests" ])
            [ { file "src/App.Cli/Run.fs" with Imports = [ "System.IO" ] }
              { file "src/App.Persistence/Store.fs" with Imports = [ "System.IO"; "System.Threading" ] }
              { file "tests/StoreTests.fs" with Imports = [ "System.IO"; "System.Net.Http"; "System.Threading" ] } ]

    Assert.False(fired "ORDO-BA-007" a)
    // The store's locking import is still a crossing (BA-001), just not a
    // concentration: one hosted boundary is below the threshold.
    Assert.Equal<string list>([ "infrastructure" ], ids a.UnexpectedCrossings)

// ---------------------------------------------------------------------------
// ORDO-BA-008 scope expansion ratio
// ---------------------------------------------------------------------------

[<Fact>]
let ``BA-008 fires when observed exceeds twice what was expected`` () =
    let a =
        run (expect [ "domain" ]) [ file "src/App.Domain/A.fs"; file "src/App.Application/B.fs"; file "src/App.Infrastructure/C.fs" ]

    Assert.Equal(Some 3.0, a.AmplificationRatio)
    Assert.True(fired "ORDO-BA-008" a)

[<Fact>]
let ``BA-008 is silent at the ratio, and the ratio is unknown when nothing architectural was expected`` () =
    let a = run (expect [ "domain" ]) [ file "src/App.Domain/A.fs"; file "src/App.Application/B.fs" ]
    Assert.Equal(Some 2.0, a.AmplificationRatio)
    Assert.False(fired "ORDO-BA-008" a)

    let unknown = run (expect [ "tests" ]) [ file "src/App.Domain/A.fs" ]
    Assert.Equal(None, unknown.AmplificationRatio)
    Assert.False(fired "ORDO-BA-008" unknown)

[<Fact>]
let ``expected boundaries the change did not touch are reported, not judged`` () =
    let a = run (expect [ "domain"; "persistence" ]) [ file "src/App.Domain/A.fs" ]
    Assert.Equal<string list>([ "persistence" ], ids a.Unexercised)
    Assert.Empty a.Signals

// ---------------------------------------------------------------------------
// ORDO-BA-009 write-set escape (reuses MutationBoundary)
// ---------------------------------------------------------------------------

let private writeSet =
    { Scopes = [ Feature "model" ]
      Projections = [ { Scope = Feature "model"; Patterns = [ "src/App.Domain/**" ] } ]
      EvaluatorReferences = [ "tests/Acceptance/**" ] }

[<Fact>]
let ``BA-009 reports files outside the declared write set and evaluator mutations`` () =
    let a =
        run
            { expect [ "domain" ] with WriteSet = Some writeSet }
            [ file "src/App.Domain/A.fs"; file "src/App.Domain/B.fs"; file "tests/Acceptance/Spec.fs"; file "docs/x.md" ]

    match signal "ORDO-BA-009" a with
    | WriteSetEscape effects ->
        Assert.Equal<(string * MutationClassification) list>(
            [ "docs/x.md", OutsideBoundary; "tests/Acceptance/Spec.fs", EvaluatorAuthorityMutation ],
            effects |> List.map (fun e -> e.Resource, e.Classification)
        )
    | other -> failwithf "unexpected %A" other

[<Fact>]
let ``BA-009 is silent without a write set, and when the write set admits every file`` () =
    Assert.False(fired "ORDO-BA-009" (run (expect [ "domain" ]) [ file "docs/x.md" ]))

    let admitted = run { expect [ "domain" ] with WriteSet = Some writeSet } [ file "src/App.Domain/A.fs" ]
    Assert.False(fired "ORDO-BA-009" admitted)

// ---------------------------------------------------------------------------
// Risk and recommendation
// ---------------------------------------------------------------------------

[<Fact>]
let ``a large change inside one boundary is not a reason to split`` () =
    let files = [ for i in 1..250 -> { file (sprintf "src/App.Domain/Part%03d/Module%03d.fs" (i % 9) i) with ChangeCount = Some 9 } ]
    let a = run (expect [ "domain" ]) files
    Assert.Empty a.Signals
    Assert.Equal(Low, a.Risk.Level)
    Assert.Equal(NoAction, a.Recommendation)

[<Fact>]
let ``one strong signal is elevated and recommends splitting along the crossing`` () =
    let a = run (expect [ "domain" ]) [ file "src/App.Domain/A.fs"; file "src/App.Application/B.fs" ]
    Assert.Equal(Elevated, a.Risk.Level)
    Assert.Equal(ConsiderSplitAlong [ b "application" ], a.Recommendation)
    Assert.Equal<string list>([ "ORDO-BA-001" ], a.Risk.Evidence |> List.map Signal.id)

[<Fact>]
let ``thresholds come from policy`` () =
    let strict = { policy with ElevatedScore = 1; HighScore = 2 }
    let a = assess map strict (expect [ "domain" ]) (changes [ file "src/App.Domain/A.fs"; file "src/App.Application/B.fs" ]) |> ok
    Assert.Equal(High, a.Risk.Level)
    Assert.Equal(RequireDesignReview [ b "application" ], a.Recommendation)

// ---------------------------------------------------------------------------
// Exceptions: approved scope expansion
// ---------------------------------------------------------------------------

let private approval id boundaries justification authorizer =
    { Expansion =
        { ExpansionId = id
          AddedScopes = []
          AddedProjections = []
          Justification = justification
          AuthorizedBy = authorizer }
      AddedBoundaries = boundaries |> List.map b }

let private crossing = [ file "src/App.Domain/A.fs"; file "src/App.Application/B.fs" ]

[<Fact>]
let ``an approved expansion downgrades the recommendation and stays visible`` () =
    let a =
        run { expect [ "domain" ] with Expansions = [ approval "EXP-1" [ "application" ] "needed" "owner" ] } crossing

    Assert.Equal(FullCoverage, a.Exception.Coverage)
    Assert.Equal(NoAction, a.Recommendation)
    Assert.Equal(Some(ConsiderSplitAlong [ b "application" ]), a.Exception.DowngradedFrom)
    // Risk is evidence; an approval does not erase it.
    Assert.Equal(Elevated, a.Risk.Level)
    Assert.Equal<string list>([ "application" ], ids a.UnexpectedCrossings)
    Assert.Equal<(BoundaryId * string) list>([ b "application", "EXP-1" ], a.Exception.ApprovedCrossings)

[<Fact>]
let ``an expansion without justification or authorizer is rejected and changes nothing`` () =
    for bad in [ approval "EXP-1" [ "application" ] "" "owner"; approval "EXP-1" [ "application" ] "why" " " ] do
        let a = run { expect [ "domain" ] with Expansions = [ bad ] } crossing
        Assert.Empty a.Exception.Applied
        Assert.Equal(1, a.Exception.Rejected.Length)
        Assert.Equal(NoCoverage, a.Exception.Coverage)
        Assert.Equal(ConsiderSplitAlong [ b "application" ], a.Recommendation)
        Assert.Equal(None, a.Exception.DowngradedFrom)

[<Fact>]
let ``an expansion naming an undeclared boundary is rejected`` () =
    let a = run { expect [ "domain" ] with Expansions = [ approval "EXP-1" [ "nowhere" ] "why" "owner" ] } crossing
    Assert.Equal<(ApprovedExpansion * ExpansionRejection) list>(
        [ approval "EXP-1" [ "nowhere" ] "why" "owner", ExpansionNamesUnknownBoundary [ b "nowhere" ] ],
        a.Exception.Rejected
    )

[<Fact>]
let ``partial approval does not downgrade`` () =
    let a =
        run
            { expect [ "domain" ] with Expansions = [ approval "EXP-1" [ "application" ] "why" "owner" ] }
            (crossing @ [ file "src/App.Persistence/C.fs" ])

    Assert.Equal(PartialCoverage, a.Exception.Coverage)
    Assert.Equal<string list>([ "persistence" ], ids a.Exception.UnapprovedCrossings)
    Assert.Equal(None, a.Exception.DowngradedFrom)

[<Fact>]
let ``an expansion's projections widen the write set through MutationBoundary`` () =
    let widen =
        { approval "EXP-1" [ "application" ] "why" "owner" with
            Expansion =
                { (approval "EXP-1" [] "why" "owner").Expansion with
                    AddedScopes = [ Feature "app" ]
                    AddedProjections = [ { Scope = Feature "app"; Patterns = [ "src/App.Application/**" ] } ] } }

    let a = run { expect [ "domain" ] with WriteSet = Some writeSet; Expansions = [ widen ] } crossing
    Assert.False(fired "ORDO-BA-009" a)

// ---------------------------------------------------------------------------
// Input validation
// ---------------------------------------------------------------------------

[<Fact>]
let ``inconsistent inputs are refused with every reason`` () =
    let broken =
        { map with
            Boundaries = map.Boundaries @ [ boundary "domain" Domain [ "x/**" ]; boundary "empty" Domain [] ] }

    match assess broken { policy with HighScore = 1 } { expect [ "ghost" ] with WorkItem = "WI-1" } { changes [ file " " ] with WorkItem = Some "WI-2" } with
    | Ok _ -> failwith "expected refusal"
    | Error errors ->
        Assert.Contains(DuplicateBoundary(b "domain"), errors)
        Assert.Contains(BoundaryWithoutPatterns(b "empty"), errors)
        Assert.Contains(UnknownBoundary("expectation", b "ghost"), errors)
        Assert.Contains(WorkItemMismatch("WI-1", "WI-2"), errors)
        Assert.Contains(EmptyPath "changed file", errors)
        Assert.Contains(InvalidPolicy("highScore", "must exceed elevatedScore"), errors)

// ---------------------------------------------------------------------------
// Wire
// ---------------------------------------------------------------------------

[<Fact>]
let ``a policy document overrides defaults and refuses unknown members`` () =
    let p = decodePolicy """{"schema":"ordo.boundary-policy/1","maxBoundaries":5,"coreLayers":["cli","domain"]}""" |> ok
    Assert.Equal(5, p.MaxBoundaries)
    Assert.Equal<Layer list>([ Cli; Domain ], p.CoreLayers)
    Assert.Equal(AmplificationPolicy.defaults.HighScore, p.HighScore)

    match decodePolicy """{"schema":"ordo.boundary-policy/1","maxBoundary":5}""" with
    | Error(Invalid("$.maxBoundary", _)) -> ()
    | other -> failwithf "expected refusal, got %A" other

[<Fact>]
let ``a document with the wrong schema is refused`` () =
    match decodeMap """{"schema":"ordo.boundary-policy/1"}""" with
    | Error(WrongSchema("ordo.boundary-policy/1", "ordo.boundary-map/1")) -> ()
    | other -> failwithf "expected refusal, got %A" other

[<Fact>]
let ``changed files may be plain text, one path per line`` () =
    let c = decodeChanges "# comment\r\nsrc/A.fs\r\n\r\n  src/B.fs  \n" |> ok
    Assert.Equal<string list>([ "src/A.fs"; "src/B.fs" ], c.Files |> List.map (fun f -> f.Path))

[<Fact>]
let ``a malformed changed-files element fails the document`` () =
    match decodeChanges """{"schema":"ordo.changed-files/1","files":[{"path":"a","changeCount":-1}]}""" with
    | Error(Invalid _) -> ()
    | other -> failwithf "expected refusal, got %A" other

// ---------------------------------------------------------------------------
// Characterization fixtures
// ---------------------------------------------------------------------------

let private fixtures = Path.Combine(__SOURCE_DIRECTORY__, "..", "fixtures", "boundary-amplification")

let private fixture name =
    let read (path: string) = File.ReadAllText(Path.Combine(fixtures, path))
    let map = read "praxis-boundaries.json" |> decodeMap |> ok
    let expectation = read (Path.Combine(name, "expectation.json")) |> decodeExpectation |> ok

    let changedName =
        if File.Exists(Path.Combine(fixtures, name, "changed.json")) then "changed.json" else "changed.txt"

    let changes = read (Path.Combine(name, changedName)) |> decodeChanges |> ok
    map, expectation, changes

[<Fact>]
let ``the Praxis usage-pacing incident is high risk with a decomposition recommendation`` () =
    let map, expectation, changes = fixture "praxis-usage-pacing"
    let a = assess map AmplificationPolicy.defaults expectation changes |> ok

    Assert.Equal(High, a.Risk.Level)

    Assert.Equal<string list>(
        [ "concurrency"; "persistence"; "process"; "provider"; "rendering" ],
        ids a.UnexpectedCrossings
    )

    Assert.Equal<string list>(
        [ "ORDO-BA-001"; "ORDO-BA-002"; "ORDO-BA-004"; "ORDO-BA-006"; "ORDO-BA-007"; "ORDO-BA-008" ],
        a.Signals |> List.map Signal.id
    )

    match a.Recommendation with
    | RequireDesignReview splitAlong ->
        Assert.Contains(b "provider", splitAlong)
        Assert.Contains(b "persistence", splitAlong)
    | other -> failwithf "expected a design review, got %A" other

    Assert.Equal(NoCoverage, a.Exception.Coverage)
    Assert.Empty a.Unclassified

[<Fact>]
let ``the Praxis incident is high risk from paths alone, without import evidence`` () =
    let map, expectation, changes = fixture "praxis-usage-pacing"
    let pathsOnly = { changes with Files = changes.Files |> List.map (fun f -> { f with Imports = [] }) }
    let a = assess map AmplificationPolicy.defaults expectation pathsOnly |> ok
    Assert.Equal(High, a.Risk.Level)
    Assert.Equal<string list>([ "rendering" ], ids a.UnexpectedCrossings)

[<Fact>]
let ``an approved cross-cutting change is visible but approved`` () =
    let map, expectation, changes = fixture "approved-cross-cutting"
    let a = assess map AmplificationPolicy.defaults expectation changes |> ok

    Assert.Equal<string list>([ "cli"; "persistence"; "provider" ], ids a.UnexpectedCrossings)
    Assert.True(fired "ORDO-BA-001" a)
    Assert.Equal(FullCoverage, a.Exception.Coverage)
    Assert.Equal("architecture-owner", (List.exactlyOne a.Exception.Applied).Expansion.AuthorizedBy)
    Assert.Equal(Some(RequireDesignReview [ b "cli"; b "persistence"; b "provider" ]), a.Exception.DowngradedFrom)
    Assert.Equal(ConsiderSplitAlong [ b "cli"; b "persistence"; b "provider" ], a.Recommendation)

// ---------------------------------------------------------------------------
// Determinism
// ---------------------------------------------------------------------------

[<Fact>]
let ``the same inputs in any order produce the same document`` () =
    let map, expectation, changes = fixture "praxis-usage-pacing"
    let render c = assess map AmplificationPolicy.defaults expectation c |> ok |> encodeAssessment "default" |> render

    let baseline = render changes

    let permutations =
        [ { changes with Files = List.rev changes.Files; History = List.rev changes.History }
          { changes with
              Files =
                  changes.Files
                  |> List.map (fun f -> { f with Imports = List.rev f.Imports })
                  |> List.sortBy (fun f -> f.Path.Length) }
          changes ]

    for c in permutations do
        Assert.Equal(baseline, render c)

    // And the document is valid JSON carrying the contract identity.
    match parse baseline |> Result.bind (requiredMember "schema") with
    | Ok(JString s) -> Assert.Equal(AssessmentSchema, s)
    | other -> failwithf "unexpected %A" other
