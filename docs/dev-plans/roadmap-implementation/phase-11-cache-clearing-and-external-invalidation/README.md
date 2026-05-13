> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 11: Cache Clearing and External Invalidation

**Status:** In progress. Workstream A public cache clearing surface is implemented.

## Purpose

Phase 11 turns the cache from an internal implementation detail into something host applications can explicitly coordinate with.

The priority is cache clearing first, not magical freshness. Applications need a boring, reliable way to say "this table changed" or "this row changed" when work happens outside the current `Database` instance. Row freshness terminology should be defined here so later phases have a clean vocabulary, but provider-backed hash/version checks should not block the first external invalidation surface.

## Execution Boundary

In scope:

- explicit cache clearing APIs for database, table, and primary-key scopes
- invalidation event envelopes suitable for later CDC/message-bus adapters
- relation/index invalidation through the same internals as mutation invalidation
- precise relation-object invalidation for loaded `ImmutableRelation<T>` and `ImmutableForeignKey<T>` instances when affected keys are known
- provider-key invalidation through Phase 10 key descriptors/accessors, with any dynamic key carrier kept bounded and separate from legacy `IKey`
- cache telemetry that identifies mutation, external, cleanup, memory-pressure, and freshness-related invalidation sources
- cache-byte terminology that does not treat the current row-payload estimate as total cache memory usage
- a minimal row freshness vocabulary for later validation and result-set caching work

Out of scope:

- dependency-tracked result-set caching
- automatic Kafka, Debezium, or database CDC clients
- transparent distributed cache coherence
- adaptive cache policy and memory-pressure cleanup
- broad cache memory-footprint accounting beyond honest terminology and telemetry handoff
- broad provider-backed row hashing
- query-plan migration or Remotion replacement

## Source Plans

- [Implementation Plan](Implementation%20Plan.md)
- [Precise Relation Cache Invalidation](Precise%20Relation%20Cache%20Invalidation.md)
- [Phase 10 Implementation Plan](../phase-10-key-and-allocation-foundation/Implementation%20Plan.md)
- [Provider-Key Row Cache Architecture](../../../Provider-Key%20Row%20Cache%20Architecture.md)
- [Cache Memory Accounting](../../performance/Cache%20Memory%20Accounting.md)
- [Phase 9A Implementation Plan](../../archive/roadmap-implementation/phase-9a-release-hardening-benchmarks-allocation-cache-invalidation/Implementation%20Plan.md)
- [Distributed Cache Coordination and CDC](../../architecture/Distributed%20Cache%20Coordination%20and%20CDC.md)
- [Memory management](../../performance/Memory%20management.md)
- [Result set caching](../../query-and-runtime/Result%20set%20caching.md)

## Exit Criteria

Phase 11 is done when:

- applications can explicitly clear all cached data, one table, or specific primary-key rows
- external invalidation can use provider-key values without constructing legacy `IKey` objects or reintroducing an equivalent universal key interface
- dynamic invalidation does not reintroduce a universal key abstraction under a new name
- relation and index cache entries are invalidated consistently with mutation invalidation
- loaded relation objects are invalidated by affected relation key or loaded primary key when precision is available, with table-wide clearing reserved as a correctness fallback
- invalidation telemetry records source, table, scope, and approximate cost
- Phase 11 documentation and telemetry do not claim the existing cache byte gauge is total cache memory usage
- unsupported or unknown invalidation signals fail predictably or degrade to conservative table invalidation
- later CDC and result-set caching plans have a stable invalidation envelope to build on
