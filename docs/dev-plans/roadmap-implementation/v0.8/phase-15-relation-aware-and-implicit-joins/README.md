> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 15: Relation-Aware and Implicit Joins

**Status:** Implemented for the implicit singular relation predicate/ordering slice after Phase 14 source-slot join composition.

## Purpose

Phase 15 turned the stronger source-slot join engine into the first model-aware query slice users actually feel: SQL-backed implicit singular relation traversal in predicates and ordering.

The original target also included `JoinBy(...)`, `JoinMany(...)`, join-local `on:` predicates, left joins, and simple implicit relation projection. The shipped Phase 15 slice did not include that broad surface. That is intentional. Those APIs need real projection, cardinality, and nullability design; shipping them as incidental member-access translation would make the support matrix lie.

The public API should remain rooted in `IQueryable<T>` extension methods so the same code works from either query root:

```csharp
var q = db.Query();
var tq = transaction.Query();

var readOnlyRows = q.Orders.JoinBy(order => order.Customer, (order, customer) => new { order, customer });
var transactionRows = tq.Orders.JoinBy(order => order.Customer, (order, customer) => new { order, customer });
```

## Implemented Boundary

In scope for the shipped Phase 15 slice:

- relation-expression resolver for generated singular and collection relations
- singular implicit relation traversal in `Where(...)`, `OrderBy(...)`, and `ThenBy(...)`
- repeated traversal of the same relation reusing one implicit join source slot
- read-only and transaction-local query-root parity for the supported implicit relation shapes
- diagnostics for unsupported relation expressions, implicit traversal, and predicates

Out of scope:

- implicit singular relation projection; Phase 19 owns that follow-up
- `JoinBy(...)` and `JoinMany(...)` inner joins
- join-local `on:` predicates rendered into SQL `ON` groups
- `LeftJoinBy(...)` and `LeftJoinMany(...)` with nullable joined values
- `net10.0` tests for standard explicit `Queryable.LeftJoin(...)` when available
- eager loading disguised as joins
- direct `db.Table` shortcuts that bypass the generated `db.Query()` surface
- collection relation projection through implicit traversal
- multi-hop relation traversal in the first implicit-join API
- nullable implicit singular traversal until left-join null semantics are deliberately designed
- client-side fallback for unsupported predicates
- dependency-tracked result-set caching

## Related Work That Should Land Together

These items are tightly coupled enough that later slices should keep using the same source-slot model:

1. **Shared relation resolver.** `JoinBy(...)`, `JoinMany(...)`, implicit singular joins, and left joins should all use one resolver for generated relation properties.
2. **Source-slot reuse.** Repeated references such as `order.Customer.Name` and `order.Customer.IsActive` should reuse one joined source slot.
3. **Joined predicate and projection binding.** Implicit joins are only acceptable if related member access binds to SQL aliases, not to post-materialization lazy relation loading.
4. **Left-join null materialization.** `LeftJoinBy(...)`, `LeftJoinMany(...)`, and nullable future implicit traversal all need the same nullable source-slot behavior.
5. **Support matrix updates.** Public docs should move one shipped slice at a time; roadmap docs can describe the target, but user docs must not imply support before tests land.

Work that should not be pulled into this phase:

- scalar converter and typed-key ergonomics, except for preserving provider-value normalization seams needed by joins
- dependency-tracked result/module caching
- arbitrary nested collection projections
- full `GroupBy(...)` or broad subquery support

## Recommended Order

1. Keep the shipped shared relation-expression resolver and rejection diagnostics.
2. Add implicit singular relation joins for simple projections in Phase 19.
3. Add relation access through supported joined/query-syntax row shapes only after projection binding is SQL-backed.
4. Implement `JoinBy(...)` and `JoinMany(...)` for inner joins after the explicit/query-syntax join engine is strong enough.
5. Add join-local `on:` predicates.
6. Add `LeftJoinBy(...)` and `LeftJoinMany(...)`.
7. Add `net10.0` standard `Queryable.LeftJoin(...)` tests and support if the expression parser needs special handling.
8. Update user docs and the support matrix only for shipped behavior.

## Source Plans

- [Relation-Aware Join API](../../../query-and-runtime/Relation-Aware%20Join%20API.md)
- [0.8 Phase 14 Source-Slot Join Composition](../phase-14-source-slot-join-composition/README.md)
- [0.8 Phase 19 SQL-Backed Projection Rows and Implicit Relation Projection](../phase-19-sql-backed-projection-rows-and-implicit-relation-projection/README.md)
- [0.9 Join and Grouping Continuation Implementation Plan](../../v0.9/Join%20and%20Grouping%20Continuation%20Implementation%20Plan.md)
- [LINQ Translation Support Matrix](../../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)
- [Implementation Plan](Implementation%20Plan.md)

## Exit Criteria

The shipped Phase 15 slice is done when:

- implicit singular relation traversal is SQL-backed for supported predicates and ordering
- supported implicit relation shapes work from both `db.Query()` and `transaction.Query()`
- implicit collection traversal remains rejected except for documented `Any(...)` and existence-equivalent `Count(...)` patterns
- unsupported shapes throw focused `QueryTranslationException` diagnostics
- docs and support matrix describe only actually shipped join behavior
