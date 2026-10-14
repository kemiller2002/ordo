# Synthetic gate fixture

This is test input for plan vocabulary 4 (typed refusal codes and branching
transitions). It is not the requirements of any product. The four sources in
`sources/` were written for these tests. `source-manifest.json` and
`blueprint.json` were derived from them, and each `contentDigest` is the
SHA-256 of its source file.

| File | Used by |
|---|---|
| `plan.v4.json` | hand-authored vocabulary 4 plan; the `Gate` golden in `tests/Ordo.Construction.Tests/Golden/gate-synthetic-v4` |
| `construction-decisions-v3.json` | decision records `/3`, which drive the vocabulary 4 default plan |

`DEC-GATE` stands in for an approval. It is a test input, not an approval.
