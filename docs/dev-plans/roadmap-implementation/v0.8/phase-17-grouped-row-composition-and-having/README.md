> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 17: Grouped Row Composition and HAVING

**Status:** Planned after grouped numeric aggregates.

## Purpose

Phase 17 makes grouped aggregate rows composable without pretending they are entity rows.

The practical user goal is ordering, paging, and filtering grouped result rows:

```csharp
var topDepartments = db.Query().DepartmentEmployees
    .GroupBy(row => row.dept_no)
    .Select(group => new
    {
        DeptNo = group.Key,
        Count = group.Count()
    })
    .OrderByDescending(row => row.Count)
    .Take(10)
    .ToList();
```

It should also support SQL `HAVING`-style predicates over group keys and aggregate values:

```csharp
var busyDepartments = db.Query().DepartmentEmployees
    .GroupBy(row => row.dept_no)
    .Where(group => group.Count() > 5)
    .Select(group => new
    {
        DeptNo = group.Key,
        Count = group.Count()
    })
    .ToList();
```

## Scope

In scope:

- `OrderBy(...)`, `OrderByDescending(...)`, `ThenBy(...)`, and `ThenByDescending(...)` over grouped projection members
- `Skip(...)` and `Take(...)` over grouped projection rows
- `Where(...)` over grouped projection rows after `Select(...)` when it can bind to aggregate aliases
- narrow `Where(...)` before grouped `Select(...)` that maps to SQL `HAVING`
- `Any()` and `Count()` over grouped result rows where the SQL shape is explicit and tested
- stable aliases for group keys and aggregate members
- active-provider coverage for SQLite, MySQL, and MariaDB

Out of scope:

- arbitrary predicates over grouped elements
- grouped element enumeration
- grouping over joined rows
- composite keys and computed keys unless Phase 18 has already supplied them
- client-side sorting/filtering fallback for unsupported grouped rows
- materialized `IGrouping<TKey,TElement>` support

## Design Requirements

Grouped result rows need a real binding model:

- grouped projection members must be bindable by later ordering and filtering operators
- aggregate aliases must be stable across providers and subquery pushdown
- `HAVING` predicates should be represented as grouped predicates, not as ordinary row predicates accidentally rendered in `WHERE`
- post-projection filtering/order/paging may require a derived-table boundary rather than direct `HAVING`

The deciding rule is semantic honesty:

- predicates over `group.Count()` before projection are `HAVING`
- predicates over projected aggregate row members after projection are derived-row filters
- both need tests proving the generated SQL shape, not only matching rows

## Verification

Required tests:

- SQL-shape tests for `HAVING`
- SQL-shape tests for derived grouped rows when ordering/filtering uses projection aliases
- behavior tests for ordering, paging, and filtering grouped rows across active providers
- scalar result tests for `Any()` and `Count()` over grouped rows
- unsupported-shape diagnostics for predicates that would require grouped element enumeration or client fallback
- transaction-root parity for supported grouped composition shapes

## Exit Criteria

Phase 17 is done when:

- grouped aggregate projection rows can be ordered and paged without client fallback
- supported grouped filters render as either `HAVING` or a deliberate derived-table filter
- supported scalar result operators over grouped rows behave consistently across providers
- aliases and parameter bindings remain stable through grouped composition
- unsupported grouped composition fails with focused `QueryTranslationException`
- public docs and the support matrix distinguish `HAVING`, derived grouped-row filtering, and still-unsupported `IGrouping` behavior
