/// Stable semantic identity for everything executable Ordo records.
///
/// Identifiers are the first place text from outside the process — a
/// provider response, a persisted record, a configuration file — becomes a
/// semantic reference. Construction is therefore a checked operation whose
/// refusal is data (`Result`), not an exception, and the underlying string
/// is unreachable except through a smart constructor. A raw `string` cannot
/// be mistaken for a contract identity by accident.
module Ordo.Core.Identifiers

/// Why a proposed identifier was refused.
///
/// The cases are distinct because they have different causes: an empty or
/// untrimmed value is usually a caller defect, a control character is
/// usually hostile or corrupt input, and an over-long value is usually a
/// misuse of an identifier field to carry content.
type IdentifierError =
    | IdentifierEmpty
    | IdentifierNotTrimmed of raw: string
    | IdentifierTooLong of length: int * limit: int
    | IdentifierHasControlCharacter of raw: string

/// The limit every identifier in this library shares. Identifiers name
/// things; they are not a place to store evidence, prose, or state.
[<Literal>]
let MaxIdentifierLength = 200

let private validate (raw: string) : Result<string, IdentifierError> =
    // The emptiness check is written against `String.IsNullOrEmpty` rather
    // than `isNull`: identifiers are constructed from persisted records and
    // from callers in other languages, where a null can genuinely arrive, and
    // F#'s own nullness analysis rightly refuses `isNull` on a `string` that
    // its signature says cannot be one.
    if System.String.IsNullOrEmpty raw then Error IdentifierEmpty
    elif raw.Trim() <> raw then Error(IdentifierNotTrimmed raw)
    elif raw.Length > MaxIdentifierLength then Error(IdentifierTooLong(raw.Length, MaxIdentifierLength))
    elif raw |> Seq.exists System.Char.IsControl then Error(IdentifierHasControlCharacter raw)
    else Ok raw

/// Identity of a bounded semantic question. Survives moving between files,
/// modules, assemblies and packages: the contract is what the identifier
/// names, not where its definition happens to live (ORDO-5605).
type DecisionContractId =
    private
    | DecisionContractId of string

    member this.Value =
        let (DecisionContractId value) = this
        value

    override this.ToString() = this.Value

/// Identity of one invocation of a contract against specific state and
/// evidence. Lets a replay, a verification and a duplicate be told apart
/// from each other and from the original.
type DecisionRequestId =
    private
    | DecisionRequestId of string

    member this.Value =
        let (DecisionRequestId value) = this
        value

    override this.ToString() = this.Value

/// Identity of a single piece of evidence, stable enough to be referenced
/// from a decision record without copying the evidence itself.
type EvidenceId =
    private
    | EvidenceId of string

    member this.Value =
        let (EvidenceId value) = this
        value

    override this.ToString() = this.Value

/// Identity of one resolution execution — one pass through Compute, Decide
/// or Deliberate. This is the identifier an observing system keys execution
/// facts on.
type ResolutionId =
    private
    | ResolutionId of string

    member this.Value =
        let (ResolutionId value) = this
        value

    override this.ToString() = this.Value

/// Identity shared by a group of causally related resolutions. Distinct
/// from a parent link: a correlation may span several causal branches
/// (ORDO-7102).
type CorrelationId =
    private
    | CorrelationId of string

    member this.Value =
        let (CorrelationId value) = this
        value

    override this.ToString() = this.Value

/// Identity of the system that produced a result — a model service, a
/// deterministic rule, a recorded fixture, a human.
type ProviderId =
    private
    | ProviderId of string

    member this.Value =
        let (ProviderId value) = this
        value

    override this.ToString() = this.Value

/// Identity of the rule set that interpreted a decision. Policy and
/// contract evolve independently (ORDO-5701), so they identify separately.
type PolicyId =
    private
    | PolicyId of string

    member this.Value =
        let (PolicyId value) = this
        value

    override this.ToString() = this.Value

/// Identity of a unit of required, not-yet-satisfied work.
type ObligationId =
    private
    | ObligationId of string

    member this.Value =
        let (ObligationId value) = this
        value

    override this.ToString() = this.Value

/// Identity of one attempted external effect.
///
/// This identifies the semantic operation whose outcome may need
/// reconciliation. It is not automatically an idempotency key; whether the
/// external contract treats it that way is a separate fact.
type ExternalEffectId =
    private
    | ExternalEffectId of string

    member this.Value =
        let (ExternalEffectId value) = this
        value

    override this.ToString() = this.Value

/// Identity of an authority to request or perform something. Capabilities
/// are named, granted and checked; they are never derived from confidence
/// or from provider identity.
type CapabilityId =
    private
    | CapabilityId of string

    member this.Value =
        let (CapabilityId value) = this
        value

    override this.ToString() = this.Value

[<RequireQualifiedAccess>]
module DecisionContractId =
    let create (raw: string) : Result<DecisionContractId, IdentifierError> =
        validate raw |> Result.map DecisionContractId

    let value (id: DecisionContractId) = id.Value

[<RequireQualifiedAccess>]
module DecisionRequestId =
    let create (raw: string) : Result<DecisionRequestId, IdentifierError> =
        validate raw |> Result.map DecisionRequestId

    let value (id: DecisionRequestId) = id.Value

[<RequireQualifiedAccess>]
module EvidenceId =
    let create (raw: string) : Result<EvidenceId, IdentifierError> =
        validate raw |> Result.map EvidenceId

    let value (id: EvidenceId) = id.Value

[<RequireQualifiedAccess>]
module ResolutionId =
    let create (raw: string) : Result<ResolutionId, IdentifierError> =
        validate raw |> Result.map ResolutionId

    let value (id: ResolutionId) = id.Value

[<RequireQualifiedAccess>]
module CorrelationId =
    let create (raw: string) : Result<CorrelationId, IdentifierError> =
        validate raw |> Result.map CorrelationId

    let value (id: CorrelationId) = id.Value

[<RequireQualifiedAccess>]
module ProviderId =
    let create (raw: string) : Result<ProviderId, IdentifierError> =
        validate raw |> Result.map ProviderId

    let value (id: ProviderId) = id.Value

[<RequireQualifiedAccess>]
module PolicyId =
    let create (raw: string) : Result<PolicyId, IdentifierError> =
        validate raw |> Result.map PolicyId

    let value (id: PolicyId) = id.Value

[<RequireQualifiedAccess>]
module ObligationId =
    let create (raw: string) : Result<ObligationId, IdentifierError> =
        validate raw |> Result.map ObligationId

    let value (id: ObligationId) = id.Value

[<RequireQualifiedAccess>]
module ExternalEffectId =
    let create (raw: string) : Result<ExternalEffectId, IdentifierError> =
        validate raw |> Result.map ExternalEffectId

    let value (id: ExternalEffectId) = id.Value

[<RequireQualifiedAccess>]
module CapabilityId =
    let create (raw: string) : Result<CapabilityId, IdentifierError> =
        validate raw |> Result.map CapabilityId

    let value (id: CapabilityId) = id.Value

/// A contract's semantic revision.
///
/// Deliberately a monotonic integer rather than a three-part version: the
/// only question a historical record must be able to answer is "which
/// semantics were in force", and a single number answers it without
/// inviting arguments about whether a change was minor or patch
/// (ORDO-2202). Contract identity plus revision is the full key.
type ContractVersion =
    private
    | ContractVersion of int

    member this.Value =
        let (ContractVersion value) = this
        value

    override this.ToString() = string this.Value

/// A policy's revision, versioned separately from the contract it
/// interprets so that changing a threshold does not restate the question
/// (ORDO-5701).
type PolicyVersion =
    private
    | PolicyVersion of int

    member this.Value =
        let (PolicyVersion value) = this
        value

    override this.ToString() = string this.Value

/// Why a proposed version number was refused.
type VersionError = VersionNotPositive of int

[<RequireQualifiedAccess>]
module ContractVersion =
    let create (value: int) : Result<ContractVersion, VersionError> =
        if value < 1 then Error(VersionNotPositive value) else Ok(ContractVersion value)

    let value (version: ContractVersion) = version.Value

[<RequireQualifiedAccess>]
module PolicyVersion =
    let create (value: int) : Result<PolicyVersion, VersionError> =
        if value < 1 then Error(VersionNotPositive value) else Ok(PolicyVersion value)

    let value (version: PolicyVersion) = version.Value
