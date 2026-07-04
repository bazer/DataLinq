> [!WARNING]
> This document is roadmap implementation material for the DataLinq 0.9 development line. It is not normative product documentation and should not be treated as a shipped support claim.

# 0.9 Scalar Converters And Typed IDs Implementation Plan

**Status:** Draft.

**Created:** 2026-07-04.

## Purpose

This document keeps the immediate 0.9 implementation plan for scalar converters, provider-value normalization, and typed-ID support.

The durable design source remains [Scalar Converter Support](../../metadata-and-generation/Scalar%20Converter%20Support.md). Keep broad converter API discussion there. Keep this page focused on the 0.9 slice that must land before memory and JSON persistence start inventing one-off value rules.

## 0.9 Goal

The 0.9 scalar-conversion work should make model values and provider values distinct, explicit, and consistently normalized across the runtime.

The first release claim should stay narrow:

> DataLinq 0.9 introduces explicit scalar converters and typed-ID query support for single-column model values whose storage representation is a provider CLR value.

That means typed IDs are in scope. Full typed-key generation, adapter packages for specific typed-ID libraries, and structured value-object querying are not the baseline claim.

## Why This Moves Into 0.9

The memory backend and JSON persistence both need a provider-value contract immediately:

- memory row buffers should store provider values, not whatever public model type happened to be exposed
- JSON snapshots and commit logs should encode provider values, not table-local conversion guesses
- cache keys and relation indexes already want provider-key identity
- query constants, local sequences, explicit joins, and grouped keys must compare the same values that storage uses
- typed IDs are the most obvious user-visible feature that proves the boundary is real

Punting this after memory/JSON would be false economy. It would force the new backends to grow stringly or backend-specific conversion rules, then unwind them later.

## Execution Boundary

In scope:

- explicit `[ScalarConverter]` property metadata
- assembly-level converter registration
- resolved model CLR type, provider CLR type, and converter metadata on column definitions
- provider-value normalization for reads, writes, query constants, local sequences, joins, keys, relations, mutation values, memory row buffers, and JSON payloads
- typed-ID equality predicates
- typed-ID local `Contains(...)` membership predicates
- typed-ID explicit join keys where both sides normalize to compatible provider values
- auto-increment/default provider values converted back to model values
- schema validation based on provider storage type
- diagnostics for unsupported typed-ID member unwrapping and structured value-object predicates

Out of scope:

- object-to-multiple-column mapping
- JSON path querying over converted JSON values
- provider-native JSON column predicates
- runtime hot-path reflection discovery of converter conventions
- dependencies on Vogen, StronglyTypedId, Meziantou.Framework.StronglyTypedId, or similar libraries in the core package
- generated typed-key classes before manual converter behavior is stable
- querying arbitrary value-object members such as `x.Email.Domain`

## Recommended Order

### Phase 3A: Metadata And Converter Registration

Work:

- add scalar converter attributes and assembly-level registrations
- resolve converter metadata during source and runtime metadata construction
- expose model/provider CLR type distinctions on columns
- validate converter type compatibility
- preserve source locations for converter diagnostics

Exit signal:

- metadata can answer model type, provider type, and converter identity without scanning attributes on hot paths
- invalid converter declarations produce focused diagnostics
- existing primitive, enum, date/time, and nullable columns keep their current behavior

### Phase 3B: Runtime Read, Write, And Key Normalization

Work:

- convert provider values to model values when materializing or exposing properties
- convert model values to provider values for mutation writers
- make generated provider-key accessors and fallback key creation use provider values
- normalize relation/index keys through provider values
- convert database-generated provider values back to model values after insert/default hydration

Exit signal:

- cache identity and relation matching use provider values
- typed-ID primary keys and foreign keys can round-trip through insert/read/update paths
- conversion failures include table, column, provider value, and target model type

### Phase 3C: Query Constant And Local Sequence Normalization

Work:

- normalize equality constants through target-column converter metadata
- normalize local `Contains(...)` values
- normalize equality-membership local `Any(predicate)` shapes where already supported
- normalize explicit join key constants and local values
- reject unsupported typed-ID member access unless deliberately supported

Exit signal:

- `Where(x => x.Id == id)` translates through provider values
- `ids.Contains(x.Id)` translates through provider values
- explicit joins over typed IDs compare provider values
- unsupported structured value-object predicates fail before SQL/memory execution

### Phase 3D: Schema Validation And Provider Fidelity

Work:

- compare database schema against provider CLR/storage type, not only model CLR type
- update diff diagnostics so converter-backed columns do not report false type drift
- add provider tests across SQLite, MySQL, and MariaDB where converter behavior touches provider storage
- consider whether UUID storage format metadata belongs in this slice as a column-specific provider-value codec

Exit signal:

- typed IDs over primitive provider columns validate without false mismatches
- converter-backed columns report actionable drift when provider storage changes
- UUID storage work, if included, uses the same provider-value normalization path rather than a separate query parameter hack

### Phase 3E: Memory And JSON Integration

Work:

- make memory row buffers store provider values
- make memory predicates compare provider values
- make memory mutation normalize provider values before indexing
- make JSON snapshot and commit-log encoding use provider values
- add typed-ID round-trip tests through memory and JSON snapshots where those features are in scope

Exit signal:

- memory and JSON persistence do not implement separate converter systems
- typed IDs work through the same query, key, and persistence rules as SQL-backed providers

## Verification Gates

The 0.9 scalar-converter slice should not be called supported until these are green:

- metadata tests for explicit property converters and assembly registrations
- converter compatibility diagnostics
- read/write typed `int`, `long`, `Guid`, and `string` ID tests
- nullable typed-ID tests for generated keys where relevant
- primary-key lookup and relation traversal with typed IDs
- equality and local membership query tests
- explicit typed-ID join tests
- insert/update/default-value hydration tests
- schema validation tests proving provider type comparison
- memory backend typed-ID query tests if memory ships
- JSON provider-value encoding tests if JSON persistence ships

## Release Boundary

The 0.9 release can claim typed-ID support only when:

- typed IDs are backed by explicit scalar converter metadata
- reads, writes, queries, keys, joins, relations, and validation normalize through provider values
- unsupported structured value-object predicates fail clearly
- docs do not imply broad value-object member translation or generated typed-key output

Possible stronger claims, if earned:

- typed-ID primary keys and foreign keys work across SQL and memory providers
- typed-ID values round-trip through JSON memory snapshots
- UUID storage format support uses the same provider-value normalization boundary

Claims to avoid unless proven:

- "any value object works in queries"
- "automatic typed ID generation"
- "support for every typed-ID library"
- "JSON value-object path queries"
- "composite value-object mapping"

## Links

- [Scalar Converter Support](../../metadata-and-generation/Scalar%20Converter%20Support.md)
- [UUID Storage Format Support](../../providers-and-features/UUID%20Storage%20Format%20Support.md)
- [Memory Backend Architecture](../../backends/memory/Architecture.md)
- [JSON Persistence Store Architecture](../../backends/memory/persistence/json/JSON%20Persistence%20Store%20Architecture.md)
- [DataLinq 0.9 Rough Roadmap](README.md)
