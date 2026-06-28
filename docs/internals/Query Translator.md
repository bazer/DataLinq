# Query Translation and Execution

DataLinq's LINQ translator supports a useful, test-backed subset of LINQ. It is not a general LINQ provider, and the docs should not imply that every expression tree can become SQL.

The lower-level SQL builder also has classes such as `Join`, `Insert`, `Update`, and `Delete`. Those are not automatic proof that equivalent high-level LINQ operators are supported. Public LINQ support is defined by the translator and the compliance tests.

For the user-facing contract, start with [Supported LINQ Queries](../Supported%20LINQ%20Queries.md). This page explains the internal shape. For the detailed parser design, see [LINQ Parser Architecture](LINQ%20Parser%20Architecture.md).

## Parser Boundary

DataLinq now owns the production parser boundary for the documented LINQ subset. `Queryable<T>` uses `ExpressionQueryPlanProvider`, which parses supported `System.Linq.Expressions` trees with `ExpressionQueryPlanParser` and produces a `DataLinqQueryPlan`.

The plan model is the semantic boundary. It records source slots, ordered operations, predicates, projection shape, result kind, and captured-value bindings. SQL generation and execution consume that DataLinq-owned plan instead of parser-specific clause or query-model types.

`Remotion.Linq` is historical migration context for the 0.8 parser replacement. It is not part of the active production query provider or public runtime package dependency graph.

The translator should stay conservative: translate known shapes, reject unknown shapes clearly, and keep the support matrix honest. Unsupported shapes should fail with DataLinq terms such as operator, selector, relation predicate, join source, or projection shape.

## Main Execution Paths

### Entity and Scalar Queries

The ordinary collection path:

1. partially evaluates local, query-independent expression subtrees
2. parses the expression into `DataLinqQueryPlan`
3. renders accepted predicates, ordering, joins, paging, single-source pushdown, and scalar result shapes through `QueryPlanSqlBuilder`
4. executes SQL through the provider
5. materializes rows through cache-aware table access
6. applies supported scalar, anonymous, or computed row-local projections after materialization

Entity reads remain cache-aware. DataLinq usually selects primary keys first, checks row cache state, and fetches missing rows rather than blindly rebuilding every row instance.

### Scalar Results

`Count`, `Any`, `Sum`, `Min`, `Max`, and `Average` are handled as scalar SQL where supported.

The aggregate boundary is deliberately narrow:

- direct numeric member selectors
- nullable numeric members
- nullable `.Value` member selectors
- filtered sequences

Computed aggregate selectors and relation-property aggregates are not part of the supported surface. Grouped aggregate projection has a separate narrow path for direct mapped keys plus `group.Key` and `group.Count()`.

### Grouped Aggregate Projection

The current grouped-query support is deliberately narrow:

- single-source query roots
- optional `Where(...)` before `GroupBy(...)`
- one direct mapped member key
- immediate `Select(...)`
- projection members limited to `group.Key` and `group.Count()`

The parser records grouping as first-class plan state: a `GroupBy` operation, `GroupKey` projection values, and grouped aggregate values. `QueryPlanSqlBuilder` renders explicit `GROUP BY` and aggregate select-list aliases. `ExpressionQueryPlanExecutor` reads those aliases directly from `IDataLinqDataReader` and invokes the projection constructor; it does not route grouped rows through `RowData` or table caches.

Materialized `IGrouping<TKey,TElement>` sequences, grouped element enumeration, computed/composite keys, grouped numeric aggregates, grouped joins, `HAVING`, and post-group composition remain outside the supported surface.

### Explicit Joins

Current explicit LINQ join support is a first baseline, not a full join engine.

Supported:

- one explicit inner `Join(...)`
- two direct DataLinq query sources
- direct member equality keys
- nullable `.Value` key normalization
- row-local projection from the joined rows
- `Where`, ordering, paging, `Any`, and `Count` over projected joined members that bind back to source-slot values

Not supported yet:

- multiple explicit joins
- `GroupJoin(...)`
- outer joins
- composite anonymous-object keys
- query-syntax transparent identifiers that project whole source entities
- post-paging joined composition such as `Join(...).Take(...).Where(...)`
- scalar aggregates over joined rows other than `Any` and `Count`
- relation-property projection inside the joined selector

Joined execution selects primary keys for each joined source, buffers those keys, materializes rows through the relevant table caches, and evaluates the result selector client-side over those materialized rows. The key buffering is deliberate: transaction connections cannot safely execute cache-hydration queries while the joined key reader is still open.

## Predicate Translation

The predicate translator handles the common shapes that are covered by tests:

- scalar equality and inequality
- range comparisons
- property-to-property comparisons in narrow translated forms
- boolean grouping with `&&`, `||`, and `!`
- nullable boolean and nullable value predicates
- string methods such as prefix/suffix/content search, casing, trimming, length, substring, and null/whitespace checks
- date/time member access for documented members
- local `Contains(...)` over arrays, lists, sets, spans, and safe local projections
- equality-shaped local `Any(predicate)` membership
- fixed true/false conditions for empty local collections
- one-to-many relation `Any(...)` and existence-equivalent `Count()` predicates translated as correlated `EXISTS`

Unsupported predicate methods and unsupported expression shapes should throw `QueryTranslationException`. Silent client-side predicate fallback would be a correctness bug.

## Projection Model

Projection is intentionally split:

- SQL is used for filtering, ordering, paging, scalar result operators, grouped aggregate projection, and join key selection.
- Row-local projection can run after materialization for supported scalar, anonymous, and computed shapes.

Relation-property projection is rejected. That prevents hidden N+1 behavior from being smuggled into what looks like a single provider query.

## Important Internals

`Queryable<T>` and `ExpressionQueryPlanProvider`
: Own the production `IQueryable<T>` boundary and route expression-tree parsing/execution through DataLinq code.

`ExpressionQueryPlanParser`
: Converts supported expression trees into `DataLinqQueryPlan` nodes, source slots, bindings, predicates, projections, and result kinds.

`ExpressionLocalValueEvaluator`
: Evaluates supported local values such as captured constants, simple member reads, empty collection factories, array/list indexes, and deterministic string operations without compiling or invoking arbitrary user methods.

`QueryPlanSqlBuilder`
: Renders plan operations to SQL, including local collection membership, relation-backed `EXISTS` predicates, ordering, paging, single-source subquery pushdown, scalar aggregates, grouped aggregate projection, and the narrow explicit join baseline.

`ExpressionQueryPlanExecutor`
: Executes sequence, scalar, single-row, projection, grouped aggregate, and explicit-join result paths from a parsed plan.

`ProjectionExpressionEvaluator`
: Evaluates supported row-local projections over materialized rows using parameter bindings, without Remotion query-source identities.

`Where` and `WhereGroup`
: Represent SQL predicates and grouped boolean logic.

`SqlQuery`, `Select`, and `Sql`
: Build SQL text, parameters, selected columns, grouping, ordering, paging, joins, and scalar command execution.

## Maintenance Rule

If a query shape is not covered in the compliance tests, do not describe it as supported. Add the regression test first, then update [Supported LINQ Queries](../Supported%20LINQ%20Queries.md) and the [LINQ Translation Support Matrix](../support-matrices/LINQ%20Translation%20Support%20Matrix.md).
