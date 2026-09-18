/// How sure something is — and, inseparably, what that number means.
///
/// A bare float is the problem this module exists to prevent. `0.92` from a
/// language model asked how sure it is, `0.92` computed by transforming a
/// provider's log-probabilities, and `0.92` measured from a thousand
/// recorded outcomes are three different claims, and only the last is a
/// calibrated probability (ORDO-1102 / ORDO-1103 / ORDO-6304). A
/// `Confidence` therefore cannot be constructed without saying which it is.
///
/// Nothing in this library reads a confidence value to grant authority.
/// Confidence may feed a domain's policy; it can never be a capability
/// (ORDO-0602 / ORDO-3-323).
module Ordo.Decisions.Confidence

/// Where a confidence value came from, and therefore what it may be used to
/// claim.
type ConfidenceProvenance =
    /// The provider said so. This is a self-report: it is not evidence that
    /// the provider is right this often.
    | ProviderReported
    /// Computed from something the provider supplied, by a named
    /// transformation. The name is required so that a mapped or normalised
    /// score never loses the fact that it was mapped.
    | Derived of transformation: string
    /// Measured against recorded outcomes. Ordo never produces this value
    /// itself — calibration needs history, which belongs to an observing
    /// system (ORDO-1104). Ordo can only carry one that was measured
    /// elsewhere.
    | EmpiricallyCalibrated of basis: string * sampleSize: int

/// Why a proposed confidence value was refused.
type ConfidenceError =
    | ConfidenceOutOfRange of value: float
    | ConfidenceNotFinite
    | CalibrationSampleNotPositive of sampleSize: int

/// A bounded magnitude with its provenance attached.
///
/// The scale is fixed and documented: `0.0` to `1.0` inclusive, where higher
/// means more confident (ORDO-6301). Values outside it are refused rather
/// than clamped, because a clamped value is a fabricated one.
type Confidence =
    private
        { ConfidenceMagnitude: float
          ConfidenceProvenance: ConfidenceProvenance }

    member this.Magnitude = this.ConfidenceMagnitude
    member this.Provenance = this.ConfidenceProvenance

[<RequireQualifiedAccess>]
module Confidence =

    let create (provenance: ConfidenceProvenance) (value: float) : Result<Confidence, ConfidenceError> =
        if System.Double.IsNaN value || System.Double.IsInfinity value then
            Error ConfidenceNotFinite
        elif value < 0.0 || value > 1.0 then
            Error(ConfidenceOutOfRange value)
        else
            match provenance with
            | EmpiricallyCalibrated(_, sampleSize) when sampleSize < 1 ->
                Error(CalibrationSampleNotPositive sampleSize)
            | _ ->
                Ok
                    { ConfidenceMagnitude = value
                      ConfidenceProvenance = provenance }

    /// Confidence a provider reported about itself.
    let providerReported (value: float) = create ProviderReported value

    /// True only for a value measured against recorded outcomes. Policies
    /// that genuinely need a probability — as opposed to a self-report —
    /// ask this rather than reading the magnitude.
    let isCalibrated (confidence: Confidence) =
        match confidence.Provenance with
        | EmpiricallyCalibrated _ -> true
        | ProviderReported
        | Derived _ -> false

    let provenanceToWire provenance =
        match provenance with
        | ProviderReported -> "provider-reported"
        | Derived _ -> "derived"
        | EmpiricallyCalibrated _ -> "empirically-calibrated"
