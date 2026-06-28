> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 13B Implementation Plan: Grouped Aggregate Projection Baseline

**Status:** Implemented for the direct mapped key plus `group.Key`/`group.Count()` slice.

## Goal

Add the first SQL-shaped `GroupBy(...)` support slice without pretending DataLinq supports materialized `IGrouping<TKey,TElement>` sequences.

The supported shape is deliberately narrow:

```csharp
db.Query().DepartmentEmployees
    .Where(row => row.dept_no.StartsWith("d00"))
    .GroupBy(row => row.dept_no)
    .Select(group => new
    {
        DeptNo = group.Key,
        Count = group.Count()
    })
    .ToList();
```

Anything that requires enumerating grouped elements, composing over grouped rows, using computed/composite keys, or grouping joined rows stays unsupported.

## Workstreams

### 1. Contract Tests First

- Add parser snapshots for `GroupBy(key).Select(g => new { g.Key, Count = g.Count() })`.
- Add SQL-shape tests that prove the renderer emits `GROUP BY`.
- Add provider behavior tests for SQLite, MySQL, and MariaDB.
- Add transaction-root parity tests for the same grouped projection.
- Keep existing bare `GroupBy(...).ToList()` diagnostics and broaden unsupported-shape tests for composite keys, computed keys, grouped element enumeration, and unsupported aggregate selectors.

### 2. Plan Model

- Add first-class grouping plan nodes:
  - group key value
  - grouped aggregate projection member
  - aggregate kind, starting with `Count`
- Keep grouped rows separate from entity projections. Grouped aggregate rows are not cache-backed table rows.
- Extend plan debug output so grouped keys and aggregate projection members are visible without leaking SQL text.

### 3. Parser

- Accept only `Queryable.GroupBy(source, keySelector).Select(groupProjection)`.
- Require a direct mapped member group key for the first slice.
- Require projections to contain only `g.Key` and supported aggregate calls.
- Start with `g.Count()`.
- Either implement direct numeric `Sum`, `Min`, `Max`, and `Average` in the same model if the Count path is stable, or keep them rejected with focused diagnostics.

### 4. SQL Rendering And Execution

- Render `GROUP BY` explicitly.
- Render grouped aggregate select lists directly rather than using row-local entity materialization.
- Read grouped result rows from `IDataLinqDataReader`.
- Convert grouped projection members into the requested anonymous result type without table-cache lookup.

### 5. Documentation

- Update public docs only after behavior tests pass.
- Update:
  - `docs/Supported LINQ Queries.md`
  - `docs/support-matrices/LINQ Translation Support Matrix.md`
  - `docs/internals/LINQ Parser Architecture.md`
  - `docs/internals/Query Translator.md`
  - the 0.8 roadmap pages

## Verification

Focused checks:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*|/*/*/QueryPlanSqlParityTests/*|/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

Completed focused check:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*|/*/*/QueryPlanSnapshotTests/GroupedAggregateSnapshot_RecordsGroupKeyAndAggregateMembers|/*/*/ExpressionQueryPlanParserTests/ExpressionParser_GroupedAggregateProjectionParsesToDataLinqPlan|/*/*/EmployeesUnsupportedQueryDiagnosticsTests/UnsupportedGroupedProjectionShapesThrowQueryTranslationException|/*/*/EmployeesUnsupportedQueryDiagnosticsTests/UnsupportedGroupByThrowsQueryTranslationException" --output failures --build
```

Result: passed `6/6` tests in both active provider batches: `sqlite-file, sqlite-memory` and `mysql-8.4, mariadb-11.8`.

## Closeout Notes

- Added first-class plan nodes for `GroupBy`, group-key projection values, and grouped aggregate projection values.
- SQL rendering now emits explicit `GROUP BY` and grouped aggregate select aliases for this shape.
- Execution reads grouped aggregate rows directly from `IDataLinqDataReader` and invokes the parsed projection constructor.
- Unsupported grouped shapes remain rejected with `QueryTranslationException`: bare `GroupBy`, computed keys, composite keys, grouped element enumeration, unsupported grouped aggregates, grouped joins, and post-group composition.
- Public docs and the support matrix now describe only the tested direct-key grouped `Count()` slice.

## Exit Criteria

- [x] `GroupBy(key).Select(g => new { g.Key, Count = g.Count() })` works from `db.Query()` and `transaction.Query()`.
- [x] The supported grouped aggregate shape passes across SQLite, MySQL, and MariaDB.
- [x] Generated SQL contains explicit `GROUP BY`.
- [x] Plan snapshots show group keys and aggregate members as DataLinq plan nodes.
- [x] Bare `GroupBy(...)`, materialized `IGrouping`, grouped element enumeration, composite keys, computed keys, grouped joins, and post-group composition remain rejected.
- [x] Public docs and the support matrix describe grouped aggregate projection as a narrow supported slice, not general `GroupBy`.
