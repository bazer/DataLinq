# 0.8 Phase 20 Review Findings: Query-Syntax Join Support

**Review date:** 2026-06-29.

**Reviewed scope:** Phase 20 docs, compiler-lowered query-syntax join parsing, transparent-identifier binding, SQL-backed projection-row handling, unsupported-shape diagnostics, public docs, support matrix updates, and focused compliance tests in the `v0.8` branch through `8821ab0b`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** Resolved in the review-follow-up pass.

## Findings

### P2: Unsupported query-syntax computed projections fall through to an executor mismatch

Phase 20 intentionally supports SQL-backed `select new { ... }` projections over transparent identifiers, not arbitrary row-local computed query-syntax join projections. The current parser rejects whole-source transparent projections, but it does not reject a computed projection such as:

```csharp
from departmentEmployee in db.Query().DepartmentEmployees
join department in db.Query().Departments
    on departmentEmployee.dept_no equals department.DeptNo
select new
{
    Label = departmentEmployee.dept_no + ":" + department.Name
}
```

The parser path can classify that final `Select(...)` as `QueryPlanProjection.JoinedRowLocal`: `CreateProjection(...)` returns a row-local joined projection when a new-object projection references more than one source but its members cannot be converted to SQL values (`src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:652`, `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:665`).

That works for explicit `Join(...)` result selectors because the original result selector has one parameter per source. It does not work for C# query syntax: the final selector has one transparent-identifier parameter. Execution then reaches `ExecuteJoinedProjection(...)`, which requires the selector parameter count to match the joined source count and throws a late "does not match the query plan source count" error (`src/DataLinq/Linq/Planning/Expressions/ExpressionPlanQueryable.cs:245`).

That is not the focused parser diagnostic promised by the phase, and it leaves an unsupported transparent-identifier shape accepted by planning.

Expected fix: when a projection is being built from a transparent identifier, accept only `SqlRow` projections whose members bind to source-slot values. Reject `JoinedRowLocal`/client-expression projections at parse time with a query-syntax-specific `QueryTranslationException`, and add unsupported-shape coverage for computed query-syntax join projections.

## Resolution Notes

Resolved in the review-follow-up pass:

- `ExpressionQueryPlanParser.ParseSelect(...)` now rejects row-local computed projections built from transparent identifiers with a query-syntax-specific `QueryTranslationException`.
- `QueryPlanUnsupportedShapeTests.ParserRejectsComputedQuerySyntaxJoinProjection` covers the compiler-lowered transparent-identifier shape by including a `where` clause before the computed projection.

Focused verification: `QueryPlanUnsupportedShapeTests` passed 8/8 on the quick SQLite provider batch, and `EmployeesJoinTranslationTests` passed 64/64 across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.

## Review Notes

- Supported query-syntax inner joins lower cleanly to the existing source-slot join model.
- Transparent identifiers bind correctly for supported `where`, `orderby`, paging, `Count()`, `Any()`, and SQL-backed projection rows.
- Whole-source query-syntax join projection is already rejected with a focused diagnostic.
- The open issue is limited to unsupported computed transparent-identifier projections; existing supported query-syntax join tests passed.

## Verification

Focused verification passed for the existing asserted coverage:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
```

The join suite passed 64/64 across active provider batches (`sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`). The unsupported-shape suite passed 14/14 in the original review; the review-follow-up pass added the computed transparent-identifier projection case and passed the quick SQLite unsupported-shape slice 8/8.
