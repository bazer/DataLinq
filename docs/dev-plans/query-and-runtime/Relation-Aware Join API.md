> [!WARNING]
> This document is roadmap or specification material. It describes proposed behavior, not current DataLinq behavior.

# Relation-Aware Join API

**Status:** Draft design.

## Purpose

DataLinq has a narrow explicit LINQ `Join(...)` baseline, but the user-facing syntax is too mechanical for a model-driven ORM:

```csharp
db.Query().Orders.Join(
    db.Query().Customers,
    order => order.CustomerId,
    customer => customer.Id,
    (order, customer) => new { order, customer });
```

That is ordinary LINQ, but it makes the user spell information DataLinq already knows from relation metadata. The goal of this plan is to define a more humane join surface while keeping the translation semantics explicit, testable, and provider-backed.

The preferred direction has four parts:

1. Make standard C# query syntax work well for explicit complex joins.
2. Add `JoinBy(...)` and `JoinMany(...)` for relation-aware fluent inner joins.
3. Add `LeftJoinBy(...)` and `LeftJoinMany(...)` with nullable joined values.
4. Add join-local `on:` predicates as additive join predicates, with relation metadata still supplying the key equality.

## Current Behavior Boundary

The shipped support boundary is intentionally small. The current implementation supports one explicit inner `Join(...)` between two direct DataLinq query sources, with direct member keys or nullable `.Value` keys, and a row-local projection from both sides.

Important current limits:

- only one explicit join
- no `GroupJoin(...)`
- no left/outer join pattern
- no composite anonymous-object keys
- no filtering, ordering, paging, or result operators over the joined result
- no relation-property joins or relation-property projections inside the result selector

The current support is documented in [`Supported LINQ Queries.md`](../../Supported%20LINQ%20Queries.md), and the executor enforces the single-join boundary in `src/DataLinq/Linq/QueryExecutor.cs`.

## Design Principles

The API should remove duplicated key selectors. It should not hide query execution.

Good:

```csharp
orders.JoinBy(order => order.Customer, (order, customer) => ...);
```

Less good:

```csharp
orders.Include(order => order.Customer).Include(order => order.Items);
```

The second shape sounds like eager loading. That is a different feature. Joins produce a new result shape and can change row cardinality. The API should say "join" when it joins.

The query engine should stay honest:

- relation metadata supplies key equality
- `on:` adds extra join predicates
- `.Where(...)` filters the joined row shape
- left joins preserve unmatched left rows
- unsupported shapes fail with `QueryTranslationException`
- no silent client-side predicate fallback

## Proposed Explicit Query Syntax

Standard C# query syntax should be a first-class documented path for complex explicit joins. It is still LINQ, but it reads closer to SQL and avoids the four-argument `Join(...)` call shape.

Single join:

```csharp
var rows =
    from order in db.Query().Orders
    join customer in db.Query().Customers
        on order.CustomerId equals customer.Id
    select new
    {
        order,
        customer
    };
```

Multiple joins with filtering:

```csharp
var rows =
    from order in db.Query().Orders
    join customer in db.Query().Customers
        on order.CustomerId equals customer.Id
    join item in db.Query().OrderItems
        on order.Id equals item.OrderId
    join product in db.Query().Products
        on item.ProductId equals product.Id
    where order.CreatedAt >= from
       && customer.IsActive
       && item.Quantity > 0
       && product.IsPublished
    select new
    {
        OrderId = order.Id,
        CustomerName = customer.Name,
        ProductName = product.Name,
        item.Quantity
    };
```

This path is valuable even after `JoinBy(...)` exists because it remains the clearest syntax when the user wants explicit key pairs or joins that are not backed by generated relation metadata.

## Proposed Relation-Aware Inner Joins

`JoinBy(...)` joins through a singular generated relation property. The relation property identifies the related table and key column pairs.

```csharp
var rows = db.Query().Orders
    .JoinBy(
        order => order.Customer,
        (order, customer) => new
        {
            order,
            customer
        });
```

For nullable foreign-key relations, inner `JoinBy(...)` excludes rows whose relation does not resolve, matching SQL inner join behavior.

`JoinMany(...)` joins through a generated collection relation property. It expands the source row, one result row per related child row.

```csharp
var rows = db.Query().Customers
    .JoinMany(
        customer => customer.Orders,
        (customer, order) => new
        {
            customer,
            order
        });
```

The fluent shape stays useful after previous joins because the relation expression can start from any model reachable inside the current row shape:

```csharp
var rows = db.Query().Orders
    .JoinBy(
        order => order.Customer,
        (order, customer) => new { order, customer })
    .JoinMany(
        x => x.order.Items,
        (x, item) => new { x.order, x.customer, item })
    .JoinBy(
        x => x.item.Product,
        (x, product) => new { x.order, x.customer, x.item, product })
    .Where(x =>
        x.order.CreatedAt >= from &&
        x.customer.IsActive &&
        x.product.IsPublished)
    .Select(x => new
    {
        OrderId = x.order.Id,
        CustomerName = x.customer.Name,
        ProductName = x.product.Name,
        x.item.Quantity
    });
```

That is not as compact as SQL aliases, but it keeps ordinary C# typing and IDE refactoring. The repeated anonymous-object reshaping is the honest price of a strongly typed fluent API.

## Join-Local `on:` Predicates

`on:` should be an optional additive predicate. It does not replace relation key equality.

```csharp
var rows = db.Query().Orders
    .JoinBy(
        order => order.Customer,
        (order, customer) => new { order, customer },
        on: (order, customer) => customer.IsActive)
    .JoinMany(
        x => x.order.Items,
        (x, item) => new { x.order, x.customer, item },
        on: (x, item) => item.Quantity > 0 && !item.IsDeleted)
    .JoinBy(
        x => x.item.Product,
        (x, product) => new { x.order, x.customer, x.item, product },
        on: (x, product) => product.IsPublished)
    .Where(x => x.order.CreatedAt >= from);
```

For inner joins, most extra `on:` predicates are equivalent to `.Where(...)` after the join:

```csharp
var rows = db.Query().Orders
    .JoinBy(
        order => order.Customer,
        (order, customer) => new { order, customer })
    .Where(x => x.customer.IsActive);
```

The separate `on:` parameter is still worth having because:

- it groups join-specific constraints with the join
- it becomes semantically important for left joins
- it avoids teaching users one style for inner joins and a different style for outer joins

## Proposed Left Joins

`LeftJoinBy(...)` should preserve every source row and make the joined singular row nullable:

```csharp
var rows = db.Query().Orders
    .LeftJoinBy(
        order => order.Customer,
        (order, customer) => new
        {
            order,
            customer
        });
```

The result selector's joined value should be nullable when nullable reference types are enabled:

```csharp
Expression<Func<Order, Customer?, TResult>>
```

`LeftJoinMany(...)` should preserve every source row and produce a null child when no related child exists:

```csharp
var rows = db.Query().Orders
    .LeftJoinMany(
        order => order.Items,
        (order, item) => new
        {
            order,
            item
        });
```

The important `ON` vs `WHERE` distinction:

```csharp
var rows = db.Query().Orders
    .LeftJoinMany(
        order => order.Items,
        (order, item) => new { order, item },
        on: (order, item) => item.Status == OrderItemStatus.Open)
    .Where(x => x.order.CreatedAt >= from);
```

This means "return orders, and attach open items when present."

This is different:

```csharp
var rows = db.Query().Orders
    .LeftJoinMany(
        order => order.Items,
        (order, item) => new { order, item })
    .Where(x => x.item != null && x.item.Status == OrderItemStatus.Open);
```

That effectively discards orders without an open item. In practice it turns the left join into an inner join for that predicate. DataLinq should make this distinction explicit in docs and tests.

## Candidate API Surface

The exact signatures need proof in code, but the public shape should be close to this:

```csharp
public static IQueryable<TResult> JoinBy<TSource, TInner, TResult>(
    this IQueryable<TSource> source,
    Expression<Func<TSource, TInner?>> relation,
    Expression<Func<TSource, TInner, TResult>> resultSelector,
    Expression<Func<TSource, TInner, bool>>? on = null)
    where TInner : IModelInstance;

public static IQueryable<TResult> JoinMany<TSource, TInner, TResult>(
    this IQueryable<TSource> source,
    Expression<Func<TSource, IImmutableRelation<TInner>>> relation,
    Expression<Func<TSource, TInner, TResult>> resultSelector,
    Expression<Func<TSource, TInner, bool>>? on = null)
    where TInner : IModelInstance;

public static IQueryable<TResult> LeftJoinBy<TSource, TInner, TResult>(
    this IQueryable<TSource> source,
    Expression<Func<TSource, TInner?>> relation,
    Expression<Func<TSource, TInner?, TResult>> resultSelector,
    Expression<Func<TSource, TInner, bool>>? on = null)
    where TInner : IModelInstance;

public static IQueryable<TResult> LeftJoinMany<TSource, TInner, TResult>(
    this IQueryable<TSource> source,
    Expression<Func<TSource, IImmutableRelation<TInner>>> relation,
    Expression<Func<TSource, TInner?, TResult>> resultSelector,
    Expression<Func<TSource, TInner, bool>>? on = null)
    where TInner : IModelInstance;
```

Open questions:

- Whether `JoinBy(...)` should accept nullable and non-nullable singular relation delegates through one overload or separate overloads.
- Whether `LeftJoinMany(...)` should emit one null child row for no match, or use a grouped result shape in a later API. The null-child row is closer to SQL left join behavior.
- Whether `on:` should permit only simple comparisons at first, matching current predicate support, or reuse the broader `WhereVisitor` predicate translator immediately.
- Whether composite relation metadata should be supported in the first implementation. It should be technically easier than explicit anonymous-object key joins, but it still needs careful materialization and null-key handling.

## Query Engine Requirements

The proposed API is not just extension-method sugar. Custom query methods are not automatically understood by the current Remotion LINQ parser and executor path. DataLinq needs a real joined-query plan.

### 1. Parser Integration

DataLinq needs one of these integration strategies:

1. Teach the query parser about DataLinq-specific join methods.
2. Rewrite `JoinBy(...)` and `JoinMany(...)` expression trees into ordinary `Queryable.Join(...)` forms before Remotion parses them.
3. Move joined-query parsing into a DataLinq-owned expression processing layer before or around Remotion.

The least invasive first attempt is probably a rewrite to ordinary join semantics for inner joins. The catch is that relation metadata lookup needs access to the provider metadata and the related table source. That may be awkward for already-composed query shapes.

For left joins and join-local `on:`, a custom joined-query plan is likely cleaner than pretending everything is standard LINQ.

### 2. Joined Query Plan

The current executor has a special `ExecuteJoinedCollection<T>` path for one `JoinClause`. That needs to become a reusable plan with:

- source slots for every table participating in the query
- stable SQL aliases for each source slot
- join edges with join type, target table, key column pairs, and optional `on:` predicate
- mapping from Remotion query sources or DataLinq custom relation sources to source slots
- a final projection expression compiled against materialized source slots

Conceptually:

```text
JoinedQueryPlan
  Root: Orders as t0
  Joins:
    Inner Customers as t1 on t0.CustomerId = t1.Id
    Inner OrderItems as t2 on t0.Id = t2.OrderId and t2.Quantity > 0
    Left Products as t3 on t2.ProductId = t3.Id
  Where:
    t0.CreatedAt >= @from
    t1.IsActive = true
  Projection:
    rows => new { order = rows[0], customer = rows[1], item = rows[2], product = rows[3] }
```

### 3. Multiple Standard Joins

Standard query syntax and chained `Join(...)` should support more than one join:

```csharp
from order in db.Query().Orders
join customer in db.Query().Customers on order.CustomerId equals customer.Id
join item in db.Query().OrderItems on order.Id equals item.OrderId
select new { order, customer, item };
```

Implementation work:

- remove the single-join restriction
- parse multiple `JoinClause` body clauses
- assign aliases deterministically
- select primary keys for every source slot
- materialize every source slot per result row
- apply the result selector after materialization

The current joined projection visitor maps query sources to an object array. That model can scale, but it needs to handle the transparent identifiers that C# query syntax may introduce for multi-join query expressions.

### 4. Filtering, Ordering, and Paging Over Joined Results

The current implementation rejects `Where(...)`, `OrderBy(...)`, `Skip(...)`, `Take(...)`, and result operators over joined results. Complex joins are not useful without those.

Required work:

- translate `Where(...)` predicates that reference any source in the joined row shape
- translate `OrderBy(...)` and `ThenBy(...)` over joined row members
- apply `Skip(...)` and `Take(...)` to the joined SQL result
- support scalar result operators such as `Any()` and `Count()` over joined queries
- keep `Single(...)` and `First(...)` semantics aligned with existing result-operator behavior

This probably requires generalizing `WhereVisitor` and `OrderByVisitor` so they can resolve column access against a source-slot map instead of assuming one root table.

### 5. Relation Metadata Resolution

For `JoinBy(...)` and `JoinMany(...)`, the relation expression must resolve to exactly one generated relation property.

Supported first shapes:

```csharp
order => order.Customer
customer => customer.Orders
x => x.order.Customer
x => x.customer.Orders
x => x.item.Product
```

Rejected first shapes:

```csharp
order => order.Customer.ParentAccount
order => order.Customer ?? fallback
order => GetCustomer(order)
order => order.Customer.Name
```

The resolver needs to:

- find the relation member access in the expression tree
- identify the owner model source slot
- look up `RelationProperty` on the owner model metadata
- identify the related table from `RelationPart.GetOtherSide()`
- produce one or more key column pairs
- determine cardinality from relation part type and property type

For singular FK relation properties, `JoinBy(...)` joins from the owner's FK columns to the related table's candidate key columns.

For collection relation properties, `JoinMany(...)` joins from the owner's candidate key columns to the child table's FK columns.

### 6. Join-Local Predicate Translation

`on:` predicates need a two-source predicate translator. It must resolve:

```csharp
(order, customer) => customer.IsActive
(x, item) => item.Quantity > 0 && x.order.CreatedAt <= item.CreatedAt
```

For the first implementation, the allowed predicate body should be deliberately close to existing `Where(...)` support:

- direct member comparisons
- local constants and captured values
- supported string/date/nullable/member functions where already available
- simple `&&`, `||`, and `!`
- column-to-column comparisons between the two relevant source slots

Unsupported first shapes:

- relation traversal inside `on:`
- subqueries
- local collection predicates inside `on:` unless the shared local-sequence translator can be safely reused
- method calls that are not already translatable in normal `Where(...)`

For left joins, `on:` must render into the SQL `ON` group, not into the global `WHERE` group.

### 7. Projection and Materialization

The existing joined implementation selects primary keys from both sides, materializes each row through DataLinq caches, then applies the projection in .NET. That is consistent with current post-materialization projection semantics.

The same model can work for multi-join:

- SQL selects primary keys for every joined source slot
- DataLinq materializes each non-null slot through the table cache
- the projection delegate receives an object array of materialized rows
- inner joins require every slot to materialize
- left joins allow null right-side slots

Outer join null handling needs care. For a left-joined table, all selected PK aliases may be database null. That should become a null source slot, not a failed key lookup.

Composite primary keys need similar care:

- all key components null means no joined row
- some null components probably indicate invalid data or an unsupported join shape
- provider null semantics need regression coverage

### 8. SQL Builder Support

The lower-level SQL builder already supports inner, left, and right joins. The LINQ layer needs to use that more generally.

Required SQL support:

- multiple joins
- composite `ON` equality pairs
- additional `ON` predicates
- global `WHERE` predicates over joined aliases
- ordering over joined aliases
- provider-specific identifier escaping remains centralized
- predictable generated aliases for diagnostics and tests

Right joins should not be part of the initial relation-aware API. Left joins are enough for common ORM usage, and right joins add mental overhead without much practical value.

### 9. Diagnostics

Unsupported joins should fail loudly and specifically.

Examples:

- `JoinBy relation expression '...' does not resolve to a generated relation property.`
- `JoinMany requires a generated collection relation property.`
- `LeftJoinBy projection cannot dereference nullable joined row without a null check.` if static analysis can catch this; otherwise leave it to C# nullable warnings.
- `Join-local predicate '...' is not supported. Only direct member comparisons and simple boolean groups are supported.`
- `Filtering over joined query source '...' is not supported yet.` during intermediate implementation slices.

The diagnostic should name the unsupported expression and the nearest supported rewrite.

## Implementation Workstreams

### Workstream A: Query Syntax Join Composition

Goals:

- make ordinary C# query syntax a documented, tested path
- generalize the current explicit `Join(...)` executor beyond one join

Tasks:

1. Add tests for single query-syntax join lowering to the existing join behavior.
2. Add tests for two and three explicit joins.
3. Support `Where(...)` after joins.
4. Support `OrderBy(...)`, `Skip(...)`, `Take(...)`, and `Count()` over joined rows.
5. Update `Supported LINQ Queries.md` only after behavior ships.

### Workstream B: Relation Resolver

Goals:

- resolve generated relation properties from expression trees
- convert relation metadata into join key column pairs

Tasks:

1. Add a focused relation-expression resolver with tests.
2. Support direct model-root relation access and relation access through anonymous joined row shapes.
3. Reject computed expressions and multi-hop relation traversal.
4. Cover singular and collection relations.
5. Decide first-slice composite relation support.

### Workstream C: `JoinBy(...)` and `JoinMany(...)`

Goals:

- add relation-aware fluent inner joins
- keep generated SQL and materialization equivalent to explicit inner joins

Tasks:

1. Add public extension methods.
2. Integrate parser or expression rewrite support.
3. Translate singular relations with `JoinBy(...)`.
4. Translate collection relations with `JoinMany(...)`.
5. Support chained relation-aware joins.
6. Add docs with examples once shipped.

### Workstream D: Join-Local `on:`

Goals:

- support additive join predicates
- prepare the API for left joins without changing semantics later

Tasks:

1. Add `on:` overloads or optional parameters.
2. Translate simple two-source predicates into the SQL `ON` group.
3. Support column-to-column predicates between joined sources.
4. Reject relation traversal and subqueries.
5. Add tests proving `on:` and `.Where(...)` produce equivalent rows for inner joins where appropriate.

### Workstream E: Left Joins

Goals:

- add `LeftJoinBy(...)` and `LeftJoinMany(...)`
- preserve nullability and cardinality honestly

Tasks:

1. Add nullable joined-slot materialization.
2. Add left singular relation tests.
3. Add left collection relation tests.
4. Add tests proving `on:` preserves unmatched left rows while `.Where(...)` can filter them out.
5. Document nullability behavior with nullable reference types enabled.

### Workstream F: Docs and Support Matrix

Goals:

- keep planned behavior separate from shipped behavior
- prevent the API from promising more than the translator supports

Tasks:

1. Update user-facing querying docs only after each implementation slice lands.
2. Update the LINQ translation support matrix with exact supported shapes.
3. Add unsupported examples and diagnostics to `Supported LINQ Queries.md`.
4. Include query-syntax and fluent relation-aware examples.

## Recommended Execution Order

1. Standard C# query syntax and multi-join composition.
2. Relation metadata resolver.
3. `JoinBy(...)` and `JoinMany(...)` inner joins.
4. Join-local `on:` predicates.
5. `LeftJoinBy(...)` and `LeftJoinMany(...)`.
6. Documentation and support matrix updates after each shipped slice.

This order keeps the foundation honest. If DataLinq cannot compose ordinary explicit joins with filtering and ordering, relation-aware joins will just be prettier syntax over a weak engine.

## Exit Criteria

This feature area is complete when:

- query syntax supports practical multi-table inner joins
- explicit and relation-aware joins can be filtered, ordered, paged, and counted
- `JoinBy(...)` resolves singular generated relation properties without explicit key selectors
- `JoinMany(...)` resolves generated collection relation properties without explicit key selectors
- `on:` predicates render into SQL `ON` clauses
- left joins preserve unmatched source rows and expose nullable joined values
- unsupported join shapes throw focused `QueryTranslationException` diagnostics
- docs and the support matrix describe only the actually shipped support boundary
