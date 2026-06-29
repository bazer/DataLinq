> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 14 Implementation Plan: Source-Slot Join Composition

**Status:** Implemented for the explicit two-source join composition slice.

## Goal

Make the existing explicit two-source inner `Join(...)` baseline composable for the ordinary operators users naturally apply after a join result:

- `Where(...)`
- `OrderBy(...)`, `OrderByDescending(...)`, `ThenBy(...)`, and `ThenByDescending(...)`
- `Skip(...)` and `Take(...)`
- `Any()` and `Count()`

The first implementation slice is deliberately not a full query-syntax transparent-identifier engine. It composes over projected joined row members that the DataLinq plan can map back to source-slot values.

## Supported Shape For This Slice

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
    .Where(row => row.DepartmentName.StartsWith("S"))
    .OrderBy(row => row.dept_no)
    .ThenBy(row => row.emp_no)
    .Take(10)
    .ToList();
```

The parser should bind `row.DepartmentName`, `row.dept_no`, and `row.emp_no` back to the `QueryPlanProjectionMember` values produced by the join result selector. Predicate and ordering translation should then reuse the normal value/function/predicate renderer.

## Out Of Scope For This Slice

- joining over computed projection members
- filtering or ordering over joined projection members that are row-local client expressions
- multiple chained joins
- query-syntax transparent identifiers that project whole source entities
- `GroupJoin(...)`
- left joins
- composite keys
- relation-aware or implicit relation joins
- post-paging joined query pushdown unless a focused safe path is added with tests
- scalar aggregates over joined rows other than `Any()` and `Count()`

Those are real follow-up features. Smuggling them into this slice would make the support matrix lie.

## Workstreams

### 1. Contract Tests

- Add behavior tests for `Where(...)`, ordering, paging, `Any()`, and `Count()` over an explicit join result.
- Add transaction-root tests for the same composed join shape.
- Add SQL-shape tests proving predicates/orderings bind to joined source aliases.
- Add unsupported diagnostics for computed joined projection members and post-paging joined composition if not implemented.

### 2. Parser Binding

- Add projection-parameter binding for joined result parameters.
- Teach value conversion to resolve member access over a bound joined projection to the corresponding `QueryPlanProjectionMember.Value`.
- Reuse normal predicate/function/order conversion after that member resolution.
- Keep relation-property projection and nested database projection guards intact.

### 3. SQL And Execution

- Let `QueryPlanSqlBuilder` render joined predicates/orderings through existing source-slot aliases.
- Keep joined sequence execution on primary-key selector materialization.
- Let SQL scalar execution handle `Any()` and `Count()` over flat joined query shapes.
- Reject joined post-paging pushdown until the derived source can preserve all required joined columns and keys.

### 4. Documentation

- Update public docs and the support matrix only for the tested composed explicit-join slice.
- Keep Phase 15 relation-aware/implicit join docs framed as future work on top of this source-slot foundation.

## Verification

Focused checks:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*|/*/*/QueryPlanSnapshotTests/*|/*/*/QueryPlanSqlParityTests/*|/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

Completed focused checks:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*|/*/*/QueryPlanSnapshotTests/*ExplicitJoin*|/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*|/*/*/QueryPlanSnapshotTests/*|/*/*/QueryPlanSqlParityTests/*|/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build
```

Result: both focused runs passed across the active provider batches: `sqlite-file, sqlite-memory` and `mysql-8.4, mariadb-11.8`.

## Closeout Notes

- Added projection-parameter binding for joined result rows so predicates and orderings can resolve projected members back to source-slot values.
- Allowed flat `Where`, ordering, paging, `Any`, and `Count` over explicit two-source joined projections.
- Kept post-paging joined composition rejected until joined derived-source pushdown has a deliberate design.
- Buffered joined primary-key values before table-cache hydration so transaction-rooted joined projections do not issue nested reads while the joined key reader is open.
- Updated public docs and the support matrix for the tested joined composition slice only.

## Exit Criteria

- [x] Explicit join result predicates bind to source-slot values.
- [x] Explicit join result ordering and paging execute correctly.
- [x] `Any()` and `Count()` over explicit join results execute correctly.
- [x] Supported composed explicit joins work from both `db.Query()` and `transaction.Query()`.
- [x] Unsupported joined composition shapes keep focused diagnostics.
- [x] Public docs and the support matrix describe only the shipped join behavior.
