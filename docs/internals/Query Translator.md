# Query Translation and Execution

DataLinq's LINQ translator supports a useful, test-backed subset of LINQ. It is not a general LINQ provider, and the docs should not imply that every expression tree can become SQL.

The lower-level SQL builder also has classes such as `Join`, `Insert`, `Update`, and `Delete`. Those are not automatic proof that equivalent high-level LINQ operators are supported. Public LINQ support is defined by the translator and the compliance tests.

For the user-facing contract, start with [Supported LINQ Queries](../Supported%20LINQ%20Queries.md). This page explains the internal shape. For the detailed parser design, see [LINQ Parser Architecture](LINQ%20Parser%20Architecture.md).

## Parser Boundary

DataLinq now owns the production parser boundary for the documented LINQ subset. `Queryable<T>` uses `ExpressionQueryPlanProvider`, which parses supported `System.Linq.Expressions` trees with `ExpressionQueryPlanParser` and produces a `DataLinqQueryPlan`.

The plan model is the semantic boundary. It records source slots, ordered operations, predicates, projection shape, result kind, and captured-value bindings. Parser-time binding capture uses a mutable `QueryPlanBindingFrame`, but `DataLinqQueryPlan` owns an immutable `QueryPlanBindings` snapshot so SQL rendering can resolve captured values without mutable plan state or render-time binding scans. SQL generation and execution consume that DataLinq-owned plan instead of parser-specific clause or query-model types.

`Remotion.Linq` is historical migration context for the 0.8 parser replacement. It is not part of the active production query provider or public runtime package dependency graph.

The translator should stay conservative: translate known shapes, reject unknown shapes clearly, and keep the support matrix honest. Unsupported shapes should fail with DataLinq terms such as operator, selector, relation predicate, join source, or projection shape.

## Main Execution Paths

### Entity and Scalar Queries

The ordinary collection path:

1. partially evaluates local, query-independent expression subtrees
2. parses the expression into `DataLinqQueryPlan`
3. renders accepted predicates, ordering, joins, paging, single-source pushdown, and scalar result shapes through `QueryPlanSqlBuilder`
4. executes SQL through the provider
5. materializes entity rows through cache-aware table access, or reads SQL-backed projection aliases directly when the projection is a source-slot row
6. applies supported computed row-local projections after materialization

Entity reads remain cache-aware. DataLinq usually selects primary keys first, checks row cache state, and fetches missing rows rather than blindly rebuilding every row instance.

### Scalar Results

`Count`, `Any`, `Sum`, `Min`, `Max`, and `Average` are handled as scalar SQL where supported.

The aggregate boundary is deliberately narrow:

- direct numeric member selectors
- nullable numeric members
- nullable `.Value` member selectors
- filtered sequences

Computed aggregate selectors and relation-property aggregates are not part of the supported surface. Grouped aggregate projection has a separate narrow path for direct mapped keys plus `group.Key`, `group.Count()`, and direct numeric grouped aggregate selectors.

### Grouped Aggregate Projection

The current grouped-query support is deliberately narrow:

- single-source query roots
- optional `Where(...)` before `GroupBy(...)`
- direct, composite, and SQL-renderable computed key members
- grouping over supported explicit joined projections and implicit singular relation traversal
- immediate `Select(...)`
- projection members limited to `group.Key` for scalar keys, `group.Key.Member` for composite keys, `group.Count()`, and direct numeric grouped `Sum`, `Min`, `Max`, and `Average` selectors
- grouped `Where(...)` predicates that compare `group.Key` or supported grouped aggregates and render as `HAVING`
- post-projection grouped-row filtering, ordering, paging, `Count()`, and `Any()` when later operators bind to projected key or aggregate members
- constructor-backed grouped projection rows when member or constructor parameter names are stable

The parser records grouping as first-class plan state: a `GroupBy` operation, named grouped key members, an element binding context, `Having` operations, `GroupKey` projection values, and grouped aggregate values. The element binding context is either a root source slot or a joined projection whose members map back to source-slot values. `QueryPlanSqlBuilder` renders explicit `GROUP BY`, `HAVING`, aggregate select-list aliases, raw grouped ordering expressions, and derived grouped scalar reductions for grouped-row `Count()` and `Any()`. `ExpressionQueryPlanExecutor` reads grouped projection aliases directly from `IDataLinqDataReader` and invokes the projection constructor; it does not route grouped rows through `RowData` or table caches.

Materialized `IGrouping<TKey,TElement>` sequences, grouped element enumeration, whole composite `group.Key` object projection, client-computed keys, computed aggregate selectors, collection relation grouping, non-bindable grouped-row composition, and terminal operators other than grouped-row `Count()`/`Any()` remain outside the supported surface.

### Explicit Joins

Current explicit LINQ join support is a first baseline, not a full join engine. The supported surface includes fluent `Join(...)` and the single-inner-join C# query syntax shape that lowers to `Queryable.Join(...)`.

Supported:

- one explicit inner `Join(...)`
- two direct DataLinq query sources
- direct member equality keys
- nullable `.Value` key normalization
- SQL-backed direct source-slot projection rows from the joined rows
- `Where`, ordering, paging, `Any`, and `Count` over projected joined members that bind back to source-slot values
- post-paging `Where`, ordering, `Any`, and `Count` over SQL-backed joined projection rows through a derived-source boundary
- transparent identifier binding for single query-syntax inner joins

Not supported yet:

- multiple explicit joins
- `GroupJoin(...)`
- outer joins
- composite anonymous-object keys
- query-syntax transparent identifiers that project whole source entities or cannot bind back to source slots
- scalar aggregates over joined rows other than `Any` and `Count`
- relation object or collection relation projection inside the joined selector

The parser represents compiler-generated transparent identifiers as temporary source-member bindings, so later query-syntax `where`, `orderby`, and `select` clauses bind back to the same source slots as fluent joins. Joined execution reads SQL projection aliases directly when every result-selector member binds to a source-slot value. Post-paging joined composition wraps the paged join in a derived source, preserves projection aliases and joined primary-key aliases, and binds later predicates/orderings to the derived aliases. Row-local computed joined projections remain a fallback for materialized execution, and cannot be used for provider-side composition after paging. The old key-buffered joined materialization path remains relevant only for row-local joined projections that cannot become SQL rows.

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
- singular relation member traversal in root predicates, ordering, and direct projection translated as implicit inner joins

Unsupported predicate methods and unsupported expression shapes should throw `QueryTranslationException`. Silent client-side predicate fallback would be a correctness bug.

## Implicit Singular Relation Joins

The current implicit relation support is intentionally narrower than the Phase 15 roadmap target.

Supported:

- generated singular relation traversal from a root row
- related member access in `Where`, `OrderBy`, `ThenBy`, and direct `Select(...)` projection
- inner-join semantics
- source-slot reuse when the same relation appears more than once in a query

Not supported yet:

- relation object projection or collection relation projection in provider `Select(...)`
- collection traversal beyond documented `Any` and existence-equivalent `Count` predicates
- multi-hop implicit traversal
- nullable/left-join semantics
- fluent relation-aware join APIs such as `JoinBy`, `JoinMany`, `LeftJoinBy`, and `LeftJoinMany`
- standard `Queryable.LeftJoin`

The parser resolves `root.Relation.Member` through relation metadata, registers an `ImplicitJoin` source slot, adds an inner `Join` operation, and binds the related member to that source slot's column. SQL rendering treats implicit join slots like explicit inner join slots. Entity execution returns root rows; SQL-backed projection execution reads the related column alias from the result row.

## Projection Model

Projection is intentionally split:

- SQL is used for filtering, ordering, paging, scalar result operators, grouped aggregate projection/composition, and join key selection.
- `QueryPlanProjection.ScalarMember` and `QueryPlanProjection.SqlRow` read aliased SQL values directly from `IDataLinqDataReader`.
- `QueryPlanProjection.SqlRow` stores named projection members as `QueryPlanValue` bindings and is used only when every member binds to a source-slot value.
- Row-local projection remains for supported computed .NET expressions after materialization.

Supported singular relation member projection uses the same implicit join source-slot machinery as predicates and ordering. Relation object projection, collection relation projection, nested provider queries, multi-hop relation traversal, and client fallback remain rejected so hidden N+1 behavior cannot be smuggled into what looks like a single provider query.

## Important Internals

`Queryable<T>` and `ExpressionQueryPlanProvider`
: Own the production `IQueryable<T>` boundary and route expression-tree parsing/execution through DataLinq code.

`ExpressionQueryPlanParser`
: Converts supported expression trees into `DataLinqQueryPlan` nodes, source slots, bindings, predicates, projections, and result kinds.

`ExpressionLocalValueEvaluator`
: Evaluates supported local values such as captured constants, simple member reads, empty collection factories, array/list indexes, and deterministic string operations without compiling or invoking arbitrary user methods.

`QueryPlanSqlBuilder`
: Renders plan operations to SQL, including local collection membership, relation-backed `EXISTS` predicates, implicit singular relation joins, ordering, paging, single-source subquery pushdown, scalar aggregates, SQL-backed projection rows, grouped aggregate projection, supported explicit/query-syntax joins, and joined SQL-row pushdown.

`ExpressionQueryPlanExecutor`
: Executes sequence, scalar, single-row, SQL-backed projection, row-local projection, grouped aggregate, and explicit-join result paths from a parsed plan.

`ProjectionExpressionEvaluator`
: Evaluates supported row-local projections over materialized rows using parameter bindings, without Remotion query-source identities.

`Where` and `WhereGroup`
: Represent SQL predicates and grouped boolean logic.

`SqlQuery`, `Select`, and `Sql`
: Build SQL text, parameters, selected columns, grouping, ordering, paging, joins, and scalar command execution.

## Maintenance Rule

If a query shape is not covered in the compliance tests, do not describe it as supported. Add the regression test first, then update [Supported LINQ Queries](../Supported%20LINQ%20Queries.md) and the [LINQ Translation Support Matrix](../support-matrices/LINQ%20Translation%20Support%20Matrix.md).
