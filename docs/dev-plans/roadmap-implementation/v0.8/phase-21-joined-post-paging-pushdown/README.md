> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 21: Joined Post-Paging Pushdown

**Status:** Planned after query-syntax join support.

## Purpose

Phase 21 extends Phase 13's operator-order discipline to joined row shapes.

The deferred shape is currently rejected for good reason:

```csharp
var rows = db.Query().DepartmentEmployees
    .Join(
        db.Query().Departments,
        departmentEmployee => departmentEmployee.dept_no,
        department => department.DeptNo,
        (departmentEmployee, department) => new
        {
            departmentEmployee.emp_no,
            departmentEmployee.dept_no,
            DepartmentName = department.Name
        })
    .OrderBy(row => row.emp_no)
    .Take(10)
    .Where(row => row.dept_no == "d001")
    .ToList();
```

That query does not mean "filter before taking ten rows." It means "take ten joined rows, then filter that limited set." Phase 13 already handles that semantic boundary for single-source mapped rows. Phase 21 makes the same promise for supported joined projections.

## Scope

In scope:

- post-paging `Where(...)` over joined projection members that bind to source-slot values
- post-paging `OrderBy(...)`, `OrderByDescending(...)`, `ThenBy(...)`, and `ThenByDescending(...)` over bindable joined projection members
- `Any()` and `Count()` over paged joined sources when the SQL shape is explicit and tested
- derived joined-source SQL that preserves all required joined primary keys and projected source-slot values
- read-only and transaction-root parity
- SQL-shape tests proving the derived table boundary is present only when semantics require it
- focused diagnostics for unsupported joined pushdown shapes

Out of scope:

- row-local computed joined projection members
- relation-property projection inside explicit join selectors
- left joins and nullable joined rows
- `GroupJoin(...)`
- composite or computed join keys
- grouping over joined rows; Phase 18 owns that work
- nested derived-table pushdown beyond the deliberately tested joined boundary
- client-side fallback when joined pushdown cannot preserve semantics

## Design Requirements

Joined pushdown must preserve source-slot identity through the derived table:

- the inner query should select stable aliases for every joined source primary-key component needed for materialization
- the inner query should select stable aliases for projection members that later predicates or orderings bind to
- the outer query should bind post-paging predicates/orderings to those derived aliases, not to the original table aliases
- `QueryPlanOperation.Pushdown` or a joined-specific equivalent should make the boundary visible in plan snapshots
- `QueryPlanSqlBuilder` must stop rejecting join pushdown only after it can render the required derived source safely
- joined projection execution must keep using provider-key cache materialization after reading the pushed-down key aliases

Flattening this shape would be wrong. Client-side filtering would also be wrong for provider semantics and paging size. The correct implementation is a deliberate SQL boundary.

## Verification

Required tests:

- plan snapshots for joined post-paging pushdown
- SQL-shape tests proving `FROM (SELECT ...)` or equivalent derived-source SQL preserves joined keys and projection aliases
- behavior tests comparing joined post-paging filters/orderings with in-memory LINQ across SQLite, MySQL, and MariaDB
- `Any()` and `Count()` tests over paged joined sources where supported
- transaction-root parity for joined pushdown
- unsupported diagnostics for computed joined projection members, relation projection, grouped joins, and unsupported nested pushdown
- docs/support-matrix updates only for the tested joined pushdown shapes

## Exit Criteria

Phase 21 is done when:

- supported joined post-paging filters and orderings preserve C# LINQ operator order
- SQL rendering uses an explicit derived-source boundary for joined pushdown
- joined key aliases and projection aliases remain stable enough for execution and SQL-shape tests
- supported scalar result operators over paged joined rows behave consistently across active providers
- supported joined pushdown works from both `db.Query()` and `transaction.Query()`
- unsupported joined pushdown shapes remain rejected with focused `QueryTranslationException` diagnostics
- public docs and the support matrix describe only the tested joined pushdown behavior
