> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 9B: Row Freshness, External Invalidation, and Adaptive Cache Policy

**Status:** Planned follow-up release after Phase 9A.

## Purpose

Phase 9B is the first broader cache-semantics phase after the Phase 9A hardening release.

Phase 9A should make the existing cache behavior measured, lower allocation, and better characterized. Phase 9B builds on that by adding controlled new semantics:

- row freshness primitives
- external invalidation hooks
- memory-pressure-aware cleanup
- adaptive cache policy where evidence supports it
- measured key/value deduplication if it wins without introducing retention or contention bugs

This phase should remain grounded in benchmark and telemetry evidence. It should not turn into a magical cache that makes freshness claims it cannot prove.

## Execution Boundary

In scope:

- row versioning or hash-based freshness primitives
- explicit external invalidation APIs
- invalidation event envelopes that can be fed by host applications or later CDC integrations
- memory-pressure-aware cleanup
- adaptive cleanup scheduling
- limited adaptive cache heuristics with clear user overrides
- measured value/key deduplication or scoped interning if it proves worthwhile
- telemetry that distinguishes mutation, external signal, freshness check, cleanup, and memory-pressure invalidation

Out of scope:

- dependency-tracked result-set caching
- transparent distributed cache coherence
- automatic Kafka or CDC client integrations
- startup database profiling that issues broad `COUNT(*)` or table scans without opt-in
- query-plan migration or Remotion replacement
- async provider pipeline work
- full migration execution

## Source Plans

- [Implementation Plan](Implementation%20Plan.md)
- [Phase 9A Implementation Plan](../phase-9a-release-hardening-benchmarks-allocation-cache-invalidation/Implementation%20Plan.md)
- [Distributed Cache Coordination and CDC](../../architecture/Distributed%20Cache%20Coordination%20and%20CDC.md)
- [Memory Optimization and Deduplication](../../performance/Memory%20Optimization%20and%20Deduplication.md)
- [Memory management](../../performance/Memory%20management.md)
- [Result set caching](../../query-and-runtime/Result%20set%20caching.md)

## Recommended Order

1. Review Phase 9A invalidation tests, telemetry, and benchmark baselines.
2. Define the row freshness contract before implementing hashing.
3. Add external invalidation primitives and local API tests.
4. Add memory-pressure-aware cleanup and scheduling improvements.
5. Add adaptive policy only behind explicit policy boundaries and telemetry.
6. Evaluate key/value deduplication with measured contention and retention checks.
7. Close with benchmarks, stress tests, and updated public documentation.

## Exit Criteria

Phase 9B is done when:

- row freshness behavior is explicit and tested
- external invalidation APIs can invalidate by table and primary key without requiring a message-bus dependency
- cache telemetry identifies invalidation source and scope
- memory-pressure cleanup is observable and configurable
- adaptive policy has safe defaults, clear opt-out, and benchmark evidence
- any deduplication or interning work proves memory wins without unacceptable contention or object retention
- result-set caching has a stronger foundation but remains a later phase
