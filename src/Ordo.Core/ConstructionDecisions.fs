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

/// Version 2 (CONSTRUCT-010) makes every choice explicit. A `stateModel`
/// must say what an unlisted pair means (`unlisted`) and may list explicit
/// `refusals` and `undecided` pairs; every `transitionEffect` must list its
/// `decisionPoints`. Version 2 records generate vocabulary 3 plans only.
[<Literal>]
let DecisionsSchemaVersion2 = "ordo.construction-decisions/2"

type RecordedState = { Name: string; Role: StateRole }

type RecordedTransition =
    { Command: string
      From: string
      To: string
      /// Requirements this transition implements. Each must be owned by the
      /// record's node in the blueprint.
      RequirementKeys: string list }

/// One choice inside an effect's handwritten behavior (version 2).
type RecordedDecisionPoint =
    { Point: string
      /// `None`: not decided; the question becomes an obligation.
      Question: string option
      RequirementKeys: string list }

/// A port an interface (or decision) node fixes.
type RecordedPort =
    | RecordedCurrentTime of requirementKeys: string list
    /// `onFailure = None` is the wire `null`: NOT DECIDED. The generator
    /// never picks a disposition for it; it emits an obligation.
    /// `decisionPoints` is `None` in version 1 records and required in
    /// version 2; an empty list asserts the cited requirements fix the
    /// effect's behavior completely.
    | RecordedTransitionEffect of
        command: string * onFailure: EffectFailureDisposition option * requirementKeys: string list * decisionPoints: RecordedDecisionPoint list option

/// What an unlisted (state, command) pair means (version 2).
[<RequireQualifiedAccess>]
type RecordedUnlisted =
    /// A documented rule, cited by these requirements, refuses it.
    | Refused of requirementKeys: string list
    /// The requirements do not decide it.
    | Undecided

type RecordedPair =
    { Command: string
      From: string
      RequirementKeys: string list }

type RecordedUndecidedPair =
    { Command: string
      From: string
      Question: string
      RequirementKeys: string list }

type StateModelContent =
    { Workflow: string
      States: RecordedState list
      Transitions: RecordedTransition list
      /// `None` in version 1 records; required in version 2.
      Unlisted: RecordedUnlisted option
      Refusals: RecordedPair list
      Undecided: RecordedUndecidedPair list }

/// An approved decision that settles blueprint conflicts. The link is the
/// record author's claim, bound by the record set's digest; generation checks
/// only that it names real conflicts it shares a requirement with.
type ConflictResolutionContent =
    { Conflicts: string list
      RequirementKeys: string list }

type DecisionContent =
    /// Wire kind `stateModel`: the states and transitions a decision fixes.
    | StateModel of StateModelContent
    /// Wire kind `portContract`: the orchestration ports an interface fixes.
    | PortContract of RecordedPort list
    /// Wire kind `conflictResolution`: the conflicts a decision settles.
    /// Accepted in every record schema version; additive.
    | ConflictResolution of ConflictResolutionContent

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

let private decisionPoint path value =
    result {
        let! decided =
            match value with
            | JObject properties ->
                match properties |> List.filter (fun (n, _) -> n = "decided") with
                | [ _, JBool b ] -> Ok b
                | _ -> fail path "expected exactly one boolean member decided"
            | _ -> fail path "expected object"
        let! p = fields path ([ "point"; "decided"; "requirementKeys" ] @ (if decided then [] else [ "question" ])) [] value
        let! point = textField path "point" p
        let! question = if decided then Ok None else textField path "question" p |> Result.map Some
        let! keys = keysField path p
        return { Point = point; Question = question; RequirementKeys = keys }
    }

let private port (second: bool) path value =
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
            let! p = fields path ([ "port"; "command"; "onFailure"; "requirementKeys" ] @ (if second then [ "decisionPoints" ] else [])) [] value
            let! command = textField path "command" p
            let! raw = field path "onFailure" p
            let! disposition = onFailure (path + ".onFailure") raw
            let! keys = keysField path p
            let! points =
                if second then
                    field path "decisionPoints" p |> Result.bind (items (path + ".decisionPoints") decisionPoint) |> Result.map Some
                else Ok None
            return RecordedTransitionEffect(command, disposition, keys, points)
        | other -> return! fail (path + ".port") ("unsupported port: " + other)
    }

let private pair path value =
    result {
        let! p = fields path [ "command"; "from"; "requirementKeys" ] [] value
        let! command = textField path "command" p
        let! from = textField path "from" p
        let! keys = keysField path p
        return { RecordedPair.Command = command; From = from; RequirementKeys = keys }
    }

let private undecidedPair path value =
    result {
        let! p = fields path [ "command"; "from"; "question"; "requirementKeys" ] [] value
        let! command = textField path "command" p
        let! from = textField path "from" p
        let! question = textField path "question" p
        let! keys = keysField path p
        return { RecordedUndecidedPair.Command = command; From = from; Question = question; RequirementKeys = keys }
    }

let private unlisted path value =
    result {
        let! disposition =
            match value with
            | JObject properties ->
                match properties |> List.filter (fun (n, _) -> n = "disposition") with
                | [ _, JString d ] -> Ok d
                | _ -> fail path "expected exactly one string member disposition"
            | _ -> fail path "expected object"
        match disposition with
        | "refused" ->
            let! p = fields path [ "disposition"; "requirementKeys" ] [] value
            let! keys = keysField path p
            return RecordedUnlisted.Refused keys
        | "undecided" ->
            let! _ = fields path [ "disposition" ] [] value
            return RecordedUnlisted.Undecided
        | other -> return! fail (path + ".disposition") ("unsupported unlisted disposition: " + other)
    }

let private content (second: bool) path value =
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
            let required = [ "kind"; "workflow"; "states"; "transitions" ] @ (if second then [ "unlisted" ] else [])
            let! p = fields path required (if second then [ "refusals"; "undecided" ] else []) value
            let! workflow = textField path "workflow" p
            let! rawStates = field path "states" p
            let! states = items (path + ".states") state rawStates
            let! rawTransitions = field path "transitions" p
            let! transitions = items (path + ".transitions") transition rawTransitions
            let! rule = if second then field path "unlisted" p |> Result.bind (unlisted (path + ".unlisted")) |> Result.map Some else Ok None
            let optionalList name reader =
                match Map.tryFind name p with
                | Some raw -> items (path + "." + name) reader raw
                | None -> Ok []
            let! refusals = optionalList "refusals" pair
            let! undecided = optionalList "undecided" undecidedPair
            return StateModel { Workflow = workflow; States = states; Transitions = transitions; Unlisted = rule; Refusals = refusals; Undecided = undecided }
        | "portContract" ->
            let! p = fields path [ "kind"; "ports" ] [] value
            let! rawPorts = field path "ports" p
            let! ports = items (path + ".ports") (port second) rawPorts
            return PortContract ports
        | "conflictResolution" ->
            let! p = fields path [ "kind"; "conflicts"; "requirementKeys" ] [] value
            let! rawConflicts = field path "conflicts" p
            let! conflicts = strings (path + ".conflicts") rawConflicts
            let! () = if List.isEmpty conflicts then fail (path + ".conflicts") "expected at least one conflict id" else Ok()
            let! keys = keysField path p
            return ConflictResolution { Conflicts = conflicts; RequirementKeys = keys }
        | other -> return! fail (path + ".kind") ("unsupported content kind: " + other)
    }

let private record (second: bool) path value =
    result {
        let! p = fields path [ "nodeId"; "content" ] [] value
        let! nodeId = textField path "nodeId" p
        let! raw = field path "content" p
        let! decided = content second (path + ".content") raw
        return { NodeId = nodeId; Content = decided }
    }

/// Strict reader. The result is untrusted until `generateDefaultPlan` checks
/// it against a prepared cohort.
let readDecisionRecords (input: string) : Result<ApprovedDecisionRecords, string> =
    result {
        let! json = parse input |> Result.mapError (sprintf "invalid JSON: %A")
        let! p = fields "$" [ "schemaVersion"; "blueprintDigest"; "cohortId"; "records" ] [] json
        let! schema = textField "$" "schemaVersion" p
        let! () =
            if schema = DecisionsSchemaVersion || schema = DecisionsSchemaVersion2 then Ok()
            else fail "$.schemaVersion" ("unsupported " + schema)
        let second = schema = DecisionsSchemaVersion2
        let! blueprint = textField "$" "blueprintDigest" p
        let! cohort = textField "$" "cohortId" p
        let! rawRecords = field "$" "records" p
        let! records = items "$.records" (record second) rawRecords
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
    | RecordedTransitionEffect(command, disposition, keys, points) ->
        JObject(
            [ "port", JString "transitionEffect"
              "command", JString command
              "onFailure", (disposition |> Option.map (effectFailureDispositionName >> JString) |> Option.defaultValue JNull)
              "requirementKeys", jKeys keys ]
            @ (points
               |> Option.map (fun ps ->
                   "decisionPoints",
                   JArray(ps |> sortedBy (fun p ->
                       JObject(
                           [ "point", JString p.Point
                             "decided", JBool p.Question.IsNone
                             "requirementKeys", jKeys p.RequirementKeys ]
                           @ (p.Question |> Option.map (fun q -> "question", JString q) |> Option.toList)))))
               |> Option.toList))

let private jContent content =
    match content with
    | StateModel m ->
        JObject(
            [ "kind", JString "stateModel"
              "workflow", JString m.Workflow
              "states", JArray(m.States |> sortedBy (fun s -> JObject [ "name", JString s.Name; "role", JString(roleName s.Role) ]))
              "transitions",
              JArray(m.Transitions |> sortedBy (fun t ->
                  JObject [ "command", JString t.Command; "from", JString t.From; "to", JString t.To; "requirementKeys", jKeys t.RequirementKeys ])) ]
            @ (match m.Unlisted with
               | None -> []
               | Some(RecordedUnlisted.Refused keys) ->
                   [ "unlisted", JObject [ "disposition", JString "refused"; "requirementKeys", jKeys keys ]
                     "refusals", JArray(m.Refusals |> sortedBy (fun r -> JObject [ "command", JString r.Command; "from", JString r.From; "requirementKeys", jKeys r.RequirementKeys ]))
                     "undecided", JArray(m.Undecided |> sortedBy (fun u -> JObject [ "command", JString u.Command; "from", JString u.From; "question", JString u.Question; "requirementKeys", jKeys u.RequirementKeys ])) ]
               | Some RecordedUnlisted.Undecided ->
                   [ "unlisted", JObject [ "disposition", JString "undecided" ]
                     "refusals", JArray(m.Refusals |> sortedBy (fun r -> JObject [ "command", JString r.Command; "from", JString r.From; "requirementKeys", jKeys r.RequirementKeys ]))
                     "undecided", JArray(m.Undecided |> sortedBy (fun u -> JObject [ "command", JString u.Command; "from", JString u.From; "question", JString u.Question; "requirementKeys", jKeys u.RequirementKeys ])) ]))
    | PortContract ports -> JObject [ "kind", JString "portContract"; "ports", JArray(sortedBy jPort ports) ]
    | ConflictResolution c ->
        JObject [ "kind", JString "conflictResolution"
                  "conflicts", JArray(c.Conflicts |> List.distinct |> List.sort |> List.map JString)
                  "requirementKeys", jKeys c.RequirementKeys ]

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
