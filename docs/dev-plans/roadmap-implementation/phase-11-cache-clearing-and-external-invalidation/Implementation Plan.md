> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 11 Implementation Plan: Cache Clearing and External Invalidation

**Status:** Next implementation priority.

## Purpose

Phase 11 adds explicit cache invalidation semantics after Phase 10 removes the worst key/allocation debt from the cache hot path.

This phase should be deliberately dull. The important feature is not a clever distributed cache. The important feature is a public, testable, provider-neutral way for applications to invalidate cached rows when data changes outside DataLinq's own mutation pipeline.

## Phase-Start Preconditions

Phase 11 should start from:

- Phase 9A invalidation tests and telemetry
- Phase 10 provider-key table/key descriptors and generated key accessors
- Phase 10 row-cache remove paths by provider key components
- Phase 10 relation/index invalidation hooks by provider key components, plus conservative table/index clear fallbacks
- Phase 10's confirmation that production source has no remaining `IKey` dependencies
- mutation invalidation behavior that is characterized for update, delete, commit, and rollback
- benchmark baselines for warm primary-key fetch, relation traversal, and cache cleanup

Phase 10 leaves `DataLinqKey` as a bounded dynamic provider-key carrier for metadata-driven paths. Phase 11 may use it as an internal adapter or explicit dynamic argument if that is the cleanest public shape, but it must not turn it into a new universal row-store identity abstraction.

## Phase 10 Handoff State

As of 2026-05-12, Phase 11 can rely on these artifacts:

- `TableDefinition.PrimaryKeyShape` and `TableKeyComponentDefinition` describe primary-key arity, provider/model CLR types, provider store kind, nullability, column ordinals, and scalar-converter placeholders.
- Generated table models install `IProviderKeyRowStoreAccessor` or `IProviderKeyDataReaderRowStoreAccessor` for primary-key tables.
- `RowCache.TryRemoveProviderKey<TKey>(...)` removes row-cache entries by provider key; table-level orchestration is internal through `TableCache.TryRemoveProviderKey<TKey>(...)`.
- Internal `TableCache.TryRemoveForeignKeyIndex<TKey>(...)` removes scalar relation/index entries by provider foreign-key value.
- Internal `TableCache.TryRemovePrimaryKeyIndex(...)`, plus public `ClearIndex()` and `ClearCache()`, provide the conservative fallback path.
- Cache maintenance telemetry already emits `datalinq.cache.operation`; current operation names live in `CacheMaintenanceOperations`.
- Phase 10 closeout artifacts are recorded in the Phase 10 measurement document.

## Goals

- expose explicit cache clearing APIs for database, table, and primary-key scopes
- make external invalidation provider-neutral and message-bus-agnostic
- invalidate relation and index cache state through the same path as local mutations
- avoid broad loaded-relation clearing when mutation or external invalidation impacts can be described by provider primary keys and relation keys
- define an invalidation event envelope for future CDC/message-bus adapters
- define a minimal row freshness vocabulary without forcing provider-backed hashes into this phase
- keep cache byte terminology honest so current row-payload estimates are not presented as total cache memory usage
- make invalidation telemetry identify source, scope, and cost

## Non-Goals

- automatic Kafka, Debezium, or database CDC integration
- transparent multi-process cache coherence
- dependency-tracked result-set caching
- memory-pressure cleanup and adaptive scheduling
- value/key interning or deduplication
- full cache memory-footprint accounting beyond terminology and Phase 12 handoff
- query parser replacement
- full migration execution

## Workstream A: Public Cache Clearing Surface

Goals:

- give applications explicit cache clearing tools
- keep the API boring enough to reason about

Candidate shape:

```csharp
database.Cache.Clear();
database.Cache.ClearTable<TModel>();
database.Cache.ClearTable(TableDefinition table);
database.Cache.Invalidate<TModel, TKey>(TKey providerPrimaryKey);
database.Cache.Invalidate<TModel>(DataLinqKeyComponents providerPrimaryKey);
database.Cache.Invalidate(TableDefinition table, DataLinqKeyComponents providerPrimaryKey);
database.Cache.InvalidateMany(TableDefinition table, IReadOnlyList<DataLinqKeyComponents> providerPrimaryKeys);
```

The exact public shape can differ. The important boundary is that generated and typed call sites should use provider-key components directly, while dynamic metadata call sites may use a small key-component carrier. That carrier must not become `IKey` under a new name.

Tasks:

1. Decide where the public surface belongs: `Database`, provider state, cache facade, or a dedicated cache coordinator.
2. Add table-level and database-level clear operations.
3. Add primary-key invalidation through Phase 10 generated/provider-key accessors.
4. Add a dynamic metadata fallback for composite provider-key invalidation without requiring `object?[]` on generated hot paths.
5. Define unknown-key behavior: no-op, diagnostic event, or exception depending on API shape.
6. Add unit tests for clear all, clear table, clear one row, clear many rows, composite keys, and unknown row signals.

Exit criteria:

- applications can explicitly clear cached rows without internal access
- generated table APIs can call invalidation without constructing `IKey`
- dynamic invalidation uses a bounded provider-key component carrier, not `IKey` or a revived universal key interface
- table-level clear exists as a conservative fallback

## Workstream B: Relation And Index Invalidation

Goals:

- keep relation/index caches coherent after external signals
- avoid a separate invalidation path that diverges from mutation invalidation
- make loaded `ImmutableRelation<T>` and `ImmutableForeignKey<T>` invalidation precise when affected keys are known
- preserve table-wide clearing as the explicit fallback when precision is unavailable

Tasks:

1. Reuse mutation invalidation internals where possible.
2. Invalidate relation indexes affected by provider key components.
3. Add an internal invalidation impact model that can carry table fallback, changed primary keys, and changed relation keys.
4. Extend relation-object subscription and notification matching so loaded relation objects are cleared by affected relation key or by intersection with changed loaded primary keys.
5. Normalize local `StateChange` mutation effects into that impact model before notifying relation subscribers.
6. Invalidate table-level relation/index state when key-level precision is unavailable or when Phase 10 left only a conservative fallback.
7. Add tests for FK relation loading followed by external parent/child invalidation.
8. Add tests for changed relation/index columns where external invalidation cannot know the old value.
9. Convert the current broad relation-clear characterization into a regression test for precise invalidation.

Exit criteria:

- relation traversal after external invalidation cannot return known-stale cached rows
- relation-object invalidation is precise for key-known local mutation and external invalidation paths
- duplicate same-target foreign keys are invalidated independently rather than by raw key value alone
- table-level invalidation remains a correct fallback when precise relation keys are unavailable
- mutation and external invalidation share the same core cache invalidation mechanics

Required companion design:

- [Precise Relation Cache Invalidation](Precise%20Relation%20Cache%20Invalidation.md)

## Workstream C: Invalidation Event Envelope

Goals:

- make future CDC/message-bus adapters possible without coupling this phase to one transport
- give telemetry and tests one normalized invalidation payload

Candidate fields:

- database name or generated database type
- table name or table model type
- provider primary-key components
- key arity and provider component types when useful for validation
- changed columns when known
- old and new relation/index provider-key values when known
- invalidation scope: row, rows, table, database
- source name: mutation, external, manual, cleanup, freshness, memory-pressure
- optional freshness/version token
- optional correlation id

Tasks:

1. Define the event DTO or internal record shape.
2. Support manual construction for application-driven invalidation.
3. Keep transport-specific metadata out of the core DTO.
4. Add validation for missing table/key fields.
5. Add tests for component-count/type mismatches.
6. Add tests for conservative downgrade when an event lacks precise primary keys or old/new relation/index values.
7. Ensure relation-object invalidation consumes the normalized event impact rather than a separate notification shape.

Exit criteria:

- external adapters can feed invalidation without referencing cache internals
- invalidation signals are serializable or easy to serialize in application code
- event handling validates key arity and provider component compatibility before touching cache internals
- old/new relation/index values can drive precise relation-object and index invalidation when supplied
- unsupported payloads fail clearly or downgrade conservatively

## Workstream D: Freshness Vocabulary

Goals:

- give later row-hash and result-set caching work shared terminology
- avoid implementing provider hash SQL before the invalidation API is stable

Tasks:

1. Define freshness states:
   - unknown
   - assumed fresh within current cache policy
   - externally invalidated
   - freshness checked
   - stale
2. Decide which states are represented in code now versus planning only.
3. Add telemetry names that do not need to change when provider-backed freshness checks arrive later.
4. Document that Phase 11 invalidation does not prove external freshness; it reacts to explicit signals.

Exit criteria:

- code and docs can distinguish invalidation from freshness proof
- result-set caching and row-version work can reuse the vocabulary

## Workstream E: Diagnostics, Benchmarks, And Closeout

Goals:

- prove invalidation correctness and cost
- keep public docs honest
- characterize the current cache byte metric as row-payload-oriented, not total cache memory

Tasks:

1. Add telemetry dimensions for invalidation source, scope, table, and approximate work performed.
2. Add stress tests for concurrent external invalidation during reads.
3. Add benchmark probes for invalidating one row, many rows, a table, and a database.
4. Record whether invalidation used precise provider-key removal or conservative table fallback.
5. Avoid using the current cache byte gauge as the cost basis for invalidation unless it is named as approximate row payload bytes.
6. Document the Phase 12 handoff for cache memory accounting: row payload, row-store overhead, transaction caches, index caches, relation-object caches, notification queues, and cache history.
7. Update cache documentation only for shipped behavior.
8. Leave CDC, adaptive policy, memory-pressure cleanup, full memory accounting, and result-set caching explicitly deferred.

Exit criteria:

- cache invalidation behavior is test-covered by scope and source
- telemetry can explain why a cache entry disappeared
- benchmark evidence exists for invalidation overhead
- closeout states which invalidation paths are provider-key precise and which are conservative fallbacks
- no Phase 11 metric or doc claims current `Bytes`/`datalinq.cache.bytes` represents total cache memory footprint

Required companion design:

- [Cache Memory Accounting](../../performance/Cache%20Memory%20Accounting.md)

## Verification

Baseline checks:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

Provider checks:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --targets sqlite-file,sqlite-memory --output failures --build
```

Stress checks should cover:

- concurrent external invalidation
- concurrent relation/index invalidation
- concurrent targeted relation-object invalidation
- cleanup during active readers
- conservative table invalidation fallback

## Release Acceptance Criteria

Phase 11 can ship when:

- external invalidation works without optional bus/CDC dependencies
- database, table, and provider-key row invalidation scopes are public and tested
- generated invalidation paths use Phase 10 provider-key accessors or explicitly documented temporary bridges
- relation/index invalidation does not diverge from mutation invalidation
- loaded relation objects are invalidated precisely when affected primary keys or relation keys are known
- invalidation telemetry records source and scope
- docs explain that external invalidation is explicit signaling, not automatic distributed coherence
