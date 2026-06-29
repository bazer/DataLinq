> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 13B: Grouped Aggregate Projection Baseline

**Status:** Implemented for the direct mapped key plus `group.Key`/`group.Count()` slice.

## Purpose

Phase 13B adds the first honest `GroupBy(...)` support slice.

The important word is "honest". LINQ `GroupBy(...).ToList()` produces `IGrouping<TKey,TElement>` values that can enumerate grouped elements. SQL `GROUP BY` produces aggregate rows. Treating those as the same feature would either materialize entire tables on the client or return a shape that is not LINQ's shape. Both are bad.

This phase should support grouped aggregate projection only:

```csharp
var counts = db.Query().DepartmentEmployees
    .Where(row => row.dept_no.StartsWith("d00"))
    .GroupBy(row => row.dept_no)
    .Select(group => new
    {
        DeptNo = group.Key,
        Count = group.Count()
    })
    .ToList();
```

That is useful, SQL-shaped, and testable without pretending DataLinq has become a full LINQ provider.

## Placement

This belongs after Phase 13 because grouping needs the same discipline around operator order, source aliases, parameters, and result-shape boundaries. It can land before Phase 14 because the first slice is deliberately single-source and should not depend on joined row composition.

If the implementation starts needing grouped joins, grouped subqueries, or post-group composition, stop and move that work behind Phase 14 or a later grouped-query phase. That would no longer be the narrow baseline.

## Execution Boundary

In scope:

- single-source provider queries from `db.Query()` and `transaction.Query()`
- `Where(...)` before `GroupBy(...)`
- direct mapped member group keys
- `g.Key` projection
- `g.Count()` projection
- direct numeric grouped `Sum`, `Min`, `Max`, and `Average` only in a later deliberately tested slice
- SQLite, MySQL, and MariaDB behavior coverage for every supported aggregate shape
- DataLinq-owned diagnostics for unsupported grouped shapes

Out of scope:

- bare `GroupBy(...).ToList()`
- materialized `IGrouping<TKey,TElement>` support
- grouped element enumeration
- `HAVING`
- composite anonymous-object keys
- computed group keys
- relation-property group keys
- grouped joins or grouping over joined row shapes
- post-group filtering, ordering, paging, or scalar result operators unless a later design adds the required SQL/subquery shape
- arbitrary aggregate selectors
- silent client-side fallback

## Plan Model Requirements

The implementation should add first-class plan concepts instead of encoding grouping as SQL strings:

- a grouping operation or equivalent query-plan node that records one or more group keys
- a grouped aggregate projection value for `Count`, then optionally direct numeric `Sum`, `Min`, `Max`, and `Average`
- plan snapshot/debug output that makes grouped keys and aggregate members visible
- validation that grouped projections reference only `g.Key` and supported aggregate calls

The plan should continue to describe semantics, not SQL text. SQL rendering is one consumer of the plan.

## SQL And Execution Requirements

SQL rendering needs real `GROUP BY` support in the lower query builder or a grouped-select renderer. Do not smuggle grouping into `What(...)` selectors and hope later code guesses correctly.

Grouped aggregate execution should read result rows directly through `IDataLinqDataReader`. It should not use `RowData` or table-cache materialization, because grouped rows are not entity rows and do not have provider keys.

## Recommended Order

1. Add failing parser and execution tests for `GroupBy(key).Select(g => new { g.Key, Count = g.Count() })`.
2. Add unsupported tests for bare `GroupBy`, grouped element enumeration, computed keys, composite keys, and unsupported aggregates.
3. Add query-plan nodes and debug snapshots for group keys and aggregate projection members.
4. Teach the parser to accept the narrow grouped projection shape.
5. Add explicit `GROUP BY` rendering and grouped result-row reading.
6. Run provider-matrix compliance coverage for SQLite, MySQL, and MariaDB.
7. Update `Supported LINQ Queries`, the LINQ support matrix, and internals docs only for the shapes proven by tests.

The live implementation checklist is tracked in [Implementation Plan](Implementation%20Plan.md).

## Exit Criteria

Phase 13B is done when:

- the supported grouped aggregate shape is implemented from both `db.Query()` and `transaction.Query()`
- `GroupBy(key).Select(g => new { g.Key, Count = g.Count() })` passes across SQLite, MySQL, and MariaDB
- any additional grouped numeric aggregates have matching provider-matrix tests, or remain explicitly unsupported
- bare `GroupBy` and broad grouped LINQ shapes still fail with focused `QueryTranslationException` diagnostics
- query-plan snapshots show grouping semantics without SQL text leakage
- generated SQL contains explicit `GROUP BY`
- public docs and the support matrix describe grouped aggregate projection as a narrow supported slice, not general `GroupBy`
