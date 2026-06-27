> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 4: Supported-Subset Expression Parser

**Status:** Planned after Phase 3.

## Purpose

Phase 4 builds the DataLinq parser over `System.Linq.Expressions`. It emits the same `DataLinqQueryPlan` that the Remotion adapter emits.

This parser should be boring and narrow. A small correct parser for DataLinq's documented query subset is better than a heroic general LINQ provider that almost works.

## Scope

In scope for the first parser slice:

- `Where`
- `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`
- `Select`
- `Skip`
- `Take`
- `Any`
- `Count`
- `Single`, `SingleOrDefault`
- `First`, `FirstOrDefault`
- `Last`, `LastOrDefault`
- documented scalar aggregates
- documented predicate comparisons, boolean grouping, string members, date/time members, and nullable shapes
- documented local collection membership and empty fixed-condition behavior

Conditional in scope:

- one-to-many relation `Any(...)` and existence-equivalent `Count()` predicates
- current narrow explicit `Join(...)` baseline

Those conditional items should be included if practical. If not, they must remain explicit compatibility-only shapes until a follow-up parser slice.

Out of scope:

- arbitrary local `Enumerable` method chains
- `GroupBy(...)`
- `GroupJoin(...)`
- outer joins
- composite-key joins
- broad nested database subqueries
- client-side predicate fallback

## Diagnostics

Unsupported shapes should fail with `QueryTranslationException` or an equivalent focused DataLinq exception. The message should identify the unsupported operator, expression shape, selector, predicate, or provider capability gap.

## Exit Criteria

- DataLinq-owned `IQueryable<T>` and `IQueryProvider` can parse representative queries into `DataLinqQueryPlan`
- documented single-source support passes on the new parser
- unsupported shapes have focused diagnostics
- relation and join parity are either implemented or explicitly marked compatibility-only
