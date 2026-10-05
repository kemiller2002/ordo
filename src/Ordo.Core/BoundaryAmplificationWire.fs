/// The wire vocabulary of boundary-amplification assessment.
///
/// Inputs (all repository-owned, versioned by their `schema` member):
///   `ordo.boundary-map/1`         path globs → boundaries, import indicators
///   `ordo.boundary-policy/1`      threshold overrides (optional file)
///   `ordo.boundary-expectation/1` what one work item declared
///   `ordo.changed-files/1`        what changed (or plain text, one path per line)
/// Output:
///   `ordo.boundary-amplification/1`
///
/// Decoders are strict: an unknown layer name is a custom layer, but an
/// unknown member in a policy, a wrong schema, or a malformed element fails
/// the whole document rather than being dropped (ORDO-8404).
/// `schemas/ordo-boundary-amplification.v1.schema.json` documents the same
/// shapes for non-.NET hosts.
module Ordo.Core.BoundaryAmplificationWire

open System
open Ordo.Core.Json
open Ordo.Core.MutationBoundary
open Ordo.Core.BoundaryAmplification

[<Literal>]
let AssessmentSchema = "ordo.boundary-amplification/1"

[<Literal>]
let MapSchema = "ordo.boundary-map/1"

[<Literal>]
let PolicySchema = "ordo.boundary-policy/1"

[<Literal>]
let ExpectationSchema = "ordo.boundary-expectation/1"

[<Literal>]
let ChangesSchema = "ordo.changed-files/1"

type DecodeError =
    | Malformed of JsonError
    | WrongSchema of found: string * expected: string
    | Invalid of path: string * why: string

[<RequireQualifiedAccess>]
module DecodeError =

    let describe error =
        match error with
        | Malformed(MalformedJson m) -> sprintf "not valid JSON: %s" m
        | Malformed(UnexpectedType(p, t)) -> sprintf "%s: expected %s" p t
        | Malformed(MissingMember p) -> sprintf "%s: required member is missing" p
        | Malformed(UnsupportedNumber p) -> sprintf "%s: number out of range" p
        | WrongSchema(found, expected) -> sprintf "schema is %s; expected %s" found expected
        | Invalid(p, why) -> sprintf "%s: %s" p why

// ---------------------------------------------------------------------------
// Decoding helpers
// ---------------------------------------------------------------------------

let private ( >>= ) r f = Result.bind f r

let private mal r = Result.mapError Malformed r

let private collectAll (results: Result<'a, DecodeError> list) =
    let rec go acc rest =
        match rest with
        | [] -> Ok(List.rev acc)
        | Error e :: _ -> Error e
        | Ok v :: tail -> go (v :: acc) tail

    go [] results

let private optionalMember name path doc =
    tryMember name doc |> Result.mapError (fun _ -> Malformed(UnexpectedType(path, "object")))

let private str path value = asString path value |> mal

let private strings path value =
    asArray path value |> mal
    >>= (List.mapi (fun i v -> str (sprintf "%s[%d]" path i) v) >> collectAll)

let private optStrings name path doc =
    optionalMember name path doc
    >>= (function
    | None
    | Some JNull -> Ok []
    | Some v -> strings (path + "." + name) v)

let private reqString name path doc =
    requiredMember name doc
    |> Result.mapError (fun _ -> Malformed(MissingMember(path + "." + name)))
    >>= str (path + "." + name)

let private optString name path doc =
    optionalMember name path doc
    >>= (function
    | None
    | Some JNull -> Ok None
    | Some v -> str (path + "." + name) v |> Result.map Some)

let private schemaOf expected doc =
    reqString "schema" "$" doc
    >>= fun found -> if found = expected then Ok doc else Error(WrongSchema(found, expected))

let private objects name path doc decode =
    optionalMember name path doc
    >>= (function
    | None
    | Some JNull -> Ok []
    | Some v ->
        asArray (path + "." + name) v |> mal
        >>= (List.mapi (fun i item -> decode (sprintf "%s.%s[%d]" path name i) item) >> collectAll))

let private parseText text = parse text |> mal

// ---------------------------------------------------------------------------
// Inputs
// ---------------------------------------------------------------------------

let private decodeBoundary path doc =
    reqString "id" path doc
    >>= fun id ->
        reqString "layer" path doc
        >>= fun layer ->
            match Layer.fromWire layer with
            | None -> Error(Invalid(path + ".layer", "must be a non-empty layer name"))
            | Some l ->
                optStrings "patterns" path doc
                |> Result.map (fun patterns ->
                    { Id = BoundaryId id
                      Layer = l
                      Patterns = patterns })

let private decodeIndicator path doc =
    reqString "boundary" path doc
    >>= fun b ->
        optStrings "imports" path doc
        |> Result.map (fun imports -> { Boundary = BoundaryId b; Imports = imports })

let decodeMap (text: string) : Result<BoundaryMap, DecodeError> =
    parseText text
    >>= schemaOf MapSchema
    >>= fun doc ->
        objects "boundaries" "$" doc decodeBoundary
        >>= fun boundaries ->
            objects "indicators" "$" doc decodeIndicator
            >>= fun indicators ->
                optStrings "orchestration" "$" doc
                |> Result.map (fun orchestration ->
                    { Boundaries = boundaries
                      Indicators = indicators
                      Orchestration = orchestration })

let private policyMembers =
    [ "schema"
      "maxBoundaries"
      "maxModules"
      "moduleDepth"
      "coreLayers"
      "coreLayerSpan"
      "hotspotWorkItems"
      "orchestrationChurn"
      "concentrationBoundaries"
      "maxExpansionRatio"
      "elevatedScore"
      "highScore" ]

/// Overrides on top of `AmplificationPolicy.defaults`. Unknown members are
/// refused so that a misspelt threshold cannot silently fall back to its
/// default.
let decodePolicy (text: string) : Result<AmplificationPolicy, DecodeError> =
    parseText text
    >>= schemaOf PolicySchema
    >>= fun doc ->
        let unknown =
            match doc with
            | JObject members -> members |> List.map fst |> List.filter (fun m -> not (List.contains m policyMembers))
            | _ -> []

        match unknown with
        | m :: _ -> Error(Invalid("$." + m, "unknown policy member"))
        | [] ->

        let int name fallback =
            optionalMember name "$" doc
            >>= (function
            | None -> Ok fallback
            | Some v -> asInt ("$." + name) v |> mal)

        let d = AmplificationPolicy.defaults

        int "maxBoundaries" d.MaxBoundaries
        >>= fun maxBoundaries ->
        int "maxModules" d.MaxModules
        >>= fun maxModules ->
        int "moduleDepth" d.ModuleDepth
        >>= fun moduleDepth ->
        int "coreLayerSpan" d.CoreLayerSpan
        >>= fun coreLayerSpan ->
        int "hotspotWorkItems" d.HotspotWorkItems
        >>= fun hotspot ->
        int "orchestrationChurn" d.OrchestrationChurn
        >>= fun churn ->
        int "concentrationBoundaries" d.ConcentrationBoundaries
        >>= fun concentration ->
        int "elevatedScore" d.ElevatedScore
        >>= fun elevated ->
        int "highScore" d.HighScore
        >>= fun high ->
        (optionalMember "maxExpansionRatio" "$" doc
         >>= (function
         | None -> Ok d.MaxExpansionRatio
         | Some v -> asFloat "$.maxExpansionRatio" v |> mal))
        >>= fun ratio ->
        (optionalMember "coreLayers" "$" doc
         >>= (function
         | None -> Ok d.CoreLayers
         | Some v ->
             strings "$.coreLayers" v
             >>= (List.map (fun l ->
                      match Layer.fromWire l with
                      | Some layer -> Ok layer
                      | None -> Error(Invalid("$.coreLayers", "layer names must be non-empty")))
                  >> collectAll)))
        |> Result.map (fun coreLayers ->
            { MaxBoundaries = maxBoundaries
              MaxModules = maxModules
              ModuleDepth = moduleDepth
              CoreLayers = coreLayers
              CoreLayerSpan = coreLayerSpan
              HotspotWorkItems = hotspot
              OrchestrationChurn = churn
              ConcentrationBoundaries = concentration
              MaxExpansionRatio = ratio
              ElevatedScore = elevated
              HighScore = high })

let private decodeScope path raw =
    match SemanticScope.fromWire raw with
    | Some s -> Ok s
    | None -> Error(Invalid(path, sprintf "'%s' is not a semantic scope (feature:|cluster:|authority:|capability:)" raw))

let private decodeScopes name path doc =
    optStrings name path doc
    >>= (List.map (decodeScope (path + "." + name)) >> collectAll)

let private decodeProjection path doc =
    reqString "scope" path doc
    >>= decodeScope (path + ".scope")
    >>= fun scope -> optStrings "patterns" path doc |> Result.map (fun ps -> { Scope = scope; Patterns = ps })

let private decodeWriteSet path doc =
    decodeScopes "scopes" path doc
    >>= fun scopes ->
        objects "projections" path doc decodeProjection
        >>= fun projections ->
            optStrings "evaluatorReferences" path doc
            |> Result.map (fun refs ->
                { Scopes = scopes
                  Projections = projections
                  EvaluatorReferences = refs })

let private decodeExpansion path doc =
    reqString "expansionId" path doc
    >>= fun id ->
        optString "justification" path doc
        >>= fun justification ->
            optString "authorizedBy" path doc
            >>= fun authorizedBy ->
                optStrings "addedBoundaries" path doc
                >>= fun boundaries ->
                    decodeScopes "addedScopes" path doc
                    >>= fun scopes ->
                        objects "addedProjections" path doc decodeProjection
                        |> Result.map (fun projections ->
                            { Expansion =
                                { ExpansionId = id
                                  AddedScopes = scopes
                                  AddedProjections = projections
                                  // Absent and blank are the same: no
                                  // justification. `MutationBoundary.expand`
                                  // refuses it; the refusal is reported.
                                  Justification = justification |> Option.defaultValue ""
                                  AuthorizedBy = authorizedBy |> Option.defaultValue "" }
                              AddedBoundaries = boundaries |> List.map BoundaryId })

let decodeExpectation (text: string) : Result<Expectation, DecodeError> =
    parseText text
    >>= schemaOf ExpectationSchema
    >>= fun doc ->
        reqString "workItem" "$" doc
        >>= fun workItem ->
            optString "requirement" "$" doc
            >>= fun requirement ->
                optStrings "expectedBoundaries" "$" doc
                >>= fun expected ->
                    (optionalMember "writeSet" "$" doc
                     >>= (function
                     | None
                     | Some JNull -> Ok None
                     | Some ws -> decodeWriteSet "$.writeSet" ws |> Result.map Some))
                    >>= fun writeSet ->
                        objects "approvedExpansions" "$" doc decodeExpansion
                        |> Result.map (fun expansions ->
                            { WorkItem = workItem
                              Requirement = requirement
                              ExpectedBoundaries = expected |> List.map BoundaryId
                              WriteSet = writeSet
                              Expansions = expansions })

let private decodeChangedFile path value =
    match value with
    | JString p ->
        Ok
            { Path = p
              ChangeCount = None
              Imports = [] }
    | doc ->
        reqString "path" path doc
        >>= fun p ->
            (optionalMember "changeCount" path doc
             >>= (function
             | None
             | Some JNull -> Ok None
             | Some v ->
                 asInt (path + ".changeCount") v |> mal
                 >>= fun n ->
                     if n < 0 then
                         Error(Invalid(path + ".changeCount", "must not be negative"))
                     else
                         Ok(Some n)))
            >>= fun count ->
                optStrings "imports" path doc
                |> Result.map (fun imports ->
                    { Path = p
                      ChangeCount = count
                      Imports = imports })

let private decodePrior path doc =
    reqString "workItem" path doc
    >>= fun w -> optStrings "paths" path doc |> Result.map (fun ps -> { WorkItem = w; Paths = ps })

/// A changed-files document, or plain text with one path per line (blank
/// lines and `#` comments ignored) so `git diff --name-only` can feed it
/// directly.
let decodeChanges (text: string) : Result<ChangeSet, DecodeError> =
    let trimmed = text.Trim()

    if trimmed.StartsWith "{" then
        parseText trimmed
        >>= schemaOf ChangesSchema
        >>= fun doc ->
            optString "workItem" "$" doc
            >>= fun workItem ->
                objects "files" "$" doc decodeChangedFile
                >>= fun files ->
                    objects "history" "$" doc decodePrior
                    |> Result.map (fun history ->
                        { WorkItem = workItem
                          Files = files
                          History = history })
    else
        Ok
            { WorkItem = None
              Files =
                trimmed.Split('\n')
                |> List.ofArray
                |> List.map (fun l -> l.Trim())
                |> List.filter (fun l -> l <> "" && not (l.StartsWith "#"))
                |> List.map (fun p ->
                    { Path = p
                      ChangeCount = None
                      Imports = [] })
              History = [] }

// ---------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------

let private ids (bs: BoundaryId list) = JArray(bs |> List.map (fun b -> JString b.Value))
let private strs (xs: string list) = JArray(xs |> List.map JString)

let private optStr value =
    match value with
    | Some(s: string) -> JString s
    | None -> JNull

let private sourceJson source =
    match source with
    | ByPath p -> JObject [ "kind", JString "path"; "path", JString p ]
    | ByImport(p, i) -> JObject [ "kind", JString "import"; "path", JString p; "import", JString i ]

let private observedJson (o: ObservedBoundary) =
    JObject
        [ "boundary", JString o.Boundary.Value
          "layer", JString(Layer.toWire o.Layer)
          "sources", JArray(o.Sources |> List.map sourceJson) ]

let private classificationToWire classification =
    match classification with
    | WithinBoundary _ -> "within-boundary"
    | OutsideBoundary -> "outside-boundary"
    | EvaluatorAuthorityMutation -> "evaluator-authority-mutation"

let private signalEvidence signal =
    match signal with
    | UnexpectedBoundaryCrossing bs -> [ "boundaries", ids bs ]
    | BoundaryBreadth(bs, t) -> [ "boundaries", ids bs; "count", JInt(int64 bs.Length); "threshold", JInt(int64 t) ]
    | ModuleScatter(ms, t) -> [ "modules", strs ms; "count", JInt(int64 ms.Length); "threshold", JInt(int64 t) ]
    | CoreLayerSpan(ls, t) ->
        [ "layers", strs (ls |> List.map Layer.toWire)
          "count", JInt(int64 ls.Length)
          "threshold", JInt(int64 t) ]
    | HotspotRecurrence(p, wis, t, o) ->
        [ "path", JString p
          "workItems", strs wis
          "count", JInt(int64 wis.Length)
          "threshold", JInt(int64 t)
          "orchestration", JBool o ]
    | OrchestrationChurn(p, n, t) -> [ "path", JString p; "changes", JInt(int64 n); "threshold", JInt(int64 t) ]
    | ResponsibilityConcentration(p, home, hosted, t) ->
        [ "path", JString p
          "home", (home |> Option.map (fun b -> JString b.Value) |> Option.defaultValue JNull)
          "hosted", ids hosted
          "threshold", JInt(int64 t) ]
    | ScopeExpansionRatio(r, t) -> [ "ratio", JFloat r; "threshold", JFloat t ]
    | WriteSetEscape effects ->
        [ "effects",
          JArray(
              effects
              |> List.map (fun e ->
                  JObject
                      [ "path", JString e.Resource
                        "classification", JString(classificationToWire e.Classification) ])
          ) ]
    | UnclassifiedPaths ps -> [ "paths", strs ps ]

let private strengthToWire strength =
    match strength with
    | Strong -> "strong"
    | Weak -> "weak"
    | Informational -> "informational"

let private signalJson signal =
    JObject
        [ "id", JString(Signal.id signal)
          "name", JString(Signal.name signal)
          "strength", JString(strengthToWire (Signal.strength signal))
          "weight", JInt(int64 (Signal.weight signal))
          "description", JString(Signal.describe signal)
          "evidence", JObject(signalEvidence signal) ]

let private recommendationJson recommendation =
    JObject
        [ "kind", JString(Recommendation.toWire recommendation)
          "splitAlong", ids (Recommendation.boundaries recommendation) ]

let private rejectionToWire rejection =
    match rejection with
    | InvalidExpansion ExpansionWithoutJustification -> "justification is required"
    | InvalidExpansion ExpansionWithoutAuthorizer -> "authorizedBy is required"
    | InvalidExpansion(ExpansionIntoEvaluatorAuthority refs) ->
        sprintf "would make evaluator authority writable: %s" (String.concat ", " refs)
    | InvalidExpansion(ProjectionWithoutScope scope) ->
        sprintf "projection for %s has no matching scope" (SemanticScope.toWire scope)
    | ExpansionNamesUnknownBoundary bs ->
        sprintf "names undeclared boundaries: %s" (bs |> List.map (fun b -> b.Value) |> String.concat ", ")

let private expansionJson (a: ApprovedExpansion) =
    JObject
        [ "expansionId", JString a.Expansion.ExpansionId
          "addedBoundaries", ids a.AddedBoundaries
          "justification", JString a.Expansion.Justification
          "authorizedBy", JString a.Expansion.AuthorizedBy ]

let private policyJson (source: string) (p: AmplificationPolicy) =
    JObject
        [ "source", JString source
          "maxBoundaries", JInt(int64 p.MaxBoundaries)
          "maxModules", JInt(int64 p.MaxModules)
          "moduleDepth", JInt(int64 p.ModuleDepth)
          "coreLayers", strs (p.CoreLayers |> List.map Layer.toWire)
          "coreLayerSpan", JInt(int64 p.CoreLayerSpan)
          "hotspotWorkItems", JInt(int64 p.HotspotWorkItems)
          "orchestrationChurn", JInt(int64 p.OrchestrationChurn)
          "concentrationBoundaries", JInt(int64 p.ConcentrationBoundaries)
          "maxExpansionRatio", JFloat p.MaxExpansionRatio
          "elevatedScore", JInt(int64 p.ElevatedScore)
          "highScore", JInt(int64 p.HighScore) ]

/// The `ordo.boundary-amplification/1` document. `policySource` says where
/// the thresholds came from (`default` or the policy file's path).
let encodeAssessment (policySource: string) (a: Assessment) : JsonValue =
    JObject
        [ "schema", JString AssessmentSchema
          "workItem", JString a.WorkItem
          "requirement", optStr a.Requirement
          "policy", policyJson policySource a.Policy
          "expected", ids a.Expected
          "observed", JArray(a.Observed |> List.map observedJson)
          "supporting", JArray(a.Supporting |> List.map observedJson)
          "unclassified", strs a.Unclassified
          "unexpectedCrossings", ids a.UnexpectedCrossings
          "unexercised", ids a.Unexercised
          "amplificationRatio", (a.AmplificationRatio |> Option.map JFloat |> Option.defaultValue JNull)
          "signals", JArray(a.Signals |> List.map signalJson)
          "risk",
          JObject
              [ "level", JString(RiskLevel.toWire a.Risk.Level)
                "score", JInt(int64 a.Risk.Score)
                "evidence", strs (a.Risk.Evidence |> List.map Signal.id) ]
          "recommendation", recommendationJson a.Recommendation
          "exception",
          JObject
              [ "coverage", JString(ExceptionCoverage.toWire a.Exception.Coverage)
                "applied", JArray(a.Exception.Applied |> List.map expansionJson)
                "rejected",
                JArray(
                    a.Exception.Rejected
                    |> List.map (fun (e, why) ->
                        JObject [ "expansionId", JString e.Expansion.ExpansionId; "reason", JString(rejectionToWire why) ])
                )
                "approvedCrossings",
                JArray(
                    a.Exception.ApprovedCrossings
                    |> List.map (fun (b, e) -> JObject [ "boundary", JString b.Value; "expansionId", JString e ])
                )
                "unapprovedCrossings", ids a.Exception.UnapprovedCrossings
                "downgradedFrom",
                (a.Exception.DowngradedFrom
                 |> Option.map recommendationJson
                 |> Option.defaultValue JNull) ] ]

let describeAssessmentError error =
    match error with
    | DuplicateBoundary b -> sprintf "boundary '%s' is declared more than once" b.Value
    | BoundaryWithoutPatterns b -> sprintf "boundary '%s' declares no path patterns" b.Value
    | UnknownBoundary(context, b) -> sprintf "%s names boundary '%s', which the boundary map does not declare" context b.Value
    | EmptyPath context -> sprintf "%s has an empty path" context
    | WorkItemMismatch(expected, changed) ->
        sprintf "expectation is for work item '%s' but the changed-files document is for '%s'" expected changed
    | MissingWorkItem -> "expectation has no workItem"
    | InvalidPolicy(field, why) -> sprintf "policy %s %s" field why

/// Human-readable rendering of the same assessment.
let renderText (a: Assessment) : string list =
    let names (bs: BoundaryId list) =
        match bs with
        | [] -> "(none)"
        | _ -> bs |> List.map (fun b -> b.Value) |> String.concat ", "

    let ratio =
        a.AmplificationRatio
        |> Option.map (fun r -> r.ToString("0.##", Globalization.CultureInfo.InvariantCulture))
        |> Option.defaultValue "unknown (nothing architectural expected)"

    [ yield sprintf "Boundary amplification: %s%s" a.WorkItem (a.Requirement |> Option.map (sprintf " — %s") |> Option.defaultValue "")
      yield sprintf "  risk:            %s (score %d)" (RiskLevel.toWire a.Risk.Level) a.Risk.Score
      yield
          sprintf
              "  recommendation:  %s%s"
              (Recommendation.toWire a.Recommendation)
              (match Recommendation.boundaries a.Recommendation with
               | [] -> ""
               | bs -> sprintf " (%s)" (names bs))
      yield sprintf "  expected:        %s" (names a.Expected)
      yield sprintf "  observed:        %s" (names (a.Observed |> List.map (fun o -> o.Boundary)))
      yield sprintf "  unexpected:      %s" (names a.UnexpectedCrossings)
      yield sprintf "  ratio:           %s" ratio
      if not a.Unclassified.IsEmpty then
          yield sprintf "  unclassified:    %s" (String.concat ", " a.Unclassified)
      yield "  signals:"
      if a.Signals.IsEmpty then
          yield "    (none)"
      for s in a.Signals do
          yield sprintf "    %s %-13s %s" (Signal.id s) (strengthToWire (Signal.strength s)) (Signal.describe s)
      match a.Exception.Coverage with
      | NotApplicable when a.Exception.Applied.IsEmpty && a.Exception.Rejected.IsEmpty -> ()
      | coverage ->
          yield sprintf "  approval:        %s coverage of unexpected crossings" (ExceptionCoverage.toWire coverage)

          for e in a.Exception.Applied do
              yield
                  sprintf
                      "    approved %s by %s: %s (admits %s)"
                      e.Expansion.ExpansionId
                      e.Expansion.AuthorizedBy
                      e.Expansion.Justification
                      (names e.AddedBoundaries)

          for (e, why) in a.Exception.Rejected do
              yield sprintf "    rejected %s: %s" e.Expansion.ExpansionId (rejectionToWire why)

          match a.Exception.DowngradedFrom with
          | Some r -> yield sprintf "    recommendation downgraded from %s by approval" (Recommendation.toWire r)
          | None -> () ]
