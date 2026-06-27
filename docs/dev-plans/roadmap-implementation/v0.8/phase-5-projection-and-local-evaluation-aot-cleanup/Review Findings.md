# 0.8 Phase 5 Review Findings: Projection and Local Evaluation AOT Cleanup

**Review date:** 2026-06-28.

**Reviewed scope:** phase 5 implementation changes from `03fa1724` through `468f0f04`. The phase 4 review-document commit inside that range was not treated as phase 5 implementation scope. Later phase 6 commits and the current phase 6 working-tree edits were not reviewed.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

## Findings

### P1: Parameter-free paging expressions can be accepted as plain table roots

`ParseSequence(...)` tries `TryParseRootSource(...)` before it dispatches to the `Queryable.Skip(...)` / `Queryable.Take(...)` parser. Phase 5 changed the root-source fallback so it derives mapped query roots from the expression type instead of invoking the local queryable expression, but it still accepts any parameter-free `IQueryable<T>` expression whose element type maps to a table.

That is too broad. A bare paging query such as:

```csharp
database.Query().Employees.Take(5)
database.Query().Employees.Skip(10)
```

has no lambda parameter, has type `IQueryable<Employee>`, and maps to a known table model. The root-source check can therefore return a plain `RootTable` parsed query before `ParsePaging(...)` ever runs, so the plan loses the `Take` / `Skip` operation and the generated SQL can return the full table instead of the requested page.

This is the same unsafe root-source broadness called out during the phase 4 review, but phase 5 touched this exact path while removing dynamic queryable invocation and left the unsafe acceptance in place. It needs to be fixed before phase 6 routes real constrained-platform queries through the expression parser.

Evidence:

- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:82`
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:185`
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:1121`
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:1645`

Expected fix: make root-source recognition structural, not type-only. Accept actual root query constants / known expression-plan root constants, and let `Queryable` method-call expressions flow to their operator parsers. Parameter-free query operators such as `Skip(...)`, `Take(...)`, and any future no-lambda sequence operator must not be classified as table roots.

## Review Notes

- The parser-local `MethodInfo.Invoke(...)` fallback is gone from the reviewed parser path, and unsupported local methods now fail without being invoked.
- Projection method fallback invocation is removed; unsupported projection methods now fail with focused diagnostics instead of invoking user code through `MethodInfo.Invoke(...)`.
- Mapped model value-member projection reads now use row data through metadata, while constructor invocation and non-model member reflection remain isolated behind default compatibility options and rejected by strict options.
- The strict platform smoke addition exercises the scalar strict parser/projection path. It does not cover constructed projection results or the paging-root issue above.
- README/status alignment drift was intentionally ignored per the phase-review instruction.

## Verification

Focused verification run in the current worktree:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/ProjectionExpressionEvaluatorTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesProjectionTranslationTests/*" --output failures --build
```

Result:

- `ProjectionExpressionEvaluatorTests`: 4/4 passed.
- `ExpressionQueryPlanParserTests`: 12/12 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- `EmployeesProjectionTranslationTests`: 8/8 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.

The passing parser suite does not currently cover bare `Skip(...)` / `Take(...)` sequence queries, which is why the finding above survives the focused verification.
