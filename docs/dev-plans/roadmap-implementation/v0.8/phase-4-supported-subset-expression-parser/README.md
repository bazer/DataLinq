> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 4: Supported-Subset Expression Parser

**Status:** Complete.

## Execution Plan

- [Implementation Plan](Implementation%20Plan.md)

## Closeout

Phase 4 closed after adding the DataLinq-owned expression parser and a non-executing queryable harness that can build supported query expression trees without Remotion.

Closeout evidence:

- `ExpressionQueryPlanParser` converts supported expression trees directly into `DataLinqQueryPlan`
- `ExpressionQueryPlanProvider` and `ExpressionPlanQueryable<T>` provide the parser-owned queryable construction path needed before the runtime switch
- parser parity coverage compares the DataLinq parser against the Remotion adapter oracle for representative single-source chains, scalar/result operators, aggregates, relation-existence predicates, local membership, string/date/nullable predicates, and the narrow explicit join baseline
- unsupported parser shapes have focused diagnostics for post-paging filters, `GroupBy(...)`, `GroupJoin(...)`, post-join filtering, relation projection, and nested database projection
- the parser namespace has no Remotion parser, clause, result-operator, query-model, or query-source type exposure
- public execution remains Remotion-backed until Phase 6 deliberately switches runtime routing

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

## Phase 5/6 Handoff

Phase 5 should treat parser-local evaluation and post-materialization projection as temporary support seams until they are either made AOT-clean or explicitly isolated outside the supported constrained-platform path.

Known handoff points:

- parser support currently uses expression inspection and local value capture to create plan bindings; Phase 5 should inventory which of those paths still invoke reflection dynamically
- row-local projection nodes can still carry client-expression shapes; Phase 5 owns the supported interpreter or generated-projector boundary
- relation-property projection and nested database projection remain unsupported and already fail during parsing
- Remotion remains available as an oracle in tests, but the parser namespace itself is Remotion-free
