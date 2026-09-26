/// Who asked: requester provenance, kept apart from authority and evidence.
///
/// Three questions stay separate in Ordo (ORDO-NEXT-07, DF-SDE-2026-D68A):
///
/// * who requested this? A `Requester`, from this module. It is an audit fact.
/// * may this actor request this? A `CapabilitySet`. It is host-supplied
///   semantic authority (DF-SDE-2026-0011).
/// * why should the transition occur? Evidence, coverage, obligations and
///   policy.
///
/// A requester is self-reported, host-supplied provenance. It is not
/// authentication, not authorization, not evidence, and not a weight on
/// evidence. This module is compiled after `Evidence`, `Coverage`,
/// `NegativeKnowledge`, `Capability`, `Obligation` and `ExternalEffect`, so
/// none of them can refer to it. Transition evaluation carries it without
/// reading it.
///
/// The actor is the Praxis provenance actor (Praxis RQ-ROS-2026-A001),
/// reproduced as a small local type with identical JSON (see `Wire`). Ordo
/// takes no dependency on Praxis.
module Ordo.Core.Provenance

open System
open Ordo.Core.Json

/// What kind of actor asked.
///
/// `Agent` is an autonomous or semi-autonomous AI system. `Automation` is a
/// deterministic non-agent process, such as CI. `Unknown` is recorded
/// honestly rather than guessed. `Extension` is a namespaced `x-...` kind
/// from the Praxis vocabulary.
[<RequireQualifiedAccess>]
type ActorKind =
    | Agent
    | Human
    | Automation
    | Unknown
    | Extension of token: string

/// Why a proposed actor, execution key or requester was refused.
type ProvenanceError =
    | UnknownActorKind of token: string
    | ActorIdEmpty
    /// An agent must state provider, model and runtime, as the literal
    /// `unknown` when not known, so that absence is never mistaken for "not
    /// applicable".
    | AgentAttributeMissing of field: string
    | ActorAttributeEmpty of field: string
    /// An extension field reused a name the contract models.
    | ReservedExtensionField of name: string
    | DuplicateActorField of name: string
    | CredentialInIdentity of field: string
    | InvalidExecutionKey of raw: string
    /// An agent's request always happens inside a run, so it must name the
    /// `EXE-...` execution.
    | AgentRequiresExecution
    | InvalidForeignRun of system: string * run: string

[<RequireQualifiedAccess>]
module ActorKind =

    let private isExtensionToken (token: string) =
        token.Length > 2
        && token.StartsWith("x-", StringComparison.Ordinal)
        && (Char.IsAsciiLetterLower token[2] || Char.IsAsciiDigit token[2])
        && token |> Seq.skip 2 |> Seq.forall (fun c -> Char.IsAsciiLetterLower c || Char.IsAsciiDigit c || c = '-')

    let toWire kind =
        match kind with
        | ActorKind.Agent -> "agent"
        | ActorKind.Human -> "human"
        | ActorKind.Automation -> "automation"
        | ActorKind.Unknown -> "unknown"
        | ActorKind.Extension token -> token

    /// `None` for a token outside the vocabulary. Callers refuse it rather
    /// than coercing it to `Unknown`: a reader that turns an unrecognised
    /// kind into another kind has reinterpreted the record (ORDO-8404).
    let fromWire (token: string) =
        match token with
        | "agent" -> Some ActorKind.Agent
        | "human" -> Some ActorKind.Human
        | "automation" -> Some ActorKind.Automation
        | "unknown" -> Some ActorKind.Unknown
        | extension when isExtensionToken extension -> Some(ActorKind.Extension extension)
        | _ -> None

/// A guard against accidentally recording authentication material as
/// identity (Praxis RQ-ROS-2026-A013). It recognises the unambiguous shapes
/// of common credentials. It is not a secret scanner, so a value it passes
/// is not thereby proven safe.
///
/// It is written by hand rather than with regular expressions so that
/// `Ordo.Core` gains no assembly reference.
[<RequireQualifiedAccess>]
module Credentials =

    let private isWordChar (c: char) = Char.IsAsciiLetterOrDigit c || c = '_'

    let private runFrom (text: string) (start: int) (allowed: char -> bool) =
        let rec go index =
            if index < text.Length && allowed text[index] then go (index + 1) else index

        go start - start

    let private boundaryAt (text: string) (index: int) = index = 0 || not (isWordChar text[index - 1])

    let private startsAt (text: string) (index: int) (prefix: string) (comparison: StringComparison) =
        index + prefix.Length <= text.Length
        && String.Compare(text, index, prefix, 0, prefix.Length, comparison) = 0

    let private tokenChar (c: char) = Char.IsAsciiLetterOrDigit c || c = '_' || c = '-'

    /// `prefix` at a word boundary followed by at least `minimum` characters
    /// that `allowed` accepts.
    let private prefixed (comparison: StringComparison) (prefix: string) (minimum: int) (allowed: char -> bool) (text: string) =
        seq { 0 .. text.Length - prefix.Length }
        |> Seq.exists (fun index ->
            boundaryAt text index
            && startsAt text index prefix comparison
            && runFrom text (index + prefix.Length) allowed >= minimum)

    let private ordinal = prefixed StringComparison.Ordinal

    let private jwt (text: string) =
        seq { 0 .. text.Length - 3 }
        |> Seq.exists (fun index ->
            boundaryAt text index
            && startsAt text index "eyJ" StringComparison.Ordinal
            && (let first = runFrom text (index + 3) tokenChar
                let afterFirst = index + 3 + first

                first >= 10
                && afterFirst < text.Length
                && text[afterFirst] = '.'
                && (let second = runFrom text (afterFirst + 1) tokenChar
                    let afterSecond = afterFirst + 1 + second

                    second >= 10
                    && afterSecond < text.Length
                    && text[afterSecond] = '.'
                    && runFrom text (afterSecond + 1) tokenChar >= 10)))

    let private bearer (text: string) =
        seq { 0 .. text.Length - 6 }
        |> Seq.exists (fun index ->
            boundaryAt text index
            && startsAt text index "bearer" StringComparison.OrdinalIgnoreCase
            && (let spaces = runFrom text (index + 6) Char.IsWhiteSpace

                spaces > 0
                && runFrom text (index + 6 + spaces) (fun c -> Char.IsAsciiLetterOrDigit c || "._~+/=-".Contains c) >= 16))

    let private assignment (text: string) =
        [ "api_key"; "api-key"; "apikey"; "access_token"; "access-token"; "accesstoken"; "secret"; "password"; "passwd" ]
        |> List.exists (fun name ->
            seq { 0 .. text.Length - name.Length }
            |> Seq.exists (fun index ->
                boundaryAt text index
                && startsAt text index name StringComparison.OrdinalIgnoreCase
                && (let afterName = index + name.Length
                    let before = runFrom text afterName Char.IsWhiteSpace
                    let separator = afterName + before

                    separator < text.Length
                    && (text[separator] = '=' || text[separator] = ':')
                    && (let after = runFrom text (separator + 1) Char.IsWhiteSpace
                        runFrom text (separator + 1 + after) (Char.IsWhiteSpace >> not) >= 8))))

    let private privateKey (text: string) =
        text.Contains("-----BEGIN ", StringComparison.Ordinal)
        && text.Contains("PRIVATE KEY-----", StringComparison.Ordinal)

    let private upperOrDigit (c: char) = Char.IsAsciiLetterUpper c || Char.IsAsciiDigit c

    let private shapes: (string -> bool) list =
        [ ordinal "sk-" 16 tokenChar
          ordinal "ghp_" 20 Char.IsAsciiLetterOrDigit
          ordinal "gho_" 20 Char.IsAsciiLetterOrDigit
          ordinal "ghu_" 20 Char.IsAsciiLetterOrDigit
          ordinal "ghs_" 20 Char.IsAsciiLetterOrDigit
          ordinal "ghr_" 20 Char.IsAsciiLetterOrDigit
          ordinal "github_pat_" 20 (fun c -> Char.IsAsciiLetterOrDigit c || c = '_')
          yield! [ 'a'; 'b'; 'p'; 'o'; 's'; 'r' ] |> List.map (fun c -> ordinal $"xox{c}-" 10 (fun c -> Char.IsAsciiLetterOrDigit c || c = '-'))
          ordinal "AKIA" 16 upperOrDigit
          ordinal "AIza" 30 tokenChar
          bearer
          jwt
          assignment
          privateKey ]

    let looksLikeCredential (value: string) =
        not (String.IsNullOrEmpty value) && shapes |> List.exists (fun shape -> shape value)

    let rec private stringsIn (value: JsonValue) =
        match value with
        | JString text -> [ text ]
        | JArray items -> items |> List.collect stringsIn
        | JObject members -> members |> List.collect (fun (name, item) -> name :: stringsIn item)
        | JNull
        | JBool _
        | JInt _
        | JFloat _ -> []

    /// Whether any string anywhere inside a JSON value looks like a
    /// credential. Used for preserved extension fields, which Ordo does not
    /// model but still refuses to carry secrets in.
    let appearsIn (value: JsonValue) = stringsIn value |> List.exists looksLikeCredential

/// The portable, self-reported identity of whoever asked (Praxis actor).
///
/// `Provider`, `Model` and `Runtime` are `None` when not applicable (a human)
/// and `Some "unknown"` when applicable but not known. The two are never
/// conflated. `Extensions` holds the fields this version of Ordo does not
/// model, verbatim and in their original order, so a newer producer's
/// fields survive a round trip through Ordo.
type Actor =
    private
        { KindValue: ActorKind
          IdValue: string
          ProviderValue: string option
          ModelValue: string option
          RuntimeValue: string option
          ExtensionsValue: (string * JsonValue) list }

    member this.Kind = this.KindValue
    member this.Id = this.IdValue
    member this.Provider = this.ProviderValue
    member this.Model = this.ModelValue
    member this.Runtime = this.RuntimeValue
    member this.Extensions = this.ExtensionsValue

[<RequireQualifiedAccess>]
module Actor =

    [<Literal>]
    let UnknownValue = "unknown"

    /// The member names the contract models, in contract key order.
    let modelledFields = [ "kind"; "id"; "provider"; "model"; "runtime" ]

    let private attributeProblems (kind: ActorKind) (fields: (string * string option) list) =
        fields
        |> List.choose (fun (name, value) ->
            match kind, value with
            | ActorKind.Agent, None -> Some(AgentAttributeMissing name)
            | _, Some text when String.IsNullOrWhiteSpace text -> Some(ActorAttributeEmpty name)
            | _ -> None)

    /// Builds an actor, refusing what the contract refuses.
    let create
        (kind: ActorKind)
        (id: string)
        (provider: string option)
        (model: string option)
        (runtime: string option)
        (extensions: (string * JsonValue) list)
        : Result<Actor, ProvenanceError> =
        let attributes = [ "provider", provider; "model", model; "runtime", runtime ]
        let extensionNames = extensions |> List.map fst

        let problems =
            [ match kind with
              | ActorKind.Extension token when ActorKind.fromWire token <> Some kind -> UnknownActorKind token
              | _ -> ()
              if String.IsNullOrWhiteSpace id then
                  ActorIdEmpty
              yield! attributeProblems kind attributes
              yield!
                  extensionNames
                  |> List.filter (fun name -> List.contains name modelledFields)
                  |> List.map ReservedExtensionField
              yield!
                  extensionNames
                  |> List.countBy (fun name -> name)
                  |> List.filter (fun (_, count) -> count > 1)
                  |> List.map (fst >> DuplicateActorField)
              yield!
                  ("id", Some id) :: attributes
                  |> List.choose (fun (name, value) ->
                      match value with
                      | Some text when Credentials.looksLikeCredential text -> Some(CredentialInIdentity name)
                      | _ -> None)
              yield!
                  extensions
                  |> List.filter (snd >> Credentials.appearsIn)
                  |> List.map (fst >> CredentialInIdentity) ]

        match problems with
        | problem :: _ -> Error problem
        | [] ->
            Ok
                { KindValue = kind
                  IdValue = id
                  ProviderValue = provider
                  ModelValue = model
                  RuntimeValue = runtime
                  ExtensionsValue = extensions }

    /// An AI agent. Pass `unknown` for any attribute that is not known.
    let agent id provider model runtime =
        create ActorKind.Agent id (Some provider) (Some model) (Some runtime) []

    /// A person. Provider, model and runtime do not apply and are omitted.
    let human id = create ActorKind.Human id None None None []

    /// A deterministic non-agent process such as CI.
    let automation id provider model runtime =
        create ActorKind.Automation id (Some provider) (Some model) (Some runtime) []

    /// Nothing is known about who asked, and that is said explicitly.
    let unknown: Actor =
        { KindValue = ActorKind.Unknown
          IdValue = UnknownValue
          ProviderValue = Some UnknownValue
          ModelValue = Some UnknownValue
          RuntimeValue = Some UnknownValue
          ExtensionsValue = [] }

    let describe (actor: Actor) =
        $"{ActorKind.toWire actor.Kind}:{actor.Id}"

/// The run an actor was acting in: a Praxis execution (`EXE-...`), another
/// system's namespaced run (`EXE-<system>.<run>`), or a contribution outside
/// any run (`CTB-...`). Ordo validates and carries keys; it never mints a
/// Praxis `EXE-<timestamp>-<random>` key.
type ExecutionKey =
    private
    | ExecutionKey of string

    member this.Value =
        let (ExecutionKey value) = this
        value

    override this.ToString() = this.Value

[<RequireQualifiedAccess>]
module ExecutionKey =

    let private keyChar (c: char) = Char.IsAsciiLetterOrDigit c || c = '.' || c = '_' || c = '-'

    let private hasBody (prefix: string) (raw: string) =
        raw.StartsWith(prefix, StringComparison.Ordinal)
        && raw.Length > prefix.Length
        && raw |> Seq.skip prefix.Length |> Seq.forall keyChar

    let create (raw: string) : Result<ExecutionKey, ProvenanceError> =
        if not (String.IsNullOrEmpty raw) && (hasBody "EXE-" raw || hasBody "CTB-" raw) then
            Ok(ExecutionKey raw)
        else
            Error(InvalidExecutionKey raw)

    let value (ExecutionKey raw) = raw

    /// Keyed by a run (`EXE-...`) rather than a contribution (`CTB-...`).
    let isExecution (ExecutionKey raw) = raw.StartsWith("EXE-", StringComparison.Ordinal)

    /// A run of a system other than Praxis, namespaced so it can never
    /// collide with or impersonate a Praxis execution (Praxis RQ-ROS-2026-A014):
    /// `EXE-<system>.<run>`, where system is `[a-z][a-z0-9-]*` and run is
    /// `[A-Za-z0-9_-][A-Za-z0-9._-]*`.
    let foreign (system: string) (run: string) : Result<ExecutionKey, ProvenanceError> =
        let systemValid =
            not (String.IsNullOrEmpty system)
            && Char.IsAsciiLetterLower system[0]
            && system |> Seq.forall (fun c -> Char.IsAsciiLetterLower c || Char.IsAsciiDigit c || c = '-')

        let runValid =
            not (String.IsNullOrEmpty run)
            && run[0] <> '.'
            && run |> Seq.forall keyChar

        if systemValid && runValid then
            Ok(ExecutionKey $"EXE-{system}.{run}")
        else
            Error(InvalidForeignRun(system, run))

/// Who requested a decision or transition, and in which run.
///
/// Recorded, never consulted: nothing in Ordo's authority, evidence,
/// coverage, negative-knowledge or policy checks reads it.
type Requester =
    private
        { ActorValue: Actor
          ExecutionValue: ExecutionKey option }

    member this.Actor = this.ActorValue
    member this.Execution = this.ExecutionValue

[<RequireQualifiedAccess>]
module Requester =

    /// An agent must name its `EXE-...` run. Other actors may omit the run
    /// when it is not known; Ordo never invents one.
    let create (actor: Actor) (execution: ExecutionKey option) : Result<Requester, ProvenanceError> =
        match actor.Kind, execution with
        | ActorKind.Agent, None -> Error AgentRequiresExecution
        | ActorKind.Agent, Some key when not (ExecutionKey.isExecution key) -> Error AgentRequiresExecution
        | _ ->
            Ok
                { ActorValue = actor
                  ExecutionValue = execution }

    /// The Praxis identity keys a host may read (Praxis RQ-ROS-2026-A014).
    /// Nothing else is consulted.
    let identityVariables =
        [ "ROS_ACTOR_KIND"
          "ROS_ACTOR"
          "ROS_TELEMETRY_PROVIDER"
          "ROS_TELEMETRY_MODEL"
          "ROS_TELEMETRY_RUNTIME"
          "ROS_EXECUTION_ID"
          "GITHUB_ACTIONS" ]

    /// Resolves a requester from the whitelisted identity variables, through
    /// a lookup the host supplies, for example
    /// `Environment.GetEnvironmentVariable >> Option.ofObj`. The function is
    /// pure: Ordo itself reads no environment.
    ///
    /// Nothing is guessed. An unset attribute is `unknown`. An explicit kind
    /// outside the vocabulary is refused. GitHub Actions with nothing
    /// declared is the contract's automation default. The run is the
    /// propagated `ROS_EXECUTION_ID` when present, otherwise the host's own
    /// namespaced run `EXE-<system>.<run>` when the host names one,
    /// otherwise absent.
    let fromIdentityVariables
        (lookup: string -> string option)
        (ownRun: (string * string) option)
        : Result<Requester, ProvenanceError> =
        let read name =
            if List.contains name identityVariables then
                lookup name
                |> Option.map (fun value -> value.Trim())
                |> Option.filter (String.IsNullOrEmpty >> not)
            else
                None

        let declared =
            [ "ROS_ACTOR_KIND"; "ROS_ACTOR"; "ROS_TELEMETRY_PROVIDER"; "ROS_TELEMETRY_MODEL"; "ROS_TELEMETRY_RUNTIME" ]
            |> List.exists (read >> Option.isSome)

        let bind f result = Result.bind f result

        let actor =
            if not declared && read "GITHUB_ACTIONS" = Some "true" then
                Actor.automation "github/github-actions" "github" Actor.UnknownValue "github-actions"
            else
                let kind =
                    match read "ROS_ACTOR_KIND" with
                    | None -> Ok ActorKind.Unknown
                    | Some token ->
                        ActorKind.fromWire token
                        |> Option.map Ok
                        |> Option.defaultValue (Error(UnknownActorKind token))

                kind
                |> bind (fun kind ->
                    let orUnknown = Option.defaultValue Actor.UnknownValue
                    let provider = read "ROS_TELEMETRY_PROVIDER"
                    let runtime = read "ROS_TELEMETRY_RUNTIME"

                    let id =
                        match read "ROS_ACTOR", kind, provider, runtime with
                        | Some explicitId, _, _, _ -> explicitId
                        | None, ActorKind.Human, _, _ -> Actor.UnknownValue
                        | None, _, Some provider, Some runtime when provider <> Actor.UnknownValue && runtime <> Actor.UnknownValue ->
                            $"{provider}/{runtime}"
                        | _ -> Actor.UnknownValue

                    match kind with
                    | ActorKind.Human -> Actor.human id
                    | _ ->
                        Actor.create
                            kind
                            id
                            (Some(orUnknown provider))
                            (Some(orUnknown (read "ROS_TELEMETRY_MODEL")))
                            (Some(orUnknown runtime))
                            [])

        let execution =
            match read "ROS_EXECUTION_ID", ownRun with
            | Some propagated, _ ->
                ExecutionKey.create propagated
                |> bind (fun key ->
                    if ExecutionKey.isExecution key then Ok(Some key) else Error(InvalidExecutionKey propagated))
            | None, Some(system, run) -> ExecutionKey.foreign system run |> Result.map Some
            | None, None -> Ok None

        match actor, execution with
        | Ok actor, Ok execution -> create actor execution
        | Error error, _
        | _, Error error -> Error error
