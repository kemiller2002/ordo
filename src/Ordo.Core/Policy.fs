/// Deterministic interpretation of domain facts.
///
/// Ordo supplies the mechanism; a domain supplies the rules (ORDO-1501).
/// Two properties are enforced by the shape of the types rather than by
/// convention:
///
/// * A policy is a pure function of the facts it is given, so the same facts
///   and the same policy version always produce the same verdict
///   (ORDO-1503). It cannot call a provider, because it cannot do anything
///   asynchronous or effectful.
/// * A policy is identified and versioned independently of the contract it
///   interprets, so that changing a threshold does not restate the question
///   (ORDO-5701) and a historical record can name the rules that judged it
///   (ORDO-5702).
module Ordo.Core.Policy

open Ordo.Core.Identifiers

type PolicyIdentity =
    { Id: PolicyId
      Version: PolicyVersion
      /// Marks a policy as experimental so that a variant under trial can
      /// never be mistaken for the production rules in a record
      /// (ORDO-5704 / ORDO-8201).
      Experimental: bool }

/// What a policy concluded.
///
/// `RequiresHumanReview` is a first-class verdict rather than a refusal
/// because needing a person is a normal outcome, not a failure (ORDO-3001).
type PolicyVerdict =
    | PolicyAllows
    | PolicyRefuses of reason: string
    | PolicyRequiresHumanReview of reason: string

/// A named, versioned rule over facts of type `'facts`.
///
/// Generic in its facts so that a domain evaluates its own state, evidence
/// and consequence without Ordo defining a universal fact schema or a
/// universal risk scale (ORDO-9302).
type Policy<'facts> =
    { Identity: PolicyIdentity
      Evaluate: 'facts -> PolicyVerdict }

[<RequireQualifiedAccess>]
module Policy =

    let create (id: PolicyId) (version: PolicyVersion) (evaluate: 'facts -> PolicyVerdict) =
        { Identity =
            { Id = id
              Version = version
              Experimental = false }
          Evaluate = evaluate }

    let asExperimental (policy: Policy<'facts>) =
        { policy with Identity = { policy.Identity with Experimental = true } }

    let verdictToWire verdict =
        match verdict with
        | PolicyAllows -> "allows"
        | PolicyRefuses _ -> "refuses"
        | PolicyRequiresHumanReview _ -> "requires-human-review"
