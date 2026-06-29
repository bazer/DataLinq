# 0.8 Phase 17 Review Findings: Grouped Row Composition and HAVING

**Review date:** 2026-06-29.

**Reviewed scope:** Phase 17 docs, grouped `HAVING` parser paths, grouped projection-member binding, grouped ordering/paging/scalar reduction SQL rendering, grouped projection execution, public docs, support matrix updates, and focused compliance tests in the `v0.8` branch through `8821ab0b`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** No blocking findings. No open Phase 17 review findings remain from this pass.

## Findings

No actionable findings.

The phase delivers the intended grouped-row composition slice without pretending grouped rows are entity rows. Predicates over grouped aggregate projection members bind back to grouped key or aggregate expressions and render as `HAVING`; grouped `Count()` and `Any()` reductions use an explicit derived grouped subquery.

## Review Notes

- `ExpressionQueryPlanParser` converts `Where(group => ...)` immediately after `GroupBy(...)` into `QueryPlanOperation.Having`, and binds post-projection grouped row members through the grouped projection member table.
- `QueryPlanSqlBuilder.BuildGroupedAggregateScalarSelect(...)` wraps grouped projection rows in a derived source for grouped `Count()`/`Any()` reductions.
- Ordering and paging over grouped aggregate projection rows stay server-side and reject post-paging grouped composition instead of silently changing LINQ operator order.
- The docs correctly keep arbitrary grouped element predicates, grouped element enumeration, materialized `IGrouping<TKey,TElement>`, and client-side fallback out of scope.

## Verification

Focused verification passed:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
```

The grouped aggregate suite passed 72/72 across active provider batches (`sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`).
