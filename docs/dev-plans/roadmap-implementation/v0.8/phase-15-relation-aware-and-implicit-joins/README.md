> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 15: Relation-Aware and Implicit Joins

**Status:** In progress for the implicit singular relation predicate/ordering slice after Phase 14 source-slot join composition.

## Purpose

Phase 15 turns the stronger source-slot join engine into the model-aware query surface users actually want:

- `JoinBy(...)` and `JoinMany(...)` for explicit relation-aware joins
- narrow implicit singular relation joins for predicates, ordering, and simple projections
- join-local `on:` predicates
- `LeftJoinBy(...)` and `LeftJoinMany(...)`
- standard `Queryable.LeftJoin(...)` coverage on `net10.0` where the BCL API is available

This belongs in 0.8 if Phase 14 lands in 0.8. Leaving relation-aware and implicit joins for later would ship the new parser with a technically cleaner engine but the same clunky user-facing join story.

The public API should remain rooted in `IQueryable<T>` extension methods so the same code works from either query root:

```csharp
var q = db.Query();
var tq = transaction.Query();

var readOnlyRows = q.Orders.JoinBy(order => order.Customer, (order, customer) => new { order, customer });
var transactionRows = tq.Orders.JoinBy(order => order.Customer, (order, customer) => new { order, customer });
```

## Execution Boundary

In scope:

- relation-expression resolver for generated singular and collection relations
- `JoinBy(...)` and `JoinMany(...)` inner joins
- singular implicit relation traversal in `Where(...)`, `OrderBy(...)`, `ThenBy(...)`, and simple `Select(...)`
- relation access through already-joined anonymous row shapes
- read-only and transaction-local query-root parity for every supported relation-aware join shape
- join-local `on:` predicates rendered into SQL `ON` groups
- `LeftJoinBy(...)` and `LeftJoinMany(...)` with nullable joined values
- `net10.0` tests for standard explicit `Queryable.LeftJoin(...)` when available
- diagnostics for unsupported relation expressions, implicit traversal, and predicates

Out of scope:

- eager loading disguised as joins
- direct `db.Table` shortcuts that bypass the generated `db.Query()` surface
- collection relation projection through implicit traversal
- multi-hop relation traversal in the first implicit-join API
- nullable implicit singular traversal until left-join null semantics are deliberately designed
- client-side fallback for unsupported predicates
- dependency-tracked result-set caching

## Related Work That Should Land Together

These items are tightly coupled enough that splitting them too far apart would create churn:

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

1. Add the shared relation-expression resolver and rejection diagnostics.
2. Implement `JoinBy(...)` and `JoinMany(...)` for inner joins.
3. Support relation access through already-joined anonymous row shapes.
4. Add implicit singular relation joins for predicates and ordering.
5. Add implicit singular relation joins for simple projections.
6. Add join-local `on:` predicates.
7. Add `LeftJoinBy(...)` and `LeftJoinMany(...)`.
8. Add `net10.0` standard `Queryable.LeftJoin(...)` tests and support if the expression parser needs special handling.
9. Update user docs and the support matrix only for shipped behavior.

## Source Plans

- [Relation-Aware Join API](../../../query-and-runtime/Relation-Aware%20Join%20API.md)
- [0.8 Phase 14 Source-Slot Join Composition](../phase-14-source-slot-join-composition/README.md)
- [Old Phase 14 Relation-Aware Joins and Left Joins](../../phase-14-relation-aware-joins-and-left-joins/README.md)
- [LINQ Translation Support Matrix](../../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)
- [Implementation Plan](Implementation%20Plan.md)

## Exit Criteria

Phase 15 is done when:

- singular and collection relation metadata can drive fluent inner joins
- relation-aware joins compose after earlier joins
- implicit singular relation traversal is SQL-backed for supported predicates, ordering, and projections
- supported relation-aware and implicit join shapes work from both `db.Query()` and `transaction.Query()`
- implicit collection traversal remains rejected except for documented `Any(...)` and existence-equivalent `Count(...)` patterns
- `on:` predicates render as join-local SQL conditions
- left joins preserve unmatched source rows and expose nullable joined values
- standard explicit `Queryable.LeftJoin(...)` has a documented `net10.0` support decision
- unsupported shapes throw focused `QueryTranslationException` diagnostics
- docs and support matrix describe only actually shipped join behavior
