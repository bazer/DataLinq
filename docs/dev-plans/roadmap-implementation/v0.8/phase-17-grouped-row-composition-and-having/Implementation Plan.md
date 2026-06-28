> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 17 Implementation Plan

**Status:** In progress.

## Objective

Make SQL-backed grouped aggregate rows composable for the narrow shapes that can preserve SQL semantics: grouped `Where(...)` before projection as `HAVING`, and ordering, paging, filtering, `Any()`, and `Count()` over grouped projection rows when the later operators bind to aggregate-row members.

## Work Items

- Add first-class grouped predicate support for `group.Key` and supported grouped aggregate comparisons.
- Render pre-projection grouped `Where(...)` as SQL `HAVING`, not as a row-level `WHERE`.
- Bind grouped projection members for post-`Select(...)` ordering and filtering.
- Support `OrderBy`, `ThenBy`, `Skip`, and `Take` over grouped aggregate projection members.
- Support `Any()` and `Count()` over grouped projection rows using explicit SQL shapes.
- Add SQL-shape tests for `HAVING`, grouped ordering/paging, and grouped scalar reductions.
- Add behavior and transaction-root tests across active providers.
- Keep grouped element enumeration, client-computed grouped values, joined grouping, composite keys, and unsupported post-group composition rejected.
- Update public docs, support matrix, and roadmap pages only for the tested grouped-row composition shapes.

## Guardrails

- No materialized `IGrouping<TKey,TElement>` support.
- No grouped element enumeration.
- No computed or composite keys.
- No grouping over joins.
- No client-side sorting, filtering, or scalar fallback for grouped rows.
- No broad derived-table engine beyond the grouped-row aliases needed by this phase.

## Verification Plan

- Focused compliance tests for grouped `HAVING`, ordering, paging, `Any()`, and `Count()` across active providers.
- SQL-shape tests proving `HAVING` appears for group predicates and no grouped predicate is flattened into `WHERE`.
- Query plan snapshot coverage for grouped predicates and grouped-row ordering.
- Unsupported-shape diagnostics for grouped element enumeration and non-bindable grouped-row operators.
- Focused TUnit filters for grouped aggregate, parser, snapshot, SQL parity, and unsupported diagnostics tests.
