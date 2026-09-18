/// Turning what a provider said into what the domain is allowed to hear.
///
/// Every provider response passes through here, and everything that arrives
/// is untrusted until it has (ORDO-7501 / ORDO-3-155). The function is pure
/// and synchronous, so the rules below are testable without a provider, a
/// network or a clock.
///
/// What this module will not do is as important as what it will:
///
/// * It never repairs a semantic answer. An undeclared choice token is a
///   contract violation, not a value to be matched to the nearest legal one
///   (ORDO-6101 / ORDO-6102).
/// * It never derives the answer from prose. A rationale that argues for a
///   different choice is recorded as a conflict and changes nothing
///   (ORDO-6104).
/// * It never invents confidence, and never treats its absence as a defect
///   (ORDO-6302 / ORDO-6303).
module Ordo.Decisions.Validation

open System
open Ordo.Core.Identifiers
open Ordo.Core.Evidence
open Ordo.Core.StateIdentity
open Ordo.Decisions.Contract
open Ordo.Decisions.Outcome
open Ordo.Decisions.Provider

/// Which declared choice tokens a rationale mentions.
///
/// Used only to surface a disagreement between prose and the typed result.
/// Ordinal, case-sensitive, and it looks for whole tokens: a loose match
/// would turn this into prose parsing, which is the thing being guarded
/// against.
let private tokensMentionedIn (rationale: string) (tokens: string list) =
    if String.IsNullOrWhiteSpace rationale then
        []
    else
        tokens
        |> List.filter (fun token ->
            let index = rationale.IndexOf(token, StringComparison.Ordinal)

            if index < 0 then
                false
            else
                let before = index = 0 || not (Char.IsLetterOrDigit rationale.[index - 1])
                let after = index + token.Length >= rationale.Length || not (Char.IsLetterOrDigit rationale.[index + token.Length])
                before && after)

/// Validates one provider response against the contract that produced it.
///
/// `evidenceUsed` is supplied by the caller rather than by the provider: the
/// evidence a decision rests on is what the contract required and the
/// request carried, not what a provider claims to have read.
let validate
    (contract: DecisionContract<'choice>)
    (state: StateSnapshot)
    (evidenceUsed: EvidenceId list)
    (identity: ProviderIdentity)
    (now: DateTimeOffset)
    (response: ProviderResponse)
    : DecisionOutcome<'choice> =
    match response with
    | ChoiceSelected(token, confidence, rationale) ->
        match ChoiceSpace.parse token contract.Choices with
        | None ->
            ProviderFailure(
                ContractViolation(
                    sprintf
                        "provider returned %s, which is not one of the declared choices [%s]"
                        (Ordo.Core.Json.render (Ordo.Core.Json.JString token))
                        (String.Join("; ", ChoiceSpace.tokens contract.Choices))
                )
            )
        | Some choice ->
            let conflicting =
                match rationale with
                | None -> []
                | Some text ->
                    tokensMentionedIn text (ChoiceSpace.tokens contract.Choices)
                    |> List.filter (fun mentioned -> not (String.Equals(mentioned, token, StringComparison.Ordinal)))

            Decided
                { Choice = choice
                  Provider = identity
                  Confidence = confidence
                  EvidenceUsed = evidenceUsed
                  Rationale = rationale
                  RationaleMentionsOtherChoices = conflicting
                  DecidedAgainst = state.Fingerprint
                  DecidedAt = now }

    | EvidenceInsufficient requirementIds ->
        // A provider may only report a requirement the contract declared.
        // Accepting an invented requirement id would let a provider define
        // what the decision needs, which is the application's to say.
        let declared = contract.RequiredEvidence |> List.map (fun r -> r.Id) |> Set.ofList
        let undeclared = requirementIds |> List.filter (declared.Contains >> not)

        if not (List.isEmpty undeclared) then
            ProviderFailure(
                ContractViolation(
                    sprintf "provider reported evidence requirements the contract does not declare: %s" (String.Join("; ", undeclared))
                )
            )
        else
            let reported = Set.ofList requirementIds

            InsufficientEvidence(contract.RequiredEvidence |> List.filter (fun r -> reported.Contains r.Id))

    | DeliberationRequested note -> RequiresDeliberation(ProviderDeclined note)
    | HumanReviewRequested note -> RequiresHumanReview(ProviderRequestedPerson note)
    | ProviderFailed error -> ProviderFailure error
    | ResponseCancelled -> ResolutionCancelled
