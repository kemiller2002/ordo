Ordo Executable Intelligence Requirements

Status: First-pass requirements baseline
Purpose: Extend Ordo from an agent-directed engineering methodology into a methodology with a small set of executable, reusable primitives for state, evidence, decisions, deliberation, policy, and resolution.

This document does not replace the existing Ordo methodology, construction method, verification rules, navigation rules, structural-locality rules, research methods, or agent guidance.

The executable implementation exists to encode stable concepts mechanically where doing so provides value.

The implementation must remain subordinate to the Ordo methodology rather than gradually replacing the methodology with a software framework.

⸻

0. Foundational Intent

ORDO-0001 — Preserve Ordo as a methodology

Ordo remains first and foremost an engineering methodology.

Executable Ordo components shall encode only concepts that are sufficiently stable, general, and mechanical to justify implementation in software.

Not every Ordo rule belongs in code.

The implementation must not attempt to convert qualitative engineering guidance into arbitrary APIs merely for the sake of making Ordo executable.

⸻

ORDO-0002 — Introduce executable Ordo primitives

Ordo shall provide executable primitives for concepts that applications and engineering systems repeatedly need to represent.

Initial candidate concepts are:

* state
* transitions
* capabilities
* evidence
* obligations
* policy
* resolution
* decision contracts
* decision outcomes
* deliberation contracts
* escalation
* verification references

These concepts shall be introduced incrementally.

⸻

ORDO-0003 — Preserve authority boundaries

The architectural authority model shall be:

Applications own domain truth.

Applications define their own:

* domain states
* domain transitions
* commands
* decisions
* invariants
* business policies
* consequences
* evidence requirements

Ordo owns generic semantics.

Ordo defines the meaning and mechanics of concepts such as:

* bounded decision
* evidence requirement
* resolution mode
* escalation
* capability
* obligation

ROS owns operational observation and optimization.

ROS may:

* execute workflows
* record execution
* measure providers
* compare providers
* replay decisions
* calculate calibration
* surface optimization opportunities

ROS shall not redefine Ordo semantics.

Providers provide intelligence.

AI models, humans, deterministic algorithms, or other providers may produce results.

They do not own application state or authorization.

⸻

1. Non-Goals

ORDO-0101 — Ordo shall not become an agent framework

Ordo shall not attempt to provide a general-purpose autonomous-agent runtime.

It shall not reproduce functionality already available in ChatGPT, Claude, Codex, or similar agents unless a capability is necessary to enforce an Ordo contract.

⸻

ORDO-0102 — Ordo shall not become a workflow engine

Ordo shall not become a generic workflow/orchestration platform.

Applications may use Ordo primitives to model workflows, but Ordo itself shall not own application workflow definitions.

⸻

ORDO-0103 — Ordo shall not become an AI-provider SDK

Provider-specific functionality shall remain behind narrow adapters.

OpenAI, Anthropic, Jev, local models, future Echelon models, and other providers shall not leak provider-specific concepts into the Ordo core.

⸻

ORDO-0104 — Ordo shall not require AI

Every core Ordo concept shall function without an AI provider.

An application shall be able to use:

* state
* transitions
* capabilities
* evidence
* obligations
* policy
* deterministic computation

without installing or configuring an AI provider.

⸻

ORDO-0105 — Do not create a model-training platform

Model training, fine-tuning, RL, RLCD-style approaches, dataset training pipelines, GPU management, and model hosting are outside the initial Ordo implementation.

The architecture shall make future experimentation possible without prematurely implementing it.

⸻

ORDO-0106 — Do not create automatic self-modification

Ordo shall not autonomously change:

* policies
* decision contracts
* state definitions
* confidence thresholds
* capability definitions
* provider-selection criteria

based merely on observed data.

Such changes require explicit engineering authority.

⸻

2. Implementation Technology

ORDO-0201 — F# implementation

All first-party executable Ordo production code shall be implemented in F#.

F# shall be used intentionally for:

* discriminated unions
* records
* pattern matching
* function composition
* exhaustive state handling
* domain modeling
* making illegal states difficult or impossible to represent

C# shall not be introduced merely for convenience.

⸻

ORDO-0202 — .NET implementation

Executable Ordo shall target .NET.

The implementation shall use the repository’s approved/current .NET SDK.

The work shall not independently upgrade the repository’s .NET version unless required and explicitly justified.

⸻

ORDO-0203 — Minimal dependencies

External dependencies shall be minimized.

A dependency may be introduced only when its value materially exceeds the maintenance, security, compatibility, and conceptual cost it introduces.

Prefer:

* FSharp.Core
* .NET BCL
* small first-party modules

over large frameworks.

⸻

ORDO-0204 — No framework dependency

Ordo Core shall not depend on:

* ASP.NET
* dependency-injection frameworks
* web frameworks
* persistence frameworks
* logging frameworks
* agent frameworks
* cloud-provider SDKs
* model-provider SDKs

These may exist in adapters where justified.

⸻

ORDO-0205 — Functional core

Core Ordo behavior shall favor:

data → pure function → result

over:

service object → hidden mutable state → side effects

Side effects shall be pushed toward explicit boundaries.

⸻

ORDO-0206 — No required dependency-injection container

Ordo shall not require a DI container.

Dependencies shall be expressible explicitly through:

* function parameters
* records of functions
* interfaces where appropriate
* composition roots

⸻

3. Project and Package Structure

The initial architecture should permit separation along these conceptual lines:

Ordo.Core
Ordo.Decisions
Ordo.Execution
Ordo.Providers.*
Ordo.Testing

These do not all need to become separate NuGet packages immediately.

Physical project boundaries shall be introduced only where they create meaningful semantic or dependency boundaries.

⸻

ORDO-0301 — Ordo.Core

Ordo.Core shall contain provider-independent concepts.

Expected concepts include:

State identity
Evidence
Evidence requirements
Capabilities
Obligations
Policies
Resolution modes
Transition results
Common identifiers
Version identifiers

It shall have no AI dependency.

⸻

ORDO-0302 — Ordo.Decisions

The decision layer shall contain:

DecisionContract
DecisionRequest
DecisionResult
DecisionOutcome
Confidence representation
Decision candidates
Escalation
DeliberationRequest
DeliberationResult

It shall not depend directly on a specific AI provider.

⸻

ORDO-0303 — Ordo.Execution

Execution shall coordinate Ordo contracts.

It may decide that the next unresolved operation requires:

Compute
Decide
Deliberate
Evidence acquisition
Human resolution

It shall not itself contain provider-specific AI behavior.

⸻

ORDO-0304 — Provider adapters

Provider-specific integrations shall exist outside the provider-neutral core.

For example:

Ordo.Providers.OpenAI
Ordo.Providers.Anthropic
Ordo.Providers.Jev
Ordo.Providers.Local

Names are illustrative until repository conventions determine physical packaging.

⸻

ORDO-0305 — Testing support

Reusable testing primitives may be supplied for:

* fake providers
* deterministic providers
* decision fixtures
* transition fixtures
* evidence fixtures
* replay fixtures

Testing support must not contaminate the production domain model.

⸻

4. Resolution Model

ORDO-0401 — Three fundamental resolution modes

Ordo shall distinguish:

type ResolutionMode =
    | Compute
    | Decide
    | Deliberate

These names are preferred over psychological terminology such as System 1/System 2 in the public API.

Fast/slow cognition may inform the architecture, but the executable terminology must describe actual engineering behavior.

⸻

ORDO-0402 — Compute

Compute means that the result can be derived mechanically from available information.

Examples include:

mathematics
comparison
parsing
dependency traversal
schema inspection
state validation
permission lookup
hash calculation
test execution
compiler output

An AI model shall not be required for a computation merely because using one is convenient.

⸻

ORDO-0403 — Decide

Decide means:

* judgment is required;
* the valid output space is known;
* the decision can be expressed as a typed bounded choice;
* open-ended exploration is not normally required.

Examples may include:

Safe | Review | Unsafe
Duplicate | Related | Unrelated
Accept | Reject | Escalate

⸻

ORDO-0404 — Deliberate

Deliberate means the unresolved work requires activities such as:

* exploration
* investigation
* decomposition
* hypothesis generation
* planning
* synthesis
* discovering unknowns
* comparing unconstrained possibilities
* reasoning across multiple sources

Deliberation may use a frontier model, human, agent, or future provider.

⸻

ORDO-0405 — Use the narrowest sufficient resolution mode

The system shall prefer the least flexible resolution mode that can correctly resolve the work.

Conceptually:

Compute
   ↑
Decide
   ↑
Deliberate

Work should move downward toward more deterministic resolution as understanding improves.

⸻

ORDO-0406 — No silent mode substitution

If something is explicitly classified as Compute, the system shall not silently invoke a decision model because computation fails.

Failure to compute shall remain observable.

Possible causes include:

* missing information
* implementation defect
* invalid assumptions
* incorrect resolution classification

⸻

5. Evidence

ORDO-0501 — Evidence shall be first-class

Ordo shall represent evidence explicitly.

Evidence shall not exist only inside:

* prompts
* chat history
* textual explanations
* agent memory

⸻

ORDO-0502 — Evidence shall be referenceable

Evidence shall have a stable identity or stable reference where practical.

The model must permit links to external evidence systems such as ROS or research artifacts without depending on those systems.

⸻

ORDO-0503 — Evidence shall preserve provenance

Evidence should be able to identify:

source
source type
time/version where relevant
reference
producing operation where relevant

⸻

ORDO-0504 — Evidence requirements shall be representable

A decision contract may declare required evidence.

Missing required evidence shall be representable separately from:

low confidence
provider failure
negative decision

⸻

ORDO-0505 — Evidence shall not imply truth automatically

Evidence presence does not guarantee:

* correctness
* freshness
* relevance
* completeness

The model must preserve the distinction between evidence availability and evidence validity.

⸻

6. Capabilities and Authority

ORDO-0601 — Capabilities shall represent authority

A capability represents something the current actor or process is permitted to request or perform.

⸻

ORDO-0602 — Confidence shall never grant capability

No confidence value, probability, provider identity, or model quality score may grant a capability.

A model returning:

0.999999 confidence

does not gain permission to perform an otherwise prohibited operation.

⸻

ORDO-0603 — Decisions shall not execute actions by themselves

A decision is information.

A decision may contribute to authorization of a transition.

It shall not implicitly perform the transition.

⸻

ORDO-0604 — Irreversible actions may require stronger policy

Applications must be able to require explicit authority for high-consequence operations regardless of decision confidence.

Examples include:

destructive data operations
production deployment
financial transfer
security changes
irreversible external effects

⸻

7. Obligations

ORDO-0701 — Unresolved required work shall be explicit

An obligation represents required work that has not yet been satisfied.

⸻

ORDO-0702 — Obligations shall survive agent boundaries

An obligation must not disappear because:

* an agent session ended
* provider changed
* execution moved between machines
* one reasoning attempt failed

⸻

ORDO-0703 — Resolution may create obligations

Examples:

AcquireEvidence
HumanReview
RunVerification
ResolveAmbiguity
InvestigateFailure

⸻

8. Typed Decision Contracts

ORDO-0801 — Every bounded AI decision shall have a contract

A decision shall not be represented merely as an arbitrary natural-language prompt.

A decision contract shall establish the legal output space.

⸻

ORDO-0802 — Contracts shall have stable identity

Each decision contract shall possess:

Contract ID
Contract version

Version identity must be sufficient to determine which semantics applied to a historical decision.

⸻

ORDO-0803 — Contracts shall define choices through types

Where practical, choices shall be represented using F# discriminated unions rather than free-form strings.

Example:

type MigrationRisk =
    | Safe
    | RequiresReview
    | Unsafe

⸻

ORDO-0804 — Providers cannot invent choices

If a contract declares:

Safe
RequiresReview
Unsafe

a provider response of:

MostlySafe

is invalid.

It shall result in a provider/contract failure, not automatic interpretation.

⸻

ORDO-0805 — Contract requirements shall be explicit

A decision contract should be capable of declaring:

identifier
version
valid choices
required evidence
decision description
policy references
consequence metadata where useful

Do not add fields without an actual semantic purpose.

⸻

ORDO-0806 — Domain choices belong to the application

Ordo must not define generic business choices such as:

Approved
Denied
Safe
Dangerous

as universal Ordo concepts.

The application owns those choices.

Ordo provides the mechanism for representing them.

⸻

9. Decision Requests

ORDO-0901 — Request context shall be explicit

A decision request shall provide only the context required by the decision contract.

⸻

ORDO-0902 — Avoid accidental context expansion

Providers shall not automatically receive entire repositories, conversations, databases, or histories merely because they are available.

Context shall be deliberately selected.

⸻

ORDO-0903 — State snapshots shall be identifiable

Where decisions depend upon mutable state, the state supplied to the decision should be identifiable through:

version
hash/fingerprint
revision
ETag
commit
other stable concurrency marker

where appropriate.

⸻

ORDO-0904 — Decision results apply to the evaluated state

A decision made against state version A shall not automatically authorize a transition against materially different state version B.

⸻

10. Decision Outcomes

ORDO-1001 — Decisions shall produce structured outcomes

The model should support outcomes conceptually equivalent to:

type DecisionOutcome<‘T> =
    | Decided of DecisionResult<‘T>
    | InsufficientEvidence of EvidenceRequirement list
    | Ambiguous of CandidateDecision<‘T> list
    | RequiresDeliberation of DeliberationReason
    | ProviderFailure of ProviderError

Exact naming may be refined during implementation.

⸻

ORDO-1002 — Do not force a false decision

A provider must not be required to choose one legal answer when available evidence does not support doing so.

⸻

ORDO-1003 — Missing evidence is not low confidence

These conditions must remain distinguishable:

“I have evidence but uncertainty remains.”
“I do not have required evidence.”
“The problem is inherently ambiguous.”
“The provider failed.”
“The task requires broader deliberation.”

⸻

11. Confidence and Probability

ORDO-1101 — Confidence shall be optional

Ordo shall not assume every provider can supply meaningful confidence.

⸻

ORDO-1102 — Confidence provenance shall be recorded

A confidence value shall identify what it represents where relevant.

At minimum, the architecture must distinguish:

provider-reported confidence
derived confidence
empirically calibrated probability

⸻

ORDO-1103 — Do not mislabel self-reported confidence

A normal LLM self-reporting:

0.92

shall not automatically be described as a 92% calibrated probability.

⸻

ORDO-1104 — Calibration belongs to observation

Empirical calibration requires historical outcomes.

ROS or another observational system may calculate calibration.

Ordo may represent calibration metadata but shall not fabricate it.

⸻

ORDO-1105 — Policies may use confidence

Applications may define policies based on confidence.

Example:

>= 0.95 → eligible for automatic transition
0.75–0.95 → verification required
< 0.75 → escalation

Such thresholds belong to domain policy.

They shall not be universal Ordo defaults.

⸻

12. Deliberation Contracts

ORDO-1201 — Deliberation shall be explicit

Open-ended reasoning shall occur through an explicit deliberation request rather than an undocumented provider call.

⸻

ORDO-1202 — Deliberation request

A deliberation request should be capable of representing:

objective
known state
known evidence
unknowns
allowed capabilities
constraints
requested outputs

⸻

ORDO-1203 — Deliberative providers do not gain unlimited authority

An agent performing deliberation shall only receive capabilities appropriate to the request.

⸻

ORDO-1204 — Deliberation shall produce a structured handoff

A deliberative result should be capable of returning:

new evidence
remaining unknowns
proposed decisions
proposed actions
proposed transitions
unresolved obligations
failure to resolve

Natural-language reasoning may accompany these artifacts when useful, but downstream systems must not depend upon parsing prose to discover the authoritative result.

⸻

ORDO-1205 — Deliberation does not directly mutate domain truth

A deliberative provider proposes or produces evidence.

Domain transitions still flow through application authority and Ordo policy.

⸻

13. Escalation

ORDO-1301 — Escalation shall be first-class

Escalation must be represented explicitly rather than implied through retries.

⸻

ORDO-1302 — Decide may escalate to Deliberate

A bounded decision may escalate when:

evidence is insufficient
ambiguity exceeds policy
confidence is below policy
the problem was misclassified
the provider reports inability

⸻

ORDO-1303 — Deliberation may return to Decide

Deliberation may gather enough evidence to transform an open problem into a bounded decision.

⸻

ORDO-1304 — Deliberation may expose Compute

Deliberation may discover that a question is mechanically answerable.

That should permit future execution through Compute.

⸻

ORDO-1305 — Human escalation must be representable

The architecture must permit:

RequiresHumanReview

without treating a human as an error condition.

⸻

ORDO-1306 — Escalation history shall be preserved

The history:

Decide
→ Deliberate
→ Decide
→ Human

must be observable rather than collapsed into only the final answer.

⸻

14. State and Transitions

ORDO-1401 — State remains application-owned

Ordo shall not require applications to inherit from a universal base state.

⸻

ORDO-1402 — Prefer discriminated unions for meaningful state

Where applicable:

type ReviewState =
    | AwaitingEvidence of ...
    | ReadyForDecision of ...
    | RequiresHumanReview of ...
    | Resolved of ...

shall be preferred to loosely coupled boolean flags.

⸻

ORDO-1403 — Illegal states should be structurally difficult

F# types should be used to prevent invalid combinations where practical.

⸻

ORDO-1404 — Transitions shall be explicit

A state-changing operation shall expose:

source requirements
requested transition
policy evaluation
result

⸻

ORDO-1405 — Transition failures shall be typed

Do not reduce transition failure to generic exceptions.

Examples may include:

InvalidState
MissingCapability
MissingEvidence
PolicyRejected
VersionConflict
UnsatisfiedObligation

⸻

ORDO-1406 — External effects remain explicit

A successful state transition does not automatically imply successful external effects.

Where external effects exist, their requested, attempted, successful, and failed states must be distinguishable where consequential.

⸻

15. Policy

ORDO-1501 — Policy belongs near domain authority

Ordo provides policy mechanisms.

Applications define domain policy.

⸻

ORDO-1502 — Policy shall be inspectable

Critical decision and transition policy must not be hidden inside provider prompts.

⸻

ORDO-1503 — Policy evaluation shall be deterministic where possible

Given the same:

state
decision
evidence
capabilities
policy version

policy evaluation should produce the same result.

⸻

ORDO-1504 — Policy versioning

Historical decisions must be traceable to the policy version that evaluated them where policy version materially affects the outcome.

⸻

16. Provider Abstraction

ORDO-1601 — Provider-neutral core

The core decision API shall not contain provider-specific request types.

⸻

ORDO-1602 — Provider identity

Decision results must be able to identify:

provider
model/system identifier where applicable
provider/model version where available
adapter version where materially relevant

⸻

ORDO-1603 — Provider capabilities

Providers may differ in capabilities.

The architecture shall allow capability declarations such as:

supports bounded choices
supports native structured output
supports probabilities
supports confidence
supports parallel decisions
supports local execution

without assuming every provider supports every feature.

⸻

ORDO-1604 — Provider failure remains typed

Provider failures must distinguish useful categories where possible:

Unavailable
Timeout
InvalidResponse
ContractViolation
AuthenticationFailure
RateLimited
UnsupportedCapability
InternalFailure

Do not depend on parsing error strings for normal control flow.

⸻

ORDO-1605 — Provider retry is not automatic domain behavior

Adapters may retry safe transient transport failures.

They must not silently repeat semantic decisions until the provider returns a preferred answer.

⸻

17. Deterministic Providers and Testing Providers

ORDO-1701 — Decisions may be supplied without AI

A provider implementation may be:

human
rule-based
fixture-based
recorded replay
local model
remote model

The contract shall not assume AI.

⸻

ORDO-1702 — Fake provider

Testing shall include a deterministic fake provider capable of returning predefined outcomes.

⸻

ORDO-1703 — Replay provider

Recorded results should be usable in deterministic tests without calling external services.

⸻

18. Replay Support

ORDO-1801 — Requests shall be replayable

A decision request must contain or reference enough stable information that an observational system can re-run the decision later.

⸻

ORDO-1802 — Replay does not execute transitions

Replaying a historical decision against another provider shall not automatically mutate application state.

⸻

ORDO-1803 — Historical semantics must be preserved

Replay must know:

contract version
relevant state snapshot/version
evidence set

A newer contract must not silently masquerade as the historical contract.

⸻

19. Observability Contract

Ordo shall expose the facts required for ROS or another system to observe execution.

Ordo shall not own the analytics platform itself.

⸻

ORDO-1901 — Stable execution identifiers

Resolution operations shall be capable of carrying stable IDs.

⸻

ORDO-1902 — Parent-child relationships

Nested operations should preserve causality.

Example:

Task
  → Deliberation
      → Evidence acquisition
      → Decision
          → Verification

⸻

ORDO-1903 — Timing hooks

Execution boundaries shall allow observation of:

start
completion
duration

without introducing ROS dependencies into Ordo.

⸻

ORDO-1904 — Usage metadata

Provider adapters should expose usage information where the provider makes it available, such as:

input tokens
output tokens
cached tokens
requests
provider cost information

Ordo shall not invent missing values.

⸻

ORDO-1905 — Outcome identity

A decision event and its eventual observed outcome must be separately identifiable.

⸻

20. Decision vs. Outcome

ORDO-2001 — Decision is not outcome

These are different concepts.

Example:

Decision:
Unsafe
Confidence:
0.91
Observed outcome:
Safe

The original decision must remain intact.

⸻

ORDO-2002 — Outcomes may arrive later

Outcome recording must not require an immediate outcome.

⸻

ORDO-2003 — Multiple validation sources may exist

Potential validation sources include:

automated verification
human review
production observation
test result
later domain state
independent provider

Ordo shall not impose a universal hierarchy among these.

⸻

21. Verification

ORDO-2101 — Decisions may require verification

Policies shall be capable of requiring secondary verification.

⸻

ORDO-2102 — Verification shall not be conflated with repetition

Asking the same provider the same question again is not automatically independent verification.

⸻

ORDO-2103 — Verification mechanism shall be identifiable

Where relevant, the system should distinguish:

same-provider retry
independent provider
deterministic verification
human verification
behavioral verification

⸻

22. Versioning

ORDO-2201 — Version anything whose semantic change affects historical interpretation

At minimum consider versioning:

DecisionContract
Policy
Evidence schema where necessary
Provider adapter
serialization schema

⸻

ORDO-2202 — Avoid unnecessary version proliferation

Version only semantics that materially matter.

Do not version every implementation detail.

⸻

ORDO-2203 — Backward compatibility

Changes to serialized public contracts must follow a deliberate compatibility strategy.

No silent breaking change is permitted.

⸻

23. Serialization

ORDO-2301 — Serialization shall be explicit at boundaries

Domain types shall not be designed primarily around a serializer.

⸻

ORDO-2302 — Stable wire representations

Where Ordo records cross process/repository boundaries, serialized representations must have:

schema identity/version
stable field meanings
defined optionality

⸻

ORDO-2303 — Unknown fields and versions

Compatibility behavior for newer fields or unsupported schema versions must be deliberate and tested.

⸻

24. Persistence

ORDO-2401 — Ordo Core shall not require a database

Persistence shall be an external concern.

⸻

ORDO-2402 — Storage-neutral records

Ordo records should be capable of being stored in:

files
GitHub
SQL
document storage
ROS stores
memory

without changing semantic meaning.

⸻

25. Security

ORDO-2501 — Secrets shall never be domain evidence by default

API keys, tokens, passwords, and credentials shall not be embedded into decision records.

⸻

ORDO-2502 — Provider adapters shall receive credentials through explicit secure boundaries

Credentials shall not be stored in core contracts.

⸻

ORDO-2503 — Context minimization

Only information required for a decision should be supplied to an external provider.

⸻

ORDO-2504 — Sensitive data policies remain application-owned

Applications determine whether particular evidence may leave a trust boundary.

⸻

26. Determinism and Reproducibility

ORDO-2601 — Deterministic functions shall remain deterministic

No hidden AI or nondeterministic external call shall occur from a function documented as deterministic.

⸻

ORDO-2602 — Nondeterminism shall be explicit

Provider-backed decisions are inherently nondeterministic unless proven otherwise.

Their outputs must not be represented as deterministic calculations.

⸻

ORDO-2603 — Reproducibility metadata

Where available, preserve inputs required to understand or approximate historical provider execution.

Do not claim perfect reproduction when provider behavior cannot be reproduced exactly.

⸻

27. Error Handling

ORDO-2701 — Expected errors shall be data

Expected domain, transition, provider, and policy failures should normally use typed results rather than exceptions.

⸻

ORDO-2702 — Exceptions remain for exceptional failures

Exceptions may represent genuinely unexpected programming/runtime failures.

⸻

ORDO-2703 — Do not collapse errors

The implementation must preserve distinctions among:

missing evidence
invalid state
lack of capability
policy rejection
provider outage
provider contract violation
serialization failure
version conflict
deliberation required
human review required

⸻

28. Concurrency

ORDO-2801 — Decisions must be state-relative

Where concurrent change matters, decision acceptance shall verify that the relevant state has not materially changed.

⸻

ORDO-2802 — Stale decisions may be rejected

A valid decision generated against stale state may become unusable.

This is not a provider error.

⸻

29. Idempotency

ORDO-2901 — Decision evaluation should be safe to retry

Requesting a decision should not directly create an irreversible application effect.

⸻

ORDO-2902 — Transition execution and decision execution remain separate

This separation allows replay, experimentation, and retries without repeated side effects.

⸻

30. Human Participation

ORDO-3001 — Human is a legitimate resolution source

Human resolution is not an exceptional failure mode.

⸻

ORDO-3002 — Human decisions may use the same contracts

Where useful, a human may answer a DecisionContract<‘T>.

This improves comparability between machine and human decisions.

⸻

ORDO-3003 — Human override shall preserve original decision

An override does not erase the original machine decision.

⸻

31. Migration of Intelligence

Ordo shall explicitly support the long-term pattern:

Deliberate
    ↓
Decide
    ↓
Compute

⸻

ORDO-3101 — Deliberation may reveal reusable bounded decisions

Repeated open-ended reasoning may become a candidate decision contract.

⸻

ORDO-3102 — Decisions may reveal deterministic rules

Repeated decisions whose outcomes are completely explained by stable deterministic conditions may become computation candidates.

⸻

ORDO-3103 — Migration shall not happen automatically

Observed regularity may create a recommendation or research hypothesis.

It shall not automatically rewrite domain behavior.

⸻

32. Relationship to ROS

ORDO-3201 — No dependency from Ordo to ROS

Executable Ordo packages shall not depend on ROS.

⸻

ORDO-3202 — ROS may depend on Ordo

ROS may consume Ordo contracts and types.

⸻

ORDO-3203 — ROS owns execution analytics

ROS is expected to record and eventually analyze:

resolution mode
provider
model
latency
cost
confidence
escalation
verification
decision
outcome
override

⸻

ORDO-3204 — ROS owns empirical provider comparison

Accuracy, calibration, cost comparisons, provider selection experiments, and replay analytics belong to ROS or another observational layer.

They do not belong in Ordo Core.

⸻

33. Relationship to Existing Ordo Agent Scripts

ORDO-3301 — Existing scripts remain valid

Introducing executable primitives must not unnecessarily invalidate existing Ordo agent scripts.

⸻

ORDO-3302 — Scripts shall gradually consume executable concepts

As primitives mature, scripts should instruct agents to use declared:

states
contracts
capabilities
evidence
obligations
verification

rather than re-inventing them conversationally.

⸻

ORDO-3303 — Methodology still governs code generation

Using the Ordo library does not mean an application automatically conforms to Ordo.

An agent must still follow:

structural locality
context discipline
verification requirements
construction method
boundary discipline
repository navigation rules
other established Ordo rules

⸻

34. Agent Behavior Requirements

ORDO-3401 — Agents must inspect declared contracts before inventing new behavior

If an application already defines a decision or transition contract, the agent must use it rather than creating an alternate mechanism.

⸻

ORDO-3402 — Agents shall prefer mechanical discovery

Before invoking deliberative reasoning for a question, agents should determine whether the answer can be obtained mechanically.

⸻

ORDO-3403 — Agents shall not treat prose as stronger authority than executable state

When canonical state/contracts disagree with generated prose, the discrepancy must be surfaced.

The agent shall not silently choose whichever interpretation is convenient.

⸻

35. API Design

ORDO-3501 — Small API surface

The public API shall remain intentionally small.

Do not expose implementation details merely because they exist.

⸻

ORDO-3502 — Prefer domain-oriented names

Avoid generic infrastructure vocabulary when a domain concept exists.

⸻

ORDO-3503 — Avoid inheritance-heavy APIs

Prefer:

records
discriminated unions
functions
small interfaces at boundaries

⸻

ORDO-3504 — Exhaustive matching

Important state and result unions should benefit from F# exhaustive pattern matching.

Compiler warnings for incomplete cases shall be treated seriously.

⸻

36. Testing Requirements

ORDO-3601 — Unit tests are mandatory

Core behavior shall have thorough unit coverage.

⸻

ORDO-3602 — Decision contract tests

Test at minimum:

valid choices
invalid choice rejection
missing evidence
ambiguous outcome
provider failure
deliberation escalation
confidence absent
confidence present

⸻

ORDO-3603 — Transition tests

Test:

valid state transition
invalid source state
missing capability
missing evidence
policy rejection
stale state/version conflict

⸻

ORDO-3604 — Provider conformance tests

Every provider adapter shall pass a common contract test suite.

⸻

ORDO-3605 — Serialization tests

Public serialized contracts shall have round-trip and compatibility tests.

⸻

ORDO-3606 — Replay tests

Historical decision inputs must be replayable without executing application transitions.

⸻

ORDO-3607 — No live-provider requirement for normal unit tests

The normal test suite shall not require network access or paid model calls.

⸻

37. Verification Requirements

ORDO-3701 — Build must remain clean

The repository must build successfully.

⸻

ORDO-3702 — Existing tests must continue to pass

No existing passing behavior may be broken without an explicit documented migration requirement.

⸻

ORDO-3703 — No warning normalization

New compiler warnings shall not simply be suppressed to obtain a green build.

⸻

ORDO-3704 — Architecture checks

Automated checks should verify important dependency boundaries where practical, especially:

Core → no provider SDK
Core → no ROS
Decisions → no concrete provider
ROS → may reference Ordo
Provider adapters → may reference provider SDK

⸻

38. Documentation

ORDO-3801 — Architecture documentation

Documentation shall explain:

why Ordo has executable primitives
what remains methodology
authority boundaries
Compute / Decide / Deliberate
decision vs transition
decision vs outcome
confidence vs calibration
Ordo vs ROS

⸻

ORDO-3802 — Examples

Provide minimal executable examples for:

Compute
Decide
Deliberate
Escalation
Human review
Transition policy
Provider adapter
Replay

⸻

ORDO-3803 — Anti-examples

Documentation shall explicitly show incorrect patterns, including:

sending deterministic calculation to an LLM
allowing model confidence to grant authority
free-form decisions when bounded choices exist
parsing prose to determine authoritative output
silent provider fallback
silently using stale decisions
treating self-reported confidence as calibrated probability

⸻

39. Compatibility and Adoption

ORDO-3901 — Incremental adoption

Existing applications shall not need to rewrite their entire architecture to begin using executable Ordo primitives.

⸻

ORDO-3902 — Vertical-slice introduction

The first implementation shall prove one narrow end-to-end decision path before broad adoption.

⸻

ORDO-3903 — No forced dependency

Applications that only use the existing Ordo methodology shall not be forced to install executable Ordo packages.

⸻

40. Packaging

ORDO-4001 — Package only stable reusable surfaces

Do not publish unstable internal projects merely because they exist.

⸻

ORDO-4002 — Package metadata

Published packages shall contain appropriate:

version
license
README
repository information
release notes

according to existing Echelon Foundry conventions.

⸻

ORDO-4003 — Initialization tooling

If Ordo already supports or is planned to support:

npx <package> init

or equivalent repository initialization mechanisms, executable Ordo additions must integrate without creating a competing setup path.

The F# runtime library and repository initialization tooling are separate concerns.

⸻

41. Performance

ORDO-4101 — Core overhead shall be negligible

Ordo Core should introduce very little computational overhead compared with domain operations.

⸻

ORDO-4102 — Avoid reflection-heavy design

Reflection, runtime code generation, or dynamic dispatch shall not be introduced where straightforward typed F# can solve the problem.

⸻

ORDO-4103 — Decision-provider latency shall remain observable

Do not hide provider latency behind abstractions that prevent measurement.

⸻

42. Cost

ORDO-4201 — Ordo Core shall not require paid services

⸻

ORDO-4202 — Provider cost shall be observable where available

Provider adapters should surface raw provider usage information rather than making hidden assumptions about pricing.

ROS may calculate normalized cost.

⸻

43. Provider Selection

ORDO-4301 — Initial provider selection shall be explicit

Initial implementation shall use explicitly configured providers.

⸻

ORDO-4302 — No autonomous optimization in first release

Ordo shall not autonomously choose a provider based on historical performance in the initial implementation.

⸻

ORDO-4303 — Architecture shall permit future routing

The provider abstraction must allow ROS or another orchestration layer to eventually select a provider without changing application decision contracts.

⸻

44. Data Collection for Future Research

ORDO-4401 — Preserve training-useful structure

Without building a training system, execution records should make it possible to later associate:

state
evidence
decision contract
provider
decision
confidence
verification
outcome
override

⸻

ORDO-4402 — Do not collect unnecessary data

Potential future model training is not justification for indiscriminate capture of sensitive or irrelevant information.

⸻

ORDO-4403 — Research traceability

Where a decision or architectural change is part of an experiment, Ordo should permit external experiment/evidence identifiers to be attached without depending on the research system.

⸻

45. First Vertical Slice

The first executable implementation shall prove the following sequence:

Domain state
    ↓
DecisionContract<‘T>
    ↓
DecisionRequest<‘T>
    ↓
IDecisionProvider / equivalent provider boundary
    ↓
DecisionOutcome<‘T>
    ↓
Policy evaluation
    ↓
Transition accepted / rejected / escalated
    ↓
Observation event emitted

⸻

ORDO-4501 — One bounded decision

Implement exactly one meaningful bounded-decision example before generalizing the architecture further.

⸻

ORDO-4502 — At least two provider implementations

The first vertical slice should support:

1. deterministic/fake provider for testing;
2. one real provider adapter.

A second real AI provider may be added when ROS replay/comparison work begins.

⸻

ORDO-4503 — No provider implementation in Core

The real provider integration shall prove that the abstraction works without contaminating the core.

⸻

ORDO-4504 — One escalation path

The first vertical slice must demonstrate:

Decide → RequiresDeliberation

or:

Decide → RequiresHumanReview

⸻

ORDO-4505 — One state/version protection case

The vertical slice shall demonstrate that a decision generated against stale state cannot silently authorize a transition against changed state.

⸻

46. Initial F# Shape

The implementation should begin conceptually near this level of complexity:

type ResolutionMode =
    | Compute
    | Decide
    | Deliberate
type EvidenceId =
    | EvidenceId of string
type EvidenceRequirement =
    {
        Id : string
        Description : string
    }
type Evidence =
    {
        Id : EvidenceId
        Source : string
        Reference : string option
    }
type ConfidenceSource =
    | ProviderReported
    | Derived
    | EmpiricallyCalibrated
type Confidence =
    {
        Value : float
        Source : ConfidenceSource
    }
type DecisionContractId =
    | DecisionContractId of string
type DecisionContract<‘choice> =
    {
        Id : DecisionContractId
        Version : int
        Choices : ‘choice list
        RequiredEvidence : EvidenceRequirement list
    }
type DecisionResult<‘choice> =
    {
        Choice : ‘choice
        Confidence : Confidence option
        Evidence : Evidence list
        Provider : string
    }
type DecisionOutcome<‘choice> =
    | Decided of DecisionResult<‘choice>
    | InsufficientEvidence of EvidenceRequirement list
    | Ambiguous of DecisionResult<‘choice> list
    | RequiresDeliberation of string
    | ProviderFailure of string

This is illustrative, not a command to copy these types blindly.

The implementation agent must refine the types according to existing Ordo conventions and repository evidence.

The design must remain comparably small and explicit unless additional complexity is demonstrably necessary.

⸻

47. Implementation Rules

ORDO-4701 — Follow Ordo while building Ordo

The implementation itself shall follow all applicable existing Ordo/SDE rules.

That includes existing requirements around:

state-directed construction
structural locality
repository navigation
semantic authority
verification
context escalation
feature manifests where applicable
boundary discipline
evidence collection

⸻

ORDO-4702 — Use ROS where already required by the project

If the Ordo repository already uses ROS for planning/execution, this implementation shall use the current ROS mechanism rather than creating a parallel task system.

⸻

ORDO-4703 — Preserve current behavior

Before modifications:

build
test
record baseline
identify current architecture
create appropriate recovery point/tag if required by existing process

After modifications:

build
test
architecture verify
run new tests
compare against baseline

⸻

ORDO-4704 — No speculative refactor

Do not refactor unrelated Ordo code while implementing this feature merely because an agent prefers another style.

⸻

ORDO-4705 — Do not introduce abstractions before evidence requires them

Specifically avoid premature:

base classes
generic repositories
service locators
workflow engines
event buses
plugin frameworks
provider registries
DSLs
code generators
dependency injection containers

⸻

48. Backward Compatibility

ORDO-4801 — Existing methodology remains authoritative

Existing documents and agent scripts continue functioning during migration.

⸻

ORDO-4802 — No hard cutover

Executable Ordo shall be introduced incrementally.

⸻

ORDO-4803 — Breaking change requires explicit migration

If a breaking change becomes unavoidable, implementation must provide:

reason
affected surfaces
migration path
compatibility period where practical
tests proving the migration

⸻

49. Naming

ORDO-4901 — Use precise engineering terminology

Public concepts should prefer:

Compute
Decide
Deliberate
DecisionContract
Evidence
Capability
Obligation
Policy
Transition

over vague anthropomorphic terms.

⸻

ORDO-4902 — Fast/slow remains conceptual inspiration

Documentation may explain the relationship to fast/slow cognition.

The API shall not assume psychological claims are software semantics.

⸻

50. Definition of Done for the First Ordo Implementation

The first executable-Ordo milestone is complete only when all of the following are true:

1. Existing Ordo behavior still works.
2. The solution builds cleanly.
3. Existing tests remain passing.
4. New unit tests pass.
5. Compute, Decide, and Deliberate are represented explicitly.
6. A typed bounded decision contract exists.
7. Invalid provider choices cannot silently enter the domain.
8. Missing evidence is distinguishable from uncertainty.
9. Confidence is optional and has provenance.
10. Self-reported confidence is not represented as empirical calibration.
11. Decision and outcome are separate concepts.
12. A decision does not itself execute a transition.
13. Capability remains independent of confidence.
14. State/version checks can invalidate stale decisions.
15. A provider-neutral interface exists.
16. A deterministic/fake provider exists.
17. At least one real provider can satisfy a bounded decision contract.
18. Provider failure is typed.
19. At least one escalation path works.
20. Deliberation is represented as an explicit contract.
21. Human review is representable.
22. Observation events expose sufficient information for ROS.
23. Ordo has no dependency on ROS.
24. Provider-specific code does not leak into Ordo Core.
25. Core has minimal external dependencies.
26. Normal unit tests require no paid/network AI access.
27. Replay can occur without executing a domain transition.
28. Historical contract versions remain identifiable.
29. Documentation explains the architecture and authority boundaries.
30. At least one complete example demonstrates the intended usage.
31. At least one anti-example demonstrates behavior the architecture forbids.
32. No unnecessary framework has been introduced.
33. No unrelated refactoring has been performed.
34. The implementation follows Ordo’s own existing engineering rules.
35. The resulting implementation is small enough that another engineer or autonomous agent can understand the core execution model without requiring hidden conversational context.

⸻

51. Explicitly Deferred Until Later Passes

The architecture must allow these capabilities, but the first implementation shall not attempt to complete them unless subsequently required:

ROS provider benchmarking
empirical calibration curves
automatic provider recommendations
automatic provider routing
decision-cost optimization
Deliberate → Decide candidate detection
Decide → Compute candidate detection
specialized local models
Jev integration
training an Echelon decision model
RL/RLCD experimentation
decision corpus export
adaptive thresholds
cross-application decision libraries
decision dashboards
automatic contract discovery
automatic policy modification
automatic model retraining

These remain future requirements, not hidden implementation expectations.

⸻

52. Governing Architectural Principle

The executable Ordo design shall preserve this ordering of authority:

DOMAIN STATE
      │
      ▼
DOMAIN CONTRACT
      │
      ▼
RESOLUTION
 ┌────┼─────────┐
 │    │         │
Compute Decide Deliberate
 │    │         │
 └────┼─────────┘
      ▼
STRUCTURED RESULT
      │
      ▼
EVIDENCE + POLICY + CAPABILITY
      │
      ▼
LEGAL TRANSITION
      │
      ▼
NEW DOMAIN STATE

The intelligence provider is deliberately absent from the authority chain.

A provider supplies information.

It does not own truth.

It does not own capability.

It does not own policy.

It does not own state.

It does not decide what transitions are legal.

That separation is fundamental to executable Ordo.

⸻

53. Core Research Hypotheses Enabled by This Architecture

The implementation should make it possible to test, without hard-coding the expected result, whether:

explicit resolution modes reduce unnecessary deliberation;
bounded typed decisions improve reliability over free-form agent decisions;
separating judgment from execution reduces invalid actions;
explicit evidence requirements improve decision quality;
confidence-aware escalation reduces failures;
different providers have materially different accuracy/cost/calibration profiles for different decision contracts;
repeated deliberation can be converted into bounded decisions;
repeated bounded decisions can be converted into deterministic computation;
explicit state makes this movement from Deliberate → Decide → Compute mechanically observable.

These are hypotheses to measure, not truths that the implementation should assume.

⸻

54. Final Constraint

The implementation shall remain simple enough that the following statement remains true:

Ordo provides a small number of strong primitives that make authority, state, evidence, uncertainty, and legal action explicit. It does not attempt to become the application.

When a proposed feature violates that principle, it requires explicit justification before inclusion.