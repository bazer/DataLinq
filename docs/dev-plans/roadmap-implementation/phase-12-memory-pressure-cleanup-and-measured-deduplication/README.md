> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 12: Memory-Pressure Cleanup and Measured Deduplication

**Status:** Ready for implementation after the Phase 11 merge. The implementation plan was drafted on 2026-05-13.

## Purpose

Phase 12 improves cache cleanup behavior after the identity and invalidation foundations are in place.

This is where memory-pressure-aware cleanup, adaptive scheduling, and scoped value/key deduplication belong. They are intentionally behind explicit cache clearing because adaptive behavior without clear invalidation semantics is harder to explain and harder to debug.

Phase 12 also owns broad cache memory accounting. The existing cache byte value is a row-payload estimate, not a full managed-memory footprint. Memory-pressure cleanup should not be built on that number without first separating row payload, row-store overhead, transaction caches, index caches, relation-object caches, notification queues, and snapshot/history overhead.

The byte-limit settings should stay as-is. Phase 12 may make the breaking behavioral change that existing byte-based limits use the corrected estimated cache footprint rather than the old row-payload estimate. Reporting is different: diagnostics should grow enough fields to explain the estimate.

## Execution Boundary

In scope:

- memory-pressure-aware cleanup through a testable abstraction
- cleanup scheduling that can react to churn without unbounded background work
- component-level cache memory estimates that distinguish row payload from broader cache footprint
- expanded cache occupancy reporting for corrected total estimate and major component estimates
- lazy cache snapshot/history work where behavior allows
- `IndexCache` reverse-map concurrency cleanup
- corrected byte-based cleanup using estimated cache footprint while keeping the existing limit settings
- measured value/key deduplication or scoped interning only where benchmarks prove a win

Out of scope:

- public external invalidation APIs
- transparent distributed cache coherence
- dependency-tracked result-set caching
- relation-aware join API work
- query parser replacement
- exact CLR heap accounting or per-object heap walking

## Source Plans

- [Implementation Plan](Implementation%20Plan.md)
- [Cache Memory Accounting](../../performance/Cache%20Memory%20Accounting.md)
- [Memory Optimization and Deduplication](../../performance/Memory%20Optimization%20and%20Deduplication.md)
- [Memory management](../../performance/Memory%20management.md)
- [Allocation Reduction Audit](../../performance/Allocation%20Reduction%20Audit.md)
- [Phase 11 Cache Clearing and External Invalidation](../phase-11-cache-clearing-and-external-invalidation/README.md)

## Recommended Order

1. Make provider construction avoid eager cache-history work where behavior allows.
2. Rename or supplement ambiguous internal byte accounting so row payload and estimated cache footprint are separate concepts.
3. Add component-level memory estimates for row stores, transaction caches, index caches, relation-object caches, notification queues, and snapshot/history overhead.
4. Expand diagnostics/reporting with corrected total cache estimate plus row-payload/component details.
5. Add an injectable memory-pressure reader and cleanup policy tests.
6. Add deterministic cleanup scheduling tests with a fake clock.
7. Fix correctness-adjacent cache internals such as mutable reverse-index buckets.
8. Change existing size-based cleanup to use estimated cache bytes, and call out the breaking semantic change in closeout notes.
9. Benchmark scoped value/key deduplication prototypes under allocation, contention, and retention checks.
10. Adopt only the deduplication strategies that clearly win.

## Exit Criteria

Phase 12 is done when:

- cleanup can react to memory pressure through a testable abstraction
- users can configure or disable pressure-triggered cleanup
- cache cleanup telemetry explains why cleanup ran
- diagnostics distinguish estimated row payload bytes from estimated total cache footprint
- cache occupancy reporting exposes enough component detail to explain the estimate
- cache internals avoid known concurrency and byte-accounting smells
- existing byte-based cache limits use estimated cache footprint without adding parallel cache-limit settings
- any adopted deduplication strategy has benchmark evidence and a retention story
- no unbounded global interner is introduced as a shortcut
