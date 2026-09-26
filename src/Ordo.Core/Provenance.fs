/// Who asked, kept apart from whether they may and why it should happen.
///
/// Three questions stay separate in executable Ordo (ORDO-PROV-01 /
/// DF-SDE-2026-0016):
///
/// * **Identity** — "who requested this?" — is answered by a `Requester`:
///   a Praxis actor plus the execution it acted in. It is self-reported
///   provenance, not authentication.
/// * **Capability** — "may this actor request this?" — is answered only by
///   the host-supplied `CapabilitySet` (DF-SDE-2026-0011).
/// * **Evidence** — "why should the transition occur?" — is answered only by
///   `Evidence` records and their requirements.
///
/// Nothing in this module can grant a capability, satisfy an evidence
/// requirement, or change confidence, and `Transition.evaluate` and decision
/// evaluation never read a requester (ORDO-PROV-03). The actor, execution,
/// and interchange block are Praxis's model, not a second one: the canonical
/// definitions are Praxis `docs/agent-provenance.md`,
/// `schemas/provenance-interchange.schema.json`, RQ-ROS-2026-A015/A016/A019
/// and DF-ROS-2026-A037. This module is a small F# codec that mirrors the
/// reference semantics of Praxis `lib/provenance-interchange.mjs`, and it is
/// tested against every vendored conformance case at Praxis contract
/// revision 1.2 (ORDO-PROV-06).
///
/// Every function is pure. Blocks are carried as `JsonValue` so that fields
/// this version does not model survive unchanged.
module Ordo.Core.Provenance

open System
open System.Globalization
open System.Text.RegularExpressions
open Ordo.Core.Json

/// The only interchange major version this reader interprets.
[<Literal>]
let SchemaTag = "praxis.provenance/1"

[<Literal>]
let private UnknownValue = "unknown"

let private regex (pattern: string) =
    Regex(pattern, RegexOptions.CultureInvariant)

// `\z` rather than `$`: .NET's `$` also matches before a trailing newline,
// which JavaScript's does not, and the verdicts must agree exactly.
let private schemaPattern = regex @"\Apraxis\.provenance/([1-9][0-9]*)\z"
let private executionKey = regex @"\AEXE-[A-Za-z0-9._-]+\z"
let private contributionKey = regex @"\ACTB-[A-Za-z0-9._-]+\z"
let private foreignExecutionKey = regex @"\AEXT-([a-z][a-z0-9-]*)\.([A-Za-z0-9._-]+)\z"
let private kindPattern = regex @"\A(agent|human|automation|unknown|x-[a-z0-9][a-z0-9-]*)\z"
let private operationGrammar = regex @"\A[a-z][a-z0-9-]*\z"
let private extensionPattern = regex @"\Ax-[a-z0-9][a-z0-9-]*\z"
let private timestampPattern = regex @"\A([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2})(\.[0-9]{1,9})?Z\z"

/// Operation codes `praxis.provenance/1` defines. Only `created` is
/// authorship; the others are roles. Codes outside this list that match the
/// grammar are tolerated, preserved, and reported.
let knownOperations =
    [ "created"
      "modified"
      "reviewed"
      "approved"
      "superseded"
      "migrated"
      "discovered"
      "measured"
      "transformed"
      "remediated"
      "validated"
      "resolved" ]

/// Recognisable credential shapes (RQ-ROS-2026-A017). A tripwire for
/// accidents, not a secret scanner: provenance never carries authentication
/// material, so a block that appears to is refused at the boundary.
let private credentialPatterns =
    [ @"gh[pousr]_[A-Za-z0-9]{20,}"
      @"github_pat_[A-Za-z0-9_]{20,}"
      @"sk-[A-Za-z0-9_-]{20,}"
      @"AKIA[0-9A-Z]{16}"
      @"xox[abprs]-[A-Za-z0-9-]{10,}"
      @"-----BEGIN [A-Z ]*PRIVATE KEY-----"
      // Contract 1.2: explicit ASCII classes only, no `\b`, `\s` or case
      // folding, whose meaning differs between .NET, JavaScript and Python.
      @"(?:^|[^A-Za-z0-9_])[Bb][Ee][Aa][Rr][Ee][Rr][\t\n\v\f\r ]+[A-Za-z0-9._~+/=-]{16,}"
      @"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\." ]
    |> List.map regex

let isCredentialLike (value: string) =
    credentialPatterns |> List.exists (fun pattern -> pattern.IsMatch value)

// ------------------------------------------------------------ json helpers

/// Contract 1.2: "blank" is defined over ASCII whitespace only (tab, LF, VT,
/// FF, CR, space). .NET `Trim()` also strips U+0085, U+00A0, U+FEFF and
/// others, which JavaScript and Python disagree about, so every other
/// character is content.
let private asciiWhitespace = [| '\t'; '\n'; '\v'; '\f'; '\r'; ' ' |]

let asciiTrim (text: string) = text.Trim asciiWhitespace

let isBlank (text: string) = (asciiTrim text).Length = 0

/// The last member with this name, as JavaScript's `JSON.parse` would keep.
let private memberOf (name: string) (value: JsonValue) =
    match value with
    | JObject members -> members |> List.rev |> List.tryFind (fst >> (=) name) |> Option.map snd
    | _ -> None

let private isNonEmptyString value =
    match value with
    | Some(JString text) -> not (isBlank text)
    | _ -> false

let private stringOf value =
    match value with
    | Some(JString text) -> Some text
    | _ -> None

let private setMember (name: string) (item: JsonValue) (value: JsonValue) =
    match value with
    | JObject members when members |> List.exists (fst >> (=) name) ->
        JObject(members |> List.map (fun (key, existing) -> if key = name then key, item else key, existing))
    | JObject members -> JObject(members @ [ name, item ])
    | other -> other

/// The instant a timestamp names, in milliseconds (the precision the
/// reference implementation compares at), or `None` when it is not one.
let private instant (text: string) : int64 option =
    let matched = timestampPattern.Match text

    if not matched.Success then
        None
    else
        let fraction =
            if matched.Groups[2].Success then
                matched.Groups[2].Value.Substring(1).PadRight(3, '0').Substring(0, 3)
            else
                "000"

        match
            DateTimeOffset.TryParseExact(
                matched.Groups[1].Value + "." + fraction + "Z",
                "yyyy-MM-ddTHH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal
            )
        with
        | true, parsed -> Some(parsed.ToUnixTimeMilliseconds())
        | _ -> None

/// Ordering where an unreadable time sorts last, as the reference does.
let private order (text: string option) =
    text |> Option.bind instant |> Option.defaultValue Int64.MaxValue

/// Dotted paths of every key or string value that is not well-formed
/// Unicode (an unpaired UTF-16 surrogate; contract 1.2).
let surrogateFindings (node: JsonValue) : string list =
    let rec walk (path: string) (current: JsonValue) =
        match current with
        | JString text -> if hasUnpairedSurrogate text then [ path ] else []
        | JArray items -> items |> List.mapi (fun index item -> walk (sprintf "%s[%d]" path index) item) |> List.concat
        | JObject members ->
            members
            |> List.collect (fun (key, item) ->
                let child = if path.Length = 0 then key else path + "." + key
                (if hasUnpairedSurrogate key then [ child ] else []) @ walk child item)
        | JNull
        | JBool _
        | JInt _
        | JFloat _ -> []

    walk "" node

/// Dotted paths of every key or string value that looks like a credential.
let credentialFindings (node: JsonValue) : string list =
    let rec walk (path: string) (current: JsonValue) =
        match current with
        | JString text -> if isCredentialLike text then [ path ] else []
        | JArray items -> items |> List.mapi (fun index item -> walk (sprintf "%s[%d]" path index) item) |> List.concat
        | JObject members ->
            members
            |> List.collect (fun (key, item) ->
                let child = if path.Length = 0 then key else path + "." + key
                (if isCredentialLike key then [ child ] else []) @ walk child item)
        | JNull
        | JBool _
        | JInt _
        | JFloat _ -> []

    walk "" node

// ------------------------------------------------------------------ keys

/// What a contribution key names.
type ContributionKeyKind =
    /// `EXE-...`: a Praxis execution.
    | PraxisExecution
    /// `EXT-<system>.<run-id>`: an execution in another Echelon system.
    | ForeignExecution of system: string
    /// `CTB-...`: a non-agent contributor outside any execution.
    | Contributor
    | InvalidKey

[<RequireQualifiedAccess>]
module ContributionKey =

    let kind (key: string) =
        if executionKey.IsMatch key then
            PraxisExecution
        else
            let foreign = foreignExecutionKey.Match key

            if foreign.Success then ForeignExecution foreign.Groups[1].Value
            elif contributionKey.IsMatch key then Contributor
            else InvalidKey

    /// Whether an agent may be keyed by this (agents need an execution).
    let isExecution (key: string) =
        match kind key with
        | PraxisExecution
        | ForeignExecution _ -> true
        | Contributor
        | InvalidKey -> false

    /// `EXT-<system>.<run-id>`, refusing ids a key cannot carry.
    let foreign (system: string) (runId: string) : Result<string, string> =
        let key = sprintf "EXT-%s.%s" system runId

        match kind key with
        | ForeignExecution found when found = system -> Ok key
        | _ -> Error(sprintf "cannot form a foreign execution key from system '%s' and run '%s'" system runId)

    /// One key segment, escaped injectively (contract 1.2): for each Unicode
    /// code point (not each UTF-16 unit), ASCII letters, digits and `-` pass
    /// through; every other code point, `.` and `_` included, becomes `_xx`
    /// per UTF-8 byte in lower-case hex. A segment therefore never contains
    /// the `.` separator, and characters outside the BMP never collide. An
    /// empty segment, or one with an unpaired surrogate (which has no UTF-8
    /// form), cannot form a key.
    let escapeSegment (text: string) : Result<string, string> =
        if String.IsNullOrEmpty text then
            Error "a key segment must be a non-empty string"
        elif hasUnpairedSurrogate text then
            Error "a key segment must be well-formed Unicode (unpaired surrogate)"
        else
            let passes (rune: Text.Rune) =
                rune.IsAscii
                && (Char.IsAsciiLetterOrDigit(char rune.Value) || rune.Value = int '-')

            text.EnumerateRunes()
            |> Seq.map (fun rune ->
                if passes rune then
                    rune.ToString()
                else
                    let bytes = Array.zeroCreate<byte> rune.Utf8SequenceLength
                    rune.EncodeToUtf8(Span<byte> bytes) |> ignore

                    bytes
                    |> Array.map (fun b -> "_" + b.ToString("x2", CultureInfo.InvariantCulture))
                    |> String.concat "")
            |> String.concat ""
            |> Ok

    /// `EXT-op.<operationId>`: the registry mapping for work whose execution
    /// is unknown and only an operation id is known, with the id escaped by
    /// `escapeSegment`. An id that cannot form a key is refused, never
    /// replaced by an invented key.
    let ofOperation (operationId: string) : Result<string, string> =
        escapeSegment operationId
        |> Result.map (fun safe -> "EXT-op." + safe)
        |> Result.mapError (sprintf "operation id cannot form a contribution key: %s")

    /// The Praxis reference `keyFromEnvelopeV1`: `EXT-run.<seg(runId)>` when
    /// a v1 envelope's run id is known, otherwise `EXT-op.<seg(operationId)>`.
    /// An error means the envelope must be rejected.
    let ofEnvelopeV1 (envelope: JsonValue) : Result<string, string> =
        let path names =
            names |> List.fold (fun node name -> node |> Option.bind (memberOf name)) (Some envelope)

        match path [ "actor"; "runId"; "state" ], path [ "actor"; "runId"; "value" ] with
        | Some(JString "known"), Some(JString run) when not (isBlank run) ->
            escapeSegment run
            |> Result.map (fun safe -> "EXT-run." + safe)
            |> Result.mapError (sprintf "run id cannot form a contribution key: %s")
        | _ ->
            match memberOf "operationId" envelope with
            | Some(JString operationId) -> ofOperation operationId
            | _ -> Error "operation id cannot form a contribution key: a key segment must be a non-empty string"

// ----------------------------------------------------------------- actors

/// The Praxis actor kinds. `x-...` extensions are preserved as written.
type ActorKind =
    | AgentActor
    | HumanActor
    | AutomationActor
    | UnknownActor
    | ExtensionActor of string

/// The Praxis actor (`provenance-actor.schema.json`): who says they did
/// something. Non-human actors carry provider, model and runtime, as the
/// literal "unknown" when not known; a human omits them because they do not
/// apply. Unknown and not-applicable are never conflated.
type Actor =
    { Kind: ActorKind
      Id: string
      Provider: string option
      Model: string option
      Runtime: string option }

[<RequireQualifiedAccess>]
module ActorKind =

    let toWire kind =
        match kind with
        | AgentActor -> "agent"
        | HumanActor -> "human"
        | AutomationActor -> "automation"
        | UnknownActor -> "unknown"
        | ExtensionActor code -> code

    let tryParse (text: string) =
        match text with
        | "agent" -> Some AgentActor
        | "human" -> Some HumanActor
        | "automation" -> Some AutomationActor
        | "unknown" -> Some UnknownActor
        | code when kindPattern.IsMatch code -> Some(ExtensionActor code)
        | _ -> None

[<RequireQualifiedAccess>]
module Actor =

    let private orUnknown (value: string option) =
        value
        |> Option.filter (isBlank >> not)
        |> Option.defaultValue UnknownValue
        |> Some

    /// An agent. Unknown attributes are recorded as "unknown", never guessed.
    let agent (id: string) (provider: string option) (model: string option) (runtime: string option) =
        { Kind = AgentActor
          Id = id
          Provider = orUnknown provider
          Model = orUnknown model
          Runtime = orUnknown runtime }

    /// A deterministic non-agent process such as CI.
    let automation (id: string) (provider: string option) (model: string option) (runtime: string option) =
        { agent id provider model runtime with Kind = AutomationActor }

    let human (id: string) =
        { Kind = HumanActor
          Id = id
          Provider = None
          Model = None
          Runtime = None }

    /// A declared actor whose kind and identity are not known.
    let unknown =
        { Kind = UnknownActor
          Id = UnknownValue
          Provider = Some UnknownValue
          Model = Some UnknownValue
          Runtime = Some UnknownValue }

    let encode (actor: Actor) : JsonValue =
        JObject(
            [ "kind", JString(ActorKind.toWire actor.Kind)
              "id", JString actor.Id ]
            @ ([ "provider", actor.Provider; "model", actor.Model; "runtime", actor.Runtime ]
               |> List.choose (fun (name, value) -> value |> Option.map (fun text -> name, JString text)))
        )

    /// Problems with an actor node, in the reference's wording.
    let problems (prefix: string) (node: JsonValue) : string list =
        match node with
        | JObject _ ->
            let kind = memberOf "kind" node

            [ match stringOf kind with
              | Some text when kindPattern.IsMatch text -> ()
              | _ -> sprintf "%s.kind is not agent, human, automation, unknown, or x-..." prefix
              if not (isNonEmptyString (memberOf "id" node)) then
                  sprintf "%s.id must not be empty; use 'unknown' when it is not known" prefix
              for field in [ "provider"; "model"; "runtime" ] do
                  let value = memberOf field node

                  if value.IsSome && not (isNonEmptyString value) then
                      sprintf "%s.%s must be a non-empty string" prefix field

                  if stringOf kind = Some "agent" && value.IsNone then
                      sprintf "%s.%s is required for an agent ('unknown' when not known)" prefix field ]
        | _ -> [ sprintf "%s must be an object" prefix ]

    let decode (node: JsonValue) : Result<Actor, string list> =
        match problems "actor" node with
        | [] ->
            match memberOf "kind" node |> stringOf |> Option.bind ActorKind.tryParse, stringOf (memberOf "id" node) with
            | Some kind, Some id ->
                Ok
                    { Kind = kind
                      Id = id
                      Provider = stringOf (memberOf "provider" node)
                      Model = stringOf (memberOf "model" node)
                      Runtime = stringOf (memberOf "runtime" node) }
            | _ -> Error [ "actor is not readable" ]
        | problems -> Error problems

    let private isKnown (value: string option) =
        match value with
        | Some text -> not (isBlank text) && asciiTrim text <> UnknownValue
        | None -> false

    /// Same actor: kind, id and every applicable known attribute agree;
    /// "unknown" never contradicts.
    let agree (left: Actor) (right: Actor) =
        let compatible a b = not (isKnown a && isKnown b) || a = b

        left.Kind = right.Kind
        && (left.Id = right.Id || not (isKnown(Some left.Id)) || not (isKnown(Some right.Id)))
        && compatible left.Provider right.Provider
        && compatible left.Model right.Model
        && compatible left.Runtime right.Runtime

// ------------------------------------------------------ the interchange block

/// One contribution, as read from a supported block.
type Contribution =
    { Key: string
      Operations: string list
      At: string
      Last: string option
      Actor: Actor
      Reason: string option
      Evidence: string list }

/// A `praxis.provenance/1` block this reader understands. The original JSON
/// is the value, so members this version does not model are preserved.
type ProvenanceBlock =
    private
    | ProvenanceBlock of JsonValue

    member this.Json =
        let (ProvenanceBlock node) = this
        node

/// What a receiver may do with a block it was given (RQ-ROS-2026-A015).
type ProvenanceVerdict =
    /// Understood. `warnings` name tolerated forward-compatible content.
    | Supported of block: ProvenanceBlock * warnings: string list
    /// Another major version: carry verbatim, never interpret or merge.
    | Unsupported of schema: string * verbatim: JsonValue
    /// Reject at the boundary; never drop or repair silently.
    | Malformed of problems: string list

/// Provenance a record carries: understood, or carried verbatim because it
/// is a major version this build does not interpret.
type CarriedProvenance =
    | Understood of ProvenanceBlock
    | CarriedVerbatim of schema: string * verbatim: JsonValue

[<RequireQualifiedAccess>]
module Contribution =

    let private stringListProblems (field: string) (value: JsonValue option) =
        match value with
        | None -> []
        | Some(JArray items) when items |> List.forall (Some >> isNonEmptyString) -> []
        | Some _ -> [ sprintf "%s must be an array of non-empty strings" field ]

    /// Problems with one contribution entry, in the reference's wording.
    let problems (key: string) (entry: JsonValue) : string list =
        let prefix = "contributions." + key

        match entry with
        | JObject _ ->
            let operations =
                match memberOf "operations" entry with
                | Some(JArray items) -> Some items
                | _ -> None

            let keyKind = ContributionKey.kind key
            let actor = memberOf "actor" entry
            let at = stringOf (memberOf "at" entry)

            [ if keyKind = InvalidKey then
                  sprintf "%s: key must be EXE-..., EXT-<system>.<run-id>, or CTB-..." prefix
              match operations with
              | None -> sprintf "%s.operations must be an array" prefix
              | Some [] -> sprintf "%s.operations must record at least one operation" prefix
              | Some items ->
                  for item in items do
                      match item with
                      | JString code when operationGrammar.IsMatch code -> ()
                      | _ -> sprintf "%s.operations: '%s' is not a valid operation code" prefix (render item)

                  if (List.distinct items).Length <> items.Length then
                      sprintf "%s.operations must not repeat an operation" prefix
              if (at |> Option.bind instant).IsNone then
                  sprintf "%s.at must be an ISO-8601 UTC timestamp" prefix
              match memberOf "last" entry with
              | None -> ()
              | Some(JString last) when (instant last).IsSome ->
                  if order (Some last) < order at then
                      sprintf "%s.last must not precede at" prefix
              | Some _ -> sprintf "%s.last must be an ISO-8601 UTC timestamp" prefix
              if actor.IsNone then
                  sprintf "%s.actor is required" prefix
              match actor with
              | Some(JObject _ as node) when stringOf (memberOf "kind" node) = Some "agent" && not (ContributionKey.isExecution key) ->
                  sprintf "%s: an agent contribution must be keyed by the execution (EXE-... or EXT-...) that produced it" prefix
              | _ -> ()
              match memberOf "reason" entry with
              | None
              | Some(JString _) -> ()
              | Some _ -> sprintf "%s.reason must be a string" prefix ]
            @ (actor |> Option.map (Actor.problems (prefix + ".actor")) |> Option.defaultValue [])
            @ stringListProblems (prefix + ".evidence") (memberOf "evidence" entry)
        | _ -> [ sprintf "%s must be an object" prefix ]

    let private operationsOf (entry: JsonValue) =
        match memberOf "operations" entry with
        | Some(JArray items) -> items |> List.choose (Some >> stringOf)
        | _ -> []

    let private evidenceOf (entry: JsonValue) =
        match memberOf "evidence" entry with
        | Some(JArray items) -> items |> List.choose (Some >> stringOf)
        | _ -> []

    /// Reads an entry already known to be well formed.
    let internal read (key: string) (entry: JsonValue) : Contribution option =
        match memberOf "actor" entry |> Option.map Actor.decode, stringOf (memberOf "at" entry) with
        | Some(Ok actor), Some at ->
            Some
                { Key = key
                  Operations = operationsOf entry
                  At = at
                  Last = stringOf (memberOf "last" entry)
                  Actor = actor
                  Reason = stringOf (memberOf "reason" entry)
                  Evidence = evidenceOf entry }
        | _ -> None

    /// The wire form of a contribution (without its key).
    let encode (contribution: Contribution) : JsonValue =
        JObject(
            [ "operations", JArray(contribution.Operations |> List.map JString)
              "at", JString contribution.At ]
            @ (contribution.Last |> Option.map (fun last -> "last", JString last) |> Option.toList)
            @ [ "actor", Actor.encode contribution.Actor ]
            @ (contribution.Reason |> Option.map (fun reason -> "reason", JString reason) |> Option.toList)
            @ (if contribution.Evidence.IsEmpty then []
               else [ "evidence", JArray(contribution.Evidence |> List.map JString) ])
        )

    let internal operations entry = operationsOf entry
    let internal evidence entry = evidenceOf entry

[<RequireQualifiedAccess>]
module ProvenanceBlock =

    let private entries (node: JsonValue) =
        match memberOf "contributions" node with
        | Some(JObject members) -> members
        | _ -> []

    let private creators (members: (string * JsonValue) list) =
        members |> List.filter (fun (_, entry) -> Contribution.operations entry |> List.contains "created")

    let private historyProblems (members: (string * JsonValue) list) =
        match creators members with
        | [] -> []
        | [ creationKey, creation ] ->
            let createdAt = order (stringOf (memberOf "at" creation))

            members
            |> List.filter (fun (key, entry) -> key <> creationKey && order (stringOf (memberOf "at" entry)) < createdAt)
            |> List.map (fun (key, _) -> sprintf "contributions.%s precedes the recorded creation (%s)" key creationKey)
        | many -> [ sprintf "more than one contribution claims 'created': %s" (String.Join(", ", many |> List.map fst)) ]

    /// Classifies a received block. A block without a `schema` tag but with
    /// a `contributions` map (the registry projection) is read as major 1.
    let classify (node: JsonValue) : ProvenanceVerdict =
        match node with
        | JObject _ ->
            match surrogateFindings node, credentialFindings node with
            | _ :: _ as unpaired, _ ->
                Malformed(
                    unpaired
                    |> List.map (fun path -> path + ": unpaired UTF-16 surrogate; provenance must be well-formed Unicode")
                )
            | [], (_ :: _ as secrets) ->
                Malformed(
                    secrets
                    |> List.map (fun path -> path + ": credential-like value; provenance must never carry authentication material")
                )
            | [], [] ->
                match memberOf "schema" node with
                | Some(JString tag) when tag <> SchemaTag ->
                    if schemaPattern.IsMatch tag then
                        Unsupported(tag, node)
                    else
                        Malformed [ sprintf "schema '%s' is not a valid praxis.provenance/<major> tag" tag ]
                | Some(JString _)
                | None ->
                    match memberOf "contributions" node with
                    | Some(JObject members) ->
                        let problems =
                            (members |> List.collect (fun (key, entry) -> Contribution.problems key entry))
                            @ (match memberOf "derivedFrom" node with
                               | None -> []
                               | Some(JArray items) when items |> List.forall (Some >> isNonEmptyString) -> []
                               | Some _ -> [ "derivedFrom must be an array of non-empty strings" ])

                        match problems with
                        | _ :: _ -> Malformed problems
                        | [] ->
                            match historyProblems members with
                            | _ :: _ as invariant -> Malformed invariant
                            | [] ->
                                let warnings =
                                    members
                                    |> List.collect (fun (key, entry) ->
                                        Contribution.operations entry
                                        |> List.filter (fun code ->
                                            not (List.contains code knownOperations) && not (extensionPattern.IsMatch code))
                                        |> List.map (fun code ->
                                            sprintf
                                                "contributions.%s.operations: '%s' is not an operation this version knows; preserved verbatim"
                                                key
                                                code))

                                Supported(ProvenanceBlock node, warnings)
                    | None -> Malformed [ "contributions is required" ]
                    | Some _ -> Malformed [ "contributions must be an object keyed by EXE-, EXT-, or CTB- keys" ]
                | Some _ -> Malformed [ "schema must be a string" ]
        | _ -> Malformed [ "provenance must be a JSON object" ]

    /// Classifies a block received as JSON text (contract 1.2). Text that is
    /// not JSON, repeats a member name within any one object, or holds an
    /// unpaired UTF-16 surrogate is malformed whatever its major version;
    /// otherwise the parsed value is classified. Never throws.
    let classifyText (text: string) : ProvenanceVerdict =
        match parse text with
        | Ok node -> classify node
        | Error(MalformedJson message) -> Malformed [ "provenance is not well-formed JSON: " + message ]
        | Error error -> Malformed [ sprintf "provenance is not well-formed JSON: %A" error ]

    /// Classifies a block a record carries: understood, carried verbatim, or
    /// the problems that make it unacceptable.
    let receive (node: JsonValue) : Result<CarriedProvenance, string list> =
        match classify node with
        | Supported(block, _) -> Ok(Understood block)
        | Unsupported(schema, verbatim) -> Ok(CarriedVerbatim(schema, verbatim))
        | Malformed problems -> Error problems

    /// A new, empty `praxis.provenance/1` block.
    let empty = ProvenanceBlock(JObject [ "schema", JString SchemaTag; "contributions", JObject [] ])

    let toJson (block: ProvenanceBlock) = block.Json

    let contributions (block: ProvenanceBlock) : Contribution list =
        entries block.Json |> List.choose (fun (key, entry) -> Contribution.read key entry)

    let derivedFrom (block: ProvenanceBlock) : string list =
        match memberOf "derivedFrom" block.Json with
        | Some(JArray items) -> items |> List.choose (Some >> stringOf)
        | _ -> []

    /// The contribution that claims `created`, or `None` when origin is not
    /// recorded. Absence is unknown, never inferred.
    let originator (block: ProvenanceBlock) : Contribution option =
        match creators (entries block.Json) with
        | [ key, entry ] -> Contribution.read key entry
        | _ -> None

    /// Contributions that played a role (for example `validated`), in time order.
    let withRole (operation: string) (block: ProvenanceBlock) : Contribution list =
        contributions block
        |> List.filter (fun item -> List.contains operation item.Operations)
        |> List.sortBy (fun item -> order (Some item.At))

    /// Appends one contribution without disturbing any other: the same key
    /// merges operations and evidence and advances `last` only when the
    /// actor agrees; a second or late `created` is refused; unknown fields
    /// are preserved. Returns the new block and whether anything changed.
    ///
    /// Contract revision 1.1: an append never returns a block `classify`
    /// would reject — it refuses credential-like values, a contribution dated
    /// before the creation, and a second originator, and it re-classifies
    /// its own result before returning it.
    let appendJson (key: string) (contribution: JsonValue) (block: ProvenanceBlock) : Result<ProvenanceBlock * bool, string> =
        let finish (next: JsonValue) (changed: bool) =
            match classify next with
            | Supported(result, _) -> Ok(result, changed)
            | Malformed problems -> Error("the resulting history would be malformed: " + String.Join("; ", problems))
            | Unsupported(schema, _) -> Error(sprintf "the resulting history would be unsupported (%s)" schema)

        match classify block.Json with
        | Unsupported(schema, _) -> Error(sprintf "refusing to append to a unsupported provenance block (%s)" schema)
        | Malformed _ -> Error "refusing to append to a malformed provenance block"
        | Supported _ ->
            match credentialFindings (JObject [ key, contribution ]) with
            | _ :: _ as secrets ->
                Error(String.Join(", ", secrets) + ": credential-like value; provenance must never carry authentication material")
            | [] ->
            match Contribution.problems key contribution with
            | _ :: _ as problems -> Error(String.Join("; ", problems))
            | [] ->
                let members = entries block.Json
                let incomingOperations = Contribution.operations contribution
                let incomingAt = stringOf (memberOf "at" contribution)
                let hasOriginator = not (creators members).IsEmpty

                let withEntries updated =
                    setMember "contributions" (JObject updated) block.Json

                let isKnown (value: string) =
                    not (isBlank value) && asciiTrim value <> UnknownValue

                match members |> List.rev |> List.tryFind (fst >> (=) key) with
                | None ->
                    if List.contains "created" incomingOperations && hasOriginator then
                        Error "the record already has an originator; record 'modified' instead of 'created'"
                    elif
                        List.contains "created" incomingOperations
                        && members |> List.exists (fun (_, entry) -> order (stringOf (memberOf "at" entry)) < order incomingAt)
                    then
                        Error "a 'created' contribution cannot follow existing contributions"
                    else
                        finish (withEntries (members @ [ key, contribution ])) true
                | Some(_, existing) ->
                    match memberOf "actor" existing |> Option.map Actor.decode, memberOf "actor" contribution |> Option.map Actor.decode with
                    | Some(Ok before), Some(Ok after) when Actor.agree before after ->
                        let existingOperations = Contribution.operations existing
                        let existingAt = stringOf (memberOf "at" existing)

                        let earlier =
                            members
                            |> List.exists (fun (other, entry) -> other <> key && order (stringOf (memberOf "at" entry)) < order existingAt)

                        if (isKnown before.Id && not (isKnown after.Id)) || (before.Kind <> UnknownActor && after.Kind = UnknownActor) then
                            Error(
                                sprintf
                                    "contribution '%s' belongs to %s:%s; an actor with unknown identity cannot extend it"
                                    key
                                    (ActorKind.toWire before.Kind)
                                    before.Id
                            )
                        elif
                            List.contains "created" incomingOperations
                            && not (List.contains "created" existingOperations)
                            && (hasOriginator || earlier)
                        then
                            Error "the record's originator is already recorded or precedes this contribution; record 'modified' instead of 'created'"
                        else
                            let operations =
                                existingOperations @ (incomingOperations |> List.filter (fun op -> not (List.contains op existingOperations)))

                            let existingEvidence = Contribution.evidence existing

                            let evidence =
                                existingEvidence
                                @ (Contribution.evidence contribution |> List.filter (fun item -> not (List.contains item existingEvidence)))

                            let latestOf (entry: JsonValue) =
                                match memberOf "last" entry with
                                | Some _ as last -> stringOf last
                                | None -> stringOf (memberOf "at" entry)

                            // The later of the existing and incoming times.
                            let latest =
                                let current = latestOf existing
                                let incoming = latestOf contribution
                                if order incoming > order current then incoming else current

                            // The incoming contribution's unknown fields are kept; the
                            // existing entry wins on conflict (contract 1.1).
                            let overlay =
                                match existing with
                                | JObject existingMembers ->
                                    existingMembers |> List.fold (fun value (name, item) -> setMember name item value) contribution
                                | other -> other

                            let merged =
                                overlay
                                |> setMember "operations" (JArray(operations |> List.map JString))
                                |> fun value ->
                                    if evidence.IsEmpty then value
                                    else setMember "evidence" (JArray(evidence |> List.map JString)) value
                                |> fun value ->
                                    match latest with
                                    | Some time when order latest > order existingAt -> setMember "last" (JString time) value
                                    | _ -> value

                            let changed = render merged <> render existing
                            let updated = members |> List.map (fun (name, entry) -> if name = key then name, merged else name, entry)
                            finish (withEntries updated) changed
                    | Some(Ok before), _ ->
                        Error(
                            sprintf
                                "contribution '%s' is already attributed to %s:%s; refusing to re-attribute it"
                                key
                                (ActorKind.toWire before.Kind)
                                before.Id
                        )
                    | _ -> Error(sprintf "contribution '%s' has no readable actor" key)

    let append (contribution: Contribution) (block: ProvenanceBlock) =
        appendJson contribution.Key (Contribution.encode contribution) block

    /// Adds lineage references (never authorship), preserving existing
    /// order, as the Praxis reference `addLineage` (contract 1.2). The block
    /// must classify as supported; `references` must be an array of
    /// non-blank, credential-free, well-formed strings; duplicates are
    /// dropped keeping the first occurrence; and the result must itself
    /// classify as supported. Returns the new block and whether anything
    /// changed, or why the lineage was refused. Nothing is stored or
    /// silently dropped on refusal.
    let addLineageJson (references: JsonValue) (node: JsonValue) : Result<ProvenanceBlock * bool, string> =
        let refuse = Error

        match classify node with
        | Unsupported(schema, _) -> refuse (sprintf "refusing to add lineage to a unsupported provenance block (%s)" schema)
        | Malformed _ -> refuse "refusing to add lineage to a malformed provenance block"
        | Supported(block, _) ->
            match references with
            | JArray items ->
                match items |> List.forall (Some >> isNonEmptyString), surrogateFindings (JObject [ "derivedFrom", references ]), credentialFindings (JObject [ "derivedFrom", references ]) with
                | false, _, _ -> refuse "lineage references must be non-empty strings"
                | true, (_ :: _ as unpaired), _ -> refuse (String.Join(", ", unpaired) + ": unpaired UTF-16 surrogate")
                | true, [], (_ :: _ as secrets) ->
                    refuse (String.Join(", ", secrets) + ": credential-like value; provenance must never carry authentication material")
                | true, [], [] ->
                    let current = derivedFrom block

                    let additions =
                        items
                        |> List.choose (Some >> stringOf)
                        |> List.distinct
                        |> List.filter (fun item -> not (List.contains item current))

                    if additions.IsEmpty then
                        Ok(block, false)
                    else
                        match classify (setMember "derivedFrom" (JArray((current @ additions) |> List.map JString)) node) with
                        | Supported(next, _) -> Ok(next, true)
                        | Malformed problems -> refuse ("the resulting lineage would be malformed: " + String.Join("; ", problems))
                        | Unsupported(schema, _) -> refuse (sprintf "the resulting lineage would be unsupported (%s)" schema)
            | _ -> refuse "lineage references must be an array"

    /// `addLineageJson` for a block this reader already understands.
    let addLineage (references: string list) (block: ProvenanceBlock) : Result<ProvenanceBlock * bool, string> =
        addLineageJson (JArray(references |> List.map JString)) block.Json

// -------------------------------------------------------------- requesters

/// Who requested a transition or a decision: an actor and, when known, the
/// execution (`EXE-...` or `EXT-<system>.<run-id>`) it acted in.
///
/// Carried beside a request, never inside the checks. An absent requester is
/// unknown, never inferred from a provider, a capability, the host process,
/// or any ambient signal (ORDO-PROV-02 / RQ-ROS-2026-A016).
///
/// Constructed only by `Requester.create`, so a requester always serializes
/// into a `supported` block.
type Requester =
    private
        { ActorValue: Actor
          ExecutionValue: string option }

    member this.Actor = this.ActorValue
    /// The execution key. `None` means the execution is not known; the
    /// contribution is then keyed `EXT-op.<operation id>`.
    member this.Execution = this.ExecutionValue

[<RequireQualifiedAccess>]
module Requester =

    /// A requester, refusing an execution key that is not an execution and
    /// an actor the interchange contract would reject.
    let create (actor: Actor) (execution: string option) : Result<Requester, string list> =
        let actorProblems = Actor.problems "actor" (Actor.encode actor)

        let executionProblems =
            match execution with
            | Some key when not (ContributionKey.isExecution key) ->
                [ sprintf "execution '%s' must be EXE-... or EXT-<system>.<run-id>" key ]
            | _ -> []

        let declared =
            JObject [ "actor", Actor.encode actor; "execution", (execution |> Option.map JString |> Option.defaultValue JNull) ]

        let unpaired =
            surrogateFindings declared
            |> List.map (fun path -> path + ": unpaired UTF-16 surrogate; provenance must be well-formed Unicode")

        let secrets =
            credentialFindings declared
            |> List.map (fun path -> path + ": credential-like value; provenance must never carry authentication material")

        match actorProblems @ executionProblems @ unpaired @ secrets with
        | [] -> Ok { ActorValue = actor; ExecutionValue = execution }
        | problems -> Error problems

    /// The contribution key: the execution, or `EXT-op.<seg(operationId)>`.
    /// Refused when there is no execution and the operation id cannot form
    /// a key (empty, or not well-formed Unicode; contract 1.2).
    let contributionKey (operationId: string) (requester: Requester) : Result<string, string> =
        match requester.Execution with
        | Some execution -> Ok execution
        | None -> ContributionKey.ofOperation operationId

    /// The interchange form of an instant: ISO-8601 UTC with milliseconds.
    let private timestamp (at: DateTimeOffset) =
        at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)

    /// The requester's `created` contribution to the request it made.
    let creation
        (operationId: string)
        (at: DateTimeOffset)
        (reason: string option)
        (requester: Requester)
        : Result<Contribution, string> =
        contributionKey operationId requester
        |> Result.map (fun key ->
            { Key = key
              Operations = [ "created" ]
              At = timestamp at
              Last = None
              Actor = requester.Actor
              Reason = reason |> Option.map (fun text -> text.Replace("\r", " ").Replace("\n", " "))
              Evidence = [] })

    /// The request's `praxis.provenance/1` block: one `created` contribution
    /// by the requester, keyed by its execution or `EXT-op.<operationId>`.
    /// The result is re-classified, so an `Ok` block always classifies
    /// `supported`; a key that cannot be formed, or a reason that would make
    /// the block malformed (a credential, an unpaired surrogate), is refused.
    let toBlock
        (operationId: string)
        (at: DateTimeOffset)
        (reason: string option)
        (requester: Requester)
        : Result<ProvenanceBlock, string> =
        creation operationId at reason requester
        |> Result.bind (fun contribution ->
            match
                ProvenanceBlock.classify (
                    JObject
                        [ "schema", JString SchemaTag
                          "contributions", JObject [ contribution.Key, Contribution.encode contribution ] ]
                )
            with
            | Supported(block, _) -> Ok block
            | Malformed problems -> Error("the request's provenance would be malformed: " + String.Join("; ", problems))
            | Unsupported(schema, _) -> Error(sprintf "the request's provenance would be unsupported (%s)" schema))

    /// The requester recorded as a block's originator, if one is recorded.
    /// `None` means unknown: nothing else in the block is promoted into a
    /// requester.
    let ofBlock (block: ProvenanceBlock) : Requester option =
        ProvenanceBlock.originator block
        |> Option.map (fun origin ->
            { ActorValue = origin.Actor
              ExecutionValue =
                if ContributionKey.isExecution origin.Key && not (origin.Key.StartsWith("EXT-op.", StringComparison.Ordinal)) then
                    Some origin.Key
                else
                    None })

// ---------------------------------------------------------- attributed records

/// A record together with the provenance of the contributions that created
/// or settled it — evidence and who produced it, an obligation and who
/// created or satisfied it, an unknown effect and who attempted it, a
/// negative observation and who searched.
///
/// Provenance sits *beside* the record, so every check that takes the record
/// (`Evidence.checkAll`, `Transition.evaluate`, coverage, obligations) cannot
/// see it. Provenance never raises evidence strength, confidence, or
/// authority (ORDO-PROV-04 / ORDO-PROV-05 / RQ-ROS-2026-A019).
type Attributed<'record> =
    { Record: 'record
      /// `None` means unattributed: unknown, never inferred.
      Provenance: CarriedProvenance option }

[<RequireQualifiedAccess>]
module Attributed =

    let unattributed (record: 'record) = { Record = record; Provenance = None }

    let withBlock (block: ProvenanceBlock) (record: 'record) =
        { Record = record
          Provenance = Some(Understood block) }

    /// The records alone — the only form evaluation accepts.
    let records (items: Attributed<'record> list) = items |> List.map (fun item -> item.Record)

    /// Records another contribution (for example `resolved` when an
    /// obligation is satisfied). Refuses to write into provenance of a major
    /// version it does not interpret.
    let contribute (contribution: Contribution) (item: Attributed<'record>) : Result<Attributed<'record>, string> =
        let current =
            match item.Provenance with
            | None -> Ok ProvenanceBlock.empty
            | Some(Understood block) -> Ok block
            | Some(CarriedVerbatim(schema, _)) ->
                Error(sprintf "refusing to append to a unsupported provenance block (%s)" schema)

        current
        |> Result.bind (ProvenanceBlock.append contribution)
        |> Result.map (fun (block, _) -> { item with Provenance = Some(Understood block) })

    /// Changes the record without touching its provenance.
    let map (f: 'a -> 'b) (item: Attributed<'a>) : Attributed<'b> =
        { Record = f item.Record
          Provenance = item.Provenance }

[<RequireQualifiedAccess>]
module CarriedProvenance =

    /// The JSON to write: the understood block, or the verbatim original.
    let toJson (carried: CarriedProvenance) =
        match carried with
        | Understood block -> block.Json
        | CarriedVerbatim(_, verbatim) -> verbatim
