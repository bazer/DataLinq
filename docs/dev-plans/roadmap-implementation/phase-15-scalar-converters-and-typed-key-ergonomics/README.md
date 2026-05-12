> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 15: Scalar Converters and Typed-Key Ergonomics

**Status:** Planned after Phase 14 unless typed-key demand pulls it forward.

## Purpose

Phase 15 adds a first-class scalar conversion layer so model APIs can expose domain types while storage, queries, cache keys, relations, and mutations use provider CLR values consistently.

Phase 10 should already design provider-key stores with converter hooks in mind. Phase 15 is where the full user-facing converter model, typed-key ergonomics, and generator support become product work.

Phase 10 Workstream H installed the cache-side seam: key-shape metadata now carries separate model/provider CLR type fields, provider store selection is named `ProviderStoreKind`, and scalar converter handles are explicit but unresolved. Phase 15 should fill those converter handles and keep the cache identity model unchanged.

## Execution Boundary

In scope:

- scalar converter metadata and explicit converter attributes
- provider CLR value normalization for reads, writes, query constants, joins, keys, and relations
- typed-ID equality and local `Contains(...)` query support
- auto-increment/default value conversion back to model values
- schema validation based on provider CLR type rather than model CLR type
- optional typed-key generation once manual converters are stable

Out of scope:

- object-to-multiple-column mapping
- JSON path querying
- runtime reflection-heavy converter discovery on hot paths
- dependencies on specific typed-ID libraries in the core package

## Source Plans

- [Scalar Converter Support](../../metadata-and-generation/Scalar%20Converter%20Support.md)
- [Generated Provider-Key Cache Design](../../performance/Generated%20Provider-Key%20Cache%20Design.md)
- [Phase 10 Key and Allocation Foundation](../phase-10-key-and-allocation-foundation/README.md)

## Recommended Order

1. Replace the Phase 10 null converter handles with resolved scalar converter metadata.
2. Add explicit property and assembly-level converter registration.
3. Normalize provider values in read, write, query, relation, and cache-key paths.
4. Add typed-ID equality and local membership query tests.
5. Add insert/update/default-value conversion tests.
6. Add optional typed-key generation only after manual converter behavior is stable.

## Exit Criteria

Phase 15 is done when:

- model values and provider values are distinct in metadata and runtime paths
- cache keys and relation indexes use provider values
- direct typed-ID equality and local `Contains(...)` queries translate through provider values
- schema validation compares the database against provider storage types
- converter behavior is test-covered across SQLite, MySQL, and MariaDB where relevant
