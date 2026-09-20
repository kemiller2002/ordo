---
id: DF-SDE-2026-0011
title: Clarify Ordo Capability as semantic authority, not authentication or object capability security
status: accepted
type: decision-record
created: 2026-09-20
updated: 2026-09-20
tags: [architecture, ordo, gh-18, next-pass, capability, authority]
supersedes: []
superseded_by: []
related_documents:
  - research/packages/RP-SDE-2026-0004--ordo-next-pass-requirements.md
  - research/evidence/EV-SDE-2026-0011--ordo-next-pass-pre-upgrade-baseline.md
  - doctrine/DECISION-AND-EVIDENCE-SEMANTICS.md
  - doctrine/FOUR-TIER-ARCHITECTURE.md
---

# DF-SDE-2026-0011

## Context

Executable Ordo already keeps Capability independent from confidence and provider output, but the term can be read as a stronger security claim than the type provides.

## Decision

An Ordo `Capability` is a **host/application-supplied semantic authority prerequisite**.

It is not:

- an authentication token;
- an object-capability reference;
- a cryptographic credential;
- a signature;
- proof of identity;
- proof that the host granted the authority correctly.

The host/application establishes identity and authenticates/authorizes the actor using whatever security mechanism the system requires, then supplies the semantic capability set to Ordo.

Providers, confidence values, evidence, and decisions cannot mint capabilities.

## Four-tier placement

Security/authentication mechanisms live at application/host boundaries. Tier 1/2 receives only the semantic authority fact it needs to evaluate a legal transition.

## Consequences

This requires documentation/type-comment clarification, not a security framework or behavioral redesign. Existing architecture already enforces the key separation.
