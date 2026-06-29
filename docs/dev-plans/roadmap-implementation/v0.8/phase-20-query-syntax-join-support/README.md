> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 20: Query-Syntax Join Support

**Status:** In progress after SQL-backed projection rows.

## Purpose

Phase 20 makes standard C# query-syntax inner joins a documented and tested path over the DataLinq query plan.

The target is ordinary C# syntax that users naturally reach for when a query stops being a single chain:

```csharp
var rows =
    from departmentEmployee in db.Query().DepartmentEmployees
    join department in db.Query().Departments
        on departmentEmployee.dept_no equals department.DeptNo
    where department.Name.StartsWith("S")
    orderby departmentEmployee.emp_no
    select new
    {
        departmentEmployee.emp_no,
        DepartmentName = department.Name
    };
```

This phase is about the expression tree that the C# compiler produces. DataLinq should not parse C# source text, and it should not treat anonymous transparent identifiers as opaque runtime objects. The query plan needs to bind transparent-identifier members back to source slots.

## Scope

In scope:

- C# query-syntax inner joins that lower to direct `Queryable.Join(...)` calls over DataLinq query roots
- direct member equality keys already supported by the explicit join baseline
- transparent-identifier binding for joined source-slot members
- `where`, `orderby`, `thenby`, paging, `Any()`, and `Count()` over supported query-syntax joined row shapes
- SQL-backed `select new { ... }` projections made of bindable joined source-slot values
- practical multi-inner-join query-syntax shapes if transparent-identifier binding remains clear
- read-only and transaction-root parity
- focused diagnostics for unsupported query-syntax join shapes

Out of scope:

- `group join` and `into` groups
- left/outer join patterns such as `DefaultIfEmpty()`
- composite anonymous-object join keys
- non-direct or computed join keys
- relation-aware fluent APIs such as `JoinBy(...)` and `JoinMany(...)`
- join-local `on:` predicate APIs
- nullable left-join materialization
- client-side fallback for unsupported transparent-identifier shapes

## Design Requirements

The parser should treat query syntax as compiler-lowered LINQ:

- inspect the expression tree shape produced by the C# compiler
- bind transparent-identifier fields to source slots or projection members, not to runtime anonymous objects
- reuse the explicit join source-slot model instead of inventing a parallel query-syntax join model
- keep direct source roots, aliases, keys, and projected values visible in plan snapshots
- build on Phase 19 SQL-backed projection rows for provider-side `select new { ... }`
- reject any transparent-identifier shape that cannot be traced to supported source-slot values

The safest first implementation may start by proving single query-syntax joins and then extend to multi-join chains. Do not claim multi-join support until tests cover the compiler-lowered shape.

## Verification

Required tests:

- expression parser tests for single query-syntax joins
- plan snapshots showing query-syntax joins as ordinary source-slot joins
- SQL-shape tests proving joined aliases, predicates, ordering, and projection aliases
- behavior tests comparing query-syntax joins with in-memory LINQ across SQLite, MySQL, and MariaDB
- transaction-root parity for supported query-syntax joins
- unsupported diagnostics for `group join`, left-join patterns, composite keys, computed keys, and opaque transparent identifiers
- docs/support-matrix updates only for the tested query-syntax join shapes

## Exit Criteria

Phase 20 is done when:

- supported C# query-syntax inner joins parse to DataLinq source-slot join plans
- transparent identifiers bind back to source-slot values for predicates, ordering, paging, result operators, and projection rows
- supported query-syntax joins work from both `db.Query()` and `transaction.Query()`
- unsupported query-syntax join shapes fail with focused DataLinq diagnostics
- public docs and the support matrix describe only the tested query-syntax join behavior
