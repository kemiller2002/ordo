# REL-ORDO-001: Versioned constructibility and decision contracts

Priority: P0
Owner system: Ordo
Consumers: Praxis, Dokimos

## Requirements
- ORD-REL-001: Publish a canonical, deterministic, versioned machine-readable contract for requirements, dependencies, decisions, conflicts, ownership and expected verification.
- ORD-REL-002: Validate that each referenced decision ID exists, has the correct node type and cohort, and satisfies required approval/resolution rules.
- ORD-REL-003: Detect unresolved or cyclic dependencies, duplicate requirement IDs, missing ownership, unsupported schema versions, and contradictory decisions before work starts.
- ORD-REL-004: Define explicit supported-version negotiation and migration behavior; never silently reinterpret incompatible contracts.
- ORD-REL-005: Emit stable contract digest and traceability links from every requirement to its evidence obligations.
- ORD-REL-006: Publish contract fixtures including valid, invalid, backward-compatible, and cross-version cases consumable by Praxis and Dokimos.
- ORD-REL-007: Make validation deterministic and side-effect-free; no new runtime dependency without justification.

## Acceptance
Invalid decision IDs and unsupported versions fail at intake with machine-readable diagnostics. The same input yields the same normalized contract and digest. Consumer compatibility tests pass.
