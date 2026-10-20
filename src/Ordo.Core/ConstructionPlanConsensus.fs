/// Deterministic consensus over independently produced construction plans
/// for one prepared cohort. See docs/architecture/construction-plan-consensus.md
/// and DF-SDE-2026-0021.
///
/// The consensus keeps only what every draft proposed. Anything proposed by
/// some drafts but not all is not chosen: it becomes an explicit
/// `declareUnresolved` disagreement record for a person to decide. There is
/// no majority vote. Two agreeing agents can share a bias, so one dissenting
/// draft is enough to keep a construct out of the plan. Every unresolved
/// question any draft raised is kept.
///
/// The result is a `ValidatedPlan` obtained from `ConstructionPlan.validate`
/// and nothing more. A consensus, even one without disagreements, is not
/// authority, approval or verification.
module Ordo.Core.ConstructionPlanConsensus

open System
open System.Security.Cryptography
open System.Text
open Ordo.Core.ConstructionIr
open Ordo.Core.ConstructionPlan

// ---------------------------------------------------------------------------
// Report.
// ---------------------------------------------------------------------------

/// A draft's identity in the consensus. Drafts are numbered from 1 in
/// canonical order, that is sorted by their own validated plan digest, so
/// numbering, prose and digest do not depend on input order. A caller maps
/// an input draft to its number by its digest.
type DraftIdentity = { Index: int; Digest: string }

/// One distinct proposal for a disputed construct, and the drafts that made it.
type Proposal = { Variant: string; Drafts: int list }

/// What the drafts disagreed on.
type DisagreementKind =
    /// A structural operation that not every draft proposed identically. It is
    /// not in the plan.
    | Structural
    /// Vocabulary 2: every draft declared the effect port but not the same
    /// `onFailure`. The port is kept with `onFailure: unresolved`, linked to
    /// the disagreement record.
    | EffectDisposition
    /// Vocabulary 2: every draft left the effect `unresolved`, but linked it
    /// to differently worded questions. All of them are kept, unlinked; the
    /// disagreement record carries the single link.
    | EffectQuestion

type Disagreement =
    { Kind: DisagreementKind
      /// The disputed construct's derived label, e.g. `transition:Running/Stop`.
      Label: string
      /// The `declareUnresolved` subject this disagreement became.
      Subject: string
      /// Each distinct proposal with its drafts, in canonical order.
      Proposals: Proposal list
      /// Drafts that proposed anything for this label.
      Proposing: int list
      /// Drafts that proposed nothing for this label.
      NotProposing: int list }

/// An unresolved question some draft raised, as kept in the plan.
type UnresolvedQuestion =
    { /// The subject in the consensus plan.
      Subject: string
      /// The subject as the drafts wrote it (normalized). It differs from
      /// `Subject` only when differently worded questions shared a subject.
      RaisedSubject: string
      /// The effect this question is linked to in the consensus plan.
      Effect: string option
      Question: string
      RaisedBy: int list }

type private ConsensusReport =
    { Plan: ValidatedPlan
      Digest: string
      Drafts: DraftIdentity list
      Agreed: string list
      Disagreements: Disagreement list
      Unresolved: UnresolvedQuestion list }

/// The outcome of `combine`. Only `combine` constructs one, so its report
/// cannot be fabricated around a plan; like the plan, it is not authority.
type ConsensusResult =
    private
    | ConsensusResult of ConsensusReport

    member private this.Report = let (ConsensusResult report) = this in report
    /// The combined plan. It passed `ConstructionPlan.validate` against the
    /// same prepared cohort as every draft; it is not authority.
    member this.Plan = this.Report.Plan
    member this.Digest = this.Report.Digest
    /// Every draft, numbered in canonical (digest) order.
    member this.Drafts = this.Report.Drafts
    /// Labels of the structural operations every draft proposed identically.
    member this.Agreed = this.Report.Agreed
    member this.Disagreements = this.Report.Disagreements
    /// Every question any draft raised, with the drafts that raised it.
    member this.Unresolved = this.Report.Unresolved

type ConsensusRefusal =
    /// Consensus needs at least two independent drafts.
    | TooFewDrafts of count: int
    /// The draft at this 1-based input position does not validate on its own.
    /// It is never dropped silently.
    | InvalidDraft of position: int * violations: PlanViolation list
    | MixedSchemaVersions of versions: string list
    | MixedVocabularyVersions of versions: string list
    /// The combined plan does not validate, for example because a dropped
    /// structural operation leaves a state unreachable or unknown. Nothing is
    /// patched: the disagreements go to a person.
    | CombinedPlanInvalid of violations: PlanViolation list * disagreements: Disagreement list

// ---------------------------------------------------------------------------
// Canonical operations.
// ---------------------------------------------------------------------------

let private noTrace: TraceDraft = { RequirementKeys = []; NodeIds = [] }

let private unionTraces (traces: TraceDraft list) : TraceDraft =
    { RequirementKeys = traces |> List.collect _.RequirementKeys |> List.distinct |> List.sort
      NodeIds = traces |> List.collect _.NodeIds |> List.distinct |> List.sort }

let private withTrace (trace: TraceDraft) operation =
    match operation with
    | DefineWorkflow w -> DefineWorkflow { w with Trace = trace }
    | DefineState s -> DefineState { s with Trace = trace }
    | DefineTransition t -> DefineTransition { t with Trace = trace }
    | DefineInvariant i -> DefineInvariant { i with Trace = trace }
    | DeclarePort p -> DeclarePort { p with Trace = trace }
    | BindCapability c -> BindCapability { c with Trace = trace }
    | DeclareUnresolved u -> DeclareUnresolved { u with Trace = trace }
    | DeclarePairRule r -> DeclarePairRule { r with Trace = trace }
    | DefineRefusal r -> DefineRefusal { r with Trace = trace }
    | DeclareUndecidedTransition u -> DeclareUndecidedTransition { u with Trace = trace }
    // Only the operation's own trace: decision-point traces are part of an
    // inventory's identity, so drafts must agree on what justifies each point.
    | DeclareExtensionInventory i -> DeclareExtensionInventory { i with Trace = trace }
    | DefineRefusalCode c -> DefineRefusalCode { c with Trace = trace }
    | AssignRefusalCode a -> AssignRefusalCode { a with Trace = trace }
    | DefineBranchingTransition b -> DefineBranchingTransition { b with Trace = trace }
    | DeclareEffectPolicy p -> DeclareEffectPolicy { p with Trace = trace }

/// Identity ignores trace. Operations are already canonical (normalized
/// names, guards and scopes sorted and distinct), so equality compares
/// guards as sets.
let private identity operation = withTrace noTrace operation

/// An effect port without its disposition, to tell a disposition-only
/// disagreement apart from a structural one.
let private withoutDisposition operation =
    match operation with
    | DeclarePort({ Kind = TransitionEffect(command, _) } as p) -> DeclarePort { p with Kind = TransitionEffect(command, None); Trace = noTrace }
    | other -> identity other

let private roleText role =
    match role with
    | Initial -> "initial"
    | Ordinary -> "ordinary"
    | Terminal -> "terminal"

let private commaList (items: string list) = String.Join(", ", items)

/// A deterministic, prose rendering of a canonical operation without its
/// trace. It names domain words only; it is never rendered into code.
let private describe operation =
    match operation with
    | DefineWorkflow w -> "defineWorkflow " + w.Name
    | DefineState s -> sprintf "defineState %s %s" s.Name (roleText s.Role)
    | DefineTransition t ->
        let guards = t.Guards |> List.map _.Name
        sprintf "defineTransition %s from %s to %s%s" t.Command t.From t.To
            (if List.isEmpty guards then "" else " guarded by " + commaList guards)
    | DefineInvariant i -> sprintf "defineInvariant %s holding in %s" i.Name (commaList i.HoldsIn)
    | DeclarePort { Kind = CurrentTime } -> "declarePort currentTime"
    | DeclarePort { Kind = TransitionEffect(command, disposition) } ->
        "declarePort transitionEffect " + command
        + (disposition |> Option.map (fun d -> " onFailure " + effectFailureDispositionName d) |> Option.defaultValue "")
    | BindCapability c -> sprintf "bindCapability %s %s" c.Command c.Capability
    | DeclareUnresolved u -> "declareUnresolved " + u.Subject
    | DeclarePairRule r ->
        "declarePairRule unlisted " + (match r.Unlisted with UnlistedPairs.Refused -> "refused" | UnlistedPairs.Undecided -> "undecided")
    | DefineRefusal r -> sprintf "defineRefusal %s from %s" r.Pair.OnCommand r.Pair.InState
    | DeclareUndecidedTransition u -> sprintf "declareUndecidedTransition %s from %s" u.Pair.OnCommand u.Pair.InState
    | DeclareExtensionInventory i ->
        let extension =
            match i.Extension with
            | ExtensionRef.Guard n -> "guard " + n
            | ExtensionRef.Invariant n -> "invariant " + n
            | ExtensionRef.Effect n -> "effect " + n
            | ExtensionRef.Selection(f, c) -> sprintf "selection %s from %s" c f
        let points =
            i.DecisionPoints
            |> List.map (fun p -> p.Point + (match p.Resolution with DecisionPointResolution.Decided -> " decided" | DecisionPointResolution.Undecided _ -> " undecided"))
        sprintf "declareExtensionInventory %s%s" extension (if List.isEmpty points then " with no decision points" else " with " + commaList points)
    | DefineRefusalCode c -> "defineRefusalCode " + c.Code
    | AssignRefusalCode a ->
        let target =
            match a.Target with
            | CodeTarget.Pair p -> sprintf "%s from %s" p.OnCommand p.InState
            | CodeTarget.Unlisted -> "unlisted pairs"
            | CodeTarget.Guard g -> "guard " + g
        sprintf "assignRefusalCode %s to %s" a.Code target
    | DefineBranchingTransition b -> sprintf "defineBranchingTransition %s from %s to one of %s" b.Pair.OnCommand b.Pair.InState (commaList b.Targets)
    | DeclareEffectPolicy p ->
        sprintf "declareEffectPolicy %s retries %d onFailed %s onUnknown %s" p.Effect p.Retries (failedActionName p.OnFailed) (unknownActionName p.OnUnknown)

let private dispositionText operation =
    match operation with
    | DeclarePort { Kind = TransitionEffect(_, Some d) } -> "onFailure " + effectFailureDispositionName d
    | DeclarePort { Kind = TransitionEffect(_, None) } -> "no onFailure"
    | other -> describe other

// ---------------------------------------------------------------------------
// Disagreement records.
// ---------------------------------------------------------------------------

let private draftsText (drafts: int list) =
    match drafts |> List.map string with
    | [] -> "no draft"
    | [ one ] -> "draft " + one
    | many -> sprintf "drafts %s and %s" (commaList (List.take (many.Length - 1) many)) (List.last many)

let private sha256Hex (value: string) =
    value |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()

/// A semantic-name subject for a disagreement label: `Disagreement` and the
/// label's ASCII words. A label too long for a name is shortened to its kind
/// and a content hash, so the subject stays deterministic and distinct.
let private subjectFor (label: string) =
    let words (text: string) =
        text.Split([| ':'; '/'; '.'; '-'; '_'; ' ' |], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
    let full = String.Join(" ", "Disagreement" :: words label)
    if full.Length <= MaxNameLength then full
    else
        let kind = label.Split(':').[0] |> words
        String.Join(" ", ("Disagreement" :: kind) @ [ "H" + (sha256Hex label).Substring(0, 16) ])

let private nameKey raw =
    SemanticName.create raw |> Result.map SemanticName.key |> Result.defaultValue raw

let private question (label: string) (proposals: Proposal list) (notProposing: int list) =
    let proposed = proposals |> List.map (fun p -> sprintf "%s proposed %s." (draftsText p.Drafts) p.Variant)
    let absent = if List.isEmpty notProposing then [] else [ sprintf "%s proposed nothing for %s." (draftsText notProposing) label ]
    let capitalized (sentence: string) = string (Char.ToUpperInvariant sentence[0]) + sentence.Substring 1
    let sentences = proposed @ absent @ [ "Consensus does not choose between independent drafts; a person must decide." ]
    String.Join(" ", sentences |> List.map capitalized)

let private record kind label (proposals: Proposal list) (all: int list) effect trace =
    let proposing = proposals |> List.collect _.Drafts |> List.distinct |> List.sort
    let notProposing = all |> List.except proposing
    let subject = subjectFor label
    let disagreement =
        { Disagreement.Kind = kind
          Label = label
          Subject = nameKey subject
          Proposals = proposals
          Proposing = proposing
          NotProposing = notProposing }
    let operation =
        DeclareUnresolved { Subject = subject; Effect = effect; Question = question label proposals notProposing; Trace = trace }
    disagreement, operation

// ---------------------------------------------------------------------------
// Combination.
// ---------------------------------------------------------------------------

type private Draft =
    { Index: int
      Digest: string
      Plan: ValidatedPlan
      Operations: OperationDraft list }

type private Decision =
    | Agree of OperationDraft
    | KeepUnresolvedEffect of port: OperationDraft * effect: string * Disagreement * OperationDraft
    | Disagree of Disagreement * OperationDraft

/// Decides one label across every draft. Every draft holds at most one
/// operation per label, because a validated draft has no duplicate
/// definitions.
let private decide (all: int list) (label: string) (held: (int * OperationDraft) list) =
    let proposals key describeWith =
        held
        |> List.groupBy (snd >> key)
        |> List.map (fun (_, group) -> { Variant = describeWith (snd group.Head); Drafts = group |> List.map fst |> List.sort })
        |> List.sortBy (fun p -> p.Drafts, p.Variant)
    let traces = held |> List.map (snd >> operationTrace) |> unionTraces
    let everyDraft = held.Length = all.Length
    let variants = held |> List.map (snd >> identity) |> List.distinct
    let portShapes = held |> List.map (snd >> withoutDisposition) |> List.distinct
    match variants, portShapes, held with
    | [ agreed ], _, _ when everyDraft -> Agree(withTrace traces agreed)
    | _, [ DeclarePort { Kind = TransitionEffect(command, _) } ], _ when everyDraft ->
        let port = DeclarePort { Kind = TransitionEffect(command, Some EffectFailureDisposition.Unresolved); Trace = traces }
        let disagreement, linked = record EffectDisposition label (proposals identity dispositionText) all (Some command) traces
        KeepUnresolvedEffect(port, command, disagreement, linked)
    | _ ->
        let disagreement, operation = record Structural label (proposals identity describe) all None traces
        Disagree(disagreement, operation)

type private RaisedQuestion =
    { Subject: string
      Effect: string option
      Question: string
      RaisedBy: int list
      Trace: TraceDraft }

/// Groups exact normalized duplicates (subject, effect, question text) and
/// nothing else, so differently worded questions are never merged.
let private dedupe (questions: RaisedQuestion list) =
    questions
    |> List.groupBy (fun q -> q.Subject, q.Effect, q.Question)
    |> List.map (fun ((subject, effect, text), group) ->
        { RaisedQuestion.Subject = subject
          Effect = effect
          Question = text
          RaisedBy = group |> List.collect _.RaisedBy |> List.distinct |> List.sort
          Trace = group |> List.map _.Trace |> unionTraces })

let private sortQuestions (questions: RaisedQuestion list) =
    questions |> List.sortBy (fun q -> q.Subject, q.Effect, q.Question, q.RaisedBy)

/// Links each draft question to an effect only where the consensus plan keeps
/// that effect `unresolved` and exactly one distinct question is linked to it.
/// Otherwise the questions are kept unlinked and a disagreement record carries
/// the link, so no question is chosen over another.
let private linkEffects (all: int list) (unresolvedEffects: Map<string, bool>) (questions: RaisedQuestion list) =
    let linked effect = questions |> List.filter (fun q -> q.Effect = Some effect)
    let unlink (q: RaisedQuestion) = { q with Effect = None }
    let effectRecords, relinked =
        unresolvedEffects
        |> Map.toList
        |> List.fold
            (fun (records, kept) (effect, disputed) ->
                match disputed, linked effect with
                | false, [ _ ] -> records, kept
                | false, (_ :: _ :: _ as many) ->
                    let proposals =
                        many
                        |> List.map (fun q -> { Variant = "the question " + q.Subject; Drafts = q.RaisedBy })
                        |> List.sortBy (fun p -> p.Drafts, p.Variant)
                    let r = record EffectQuestion ("effect-question:" + effect) proposals all (Some effect) (many |> List.map _.Trace |> unionTraces)
                    r :: records, kept |> List.map (fun q -> if q.Effect = Some effect then unlink q else q)
                | _ -> records, kept |> List.map (fun q -> if q.Effect = Some effect then unlink q else q))
            ([], questions)
    // A link to an effect the plan does not keep unresolved is dropped.
    let cleaned =
        relinked |> List.map (fun q -> match q.Effect with Some e when not (Map.containsKey e unresolvedEffects) -> unlink q | _ -> q)
    List.rev effectRecords, dedupe cleaned

/// Differently worded questions that share a subject keep their own text and
/// trace; each subject gains the lowest draft number that raised it. A draft
/// cannot raise two questions under one subject, so the suffix is distinct.
let private disambiguate (questions: RaisedQuestion list) =
    questions
    |> List.groupBy _.Subject
    |> List.collect (fun (subject, group) ->
        match group with
        | [ single ] -> [ subject, single ]
        | many -> many |> List.map (fun q -> subject, { q with Subject = sprintf "%s draft %d" subject (List.min q.RaisedBy) }))

let private refusalOrder refusal =
    match refusal with
    | TooFewDrafts _ -> 0, 0
    | MixedSchemaVersions _ -> 1, 0
    | MixedVocabularyVersions _ -> 2, 0
    | InvalidDraft(position, _) -> 3, position
    | CombinedPlanInvalid _ -> 4, 0

/// Combines independently produced drafts for one prepared cohort. Requires
/// at least two drafts, each valid on its own, in one schema and vocabulary
/// version. The combined plan must itself validate; otherwise the
/// disagreements are returned for a person to decide. The result and its
/// digest do not depend on the order of `drafts`.
let combine (prepared: PreparedCohort) (drafts: PlanDraft list) : Result<ConsensusResult, ConsensusRefusal list> =
    let checkedDrafts = drafts |> List.mapi (fun i d -> i + 1, d, ConstructionPlan.validate prepared d)
    let versions f = drafts |> List.map f |> List.distinct |> List.sort
    let refusals =
        [ if drafts.Length < 2 then TooFewDrafts drafts.Length
          match versions _.SchemaVersion with
          | _ :: _ :: _ as many -> MixedSchemaVersions many
          | _ -> ()
          match versions _.VocabularyVersion with
          | _ :: _ :: _ as many -> MixedVocabularyVersions many
          | _ -> ()
          for position, _, outcome in checkedDrafts do
              match outcome with
              | Error violations -> InvalidDraft(position, violations)
              | Ok _ -> () ]
    match refusals with
    | _ :: _ -> Error(refusals |> List.sortBy refusalOrder)
    | [] ->
        let valid = checkedDrafts |> List.choose (fun (_, _, outcome) -> Result.toOption outcome)
        let ordered =
            valid
            |> List.sortBy ConstructionPlan.digest
            |> List.mapi (fun i plan ->
                { Draft.Index = i + 1
                  Digest = ConstructionPlan.digest plan
                  Plan = plan
                  Operations = ConstructionPlan.canonicalOperations plan })
        let all = ordered |> List.map _.Index
        let structural =
            ordered
            |> List.collect (fun d ->
                d.Operations
                |> List.filter (function DeclareUnresolved _ -> false | _ -> true)
                |> List.map (fun op -> operationLabel op, (d.Index, op)))
            |> List.groupBy fst
            |> List.sortBy fst
            |> List.map (fun (label, group) -> label, decide all label (List.map snd group))
        let agreed = structural |> List.choose (fun (label, d) -> match d with Agree op -> Some(label, op) | _ -> None)
        let unresolvedEffects =
            [ for _, d in structural do
                  match d with
                  | Agree(DeclarePort { Kind = TransitionEffect(command, Some EffectFailureDisposition.Unresolved) }) -> command, false
                  | KeepUnresolvedEffect(_, command, _, _) -> command, true
                  | _ -> () ]
            |> Map.ofList
        let raised =
            ordered
            |> List.collect (fun d ->
                d.Operations
                |> List.choose (function
                    | DeclareUnresolved u ->
                        Some { RaisedQuestion.Subject = u.Subject; Effect = u.Effect; Question = u.Question; RaisedBy = [ d.Index ]; Trace = u.Trace }
                    | _ -> None))
            |> dedupe
        let effectRecords, questions = linkEffects all unresolvedEffects raised
        let kept = questions |> sortQuestions |> disambiguate
        let structuralRecords =
            structural
            |> List.choose (fun (_, d) ->
                match d with
                | KeepUnresolvedEffect(_, _, disagreement, operation) | Disagree(disagreement, operation) -> Some(disagreement, operation)
                | Agree _ -> None)
        let records = structuralRecords @ effectRecords
        let disagreements = records |> List.map fst |> List.sortBy (fun d -> d.Label, d.Kind)
        let first = ordered.Head.Plan |> ConstructionPlan.view
        let combined: PlanDraft =
            { SchemaVersion = first.Binding.SchemaVersion
              VocabularyVersion = first.Binding.VocabularyVersion
              Binding =
                { SourceManifestDigest = first.Binding.SourceManifestDigest
                  BlueprintDigest = first.Binding.BlueprintDigest
                  CohortId = first.Binding.CohortId
                  DecisionIds = first.Binding.DecisionIds }
              Operations =
                [ yield! agreed |> List.map snd
                  for _, d in structural do
                      match d with
                      | KeepUnresolvedEffect(port, _, _, _) -> yield port
                      | _ -> ()
                  yield! records |> List.map snd
                  for _, q in kept ->
                      DeclareUnresolved { Subject = q.Subject; Effect = q.Effect; Question = q.Question; Trace = q.Trace } ] }
        match ConstructionPlan.validate prepared combined with
        | Error violations -> Error [ CombinedPlanInvalid(violations, disagreements) ]
        | Ok plan ->
            Ok(ConsensusResult
                { Plan = plan
                  Digest = ConstructionPlan.digest plan
                  Drafts = ordered |> List.map (fun d -> { DraftIdentity.Index = d.Index; Digest = d.Digest })
                  Agreed = agreed |> List.map fst |> List.sort
                  Disagreements = disagreements
                  Unresolved =
                    kept
                    |> List.map (fun (raisedSubject, q) ->
                        { UnresolvedQuestion.Subject = nameKey q.Subject
                          RaisedSubject = raisedSubject
                          Effect = q.Effect
                          Question = q.Question
                          RaisedBy = q.RaisedBy })
                    |> List.sortBy (fun q -> q.Subject, q.Question) })
