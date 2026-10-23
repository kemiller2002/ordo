/// The construction trace (`echelon.construction-trace/0.1`): every
/// rule-bearing construct, generated or handwritten, with its disposition and
/// the handoff records it was built from. Deterministic: stable ordering, LF
/// line endings, no timestamps, no machine-dependent values.
module Echelon.Construct.Trace

open System
open System.Security.Cryptography
open System.Text
open Ordo.Core.Json
open Ordo.Core.ConstructionPlan
open Echelon.Construct.Handoff
open Echelon.Construct.Admission
open Echelon.Construct.Translate
open Echelon.Construct.Handwritten

[<Literal>]
let SchemaName = "echelon.construction-trace"

[<Literal>]
let SchemaVersion = "0.1"

let Path = "out/construction-trace.json"

let sha256Hex (bytes: byte array) = "sha256:" + (SHA256.HashData bytes |> Convert.ToHexString).ToLowerInvariant()

let sha256Text (text: string) = Encoding.UTF8.GetBytes text |> sha256Hex

[<RequireQualifiedAccess>]
type AuthorityKind =
    | Requirement
    | Decision
    | Question
    | Invariant

type AuthorityRef = { Kind: AuthorityKind; Id: string }

type TraceConstruct =
    { Symbol: string
      Kind: ConstructKind
      File: string
      Disposition: Disposition
      Authority: AuthorityRef list }

type TraceError =
    /// A construct cites an ID that is not a current authority record in the handoff.
    | UnknownAuthority of symbol: string * id: string
    /// A construct cites a superseded, proposed or rejected record as its authority.
    | NotCurrentAuthority of symbol: string * id: string
    /// A construct that rests on an unresolved question must be marked deferred, and only such a construct may be.
    | DispositionMismatch of symbol: string * disposition: Disposition

/// Resolves an ID to its kind in the admitted handoff, refusing anything that
/// is not current authority. Observations are never authority.
let private resolve (h: Handoff) (symbol: string) (id: string) : Result<AuthorityRef, TraceError> =
    match h.Requirements |> List.tryFind (fun r -> r.Id = id) with
    | Some r when r.Status = RequirementStatus.Accepted -> Ok { Kind = AuthorityKind.Requirement; Id = id }
    | Some _ -> Error(NotCurrentAuthority(symbol, id))
    | None ->
        match h.Decisions |> List.tryFind (fun d -> d.Id = id) with
        | Some d when d.Status = DecisionStatus.Accepted -> Ok { Kind = AuthorityKind.Decision; Id = id }
        | Some _ -> Error(NotCurrentAuthority(symbol, id))
        | None ->
            if h.PolicyQuestions |> List.exists (fun q -> q.Id = id) then Ok { Kind = AuthorityKind.Question; Id = id }
            elif h.Invariants |> List.exists (fun i -> i.Id = id) then Ok { Kind = AuthorityKind.Invariant; Id = id }
            else Error(UnknownAuthority(symbol, id))

let private kindOrder =
    function
    | AuthorityKind.Requirement -> 0
    | AuthorityKind.Decision -> 1
    | AuthorityKind.Question -> 2
    | AuthorityKind.Invariant -> 3

let private toTrace (h: Handoff) (c: Construct) : Result<TraceConstruct, TraceError list> =
    let resolved = c.Authority |> List.distinct |> List.map (resolve h c.Symbol)
    let errors = resolved |> List.choose (function Error e -> Some e | Ok _ -> None)
    let refs =
        resolved
        |> List.choose (function Ok r -> Some r | Error _ -> None)
        |> List.sortBy (fun r -> kindOrder r.Kind, r.Id)
    let restsOnQuestion = refs |> List.exists (fun r -> r.Kind = AuthorityKind.Question)
    let dispositionErrors =
        match c.Disposition with
        | Disposition.DeferredPendingQuestion when not restsOnQuestion -> [ DispositionMismatch(c.Symbol, c.Disposition) ]
        | _ -> []
    match errors @ dispositionErrors with
    | [] -> Ok { Symbol = c.Symbol; Kind = c.Kind; File = c.File; Disposition = c.Disposition; Authority = refs }
    | e -> Error e

// ---------------------------------------------------------------------------
// Constructs Ordo's emitter generated, read back from the validated plan.
// ---------------------------------------------------------------------------

/// Maps an ECIR trace back to handoff IDs: requirement keys to their original
/// IDs; decision and deferral nodes to decision and question IDs. Contract,
/// cohort and verification nodes are containers and are dropped.
let private idsOf (h: Handoff) (ir: Ecir) (trace: Trace) =
    let fromKeys =
        trace.RequirementKeys
        |> List.choose (fun key -> ir.Manifest.Requirements |> List.tryFind (fun r -> r.Key = key) |> Option.map _.OriginalId)
    let fromNodes =
        trace.NodeIds
        |> List.filter (fun id -> h.Decisions |> List.exists (fun d -> d.Id = id) || h.PolicyQuestions |> List.exists (fun q -> q.Id = id))
    fromKeys @ fromNodes

let generatedFiles (workflow: string) =
    {| Model = sprintf "src/Invoice.Slice/generated/%s/%s.Model.g.fs" workflow workflow
       Transition = sprintf "src/Invoice.Slice/generated/%s/%s.Transition.g.fs" workflow workflow |}

let generatedConstructs (admitted: AdmittedHandoff) (ir: Ecir) (validated: ValidatedPlan) : Construct list =
    let h = handoffOf admitted
    let w = (view validated).Workflow
    let wf = w.Name.Key
    let ns = "Generated." + wf
    let files = generatedFiles wf
    let ids = idsOf h ir
    let disposition (authority: string list) =
        if authority |> List.exists (fun id -> h.PolicyQuestions |> List.exists (fun q -> q.Id = id))
        then Disposition.DeferredPendingQuestion
        else Disposition.Authorized
    let make symbol kind file authority : Construct =
        { Symbol = symbol; Kind = kind; File = file; Disposition = disposition authority; Authority = authority }
    let pairTraces =
        (w.Transitions |> List.map (fun t -> t.Command.Key, t.Trace))
        @ (w.BranchingTransitions |> List.map (fun b -> b.OnCommand.Key, b.Trace))
    let states =
        w.States |> List.map (fun s -> make (sprintf "%s.%sState.%s" ns wf s.Name.Key) ConstructKind.State files.Model (ids s.Trace))
    let commands =
        w.Commands
        |> List.map (fun c ->
            let authority = pairTraces |> List.filter (fst >> (=) c.Key) |> List.collect (snd >> ids)
            make (sprintf "%s.%sCommand.%s" ns wf c.Key) ConstructKind.Command files.Model authority)
    let transitions =
        w.Transitions
        |> List.map (fun t ->
            make (sprintf "%s.%sTransition.legal:%s/%s" ns wf t.From.Key t.Command.Key) ConstructKind.Transition files.Transition (ids t.Trace))
    let branches =
        w.BranchingTransitions
        |> List.map (fun b ->
            make (sprintf "%s.%sBranch_%s_%s" ns wf b.InState.Key b.OnCommand.Key) ConstructKind.Transition files.Model (ids b.Trace))
    let codes =
        w.RefusalCodes
        |> List.map (fun (code, trace) -> make (sprintf "%s.%sRefusalCode.%s" ns wf code.Key) ConstructKind.Refusal files.Model (ids trace))
    let assignments =
        w.CodeAssignments
        |> List.map (fun a ->
            let subject =
                match a.Target with
                | CodeSubject.Pair(f, c) -> sprintf "%s/%s" f.Key c.Key
                | CodeSubject.Unlisted -> "unlisted"
                | CodeSubject.Guard g -> "guard/" + g.Key
            make (sprintf "%s.%sTransition.pairCode:%s" ns wf subject) ConstructKind.Refusal files.Transition (ids a.Trace))
    let pairRule =
        w.PairRule
        |> Option.toList
        |> List.map (fun (_, trace) -> make (sprintf "%s.%sTransition.pairRule:unlisted" ns wf) ConstructKind.Refusal files.Transition (ids trace))
    let undecided =
        w.UndecidedTransitions
        |> List.map (fun u ->
            make (sprintf "%s.%sTransition.undecided:%s/%s" ns wf u.InState.Key u.OnCommand.Key) ConstructKind.Refusal files.Transition (ids u.Trace))
    // VAC-002-P3-03-R2: `decide` reuses every transition, refusal and open
    // pair of the plan, so it carries all of their authority.
    let decideAuthority =
        (transitions @ branches @ assignments @ pairRule @ undecided) |> List.collect _.Authority |> List.distinct
    let decide : Construct =
        { Symbol = sprintf "%s.%sTransition.decide" ns wf; Kind = ConstructKind.Transition; File = files.Transition
          Disposition = Disposition.Authorized; Authority = decideAuthority }
    states @ commands @ transitions @ branches @ codes @ assignments @ pairRule @ undecided @ [ decide ]

// ---------------------------------------------------------------------------
// Excluded observations.
// ---------------------------------------------------------------------------

type Excluded =
    { ObservationId: string
      Reason: string
      RoutedTo: string option }

/// Every observation is excluded: observations carry no authority. Each is
/// routed to the owner of the accepted decision that governs the rule it
/// concerns, found through `concerns` (the slice table), or left unrouted.
let excluded (admitted: AdmittedHandoff) : Excluded list =
    let h = handoffOf admitted
    let limitOwner = h.Decisions |> List.tryFind (fun d -> d.Id = LimitDecision) |> Option.map _.Owner
    let superseded = h.Decisions |> List.filter (fun d -> d.Status = DecisionStatus.Superseded)
    h.Observations
    |> List.sortBy _.Id
    |> List.map (fun o ->
        let reason =
            match o.Kind with
            | ObservationKind.Exemption ->
                sprintf "Observed exemption (%s) has authority 'unverified': no requirement or accepted decision supports it, and %s states no customer category is exempt. Not constructed; no customer-category parameter exists in the constructed model. The legacy code is left unchanged for its owner." (o.Location.Symbol |> Option.defaultValue o.Location.Path) LimitDecision
            | ObservationKind.SupersededRule ->
                sprintf "Observed rule (%s) carries the value of a superseded decision (%s, superseded by %s). Not constructed; the limit is taken only from %s parameters. The legacy code is left unchanged for its owner." (o.Location.Symbol |> Option.defaultValue o.Location.Path) (superseded |> List.map _.Id |> String.concat ", ") LimitDecision LimitDecision
            | _ ->
                sprintf "Observed behaviour (%s) has authority 'unverified'. Not constructed. The existing code is left unchanged for its owner." (o.Location.Symbol |> Option.defaultValue o.Location.Path)
        { ObservationId = o.Id; Reason = reason; RoutedTo = limitOwner })

// ---------------------------------------------------------------------------
// Rendering.
// ---------------------------------------------------------------------------

let private kindName =
    function
    | ConstructKind.State -> "state"
    | ConstructKind.Command -> "command"
    | ConstructKind.Transition -> "transition"
    | ConstructKind.Refusal -> "refusal"
    | ConstructKind.ValidatedConstructor -> "validated-constructor"
    | ConstructKind.ValueType -> "value-type"
    | ConstructKind.Constant -> "constant"
    | ConstructKind.Port -> "port"
    | ConstructKind.Capability -> "capability"

let dispositionName =
    function
    | Disposition.Authorized -> "authorized"
    | Disposition.DeferredPendingQuestion -> "deferred-pending-question"
    | Disposition.Technical -> "technical"

let private authorityKindName =
    function
    | AuthorityKind.Requirement -> "requirement"
    | AuthorityKind.Decision -> "decision"
    | AuthorityKind.Question -> "question"
    | AuthorityKind.Invariant -> "invariant"

let build (admitted: AdmittedHandoff) (constructs: Construct list) : Result<TraceConstruct list, TraceError list> =
    let h = handoffOf admitted
    let results = constructs |> List.map (toTrace h)
    match results |> List.collect (function Error e -> e | Ok _ -> []) with
    | [] ->
        results
        |> List.choose (function Ok c -> Some c | Error _ -> None)
        |> List.sortWith (fun a b ->
            match String.CompareOrdinal(a.File, b.File) with
            | 0 -> String.CompareOrdinal(a.Symbol, b.Symbol)
            | n -> n)
        |> Ok
    | errors -> Error errors

let renderTrace (handoffId: string) (handoffDigest: string) (constructs: TraceConstruct list) (excludedItems: Excluded list) (outputs: (string * string) list) : string =
    let construct (c: TraceConstruct) =
        JObject
            [ "symbol", JString c.Symbol
              "kind", JString(kindName c.Kind)
              "file", JString c.File
              "disposition", JString(dispositionName c.Disposition)
              "authority", JArray(c.Authority |> List.map (fun a -> JObject [ "kind", JString(authorityKindName a.Kind); "id", JString a.Id ])) ]
    let exclusion (e: Excluded) =
        JObject(
            [ "observationId", JString e.ObservationId; "reason", JString e.Reason ]
            @ (e.RoutedTo |> Option.map (fun r -> [ "routedTo", JString r ]) |> Option.defaultValue []))
    let document =
        JObject
            [ "schema", JString SchemaName
              "schemaVersion", JString SchemaVersion
              "handoff", JObject [ "handoffId", JString handoffId; "sha256", JString handoffDigest ]
              "generator",
              JObject [ "name", JString AuthorityEmit.GeneratorName
                        "version", JString(AuthorityEmit.GeneratorVersion + "+" + Ordo.Construction.Artifacts.GeneratorVersion4) ]
              "constructs", JArray(constructs |> List.map construct)
              "excluded", JArray(excludedItems |> List.map exclusion)
              "outputs",
              JArray(
                  outputs
                  |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
                  |> List.map (fun (path, digest) -> JObject [ "path", JString path; "sha256", JString digest ])) ]
    render document + "\n"
