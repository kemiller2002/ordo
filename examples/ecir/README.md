# ECIR v1 fixtures

Ordo contract: [docs/architecture/ecir-v1.md](../../docs/architecture/ecir-v1.md). Schema: [schemas/ecir-v1.schema.json](../../schemas/ecir-v1.schema.json).

The files provide a **minimal synthetic test corpus**, not a valid production authority chain. The `sha256:` values are syntactically valid **fixture placeholders**, not attestations of the sample documents; production ingestion must independently derive/verify cryptographic digests.

- `source-manifest.json`: independent pinned intake with **two distinct requirements that share the original local ID `R-001`**. Their qualified keys identify different documents.
- `valid-blueprint.json`: preserves both; one modeled and one explicitly deferred. The modeled requirement has a decision, executable cohort and verification obligation. A separate verified authorization still must approve the decision before cohort execution.
- `invalid-omitted-requirement.json`: shape-valid JSON, but it silently drops one imported requirement. **Ordo semantic validation MUST reject it** against `source-manifest.json`.
- `invalid-nonreciprocal-link.json`: shape-valid JSON, but its verification node drops the reverse requirement link. **Ordo semantic validation MUST reject it**.

JSON Schema cannot prove intake equality, reciprocal links, model authorization or independent test quality. The typed Ordo validator and independent intake are essential.

The fixtures are not a substitute for the adversarial F# test suite. Completing the next increment will include the strict JSON decoder and wire round-trip/golden tests that execute these files directly, plus genuine canonical SHA-256 round-trip checks.
