> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 14: Source-Slot Join Composition

**Status:** Implemented for the explicit two-source join composition slice.

## Purpose

Phase 14 resumed join work after the query plan existed, the 0.8 AOT/browser release gates had tooling, Phase 13 had made single-source operator ordering composable, and Phase 13B had added the first single-source grouped aggregate result shape. This is where the old Phase 13 explicit-join plan became useful again, but rebased on DataLinq source slots instead of Remotion query-source identities.

This phase used to be the 0.8 Phase 8 follow-up. It moved behind the AOT/browser evidence work because 0.8 should first make browser AOT actually run, report, and deploy at sensible sizes. It should still remain in the 0.8 line: joins are the next query feature users will hit after the parser boundary is owned by DataLinq.

Start from the current [LINQ Parser Architecture](../../../../internals/LINQ%20Parser%20Architecture.md), not from the older Remotion-shaped join notes. The existing source slots, `JoinedRowLocal` projection path, primary-key based joined materialization, and current join exclusions are the baseline to extend.

Every supported join shape must work from both read-only and transaction-local query roots:

```csharp
var q = db.Query();
var tq = transaction.Query();
```

That should be a natural consequence of implementing joins as query-provider behavior over `IQueryable<T>` sources, not as methods tied to `Database<T>`.

## Implemented Scope

The shipped Phase 14 slice is deliberately narrower than the original broad join target:

- preserve and strengthen the current narrow explicit `Join(...)` baseline
- add source-slot plan tests for explicit joined rows
- support flat filtering, ordering, paging, `Any`, and `Count` over joined projection members that bind back to source-slot values
- keep joined materialization on provider-key components
- prepare relation-aware and implicit relation joins on top of the same source-slot model

Out of scope for the shipped Phase 14 slice:

- query-syntax joins and transparent identifiers; Phase 20 owns that follow-up
- joined post-paging pushdown after `Skip(...)` or `Take(...)`; Phase 21 owns that follow-up
- multiple chained joins unless Phase 20 deliberately proves the compiler-lowered shape
- left joins before inner-join composition is stable
- relation-aware or implicit syntax that hides a weak explicit join engine
- arbitrary composite-key and grouped join shapes unless deliberately added

## Source Plans

- [Old Phase 13 Explicit Multi-Join Composition](../../phase-13-explicit-multi-join-composition/README.md)
- [Old Phase 14 Relation-Aware Joins and Left Joins](../../phase-14-relation-aware-joins-and-left-joins/README.md)
- [0.8 Phase 13 Query Composition and Subquery Pushdown](../phase-13-query-composition-and-subquery-pushdown/README.md)
- [0.8 Phase 15 Relation-Aware and Implicit Joins](../phase-15-relation-aware-and-implicit-joins/README.md)
- [0.8 Phase 20 Query-Syntax Join Support](../phase-20-query-syntax-join-support/README.md)
- [0.8 Phase 21 Joined Post-Paging Pushdown](../phase-21-joined-post-paging-pushdown/README.md)
- [Relation-Aware Join API](../../../query-and-runtime/Relation-Aware%20Join%20API.md)
- [Implementation Plan](Implementation%20Plan.md)

## Exit Criteria

- explicit two-source joins compose through source-slot-aware plan nodes
- flat joined predicates, ordering, paging, `Any`, and `Count` bind to source-slot values
- supported joined queries work from both `db.Query()` and `transaction.Query()`
- query-root alias examples use `var q = db.Query();` instead of repeated `db.Query().Table`
- relation-aware and implicit join work has a stable source-slot foundation to build on
- unsupported join shapes fail with focused diagnostics
- user docs and the support matrix describe only shipped join behavior
