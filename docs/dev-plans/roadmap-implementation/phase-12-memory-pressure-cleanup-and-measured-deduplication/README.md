> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 12: Memory-Pressure Cleanup and Measured Deduplication

**Status:** Planned after Phase 11.

## Purpose

Phase 12 improves cache cleanup behavior after the identity and invalidation foundations are in place.

This is where memory-pressure-aware cleanup, adaptive scheduling, and scoped value/key deduplication belong. They are intentionally behind explicit cache clearing because adaptive behavior without clear invalidation semantics is harder to explain and harder to debug.

## Execution Boundary

In scope:

- memory-pressure-aware cleanup through a testable abstraction
- cleanup scheduling that can react to churn without unbounded background work
- lazy cache snapshot/history work where behavior allows
- `IndexCache` reverse-map concurrency cleanup
- `RowCache.TotalBytes` running counters or equivalent low-cost telemetry
- measured value/key deduplication or scoped interning only where benchmarks prove a win

Out of scope:

- public external invalidation APIs
- transparent distributed cache coherence
- dependency-tracked result-set caching
- relation-aware join API work
- query parser replacement

## Source Plans

- [Memory Optimization and Deduplication](../../performance/Memory%20Optimization%20and%20Deduplication.md)
- [Memory management](../../performance/Memory%20management.md)
- [Allocation Reduction Audit](../../performance/Allocation%20Reduction%20Audit.md)
- [Phase 11 Cache Clearing and External Invalidation](../phase-11-cache-clearing-and-external-invalidation/README.md)

## Recommended Order

1. Make provider construction avoid eager cache-history work where behavior allows.
2. Add an injectable memory-pressure reader and cleanup policy tests.
3. Add deterministic cleanup scheduling tests with a fake clock.
4. Fix correctness-adjacent cache internals such as mutable reverse-index buckets.
5. Benchmark scoped value/key deduplication prototypes under allocation, contention, and retention checks.
6. Adopt only the deduplication strategies that clearly win.

## Exit Criteria

Phase 12 is done when:

- cleanup can react to memory pressure through a testable abstraction
- users can configure or disable pressure-triggered cleanup
- cache cleanup telemetry explains why cleanup ran
- cache internals avoid known concurrency and repeated-size-computation smells
- any deduplication strategy has benchmark evidence and a retention story
- no unbounded global interner is introduced as a shortcut
