# 0.8 Phase 16 Review Findings: Grouped Numeric Aggregates

**Review date:** 2026-06-29.

**Reviewed scope:** Phase 16 docs, grouped numeric aggregate parser changes, grouped aggregate SQL rendering, grouped projection execution, public LINQ docs, support matrix updates, and focused compliance tests in the `v0.8` branch through `8821ab0b`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** No blocking findings. No open Phase 16 review findings remain from this pass.

## Findings

No actionable findings.

The implementation is narrow in the right way: grouped `Sum`, `Min`, `Max`, and `Average` are represented as grouped aggregate plan values with direct numeric selectors, SQL rendering emits aggregate expressions from the plan, and unsupported computed/relation aggregate selectors remain rejected instead of falling back to client work.

## Review Notes

- `ExpressionQueryPlanParser` keeps `group.Count()` selectorless and records numeric grouped aggregate selectors as `QueryPlanGroupedAggregateValue` values.
- `QueryPlanSqlValueRenderer` renders `COUNT(*)`, `SUM`, `MIN`, `MAX`, and `AVG` from grouped aggregate values, with `COALESCE(SUM(...), 0)` preserving the current nullable-sum behavior.
- Grouped aggregate projection execution reads result aliases directly from the data reader and constructs the projection row, rather than trying to materialize grouped rows as entity/cache-backed rows.
- The public docs and support matrix keep materialized `IGrouping<TKey,TElement>`, grouped element enumeration, computed aggregate selectors, relation selectors, and client fallback out of the shipped claim.

## Verification

Focused verification passed:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
```

The grouped aggregate suite passed 72/72 across active provider batches (`sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`).
