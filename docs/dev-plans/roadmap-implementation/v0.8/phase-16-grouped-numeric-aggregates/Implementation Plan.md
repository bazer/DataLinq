> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 16 Implementation Plan

**Status:** In progress.

## Objective

Extend the existing SQL-backed grouped aggregate projection from direct-key `group.Count()` to direct numeric grouped `Sum`, `Min`, `Max`, and `Average`, while preserving the current non-goals around materialized `IGrouping<TKey,TElement>`, computed selectors, grouped composition, and joined grouping.

## Work Items

- Extend `QueryPlanGroupedAggregateValue` so grouped aggregate members carry both aggregate kind and selector value when the aggregate reads an element column.
- Parse `group.Sum(row => row.Column)`, `group.Min(...)`, `group.Max(...)`, and `group.Average(...)` in grouped aggregate projections.
- Keep `group.Count()` selectorless and preserve the existing grouped count plan/debug shape.
- Render grouped numeric aggregate SQL from plan values with stable projection aliases.
- Reuse the existing grouped aggregate data-reader projection path, with tests documenting result CLR conversion.
- Add behavior, SQL-shape, snapshot, parser, transaction-root, and unsupported-shape coverage.
- Update the public LINQ docs, the support matrix, and the 0.8 roadmap phase status after the behavior is verified.

## Guardrails

- No computed grouped aggregate selectors in this phase.
- No relation-property selectors.
- No `HAVING`, post-group ordering, paging, or terminal operators.
- No composite or computed group keys.
- No grouping over joined rows.
- No client-side fallback for unsupported grouped aggregate shapes.

## Verification Plan

- Focused compliance tests for grouped numeric aggregate behavior across active providers.
- SQL-shape assertions for `SUM`, `MIN`, `MAX`, `AVG`, `COUNT`, and `GROUP BY`.
- Query plan snapshot coverage for multiple grouped aggregate members.
- Unsupported-shape tests for computed grouped aggregate selectors.
- Focused TUnit run for grouped aggregate, parser, snapshot, and SQL parity tests.
