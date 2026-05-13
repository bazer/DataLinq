# Query Translation and Execution

DataLinq's LINQ translator supports a useful, test-backed subset of LINQ. It is not a general LINQ provider, and the docs should not imply that every expression tree can become SQL.

The lower-level SQL builder also has classes such as `Join`, `Insert`, `Update`, and `Delete`. Those are not automatic proof that equivalent high-level LINQ operators are supported. Public LINQ support is defined by the translator and the compliance tests.

For the user-facing contract, start with [Supported LINQ Queries](../Supported%20LINQ%20Queries.md). This page explains the internal shape.

## Parser Boundary

DataLinq currently uses `Remotion.Linq` to parse LINQ expression trees into query models. That dependency is still part of the reason the broad AOT/trimming claim remains narrow.

The long-term direction is to isolate or replace that parser behind a DataLinq-owned query plan, but that is future work. The current translator should therefore stay conservative: translate known shapes, reject unknown shapes clearly, and keep the support matrix honest.

## Main Execution Paths

### Entity and Scalar Queries

The ordinary collection path:

1. partially evaluates local, query-independent expression subtrees
2. parses the expression through `Remotion.Linq`
3. composes accepted `Where` and `OrderBy` clauses
4. applies supported result operators such as paging and single-row limits
5. executes SQL
6. materializes rows through cache-aware table access
7. applies supported scalar, anonymous, or computed row-local projections after materialization

Entity reads remain cache-aware. DataLinq usually selects primary keys first, checks row cache state, and fetches missing rows rather than blindly rebuilding every row instance.

### Scalar Results

`Count`, `Any`, `Sum`, `Min`, `Max`, and `Average` are handled as scalar SQL where supported.

The aggregate boundary is deliberately narrow:

- direct numeric member selectors
- nullable numeric members
- nullable `.Value` member selectors
- filtered sequences

Computed aggregate selectors, grouped aggregates, and relation-property aggregates are not part of the supported surface.

### Explicit Joins

Current explicit LINQ join support is a first baseline, not a full join engine.

Supported:

- one explicit inner `Join(...)`
- two direct DataLinq query sources
- direct member equality keys
- nullable `.Value` key normalization
- row-local projection from the joined rows

Not supported yet:

- multiple explicit joins
- `GroupJoin(...)`
- outer joins
- composite anonymous-object keys
- filtering, ordering, paging, or result operators over joined row shapes
- relation-property projection inside the joined selector

Joined execution selects primary keys for each joined source, materializes rows through the relevant table caches, and evaluates the result selector client-side over those materialized rows.

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

- SQL is used for filtering, ordering, paging, scalar result operators, and join key selection.
- Row-local projection can run after materialization for supported scalar, anonymous, and computed shapes.

Relation-property projection is rejected. That prevents hidden N+1 behavior from being smuggled into what looks like a single provider query.

## Important Internals

`Evaluator`
: Partially evaluates local subtrees that do not depend on query parameters.

`QueryExecutor`
: Owns query-model execution, result operators, explicit join handling, scalar result execution, and projection evaluation.

`QueryBuilder`
: Builds translated predicate SQL, including local collection membership and relation-backed `EXISTS` predicates.

`Where` and `WhereGroup`
: Represent SQL predicates and grouped boolean logic.

`SqlQuery`, `Select`, and `Sql`
: Build SQL text, parameters, selected columns, ordering, paging, joins, and scalar command execution.

## Maintenance Rule

If a query shape is not covered in the compliance tests, do not describe it as supported. Add the regression test first, then update [Supported LINQ Queries](../Supported%20LINQ%20Queries.md) and the [LINQ Translation Support Matrix](../support-matrices/LINQ%20Translation%20Support%20Matrix.md).
