# 0.8 Phase 13 Review Findings: Query Composition and Subquery Pushdown

**Review date:** 2026-06-28.

**Reviewed scope:** Phase 13 docs, parser pushdown logic, SQL rendering, projection execution, support docs, and focused query translation tests in the `v0.8` branch through `a978d85a`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** Resolved in the review-follow-up pass.

## Findings

### P2: `Count()` and `Any()` over paged sources still reduce client-side entity rows

The Phase 13 implementation plan says `Count()` and `Any()` should work over pushed-down paged sources without client-side row counting (`Implementation Plan.md:69`, `Implementation Plan.md:103`). That is the right contract: `Where(...).Skip(...).Take(...).Count()` should be represented as SQL scalar work over the planned page, not as a hidden materialize-and-count fallback.

The current execution path still materializes entity rows. `ExpressionPlanQueryable.cs:174` detects paged `Count`/`Any`, `ExpressionPlanQueryable.cs:178` enters `ExecutePagedSequenceReduction(...)`, and `ExpressionPlanQueryable.cs:189` reduces the materialized rows with `rows.Any()` or `rows.Count()`.

That preserves small-page result correctness, but it violates the phase's own no-client-reduction exit criterion and can become expensive for large `Take(...)` windows. The existing `ExpressionExecutionProvider_PagedAggregateUsesPushedDownSource` coverage is not enough evidence for the claim because it exercises paged `Sum(...)`, while the special client-reduction path is specifically for paged `Count` and `Any`.

Expected fix: render paged `Count()` and `Any()` as SQL scalar results over the pushed-down source, or narrow the phase closeout wording if the team deliberately accepts entity-row reduction for now. Add focused SQL-shape tests for paged `Count()` and paged `Any()` so this does not regress silently.

### P3: Phase 13 README still describes the old post-paging rejection boundary

`README.md:11` says the current documented boundary rejects post-paging filters and orderings because flattening final SQL clause order would be wrong.

That statement is stale inside the Phase 13 folder. The implementation plan marks post-paging pushdown complete, and current tests cover SQL subquery pushdown for supported single-source shapes. The README later describes this phase as the work that should fix the boundary, but because the phase status is implemented, the opening text now reads like current behavior is still rejection.

There is also a small evidence gap against the plan checklist. `Implementation Plan.md:29` specifically called out `Take(...).OrderBy(...)` and `Skip(...).OrderBy(...)`. Existing coverage proves ordered post-paging variants such as `OrderBy(...).Take(...).Where(...)` and `OrderBy(...).Take(...).OrderByDescending(...)`, but direct no-leading-order `Take(...).OrderBy(...)` and `Skip(...).OrderBy(...)` shapes were not found in the focused tests.

The parser likely handles those shapes through the same generic `PushDownPostPagingOperations(...)` path. This is not a runtime finding from this pass. It is a README wording issue plus a test-evidence gap against the phase's own checklist.

Expected fix: update the README to say Phase 13 implemented the supported single-source post-paging pushdown boundary, and add direct tests for `Take(...).OrderBy(...)` and `Skip(...).OrderBy(...)` or explicitly remove them from the completed checklist.

## Resolution Notes

Resolved in the review-follow-up pass:

- `ExpressionQueryPlanExecutor` no longer contains the materialize-and-count fallback for paged `Count()`/`Any()`.
- `QueryPlanSqlParityTests` now covers paged `Count()` and `Any()` SQL scalar pushdown, plus direct `Take(...).OrderBy(...)` and `Skip(...).OrderBy(...)` SQL-shape evidence.
- The Phase 13 README now states that the implemented boundary supports single-source post-paging pushdown instead of describing the old rejection boundary as current.

Focused verification: `QueryPlanSqlParityTests` passed 32/32 on the quick SQLite provider batch, then passed 64/64 across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.

## Review Notes

- `QueryPlanOperation.Pushdown` is a real plan boundary, not an accidental SQL-string trick.
- `QueryPlanSqlBuilder` renders single-source derived table pushdown and intentionally rejects nested pushdown over joins.
- Transaction-rooted post-paging composition has explicit compliance coverage.
- Unsupported composition over projections, joins, grouped sources, and nested database projection remains rejected.

## Verification

Focused source inspection:

```powershell
rg -n "client-side row counting|RequiresPagedSequenceReduction|ExecutePagedSequenceReduction|ExpressionExecutionProvider_PagedAggregateUsesPushedDownSource|Count\(\)|Any\(\)" docs\dev-plans\roadmap-implementation\v0.8\phase-13-query-composition-and-subquery-pushdown src\DataLinq src\DataLinq.Tests.Compliance
```

Focused delegated verification passed across active provider batches (`sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`):

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
```
