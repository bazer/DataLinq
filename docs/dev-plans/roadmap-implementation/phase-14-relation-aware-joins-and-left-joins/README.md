> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 14: Relation-Aware Joins and Left Joins

**Status:** Planned after Phase 13.

## Purpose

Phase 14 adds the model-aware join API users actually want after the explicit join engine is strong enough to support it.

This phase owns `JoinBy(...)`, `JoinMany(...)`, join-local `on:` predicates, and left-join behavior. It should use relation metadata to remove duplicated key selectors without hiding the fact that a join changes row shape and cardinality.

## Execution Boundary

In scope:

- relation-expression resolver for generated singular and collection relations
- `JoinBy(...)` and `JoinMany(...)` inner joins
- join-local `on:` predicates rendered into SQL `ON` groups
- `LeftJoinBy(...)` and `LeftJoinMany(...)`
- nullable joined-slot materialization and documentation
- diagnostics for unsupported relation expressions and predicates

Out of scope:

- eager loading disguised as joins
- multi-hop relation traversal in the first relation-aware API
- client-side fallback for unsupported predicates
- Remotion replacement
- dependency-tracked result-set caching

## Source Plans

- [Relation-Aware Join API](../../query-and-runtime/Relation-Aware%20Join%20API.md)
- [Phase 13 Explicit Multi-Join Composition](../phase-13-explicit-multi-join-composition/README.md)
- [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)

## Recommended Order

1. Add a focused relation-expression resolver with tests.
2. Implement `JoinBy(...)` and `JoinMany(...)` for inner joins.
3. Support relation access through already-joined anonymous row shapes.
4. Add join-local `on:` predicates with the same supported predicate subset as normal joined filters.
5. Add `LeftJoinBy(...)` and `LeftJoinMany(...)` with nullable joined values.
6. Document the `ON` versus `WHERE` behavior for left joins.

## Exit Criteria

Phase 14 is done when:

- singular and collection relation metadata can drive fluent inner joins
- relation-aware joins compose after earlier joins
- `on:` predicates render as join-local SQL conditions
- left joins preserve unmatched source rows and expose nullable joined values
- unsupported shapes throw focused `QueryTranslationException` diagnostics
- docs and support matrix describe only actually shipped join behavior
