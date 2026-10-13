/// Construction decision records, `ordo.construction-decisions/1`
/// (CONSTRUCT-008). An ECIR blueprint records decision and interface nodes
/// with prose justifications only; the states, transitions and ports a
/// decision fixes exist only in requirement text. A decision record set
/// carries that structured content for approved nodes so that Ordo can
/// generate a plan without inventing behavior.
///
/// The set is authored and approved once, by an operator, and a host binds
/// its digest (`digest`) into the signed grant. Reading it yields an
/// untrusted value: only `ConstructionDefaultPlan.generateDefaultPlan`
/// checks it against a prepared cohort, and refuses anything that
/// contradicts the blueprint. A record is never authority by itself.
/// See docs/architecture/construction-default-plan.md.
module Ordo.Core.ConstructionDecisions

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.StrictJson
open Ordo.Core.ConstructionPlan

[<Literal>]
let DecisionsSchemaVersion = "ordo.construction-decisions/1"

type RecordedState = { Name: string; Role: StateRole }

type RecordedTransition =
    { Command: string
      From: string
      To: string
      /// Requirements this transition implements. Each must be owned by the
      /// record's node in the blueprint.
      RequirementKeys: string list }

/// A port an interface (or decision) node fixes.
type RecordedPort =
    | RecordedCurrentTime of requirementKeys: string list
    /// `onFailure = None` is the wire `null`: NOT DECIDED. The generator
    /// never picks a disposition for it; it emits an obligation.
    | RecordedTransitionEffect of command: string * onFailure: EffectFailureDisposition option * requirementKeys: string list

type StateModelContent =
    { Workflow: string
      States: RecordedState list
      Transitions: RecordedTransition list }

type DecisionContent =
    /// Wire kind `stateModel`: the states and transitions a decision fixes.
    | StateModel of StateModelContent
    /// Wire kind `portContract`: the orchestration ports an interface fixes.
    | PortContract of RecordedPort list

type DecisionRecord = { NodeId: string; Content: DecisionContent }

/// The record set as read. Approved means approved by the operator and bound
/// by digest; it is still checked against the prepared cohort and blueprint
/// before anything is generated from it.
type ApprovedDecisionRecords =
    { SchemaVersion: string
      /// The canonical blueprint digest the records were approved against.
      BlueprintDigest: string
      CohortId: string
      Records: DecisionRecord list }

// ---------------------------------------------------------------------------
// Reading. Strict: exactly the permitted members, once each; an unknown
// content kind, port, role or disposition is refused by name.
// ---------------------------------------------------------------------------

let private roleOf path raw =
    match raw with
    | "initial" -> Ok Initial
    | "ordinary" -> Ok Ordinary
    | "terminal" -> Ok Terminal
    | other -> fail path ("unknown state role: " + other)

let private keysField path p =
    result {
        let! raw = field path "requirementKeys" p
        return! strings (path + ".requirementKeys") raw
    }

let private state path value =
    result {
        let! p = fields path [ "name"; "role" ] [] value
        let! name = textField path "name" p
        let! roleText = textField path "role" p
        let! role = roleOf (path + ".role") roleText
        return { RecordedState.Name = name; Role = role }
    }

let private transition path value =
    result {
        let! p = fields path [ "command"; "from"; "to"; "requirementKeys" ] [] value
        let! command = textField path "command" p
        let! from = textField path "from" p
        let! target = textField path "to" p
        let! keys = keysField path p
        return { RecordedTransition.Command = command; From = from; To = target; RequirementKeys = keys }
    }

let private onFailure path value =
    match value with
    | JNull -> Ok None
    | JString "remain" -> Ok(Some EffectFailureDisposition.Remain)
    | JString "proceed" -> Ok(Some EffectFailureDisposition.Proceed)
    | JString "unresolved" -> Ok(Some EffectFailureDisposition.Unresolved)
    | JString other -> fail path ("unsupported onFailure: " + other)
    | _ -> fail path "expected null or a disposition string"

let private port path value =
    result {
        let! kind =
            match value with
            | JObject properties ->
                match properties |> List.filter (fun (name, _) -> name = "port") with
                | [ _, JString port ] -> Ok port
                | _ -> fail path "expected exactly one string member port"
            | _ -> fail path "expected object"
        match kind with
        | "currentTime" ->
            let! p = fields path [ "port"; "requirementKeys" ] [] value
            let! keys = keysField path p
            return RecordedCurrentTime keys
        | "transitionEffect" ->
            let! p = fields path [ "port"; "command"; "onFailure"; "requirementKeys" ] [] value
            let! command = textField path "command" p
            let! raw = field path "onFailure" p
            let! disposition = onFailure (path + ".onFailure") raw
            let! keys = keysField path p
            return RecordedTransitionEffect(command, disposition, keys)
        | other -> return! fail (path + ".port") ("unsupported port: " + other)
    }

let private content path value =
    result {
        let! kind =
            match value with
            | JObject properties ->
                match properties |> List.filter (fun (name, _) -> name = "kind") with
                | [ _, JString kind ] -> Ok kind
                | _ -> fail path "expected exactly one string member kind"
            | _ -> fail path "expected object"
        match kind with
        | "stateModel" ->
            let! p = fields path [ "kind"; "workflow"; "states"; "transitions" ] [] value
            let! workflow = textField path "workflow" p
            let! rawStates = field path "states" p
            let! states = items (path + ".states") state rawStates
            let! rawTransitions = field path "transitions" p
            let! transitions = items (path + ".transitions") transition rawTransitions
            return StateModel { Workflow = workflow; States = states; Transitions = transitions }
        | "portContract" ->
            let! p = fields path [ "kind"; "ports" ] [] value
            let! rawPorts = field path "ports" p
            let! ports = items (path + ".ports") port rawPorts
            return PortContract ports
        | other -> return! fail (path + ".kind") ("unsupported content kind: " + other)
    }

let private record path value =
    result {
        let! p = fields path [ "nodeId"; "content" ] [] value
        let! nodeId = textField path "nodeId" p
        let! raw = field path "content" p
        let! decided = content (path + ".content") raw
        return { NodeId = nodeId; Content = decided }
    }

/// Strict reader. The result is untrusted until `generateDefaultPlan` checks
/// it against a prepared cohort.
let readDecisionRecords (input: string) : Result<ApprovedDecisionRecords, string> =
    result {
        let! json = parse input |> Result.mapError (sprintf "invalid JSON: %A")
        let! p = fields "$" [ "schemaVersion"; "blueprintDigest"; "cohortId"; "records" ] [] json
        let! schema = textField "$" "schemaVersion" p
        let! () = if schema = DecisionsSchemaVersion then Ok() else fail "$.schemaVersion" ("unsupported " + schema)
        let! blueprint = textField "$" "blueprintDigest" p
        let! cohort = textField "$" "cohortId" p
        let! rawRecords = field "$" "records" p
        let! records = items "$.records" record rawRecords
        return { ApprovedDecisionRecords.SchemaVersion = schema; BlueprintDigest = blueprint; CohortId = cohort; Records = records }
    }

// ---------------------------------------------------------------------------
// Canonical form. List order carries no meaning in a record set, so the
// canonical form sorts every list; identity is the digest of that form.
// ---------------------------------------------------------------------------

let private roleName role =
    match role with
    | Initial -> "initial"
    | Ordinary -> "ordinary"
    | Terminal -> "terminal"

let private jKeys keys = JArray(keys |> List.distinct |> List.sort |> List.map JString)

let private sortedBy (render: 'a -> JsonValue) (values: 'a list) =
    values |> List.map render |> List.sortWith (fun a b -> String.CompareOrdinal(renderCanonical a, renderCanonical b))

let private jPort port =
    match port with
    | RecordedCurrentTime keys -> JObject [ "port", JString "currentTime"; "requirementKeys", jKeys keys ]
    | RecordedTransitionEffect(command, disposition, keys) ->
        JObject [ "port", JString "transitionEffect"
                  "command", JString command
                  "onFailure", (disposition |> Option.map (effectFailureDispositionName >> JString) |> Option.defaultValue JNull)
                  "requirementKeys", jKeys keys ]

let private jContent content =
    match content with
    | StateModel m ->
        JObject [ "kind", JString "stateModel"
                  "workflow", JString m.Workflow
                  "states", JArray(m.States |> sortedBy (fun s -> JObject [ "name", JString s.Name; "role", JString(roleName s.Role) ]))
                  "transitions",
                  JArray(m.Transitions |> sortedBy (fun t ->
                      JObject [ "command", JString t.Command; "from", JString t.From; "to", JString t.To; "requirementKeys", jKeys t.RequirementKeys ])) ]
    | PortContract ports -> JObject [ "kind", JString "portContract"; "ports", JArray(sortedBy jPort ports) ]

let toJson (records: ApprovedDecisionRecords) =
    JObject [ "schemaVersion", JString records.SchemaVersion
              "blueprintDigest", JString records.BlueprintDigest
              "cohortId", JString records.CohortId
              "records",
              JArray(records.Records |> sortedBy (fun r -> JObject [ "nodeId", JString r.NodeId; "content", jContent r.Content ])) ]

/// Canonical serialization: sorted object keys, sorted lists, distinct sorted
/// requirement keys. Equal meaning gives equal bytes. A file written with
/// exactly these bytes has a SHA-256 equal to `digest`.
let encodeDecisionRecords (records: ApprovedDecisionRecords) = toJson records |> renderCanonical

/// Content address of the canonical record set. This, not a file hash of an
/// arbitrary rendering, is what a host binds into a grant.
let digest (records: ApprovedDecisionRecords) =
    encodeDecisionRecords records |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
    |> fun hex -> "sha256:" + hex.ToLowerInvariant()
