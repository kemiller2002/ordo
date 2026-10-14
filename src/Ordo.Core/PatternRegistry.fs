/// The versioned registry of approved construction patterns,
/// `ordo.pattern-registry/1` (ORDO-PATTERN-001).
///
/// A pattern is a construction Ordo can perform deterministically because
/// its semantics are verified: a stable identity, a version, the typed
/// inputs and approved decisions it needs, the conditions it guarantees, the
/// generator that realizes it, the independent verification that checks it,
/// and what it refuses. Recognition (`PatternRecognition`) asks every
/// selected pattern to claim requirements from structured, approved facts
/// only. No requirement prose is interpreted and no model is called.
///
/// A pattern's claim function is code, so its contract digest cannot see a
/// change to it. The rule is therefore: any change to what a pattern claims
/// or generates is a new pattern version. A recognition pinned to the old
/// version keeps the old contract or fails; it never silently switches.
/// See docs/architecture/pattern-registry.md.
module Ordo.Core.PatternRegistry

open System
open System.Text
open System.Security.Cryptography
open Ordo.Core.Json
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionPlan
open Ordo.Core.ConstructionDecisions

[<Literal>]
let RegistrySchemaVersion = "ordo.pattern-registry/1"

// ---------------------------------------------------------------------------
// Identity.
// ---------------------------------------------------------------------------

/// Stable pattern identity. `Id` never changes meaning; `Version` changes
/// whenever what the pattern claims or generates changes.
type PatternRef = { Id: string; Version: int }

[<RequireQualifiedAccess>]
module PatternRef =
    let label (r: PatternRef) = sprintf "%s@%d" r.Id r.Version

/// The deterministic generator a pattern's construction runs through.
type GeneratorRef = { Id: string; Version: string }

// ---------------------------------------------------------------------------
// The facts recognition reads. Derived from the pinned manifest, the
// validated blueprint and approved decision records; nothing else.
// ---------------------------------------------------------------------------

/// What recognition knows about one requirement.
type RequirementFacts =
    { Key: string
      Disposition: Disposition
      /// Non-cohort nodes in the cohort's required scope that own the key.
      Owners: ConstructionNode list
      /// Recorded transitions citing the key, with the record's node id.
      Transitions: (string * RecordedTransition) list
      /// Recorded ports citing the key, with the record's node id.
      Ports: (string * RecordedPort) list }

/// What recognition knows about the cohort as a whole.
type CohortFacts =
    { CohortId: string
      ScopeNodes: Map<string, ConstructionNode>
      AuthorizedDecisionIds: Set<string>
      /// State models in approved records, with the record's node id.
      StateModels: (string * StateModelContent) list
      /// Conflicts settled by a `conflictResolution` record on an authorized
      /// decision in scope, with the deciding node ids. Only links that name a
      /// real conflict in scope sharing a requirement key are kept.
      ResolvedConflicts: Map<string, string list> }

// ---------------------------------------------------------------------------
// Claims.
// ---------------------------------------------------------------------------

/// A decision construction needs and does not have. Recognition never
/// supplies a default for one.
type DecisionGap =
    /// A transition effect's failure disposition is not decided.
    | EffectFailureUndecided of recordNodeId: string * command: string
    /// A decision node the requirement depends on is not externally approved.
    | DecisionNotAuthorized of decisionId: string
    /// More than one pattern would construct the requirement differently.
    | PatternSelectionRequired of patterns: PatternRef list
    /// A conflict node in scope states an open question about the requirement.
    | ConflictUnresolved of conflictId: string

[<RequireQualifiedAccess>]
type ClaimKind =
    /// The pattern's generator constructs the requirement's behavior.
    | Constructs
    /// The pattern generates only a typed interface (an extension surface);
    /// the behavior behind it is handwritten.
    | InterfaceOnly of surfaces: string list

type PatternClaim =
    { Pattern: PatternRef
      Kind: ClaimKind
      /// Approved node ids the claim rests on.
      Evidence: string list
      Gaps: DecisionGap list
      /// Why the matched content cannot be used safely. Any defect blocks.
      Defects: string list }

// ---------------------------------------------------------------------------
// Contracts and definitions.
// ---------------------------------------------------------------------------

/// The declarative contract of one pattern version. Its canonical form is
/// what a pin binds.
type PatternContract =
    { Ref: PatternRef
      Title: string
      /// Inventory family (research/pattern-construction/inventory.json).
      Family: string
      RequiredInputs: string list
      RequiredDecisions: string list
      Preconditions: string list
      Postconditions: string list
      Invariants: string list
      Generator: GeneratorRef
      /// Independent verification obligations, stated over the approved
      /// content, never over the generated code.
      Verification: string list
      /// Variations the pattern refuses to claim; they are delegated.
      Unsupported: string list }

type PatternDefinition =
    { Contract: PatternContract
      /// Pure and total. None when the pattern does not apply.
      Claim: CohortFacts -> RequirementFacts -> PatternClaim option }

type RegistryError =
    | BlankRegistryVersion
    | BlankPatternId of version: int
    | NonPositiveVersion of PatternRef
    | DuplicatePattern of PatternRef

type Registry =
    private
        { RegistryVersion: string
          Definitions: Map<PatternRef, PatternDefinition> }

    member this.Version = this.RegistryVersion
    member this.Contracts = this.Definitions |> Map.toList |> List.map (snd >> _.Contract)

/// What a recognition was bound to: the registry version and the exact
/// contract digest of every pattern it used.
type RegistryPin =
    { RegistryVersion: string
      Patterns: (PatternRef * string) list }

type PinError =
    | UnknownPatternVersion of PatternRef
    | PatternContractChanged of PatternRef * pinned: string * actual: string

// ---------------------------------------------------------------------------
// Canonical contract form and digests.
// ---------------------------------------------------------------------------

let private sha256 (text: string) =
    text |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
    |> fun hex -> "sha256:" + hex.ToLowerInvariant()

let private jStrings (values: string list) = JArray(values |> List.map JString)

let contractJson (c: PatternContract) =
    JObject [ "id", JString c.Ref.Id
              "version", JInt(int64 c.Ref.Version)
              "title", JString c.Title
              "family", JString c.Family
              "requiredInputs", jStrings c.RequiredInputs
              "requiredDecisions", jStrings c.RequiredDecisions
              "preconditions", jStrings c.Preconditions
              "postconditions", jStrings c.Postconditions
              "invariants", jStrings c.Invariants
              "generator", JObject [ "id", JString c.Generator.Id; "version", JString c.Generator.Version ]
              "verification", jStrings c.Verification
              "unsupported", jStrings c.Unsupported ]

let contractDigest (c: PatternContract) = contractJson c |> renderCanonical |> sha256

[<RequireQualifiedAccess>]
module Registry =

    /// Order of `definitions` carries no meaning.
    let create (version: string) (definitions: PatternDefinition list) : Result<Registry, RegistryError list> =
        let refs = definitions |> List.map _.Contract.Ref
        let errors =
            [ if String.IsNullOrWhiteSpace version then BlankRegistryVersion
              for r in refs do
                  if String.IsNullOrWhiteSpace r.Id then BlankPatternId r.Version
                  if r.Version < 1 then NonPositiveVersion r
              for r in refs |> List.countBy id |> List.filter (snd >> (<) 1) |> List.map fst do
                  DuplicatePattern r ]
        match errors with
        | [] ->
            Ok { RegistryVersion = version
                 Definitions = definitions |> List.map (fun d -> d.Contract.Ref, d) |> Map.ofList }
        | _ -> Error(errors |> List.sortBy (sprintf "%A"))

    /// Canonical registry document: every contract, sorted by identity.
    let encode (registry: Registry) =
        JObject [ "schemaVersion", JString RegistrySchemaVersion
                  "registryVersion", JString registry.RegistryVersion
                  "patterns", JArray(registry.Contracts |> List.sortBy (fun c -> c.Ref.Id, c.Ref.Version) |> List.map contractJson) ]
        |> renderCanonical

    let digest registry = encode registry |> sha256

    let private pinOf (registry: Registry) (definitions: PatternDefinition list) : RegistryPin =
        { RegistryVersion = registry.RegistryVersion
          Patterns = definitions |> List.map (fun d -> d.Contract.Ref, contractDigest d.Contract) |> List.sortBy fst }

    /// Without a pin, the latest version of every pattern id. With a pin,
    /// exactly the pinned versions with unchanged contracts, or an error.
    /// A pattern added to the registry after a pin was approved is not
    /// selected under that pin.
    let select (pin: RegistryPin option) (registry: Registry) : Result<PatternDefinition list * RegistryPin, PinError list> =
        match pin with
        | None ->
            let latest =
                registry.Definitions
                |> Map.toList
                |> List.map snd
                |> List.groupBy _.Contract.Ref.Id
                |> List.map (snd >> List.maxBy _.Contract.Ref.Version)
                |> List.sortBy _.Contract.Ref
            Ok(latest, pinOf registry latest)
        | Some pinned ->
            let resolved =
                pinned.Patterns
                |> List.map (fun (r, digest) ->
                    match Map.tryFind r registry.Definitions with
                    | None -> Error(UnknownPatternVersion r)
                    | Some d ->
                        let actual = contractDigest d.Contract
                        if actual = digest then Ok d else Error(PatternContractChanged(r, digest, actual)))
            match resolved |> List.choose (function Error e -> Some e | Ok _ -> None) with
            | [] ->
                let definitions = resolved |> List.choose (function Ok d -> Some d | Error _ -> None) |> List.sortBy _.Contract.Ref
                Ok(definitions, { pinned with Patterns = pinned.Patterns |> List.sortBy fst })
            | errors -> Error errors

// ---------------------------------------------------------------------------
// The initial pattern library. Each wraps the existing deterministic
// generator `ordo.default-plan/3`; nothing here generates code itself.
// ---------------------------------------------------------------------------

[<RequireQualifiedAccess>]
module BuiltIn =

    let DefaultPlanGenerator = { GeneratorRef.Id = "ordo.default-plan"; Version = "ordo.default-plan/3" }

    let private nameDefect (raw: string) =
        match SemanticName.create raw with
        | Ok _ -> None
        | Error e -> Some(sprintf "name '%s' is not a construction name: %A" raw e)

    let private key raw = match SemanticName.create raw with Ok n -> n.Key | Error _ -> raw

    let private workflowName (cohort: CohortFacts) =
        match cohort.StateModels with
        | [ _, model ] ->
            match SemanticName.create model.Workflow with
            | Ok name -> Ok name
            | Error e -> Error(sprintf "workflow name '%s' is not a construction name: %A" model.Workflow e)
        | [] -> Error "no approved state model to attach the port to"
        | many -> Error(sprintf "%d state models in one cohort (%s)" many.Length (many |> List.map fst |> String.concat ", "))

    /// Structural soundness of a recorded state model: the subset of the
    /// default-plan checks recognition needs to avoid claiming content the
    /// generator would refuse. The generator still checks everything.
    let stateModelDefects (model: StateModelContent) : string list =
        let states = model.States |> List.map (fun s -> key s.Name)
        let initials = model.States |> List.filter (fun s -> s.Role = Initial)
        [ yield! nameDefect model.Workflow |> Option.toList
          for s in model.States do yield! nameDefect s.Name |> Option.toList
          for t in model.Transitions do yield! nameDefect t.Command |> Option.toList
          for d in states |> List.countBy id |> List.filter (snd >> (<) 1) do
              yield sprintf "state %s is recorded more than once" (fst d)
          match initials with
          | [ _ ] -> ()
          | [] -> yield "no initial state"
          | many -> yield sprintf "%d initial states" many.Length
          for t in model.Transitions do
              for s in [ t.From; t.To ] do
                  if not (List.contains (key s) states) then
                      yield sprintf "transition %s/%s names unknown state %s" t.From t.Command s
          for (from, command), targets in
              model.Transitions
              |> List.groupBy (fun t -> key t.From, key t.Command)
              |> List.map (fun (k, ts) -> k, ts |> List.map (fun t -> key t.To) |> List.distinct)
              |> List.filter (snd >> List.length >> (<) 1) do
              yield sprintf "transition %s/%s has more than one target (%s)" from command (String.concat ", " targets) ]
        |> List.distinct

    let private authorization (cohort: CohortFacts) nodeId =
        match Map.tryFind nodeId cohort.ScopeNodes with
        | Some n when n.Kind = Decision && not (Set.contains nodeId cohort.AuthorizedDecisionIds) -> [ DecisionNotAuthorized nodeId ]
        | _ -> []

    let stateMachineContract =
        { Ref = { Id = "ordo.state-machine"; Version = 1 }
          Title = "Typed state machine with deterministic transitions"
          Family = "state.machine-guarded-transition"
          RequiredInputs = [ "construction decision record of kind stateModel on an approved decision node" ]
          RequiredDecisions = [ "the decision node that fixes the states, roles and transitions" ]
          Preconditions =
            [ "exactly one state model in the cohort"
              "exactly one initial state; state names unique"
              "every transition names recorded states"
              "(state, command) determines at most one target" ]
          Postconditions =
            [ "generated Model, Transition and construction-manifest files under generated/<Workflow>/"
              "a command not recorded for the current state is refused with a typed result" ]
          Invariants = [ "the state value is always one of the recorded states"; "terminal states have no outgoing transition" ]
          Generator = DefaultPlanGenerator
          Verification =
            [ "every recorded (state, command) pair yields exactly the recorded target"
              "every unrecorded (state, command) pair is refused and leaves the state unchanged" ]
          Unsupported =
            [ "guards with domain conditions (delegated)"
              "conditional or multi-target transitions"
              "hierarchical or parallel states" ] }

    let stateMachine: PatternDefinition =
        { Contract = stateMachineContract
          Claim =
            fun cohort facts ->
                match facts.Transitions with
                | [] -> None
                | cited ->
                    let nodes = cited |> List.map fst |> List.distinct |> List.sort
                    let kindDefects =
                        nodes
                        |> List.choose (fun id ->
                            match Map.tryFind id cohort.ScopeNodes with
                            | Some n when n.Kind = Decision -> None
                            | Some n -> Some(sprintf "stateModel record on %s node %s" (string n.Kind) id)
                            | None -> Some(sprintf "stateModel record on node %s outside the cohort's required scope" id))
                    let modelDefects =
                        match cohort.StateModels with
                        | [ _, model ] -> stateModelDefects model
                        | models -> [ sprintf "%d state models in one cohort" models.Length ]
                    Some
                        { Pattern = stateMachineContract.Ref
                          Kind = ClaimKind.Constructs
                          Evidence = nodes
                          Gaps = nodes |> List.collect (authorization cohort)
                          Defects = kindDefects @ modelDefects } }

    let transitionEffectContract =
        { Ref = { Id = "ordo.transition-effect-port"; Version = 1 }
          Title = "Typed effect port on a transition with a decided failure disposition"
          Family = "integration.external-adapter"
          RequiredInputs = [ "portContract record with a transitionEffect port"; "the state model whose command it names" ]
          RequiredDecisions = [ "what a failed or unknown effect outcome means: remain or proceed" ]
          Preconditions = [ "the command is a recorded transition"; "a decided disposition is recorded on an approved decision node" ]
          Postconditions = [ "a generated port interface member Perform/<Command>"; "orchestration applies the decided disposition" ]
          Invariants = [ "the transition commits only as the decided disposition allows" ]
          Generator = DefaultPlanGenerator
          Verification = [ "an effect failure leaves the workflow in the state the decided disposition names" ]
          Unsupported = [ "the effect's own behavior (handwritten behind the port)"; "retry, timeout or reconciliation policies" ] }

    let transitionEffect: PatternDefinition =
        { Contract = transitionEffectContract
          Claim =
            fun cohort facts ->
                let effects =
                    facts.Ports
                    |> List.choose (fun (nodeId, port) ->
                        match port with
                        | RecordedTransitionEffect(command, disposition, _, _) -> Some(nodeId, command, disposition)
                        | RecordedCurrentTime _ -> None)
                match effects with
                | [] -> None
                | _ ->
                    let workflow = workflowName cohort
                    let commands =
                        cohort.StateModels |> List.collect (snd >> _.Transitions) |> List.map (fun t -> key t.Command) |> Set.ofList
                    let surfaces, surfaceDefects =
                        effects
                        |> List.map (fun (_, command, _) ->
                            match workflow, SemanticName.create command with
                            | Ok w, Ok c -> Ok(ConstructionLayout.effectContract w c)
                            | Error e, _ -> Error e
                            | _, Error e -> Error(sprintf "effect command '%s' is not a construction name: %A" command e))
                        |> List.fold (fun (ok, bad) r -> match r with Ok s -> s :: ok, bad | Error e -> ok, e :: bad) ([], [])
                    let defects =
                        [ yield! surfaceDefects
                          for nodeId, command, disposition in effects do
                              if not (Set.contains (key command) commands) then
                                  yield sprintf "effect %s on %s names no recorded transition" command nodeId
                              match disposition, Map.tryFind nodeId cohort.ScopeNodes with
                              | Some(EffectFailureDisposition.Remain | EffectFailureDisposition.Proceed), Some n when n.Kind <> Decision ->
                                  yield sprintf "decided disposition for %s recorded on %s node %s, not a decision" command (string n.Kind) nodeId
                              | _ -> () ]
                    let gaps =
                        [ for nodeId, command, disposition in effects do
                              match disposition with
                              | None
                              | Some EffectFailureDisposition.Unresolved -> yield EffectFailureUndecided(nodeId, command)
                              | Some _ -> yield! authorization cohort nodeId ]
                    Some
                        { Pattern = transitionEffectContract.Ref
                          Kind = ClaimKind.InterfaceOnly(surfaces |> List.distinct |> List.sort)
                          Evidence = effects |> List.map (fun (n, _, _) -> n) |> List.distinct |> List.sort
                          Gaps = gaps |> List.distinct
                          Defects = defects |> List.distinct } }

    let currentTimeContract =
        { Ref = { Id = "ordo.current-time-port"; Version = 1 }
          Title = "Injected current-time port"
          Family = "integration.external-adapter"
          RequiredInputs = [ "portContract record with a currentTime port"; "the state model it belongs to" ]
          RequiredDecisions = []
          Preconditions = [ "exactly one state model in the cohort" ]
          Postconditions = [ "a generated CurrentTime port member; no ambient clock in generated code" ]
          Invariants = [ "generated code reads time only through the port" ]
          Generator = DefaultPlanGenerator
          Verification = [ "generated code is deterministic under a fixed clock" ]
          Unsupported = [ "time arithmetic, rounding, time zones and calendars (handwritten)" ] }

    let currentTime: PatternDefinition =
        { Contract = currentTimeContract
          Claim =
            fun cohort facts ->
                let nodes =
                    facts.Ports
                    |> List.choose (fun (nodeId, port) -> match port with RecordedCurrentTime _ -> Some nodeId | _ -> None)
                    |> List.distinct |> List.sort
                match nodes with
                | [] -> None
                | _ ->
                    let surface, defects =
                        match workflowName cohort with
                        | Ok w -> [ ConstructionLayout.currentTimeContract w ], []
                        | Error e -> [], [ e ]
                    Some
                        { Pattern = currentTimeContract.Ref
                          Kind = ClaimKind.InterfaceOnly surface
                          Evidence = nodes
                          Gaps = []
                          Defects = defects } }

    [<Literal>]
    let RegistryVersion = "ordo.patterns/1"

    let definitions = [ stateMachine; transitionEffect; currentTime ]

    let registry =
        match Registry.create RegistryVersion definitions with
        | Ok r -> r
        | Error e -> invalidOp (sprintf "built-in registry is invalid: %A" e)
