> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 18 Implementation Plan

**Status:** In progress.

## Objective

Extend the Phase 13B/16/17 grouped aggregate row model from one direct source key to named SQL-renderable key members and supported joined source-slot inputs, without materialized `IGrouping<TKey,TElement>` support or client fallback.

## Implementation Shape

Phase 18 needs a grouping binding model, not more ad hoc expression checks:

- represent grouping keys as named plan members when the key selector is an anonymous-object key
- keep scalar direct keys as the existing single-key case
- bind `group.Key.Member` to the planned key member by name
- render every key member in both the select list and `GROUP BY`
- let SQL-renderable computed key values reuse existing `QueryPlanFunctionValue` support, such as date parts and string functions
- bind grouped aggregate selectors either to a root source or to a joined projection, depending on the grouped input shape
- allow grouping over explicit joined projections only when key members and aggregate selectors map back to source-slot values
- allow grouping over implicit singular relation traversal only through the existing SQL-backed implicit join path

## Work Items

- [ ] Extend the grouping plan model with named key members and an element-binding context.
- [ ] Parse direct, composite anonymous-object, and SQL-renderable computed group keys.
- [ ] Bind `group.Key.Member` in grouped projections, grouped predicates, and grouped ordering.
- [ ] Support grouped aggregate selectors over joined projection members when they map to source-slot numeric values.
- [ ] Allow grouping after supported explicit joined-row projections and SQL-backed implicit singular relation traversal.
- [ ] Keep whole composite `group.Key` projection, grouped element enumeration, client-computed keys, collection relation grouping, and non-bindable joined projection members rejected.
- [ ] Add provider behavior tests for composite keys, computed keys, enum/nullable/string keys, explicit joined grouping, and implicit relation grouping.
- [ ] Add plan snapshot and SQL-shape tests proving named key members and joined source-slot grouping.
- [ ] Update the phase README, roadmap pages, public LINQ docs, support matrix, and internals docs for the exact supported boundary.

## Guardrails

- No materialized `IGrouping<TKey,TElement>` rows.
- No grouped element enumeration.
- No arbitrary client methods in group keys.
- No collection relation grouping or hidden row multiplication.
- No broad joined grouping over row-local computed projection members.
- No composite key projection as a whole object unless a later materialization design explicitly supports it.

## Verification Plan

- Build `src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj`.
- Run the grouped aggregate compliance filter across active providers.
- Run parser and snapshot compliance filters.
- Run unsupported diagnostics focused on GroupBy and grouped projection shapes.
- Run explicit/implicit join filters if joined grouping touches shared join binding behavior.
