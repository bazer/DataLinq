> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 9A: Release Hardening, Benchmarks, Allocation, and Cache Invalidation

**Status:** Complete as of 2026-05-10.

## Purpose

Phase 9A is the release-hardening slice before DataLinq adds broader cache semantics.

The goal is not to ship a clever new cache system yet. The goal is to make the existing runtime quieter, easier to measure, lower allocation, and more defensible around invalidation:

- clean compiler warnings instead of hiding them
- make benchmark history and the website useful for long-term trend interpretation
- implement the measured allocation-reduction work
- characterize current cache invalidation behavior before changing it
- harden cache invalidation around mutation and transaction boundaries
- clean small cache internals that are already known to be allocation or concurrency risks

That combination is large, but it is coherent. It improves trust in the product before the follow-up Phase 10-12 key/cache work introduces provider-key cache paths, external invalidation hooks, memory-pressure cleanup, and adaptive policy.

## Execution Boundary

In scope:

- warning cleanup from the current warning cleanup plan
- benchmark-history and website trend improvements
- allocation audit workstreams for metadata collections, metadata lookup maps, key value access, generated metadata startup, query temporary arrays, and cache internals
- invalidation characterization tests
- conservative invalidation hardening for update/delete, changed relation/index columns, transaction commit/rollback, and cache notifications
- telemetry and benchmark coverage for cache invalidation and macro read/write behavior

Out of scope:

- row hashing or row-version freshness checks
- external invalidation APIs for host applications or message queues
- adaptive cache heuristics and startup database profiling
- global key/value deduplication unless a narrow allocation fix proves independently worthwhile
- dependency-tracked result-set caching
- async provider pipeline work
- Remotion replacement or query-plan migration

## Source Plans

- [Implementation Plan](Implementation%20Plan.md)
- [Benchmark Closeout](Benchmark%20Closeout.md)
- [Warning Cleanup Plan](../../tooling/Warning%20Cleanup%20Plan.md)
- [Representative Benchmark Suite and Website Trends](../../../performance/Representative%20Benchmark%20Suite%20and%20Website%20Trends.md)
- [Allocation Reduction Audit](../../../performance/Allocation%20Reduction%20Audit.md)
- [Memory Optimization and Deduplication](../../../performance/Memory%20Optimization%20and%20Deduplication.md)
- [Memory management](../../../performance/Memory%20management.md)

## Recommended Order

1. Re-establish warning and benchmark baselines.
2. Complete warning cleanup.
3. Upgrade benchmark history and website trend interpretation.
4. Implement metadata and key allocation reductions.
5. Add cache invalidation characterization tests.
6. Harden invalidation and low-risk cache internals.
7. Finish generated metadata and query temporary-array allocation reductions.
8. Re-measure and close the phase with documented benchmark evidence.

## Exit Criteria

Phase 9A is done when:

- solution warning cleanup is complete or has a documented remaining third-party/environment exception
- benchmark history distinguishes profile, last-run date, and long-term trend information clearly
- published benchmark history uses age-based retention or has an implementation-ready thinning path
- public metadata array APIs have been replaced by stable read-only collection APIs
- runtime metadata lookups avoid repeated copied-array scans on hot paths
- key access no longer allocates arrays just to read existing key values
- provider initialization and measured hot-path allocations are re-baselined after the allocation work
- cache invalidation tests cover update, delete, indexed/relation column changes, commit, rollback, and relation notifications
- `IndexCache` reverse mappings are thread-safe under mutation
- `DatabaseCache` does not eagerly create initial history snapshots unless observable behavior requires it
- `RowCache.TotalBytes` does not repeatedly sum the queue on every read
- telemetry and benchmark output can explain whether cache invalidation changed scope or cost

The [benchmark closeout](Benchmark%20Closeout.md) records the final measured boundary. Allocation evidence is usable; closeout timings were too noisy for latency claims.
