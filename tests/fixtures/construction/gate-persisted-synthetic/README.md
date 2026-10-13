# Synthetic persisted gate fixture

This is test input for plan vocabulary 6 (persisted instances). It is not the
requirements of any product. The sources are those of `../gate-synthetic`,
plus `gate-stored.txt` (GATE-5). Each `contentDigest` is the SHA-256 of its
source file.

| File | Golden |
|---|---|
| `plan.v6-durable.json` | `StoredGate`: durable idempotency; a stale write is refused (`StaleRevision`); a missing gate reports `NoSuchGate` |
| `plan.v6-session.json` | `SessionGate`: session idempotency; a stale write is escalated |

`DEC-GATE` stands in for an approval. It is a test input, not an approval.
