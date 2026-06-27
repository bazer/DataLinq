> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 3: SQL Generation on Query Plan

**Status:** Next.

## Execution Plan

- [Implementation Plan](Implementation%20Plan.md)

## Purpose

Phase 3 moves SQL generation and query diagnostics off Remotion clause types. After this phase, Remotion may still parse expressions, but it should no longer be the semantic model consumed by the SQL translator.

## Scope

In scope:

- translate predicates from DataLinq plan nodes
- translate ordering, paging, and result operators from plan nodes
- resolve columns through source slots
- preserve provider-specific function rendering through abstract function/value nodes
- preserve fixed-condition semantics
- preserve nullable comparison behavior
- preserve local sequence behavior
- preserve scalar aggregate behavior
- preserve current relation `EXISTS` and narrow join behavior where practical
- replace `SqlQuery<T>.Where(WhereClause)` and `OrderBy(OrderByClause)` as the main translation boundary

Out of scope:

- changing public LINQ semantics
- making the new parser default
- broad join expansion
- non-SQL execution

## Design Rule

SQL rendering should be a consumer of the query plan. It should not pull Remotion expression nodes back into the middle of translation through convenience helpers. If a helper still needs `QuerySourceReferenceExpression`, `SubQueryExpression`, `WhereClause`, or `OrderByClause`, the migration is incomplete.

## Exit Criteria

- supported single-source queries generate equivalent SQL/results through the plan path
- SQL diagnostics name DataLinq plan/operator concepts rather than Remotion internals where practical
- provider compliance tests pass for changed SQL generation behavior
- Remotion remains only the parser producer for the migrated path
