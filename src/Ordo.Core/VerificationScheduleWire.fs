/// Canonical, content-addressed rendering of `ordo.verification-schedule/1`.
/// The rendering has no timestamp and sorts object keys, so the same plan and
/// context always render to the same bytes (asserted in
/// `tests/Ordo.Tests/VerificationScheduleTests.fs`).
module Ordo.Core.VerificationScheduleWire

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.VerificationSchedule

[<Literal>]
let ScheduleSchema = "ordo.verification-schedule/1"

let private jStrings (values: string list) = JArray(values |> List.map JString)

let private stateJson state =
    match state with
    | ObligationState.Satisfied -> JObject [ "state", JString "satisfied" ]
    | ObligationState.Failing -> JObject [ "state", JString "failing" ]
    | ObligationState.NotRun -> JObject [ "state", JString "notRun" ]
    | ObligationState.BlockedRun -> JObject [ "state", JString "blocked" ]
    | ObligationState.Stale mismatched -> JObject [ "state", JString "stale"; "mismatchedBindings", jStrings mismatched ]
    | ObligationState.TestMissing reason -> JObject [ "state", JString "testMissing"; "reason", JString reason ]

let workKindName kind =
    match kind with
    | WorkKind.AuthorTest -> "authorTest"
    | WorkKind.FixImplementation -> "fixImplementation"
    | WorkKind.RunVerification -> "runVerification"
    | WorkKind.UnblockVerification -> "unblockVerification"
    | WorkKind.RefreshEvidence -> "refreshEvidence"

let private testJson test =
    match test with
    | Identified id -> JObject [ "status", JString "identified"; "id", JString id ]
    | MissingTest reason -> JObject [ "status", JString "missing"; "reason", JString reason ]

let private workJson (item: WorkItem) =
    JObject
        [ "requirementId", JString item.RequirementId
          "obligationId", JString item.ObligationId
          "kind", JString(workKindName item.Kind)
          "mandatory", JBool item.Mandatory
          "method", JString item.Method
          "test", testJson item.Test
          "detail", JString item.Detail ]

let private edgeJson (edge: DependencyEdge) =
    JObject [ "requirement", JString edge.Requirement; "dependsOn", JString edge.DependsOn ]

let private requirementJson (row: ScheduledRequirement) =
    JObject
        [ "id", JString row.Id
          "priority", JString row.Priority
          "rank", JInt(int64 row.Rank)
          "score",
          JObject
              [ "mandatoryObligations", JInt(int64 row.Score.MandatoryObligations)
                "transitiveDependents", JInt(int64 row.Score.TransitiveDependents)
                "mandatoryUnblocked", JInt(int64 row.Score.MandatoryUnblocked) ]
          "dependsOn", jStrings row.DependsOn
          "dependents", jStrings row.Dependents
          "obligations",
          JArray(row.Obligations |> List.map (fun (id, state) -> match stateJson state with
                                                                  | JObject members -> JObject(("id", JString id) :: members)
                                                                  | other -> other))
          "acceptanceBlocker", JBool row.AcceptanceBlocker
          "inCycle", JBool row.InCycle ]

let private body (schedule: VerificationSchedule) =
    let count name (n: int) = name, JInt(int64 n)

    [ "schema", JString ScheduleSchema
      "planDigest", JString schedule.PlanDigest
      "heuristic",
      JString "rank by mandatoryUnblocked desc (P0 obligations of the requirement and all transitive dependents), then transitiveDependents desc, then id ordinal asc; ranking never excludes a requirement"
      "summary",
      JObject
          [ count "requirements" schedule.Requirements.Length
            count "acceptanceBlockers" schedule.AcceptanceBlockers.Length
            count "dependencies" schedule.Dependencies.Length
            count "unknownDependencies" schedule.UnknownDependencies.Length
            count "cycles" schedule.Cycles.Length
            count "implementationTasks" schedule.ImplementationTasks.Length
            count "requiredVerification" schedule.RequiredVerification.Length
            count "staleEvidence" schedule.StaleEvidence.Length
            count "independentWaves" schedule.IndependentWork.Length
            count "cycleBlocked" schedule.CycleBlocked.Length ]
      "acceptanceBlockers", jStrings schedule.AcceptanceBlockers
      "requirements", JArray(schedule.Requirements |> List.map requirementJson)
      "dependencies", JArray(schedule.Dependencies |> List.map edgeJson)
      "unknownDependencies", JArray(schedule.UnknownDependencies |> List.map edgeJson)
      "cycles", JArray(schedule.Cycles |> List.map jStrings)
      "implementationTasks", JArray(schedule.ImplementationTasks |> List.map workJson)
      "requiredVerification", JArray(schedule.RequiredVerification |> List.map workJson)
      "staleEvidence",
      JArray(
          schedule.StaleEvidence
          |> List.map (fun s ->
              JObject [ "requirementId", JString s.RequirementId; "obligationId", JString s.ObligationId; "mismatchedBindings", jStrings s.Mismatched ])
      )
      "independentWork", JArray(schedule.IndependentWork |> List.map jStrings)
      "cycleBlocked", jStrings schedule.CycleBlocked ]

let private sha256 (text: string) =
    "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes text)).ToLowerInvariant()

/// The schedule's digest: SHA-256 over the canonical rendering of the
/// schedule without its own `scheduleDigest` member.
let digest (schedule: VerificationSchedule) = body schedule |> JObject |> renderCanonical |> sha256

/// Canonical bytes (sorted keys) including `scheduleDigest`.
let render (schedule: VerificationSchedule) =
    (body schedule @ [ "scheduleDigest", JString(digest schedule) ]) |> JObject |> renderCanonical
