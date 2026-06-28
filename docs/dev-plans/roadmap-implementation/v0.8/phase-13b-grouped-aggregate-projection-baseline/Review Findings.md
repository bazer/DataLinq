# 0.8 Phase 13B Review Findings: Grouped Aggregate Projection Baseline

**Review date:** 2026-06-28.

**Reviewed scope:** Phase 13B docs, grouped parser and SQL-rendering paths, grouped aggregate projection execution, support docs, and focused grouped aggregate tests in the `v0.8` branch through `57da59e2`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** No blocking findings. No open Phase 13B review findings remain from this pass.

## Findings

No actionable findings.

The implementation, docs, and tests line up with the narrow direct-key grouped `Count()` slice. The phase does not pretend to support materialized `IGrouping<TKey,TElement>`, grouped joins, computed or composite keys, grouped numeric aggregates, `HAVING`, or post-group composition.

## Review Notes

- `ExpressionQueryPlanParser` accepts only immediate grouped aggregate `Select(...)` projections with `group.Key` and `group.Count()`.
- `QueryPlanSqlBuilder` renders explicit `GROUP BY` and `COUNT(*)` selectors instead of smuggling grouping through raw projection text.
- Grouped aggregate projection execution reads grouped result rows directly and constructs the projection row, rather than trying to materialize grouped rows as entity/cache-backed rows.
- Transaction-rooted grouped count projection has focused provider-matrix coverage.

## Verification

Focused delegated verification passed across active provider batches (`sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`):

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
```
