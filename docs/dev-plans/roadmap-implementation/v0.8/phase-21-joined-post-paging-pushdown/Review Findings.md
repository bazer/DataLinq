# 0.8 Phase 21 Review Findings: Joined Post-Paging Pushdown

**Review date:** 2026-06-29.

**Reviewed scope:** Phase 21 docs, joined post-paging pushdown parser logic, derived-source SQL rendering, joined projection alias preservation, joined scalar reductions, public docs, support matrix updates, and focused compliance tests in the `v0.8` branch through `8821ab0b`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** No blocking findings. No open Phase 21 review findings remain from this pass.

## Findings

No actionable findings.

The implementation preserves the important semantic boundary: post-paging joined predicates and orderings are rendered against a derived source instead of being flattened into the pre-paging join. Unsupported row-local joined projections remain rejected for post-paging composition.

## Review Notes

- `ExpressionQueryPlanParser.PushDownPostPagingOperations(...)` records the pre-paging joined operations inside a `Pushdown` operation before adding later filters/orderings.
- `QueryPlanSqlBuilder.PushDownJoined(...)` builds the inner joined SQL with projection aliases plus joined primary-key aliases, then switches outer predicates/orderings to the derived-source column map.
- `QueryPlanDerivedColumnMap` keeps post-paging filters/orderings bound to projection aliases, while `GetJoinedPrimaryKeySelectors(...)` preserves joined key aliases for the paths that need joined row materialization.
- Existing tests cover explicit and query-syntax joined post-paging filters/orderings, `Count()`, `Any()`, transaction roots, and SQL shape.

## Verification

Focused verification passed:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
```

The join suite passed 64/64 and SQL parity passed 60/60 across active provider batches (`sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`).
