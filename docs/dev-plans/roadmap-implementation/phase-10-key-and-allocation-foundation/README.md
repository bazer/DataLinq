> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 10: Key and Allocation Foundation

**Status:** Active implementation. Workstreams A through E are implemented as of 2026-05-12.

## Purpose

Phase 10 removes the allocation and identity debt that would otherwise leak into every later cache and join feature.

The main target is straightforward: generated code already knows table identity, primary-key columns, provider CLR types, relation columns, and row ordinals. Runtime hot paths should not allocate `IKey` wrappers, object arrays, or defensive metadata snapshots just to rediscover those facts.

## Execution Boundary

In scope:

- non-copying metadata collection access and frozen lookup maps
- generated provider-key row/cache accessors
- relation and query lookup paths that avoid lookup-only `IKey` construction
- deletion of the legacy `IKey` abstraction rather than a long-lived transitional compatibility layer
- generated static `Get(...)` and materialization paths that use provider key components directly
- before/after allocation benchmarks for provider initialization, warm primary-key fetch, relation traversal, and query hot paths

Out of scope:

- public external invalidation APIs
- memory-pressure cleanup policy
- relation-aware join API design
- full scalar converter/typed-ID ergonomics
- result-set caching
- Remotion replacement

## Source Plans

- [Implementation Plan](Implementation%20Plan.md)
- [Measurement Baseline](Measurement%20Baseline.md)
- [Generated Provider-Key Cache Design](../../performance/Generated%20Provider-Key%20Cache%20Design.md)
- [Allocation Reduction Audit](../../performance/Allocation%20Reduction%20Audit.md)
- [Source Generator Optimizations](../../metadata-and-generation/Source%20Generator%20Optimizations.md)
- [Scalar Converter Support](../../metadata-and-generation/Scalar%20Converter%20Support.md)

## Recommended Order

1. Refresh allocation baselines from the existing benchmark lanes.
2. Replace defensive metadata array snapshots with stable read-only collection APIs and internal non-copying accessors.
3. Add frozen metadata lookup maps for table and column resolution.
4. Add generated/provider-key row-store paths for scalar primary keys.
5. Remove relation lookup-only `IKey` construction on generated relation paths.
6. Convert query/materialization key reads to provider-key components where practical.
7. Re-measure allocations and confirm no production `IKey` dependencies remain before moving to Phase 11.

## Exit Criteria

Phase 10 is done when:

- runtime provider initialization and hot query paths no longer depend on defensive metadata array snapshots
- generated scalar primary-key cache hits avoid DataLinq-owned key allocations
- generated relation traversal avoids lookup-only key objects for the common scalar-FK path
- legacy `IKey` types and public APIs are deleted
- benchmark artifacts show the before/after allocation impact
- later cache invalidation APIs can be designed around provider key components rather than `IKey`
