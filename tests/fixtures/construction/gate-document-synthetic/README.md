# Synthetic gate yard fixture

This is test input for plan vocabulary 7 (document persistence). It is not
the requirements of any product. The sources are those of `../gate-synthetic`,
plus `gate-yard.txt` (GATE-6). Each `contentDigest` is the SHA-256 of its
source file.

| File | Golden |
|---|---|
| `plan.v7-durable.json` | `GateYard`: durable idempotency; a save against a moved yard is refused (`YardMoved`); a missing gate reports `NoSuchGate`; a stale gate revision reports `StaleEntry` |
| `plan.v7-session.json` | `SessionYard`: session idempotency; a save conflict is escalated |
| `plan.v7-open.json` | `OpenYard`: every choice left unresolved |
| `construction-decisions-v5.json` | decision records `/5` (state model, codes with wire text, a durable document that refuses save conflicts and reports reconcile conflicts); its default plan generates the same document tier as `GateYard` |

`DEC-GATE` stands in for an approval. It is a test input, not an approval.
