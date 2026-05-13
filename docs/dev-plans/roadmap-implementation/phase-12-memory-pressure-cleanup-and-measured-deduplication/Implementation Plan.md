> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 12 Implementation Plan: Memory-Pressure Cleanup and Measured Deduplication

**Status:** Ready for implementation as of 2026-05-13, after the Phase 11 branch was merged to `master`.

## Purpose

Phase 12 turns the cache cleanup story from "periodically remove old rows using a row-payload byte counter" into something operationally honest:

- cleanup can run because configured limits were exceeded or because the process is under memory pressure
- diagnostics explain the difference between row payload bytes and estimated cache footprint
- byte-based cache limits keep their existing configuration surface but use the corrected estimated footprint basis
- deduplication is allowed only where benchmarks prove it reduces allocation or retained heap without adding bad contention or retention behavior

The brutally important distinction is that this phase is not a heap profiler. It should produce stable, explainable estimates and better cleanup decisions. Exact managed-object accounting is not a realistic goal in .NET and pretending otherwise would make the feature look more precise while being less useful.

## Phase-Start Inventory

Phase 12 starts after Phase 11 delivered explicit cache clearing, external invalidation, freshness vocabulary, invalidation telemetry, and honest documentation for the existing row-payload byte gauge.

Current cache state to build on:

- `CacheOccupancyMetricsSnapshot.Bytes` and `datalinq.cache.bytes` are estimated row-payload bytes, not total cache memory footprint.
- `RowStore<TKey>` already maintains a running `totalBytes` row-payload counter. The old "recompute row bytes on every read" issue is no longer present.
- `DatabaseCache` already avoids eager cache-history snapshots. `DatabaseCacheTests.Constructor_DoesNotCreateHistorySnapshotUntilRequested` covers that behavior.
- `IndexCache` no longer uses mutable `List<T>` buckets for the primary-key reverse map. It uses `ImmutableArray<TKey>` values. Phase 12 should still harden add/remove races and accounting, but it should not pretend the old mutable-list implementation is still current.
- `CleanCacheWorker` is still a timer-style worker. It calls `CleanRelationNotifications()` and `RemoveRowsBySettings()` and records cleanup through table maintenance paths, but it has no memory-pressure reader, no cleanup reason policy, and no deterministic clock abstraction.
- `DatabaseCache` currently stores a single `CleanCacheWorker?`, but its constructor loops through `Policy.CacheCleanup`. If multiple cleanup intervals are configured, earlier workers can be overwritten by later ones. Phase 12 should consolidate this worker model before adding pressure-triggered scheduling.
- `TableCache.RemoveRowsByLimit(...)` applies row and byte limits through `RowCache`, while age cleanup also removes old index entries. Phase 12 must make limit-driven eviction and index/relation cleanup agree.
- Transaction-local caches, index caches, relation-object subscriptions, notification queues, and cache snapshots are not part of the current byte estimate.

## Ground Rules

1. Keep `Bytes` honest. Treat existing `Bytes`/`TotalBytes` members as legacy row-payload surfaces unless they are deliberately replaced with a documented break.
2. Add explicit names before changing behavior: `RowPayloadBytes`, `EstimatedCacheBytes`, and component estimates.
3. Keep existing `CacheLimitType.Bytes`, `Kilobytes`, `Megabytes`, and `Gigabytes` settings. Change their implementation basis to `EstimatedCacheBytes` and call out the breaking semantic change in closeout notes.
4. Do not add parallel row-payload byte-limit settings just to preserve misleading old behavior.
5. Avoid reflection in hot cache paths. Estimates should be maintained by counters or cheap count-based calculations at centralized mutation points.
6. Do not introduce a global value/key interner. Any interning must be scoped, bounded, benchmark-led, and removable.
7. Do not let accounting retain objects merely to count them. Weak relation subscriptions must stay weak.
8. Keep pressure cleanup configurable and disableable through a real public policy surface. Do not hide product behavior behind environment variables.
9. Public docs must describe only shipped behavior. Roadmap or benchmark-only experiments stay in dev plans and benchmark notes.

## Workstream A: Memory Estimate Vocabulary And Primitives

Status: implemented.

Goals:

- establish one internal estimate model for cache-owned memory
- make row payload and estimated footprint distinct in code
- keep compatibility aliases explicit instead of ambiguous

Tasks:

1. Add an internal `CacheMemoryEstimate` record struct under `DataLinq.Cache` with these components:
   - `RowPayloadBytes`
   - `RowStoreOverheadBytes`
   - `TransactionRowPayloadBytes`
   - `TransactionRowStoreOverheadBytes`
   - `IndexPayloadBytes`
   - `IndexOverheadBytes`
   - `RelationObjectBytes`
   - `NotificationBytes`
   - `SnapshotBytes`
   - computed `EstimatedCacheBytes`
2. Add a small `CacheMemoryEstimator` helper for documented object-layout approximations:
   - reference size from `IntPtr.Size`
   - object header estimate
   - array header estimate
   - dictionary bucket/entry approximations
   - string and byte-array payload plus container overhead
   - `DataLinqKey` component-array overhead
3. Use saturating or defensive addition where estimates can aggregate many components. A bad estimate should not overflow into a negative cleanup basis.
4. Rename internal row-payload variables where practical. For public or semi-public existing surfaces such as `TableCache.TotalBytes`, `DatabaseCacheSnapshot.TotalBytes`, and `CacheOccupancyMetricsSnapshot.Bytes`, keep compatibility but document and back them with `RowPayloadBytes`.
5. Add unit tests for:
   - component addition
   - zero/empty estimate behavior
   - overflow protection
   - formatted byte aliases still matching row payload where retained

Exit criteria:

- new cache accounting code never has to guess what `Bytes` means
- row payload and estimated footprint can be tested independently
- public compatibility aliases are deliberate, not accidental

Implementation notes:

- 2026-05-13: Added the first vocabulary layer: `CacheMemoryEstimate`, `CacheMemoryEstimator`, and explicit row-payload aliases on existing cache byte surfaces. Existing `TotalBytes`/`Bytes` names remain compatibility aliases for row payload; Workstream B owns non-row component counters and Workstream C owns public diagnostic expansion.

## Workstream B: Component-Level Cache Accounting

Status: in progress.

Goals:

- account for every major cache-owned structure cheaply enough for normal use
- expose enough component detail to explain why cleanup ran

Tasks:

1. Extend `IRowStore` and `RowCache` with memory-estimate reporting.
   - Keep the existing row-payload counter.
   - Add estimated row-store overhead for `RowEntry`, dictionary entries/buckets, provider keys, `RowData`, the dense `object?[]`, and immutable row instances.
   - Prefer counters updated on add/remove/clear over full dictionary walks.
2. Include transaction-local row caches.
   - Aggregate transaction-local row payload into `TransactionRowPayloadBytes`.
   - Aggregate transaction-local row-store structures into `TransactionRowStoreOverheadBytes`.
   - Add tests that a read/write transaction cache changes the estimate and drops after `RemoveTransaction`.
3. Add estimate reporting to `IIndexCache` and `TypedIndexCache<TKey>`.
   - Count foreign-key dictionary entries.
   - Count `DataLinqKey[]` primary-key arrays.
   - Count reverse-map keys and `ImmutableArray<TKey>` values.
   - Count tick queue entries.
   - Keep index payload and index overhead separate.
4. Harden `IndexCache` reverse-map updates while adding accounting.
   - The old mutable-list bucket is gone, but `RemoveReverseMapping` still needs clear concurrent add/remove semantics.
   - Use either a lock around reverse-map mutation or atomic compare/update loops. Pick the simpler design that makes tests boring.
5. Add notification queue estimates to `CacheNotificationManager`.
   - Estimate subscription records, weak references, relation keys, and loaded primary-key arrays.
   - Track enough counters during subscribe, notify, requeue, and clean so estimates do not require expensive queue walks.
6. Decide the relation-object accounting boundary.
   - Preferred: account for relation metadata and retained primary-key arrays carried by subscriptions, and leave actual weakly referenced relation object internals out unless they can self-report without becoming strongly retained.
   - Do not turn weak relation tracking into a memory leak in order to improve an estimate.
7. Account for cache history snapshots at the database level.
   - `CacheHistory` is already lazy and bounded; include `SnapshotBytes` in database-level estimates or expose it as diagnostics overhead.
   - Make the decision explicit in docs and tests.
8. Add table, provider, and runtime aggregation tests.

Exit criteria:

- row stores, transaction caches, index caches, notification queues, and snapshots contribute to estimates
- component estimates drop when those structures are cleared
- relation-object accounting does not introduce strong references
- estimates are cheap enough to read from metrics without surprising allocations

Implementation notes:

- 2026-05-13: Started component accounting with internal row-store estimates. `RowCache` now reports row payload separately from row-store overhead, with per-row overhead counters updated on add/remove/clear and transaction-local row caches remapped into transaction payload/overhead at the table estimate boundary.
- 2026-05-13: Added index-cache estimates for retained primary-key arrays, foreign-key dictionaries, reverse-map dictionaries, `ImmutableArray` backing arrays, and tick queues. Reverse-map mutation now runs under the cache lock with explicit counter adjustments instead of optimistic `ConcurrentDictionary` update delegates.
- 2026-05-13: Added notification and snapshot estimates. Notification queues count subscription records, weak references, and queue overhead in `NotificationBytes`; relation subscriptions count retained relation keys and loaded primary-key arrays in `RelationObjectBytes` without counting or strengthening weakly referenced relation objects. Cache history is included as database-level `SnapshotBytes` diagnostics overhead because it is lazy and bounded.

## Workstream C: Diagnostics And Byte-Limit Cutover

Goals:

- expose the corrected total estimate and its components
- change byte-based cleanup to use estimated footprint
- keep telemetry compatibility where the old name already means row payload

Tasks:

1. Expand `CacheOccupancyMetricsSnapshot`.
   - Add `RowPayloadBytes` and `EstimatedCacheBytes`.
   - Add major component fields when practical: row-store overhead, transaction payload/overhead, index payload/overhead, relation object bytes, notification bytes, and snapshot bytes.
   - Keep `Bytes` as a legacy alias for row payload unless the implementation intentionally chooses a source-breaking cleanup.
2. Expand `DataLinqMetrics` table/provider/runtime state to store and aggregate the new occupancy fields.
3. Expand OpenTelemetry gauges.
   - Keep `datalinq.cache.bytes` as row payload for compatibility with Phase 11 documentation.
   - Add an explicit row-payload gauge if useful, for example `datalinq.cache.row_payload.bytes`.
   - Add `datalinq.cache.estimated.bytes`.
   - Add component gauges only for the high-value fields; do not create a noisy metric per tiny internal object.
4. Update `TableCacheSnapshot` and `DatabaseCacheSnapshot`.
   - Add `RowPayloadBytes`, `EstimatedCacheBytes`, and formatted variants.
   - Keep `TotalBytes` as a row-payload alias only if compatibility needs it.
5. Add cleanup paths that remove rows while preserving index/relation consistency.
   - Row/size-limit eviction should return or process removed primary keys so loaded index caches and relation subscriptions do not retain stale state.
   - Reuse Phase 11 invalidation mechanics where possible instead of adding another silent eviction path.
6. Change table byte limits to compare against `EstimatedCacheBytes`.
   - `CacheLimitType.Bytes`, `Kilobytes`, `Megabytes`, and `Gigabytes` should keep their names and configured values.
   - The cleanup loop should stop when the table estimate is under the converted byte limit, not when row payload is under it.
7. Characterize database-scoped byte limits before changing them.
   - If the current behavior effectively applies a database byte limit independently to each table, add a characterization test.
   - Prefer correcting database byte limits to enforce the total database estimate, with a release-note callout, because Phase 12 is already taking the byte-limit semantic break.
8. Extend cleanup telemetry with cleanup reason and basis.
   - Existing operation names such as `size_limit`, `age_limit`, and `row_limit` should stay stable.
   - Add tags or snapshot fields that distinguish scheduled cleanup, manual clear/invalidation, and memory-pressure cleanup.
   - When cleanup is size-based, record whether the basis was `estimated_cache_bytes`.
9. Update docs after implementation:
   - `docs/Diagnostics and Metrics.md`
   - `docs/dev-plans/performance/Cache Memory Accounting.md`
   - this implementation plan closeout

Exit criteria:

- operators can see row payload and estimated cache footprint side by side
- byte-based cache limits use estimated footprint
- stale index/relation state is not left behind by limit-driven row eviction
- docs clearly call out the breaking byte-limit semantic change

## Workstream D: Memory-Pressure Policy And Cleanup Scheduling

Goals:

- make cleanup react to process memory pressure through a testable abstraction
- make worker scheduling bounded, disposable, and deterministic under test
- keep pressure cleanup configurable and understandable

Tasks:

1. Replace the one-worker-per-cleanup-policy constructor loop with a single coordinated cleanup scheduler.
   - The current single `CleanCacheWorker?` property cannot safely represent multiple configured cleanup intervals.
   - The replacement should own all configured schedule intervals and stop every background task on dispose.
2. Add deterministic time and threading seams.
   - Prefer `TimeProvider` if it fits all target frameworks.
   - Otherwise add a tiny internal clock abstraction.
   - Add a fake clock and fake worker runner for unit tests.
3. Add an internal memory-pressure reader.
   - Suggested shape: `IMemoryPressureReader.GetSnapshot()`.
   - Default implementation should use `GC.GetGCMemoryInfo()` and `GC.GetTotalMemory(forceFullCollection: false)` where available.
   - Browser/WASM should report unsupported or disabled pressure cleanup.
4. Add a cleanup policy evaluator.
   - Inputs: memory-pressure snapshot, cache estimate, configured limits, last cleanup time, recent cleanup result, and churn counters.
   - Output: cleanup decision with reason, severity, and target budget.
   - Reasons should include at least `scheduled`, `row_limit`, `size_limit`, `age_limit`, `memory_pressure`, and `manual`.
5. Add a public configuration surface for pressure-triggered cleanup.
   - Preferred direction: metadata-backed cache policy, because cache limits and cleanup intervals already live in metadata.
   - Keep defaults conservative.
   - Provide a clear disable path.
   - Do not rely on environment variables as the product API.
6. Implement bounded pressure cleanup.
   - Avoid an unbounded background loop under sustained pressure.
   - Use cooldowns and maximum rows/bytes per pass.
   - Escalate from table-local cleanup to database-level cleanup only through explicit policy decisions.
7. Record cleanup telemetry for pressure-triggered work.
   - Reuse `CacheInvalidationSources.MemoryPressure` where the invalidation vocabulary fits.
   - Add cleanup-specific reason/basis data where invalidation metrics are the wrong abstraction.
8. Add tests for:
   - pressure reader unsupported/disabled path
   - low pressure no-op
   - high pressure schedules cleanup once
   - sustained high pressure respects cooldown/bounds
   - multiple cleanup intervals do not leak workers
   - dispose stops all background activity
   - browser runtime does not start pressure cleanup

Exit criteria:

- pressure cleanup is testable without real memory pressure
- users can configure or disable pressure-triggered cleanup
- scheduler behavior is deterministic under unit tests
- background cleanup cannot spin indefinitely under sustained pressure

## Workstream E: Measured Deduplication And Scoped Interning

Goals:

- reduce retained cache memory only where repeated values make it worthwhile
- avoid global retention and contention traps
- keep dense `RowData` intact unless measurements justify a change

Tasks:

1. Establish benchmarks before writing production dedup code.
   - Warm primary-key fetch with cache estimates enabled.
   - Warm relation traversal with relation/index caches.
   - Row-cache add/get/remove.
   - Large relation index preload.
   - High-cardinality string workload.
   - Low-cardinality repeated string workload.
   - Composite dynamic key workload.
2. Prototype scoped dedup strategies behind benchmark-only or internal flags first.
   - Candidate: per-table/per-column bounded string pool for short repeated strings.
   - Candidate: scoped `DataLinqKey` component-array dedup for dynamic composite keys.
   - Candidate: relation/index key array reuse only if ownership is unambiguous.
3. Reject unsafe strategies by default.
   - Do not intern `byte[]`.
   - Do not intern floating-point values.
   - Do not intern GUIDs unless data proves a real win.
   - Do not intern long strings without a size cap.
   - Do not intern high-cardinality columns.
4. Define adoption gates before productionizing a prototype.
   - Retained heap must improve in controlled GC-delta probes.
   - Allocation must improve in BenchmarkDotNet results for the target workload.
   - Throughput regression must be negligible or justified by a large retained-memory win.
   - Churn tests must prove the pool is bounded and old values can disappear.
5. Integrate accepted dedup with cleanup.
   - If a scoped pool is adopted, it needs compaction or bounded eviction through the cleanup scheduler.
   - The pool's memory estimate must be counted.
6. Document rejected prototypes.
   - A rejected dedup strategy with numbers is useful; a hand-wavy "maybe later" is not.

Exit criteria:

- no global unbounded interner exists
- every adopted dedup strategy has before/after benchmark evidence
- every adopted pool is bounded and has a retention story
- rejected prototypes are recorded with enough data to avoid repeating the same experiment blindly

## Workstream F: Verification, Documentation, And Closeout

Goals:

- prove behavior and cost
- leave implementation docs accurate for the next phase

Required verification:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq\DataLinq.csproj -c Debug -v minimal --no-incremental
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug -v minimal --no-incremental
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -c Debug -v minimal --no-incremental
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

Benchmark lanes to add or extend:

```powershell
$env:DATALINQ_BENCHMARK_PROVIDERS='sqlite-memory'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase12-cache-memory --profile smoke
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase12-cache-memory --profile default --history-json artifacts\benchmarks\history\phase12-cache-memory-YYYYMMDD.json
```

Heap sanity probes:

- load a controlled number of rows and compare `EstimatedCacheBytes` deltas with `GC.GetTotalMemory(forceFullCollection: true)` before and after
- repeat with relation/index preloads
- repeat with notification subscribers
- treat these as sanity ranges, not equality assertions

Documentation updates:

- close out each workstream in this file as it lands
- update the Phase 12 README status and exit criteria
- update shipped diagnostics docs only after the new metrics/API are implemented
- update `Cache Memory Accounting.md` with the final estimate components and any intentionally excluded structures
- update benchmark notes with retained-heap and allocation evidence for accepted or rejected dedup prototypes

Release acceptance criteria:

- cleanup can react to memory pressure through a fakeable abstraction
- pressure cleanup can be configured or disabled
- background cleanup is bounded and all workers are disposable
- diagnostics expose row payload, estimated cache footprint, and major components
- byte-based limits use estimated cache footprint
- cleanup telemetry explains why cleanup ran
- row/size/age eviction keeps row, index, relation, notification, and accounting state coherent
- any dedup strategy has benchmark evidence and bounded retention behavior
- no docs imply exact managed heap accounting

## Suggested Commit Slices

1. Add `CacheMemoryEstimate` primitives and row-payload naming.
2. Add row-store and transaction-cache memory estimates.
3. Add index and notification memory estimates.
4. Expand occupancy snapshots, metrics, and telemetry gauges.
5. Convert byte-limit cleanup to estimated footprint and repair limit eviction consistency.
6. Replace cleanup worker scheduling with a single bounded scheduler and fake clock tests.
7. Add memory-pressure reader, policy evaluator, configuration, and telemetry.
8. Add Phase 12 benchmark lanes and GC-delta sanity probes.
9. Prototype scoped dedup strategies and adopt only measured wins.
10. Update shipped docs and Phase 12 closeout notes.
