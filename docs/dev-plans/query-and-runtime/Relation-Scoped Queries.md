> [!WARNING]
> This is an unscheduled proposal, not a shipped API or part of the 0.10 implementation plan.

# Relation-Scoped Queries

**Status:** Proposed. Retained as a backlog idea; the detailed contract still needs design and evidence.

**Target release:** Unscheduled. Explicitly excluded from 0.10.

**Last reviewed:** 2026-08-30.

**Prerequisites:** A stable query/read-source and async execution contract, a deliberate representation of relation membership in query plans, and reviewed source/cache/testing semantics. Scheduling requires an explicit roadmap revision with ownership and exit evidence.

**Origin:** The earlier AAPI-10 proposal in the [0.10 Async Public API Decisions](../roadmap-implementation/v0.10/Async%20Public%20API%20Decisions.md) was moved here so 0.10 can focus on asynchronous execution without introducing a new relation query surface.

## Why Keep This Idea

A collection relation is convenient when the caller wants the related rows. It becomes expensive when the relation is large and the caller needs only a small subset. Today, local enumeration can materialize the entire relation before applying a filter or `Take`. Making that enumeration asynchronous does not, by itself, move the filtering into the database.

An explicit query entry point could let callers filter, order, page, project, or execute supported aggregates over related rows through the existing DataLinq query provider. It could also avoid repeating the parent/child key predicate in application queries, especially for composite or converted keys.

This remains useful independently of async: both synchronous and asynchronous terminals could execute the same composed query. It is therefore a separate query feature, not a prerequisite for async relation loading.

## Candidate Consumer Shape

Illustrative future code, with model/property names chosen for readability:

```csharp
var query = employee.Salaries.Query();

var recentSalaries = await query
    .Where(salary => salary.FromDate >= cutoff)
    .OrderBy(salary => salary.FromDate)
    .Take(10)
    .ToListAsync(ct);

// The same query surface also supports synchronous execution.
var count = query.Count();
```

The candidate is a synchronous, parameterless `Query()` returning `IQueryable<T>` for the relation's row type. Construction would perform no database I/O and need no cancellation token; an async terminal would accept the optional token.

Keep the relation's existing `IEnumerable<T>` surface. Do not make `IImmutableRelation<T>` itself inherit `IQueryable<T>`: that would change LINQ overload selection and local predicate behavior at existing call sites. An explicit method makes the caller's choice visible.

In 0.10, callers can instead use existing database/transaction query roots with explicitly written relation predicates. The async relation view remains useful for local composition, without a promise of provider-side filtering.

## Candidate Semantics

These are design constraints to investigate, not frozen API requirements:

- Carry the relation's membership predicate into the query plan, including scalar, composite, nullable, and converted-key semantics. A related-table query without the parent constraint is incorrect.
- Use DataLinq's real query provider and its supported translation/capability validation. Loading `Values` and then calling `AsQueryable()` would preserve the full-load problem and substitute local execution.
- Compose supported filtering, ordering, and paging before execution so a caller can request a subset without first loading every related row. Do not promise a particular database execution plan or bypass valid row-cache use.
- Preserve the originating read source or transaction, row identity, visibility, cancellation, and source ownership.
- Never publish a filtered or limited result as a completely loaded relation cache. Decide separately whether and how individual materialized rows participate in existing row caching.
- Keep ordinary relation enumeration and local LINQ semantics intact. This feature should not silently translate an existing local `Func<T, bool>` predicate.
- Reuse the existing supported query language. The convenience of a relation root is not approval for arbitrary joins, grouping, client fallback, or a new query engine.

## Why This Is Deferred

The method is small; the execution contract is not. It crosses boundaries that 0.10 does not need to redesign to support honest async loading.

### Query Roots And Optimizations

The [query plan parser](../../../src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs) must recognize a root whose meaning includes relation membership, not merely a related table. Review how that constraint survives normalization, projection, terminal rewrites, and execution dispatch.

In particular, a primary-key lookup optimization must not return a cached row that exists in the table but belongs to another parent. Scalar/composite and converted-key cases need explicit evidence. Existing translated navigation predicates must continue to bind and translate as before.

### Source Lifetime And Visibility

Decide which source is retained when the query is created, how a transaction-bound relation behaves after completion, and what subsequent execution can observe. Query construction must not silently detach a transaction relation onto a database root merely to keep the query usable.

Repeated execution, transaction-local changes, invalidation between construction and execution, and already cached relation snapshots need a coherent visibility rule. Local .NET comparisons can also differ from provider comparisons, including collation and null handling; explicit composition should make that semantic boundary clear.

### Cache Completeness And Materialization

The [current relation implementation](../../../src/DataLinq/Instances/ImmutableRelation.cs) combines loading with collection/cache behavior. A provider subset must preserve row identity without misrepresenting relation completeness. Cached local enumeration and newly executed provider queries need not be identical snapshots; documentation and tests must explain the difference.

### Public Implementations And Testing

Decide where the future member belongs and how custom relations declare query capability. A standalone relation containing test rows does not automatically have a DataLinq execution source. It must not silently expose LINQ-to-Objects as though it were the production provider.

If a future testing adapter supplies a query root, prefer a deliberately configured real `DataLinq.Memory` source within its supported capabilities, or an explicit unsupported-capability result. Do not require every object-graph fixture to construct a provider. None of this relation-query capability work is required for the 0.10 relation doubles or their async execution members.

## Questions Before Scheduling

1. What representation carries relation membership through the existing query pipeline without a general parser rewrite?
2. Is the public member on the relation interface, a capability interface, or a relation-specific extension, and what is the custom-implementation migration?
3. When are the relation key and source captured, and what happens after source completion or invalidation?
4. Can valid cached rows satisfy execution without losing membership, ordering, visibility, or projection semantics?
5. How do absent/partially null composite relation keys behave under existing metadata rules?
6. Which SQL and Memory query shapes are supported, and how does an unsupported standalone fixture fail?
7. What evidence demonstrates a useful improvement for a large relation without requiring a broader query-language or cache-policy change?

## Future Exit Evidence

- I/O-free construction, ordinary sync/async terminals, optional tokens, and preserved local LINQ overload binding.
- Membership retained through scalar/composite/converted-key filtering and exact-primary-key lookup, including rejection of a matching table row outside the relation.
- Supported provider filtering/paging on a cold, large relation without first materializing the whole relation.
- Correct row identity, transaction/source visibility, invalidation, and complete-versus-partial cache publication.
- Cancellation, early enumeration disposal, repeated execution, and use after source completion.
- Explicit custom/test implementation behavior and Memory capability validation without a second query provider.
- Consumer and compatibility checks, migration guidance where needed, and a scoped performance comparison.

## Explicit Non-Goals

- No 0.10 API, parser implementation, test-double query requirement, or release gate.
- No replacement of relation `IEnumerable<T>` with `IQueryable<T>`.
- No automatic translation of local relation LINQ or changes to existing enumeration semantics.
- No new join/grouping engine, eager-loading/batching system, relation mutation API, cache-coalescing policy, or Memory write/transaction capability.
- No claim that this proposal is necessary to implement the accepted async relation operations.

## Related Plans

- [Async and Lazy Loading](Async%20and%20Lazy%20Loading.md)
- [Relation-Aware Join API](Relation-Aware%20Join%20API.md)
- [Model Testing and Mocking Support](../testing/Model%20Testing%20and%20Mocking%20Support.md)
- [Development Roadmap](../Roadmap.md)
