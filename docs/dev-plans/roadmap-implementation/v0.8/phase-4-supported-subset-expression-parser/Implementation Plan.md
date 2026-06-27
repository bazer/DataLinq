> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 4 Implementation Plan: Supported-Subset Expression Parser

**Status:** Complete.

**Completed:** 2026-06-27.

## Purpose

Phase 4 introduces a DataLinq-owned parser over `System.Linq.Expressions`.

The parser emits the same `DataLinqQueryPlan` consumed by Phase 3 SQL generation. Remotion can remain the default public `IQueryable` provider until the later dual-run and switch phases, but it should no longer be the only component capable of turning supported LINQ expression trees into DataLinq plan nodes.

## Inputs

Phase 4 consumes:

- the Phase 1 query support contract
- the Phase 2 `DataLinqQueryPlan` node model
- the Phase 3 plan SQL renderer and parity tests
- the current Remotion adapter as a temporary oracle, not as parser infrastructure

The DataLinq parser must not call Remotion's `QueryParser`, consume `QueryModel`, or import Remotion clause/result-operator types.

## Scope

The first supported parser surface is intentionally narrow:

- direct DataLinq root query sources
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
- `Sum`, `Min`, `Max`, `Average` with supported selectors
- supported comparisons, boolean grouping, nullable predicates, string functions, date/time members, and fixed conditions
- local `Contains(...)`, empty local collections, and supported local `Any(...)` membership
- collection relation `Any(...)` and existence-equivalent relation `Count()` predicates
- the current narrow explicit inner `Join(...)` baseline

Out of scope:

- changing the default public query provider
- removing Remotion package references
- broad nested database subqueries
- `GroupBy(...)`
- `GroupJoin(...)`
- composite-key joins
- outer joins
- client-side predicate fallback
- claiming projection execution is AOT-clean

## Workstreams

### A. Parser Foundation

Goal: add an internal parser that consumes expression trees directly and emits `DataLinqQueryPlan`.

Tasks:

1. Add an internal parser namespace separate from Remotion adapter code.
2. Parse root `IQueryable` constants through the actual queryable element type.
3. Register root, relation, and explicit join source slots using the existing plan node model.
4. Keep captured scalar and local sequence values in `QueryPlanBindingFrame`.
5. Avoid Remotion imports in the parser namespace.

### B. Operator Chain Parsing

Goal: preserve LINQ operator order for the supported subset.

Tasks:

1. Parse `Where`, ordering, `Skip`, `Take`, and `Select` in source-to-result order.
2. Merge consecutive ordering operators into one ordered plan operation.
3. Keep post-paging `Where` and ordering rejection until subquery pushdown is deliberately implemented.
4. Parse scalar and single-result terminal operators into `QueryPlanResult`.
5. Parse supported aggregate selector shapes into both projection and aggregate selector plan values.

### C. Predicate and Value Parsing

Goal: match the Phase 1/2 support contract without falling back to client predicates.

Tasks:

1. Convert comparisons, boolean grouping, negation, nullable comparisons, fixed conditions, and bool columns.
2. Convert supported string and date/time member/function shapes into `QueryPlanFunctionValue`.
3. Convert captured local scalars without leaking values into plan snapshots.
4. Convert local sequence membership and empty sequences into `In` or fixed predicates.
5. Convert supported local `Any(...)` membership.
6. Convert relation `Any(...)` and existence-equivalent relation `Count()` predicates.

### D. Projection and Join Baseline

Goal: keep projection and explicit join plan shapes compatible with Phase 3 SQL generation.

Tasks:

1. Preserve entity, scalar member, anonymous, computed row-local, and joined row-local projection shapes.
2. Reject relation projection and nested database projection shapes with focused DataLinq diagnostics.
3. Preserve the one direct inner `Join(...)` baseline.
4. Reject join expansion scenarios owned by Phase 8.

### E. Parity Evidence

Goal: prove the new parser emits the same plan for representative supported shapes before any runtime switch.

Tasks:

1. Add parser-vs-Remotion adapter snapshot parity tests for core single-source chains.
2. Add scalar/result/aggregate parity snapshots.
3. Add relation, local membership, and explicit join parity snapshots.
4. Add diagnostics tests for unsupported post-paging operators.
5. Add an architectural guard that parser types do not expose Remotion types.

## Recommended Implementation Order

1. Add this implementation plan and mark Phase 4 in progress.
2. Add the expression parser foundation and representative parity tests.
3. Expand parser support to all documented Phase 1 support areas.
4. Run focused parser parity tests and existing plan/SQL compliance tests.
5. Add closeout notes identifying gaps that Phase 5/6 must own.

## Verification

Focused verification:

- `QueryPlanNodeTests`
- `ExpressionQueryPlanParserTests`
- `QueryPlanSnapshotTests`
- `QueryPlanSqlParityTests`
- existing translation suites covering boolean, nullable, string/date functions, local membership, relations, aggregates, projections, joins, and unsupported diagnostics

Broad verification:

- unit quick suite
- compliance quick suite

Provider-backed execution remains important even though the new parser is not yet the default, because parity snapshots must continue to target the plan consumed by provider SQL rendering.

## Exit Criteria

Phase 4 is complete when:

- DataLinq has an internal expression parser that emits `DataLinqQueryPlan` without Remotion parser or clause inputs
- representative documented support shapes match the Remotion adapter plan oracle
- unsupported shapes fail with focused DataLinq diagnostics
- the parser namespace has no Remotion type exposure
- no public docs claim the new parser is the shipped default before Phase 6 switches execution

## Phase 5/6 Handoff

Phase 5 owns projection/local-evaluation AOT cleanup. Phase 6 owns dual-run parity and the runtime switch.

The handoff should list:

- parser-supported shapes with parity evidence
- any parser-supported shapes that still rely on temporary local/projection reflection helpers
- remaining Remotion-only active test scaffolding
- any deliberate unsupported-shape contractions requiring release notes before Phase 7

Current handoff:

- `ExpressionQueryPlanParserTests` cover representative supported parser shapes against the Remotion adapter oracle.
- `ExpressionPlanQueryable<T>` provides a parser-owned queryable construction path but deliberately does not execute queries yet.
- relation-property projection, nested database projection, `GroupBy(...)`, `GroupJoin(...)`, and post-join/post-paging unsupported shapes remain rejected with DataLinq diagnostics.
- parser-local captured value evaluation and row-local projection compatibility are the concrete Phase 5 cleanup targets.
- no deliberate supported-shape contraction has been recorded for Phase 4.
