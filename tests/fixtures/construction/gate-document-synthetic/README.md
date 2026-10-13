# Synthetic gate yard fixture

This is test input for plan vocabulary 7 (document persistence). It is not
the requirements of any product. The sources are those of `../gate-synthetic`,
plus `gate-yard.txt` (GATE-6). Each `contentDigest` is the SHA-256 of its
source file.

| File | Golden |
|---|---|
| `plan.v7-durable.json` | `GateYard`: durable idempotency; a save against a moved yard is refused (`YardMoved`); a missing gate reports `NoSuchGate`; a stale gate revision reports `StaleEntry` |
| `plan.v7-session.json` | `SessionYard`: session idempotency; a save conflict is escalated |
| `plan.v7-open.json` | `OpenYard`: both choices left unresolved |

`DEC-GATE` stands in for an approval. It is a test input, not an approval.
