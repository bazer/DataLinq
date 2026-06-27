> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 4: Supported-Subset Expression Parser

**Status:** In progress.

## Execution Plan

- [Implementation Plan](Implementation%20Plan.md)

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
- one-to-many relation `Any(...)` and existence-equivalent `Count()` predicates
- current narrow explicit `Join(...)` baseline

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
- documented support passes on the new parser, including current relation-existence predicates and the narrow explicit join baseline
- unsupported shapes have focused diagnostics
- any deliberate contraction of documented support is recorded as a breaking release decision before Phase 7 removes Remotion
