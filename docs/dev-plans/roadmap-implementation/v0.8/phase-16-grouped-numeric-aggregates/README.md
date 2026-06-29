> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 16: Grouped Numeric Aggregates

**Status:** Implemented for direct numeric grouped aggregate selectors.

## Purpose

Phase 16 extends the existing direct-key grouped aggregate projection from `Count()` to the numeric aggregate operators users actually need in reports:

```csharp
var departmentStats = db.Query().DepartmentEmployees
    .Where(row => row.dept_no.StartsWith("d00"))
    .GroupBy(row => row.dept_no)
    .Select(group => new
    {
        DeptNo = group.Key,
        Count = group.Count(),
        SumEmployeeNumbers = group.Sum(row => row.emp_no),
        MinEmployeeNumber = group.Min(row => row.emp_no),
        MaxEmployeeNumber = group.Max(row => row.emp_no),
        AverageEmployeeNumber = group.Average(row => row.emp_no)
    })
    .ToList();
```

The target is still SQL-shaped grouped aggregate rows. It is not materialized `IGrouping<TKey,TElement>` support.

## Scope

In scope:

- grouped `Sum(...)`, `Min(...)`, `Max(...)`, and `Average(...)`
- `Count()` alongside numeric aggregate members
- multiple aggregate members in one grouped projection
- direct numeric member selectors
- nullable numeric member selectors and nullable `.Value` member selectors for the tested nullable numeric column shape
- optional `Where(...)` before `GroupBy(...)`, preserving Phase 13B behavior
- active-provider coverage for SQLite, MySQL, and MariaDB
- focused diagnostics for unsupported grouped aggregate selectors

Out of scope:

- computed aggregate selectors such as `group.Sum(row => row.Value + 1)`
- relation-property aggregate selectors
- aggregate predicates, `HAVING`, or post-group composition
- composite keys and computed keys
- grouping over joined rows
- materialized `IGrouping<TKey,TElement>` values or grouped element enumeration
- client-side fallback when SQL cannot represent the aggregate

## Design Requirements

The plan model should keep grouped aggregates first-class:

- extend grouped aggregate plan values with an aggregate kind and selector value
- preserve the current grouped `Count()` shape without special-case string SQL leaking into parser code
- reuse the scalar aggregate selector validation rules where they are correct, but do not assume scalar empty-sequence behavior automatically matches grouped nullable aggregate behavior
- make result CLR types explicit for each aggregate so reader conversion is boring and provider-neutral

The SQL renderer should render aggregate expressions from plan values:

- `COUNT(*)` for `Count()`
- provider-compatible `SUM(column)`, `MIN(column)`, `MAX(column)`, and `AVG(column)` for supported selectors
- aliases that remain stable for data-reader materialization

## Verification

Required tests:

- plan snapshots for grouped numeric aggregate members
- SQL-shape tests proving explicit `GROUP BY` and aggregate select-list expressions
- behavior tests against SQLite, MySQL, and MariaDB
- nullable aggregate tests over the nullable numeric `employees.emp_no` selector shape
- unsupported selector diagnostics for computed selectors, relation selectors, and unsupported methods
- transaction-root parity for grouped numeric aggregates

## Exit Criteria

Phase 16 is done when:

- grouped `Count()` still works unchanged
- grouped numeric aggregates work in the same projection shape as `Count()`
- multiple aggregate members can be projected from one grouped query
- nullable aggregate semantics are documented through tests instead of guessed
- unsupported grouped aggregate selectors fail with `QueryTranslationException`
- public docs and the support matrix describe only the tested grouped numeric aggregate shapes
