> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 11 Implementation Plan: Cache Clearing and External Invalidation

**Status:** Planned after Phase 10.

## Purpose

Phase 11 adds explicit cache invalidation semantics after Phase 10 removes the worst key/allocation debt from the cache hot path.

This phase should be deliberately dull. The important feature is not a clever distributed cache. The important feature is a public, testable, provider-neutral way for applications to invalidate cached rows when data changes outside DataLinq's own mutation pipeline.

## Phase-Start Preconditions

Phase 11 should start from:

- Phase 9A invalidation tests and telemetry
- Phase 10 provider-key cache paths or a clearly documented temporary bridge
- mutation invalidation behavior that is characterized for update, delete, commit, and rollback
- benchmark baselines for warm primary-key fetch, relation traversal, and cache cleanup

If Phase 10 has not removed `IKey` from the relevant cache APIs yet, Phase 11 may use transitional adapters internally, but the public invalidation shape should still be provider-key-oriented.

## Goals

- expose explicit cache clearing APIs for database, table, and primary-key scopes
- make external invalidation provider-neutral and message-bus-agnostic
- invalidate relation and index cache state through the same path as local mutations
- define an invalidation event envelope for future CDC/message-bus adapters
- define a minimal row freshness vocabulary without forcing provider-backed hashes into this phase
- make invalidation telemetry identify source, scope, and cost

## Non-Goals

- automatic Kafka, Debezium, or database CDC integration
- transparent multi-process cache coherence
- dependency-tracked result-set caching
- memory-pressure cleanup and adaptive scheduling
- value/key interning or deduplication
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
database.Cache.Invalidate<TModel>(providerPrimaryKey);
database.Cache.Invalidate(TableDefinition table, IReadOnlyList<object?> providerPrimaryKey);
database.Cache.InvalidateMany(TableDefinition table, IReadOnlyList<IReadOnlyList<object?>> providerPrimaryKeys);
```

Tasks:

1. Decide where the public surface belongs: `Database`, provider state, cache facade, or a dedicated cache coordinator.
2. Add table-level and database-level clear operations.
3. Add primary-key invalidation by generated provider-key accessors.
4. Add composite provider-key invalidation without requiring `object?[]` on hot generated paths.
5. Define unknown-key behavior: no-op, diagnostic event, or exception depending on API shape.
6. Add unit tests for clear all, clear table, clear one row, clear many rows, and unknown row signals.

Exit criteria:

- applications can explicitly clear cached rows without internal access
- generated table APIs can call invalidation without constructing `IKey`
- table-level clear exists as a conservative fallback

## Workstream B: Relation And Index Invalidation

Goals:

- keep relation/index caches coherent after external signals
- avoid a separate invalidation path that diverges from mutation invalidation

Tasks:

1. Reuse mutation invalidation internals where possible.
2. Invalidate relation indexes affected by a row key.
3. Invalidate table-level relation/index state when key-level precision is unavailable.
4. Add tests for FK relation loading followed by external parent/child invalidation.
5. Add tests for changed relation/index columns where external invalidation cannot know the old value.

Exit criteria:

- relation traversal after external invalidation cannot return known-stale cached rows
- table-level invalidation remains a correct fallback when precise relation keys are unavailable
- mutation and external invalidation share the same core cache invalidation mechanics

## Workstream C: Invalidation Event Envelope

Goals:

- make future CDC/message-bus adapters possible without coupling this phase to one transport
- give telemetry and tests one normalized invalidation payload

Candidate fields:

- database name or generated database type
- table name or table model type
- provider primary-key components
- invalidation scope: row, rows, table, database
- source name: mutation, external, manual, cleanup, freshness, memory-pressure
- optional freshness/version token
- optional correlation id

Tasks:

1. Define the event DTO or internal record shape.
2. Support manual construction for application-driven invalidation.
3. Keep transport-specific metadata out of the core DTO.
4. Add validation for missing table/key fields.
5. Add tests for conservative downgrade when an event lacks precise keys.

Exit criteria:

- external adapters can feed invalidation without referencing cache internals
- invalidation signals are serializable or easy to serialize in application code
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

Tasks:

1. Add telemetry dimensions for invalidation source, scope, table, and approximate work performed.
2. Add stress tests for concurrent external invalidation during reads.
3. Add benchmark probes for invalidating one row, many rows, a table, and a database.
4. Update cache documentation only for shipped behavior.
5. Leave CDC, adaptive policy, and result-set caching explicitly deferred.

Exit criteria:

- cache invalidation behavior is test-covered by scope and source
- telemetry can explain why a cache entry disappeared
- benchmark evidence exists for invalidation overhead

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
- cleanup during active readers
- conservative table invalidation fallback

## Release Acceptance Criteria

Phase 11 can ship when:

- external invalidation works without optional bus/CDC dependencies
- database, table, and provider-key row invalidation scopes are public and tested
- relation/index invalidation does not diverge from mutation invalidation
- invalidation telemetry records source and scope
- docs explain that external invalidation is explicit signaling, not automatic distributed coherence
