> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Cache Memory Accounting

**Status:** Design input for Phases 11 and 12.

## Purpose

DataLinq currently reports cache "bytes", but that value is not a managed-memory footprint. It is a cheap estimate of row payload bytes.

That distinction matters because cache limits, diagnostics, and future memory-pressure cleanup can become actively misleading if they pretend the existing number includes keys, dictionaries, index caches, relation caches, transaction caches, and runtime object overhead. It does not.

The right fix is not to chase exact CLR heap accounting row by row. Exact object-size accounting in .NET is brittle, runtime-dependent, expensive, and usually false precision. The right fix is to name the current value honestly, then add a broader estimated footprint model whose components are explicit enough to reason about and cheap enough to maintain.

## Current Behavior

The current byte path is:

```text
RowData.Size
  -> RowStore<TKey>.RowEntry.Size
  -> RowStore<TKey>.TotalBytes
  -> RowCache.TotalBytes
  -> TableCache.TotalBytes
  -> CacheOccupancyMetricsSnapshot.Bytes
  -> datalinq.cache.bytes
```

`RowData.Size` is computed while reading column values:

- fixed-size scalar model values use `ValueProperty.CsSize`
- strings use `Length * sizeof(char) + sizeof(int)`
- byte arrays use `Length`
- null values count as zero

That value is useful, but it is row payload accounting. It is not total cache memory accounting.

## What Is Missing

The current byte value does not include:

- `RowData` object overhead
- the dense `object?[]` backing array inside each `RowData`
- boxed value-type objects stored in that array
- immutable model instance objects and generated lazy fields
- `RowStore<TKey>.RowEntry` objects
- `Dictionary<TKey, RowEntry>` buckets, entries, spare capacity, and comparer overhead
- provider primary keys, generated composite keys, and dynamic `DataLinqKey` component arrays
- transaction-local row caches
- relation index caches, including foreign-key dictionaries, `DataLinqKey[]` primary-key arrays, and reverse maps
- loaded `ImmutableRelation<T>` state such as `ImmutableArray<T>` and `FrozenDictionary<DataLinqKey,T>`
- loaded `ImmutableForeignKey<T>` holders
- cache-notification queues, weak references, and subscription records
- cache snapshot/history objects

The practical result is blunt: a table can report modest `Bytes` while retaining substantial memory through relation indexes, relation-object caches, transaction caches, and dictionary/key overhead.

## Terminology

Use explicit names. Avoid calling any estimate "memory usage" unless the estimate includes enough components to deserve the phrase.

Recommended vocabulary:

`RowPayloadBytes`
: Estimated value payload stored in cached rows. This is approximately what the current `Bytes` gauge represents.

`RowStoreOverheadBytes`
: Estimated overhead for row-store entries, dictionary buckets/entries, keys, row data containers, and immutable model objects.

`TransactionRowPayloadBytes`
: Estimated value payload stored in transaction-local row caches.

`TransactionRowStoreOverheadBytes`
: Estimated overhead for transaction-local row-store structures.

`IndexPayloadBytes`
: Estimated payload retained by index caches: foreign keys, primary-key arrays, and reverse-map keys.

`IndexOverheadBytes`
: Estimated dictionary, queue, immutable-array, and bucket overhead retained by index caches.

`RelationObjectBytes`
: Estimated memory retained by loaded relation/reference objects and their materialized relation collections.

`NotificationBytes`
: Estimated memory retained by cache-notification queues and weak-reference subscription records.

`SnapshotBytes`
: Estimated memory retained by cache history snapshots.

`EstimatedCacheBytes`
: Sum of the estimated cache-owned components DataLinq can account for cheaply.

The key word is estimated. The goal is useful operational signal, not a heap profiler.

## Phase Split

### Phase 11 Responsibilities

Phase 11 should not implement full memory accounting. It should prevent new invalidation APIs and telemetry from further entrenching the ambiguous `Bytes` meaning.

Tasks:

1. Characterize the current `Bytes` metric as estimated row payload bytes in Phase 11 closeout notes.
2. Avoid using current `Bytes` as the cost basis for invalidation telemetry.
3. Document the Phase 12 decision: keep the existing byte-limit settings and enum values, but change their implementation basis from row payload to estimated cache footprint.
4. Ensure invalidation telemetry reports units honestly:
   - rows removed
   - index entries removed
   - relation subscribers cleared
   - approximate payload bytes removed, if available
5. Hand Phase 12 a concrete list of cache-owned structures that must be included in broader memory accounting.

Exit condition for Phase 11:

- no new Phase 11 API, metric, or document claims that current cache byte reporting is total cache memory usage

### Phase 12 Responsibilities

Phase 12 owns the implementation of broader estimated cache memory accounting.

Tasks:

1. Preserve the existing byte-limit settings and enum values while changing their accounting basis to estimated cache footprint.
2. Add a `CacheMemoryEstimate` or equivalent internal snapshot with component-level byte counts.
3. Make row stores report both payload and estimated overhead.
4. Make transaction-local row caches contribute to memory estimates.
5. Make index caches report entry counts plus estimated payload and overhead.
6. Decide whether relation-object caches are cache-owned enough to report directly, or whether subscription telemetry should report their approximate retained values.
7. Add notification queue memory estimates.
8. Decide whether `CacheHistory` should be included in cache memory estimates or separately reported as diagnostics overhead.
9. Update existing size-based cleanup to use `EstimatedCacheBytes`, not row-payload bytes.
10. Expand diagnostics/reporting so operators can see the corrected total estimate and the important component estimates.
11. Add benchmarks that compare the estimate with observed managed heap deltas under controlled scenarios.

Exit condition for Phase 12:

- existing `Bytes`, `Kilobytes`, `Megabytes`, and `Gigabytes` limits use the corrected estimated-cache-footprint basis, and operators can still inspect row payload size as a component when needed

## Suggested Internal Shapes

Sketch:

```csharp
internal readonly record struct CacheMemoryEstimate(
    long RowPayloadBytes,
    long RowStoreOverheadBytes,
    long TransactionRowPayloadBytes,
    long TransactionRowStoreOverheadBytes,
    long IndexPayloadBytes,
    long IndexOverheadBytes,
    long RelationObjectBytes,
    long NotificationBytes,
    long SnapshotBytes)
{
    public long EstimatedCacheBytes =>
        RowPayloadBytes +
        RowStoreOverheadBytes +
        TransactionRowPayloadBytes +
        TransactionRowStoreOverheadBytes +
        IndexPayloadBytes +
        IndexOverheadBytes +
        RelationObjectBytes +
        NotificationBytes +
        SnapshotBytes;
}
```

Public diagnostics should expose enough fields to make the estimate explainable. At minimum, expose `RowPayloadBytes` and `EstimatedCacheBytes`; preferably expose the major components as well. The internal accounting should always keep components separate. A single opaque estimate is hard to debug and easy to misuse.

## Estimation Guidance

The estimator should be boring and conservative:

- use `IntPtr.Size` for reference-sized fields
- include object headers with a documented approximation
- include array headers and element slots
- include dictionary entry and bucket arrays based on current capacity if available, otherwise count-based approximations
- include string payload as UTF-16 chars plus object/array overhead
- include byte-array payload plus array overhead
- include `DataLinqKey` component arrays for composite dynamic keys
- treat generated composite key structs as inline dictionary key payload where possible
- avoid reflection in hot paths
- keep estimates updated through running counters where updates are already centralized

Do not call the result exact. It will not be exact. It needs to be stable, comparable, and directionally honest.

## Cleanup Policy Implications

Current `CacheLimitType.Bytes`, `Kilobytes`, `Megabytes`, and `Gigabytes` act on the current row-payload estimate. That is not wrong if documented as row payload, but it is wrong if sold as total cache memory.

The project decision is to take the behavioral break for cache limit cleanup and keep the existing byte-limit settings as-is. Phase 12 should make the existing byte-based limit types use the corrected `EstimatedCacheBytes` basis. Do not add parallel row-payload or estimated-footprint limit settings just to preserve the old behavior.

This limit-setting decision does not block adding richer reporting fields. In fact, adding component-level reporting is required so users can understand why a byte limit is being hit. The old cleanup behavior was not a useful contract; it was a misleading implementation detail. Keeping the cache limit values and fixing the basis makes user code simpler and makes the docs more honest. The closeout notes must call out the breaking semantic change clearly.

## Verification Plan

Minimum tests:

- row payload bytes increase and decrease on row add/remove/clear
- transaction-local row payload contributes to memory estimates
- index cache estimates increase when relation keys are cached and drop on index clear
- composite keys and dynamic `DataLinqKey` component arrays contribute to estimates
- relation-object estimates or relation subscription diagnostics reflect loaded collections
- notification queue estimates increase on subscribe and drop after notify/clean
- size-based cleanup uses the existing byte-limit settings with the corrected estimated-cache-footprint basis
- telemetry/snapshot sums match table-to-provider-to-runtime aggregation
- public diagnostics expose corrected total estimate plus row-payload/component detail

Benchmark probes:

- warm primary-key fetch with memory estimates enabled
- warm relation traversal with index and relation-object estimates enabled
- row-cache add/get/remove with accounting enabled
- cache clear and table clear with accounting enabled
- large relation index preload with accounting enabled

Comparison checks:

- load a controlled number of rows and compare estimated deltas with `GC.GetTotalMemory(forceFullCollection: true)` before/after
- treat comparison as a sanity range, not an equality assertion
- record expected under/over-count boundaries in closeout notes

## Non-Goals

- exact managed heap accounting
- per-object heap walking
- runtime-specific object layout dependence beyond documented approximations
- global string/key interning as a hidden accounting fix
- changing cache eviction behavior without benchmark and retention evidence
