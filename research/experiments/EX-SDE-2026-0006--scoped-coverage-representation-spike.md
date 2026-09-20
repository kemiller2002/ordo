---
id: EX-SDE-2026-0006
title: Scoped coverage representation spike against Strata and Time Tracking
status: completed
type: experiment-record
created: 2026-09-20
updated: 2026-09-20
tags: [ordo, coverage, gh-22, strata, time-tracking]
related_documents:
  - research/decisions/DF-SDE-2026-0008--adopt-scoped-context-coverage-semantics.md
  - research/decisions/DF-SDE-2026-0014--represent-context-coverage-as-first-class-claims.md
---

# EX-SDE-2026-0006

## Question

Should executable Ordo represent scoped context coverage as:

A. a first-class typed coverage claim; or
B. ordinary Evidence whose content encodes coverage?

The comparison uses current repository behavior, not invented examples.

## Corpus

### Strata

Strata.Semantic.AnalysisScope already demonstrates independent completeness dimensions:

- relations may be Complete;
- relation_access may be Partial because a visible relation is unreadable;
- grants or rls_policies may be NotRequested;
- absence claims are blocked unless the relevant observation scope is complete.

EV-STRATA-2026-E7A9 proves that visible metadata and usable data are different dimensions. EV-STRATA-2026-D3A8 proves that not compared cannot be rendered as no difference.

### Time Tracking Application

The domain tests demonstrate:

- an unchanged historical reference may remain valid after its project/tag becomes inactive;
- assigning a newly inactive reference is rejected;
- restore re-validates against current state and rejects a newly introduced overlap;
- a failed reference.json pull does not start reconciliation;
- a successful pull establishes the reference catalog used by reconciliation;
- a 404 means nothing published yet, which is a domain-specific known state, not a transport failure.

These cases separate coverage from ordinary domain state. Project is inactive is state. Did we successfully establish the current project catalog is coverage.

## Prototype A: first-class claim

Conceptual shape:

    CoverageScope
    CoverageStatus = Complete | Partial | Unknown
    ContextCoverageClaim = { Scope; Status; Provenance : EvidenceId list }
    CoverageRequirement = { Scope; Description }

A contract may require Complete coverage for selected scopes. Other claims may remain Partial or Unknown and still travel with the request.

## Prototype B: typed Evidence content

Conceptual shape:

    Evidence { Kind = Direct; Content = { kind = coverage; scope = ...; status = partial } }

To enforce a contract requirement, Ordo would have to either:

1. parse domain JSON content inside Evidence; or
2. add a special typed coverage schema inside Evidence.

Option 1 weakens the typed boundary. Option 2 recreates a first-class coverage type indirectly.

## Scenario comparison

| Scenario | First-class claim | Evidence-only |
|---|---|---|
| Strata relations Complete + relation_access Partial | naturally carries two typed claims | requires parsing two evidence payloads |
| Strata NotRequested/unknown dimension | maps to Unknown while evidence preserves why | status meaning is hidden in content |
| Strata false-clean protection | contract can require Complete comparison scope | enforcement requires inspecting evidence content |
| Time Tracking failed reference pull | explicit Unknown reference-catalog scope | indistinguishable without coverage-specific content parsing |
| Time Tracking successful reference pull | explicit Complete reference-catalog scope | must infer a semantic status from evidence payload |
| Historical inactive reference | correctly remains domain state, not coverage | coverage-shaped evidence risks absorbing domain semantics |
| Restore overlap check | can require Complete current-activity scope | contract cannot mechanically demand it without a coverage parser |
| Provider prompt honesty | provider receives status separately from evidence content | model must infer coverage from arbitrary evidence prose/JSON |

## Findings

Evidence answers: what observation or fact do we have?

Coverage answers: how completely did the declared observation method cover this named scope?

A coverage claim can cite evidence without becoming evidence itself.

If Ordo leaves coverage inside arbitrary Evidence content, it cannot mechanically enforce Complete, Partial, and Unknown. If Ordo adds a special parser/type for that content, it has recreated the first-class representation with more indirection.

Complete is never inferred from successful provider execution, context size, confidence, or lack of errors.

Partial means known incomplete coverage. Unknown means sufficient coverage has not been established.

Strata's richer Inaccessible and NotRequested states remain domain facts. At the Ordo boundary they may justify Partial or Unknown while their exact reason remains in the cited evidence.

## Result

Promote Prototype A: first-class typed coverage claims with evidence references as provenance.

Do not create:

- a global request completeness flag;
- a universal Strata-like taxonomy inside Ordo;
- a coverage confidence score;
- automatic Complete inference;
- a graph or coverage propagation engine.

## Implementation consequences

- add a small Core coverage vocabulary;
- allow contracts to require Complete coverage for named scopes;
- carry multiple claims on a DecisionRequest;
- refuse required Partial, Unknown, or missing coverage before provider execution;
- expose coverage explicitly to the provider boundary;
- keep evidence records as the provenance source for how the claim was established.