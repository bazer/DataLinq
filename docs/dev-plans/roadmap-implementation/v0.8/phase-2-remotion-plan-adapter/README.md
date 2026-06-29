> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 2: Remotion Plan Adapter

**Status:** Complete.

## Execution Plan

- [Implementation Plan](Implementation%20Plan.md)

## Purpose

Phase 2 introduces the DataLinq query plan while Remotion still parses expressions. This is the central strangler step: Remotion becomes one producer of DataLinq-owned plan nodes instead of the shape that the rest of query execution depends on.

## Scope

In scope:

- define immutable query plan nodes
- represent source slots explicitly
- separate query shape from captured runtime values
- represent predicates, ordering, paging, projection, result operators, local sequences, joins, and relation-existence predicates
- add `RemotionQueryPlanAdapter`
- add normalized plan snapshot tests

Out of scope:

- deleting Remotion
- replacing `Queryable<T>`
- moving all SQL generation at once
- adding new LINQ support

## Plan Requirements

The plan must be backend-neutral enough that SQL is one consumer, not the design center. It needs:

- stable source-slot identities
- explicit predicate nodes for boolean logic, comparisons, membership, fixed conditions, and relation existence
- explicit value nodes for columns, constants, captured values, local sequences, converted provider values, and functions
- operation order preservation for ordering and paging
- projection nodes that distinguish entity, scalar member, anonymous, and computed row-local projection
- result nodes for sequence, single/first/last variants, count, any, and scalar aggregates

## Exit Criteria

- representative Remotion-parsed queries can be converted to `DataLinqQueryPlan`
- snapshot tests make plan shape reviewable
- captured values are not baked into reusable query shape
- the old execution path can still run while the plan model is proven

## Completion Notes

Implemented in the internal `DataLinq.Linq.Planning` namespace:

- immutable query plan source, operation, predicate, value, projection, result, binding, and debug writer types
- `RemotionQueryPlanAdapter` as the temporary Remotion-to-DataLinq plan producer
- normalized snapshots that redact captured scalar and local-sequence values
- focused unsupported-shape tests for post-paging filters, `GroupJoin`, and post-join filters

The production SQL execution path is intentionally unchanged. Phase 3 is the handoff point for moving SQL generation and diagnostics onto `DataLinqQueryPlan`.
