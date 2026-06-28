# 0.8 Phase 13 Review Findings: Query Composition and Subquery Pushdown

**Review date:** 2026-06-28.

**Reviewed scope:** Phase 13 docs, parser pushdown logic, SQL rendering, projection execution, support docs, and focused query translation tests in the `v0.8` branch through `57da59e2`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** No blocking runtime findings. One documentation/evidence finding remains open.

## Findings

### P3: Phase 13 README still describes the old post-paging rejection boundary

`README.md:11` says the current documented boundary rejects post-paging filters and orderings because flattening final SQL clause order would be wrong.

That statement is stale inside the Phase 13 folder. The implementation plan marks post-paging pushdown complete, and current tests cover SQL subquery pushdown for supported single-source shapes. The README later describes this phase as the work that should fix the boundary, but because the phase status is implemented, the opening text now reads like current behavior is still rejection.

There is also a small evidence gap against the plan checklist. `Implementation Plan.md:29` specifically called out `Take(...).OrderBy(...)` and `Skip(...).OrderBy(...)`. Existing coverage proves ordered post-paging variants such as `OrderBy(...).Take(...).Where(...)` and `OrderBy(...).Take(...).OrderByDescending(...)`, but direct no-leading-order `Take(...).OrderBy(...)` and `Skip(...).OrderBy(...)` shapes were not found in the focused tests.

The parser likely handles those shapes through the same generic `PushDownPostPagingOperations(...)` path. This is not a runtime finding from this pass. It is a README wording issue plus a test-evidence gap against the phase's own checklist.

Expected fix: update the README to say Phase 13 implemented the supported single-source post-paging pushdown boundary, and add direct tests for `Take(...).OrderBy(...)` and `Skip(...).OrderBy(...)` or explicitly remove them from the completed checklist.

## Review Notes

- `QueryPlanOperation.Pushdown` is a real plan boundary, not an accidental SQL-string trick.
- `QueryPlanSqlBuilder` renders single-source derived table pushdown and intentionally rejects nested pushdown over joins.
- Transaction-rooted post-paging composition has explicit compliance coverage.
- Unsupported composition over projections, joins, grouped sources, and nested database projection remains rejected.

## Verification

Focused delegated verification passed across active provider batches (`sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`):

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
```
