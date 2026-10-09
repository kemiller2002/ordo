# ECIR v1 fixtures

Ordo contract: [docs/architecture/ecir-v1.md](../../docs/architecture/ecir-v1.md). Schema: [schemas/ecir-v1.schema.json](../../schemas/ecir-v1.schema.json).

The files provide a **minimal synthetic test corpus**, not a production authorization chain. Their source revision, section-content and canonical manifest SHA-256 values are **real, reproducible hashes** of the fixture documents in `requirements/`. Production ingestion must derive and protect its own independent manifest; these example hashes cannot grant authority.

- `requirements/domain.md` and `requirements/operations.md`: the exact original source text, with independently checked document revision, heading position and section-content digests. Their local IDs deliberately collide.
- `source-manifest.json`: independent pinned intake with **two distinct requirements that share the original local ID `R-001`**. Their qualified keys identify different documents.
- `valid-blueprint.json`: preserves both; one modeled and one explicitly deferred. The modeled requirement has a decision, a proposed (not yet authorized) execution cohort and verification obligation. A separate verified authorization still must approve the decision before cohort execution.
- `invalid-omitted-requirement.json`: shape-valid JSON, but it silently drops one imported requirement. **Ordo semantic validation MUST reject it** against `source-manifest.json`.
- `invalid-nonreciprocal-link.json`: shape-valid JSON, but its verification node drops the reverse requirement link. **Ordo semantic validation MUST reject it**.

JSON Schema cannot prove intake equality, reciprocal links, model authorization or independent test quality. The typed Ordo validator and independent intake are essential.

The checked-in Ordo F# tests execute these artifacts directly, recompute their cryptographic hashes from the original fixture text, and assert that the intentionally invalid examples fail closed. The independent source authority and decision authorizer remain separate from the blueprint.
