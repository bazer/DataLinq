> [!WARNING]
> This document is roadmap implementation material for the DataLinq 0.9 development line. It is not normative product documentation and should not be treated as a shipped support claim.

# 0.9 Join And Grouping Continuation Implementation Plan

**Status:** Draft.

**Created:** 2026-07-04.

## Purpose

This document keeps the immediate 0.9 implementation plan for bounded SQL-backed join and grouping continuation.

The durable API and design discussion remains in [Relation-Aware Join API](../../query-and-runtime/Relation-Aware%20Join%20API.md), while current shipped behavior remains documented in [Supported LINQ Queries](../../../Supported%20LINQ%20Queries.md) and the [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md).

## 0.9 Goal

The 0.9 query-continuation work should extend the DataLinq-owned query plan where it already has pressure:

1. practical multi-inner-join pipelines
2. grouped aggregate rows over those joined source-slot shapes
3. relation-aware inner joins only after ordinary explicit joins are boring

The first release claim should stay narrow:

> DataLinq 0.9 extends SQL-backed source-slot join composition and grouped aggregate projection for documented query shapes.

This is not a general LINQ provider expansion. It is a continuation of the 0.8 source-slot, projection-row, and grouped-row work.

## Execution Boundary

In scope:

- standard C# query syntax with multiple inner joins
- chained explicit `Join(...)` support beyond the current single-join boundary
- filtering, ordering, paging, `Any()`, and `Count()` over supported multi-join projection rows
- SQL-backed direct projection rows over multi-join source slots
- grouping over supported multi-join projection rows when key and aggregate selectors bind to source-slot values
- typed-ID/provider-value join key normalization once scalar converters are available
- parity between `db.Query()` and `transaction.Query()` roots
- focused diagnostics for unsupported join and grouping shapes
- `JoinBy(...)` and `JoinMany(...)` as stretch work only after explicit multi-join composition is stable

Out of scope for the baseline:

- materialized `IGrouping<TKey,TElement>` sequences
- grouped element enumeration
- `GroupJoin(...)`
- left/outer join patterns such as `DefaultIfEmpty()`
- `LeftJoinBy(...)`, `LeftJoinMany(...)`, and standard `Queryable.LeftJoin(...)`
- composite anonymous-object join keys unless the provider-value and source-slot machinery is ready
- implicit collection projection or hidden row multiplication
- row-local computed joined projection members used for provider-side post-paging composition
- client-side fallback for unsupported SQL query shapes

Left joins are a possible stretch after inner join composition and relation-aware inner joins. They are not a baseline 0.9 claim.

## Recommended Order

### Phase 5A: Multi-Join Query Syntax And Chained `Join(...)`

Work:

- add failing tests for two and three explicit inner joins
- cover compiler-lowered transparent identifiers for multi-join query syntax
- generalize source-slot and alias handling beyond two sources
- select primary keys and direct projection aliases for every source slot
- preserve read-only and transaction-root execution parity

Exit signal:

- practical multi-table inner joins work through standard query syntax
- direct source-slot projection rows materialize from SQL aliases
- unsupported opaque transparent identifiers fail with useful diagnostics

### Phase 5B: Composition Over Multi-Join Rows

Work:

- support `Where(...)`, `OrderBy(...)`, `ThenBy(...)`, `Skip(...)`, and `Take(...)` over direct multi-join projection members
- support `Any()` and `Count()` over supported multi-join row shapes
- preserve operator order through derived joined sources when later operators apply after paging
- keep row-local computed joined projections out of provider-side composition

Exit signal:

- multi-join rows compose like the existing single-join supported slice
- post-paging composition uses explicit derived-source boundaries where flattening would be wrong
- provider behavior matches in-memory LINQ expectations for the documented shapes

### Phase 5C: Grouping Over Multi-Join Source Slots

Work:

- group over supported multi-join direct projection rows
- support group keys that bind to source-slot values or already-supported SQL-renderable functions
- support grouped `Count()`, direct numeric `Sum(...)`, `Min(...)`, `Max(...)`, and `Average(...)`
- support grouped-row filtering, ordering, paging, `Any()`, and `Count()` where existing grouped-row rules apply
- reject grouped element enumeration and materialized `IGrouping`

Exit signal:

- grouped aggregate rows can use multi-join inputs without inventing a second grouping model
- diagnostics keep unsupported grouped join and `GroupJoin` shapes outside the support boundary

### Phase 5D: Relation-Aware Inner Joins As Stretch

Work:

- add a focused relation-expression resolver if explicit joins are stable
- implement `JoinBy(...)` for singular generated relations
- implement `JoinMany(...)` for generated collection relations
- support relation access through already-joined anonymous row shapes
- keep join-local `on:` predicates and left joins out unless the inner-join API is already boring

Exit signal:

- relation metadata can drive inner joins without explicit key selectors
- relation-aware joins compose after earlier explicit joins
- collection relation expansion remains explicit through `JoinMany(...)`

## Verification Gates

The 0.9 join/grouping continuation should not be called supported until these are green:

- expression-parser snapshot tests for multi-join source slots
- SQL-shape tests proving deterministic aliases and joined source maps
- provider behavior tests across SQLite, MySQL, and MariaDB
- transaction-root tests for each supported shape
- typed-ID join key tests if scalar converters ship before the join slice
- unsupported-shape tests for `GroupJoin`, left joins, opaque transparent identifiers, row-local joined composition, and materialized grouping
- support-matrix and user-doc updates that list only shipped behavior

## Release Boundary

The 0.9 release can claim join/grouping expansion only when:

- multi-join query syntax is tested from active providers
- joined rows compose through SQL-backed projection members
- grouping over joined rows uses the same grouped aggregate row model as 0.8
- unsupported joins and grouping shapes fail with `QueryTranslationException`
- docs do not imply left joins, grouped joins, or materialized `IGrouping`

Possible stronger claims, if earned:

- relation-aware `JoinBy(...)` and `JoinMany(...)` inner joins
- typed-ID join keys through scalar converters
- grouped aggregate rows over relation-aware inner joins

Claims to avoid unless proven:

- "full join support"
- "LINQ GroupJoin support"
- "left join support"
- "navigation-property query parity with EF Core"
- "general GroupBy support"

## Links

- [Relation-Aware Join API](../../query-and-runtime/Relation-Aware%20Join%20API.md)
- [LINQ Parser Architecture Review](../../query-and-runtime/LINQ%20Parser%20Architecture%20Review.md)
- [Supported LINQ Queries](../../../Supported%20LINQ%20Queries.md)
- [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)
- [DataLinq 0.9 Rough Roadmap](README.md)
