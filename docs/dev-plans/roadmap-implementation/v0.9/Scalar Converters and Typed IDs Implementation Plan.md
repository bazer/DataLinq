> [!WARNING]
> This document is roadmap implementation material for the DataLinq 0.9 development line. It is not normative product documentation and should not be treated as a shipped support claim.

# 0.9 Scalar Converters And Typed IDs Implementation Plan

**Status:** Accepted.

**Target:** Required 0.9 baseline work.

**Created:** 2026-07-04.

**Last reviewed:** 2026-07-10.

## Purpose

This document defines the required 0.9 implementation slice for scalar converters, provider-value normalization, and typed-ID support.

The durable design source remains [Scalar Converter Support](../../metadata-and-generation/Scalar%20Converter%20Support.md). Keep broad converter API discussion there. Keep this page focused on the release-critical work that SQL providers and the read-only memory backend must share.

## 0.9 Decision

Scalar conversion is part of the 0.9 release baseline, not a stretch goal.

The first release claim stays deliberately narrow:

> DataLinq 0.9 supports explicit scalar converters and typed IDs for single-column model values whose canonical provider representation is one CLR scalar value.

Typed IDs are the first proof of the boundary. Full generated typed-key classes, library-specific adapter packages, and structured value-object querying remain later work.

## Required Three-Layer Value Model

The implementation must distinguish three different representations instead of calling all of them "provider values":

| Layer | Example | Owner | Used for |
| --- | --- | --- | --- |
| Model value | `OrderId` | Scalar converter model side | Public model properties and public model-valued row state |
| Canonical provider CLR value | `int`, `Guid`, `string` | Scalar converter provider side | Backend-neutral query comparison, cache/relation identity, internal provider row buffers |
| Provider physical/wire value | `byte[16]`, SQL text, connector parameter value | Provider/column codec | Database parameters, data readers/writers, SQL literals, and physical schema semantics |

The conversion pipeline is therefore:

```text
model value <-> canonical provider CLR value <-> provider physical/wire value
```

The first arrow is the scalar-converter contract. The second arrow is provider- and sometimes column-specific. UUID binary layout is the clearest example: a typed ID may normalize to canonical `Guid`, while MySQL encodes that `Guid` as a particular `BINARY(16)` byte order.

Do not merge these arrows into one generic converter call. Doing so would make model converters provider-specific, make memory execution depend on SQL wire formats, and recreate the existing UUID ambiguity under a different name.

## Row-State Compatibility Rule

Existing public `RowData` and the public model-instance/indexer surfaces that expose it remain model-valued in 0.9. Changing those values to canonical or physical provider values would be a silent public behavior break.

Backend-neutral execution should instead introduce a separate internal provider-value row buffer whose cells contain canonical provider CLR values. The exact internal type name is an implementation detail; the semantic split is not.

Consequences:

- SQL readers decode physical values to canonical provider values, then scalar converters materialize model values into public row state.
- mutation paths convert model-valued row state to canonical provider values before provider writers encode physical values.
- the memory backend stores and compares canonical provider values internally, while materialized models still expose model values.
- cache and relation identity use canonical provider key values, not connector-specific bytes or public wrapper instances.

## Execution Boundary

In scope for the 0.9 baseline:

- explicit `[ScalarConverter]` property metadata
- assembly-level converter registration
- resolved model CLR type, canonical provider CLR type, and converter metadata on column definitions
- provider-value normalization for reads, writes, query constants, local sequences, keys, relations, and mutation values
- a separate internal canonical-provider-value buffer for backend execution
- typed-ID equality predicates
- typed-ID local `Contains(...)` membership predicates
- typed-ID explicit join keys where both sides normalize to compatible canonical provider types
- auto-increment/default physical values converted back through canonical provider values to model values
- schema validation based on canonical provider type plus provider physical storage metadata
- focused diagnostics for unsupported typed-ID member unwrapping and structured value-object predicates
- dynamic/fallback key creation that is correct for converted values

Out of scope for the baseline:

- changing public `RowData` to store provider values
- object-to-multiple-column mapping
- JSON path querying over converted JSON values
- provider-native JSON column predicates
- runtime hot-path reflection discovery of converter conventions
- dependencies on Vogen, StronglyTypedId, Meziantou.Framework.StronglyTypedId, or similar libraries in the core package
- generated typed-key classes or generated optimized provider-key accessors as a correctness requirement
- querying arbitrary value-object members such as `x.Email.Domain`
- provider-specific physical codecs implemented inside model converters

## Dependency Order

Use workstream-local IDs so this plan does not collide with phase numbers in other plans.

### SC-1: Value Contract And Metadata

Work:

- define the model-to-canonical-provider scalar converter contract
- add scalar converter attributes and assembly-level registrations
- resolve converter metadata during source and runtime metadata construction
- expose model CLR type and canonical provider CLR type distinctly on columns
- validate converter direction, nullability, and type compatibility
- preserve source locations for converter diagnostics
- define the provider/column-codec handoff without baking physical storage into scalar converters

Exit signal:

- metadata can answer model type, canonical provider type, and converter identity without hot-path attribute scanning
- invalid converter declarations produce focused diagnostics
- physical/wire conversion remains a distinct provider concern
- existing primitive, enum, date/time, and nullable columns keep their current behavior

### SC-2: Runtime Materialization And Mutation

Work:

- add the internal canonical-provider-value row buffer
- decode physical reader values to canonical provider values before model conversion
- convert canonical provider values to model values during materialization
- convert model values to canonical provider values before mutation writers encode them
- convert database-generated/default physical values back through both layers after insert or hydration
- preserve model values in public `RowData`, `GetValues()`, indexers, and model-instance APIs

Exit signal:

- typed `int`, `long`, `Guid`, and `string` IDs round-trip through SQL reads and mutations
- public row-state behavior remains model-valued
- conversion failures identify table, column, source representation, and target type

### SC-3: Dynamic Keys, Relations, And Cache Identity

Work:

- make the dynamic/fallback key path normalize every key component to its canonical provider value
- normalize relation and cache-index keys through the same path
- support converted primary and foreign keys before adding generated fast paths
- document canonical provider equality and hashing requirements
- add generated optimized provider-key accessors only later, after the fallback path is proven correct and profiling shows value

Exit signal:

- dynamic primary-key lookup and relation matching work with typed IDs
- composite fallback keys can mix converted and unconverted scalar components correctly
- correctness does not depend on generated typed-key output
- cache identity does not depend on model-wrapper reference identity or provider wire bytes

### SC-4: Query Constants And Local Sequences

Work:

- normalize equality constants through target-column converter metadata
- normalize local `Contains(...)` values
- normalize equality-membership local `Any(predicate)` shapes where already supported
- normalize explicit join keys through canonical provider values
- preserve column metadata until any provider physical/wire encoding step
- reject unsupported typed-ID member access unless deliberately supported

Exit signal:

- `Where(x => x.Id == id)` compares canonical provider values and binds the correct physical value
- `ids.Contains(x.Id)` normalizes every local value through the same column path
- explicit joins over typed IDs compare compatible canonical provider values
- unsupported structured value-object predicates fail before backend execution

### SC-5: Schema Validation And Provider Handoff

Work:

- compare logical compatibility against canonical provider CLR type rather than only model CLR type
- let provider storage validation add physical type/format constraints
- update diff diagnostics so converter-backed columns do not report false type drift
- establish the hook used by column-specific physical codecs
- test provider handoff across SQLite, MySQL, and MariaDB

Exit signal:

- typed IDs over primitive provider columns validate without false mismatches
- physical storage mismatches remain visible rather than being hidden by canonical type compatibility
- UUID storage codecs can use the common handoff instead of query-specific parameter hacks

### SC-6: Read-Only Memory Integration

Work:

- seed memory tables into internal canonical-provider-value buffers
- make memory predicates, ordering, membership, keys, and relations operate on canonical provider values
- convert selected values back to model values during materialization
- keep SQL physical/wire codecs out of the memory executor

Exit signal:

- the read-only memory backend does not implement a second converter system
- typed IDs behave consistently for the memory query shapes included in 0.9
- public models materialized from memory expose the same model values as SQL-backed models

## UUID Handoff

The bounded 0.9 UUID storage slice begins after `SC-1` through `SC-5` establish the two conversion boundaries. It is owned by [UUID Storage Format Support](../../providers-and-features/UUID%20Storage%20Format%20Support.md), not duplicated here.

That slice is required because a canonical `Guid` does not say whether a given database column uses native UUID, text, little-endian `BINARY(16)`, or RFC-order `BINARY(16)`. UUID reads, writes, parameters, local membership, keys, relations, defaults, and validation must all use the same column codec.

## Verification Gates

The 0.9 scalar-converter baseline is not complete until these are green:

- metadata tests for explicit property converters and assembly registrations
- converter direction, nullability, and compatibility diagnostics
- read/write typed `int`, `long`, `Guid`, and `string` ID tests
- nullable typed-ID tests where relevant
- assertions that public `RowData` and model-instance access remain model-valued
- internal provider-buffer tests using canonical provider values
- dynamic/fallback primary-key lookup and relation traversal with typed IDs
- composite fallback-key tests with converted components
- equality and local membership query tests
- explicit typed-ID join-key tests for already-supported joins
- insert/update/default-value hydration tests
- schema validation tests proving canonical and physical checks remain distinct
- read-only memory typed-ID query tests for the shipped memory capability set
- the separate UUID 0.9 codec gates

Generated optimized key accessors are not a release gate. They may follow only after the correct dynamic path is measured and shown to need optimization.

## Release Boundary

The 0.9 release can claim typed-ID support only when:

- typed IDs are backed by explicit scalar converter metadata
- reads, writes, queries, dynamic keys, joins, relations, and validation use the three-layer value model
- public row state remains model-valued
- memory and SQL execution share canonical provider-value semantics
- unsupported structured value-object predicates fail clearly
- docs do not imply broad value-object member translation or generated typed-key output

Claims to avoid unless separately implemented and proven:

- "any value object works in queries"
- "automatic typed-ID generation"
- "support for every typed-ID library"
- "JSON value-object path queries"
- "composite value-object mapping"
- "provider wire formats are part of the scalar converter contract"

## Links

- [Scalar Converter Support](../../metadata-and-generation/Scalar%20Converter%20Support.md)
- [UUID Storage Format Support](../../providers-and-features/UUID%20Storage%20Format%20Support.md)
- [Memory Backend Architecture](../../backends/memory/Architecture.md)
- [DataLinq 0.9 Roadmap](README.md)
