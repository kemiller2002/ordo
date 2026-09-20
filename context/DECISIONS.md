# State Directed Engineering decisions

Material decisions use `DF-` records under `research/decisions/`. This
compact table is a navigation view, not a replacement for those records.

| Date | Decision | Status | Rationale | Record |
|---|---|---|---|---|
| 2026-09-02 | Use ROS 1.2.1-main.16.1 as a measured greenfield pilot. | provisional | Test portability and operational value on a real beginning project. | Not yet promoted to a `DF-` record |
| 2026-09-02 | Adopt State-Directed Engineering as a name and concept distinct from State Programming. | accepted | Separates paradigm claims from methodology claims so one can be revised without relitigating the other. | `research/decisions/DF-SDE-2026-0001` |
| 2026-09-02 | Treat corrected Experiment 3 (commit `8ac05fd`) as authoritative over any earlier draft or unlocated transition artifact. | accepted | Ensures SDE cites the corrected figures (76 log entries, incomplete Condition B telemetry, 4.0 BCA third replication), not a superseded or unlocated summary. | `research/decisions/DF-SDE-2026-0002` |
| 2026-09-02 | Defer solving the 4.0 Boundary Change Amplification finding during this migration. | accepted | Avoid conflating architecture change with methodology bootstrap, per the migration mission's explicit instruction. | `research/decisions/DF-SDE-2026-0003` |
| 2026-09-05 | Adopt SDE v0.2 structural-locality, deterministic-navigation, and bounded-context contracts while keeping their claimed benefits experimental. | accepted | Makes the research notes operational without laundering engineering rationale into validated outcome evidence. | `research/decisions/DF-SDE-2026-0004` |
| 2026-09-16 | Adopt Ordo as the public methodology name while retaining SDE as the descriptive engineering approach. | accepted | Gives the methodology a stable name without discarding the descriptive term. | `research/decisions/DF-SDE-2026-0005` |
| 2026-09-18 | Introduce executable Ordo primitives with provider-neutral Core/Decisions and provider-specific adapters. | accepted | Makes bounded decision, evidence, authority, obligation and transition semantics executable without letting a provider authorize change. | `research/decisions/DF-SDE-2026-0006` |
| 2026-09-20 | Use minimal, versioned, semantically complete state views and fingerprint the full selected view. | accepted | Keeps stale-state safety simple while leaving relevance with the domain. | `research/decisions/DF-SDE-2026-0007` |
| 2026-09-20 | Make context coverage scoped and preserve Complete, Partial and Unknown as distinct semantics. | accepted | Real repositories are complete in some dimensions and incomplete in others. | `research/decisions/DF-SDE-2026-0008` |
| 2026-09-20 | Validate Derived evidence dependencies as an acyclic closed set. | accepted | Completes the existing provenance model without general graph infrastructure. | `research/decisions/DF-SDE-2026-0009` |
| 2026-09-20 | Treat unknown external effect outcome as a reconciliation state, not failure. | accepted | Four independent applications show blind retry can be unsafe. | `research/decisions/DF-SDE-2026-0010` |
| 2026-09-20 | Define Ordo Capability as semantic authority supplied by the host, not authentication/security proof. | accepted | Preserves confidence/authority separation without overstating security semantics. | `research/decisions/DF-SDE-2026-0011` |
| 2026-09-20 | Require scoped provenance for reusable negative knowledge. | accepted | "Not found" is meaningful only with scope, method, time/state identity and coverage. | `research/decisions/DF-SDE-2026-0012` |
| 2026-09-20 | Release the governed methodology as an additive SDE minor version while versioning Ordo wire semantics independently. | accepted | Methodology distribution and persisted decision semantics have different compatibility boundaries. | `research/decisions/DF-SDE-2026-0013` |
