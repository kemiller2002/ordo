/// Ordo decision events, `ordo.decision-event/1` (ORDO-OBS-001).
///
/// An append-only record of what Ordo's pattern recognition decided, kept
/// apart from what happened afterwards. Four questions stay separate, each
/// with its own event kind, and none is ever inferred from another:
///
/// * `pattern.evaluated`  - did a registered pattern claim the requirement?
/// * `pattern.selection`  - what did recognition do with it: construct it
///                          with a pattern, hand it to an agent, escalate a
///                          decision, or refuse it?
/// * `agent.execution`    - what the executor reported doing afterwards.
/// * `verification.result`- what an independent verifier concluded.
///
/// A selection is never evidence of execution, and an execution is never
/// evidence of verification. A missing event means "not recorded", which the
/// summary reports as unknown rather than as a failure or a zero.
///
/// Every field is an identifier, an enumeration, a digest, a count or a
/// duration. No requirement prose, prompt, source content, blocker reason or
/// decision question is ever written, and caller-supplied identifiers are
/// refused when they do not look like identifiers or look like credentials.
///
/// This module is pure: the host supplies the clock, a per-invocation nonce
/// and the measured duration, and owns all I/O.
module Ordo.Core.DecisionEvents

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.PatternRegistry
open Ordo.Core.PatternRecognition

[<Literal>]
let EventSchemaVersion = "ordo.decision-event/1"

[<Literal>]
let SummarySchemaVersion = "ordo.decision-summary/1"

// ---------------------------------------------------------------------------
// Vocabulary.
// ---------------------------------------------------------------------------

[<RequireQualifiedAccess>]
type MatchStatus =
    /// Exactly the claimed patterns construct the requirement.
    | Matched
    /// A pattern generated part of the structure; the rest needs an agent or a decision.
    | Partial
    /// No registered pattern claimed the requirement.
    | Unmatched
    /// More than one pattern would construct it differently; a person must choose.
    | Ambiguous
    /// Recognition refused the requirement (invalid source, defective content, refused generation).
    | Rejected

[<RequireQualifiedAccess>]
type SelectionDisposition =
    /// Recognition classified the requirement as constructible by its patterns.
    | PatternSelected
    /// The requirement was handed to agent reasoning through a delegation packet.
    | FallbackAgent
    /// A decision is required first; nothing may be constructed yet.
    | Escalated
    /// The requirement was refused.
    | Rejected

[<RequireQualifiedAccess>]
type ExecutionStatus =
    | Succeeded
    | Failed
    | Abandoned

[<RequireQualifiedAccess>]
type VerificationVerdict =
    | Passed
    | Failed
    /// The verifier ran and could not decide. Neither success nor failure.
    | Indeterminate

/// Identifiers that let an event be joined to Praxis work and Dokimos results.
/// Every member is optional because not every source knows every identifier;
/// absent is recorded as absent, never guessed.
type Correlation =
    { /// One recognition invocation; shared by its run, evaluation and selection events.
      RecognitionId: string option
      RunId: string option
      SessionId: string option
      WorkItemId: string option
      CohortId: string option
      RequirementKey: string option
      RequirementId: string option
      /// Deterministic per (inputs, registry, cohort, requirement): the
      /// verification correlation id. See `decisionId`.
      DecisionId: string option
      PacketId: string option }

type PatternMatch =
    { Pattern: PatternRef
      /// The pattern's inventory family, from the registry contract.
      Family: string option
      /// "constructs" or "interfaceOnly".
      Claim: string }

type RunFacts =
    { RegistryVersion: string
      ManifestDigest: string
      BlueprintDigest: string
      DecisionsDigest: string option
      Assigned: int
      Evaluated: int
      Excluded: int
      Constructible: int
      DecisionRequired: int
      Delegated: int
      Blocked: int
      Packets: int
      /// Wall-clock duration of the whole recognition, measured by the host.
      DurationMs: int64 option }

type EvaluationFacts =
    { Status: MatchStatus
      Patterns: PatternMatch list
      /// Gap kinds only (e.g. "patternSelectionRequired"); never the question text.
      GapKinds: string list
      /// Blocker kinds only (e.g. "unusableContent"); never the reason text.
      BlockerKinds: string list }

type SelectionFacts =
    { Disposition: SelectionDisposition
      Selected: PatternRef list
      GeneratedBy: PatternRef list }

type ExecutionFacts =
    { Status: ExecutionStatus
      DurationMs: int64 option
      Executor: string option }

type VerificationFacts =
    { Verdict: VerificationVerdict
      Verifier: string
      /// What the verdict covers, e.g. "completion", "recognition-classification".
      Scope: string
      /// sha256 of the verifier's receipt, when one was supplied.
      ReceiptDigest: string option
      Commit: string option }

type EventBody =
    | RecognitionRun of RunFacts
    | PatternEvaluated of EvaluationFacts
    | PatternSelection of SelectionFacts
    | AgentExecution of ExecutionFacts
    | VerificationResult of VerificationFacts

type DecisionEvent =
    { EventId: string
      OccurredAt: DateTimeOffset
      OrdoVersion: string
      /// Who observed the fact: "ordo.recognize" (machine-observed),
      /// "agent-reported", "dokimos.completion-verification", ...
      EvidenceSource: string
      Correlation: Correlation
      Body: EventBody }

[<RequireQualifiedAccess>]
module Kind =
    let [<Literal>] Run = "recognition.run"
    let [<Literal>] Evaluated = "pattern.evaluated"
    let [<Literal>] Selection = "pattern.selection"
    let [<Literal>] Execution = "agent.execution"
    let [<Literal>] Verification = "verification.result"

let kindOf body =
    match body with
    | RecognitionRun _ -> Kind.Run
    | PatternEvaluated _ -> Kind.Evaluated
    | PatternSelection _ -> Kind.Selection
    | AgentExecution _ -> Kind.Execution
    | VerificationResult _ -> Kind.Verification

// ---------------------------------------------------------------------------
// Wire tokens.
// ---------------------------------------------------------------------------

let matchStatusToken s =
    match s with
    | MatchStatus.Matched -> "matched"
    | MatchStatus.Partial -> "partial"
    | MatchStatus.Unmatched -> "unmatched"
    | MatchStatus.Ambiguous -> "ambiguous"
    | MatchStatus.Rejected -> "rejected"

let dispositionToken d =
    match d with
    | SelectionDisposition.PatternSelected -> "pattern-selected"
    | SelectionDisposition.FallbackAgent -> "fallback-agent"
    | SelectionDisposition.Escalated -> "escalated"
    | SelectionDisposition.Rejected -> "rejected"

let executionToken s =
    match s with
    | ExecutionStatus.Succeeded -> "succeeded"
    | ExecutionStatus.Failed -> "failed"
    | ExecutionStatus.Abandoned -> "abandoned"

let verdictToken v =
    match v with
    | VerificationVerdict.Passed -> "passed"
    | VerificationVerdict.Failed -> "failed"
    | VerificationVerdict.Indeterminate -> "indeterminate"

let private tokenTable (render: 'a -> string) (cases: 'a list) =
    let table = cases |> List.map (fun c -> render c, c) |> Map.ofList
    fun (token: string) -> Map.tryFind token table

let parseMatchStatus =
    tokenTable matchStatusToken [ MatchStatus.Matched; MatchStatus.Partial; MatchStatus.Unmatched; MatchStatus.Ambiguous; MatchStatus.Rejected ]

let parseDisposition =
    tokenTable dispositionToken
        [ SelectionDisposition.PatternSelected; SelectionDisposition.FallbackAgent; SelectionDisposition.Escalated; SelectionDisposition.Rejected ]

let parseExecutionStatus =
    tokenTable executionToken [ ExecutionStatus.Succeeded; ExecutionStatus.Failed; ExecutionStatus.Abandoned ]

let parseVerdict =
    tokenTable verdictToken [ VerificationVerdict.Passed; VerificationVerdict.Failed; VerificationVerdict.Indeterminate ]

// ---------------------------------------------------------------------------
// Identifier hygiene. Telemetry carries identifiers only.
// ---------------------------------------------------------------------------

let private identifierChar (c: char) =
    Char.IsAsciiLetterOrDigit c || "._:/@#+-".Contains c

let private hasIdentifierShape (value: string) =
    value.Length <= 160 && Char.IsAsciiLetterOrDigit value[0] && Seq.forall identifierChar value

let private credentialPrefixes = [ "sk-"; "ghp_"; "gho_"; "ghs_"; "ghu_"; "ghr_"; "github_pat_"; "xoxa-"; "xoxb-"; "xoxp-"; "xoxr-"; "AKIA"; "AIza" ]

let private credentialMarkers = [ "password"; "passwd"; "secret"; "token"; "apikey"; "api_key"; "bearer" ]

let private looksLikeCredential (value: string) =
    let lower = value.ToLowerInvariant()
    credentialPrefixes |> List.exists (fun p -> value.StartsWith(p, StringComparison.Ordinal))
    || (value.StartsWith("eyJ", StringComparison.Ordinal) && value.Contains '.')
    || credentialMarkers |> List.exists (fun m -> lower.Contains(m + "=") || lower.Contains(m + ":"))

/// Ok when `value` is safe to persist as an identifier.
let checkIdentifier (field: string) (value: string) : Result<string, string> =
    if String.IsNullOrWhiteSpace value then Error(sprintf "%s is blank" field)
    elif not (hasIdentifierShape value) then Error(sprintf "%s is not an identifier (letters, digits and ._:/@#+- only, at most 160)" field)
    elif looksLikeCredential value then Error(sprintf "%s looks like a credential and was refused" field)
    else Ok value

/// Keeps a value only when it is a safe identifier. Used for ambient
/// identifiers read from the environment, where refusing the whole record
/// would be worse than omitting one field.
let safeIdentifier (value: string option) =
    value |> Option.bind (fun v -> match checkIdentifier "value" v with Ok ok -> Some ok | Error _ -> None)

// ---------------------------------------------------------------------------
// Deriving events from a recognition report.
// ---------------------------------------------------------------------------

let private sha256Hex (text: string) =
    text |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString |> fun h -> h.ToLowerInvariant()

/// The verification correlation id of one recognized requirement. Equal
/// inputs, registry, cohort and requirement give an equal id, so any
/// independent verifier holding the same inputs can derive it.
let decisionId (report: RecognitionReport) (requirementKey: string) =
    [ report.SourceManifestDigest
      report.BlueprintDigest
      report.DecisionRecordsDigest |> Option.defaultValue ""
      report.Pin.RegistryVersion
      report.CohortId
      requirementKey ]
    |> String.concat "\n"
    |> sha256Hex
    |> fun h -> "odc-" + h.Substring(0, 24)

let private gapKind gap =
    match gap with
    | EffectFailureUndecided _ -> "effectFailureUndecided"
    | DecisionNotAuthorized _ -> "decisionNotAuthorized"
    | PatternSelectionRequired _ -> "patternSelectionRequired"
    | ConflictUnresolved _ -> "conflictUnresolved"

let private blockerKind blocker =
    match blocker with
    | InvalidSource _ -> "invalidSource"
    | DispositionUnresolved _ -> "dispositionUnresolved"
    | UnusableContent _ -> "unusableContent"
    | AwaitingPrerequisites _ -> "awaitingPrerequisites"
    | GenerationRefused _ -> "generationRefused"

let matchStatusOf (r: RequirementRecognition) =
    match r.Outcome with
    | Blocked _ -> MatchStatus.Rejected
    | DecisionRequired(gaps, _) when gaps |> List.exists (function PatternSelectionRequired _ -> true | _ -> false) -> MatchStatus.Ambiguous
    | _ when List.isEmpty r.Claims -> MatchStatus.Unmatched
    | Constructible _ -> MatchStatus.Matched
    | Delegate _
    | DecisionRequired _ -> MatchStatus.Partial

let dispositionOf (outcome: Outcome) =
    match outcome with
    | Constructible _ -> SelectionDisposition.PatternSelected
    | Delegate _ -> SelectionDisposition.FallbackAgent
    | DecisionRequired _ -> SelectionDisposition.Escalated
    | Blocked _ -> SelectionDisposition.Rejected

/// What the host knows about this invocation.
type RecognitionContext =
    { OccurredAt: DateTimeOffset
      OrdoVersion: string
      /// Unique per invocation (e.g. a GUID). Makes event ids unique while
      /// keeping this function pure.
      Nonce: string
      RunId: string option
      SessionId: string option
      WorkItemId: string option
      DurationMs: int64 option
      /// Pattern families by reference, from the registry the report used.
      Families: Map<PatternRef, string> }

let familiesOf (registry: Registry) =
    registry.Contracts |> List.map (fun c -> c.Ref, c.Family) |> Map.ofList

let private eventId (nonce: string) (kind: string) (subject: string) =
    "ode-" + (sha256Hex (String.concat "\n" [ nonce; kind; subject ])).Substring(0, 24)

/// One run event, then for each evaluated requirement an evaluation event
/// followed by a selection event. Excluded requirements were not evaluated
/// and appear only in the run's counts.
let fromReport (context: RecognitionContext) (report: RecognitionReport) : DecisionEvent list =
    let packetOf key =
        report.Packets |> List.tryFind (fun p -> List.contains key p.RequirementKeys) |> Option.map _.Id
    let baseCorrelation =
        { RecognitionId = Some("orr-" + (sha256Hex context.Nonce).Substring(0, 24))
          RunId = context.RunId
          SessionId = context.SessionId
          WorkItemId = context.WorkItemId
          CohortId = Some report.CohortId
          RequirementKey = None
          RequirementId = None
          DecisionId = None
          PacketId = None }
    let make kind subject correlation body =
        { EventId = eventId context.Nonce kind subject
          OccurredAt = context.OccurredAt
          OrdoVersion = context.OrdoVersion
          EvidenceSource = "ordo.recognize"
          Correlation = correlation
          Body = body }
    let m = report.Measurements
    let run =
        make Kind.Run report.CohortId baseCorrelation
            (RecognitionRun
                { RegistryVersion = report.Pin.RegistryVersion
                  ManifestDigest = report.SourceManifestDigest
                  BlueprintDigest = report.BlueprintDigest
                  DecisionsDigest = report.DecisionRecordsDigest
                  Assigned = m.AssignedRequirements
                  Evaluated = m.Evaluated
                  Excluded = m.Excluded
                  Constructible = m.Constructible
                  DecisionRequired = m.DecisionRequired
                  Delegated = m.Delegated
                  Blocked = m.Blocked
                  Packets = m.DelegationPackets
                  DurationMs = context.DurationMs })
    let perRequirement (r: RequirementRecognition) =
        let id = decisionId report r.Key
        let correlation =
            { baseCorrelation with
                RequirementKey = safeIdentifier (Some r.Key)
                RequirementId = safeIdentifier (Some r.OriginalId)
                DecisionId = Some id
                PacketId = packetOf r.Key }
        let matches =
            r.Claims
            |> List.map (fun c ->
                { Pattern = c.Pattern
                  Family = Map.tryFind c.Pattern context.Families
                  Claim = match c.Kind with ClaimKind.Constructs -> "constructs" | ClaimKind.InterfaceOnly _ -> "interfaceOnly" })
        let gaps, blockers, selected, generatedBy =
            match r.Outcome with
            | Constructible(patterns, _) -> [], [], patterns, []
            | DecisionRequired(gaps, by) -> gaps |> List.map gapKind |> List.distinct, [], [], by
            | Delegate(_, by) -> [], [], [], by
            | Blocked blockers -> [], blockers |> List.map blockerKind |> List.distinct, [], []
        [ make Kind.Evaluated id correlation
              (PatternEvaluated { Status = matchStatusOf r; Patterns = matches; GapKinds = gaps; BlockerKinds = blockers })
          make Kind.Selection id correlation
              (PatternSelection { Disposition = dispositionOf r.Outcome; Selected = selected; GeneratedBy = generatedBy }) ]
    run :: (report.Requirements |> List.collect perRequirement)

/// An executor's report of what it did with a decision or packet. Recorded
/// as reported; it says nothing about whether the work is correct.
let executionEvent (occurredAt: DateTimeOffset) (ordoVersion: string) (nonce: string) (source: string)
                   (correlation: Correlation) (facts: ExecutionFacts) =
    { EventId = eventId nonce Kind.Execution (string occurredAt.UtcTicks)
      OccurredAt = occurredAt
      OrdoVersion = ordoVersion
      EvidenceSource = source
      Correlation = correlation
      Body = AgentExecution facts }

/// An independent verifier's conclusion about a decision, packet or work item.
let verificationEvent (occurredAt: DateTimeOffset) (ordoVersion: string) (nonce: string) (source: string)
                      (correlation: Correlation) (facts: VerificationFacts) =
    { EventId = eventId nonce Kind.Verification (string occurredAt.UtcTicks)
      OccurredAt = occurredAt
      OrdoVersion = ordoVersion
      EvidenceSource = source
      Correlation = correlation
      Body = VerificationResult facts }

// ---------------------------------------------------------------------------
// Encoding: one compact JSON object per line.
// ---------------------------------------------------------------------------

let private jOpt f value = value |> Option.map f |> Option.defaultValue JNull
let private jStr = jOpt JString
let private jRef (r: PatternRef) = JObject [ "id", JString r.Id; "version", JInt(int64 r.Version) ]
let private jRefs refs = JArray(refs |> List.map jRef)

let private encodeBody body =
    match body with
    | RecognitionRun f ->
        [ "registryVersion", JString f.RegistryVersion
          "manifestDigest", JString f.ManifestDigest
          "blueprintDigest", JString f.BlueprintDigest
          "decisionsDigest", jStr f.DecisionsDigest
          "counts",
          JObject [ "assigned", JInt(int64 f.Assigned)
                    "evaluated", JInt(int64 f.Evaluated)
                    "excluded", JInt(int64 f.Excluded)
                    "constructible", JInt(int64 f.Constructible)
                    "decisionRequired", JInt(int64 f.DecisionRequired)
                    "delegated", JInt(int64 f.Delegated)
                    "blocked", JInt(int64 f.Blocked)
                    "packets", JInt(int64 f.Packets) ]
          "durationMs", jOpt JInt f.DurationMs ]
    | PatternEvaluated f ->
        [ "matchStatus", JString(matchStatusToken f.Status)
          "patterns",
          JArray(f.Patterns |> List.map (fun p ->
              JObject [ "id", JString p.Pattern.Id
                        "version", JInt(int64 p.Pattern.Version)
                        "family", jStr p.Family
                        "claim", JString p.Claim ]))
          "gapKinds", JArray(f.GapKinds |> List.map JString)
          "blockerKinds", JArray(f.BlockerKinds |> List.map JString) ]
    | PatternSelection f ->
        [ "disposition", JString(dispositionToken f.Disposition)
          "selected", jRefs f.Selected
          "generatedBy", jRefs f.GeneratedBy ]
    | AgentExecution f ->
        [ "status", JString(executionToken f.Status)
          "durationMs", jOpt JInt f.DurationMs
          "executor", jStr f.Executor ]
    | VerificationResult f ->
        [ "verdict", JString(verdictToken f.Verdict)
          "verifier", JString f.Verifier
          "scope", JString f.Scope
          "receiptDigest", jStr f.ReceiptDigest
          "commit", jStr f.Commit ]

let encodeEvent (e: DecisionEvent) : string =
    let c = e.Correlation
    JObject
        [ "schemaVersion", JString EventSchemaVersion
          "eventId", JString e.EventId
          "kind", JString(kindOf e.Body)
          "occurredAt", JString(e.OccurredAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))
          "ordoVersion", JString e.OrdoVersion
          "evidenceSource", JString e.EvidenceSource
          "correlation",
          JObject [ "recognitionId", jStr c.RecognitionId
                    "runId", jStr c.RunId
                    "sessionId", jStr c.SessionId
                    "workItemId", jStr c.WorkItemId
                    "cohortId", jStr c.CohortId
                    "requirementKey", jStr c.RequirementKey
                    "requirementId", jStr c.RequirementId
                    "decisionId", jStr c.DecisionId
                    "packetId", jStr c.PacketId ]
          "data", JObject(encodeBody e.Body) ]
    |> renderCompact

// ---------------------------------------------------------------------------
// Decoding. Tolerant of a damaged log: each line stands alone.
// ---------------------------------------------------------------------------

let private field name (value: JsonValue) =
    match tryMember name value with
    | Ok(Some v) -> Ok v
    | Ok None -> Error(sprintf "missing %s" name)
    | Error _ -> Error "not an object"

let private str name value =
    field name value |> Result.bind (function JString s -> Ok s | _ -> Error(sprintf "%s is not a string" name))

let private optStr name value =
    match tryMember name value with
    | Ok(Some(JString s)) -> Ok(Some s)
    | Ok(Some JNull) | Ok None -> Ok None
    | _ -> Error(sprintf "%s is not a string" name)

let private int' name value =
    field name value |> Result.bind (function JInt n when n >= 0L && n <= int64 Int32.MaxValue -> Ok(int n) | _ -> Error(sprintf "%s is not a count" name))

let private optInt64 name value =
    match tryMember name value with
    | Ok(Some(JInt n)) when n >= 0L -> Ok(Some n)
    | Ok(Some JNull) | Ok None -> Ok None
    | _ -> Error(sprintf "%s is not a non-negative integer" name)

let private all (results: Result<'a, string> list) =
    List.foldBack (fun r acc -> match r, acc with Ok x, Ok xs -> Ok(x :: xs) | Error e, _ -> Error e | _, Error e -> Error e) results (Ok [])

let private arr name value =
    field name value |> Result.bind (function JArray xs -> Ok xs | _ -> Error(sprintf "%s is not an array" name))

let private strings name value =
    arr name value |> Result.bind (List.map (function JString s -> Ok s | _ -> Error(sprintf "%s holds a non-string" name)) >> all)

let private patternRef value =
    match str "id" value, int' "version" value with
    | Ok id, Ok v when v >= 1 -> Ok { PatternRef.Id = id; Version = v }
    | _ -> Error "malformed pattern reference"

let private refs name value = arr name value |> Result.bind (List.map patternRef >> all)

let private token name parse value =
    str name value |> Result.bind (fun t -> parse t |> Option.map Ok |> Option.defaultValue (Error(sprintf "unknown %s '%s'" name t)))

let private decodeBody kind data =
    match kind with
    | Kind.Run ->
        field "counts" data
        |> Result.bind (fun counts ->
            match str "registryVersion" data, str "manifestDigest" data, str "blueprintDigest" data, optStr "decisionsDigest" data,
                  all [ int' "assigned" counts; int' "evaluated" counts; int' "excluded" counts; int' "constructible" counts
                        int' "decisionRequired" counts; int' "delegated" counts; int' "blocked" counts; int' "packets" counts ],
                  optInt64 "durationMs" data with
            | Ok rv, Ok md, Ok bd, Ok dd, Ok [ a; ev; ex; co; dr; de; bl; pk ], Ok dur ->
                Ok(RecognitionRun
                    { RegistryVersion = rv; ManifestDigest = md; BlueprintDigest = bd; DecisionsDigest = dd
                      Assigned = a; Evaluated = ev; Excluded = ex; Constructible = co; DecisionRequired = dr
                      Delegated = de; Blocked = bl; Packets = pk; DurationMs = dur })
            | _ -> Error "malformed recognition.run data")
    | Kind.Evaluated ->
        let pattern p =
            match patternRef p, optStr "family" p, str "claim" p with
            | Ok r, Ok fam, Ok claim -> Ok { Pattern = r; Family = fam; Claim = claim }
            | _ -> Error "malformed pattern match"
        match token "matchStatus" parseMatchStatus data, arr "patterns" data |> Result.bind (List.map pattern >> all),
              strings "gapKinds" data, strings "blockerKinds" data with
        | Ok s, Ok ps, Ok gs, Ok bs -> Ok(PatternEvaluated { Status = s; Patterns = ps; GapKinds = gs; BlockerKinds = bs })
        | Error e, _, _, _ | _, Error e, _, _ | _, _, Error e, _ | _, _, _, Error e -> Error e
    | Kind.Selection ->
        match token "disposition" parseDisposition data, refs "selected" data, refs "generatedBy" data with
        | Ok d, Ok s, Ok g -> Ok(PatternSelection { Disposition = d; Selected = s; GeneratedBy = g })
        | Error e, _, _ | _, Error e, _ | _, _, Error e -> Error e
    | Kind.Execution ->
        match token "status" parseExecutionStatus data, optInt64 "durationMs" data, optStr "executor" data with
        | Ok s, Ok d, Ok x -> Ok(AgentExecution { Status = s; DurationMs = d; Executor = x })
        | Error e, _, _ | _, Error e, _ | _, _, Error e -> Error e
    | Kind.Verification ->
        match token "verdict" parseVerdict data, str "verifier" data, str "scope" data, optStr "receiptDigest" data, optStr "commit" data with
        | Ok v, Ok who, Ok scope, Ok digest, Ok commit ->
            Ok(VerificationResult { Verdict = v; Verifier = who; Scope = scope; ReceiptDigest = digest; Commit = commit })
        | Error e, _, _, _, _ | _, Error e, _, _, _ | _, _, Error e, _, _ | _, _, _, Error e, _ | _, _, _, _, Error e -> Error e
    | other -> Error(sprintf "unknown event kind '%s'" other)

let decodeEvent (line: string) : Result<DecisionEvent, string> =
    match parse line with
    | Error _ -> Error "not valid JSON"
    | Ok json ->
        match str "schemaVersion" json with
        | Ok v when v <> EventSchemaVersion -> Error(sprintf "unsupported schemaVersion '%s'" v)
        | Error e -> Error e
        | Ok _ ->
            let correlation =
                field "correlation" json
                |> Result.bind (fun c ->
                    match all [ optStr "recognitionId" c; optStr "runId" c; optStr "sessionId" c; optStr "workItemId" c; optStr "cohortId" c
                                optStr "requirementKey" c; optStr "requirementId" c; optStr "decisionId" c; optStr "packetId" c ] with
                    | Ok [ recognition; run; session; work; cohort; key; rid; did; pid ] ->
                        Ok { RecognitionId = recognition; RunId = run; SessionId = session; WorkItemId = work; CohortId = cohort
                             RequirementKey = key; RequirementId = rid; DecisionId = did; PacketId = pid }
                    | Ok _ -> Error "malformed correlation"
                    | Error e -> Error e)
            let occurredAt =
                str "occurredAt" json
                |> Result.bind (fun t ->
                    match DateTimeOffset.TryParse(t, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.AssumeUniversal) with
                    | true, at -> Ok at
                    | _ -> Error "occurredAt is not a timestamp")
            match str "eventId" json, str "kind" json, occurredAt, str "ordoVersion" json, str "evidenceSource" json, correlation,
                  field "data" json with
            | Ok id, Ok kind, Ok at, Ok version, Ok source, Ok corr, Ok data ->
                decodeBody kind data
                |> Result.map (fun body ->
                    { EventId = id; OccurredAt = at; OrdoVersion = version; EvidenceSource = source; Correlation = corr; Body = body })
            | Error e, _, _, _, _, _, _ | _, Error e, _, _, _, _, _ | _, _, Error e, _, _, _, _ | _, _, _, Error e, _, _, _
            | _, _, _, _, Error e, _, _ | _, _, _, _, _, Error e, _ | _, _, _, _, _, _, Error e -> Error e

type CorruptLine = { Line: int; Reason: string }

type LogRead =
    { Events: DecisionEvent list
      Corrupt: CorruptLine list
      /// Event ids seen more than once (only the first is kept).
      Duplicates: int }

/// Reads a whole log. Blank lines are ignored; every other line either
/// decodes or is reported as corrupt with its 1-based line number.
let readLog (text: string) : LogRead =
    let decoded =
        text.Split('\n')
        |> Array.toList
        |> List.mapi (fun i line -> i + 1, line.TrimEnd('\r'))
        |> List.filter (snd >> String.IsNullOrWhiteSpace >> not)
        |> List.map (fun (n, line) -> n, decodeEvent line)
    let events = decoded |> List.choose (snd >> function Ok e -> Some e | Error _ -> None)
    let unique = events |> List.distinctBy _.EventId
    { Events = unique
      Corrupt = decoded |> List.choose (fun (n, r) -> match r with Error reason -> Some { Line = n; Reason = reason } | Ok _ -> None)
      Duplicates = events.Length - unique.Length }

// ---------------------------------------------------------------------------
// Summary: what is known, and separately what is not.
// ---------------------------------------------------------------------------

type DecisionSummary =
    { Runs: int
      /// Runs whose evaluation events are fewer than the run's own evaluated count.
      IncompleteRuns: int
      Evaluated: int
      Matched: int
      Partial: int
      Unmatched: int
      Ambiguous: int
      Rejected: int
      /// Matched / Evaluated; None when nothing was evaluated (not zero).
      MatchRate: float option
      /// Claims by pattern family across evaluated decisions.
      UsageByFamily: (string * int) list
      UsageByPattern: (string * int) list
      Selections: (string * int) list
      FallbackToAgent: int
      /// Selected or delegated decisions with no execution event.
      ExecutionUnknown: int
      ExecutionsReported: (string * int) list
      /// Independent verdicts joined to a decision (directly or via packet).
      Verified: (string * int) list
      /// Decisions an executor reported done, with no verification recorded.
      ExecutedButUnverified: int
      /// Execution or verification events that name no known decision, packet or work item.
      Uncorrelated: int
      CorruptLines: int
      DuplicateEvents: int }

let summarize (log: LogRead) : DecisionSummary =
    let events = log.Events
    let pick f = events |> List.choose f
    let runs = pick (fun e -> match e.Body with RecognitionRun r -> Some(e, r) | _ -> None)
    let evaluations = pick (fun e -> match e.Body with PatternEvaluated f -> Some(e, f) | _ -> None)
    let selections = pick (fun e -> match e.Body with PatternSelection f -> Some(e, f) | _ -> None)
    let executions = pick (fun e -> match e.Body with AgentExecution f -> Some(e, f) | _ -> None)
    let verifications = pick (fun e -> match e.Body with VerificationResult f -> Some(e, f) | _ -> None)
    let count status = evaluations |> List.filter (fun (_, f) -> f.Status = status) |> List.length
    let countBy key items = items |> List.countBy key |> List.sortBy fst
    // A decision is the unit of correlation; its packet and work item are aliases for joining.
    let decisions =
        selections
        |> List.choose (fun (e, f) -> e.Correlation.DecisionId |> Option.map (fun id -> id, (e.Correlation, f)))
        |> Map.ofList
    let decisionsOf (c: Correlation) =
        match c.DecisionId, c.PacketId, c.WorkItemId with
        | Some id, _, _ when decisions.ContainsKey id -> [ id ]
        | _, Some pid, _ ->
            decisions |> Map.toList |> List.filter (fun (_, (dc, _)) -> dc.PacketId = Some pid) |> List.map fst
        | _, _, Some wid ->
            decisions |> Map.toList |> List.filter (fun (_, (dc, _)) -> dc.WorkItemId = Some wid) |> List.map fst
        | _ -> []
    let executedDecisions = executions |> List.collect (fst >> _.Correlation >> decisionsOf) |> Set.ofList
    let verifiedDecisions = verifications |> List.collect (fst >> _.Correlation >> decisionsOf) |> Set.ofList
    let actionable =
        decisions
        |> Map.filter (fun _ (_, f) ->
            f.Disposition = SelectionDisposition.PatternSelected || f.Disposition = SelectionDisposition.FallbackAgent)
    let incomplete =
        runs
        |> List.filter (fun (e, r) ->
            let found =
                evaluations
                |> List.filter (fun (ev, _) -> e.Correlation.RecognitionId.IsSome && ev.Correlation.RecognitionId = e.Correlation.RecognitionId)
                |> List.length
            found < r.Evaluated)
    let evaluated = evaluations.Length
    let matched = count MatchStatus.Matched
    { Runs = runs.Length
      IncompleteRuns = incomplete.Length
      Evaluated = evaluated
      Matched = matched
      Partial = count MatchStatus.Partial
      Unmatched = count MatchStatus.Unmatched
      Ambiguous = count MatchStatus.Ambiguous
      Rejected = count MatchStatus.Rejected
      MatchRate = if evaluated = 0 then None else Some(float matched / float evaluated)
      UsageByFamily =
        evaluations
        |> List.collect (fun (_, f) -> f.Patterns |> List.map (fun p -> p.Family |> Option.defaultValue "unknown"))
        |> countBy id
      UsageByPattern = evaluations |> List.collect (fun (_, f) -> f.Patterns |> List.map (fun p -> PatternRef.label p.Pattern)) |> countBy id
      Selections = selections |> List.map (fun (_, f) -> dispositionToken f.Disposition) |> countBy id
      FallbackToAgent = selections |> List.filter (fun (_, f) -> f.Disposition = SelectionDisposition.FallbackAgent) |> List.length
      ExecutionUnknown = actionable |> Map.filter (fun id _ -> not (executedDecisions.Contains id)) |> Map.count
      ExecutionsReported = executions |> List.map (fun (_, f) -> executionToken f.Status) |> countBy id
      Verified = verifications |> List.filter (fst >> _.Correlation >> decisionsOf >> List.isEmpty >> not) |> List.map (fun (_, f) -> verdictToken f.Verdict) |> countBy id
      ExecutedButUnverified = executedDecisions |> Set.filter (verifiedDecisions.Contains >> not) |> Set.count
      Uncorrelated =
        (executions |> List.map fst) @ (verifications |> List.map fst)
        |> List.filter (_.Correlation >> decisionsOf >> List.isEmpty)
        |> List.length
      CorruptLines = log.Corrupt.Length
      DuplicateEvents = log.Duplicates }

let encodeSummary (s: DecisionSummary) : string =
    let pairs xs = JObject(xs |> List.map (fun (k, v: int) -> k, JInt(int64 v)))
    JObject
        [ "schemaVersion", JString SummarySchemaVersion
          "runs", JInt(int64 s.Runs)
          "incompleteRuns", JInt(int64 s.IncompleteRuns)
          "evaluated", JInt(int64 s.Evaluated)
          "matchStatus",
          JObject [ "matched", JInt(int64 s.Matched)
                    "partial", JInt(int64 s.Partial)
                    "unmatched", JInt(int64 s.Unmatched)
                    "ambiguous", JInt(int64 s.Ambiguous)
                    "rejected", JInt(int64 s.Rejected) ]
          "matchRate", s.MatchRate |> Option.map JFloat |> Option.defaultValue JNull
          "usageByFamily", pairs s.UsageByFamily
          "usageByPattern", pairs s.UsageByPattern
          "selections", pairs s.Selections
          "fallbackToAgent", JInt(int64 s.FallbackToAgent)
          "executionsReported", pairs s.ExecutionsReported
          "executionUnknown", JInt(int64 s.ExecutionUnknown)
          "verified", pairs s.Verified
          "executedButUnverified", JInt(int64 s.ExecutedButUnverified)
          "uncorrelated", JInt(int64 s.Uncorrelated)
          "corruptLines", JInt(int64 s.CorruptLines)
          "duplicateEvents", JInt(int64 s.DuplicateEvents) ]
    |> render
