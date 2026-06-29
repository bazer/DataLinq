# 0.8 Phase 19 Review Findings: SQL-Backed Projection Rows and Implicit Relation Projection

**Review date:** 2026-06-29.

**Reviewed scope:** Phase 19 docs, SQL-backed projection-row parser paths, implicit singular relation projection binding, projection-row SQL rendering, data-reader projection materialization, public docs, support matrix updates, and focused compliance tests in the `v0.8` branch through `8821ab0b`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** Resolved in the review-follow-up pass.

## Findings

### P2: Relation projection diagnostics changed without updating the older unsupported-diagnostics test

Phase 19 correctly rejects relation-object projection, but the active `EmployeesUnsupportedQueryDiagnosticsTests` suite is now red because the older relation-selector assertion was not updated for the newer, more specific diagnostic wording.

The failing test expects the message fragment `"Relation property 'Managers'"` (`src/DataLinq.Tests.Compliance/Translation/EmployeesUnsupportedQueryDiagnosticsTests.cs:61`). The actual exception is:

```text
Collection relation property 'Managers' is not supported as a row-local LINQ Select projection. Project a mapped relation member directly so it can bind to SQL, or materialize before loading relation data. Expression: x.Managers
```

That actual diagnostic is better than the old one; the problem is the stale exact-case assertion. Because this is an active compliance test class, a focused diagnostics run now fails on all active provider targets even though the product behavior is still a clean `QueryTranslationException`.

Expected fix: update the unsupported-diagnostics assertion to accept the current relation-object projection wording, preferably by checking `"Collection relation property 'Managers'"` and `"LINQ Select projection"` rather than the older generic `"Relation property 'Managers'"` fragment.

## Resolution Notes

Resolved in the review-follow-up pass:

- `EmployeesUnsupportedQueryDiagnosticsTests.UnsupportedRelationSelectorThrowsQueryTranslationException` now asserts `"Collection relation property 'Managers'"` and `"LINQ Select projection"`, matching the current more precise diagnostic.

Focused verification: `EmployeesUnsupportedQueryDiagnosticsTests` passed 36/36 across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.

## Review Notes

- `ExpressionQueryPlanParser` creates `QueryPlanProjection.SqlRow` only when every projection member can bind to a source-slot column.
- Supported implicit singular relation projection reuses the same implicit join source-slot machinery as relation predicates/orderings.
- `QueryPlanSqlBuilder` renders stable aliases for SQL-backed projection members, and `ExpressionQueryPlanExecutor.ExecuteSqlRowProjection(...)` materializes the constructor-backed row from `IDataLinqDataReader`.
- Row-local computed single-source projections remain post-materialization behavior; they are not mislabeled as SQL-backed projection rows.
- Accepted provider projections are SQL-backed and read from the result row. The implementation does not hide lazy relation loading inside `Select(...)`.

## Verification

Focused verification passed:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesProjectionTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesImplicitRelationJoinTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
```

Projection tests passed 28/28 and implicit relation join tests passed 12/12 across active provider batches (`sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`).

Focused verification failure recorded by this review:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*" --output failures --build
```

Result: failed 4 provider-target cases for `UnsupportedRelationSelectorThrowsQueryTranslationException` because the assertion expected `"Relation property 'Managers'"` while the current exception says `"Collection relation property 'Managers'"`.
