> [!WARNING]
> This document is roadmap implementation material for the DataLinq 0.9 development line. It is not normative product documentation and should not be treated as a shipped support claim.

# 0.9 Scalar Converters And Typed IDs Implementation Plan

**Status:** Implementation in progress. `SC-1` is complete; `SC-2`, `SC-3`, and `SC-4` have bounded green slices but are not complete.

**Target:** Required 0.9 baseline work.

**Created:** 2026-07-04.

**Last reviewed:** 2026-07-13.

## Purpose

This document defines the required 0.9 implementation slice for scalar converters, provider-value normalization, and typed-ID support.

The durable design source remains [Scalar Converter Support](../../metadata-and-generation/Scalar%20Converter%20Support.md). Keep broad converter API discussion there. Keep this page focused on the release-critical work that SQL providers and the read-only memory backend must share.

## 0.9 Decision

Scalar conversion is part of the 0.9 release baseline, not a stretch goal.

The first release claim stays deliberately narrow:

> DataLinq 0.9 supports explicit scalar converters and typed IDs for single-column model values whose canonical provider representation is one CLR scalar value.

Typed IDs are the first proof of the boundary. Full generated typed-key classes, library-specific adapter packages, and structured value-object querying remain later work.

The 0.9 converter contract is deliberately boring:

- converters are pure and stateless
- converter types have a public parameterless constructor and are instantiated/resolved once during metadata construction, never discovered on a hot path
- an explicit property converter overrides an assembly registration
- duplicate assembly registrations for the same model type are rejected deterministically
- DataLinq owns null handling and does not call ordinary converters for null values
- service-dependent or scoped converters are deferred until a real lifetime/DI design exists
- existing key APIs normalize model-valued key components, including typed IDs; 0.9 does not require new generated `Find(TId)`-style overloads

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
- populate the existing provider/model type and converter-handle scaffolding in `TableKeyShape` rather than inventing a parallel key-metadata system

Exit signal:

- metadata can answer model type, canonical provider type, and converter identity without hot-path attribute scanning
- invalid converter declarations produce focused diagnostics
- physical/wire conversion remains a distinct provider concern
- existing primitive, enum, date/time, and nullable columns keep their current behavior

### SC-2: Runtime Materialization And Mutation

Progress through 2026-07-13: the shared canonical-provider-to-model row materializer is implemented with column-only conversion context, null bypass, output validation, and safe diagnostics. `ProviderRowDecoder` decodes full SQL reader rows into canonical provider values before scalar materialization, and the buffered SQL primary-key row loader owns command/reader lifetime around it. Bounded `F6-A` now makes that adapter the live single and batched primary-key cache-miss route for SQL source services only when every canonical primary-key component is integral, including transaction-scoped authoritative reload after mutation. `TableCache` remains the one logical cache probe and immutable-instance identity owner; a known miss materializes without a second probe or miss metric while publication still returns a concurrent winner. Scalar converter-backed primary-key projections for non-simple entity queries use the same per-column physical-to-canonical decoder before `DataLinqKey` construction. SQL `ScalarMember` result projections over converter-backed columns now use the same per-column physical-to-canonical decoder and scalar materializer before result-shape adaptation, for both root and joined sources. Converter-backed column members in aliased anonymous and explicit-constructor SQL `SqlRow` projections pass through the same boundary for root and joined sources while the projection retains `SqlOnlyCompatibility`. Model-compatible converter-backed column keys in direct SQL `GroupedAggregate` results now use that boundary while constructor-backed grouped results retain `SqlOnlyCompatibility`. Single-source integral-keyed `ComputedRowLocal` `NewArray` recipes also consume converter-backed model values under `AotStrict`. Primitive-key `JoinedRowLocal` `NewArray` SQL execution also preserves converter-backed joined-source values while retaining the projection's intentional `SqlOnlyCompatibility` fence. Non-primary-key full-row canonical `Guid` reads and mutation writes now consume resolved text/native/binary codecs on SQLite, MySQL, and MariaDB for direct values, representative binary converter-backed typed IDs, and nullable direct/typed Text36/SQL NULL. Only `DecodeFullRow` opts converter-backed non-key Guids into column-aware decoding; UUID primary keys and scalar/projection decoding retain metadata-free `GetGuid`. String/CHAR keys retain the legacy route because database collation can differ from CLR equality; `Guid`/binary and other codec-sensitive key shapes also retain the legacy path. `ModelValueConverter` applies reverse model-to-canonical mapping once per serialization, identity-capture, or identity-validation boundary for insert values, update values and keys, and delete keys. `StateChange` captures canonical update/delete keys and reuses them for physical rendering, so the provider writer does not double-convert those keys at render time. Existing canonical loader/key writer calls do not enter that model conversion path. Raw SQL auto-increment results accept only checked conversions among the eight integral CLR types, then use the same single-column canonical-to-model materializer before assigning the mutable generated-ID slot. This covers SQLite `Int64`-shaped and MySQL/MariaDB unsigned result shapes without decimal rounding or string parsing, including converter-backed IDs whose canonical provider type is integral. Insert mutations preserve one table-ordinal write slot per column with explicit assignment provenance. An assigned null is written as SQL `NULL`; an unset, provider-applicable `DefaultSql` may be omitted from a non-primary-key, non-indexed column even when that column has a scalar converter, provided the authoritative reload uses an `F6-A`-compatible integral canonical primary key or one decodable integral auto-increment key. The authoritative reload decodes the physical server value to its canonical provider value and then materializes the public model value. Provider-mismatched `DefaultSql`, client `[Default]` values, indexed defaults, non-integral generated keys, unsupported key shapes, and rows with unknown non-auto keys retain the existing explicit-write behavior. UUID routes beyond direct and representative typed non-primary-key full-row mutation/materialization, including other typed formats, UUID primary keys, projections, predicates/membership, relations, defaults, provider-less MySQL/MariaDB public-reader fallback removal, composite reader keys, member-init row evidence, joined/function-derived grouped keys, converter-backed aggregate values, `HAVING`, joined-local key handoff beyond primitive integral keys and neutral local-recipe hydration beyond integral SQL keys, the legacy custom-source fallback, relation/index loading, and the remaining reader/query/materialization routes remain open; SC-2 is not complete.

Bounded mutable-assignment progress on 2026-07-12 removes the generic `Convert.ChangeType` fallback from converter-backed `MutableRowData` columns. Those setters now validate against the resolved model CLR type and keep public row/indexer state model-valued; passing the canonical provider CLR value is rejected with table, column, expected-model-type, and received-type context without invoking either converter direction or recording a mutation. Exact model values and null retain existing assignment behavior, while non-converted primitive columns retain their historical conversion fallback. This closes the explicit mutable-setter work item but not indexed defaults, SQLite and MySQL/MariaDB UUID routes beyond direct and representative typed non-primary-key full-row mutation/materialization, relation/index routing, or the remaining SC-2 routes.

Default-only insert progress on 2026-07-12 now omits an unassigned null auto-increment primary-key slot when its canonical provider type is integral, including converter-backed model IDs. When every write slot is omitted, the provider owns the zero-column SQL shape: SQLite renders `DEFAULT VALUES`, while MySQL and MariaDB render `() VALUES ()`. The same reload-safe path omits and hydrates provider-applicable, non-indexed server defaults through scalar conversion when the authoritative primary-key reload is integral-canonical and therefore `F6-A` compatible. The focused converted-default active-provider matrix passes `4/4`; full unit, generator, SQLite compliance, and MySQL/MariaDB compliance runs pass `1051/1051`, `57/57`, `746/746`, and `748/748`. An assigned null or explicit auto-increment value remains an explicit write. Providers that do not declare default-only syntax retain the legacy null-key write and empty-query `VALUES (NULL)` fallback. Indexed defaults, unknown non-auto keys, non-integral generated IDs, string/CHAR and `Guid`/binary key routes, SQLite and MySQL/MariaDB UUID routes beyond direct and representative typed non-primary-key full-row mutation/materialization, relation/index routing, composite reader keys, member-init row evidence, joined/function-derived grouped keys, converter-backed aggregate values, `HAVING`, joined-local key handoff beyond primitive integral keys and neutral local-recipe hydration beyond integral SQL keys, and the remaining query/materialization routes remain open; SC-2 is not complete.

SQL-projection progress on 2026-07-12 routes SQL `ScalarMember` results over converter-backed columns through `ProviderRowDecoder.DecodeCanonicalValue` and `ProviderRowMaterializer.MaterializeValue` before `ConvertProjectionResult` adapts the declared result shape. Active-provider evidence covers root and joined `QueryTypedId` projections, nullable columns and nullable lifting, boxing that retains the model wrapper, terminal `Single()`, and explicit `AotStrict` execution; unsupported model-to-`int` conversion remains a focused translation diagnostic. Converter-backed column members in aliased anonymous and explicit-constructor SQL `SqlRow` projections now pass through the same boundary for root and joined rows while the projection retains `SqlOnlyCompatibility`; active-provider evidence covers nullable, lifted-nullable, boxed, and terminal shapes. This is evidence for an `int`-backed typed ID only. It does not establish general nullable `.Value`/`.HasValue` predicate or projection translation beyond the exact same-mapping join-key shape exercised here, arbitrary numeric converter widening or narrowing, Native AOT or browser execution, implicit-relation or member-init row projections, grouped/aggregate conversion outside the bounded key-only slice below, or retained local recipes beyond the bounded single-source `NewArray` slice below. Full unit, generator, SQLite compliance, and MySQL/MariaDB compliance runs pass `1051/1051`, `57/57`, `746/746`, and `748/748`. `SC-2` remains open.

Explicit-constructor row evidence on 2026-07-12 proves constructor-backed DTO/record SQL `SqlRow` projections apply the per-column physical-to-canonical-to-model boundary before positional constructor adaptation. Active-provider coverage includes root and joined source slots, non-nullable, nullable, lifted-nullable, and boxed typed IDs, plus terminal `Single()`. Exact constructor/result types are preserved, the projection remains `SqlOnlyCompatibility`, and `AotStrict` rejects the reflective constructor path by design. Full gates pass `1051/1051` unit, `57/57` generator, `752/752` SQLite compliance, `1416/1416` four-server compliance, and `160/160` in the latest MySQL/MariaDB provider-specific lane. This does not establish member-init projections, implicit-relation columns, post-projection DTO composition, Native AOT/browser construction, or memory-backend execution.

Implicit-relation row evidence on 2026-07-12 proves converter-backed non-key values reached through a one-hop singular implicit relation retain the related table's `ColumnDefinition` through `ImplicitJoin` planning and direct SQL `SqlRow` materialization. The dedicated active-provider fixture keeps PK/FK relation keys primitive and covers non-nullable, nullable, lifted-nullable, boxed, and terminal typed-ID values. Plan assertions record one root plus one implicit-join source and tie all converted related members to that source. Full gates pass `1051/1051` unit, `57/57` generator, `754/754` SQLite compliance, `1420/1420` four-server compliance, and `160/160` in the latest MySQL/MariaDB provider-specific lane. This does not establish converted relation-key normalization, lazy relation loading, relation cache/index behavior, nullable left-join semantics, multi-hop or collection relations, member-init/local recipes, Native AOT/browser execution, or memory-backend relations.

Single-source local-recipe evidence on 2026-07-13 proves that converter-backed source columns enter retained `ComputedRowLocal` `NewArray` recipes as materialized model values. Active-provider coverage executes sequence and terminal `Single()` plans under `AotStrict` after explicitly clearing the table cache and covers non-nullable, nullable, lifted-nullable, boxed-nullable, `HasValue`, and conditional `.Value` nodes; exact type assertions show that no canonical `int` values leak into the result. The plan and recipe remain `AotSafe`; compatibility construction and member reflection are not involved. This evidence does not instrument a specific loader implementation and is bounded to the current integral-canonical SQL entity-key configuration. Full gates pass `1051/1051` unit, `57/57` generator, `756/756` SQLite compliance, `1424/1424` four-server compliance, and `160/160` in the latest MySQL/MariaDB provider-specific lane. As a single-source slice, it does not establish joined-local execution, constructor/member-init local recipes, string/CHAR, `Guid`/binary, composite or unknown-key neutral hydration, custom sources, Native AOT/browser publication, or memory-backend recipe execution.

Primitive-key joined-local evidence on 2026-07-13 proves that explicit-inner-join `JoinedRowLocal` `NewArray` SQL execution supplies converter-backed joined-source columns to the normalized recipe as model values after reloading both source rows. Active-provider coverage after explicit cache clearing includes non-nullable, nullable, lifted-nullable, boxed-nullable, `HasValue`, and conditional `.Value` shapes without canonical-`int` leakage. Plan assertions pin one root plus one explicit-join source, one primitive non-converted `int` primary key per source, and converter metadata on the joined columns. The `NewArray` recipe is `AotSafe`; the enclosing projection intentionally remains `SqlOnlyCompatibility`, so `AotStrict` rejection is preserved. The focused active-provider matrix passes `6/6`. Two preceding concurrent four-target attempts reached `1426/1428` and `1427/1428`; their only failures were unrelated connection refusals, and every exact failing test passed on its immediate target-specific rerun. Full gates pass `1051/1051` unit, `57/57` generator, `758/758` SQLite compliance, `426/426` on each of MySQL 8.4 and MariaDB 10.11/11.4/11.8 (`1704/1704` sequential server executions after concurrent connection instability), and `160/160` in the latest MySQL/MariaDB provider-specific lane. Converted, composite, string, `Guid`, binary, and codec-sensitive joined keys, implicit/outer joins, nullable missing-source rows, joined terminal/paging composition, custom or memory backends, and Native AOT/browser publication remain outside this slice.

Grouped-key projection progress on 2026-07-12 applies the same per-column physical-to-canonical-to-model boundary to model-compatible converter-backed column keys in direct SQL `QueryPlanProjection.GroupedAggregate` results while retaining `SqlOnlyCompatibility`. Active-provider evidence covers scalar, nullable, boxed, and anonymous composite group keys projected through named members. Ordinary `Count()` members continue through the raw aggregate-value path, and an explicit `QueryTypedId`-to-`int` group key retains the raw provider-value fallback rather than being rematerialized as a wrapper. This bounded slice does not establish joined or function-derived group keys, supported converter-backed aggregate execution, general `HAVING`, Native AOT or browser execution, backend-neutral or memory grouping, or general collation/codec semantics. Full unit, generator, SQLite compliance, and MySQL/MariaDB compliance runs pass `1051/1051`, `57/57`, `746/746`, and `748/748`. `SC-2` remains open.

Converted-aggregate semantics progress on 2026-07-12 rejects scalar and grouped `Sum`, `Min`, `Max`, and `Average` during plan translation when their selector resolves to a converter-backed column, including aggregate predicates in grouped `HAVING`. The existing converter contract declares only per-value `ToProvider` and `FromProvider`; reversibility does not imply the additive, ordering, or mean-preserving behavior required by those SQL aggregates. A single `FromProvider` call over the SQL result would therefore be unsound. Selectorless `Count` and `Any`, and grouped `Count()`, remain supported. The SQL aggregate selector validators retain the same check as defense in depth. Active-provider coverage uses a numeric model/provider converter that deliberately changes aggregate semantics and proves every rejection occurs before a SQL command. Full gates pass `1051/1051` unit, `57/57` generator, `750/750` SQLite compliance, `1412/1412` four-server compliance, and `160/160` in the latest MySQL/MariaDB provider-specific lane. Aggregate capability metadata and supported converter-backed aggregate execution remain open; the guard closes a silent wrong-result path, not the aggregate work item.

Work:

- plug scalar conversion into the shared canonical-provider-value row buffer and materializer owned by foundation workstream `F3`
- decode physical reader values to canonical provider values before model conversion
- convert canonical provider values to model values during materialization
- convert model values to canonical provider values before mutation writers encode them
- convert database-generated/default physical values back through both layers after insert or hydration
- preserve model values in public `RowData`, `GetValues()`, indexers, and model-instance APIs
- replace `MutableRowData.SetValue` conversion fallbacks with the resolved scalar boundary where converter-backed properties are involved

Exit signal:

- typed `int`, `long`, `Guid`, and `string` IDs round-trip between model and canonical provider values; SQL physical `Guid` round-trips remain owned by `UUID-2`
- public row-state behavior remains model-valued
- conversion failures identify table, column, source representation, and target type

### SC-3: Dynamic Keys, Relations, And Cache Identity

Progress through 2026-07-12: full-row materialization snapshots primary-key components from the canonical provider row before scalar conversion, including converted and composite components. `DataLinqKey` owns mutable binary components at every ingress and returns defensive copies, preserving its cached hash and dictionary identity across caller mutation. Scalar and generated composite provider keys containing `byte[]` route through an owned structural row store, and index caches snapshot primary-key arrays before publishing forward and reverse mappings. The dynamic `KeyFactory` row and model-instance overloads normalize only converter-backed model components, including mixed composite and binary keys; identity mappings retain the existing path. Its scalar converter-backed reader overload now decodes the selected key column to its canonical provider value before constructing `DataLinqKey`. That reader-key decode invokes neither converter direction; subsequent full-row materialization still applies `FromProvider`. This enables batched and ordered non-simple typed-ID entity queries without enabling converted typed-store fast paths. Current and original invalidation keys use the same boundary. Metadata-free/provider key APIs remain unchanged, and generated provider-key accessors stay disabled for converted components. Generated `Get(...)` methods remain available through explicit column-aware model-key helpers. Composite reader keys remain open; scalar primary-key use from joined or relation callers is not yet characterized as a completed route. Reader-sourced index and relation operands and external lookup/query operands also remain open.

Work:

- make the dynamic/fallback key path normalize every key component to its canonical provider value
- normalize relation and cache-index keys through the same path
- support converted primary and foreign keys before adding generated fast paths
- document canonical provider equality and hashing requirements
- disable generated provider-key and relation fast paths for converted components when they still use the model CLR type; route those cases through the normalized dynamic fallback
- add generated optimized provider-key accessors only later, after the fallback path is proven correct and profiling shows value

Exit signal:

- dynamic primary-key lookup and relation matching work with typed IDs
- composite fallback keys can mix converted and unconverted scalar components correctly
- correctness does not depend on generated typed-key output
- cache identity does not depend on model-wrapper reference identity or provider wire bytes

### SC-4: Query Constants And Local Sequences

Progress on 2026-07-12: the expression-query SQL adapter now normalizes converter-backed values for direct equality/inequality in either operand order and for the existing local `Contains(...)`/equality-`Any(...)` membership plan. Ordered comparisons are rejected because the converter contract does not promise order preservation. The query-plan boundary produces canonical provider values and tags them with their target column. Canonical values remain visible to primary-key/cache analysis, while SQL binding lazily memoizes detached provider-writer physical values; both ordinary rendering and select-template cache hits use that physical view. Ordinary `ValueOperand` instances remain the already-physical/manual contract, so existing `SqlQuery`, source-row-loader, and provider-key paths are not double-converted or forced to expose a writer. The same handoff makes the existing MariaDB `BINARY(16)` membership regression pass without MySqlConnector `GuidFormat`, although broader UUID metadata/codec work remains in `UUID-1` through `UUID-5`. Cross-provider typed-ID predicate execution now covers the bounded integral-canonical cache-cold entity route, SQL `ScalarMember` results, and aliased anonymous SQL `SqlRow` column members; focused SQL/parameter inspection still uses SQLite. Collation/codec-sensitive keys and the remaining member-init row, aggregate, joined-local key handoff beyond primitive integral keys, neutral local-recipe hydration beyond integral SQL keys, and backend-handoff routes remain W4/W5 work. Explicit join compatibility, grouped predicates and non-column operands, typed-ID member access, and structured value-object predicates remain open.

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
- make memory predicates, ordering, membership, primary keys, and internal lookup identity operate on canonical provider values
- convert selected values back to model values during materialization
- keep SQL physical/wire codecs out of the memory executor
- keep relation traversal and relation-query support outside the 0.9 memory capability set

Exit signal:

- the read-only memory backend does not implement a second converter system
- typed IDs behave consistently for the memory query shapes included in 0.9
- public models materialized from memory expose the same model values as SQL-backed models

## UUID Handoff

The bounded 0.9 UUID storage slice begins with metadata/codec work after `SC-1` and consumes `SC-2` through `SC-5` at its provider, query/key, and validation boundaries. The granular prerequisites are defined by [UUID Storage Format Support](../../providers-and-features/UUID%20Storage%20Format%20Support.md); UUID work is not duplicated here.

Bounded `UUID-1A` progress on 2026-07-12 freezes the public declaration vocabulary, preserves raw declarations through every generation surface, and implements strict canonical-`Guid`/physical codec primitives. `UUID-1B` now resolves immutable provider-keyed definitions only after `ColumnScalarMapping` is authoritative, so explicit and assembly-registered typed IDs over canonical `Guid` receive the same physical-format rules as direct `Guid` properties. It uses parity-aligned built-in provider type rules and carries the result through generated runtime metadata; arbitrary downstream SQL-factory overrides remain outside provider-neutral resolution. Neither slice consumes `SC-2`: readers, writers, queries, keys, defaults, and schema behavior remain later UUID work.

SQLite UUID provider-consumption progress on 2026-07-13: A bounded SQLite-first `UUID-2` checkpoint requires exact resolved SQLite UUID storage metadata when mapped canonical `Guid` values cross the provider boundary. Direct full-row coverage spans Text36, Text32, little-endian BLOB, RFC-order BLOB, and nullable Text36/SQL NULL. A subsequent full-row-only converter bridge composes the same column-aware reader/writer boundary with a typed ID over RFC-order BLOB and a nullable typed ID over Text36/SQL NULL. Frozen non-symmetric writes, independent raw seeds, cache-cleared reads, and updates prove the physical/model boundary; UUID primary keys explicitly retain legacy read/write behavior. Full gates pass `1055/1055` unit and `760/760` SQLite compliance, and `DataLinq.SQLite` builds cleanly for `net8.0`, `net9.0`, and `net10.0`. Other typed formats, projection result decoding, UUID predicates/membership, UUID keys/cache/relations/delete-key paths, defaults, ambiguous BLOB metadata, other SQLite reader routes, memory, and Native AOT/browser publication remain outside this slice; aggregate `UUID-2` is not complete.

MySQL/MariaDB UUID provider-consumption progress on 2026-07-13: Provider-owned access and transaction readers carry exact MySQL versus MariaDB identity into non-primary-key full-row canonical `Guid` decoding, and the shared writer takes the same identity from the selected provider factory. Direct values span native MariaDB UUID, Text36, Text32, little-endian `BINARY(16)`, and RFC-order `BINARY(16)`. The converter-backed slice proves a typed ID whose definition is little-endian binary on MySQL and RFC-order binary on MariaDB, plus nullable typed Text36/SQL NULL. Binary reads use raw bytes; text/native reads accept raw text or connector-canonical `Guid`. Frozen non-symmetric writes, independent raw seeds, normal/transaction access, updates, provider-differentiated storage, and no/adversarial `GuidFormat` modes prove the boundary. Full gates pass `1055/1055` unit, `760/760` SQLite compliance, `429/429` on each server target (`1716/1716` total), and `414/414` combined provider-specific executions; `DataLinq.MySql` builds cleanly for `net8.0`, `net9.0`, and `net10.0`. Other typed formats, UUID primary keys, projections, predicates/membership, relations, defaults, provider-less public reader/access/transaction construction, memory, and Native AOT/browser publication remain outside the evidence claim; aggregate `UUID-2` is not complete.

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
- converter-backed direct scalar projection tests covering model, nullable, lifted-nullable, boxed, joined-source, terminal, and `AotStrict` result shapes across active SQL providers
- converter-backed aliased anonymous `SqlRow` projection tests covering root and joined sources, nullable, lifted-nullable, boxed, terminal, and `SqlOnlyCompatibility` result shapes across active SQL providers
- converter-backed explicit-constructor DTO/record `SqlRow` tests covering root and joined sources, nullable, lifted-nullable, boxed, terminal, exact constructor/result types, `SqlOnlyCompatibility`, and intentional `AotStrict` rejection across active SQL providers
- converter-backed implicit-relation `SqlRow` tests using primitive relation keys and converted related non-key columns, covering exact `ImplicitJoin` source metadata, nullable, lifted-nullable, boxed, terminal, and SQL join shapes across active SQL providers
- converter-backed single-source `ComputedRowLocal` `NewArray` recipe tests covering integral-keyed execution after explicit cache clearing, nullable, lifted-nullable, boxed-nullable, conditional, `AotStrict`, sequence, and terminal shapes across active SQL providers
- converter-backed joined-source `JoinedRowLocal` `NewArray` recipe tests over primitive integral PK/FK keys, covering exact root/explicit-join source and key metadata, nullable, lifted-nullable, boxed-nullable, conditional, explicit cache clearing, model-value non-leakage, `AotSafe` recipe disposition, and the enclosing projection's intentional `SqlOnlyCompatibility`/`AotStrict` fence across active SQL providers
- converter-backed direct `GroupedAggregate` key tests covering scalar, nullable, boxed, and named composite model shapes, ordinary raw aggregate members, and explicit provider-typed fallback across active SQL providers
- converter-backed numeric aggregate rejection tests covering scalar and grouped `Sum`, `Min`, `Max`, `Average`, grouped `HAVING`, pre-command failure, and unaffected selectorless counts across active SQL providers
- explicit typed-ID join-key tests for already-supported joins
- insert/update/default-value hydration tests

- SQLite non-primary-key full-row UUID provider-consumption tests covering direct `Guid` across Text36, Text32, little-endian BLOB, RFC-order BLOB, and nullable Text36/SQL NULL, plus representative converter-backed RFC BLOB and nullable typed Text36/SQL NULL through owned-transaction insert, update, exact raw write vectors, independently raw-seeded reads, and cache-cleared materialization
- MySQL/MariaDB non-primary-key full-row UUID provider-consumption tests covering direct native MariaDB UUID, Text36, Text32, little-endian/RFC `BINARY(16)`, nullable Text36/SQL NULL, and representative converter-backed provider-differentiated binary plus nullable typed Text36/SQL NULL under exact provider identity, normal/transaction readers, frozen raw writes, independent raw seeds, update re-encoding, and no/adversarial connector `GuidFormat` modes
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

- [0.9 Implementation Order And Integration Plan](Implementation%20Order%20and%20Integration%20Plan.md)
- [Scalar Converter Support](../../metadata-and-generation/Scalar%20Converter%20Support.md)
- [UUID Storage Format Support](../../providers-and-features/UUID%20Storage%20Format%20Support.md)
- [Memory Backend Architecture](../../backends/memory/Architecture.md)
- [DataLinq 0.9 Roadmap](README.md)
