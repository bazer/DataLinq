> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 13: Query Composition and Subquery Pushdown

**Status:** Implemented for the single-source Phase 13 slice.

## Purpose

Phase 13 makes DataLinq preserve LINQ operator order for ordering, filtering, paging, and scalar result operators before joins widen the same machinery.

The implemented Phase 13 boundary supports single-source post-paging filters, orderings, and scalar reductions by inserting a derived-source boundary when flattening final SQL clause order would be wrong. For example:

```csharp
var q = db.Query();

var rows = q.Employees
    .OrderBy(employee => employee.LastName)
    .Take(10)
    .OrderBy(employee => employee.HireDate)
    .ToList();
```

The last `OrderBy(...)` is not the same as replacing the first `ORDER BY`. It orders the already-limited ten-row source. DataLinq renders that shape with a subquery boundary to preserve the C# meaning.

This phase should also make query-root ownership explicit. Every supported query command must work from the generated read-only query root and from a transaction-local query root:

```csharp
var q = db.Query();
var tx = db.Transaction();
var tq = tx.Query();
```

The parser should not care which root produced the `IQueryable<T>`. Execution absolutely should: a query rooted in `transaction.Query()` must use the transaction data source for SQL execution, materialization, relation lookup, and cache interaction.

## Execution Boundary

In scope:

- preserve LINQ operator order for `Where(...)`, `OrderBy(...)`, `ThenBy(...)`, `Skip(...)`, `Take(...)`, and supported scalar result operators
- add SQL subquery pushdown when later operators must apply over an already-filtered, ordered, skipped, or limited source
- support shapes such as `Take(...).OrderBy(...)`, `Skip(...).OrderBy(...)`, `OrderBy(...).Take(...).OrderBy(...)`, and post-paging `Where(...)`
- keep row-limiting without deterministic ordering legal but documented as nondeterministic
- prove every supported command works from both `db.Query()` and `transaction.Query()`
- keep parameter binding, aliases, and projection binding stable across pushed-down query sources
- prepare the same nested-source representation for Phase 14 joined row shapes

Out of scope:

- multi-source joins
- relation-aware joins
- implicit relation traversal
- `GroupBy(...)`
- arbitrary nested database subqueries in user projections
- client-side fallback when SQL translation cannot preserve operator order

## Related Work From Older Plans

This phase carries forward the pieces of old Phase 17 that were intentionally deferred during parser replacement:

- `DataLinqQueryPlan` should represent operator order, not final SQL clause order.
- `OrderBy(...).Take(...)` can usually render as one flat query.
- `Take(...).OrderBy(...)`, `Skip(...).OrderBy(...)`, and `OrderBy(...).Take(...).OrderBy(...)` require subquery pushdown.
- Plan and SQL tests should prove the expected nesting shape instead of only checking returned rows.
- Unsupported shapes should fail with DataLinq-owned diagnostics instead of silently flattening into wrong SQL.

This phase also completes the known Phase 1 query-contract gap around post-paging filters/orderings.

## Recommended Order

1. Add failing behavior and SQL-shape tests for post-paging filters/orderings.
2. Add transaction-root parity tests for currently supported single-source commands.
3. Extend query-plan operations so nested source boundaries are explicit.
4. Teach SQL generation to render pushed-down single-source subqueries with stable aliases.
5. Preserve parameter binding and projection binding across nested source scopes.
6. Add scalar result operator coverage over pushed-down sources.
7. Update user docs and the support matrix only for shipped shapes.

The live implementation checklist is tracked in [Implementation Plan](Implementation%20Plan.md).

## Source Plans

- [Phase 17 Query Plan and Remotion Isolation](../../phase-17-query-plan-and-remotion-isolation/Implementation%20Plan.md)
- [0.8 Phase 1 Query Contract and Plan Baseline](../phase-1-query-contract-and-plan-baseline/Implementation%20Plan.md)
- [Supported LINQ Queries](../../../../Supported%20LINQ%20Queries.md)
- [LINQ Translation Support Matrix](../../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)

## Exit Criteria

Phase 13 is done when:

- post-paging filters and orderings translate with single-source subquery pushdown for mapped-row queries
- supported operator-order-sensitive shapes preserve the C# sequence semantics in SQL and row results
- SQL-shape tests prove where subquery pushdown is required
- supported commands execute equivalently from `db.Query()` and `transaction.Query()`, while transaction-rooted queries use the transaction data source
- public docs and the support matrix describe only shipped behavior
