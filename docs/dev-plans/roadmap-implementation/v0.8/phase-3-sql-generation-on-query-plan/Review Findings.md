# 0.8 Phase 3 Review Findings: SQL Generation on Query Plan

**Review date:** 2026-06-27.

**Reviewed scope:** phase 3 changes from `0b808127` through `2bf4d829`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

## Findings

No blocking findings.

The phase 3 changes correctly move the production SQL-generation path onto `DataLinqQueryPlan` for the reviewed surface. The new `DataLinq.Linq.Planning.Sql` renderer does not take Remotion types, and `QueryExecutor` now routes normal collection, scalar, aggregate, relation `EXISTS`, and the narrow explicit join SQL construction through `QueryPlanSqlBuilder`.

## Review Notes

- `QueryPlanSqlBuilder`, `QueryPlanSqlPredicateBuilder`, `QueryPlanSqlValueRenderer`, and `QueryPlanSqlSourceMap` keep SQL rendering in DataLinq plan terms and reuse the existing lower-level `SqlQuery`, `WhereGroup`, `Operand`, `Select`, and provider function primitives.
- The retained legacy `ParseLegacyQueryModel`/`BuildLegacySqlQuery` path is test scaffolding only. Production collection/scalar routing now calls the plan-backed path.
- The alias changes in `Select` were checked against entity materialization, primary-key cache shortcuts, scalar selectors, and join primary-key selectors. I did not find a phase 3 regression there.
- Remaining Remotion usage in `QueryExecutor` is still real, but it is in the parser/projection compatibility boundary that later phases own, not in the new plan SQL renderer.
- README/status drift is intentionally ignored for this review per the phase closeout instructions.

## Verification

All commands were run in the current worktree with the existing phase 4 edits present, while the code review scope was limited to phase 3 commits.

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/QueryPlanNodeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesAggregateTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesRelationPredicateTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesStringMemberTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesNullablePredicateTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesContainsTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesBooleanLogicTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesProjectionTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesDateTimeMemberTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesEmptyListQueryTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesLocalAnyPredicateTests/*" --output failures --build
```

Result: all listed suites passed across their configured targets.
