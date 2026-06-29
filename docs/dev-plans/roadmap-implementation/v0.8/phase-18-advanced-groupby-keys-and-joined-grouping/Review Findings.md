# 0.8 Phase 18 Review Findings: Advanced GroupBy Keys and Joined Grouping

**Review date:** 2026-06-29.

**Reviewed scope:** Phase 18 docs, advanced group-key parser paths, joined grouping binding, SQL function rendering for grouped keys, grouped aggregate SQL rendering, public docs, support matrix updates, and focused compliance tests in the `v0.8` branch through `8821ab0b`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** Resolved in the review-follow-up pass.

## Findings

### P2: Joined computed group keys can render unqualified column references

Phase 18 combines two features: SQL-renderable computed group keys and grouping over supported joined row shapes. The plan layer can represent that combination, but SQL rendering drops source aliases when a provider function renders a column argument.

`QueryPlanSqlValueRenderer.RenderProviderFunctionArgument(...)` renders a plain `QueryPlanColumnValue` as `column.Column.DbName` unless a derived-column map is active (`src/DataLinq/Linq/Planning/Sql/QueryPlanSqlValueRenderer.cs:212`). The SQLite and MySQL providers then quote the bare name when it has no dot or escape character (`src/DataLinq.SQLite/SQLiteProvider.cs:182`, `src/DataLinq.MySql/Shared/SqlProvider.cs:123`).

That is acceptable for single-table computed keys such as `row.from_date.Year`. It is not safe for joined computed keys such as grouping by `row.dept_no.ToUpper()` after joining `dept_emp` and `departments`, because both sides expose `dept_no`. The rendered SQL function argument becomes an unqualified escaped column name rather than `t0."dept_no"` or `t1."dept_no"`, so the SQL can become ambiguous or bind to the wrong source.

The existing Phase 18 tests cover single-source computed keys and direct joined grouping keys, but not a SQL function over a joined source-slot column. That is exactly the missing coverage.

Expected fix: render provider function arguments through `RenderColumnSql(...)` for `QueryPlanColumnValue` so source aliases are preserved in joined and implicit-join queries, then add provider-matrix SQL-shape and behavior coverage for computed joined group keys over a column name that exists on both joined tables.

## Resolution Notes

Resolved in the review-follow-up pass:

- `QueryPlanSqlValueRenderer.RenderProviderFunctionArgument(...)` now renders `QueryPlanColumnValue` through `RenderColumnSql(...)`, preserving source aliases inside provider function calls.
- `EmployeesGroupedAggregateTranslationTests.GroupedComputedKeyOverExplicitJoinQualifiesAmbiguousColumn` covers behavior and SQL shape for a computed joined group key over `dept_no`, which exists on both joined tables.

Focused verification: `EmployeesGroupedAggregateTranslationTests` passed 76/76 across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.

## Review Notes

- The group-key plan model is solid: named composite keys and computed key values are first-class plan values, and `group.Key.Member` binds by member name.
- Direct joined grouping over projected source-slot values is covered and passed across active providers.
- The open issue is specifically SQL rendering of function arguments, not parser binding or projection-row materialization.
- Public docs correctly avoid claiming materialized `IGrouping<TKey,TElement>` support.

## Verification

Focused verification passed for the existing asserted coverage:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesImplicitRelationJoinTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
```

Those tests do not cover computed group keys over joined source-slot columns with ambiguous column names; that gap is part of the finding.
