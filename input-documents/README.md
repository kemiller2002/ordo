# Input documents

Externally supplied source documents that this repository executed. They are
kept verbatim, as provenance for work that has already been done — not as
governance.

## What is here

| File | What it is |
|---|---|
| `structured-requiredments-1.md` | *Ordo Executable Intelligence Requirements* — Pass 1. Requirement IDs `ORDO-nnnn`. |
| `ordo-executable-intelligence-requirements-pass-2.txt` | Pass 2. Hardening, edge cases and failure modes. IDs `ORDO-5501`–`ORDO-111xx`. |
| `ordo-executable-intelligence-requirements-pass-3.txt` | Pass 3. Consolidation and the v0.1 implementation scope. IDs `ORDO-3-nnn`. |
| `ordo-executable-intelligence-v0.1-agent-implementation-script.txt` | The implementation script that directed the v0.1 build. |

The filename `structured-requiredments-1.md` carries a typo from the source
and is left as it arrived; renaming it would break the provenance it exists
to provide.

## What authority these have

They were **explicit user instruction** when they were issued, which
`AGENTS.md` places at the top of the authority order, and executable Ordo
v0.1 was built from them.

They are **not** canonical governance now, and they are not a standing
contract. Requirements documents record what was asked for; they do not
record what was built, which of their requirements were deferred, or how the
conflicts between the three passes were resolved. For any of those questions
the accepted records are authoritative:

- [`research/decisions/DF-SDE-2026-0006`](../research/decisions/DF-SDE-2026-0006--introduce-executable-ordo-primitives.md)
  — the accepted architecture decision, including the four conflicts between
  passes that were resolved explicitly.
- [`docs/architecture/executable-ordo-traceability.md`](../docs/architecture/executable-ordo-traceability.md)
  — every requirement's status and class, and what is deferred.
- [`docs/architecture/executable-ordo.md`](../docs/architecture/executable-ordo.md)
  — the architecture as built.

Where a document here disagrees with one of those, the accepted record wins
and the document here is the historical input, not a correction to it.

## Provenance

Brought onto `main` under `WI-0039`, byte-identical to the blobs the
repository owner committed on branch `claude/ros-bootstrap-init-ao9vu4`:
`952bb64` ("add decisioning") for Passes 1–3, `90584dd`
("agent instructions") for the implementation script.

## Adding to this directory

This is a landing place for externally supplied source material, not a
working area. A document belongs here only if it was supplied from outside
the repository and something in the repository cites it. Anything this
repository authors itself belongs in `research/`, `doctrine/`, `method/` or
`docs/` under the routing in
[`context/ARCHITECTURE.md`](../context/ARCHITECTURE.md).
