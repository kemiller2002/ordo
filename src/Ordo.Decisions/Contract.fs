/// A bounded semantic question with a legal typed output space.
///
/// A contract is what makes a decision something other than a prompt. It
/// names the question, fixes the answers, states what evidence the answer
/// requires, and carries an identity and revision that outlive the code
/// (ORDO-0801 / ORDO-0802).
///
/// The `ChoiceSpace` below is the mechanism by which a provider cannot
/// invent an answer. A provider sees tokens and returns a token; the only
/// tokens that parse are tokens of declared choices; so `MostlySafe` from a
/// contract offering `Safe | RequiresReview | Unsafe` is a contract
/// violation rather than a value anybody has to interpret (ORDO-0804).
module Ordo.Decisions.Contract

open System
open Ordo.Core.Identifiers
open Ordo.Core.Evidence

/// What kind of successor a contract has.
type ReplacementCompatibility =
    /// Records under the old contract can be read as records under the new
    /// one without change.
    | CompatibleReplacement
    /// The new contract asks a different question. Old records stay
    /// interpretable under the old contract and must not be reinterpreted
    /// under the new one (ORDO-3-330).
    | IncompatibleReplacement
    | MigrationRequired of note: string

type ContractReplacement =
    { Replacement: DecisionContractId
      ReplacementVersion: ContractVersion
      Compatibility: ReplacementCompatibility }

/// Where a contract is in its life.
///
/// Deprecation and retirement change what may be *asked*; neither changes
/// what past records *mean*. A record made under a retired contract stays
/// readable forever (ORDO-5602).
type ContractLifecycle =
    /// Under development. May be exercised, but a record made under a draft
    /// contract is marked as such rather than passing for production.
    | Draft
    | Active
    /// Superseded, still answering. New work should move to the successor.
    | Deprecated of ContractReplacement option
    /// Closed to new requests (ORDO-5603).
    | Retired of at: DateTimeOffset * ContractReplacement option

/// Why a choice space could not be built.
type ChoiceSpaceError =
    | NoChoicesDeclared
    | DuplicateChoiceToken of token: string
    | EmptyChoiceToken

/// The declared legal answers, together with the tokens they travel as.
///
/// Private so that the pairing of choice and token cannot be assembled
/// anywhere except through `create`, which checks that tokens are distinct
/// and non-empty. Without that check two choices could share a token and
/// parsing would silently pick one of them.
type ChoiceSpace<'choice when 'choice: equality> =
    private
        { Declared: ('choice * string) list }

    member this.Choices = this.Declared |> List.map fst
    member this.Tokens = this.Declared |> List.map snd

[<RequireQualifiedAccess>]
module ChoiceSpace =

    let create (token: 'choice -> string) (choices: 'choice list) : Result<ChoiceSpace<'choice>, ChoiceSpaceError> =
        let declared = choices |> List.map (fun choice -> choice, token choice)

        let duplicate =
            declared
            |> List.countBy snd
            |> List.tryFind (fun (_, count) -> count > 1)
            |> Option.map fst

        match declared, duplicate with
        | [], _ -> Error NoChoicesDeclared
        | _, Some token -> Error(DuplicateChoiceToken token)
        | _ when declared |> List.exists (fun (_, t) -> String.IsNullOrWhiteSpace t) -> Error EmptyChoiceToken
        | _ -> Ok { Declared = declared }

    /// Turns a token from outside the process into a choice, or nothing.
    ///
    /// This is the whole of the "providers cannot invent choices" rule. It
    /// matches ordinally and exactly: no trimming, no case folding, no
    /// nearest match. A provider that returns `safe ` for `Safe` has
    /// violated the contract, and guessing that it meant `Safe` would be
    /// repairing a semantic answer (ORDO-6102).
    let parse (token: string) (space: ChoiceSpace<'choice>) : 'choice option =
        space.Declared
        |> List.tryFind (fun (_, declared) -> String.Equals(declared, token, StringComparison.Ordinal))
        |> Option.map fst

    let tokenOf (choice: 'choice) (space: ChoiceSpace<'choice>) : string option =
        space.Declared |> List.tryFind (fst >> (=) choice) |> Option.map snd

    let tokens (space: ChoiceSpace<'choice>) = space.Tokens

    let choices (space: ChoiceSpace<'choice>) = space.Choices

/// A bounded decision.
///
/// Generic in the domain's own choice type, because the choices belong to
/// the application: `Approved`, `Safe`, `Duplicate` are not universal Ordo
/// concepts and are never defined here (ORDO-0806 / ORDO-3-030).
type DecisionContract<'choice when 'choice: equality> =
    { Id: DecisionContractId
      Version: ContractVersion
      Lifecycle: ContractLifecycle
      /// The question, in the domain's terms. An adapter renders this into
      /// whatever a provider needs; that rendering is an adapter
      /// representation of the contract, never the contract itself
      /// (ORDO-6002).
      Question: string
      /// What this contract does and does not cover. Stated because a
      /// bounded question with unstated bounds is not bounded.
      Scope: string
      Choices: ChoiceSpace<'choice>
      RequiredEvidence: EvidenceRequirement list
      /// A domain-defined consequence label, if the domain classifies by
      /// consequence. Ordo carries it and never interprets it: there is no
      /// universal risk scale here (ORDO-9302).
      Consequence: string option }

[<RequireQualifiedAccess>]
module ContractLifecycle =

    /// Whether the contract may receive new decision requests.
    let acceptsNewRequests lifecycle =
        match lifecycle with
        | Draft
        | Active
        | Deprecated _ -> true
        | Retired _ -> false

    let toWire lifecycle =
        match lifecycle with
        | Draft -> "draft"
        | Active -> "active"
        | Deprecated _ -> "deprecated"
        | Retired _ -> "retired"

[<RequireQualifiedAccess>]
module DecisionContract =

    let create
        (id: DecisionContractId)
        (version: ContractVersion)
        (question: string)
        (scope: string)
        (choices: ChoiceSpace<'choice>)
        =
        { Id = id
          Version = version
          Lifecycle = Draft
          Question = question
          Scope = scope
          Choices = choices
          RequiredEvidence = []
          Consequence = None }

    let activated (contract: DecisionContract<'choice>) = { contract with Lifecycle = Active }

    let requiring (requirements: EvidenceRequirement list) (contract: DecisionContract<'choice>) =
        { contract with RequiredEvidence = requirements }

    let withConsequence (label: string) (contract: DecisionContract<'choice>) =
        { contract with Consequence = Some label }

    let retiredAt (at: DateTimeOffset) (replacement: ContractReplacement option) (contract: DecisionContract<'choice>) =
        { contract with Lifecycle = Retired(at, replacement) }
