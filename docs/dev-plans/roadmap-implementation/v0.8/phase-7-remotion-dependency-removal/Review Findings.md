# 0.8 Phase 7 Review Findings: Remotion Dependency Removal

**Review date:** 2026-06-28.

**Reviewed scope:** phase 7 implementation commits `d3b69716`, `befbc9ce`, `f1f1bacf`, and `a3ecea2b`. The interleaved phase 6 review-document commit `ee62c82b` was not treated as phase 7 implementation scope.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** No blocking findings. No open phase 7 review findings remain from this pass.

## Findings

No actionable findings.

The production provider switch, runtime dependency deletion, and test-ownership cleanup are coherent with the phase 7 contract. The main runtime no longer depends on `Remotion.Linq`, production query roots now execute through the DataLinq-owned expression provider, and the earlier bare-paging parser issue is covered by structural root recognition plus executable-route tests.

## Review Notes

- `Queryable<T>` is now a DataLinq-owned `IOrderedQueryable<T>` root that routes production execution through `ExpressionQueryPlanProvider.ForExecution(...)`.
- The deleted Remotion scaffolding is no longer reachable from the main runtime package graph. Remaining Remotion text in active tests is guard or historical-classification coverage, not an active parser/runtime dependency.
- The expression execution route still keeps projection execution intentionally narrow: entity rows, scalar terminal operations, row-local projections, explicit join projections, and supported aggregate/terminal shapes. That matches the phase contract and the current LINQ support matrix direction.
- Paged `Count(...)` and `Any(...)` preserve paging semantics by reducing over the paged entity sequence. That is a correctness-first tradeoff, not a blocker for this removal phase.
- README/status alignment drift was intentionally ignored per the phase-review instruction.

## Verification

Focused verification run in the current worktree:

```powershell
rg -n "Remotion|QueryModel|QueryableBase|QueryParser|WhereClause|OrderByClause|ResultOperator" src\DataLinq src\DataLinq.Tests.Unit src\DataLinq.Tests.Compliance src\Directory.Packages.props
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesQueryBehaviorTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesProjectionTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*|/*/*/EmployeesRelationPredicateTranslationTests/*|/*/*/EmployeesAggregateTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/ExpressionLocalValueEvaluatorTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/QueryPlanNodeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/CompatibilitySizeReportTests/*" --output failures --build
```

Result:

- Runtime/package search found no `Remotion` hits in `src/DataLinq` or `src/Directory.Packages.props`; the remaining hits are intentional guard or historical-classification strings in tests.
- `EmployeesQueryBehaviorTests`: 31/31 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- `EmployeesProjectionTranslationTests`: 8/8 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- Combined join/relation/aggregate compliance filter: 12/12 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- `ExpressionQueryPlanParserTests`: 13/13 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- `QueryPlanSqlParityTests`: 22/22 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- `ExpressionLocalValueEvaluatorTests`: 2/2 passed.
- `QueryPlanNodeTests`: 8/8 passed.
- `CompatibilitySizeReportTests`: 7/7 passed.

The broader pack, package-report, trim, and solution-build gates recorded in the implementation plan were reviewed as phase closeout evidence but were not rerun during this review pass.
