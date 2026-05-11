> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 16: Dependency-Tracked Result-Set Caching

**Status:** Deferred until cache invalidation, freshness vocabulary, joins, and projection semantics are stronger.

## Purpose

Phase 16 adds explicit cached computation scopes and dependency fingerprints for application-level result caching.

This should stay late. A result cache that cannot explain exactly which rows it depends on and when those rows became invalid is worse than a normal TTL cache because it looks more trustworthy than it is.

## Execution Boundary

In scope:

- explicit cached computation scopes
- row dependency fingerprints collected during reads
- validation of stamped results against current dependency state
- integration with Phase 11 invalidation envelopes and Phase 13/14 projection/join semantics
- documentation that keeps result-set caching separate from row-cache behavior

Out of scope:

- transparent caching of arbitrary LINQ result sets
- automatic distributed cache coherence
- provider-specific CDC clients
- replacing application caches such as `IMemoryCache`
- Remotion replacement

## Source Plans

- [Result set caching](../../query-and-runtime/Result%20set%20caching.md)
- [Projections and Views](../../query-and-runtime/Projections%20and%20Views.md)
- [Phase 11 Cache Clearing and External Invalidation](../phase-11-cache-clearing-and-external-invalidation/README.md)
- [Phase 13 Explicit Multi-Join Composition](../phase-13-explicit-multi-join-composition/README.md)
- [Phase 14 Relation-Aware Joins and Left Joins](../phase-14-relation-aware-joins-and-left-joins/README.md)

## Recommended Order

1. Define the dependency fingerprint format.
2. Add an explicit read-tracking scope.
3. Stamp user-computed results with dependency fingerprints.
4. Validate stamped results against current row state or invalidation state.
5. Integrate dependency invalidation with existing application cache examples.
6. Benchmark validation overhead against simple recomputation.

## Exit Criteria

Phase 16 is done when:

- applications can explicitly track reads for a computed result
- stamped results can be validated without reloading full rows
- invalidation and freshness behavior is explainable in diagnostics
- docs make clear this is an application-cache coordination feature, not magic TTL replacement
