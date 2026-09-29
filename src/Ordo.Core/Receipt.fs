/// Typed expected and observed receipts, and their comparison
/// (ORD-EXEC-040..044, ORD-EXEC-100..107).
///
/// Three concepts stay separate on purpose:
///
/// * an `ExpectedReceipt` is a typed postcondition declared before a step;
/// * an `ObservedReceipt` is what was actually seen afterwards, with the
///   actor's narrative kept apart from machine-observed facts;
/// * a `ReceiptResult` is the comparison, which is `Match`, `Mismatch` or
///   `Indeterminate` — and `Indeterminate` is neither success nor failure.
///
/// A narrative claim never participates in the comparison. Only observed
/// facts do (ORD-EXEC-043 / ORD-EXEC-107).
module Ordo.Core.Receipt

/// A typed postcondition (ORD-EXEC-101).
type ExpectedReceipt =
    /// The artifact exists, whatever its content.
    | ArtifactExists of reference: string
    /// The artifact exists with exactly this content digest.
    | ArtifactIdentity of reference: string * digest: string
    /// The artifact no longer exists.
    | ArtifactAbsent of reference: string
    /// A command or capability invocation completed successfully.
    | CommandSucceeded of command: string
    /// A named piece of state has this value.
    | StateEquals of key: string * value: string
    /// The artifact conforms to a named contract (a schema, an interface).
    | ConformsToContract of reference: string * contract: string
    /// Verification passed under the identified evaluator.
    | VerificationSatisfied of evaluatorFingerprint: string
    /// A named transition was observed.
    | TransitionObserved of transition: string
    /// Every constituent postcondition holds (ORD-EXEC-106).
    | AllOf of ExpectedReceipt list

/// One machine- or independently-observed fact.
type ObservedFact =
    | ArtifactObserved of reference: string * digest: string option
    | ArtifactNotFound of reference: string
    | CommandExited of command: string * exitCode: int
    /// The command was attempted but its outcome is unknown: a timeout, a
    /// lost connection, a crash after dispatch.
    | CommandOutcomeUnknown of command: string * reason: string
    | StateObserved of key: string * value: string
    | ContractChecked of reference: string * contract: string * conforms: bool
    | VerificationObserved of evaluatorFingerprint: string * passed: bool
    | TransitionRecorded of transition: string
    /// The host tried to observe a subject and could not.
    | Unobservable of subject: string * reason: string

/// Who observed the facts. A self-report by the executing actor is weaker
/// evidence than a host observation, and the distinction is kept
/// (ORD-EXEC-043).
type ObservationSource =
    | HostObserved of observer: string
    | IndependentlyObserved of observer: string
    | SelfReported of actor: string

type ObservedReceipt =
    { Source: ObservationSource
      Facts: ObservedFact list
      /// The actor's account. Recorded; never compared.
      Narrative: string option }

/// The outcome of comparing one expected receipt with what was observed.
///
/// Every case carries the facts that justified it (ORD-EXEC-105). A composite
/// keeps each constituent's own result so a partial match cannot be mistaken
/// for complete satisfaction (ORD-EXEC-106).
type ReceiptResult =
    | Match of expected: ExpectedReceipt * because: ObservedFact list
    | Mismatch of expected: ExpectedReceipt * reason: string * because: ObservedFact list
    | Indeterminate of expected: ExpectedReceipt * reason: string * because: ObservedFact list
    | Composite of expected: ExpectedReceipt * constituents: ReceiptResult list

/// The three-valued summary of a result tree.
type ReceiptOutcome =
    | Matched
    | Mismatched
    | Undetermined

[<RequireQualifiedAccess>]
module ReceiptOutcome =

    let toWire outcome =
        match outcome with
        | Matched -> "match"
        | Mismatched -> "mismatch"
        | Undetermined -> "indeterminate"

    let fromWire raw =
        match raw with
        | "match" -> Some Matched
        | "mismatch" -> Some Mismatched
        | "indeterminate" -> Some Undetermined
        | _ -> None

[<RequireQualifiedAccess>]
module Receipt =

    let private isPresence fact =
        match fact with
        | ArtifactObserved _ -> true
        | _ -> false

    let private sourceTrusted (observed: ObservedReceipt) =
        match observed.Source with
        | HostObserved _
        | IndependentlyObserved _ -> true
        | SelfReported _ -> false

    let private unobservableFor subject facts =
        facts
        |> List.filter (fun f ->
            match f with
            | Unobservable(s, _) -> s = subject
            | _ -> false)

    let private undetermined expected subject (facts: ObservedFact list) fallback =
        match unobservableFor subject facts with
        | [] -> Indeterminate(expected, fallback, [])
        | why -> Indeterminate(expected, "not observable", why)

    let rec private compareOne (facts: ObservedFact list) (expected: ExpectedReceipt) : ReceiptResult =
        match expected with
        | ArtifactExists reference ->
            let seen =
                facts
                |> List.filter (fun f ->
                    match f with
                    | ArtifactObserved(r, _)
                    | ArtifactNotFound r -> r = reference
                    | _ -> false)

            match seen with
            | [] -> undetermined expected reference facts "no observation of the artifact"
            | _ when seen |> List.exists isPresence -> Match(expected, seen)
            | _ -> Mismatch(expected, "artifact not found", seen)
        | ArtifactAbsent reference ->
            let seen =
                facts
                |> List.filter (fun f ->
                    match f with
                    | ArtifactObserved(r, _)
                    | ArtifactNotFound r -> r = reference
                    | _ -> false)

            match seen with
            | [] -> undetermined expected reference facts "no observation of the artifact"
            | _ when seen |> List.exists isPresence -> Mismatch(expected, "artifact still present", seen)
            | _ -> Match(expected, seen)
        | ArtifactIdentity(reference, digest) ->
            let seen =
                facts
                |> List.filter (fun f ->
                    match f with
                    | ArtifactObserved(r, _)
                    | ArtifactNotFound r -> r = reference
                    | _ -> false)

            match seen with
            | [] -> undetermined expected reference facts "no observation of the artifact"
            | _ ->
                let digests =
                    seen
                    |> List.choose (fun f ->
                        match f with
                        | ArtifactObserved(_, d) -> Some d
                        | _ -> None)

                match digests with
                | [] -> Mismatch(expected, "artifact not found", seen)
                | ds when ds |> List.exists (fun d -> d = Some digest) -> Match(expected, seen)
                | ds when ds |> List.forall Option.isNone ->
                    Indeterminate(expected, "artifact observed without a digest", seen)
                | _ -> Mismatch(expected, "artifact digest differs", seen)
        | CommandSucceeded command ->
            let seen =
                facts
                |> List.filter (fun f ->
                    match f with
                    | CommandExited(c, _)
                    | CommandOutcomeUnknown(c, _) -> c = command
                    | _ -> false)

            match seen |> List.tryLast with
            | None -> undetermined expected command facts "command outcome not observed"
            | Some(CommandExited(_, 0)) -> Match(expected, seen)
            | Some(CommandExited(_, code)) -> Mismatch(expected, sprintf "exit code %d" code, seen)
            | Some _ -> Indeterminate(expected, "command outcome unknown", seen)
        | StateEquals(key, value) ->
            let seen =
                facts
                |> List.filter (fun f ->
                    match f with
                    | StateObserved(k, _) -> k = key
                    | _ -> false)

            match seen |> List.tryLast with
            | None -> undetermined expected key facts "state not observed"
            | Some(StateObserved(_, v)) when v = value -> Match(expected, seen)
            | Some _ -> Mismatch(expected, "state differs", seen)
        | ConformsToContract(reference, contract) ->
            let seen =
                facts
                |> List.filter (fun f ->
                    match f with
                    | ContractChecked(r, c, _) -> r = reference && c = contract
                    | _ -> false)

            match seen |> List.tryLast with
            | None -> undetermined expected reference facts "contract conformance not observed"
            | Some(ContractChecked(_, _, true)) -> Match(expected, seen)
            | Some _ -> Mismatch(expected, "artifact does not conform", seen)
        | VerificationSatisfied fingerprint ->
            let seen =
                facts
                |> List.filter (fun f ->
                    match f with
                    | VerificationObserved _ -> true
                    | _ -> false)

            match seen |> List.tryLast with
            | None -> undetermined expected "verification" facts "verification not observed"
            | Some(VerificationObserved(fp, _)) when fp <> fingerprint ->
                // A verdict under another evaluator says nothing about this
                // one: not a failure, not a pass (ORD-EXEC-094).
                Indeterminate(expected, "verification observed under a different evaluator", seen)
            | Some(VerificationObserved(_, true)) -> Match(expected, seen)
            | Some _ -> Mismatch(expected, "verification failed", seen)
        | TransitionObserved transition ->
            let seen =
                facts
                |> List.filter (fun f ->
                    match f with
                    | TransitionRecorded t -> t = transition
                    | _ -> false)

            match seen with
            | [] -> undetermined expected transition facts "transition not observed"
            | _ -> Match(expected, seen)
        | AllOf parts -> Composite(expected, parts |> List.map (compareOne facts))

    /// Summarize a result tree. A composite matches only when every
    /// constituent matches; any mismatch dominates; otherwise it is
    /// indeterminate.
    let rec outcome (result: ReceiptResult) : ReceiptOutcome =
        match result with
        | Match _ -> Matched
        | Mismatch _ -> Mismatched
        | Indeterminate _ -> Undetermined
        | Composite(_, []) -> Matched
        | Composite(_, parts) ->
            let outcomes = parts |> List.map outcome

            if outcomes |> List.contains Mismatched then Mismatched
            elif outcomes |> List.contains Undetermined then Undetermined
            else Matched

    /// Compare an expected receipt with an observed one.
    ///
    /// A self-reported observation can never produce `Match`: the most it can
    /// establish is that the actor claims the postcondition, which is
    /// insufficient evidence (ORD-EXEC-043). A self-reported mismatch is still
    /// a mismatch — an actor reporting its own failure is believed.
    let compare (expected: ExpectedReceipt) (observed: ObservedReceipt) : ReceiptResult =
        let raw = compareOne observed.Facts expected

        if sourceTrusted observed then
            raw
        else
            let rec demote result =
                match result with
                | Match(e, because) -> Indeterminate(e, "self-reported evidence is insufficient", because)
                | Composite(e, parts) -> Composite(e, parts |> List.map demote)
                | Mismatch _
                | Indeterminate _ -> result

            demote raw

    /// Only a match permits automatic advancement (ORD-EXEC-042).
    let permitsAdvancement result = outcome result = Matched

    /// The leaf results of a result tree, for diagnosis.
    let rec leaves (result: ReceiptResult) : ReceiptResult list =
        match result with
        | Composite(_, parts) -> parts |> List.collect leaves
        | leaf -> [ leaf ]
