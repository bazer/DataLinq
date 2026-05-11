> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 13: Explicit Multi-Join Composition

**Status:** Planned after Phase 12.

## Purpose

Phase 13 makes ordinary explicit joins genuinely useful before DataLinq adds prettier relation-aware join syntax.

The rule is simple: if DataLinq cannot compose standard C# query-syntax joins with filtering, ordering, paging, and result operators, then `JoinBy(...)` would just be attractive syntax over a weak engine.

## Execution Boundary

In scope:

- standard C# query-syntax joins over multiple tables
- chained explicit `Join(...)` support beyond the current single-join boundary
- `Where`, `OrderBy`, `ThenBy`, `Skip`, `Take`, `Any`, and `Count` over joined results
- joined materialization through provider-key components rather than legacy key wrappers
- diagnostics for unsupported join shapes

Out of scope:

- relation-aware `JoinBy(...)` and `JoinMany(...)`
- left joins
- join-local `on:` predicates beyond what is needed for explicit join parity
- Remotion replacement
- full DataLinq-owned query parser

## Source Plans

- [Relation-Aware Join API](../../query-and-runtime/Relation-Aware%20Join%20API.md)
- [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)
- [Phase 10 Key and Allocation Foundation](../phase-10-key-and-allocation-foundation/README.md)

## Recommended Order

1. Add tests for query-syntax lowering to the existing single-join behavior.
2. Add failing tests for two and three explicit joins.
3. Generalize joined source-slot and alias handling.
4. Support filtering, ordering, paging, and count/any over joined row shapes.
5. Make joined materialization use provider-key components directly.
6. Update user docs and the support matrix only for shipped join shapes.

## Exit Criteria

Phase 13 is done when:

- practical multi-table inner joins work through standard query syntax
- joined results can be filtered, ordered, paged, and counted
- joined materialization does not reintroduce avoidable `IKey` allocation on generated provider-key paths
- unsupported join shapes fail with focused diagnostics
- documentation describes the exact shipped support boundary
