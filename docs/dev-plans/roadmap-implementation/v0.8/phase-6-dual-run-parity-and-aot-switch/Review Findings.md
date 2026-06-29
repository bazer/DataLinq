# 0.8 Phase 6 Review Findings: Dual-Run Parity and AOT Switch

**Review date:** 2026-06-28.

**Reviewed scope:** phase 6 implementation changes from `694765b9` through `d473b3f5`. The interleaved phase 5 review-document commit `21e4e652` was not treated as phase 6 implementation scope. The phase 2 nullable plan guard fix at `aa507fb6` was included because it landed inside the phase 6 slice and affects query-plan parity correctness.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** Resolved in the Phase 7 production-provider switch follow-up. No open Phase 6 review findings remain from this pass.

## Resolved Findings

### P1: The executable expression-parser route can drop bare paging operators

**Status:** Resolved in the Phase 7 production-provider switch follow-up.

Phase 6 adds `ExpressionQueryPlanProvider.ForExecution(...)`, and `ExpressionPlanQueryable<T>.GetEnumerator()` now executes parsed plans through `QueryPlanSqlBuilder`. That makes the phase 5 root-source bug an execution-route correctness bug.

`ParseSequence(...)` still checks `TryParseRootSource(...)` before dispatching to `Queryable.Skip(...)` / `Queryable.Take(...)`. `TryParseRootSource(...)` accepts any parameter-free `IQueryable<T>` expression whose element type maps to a table. A bare paging expression has exactly that shape:

```csharp
var provider = ExpressionQueryPlanProvider.ForExecution(database.Provider.ReadOnlyAccess);
var employees = provider.CreateRoot<Employee>();

var firstFive = employees.Take(5).ToArray();
var firstFiveCount = employees.Take(5).Count();
var afterTenAny = employees.Skip(10).Any();
```

Those expressions contain no lambda parameter, so `TryParseRootSource(...)` can classify `Take(...)` / `Skip(...)` as a plain table root before `ParsePaging(...)` runs. The resulting plan loses the paging operation. Through the new phase 6 execution route this can return the full table for `Take(5).ToArray()`, count the full table for `Take(5).Count()`, or evaluate `Skip(10).Any()` against the unskipped table.

The new parity tests exercise paging only when another lambda-bearing operator is present, for example `Where(...).OrderBy(...).Take(5)`. Those shapes do not expose the bug because `ContainsParameter(...)` sees the lambda parameter and prevents the broad root-source fallback.

Evidence:

- `src/DataLinq/Linq/Planning/Expressions/ExpressionPlanQueryable.cs:29`
- `src/DataLinq/Linq/Planning/Expressions/ExpressionPlanQueryable.cs:87`
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:92`
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:185`
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:1121`
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:1475`

Expected fix: make root-source recognition structural instead of type-only. Accept actual root constants / known expression-plan root constants, and let `Queryable` method-call expressions flow to their operator parser even when they contain no lambda parameter. Add executable-route parity tests for `root.Take(n)`, `root.Skip(n)`, `root.Take(n).Count()`, and `root.Skip(n).Any()` before treating the phase 6 switch as green.

Resolution review:

- `TryParseRootSource(...)` no longer accepts arbitrary parameter-free `IQueryable<T>` expressions based only on element type.
- Root recognition is structural: actual query root constants, captured query values that are still root queryables, and generated `Database.Query().DbRead<T>` table properties are accepted.
- Captured or nested `IQueryable` values are rejected before local sequence extraction can enumerate them.
- Executable-route coverage now includes bare `Take(...)`, bare `Skip(...)`, `Take(...).Count()`, and `Skip(...).Any()`.

## Review Notes

- The expression execution provider is deliberately narrow: entity sequences and scalar terminal results only. That matches the phase 6 README and I did not treat unsupported constructed projections as a finding.
- The constrained smoke projects no longer explicitly root `DataLinq` or `Remotion.Linq`; the remaining Remotion trim classification is correctly reported as a main package dependency issue for phase 7.
- The nullable plan guard fix is directionally correct. Missing captured bindings now fail loudly instead of being treated as non-null comparison operands.
- README/status alignment drift was intentionally ignored per the phase-review instruction.

## Verification

Focused verification run in the current worktree:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/CompatibilitySizeReportTests/*|/*/*/QueryPlanNodeTests/*" --output failures --build
```

Original review result:

- `QueryPlanSqlParityTests`: 20/20 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- `ExpressionQueryPlanParserTests`: 12/12 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- `CompatibilitySizeReportTests` / `QueryPlanNodeTests` focused unit filter: 7/7 passed.

The passing suites do not currently cover the bare paging execution-route cases listed in the finding.

Focused checks run during the resolution:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
```

Resolution result:

- `ExpressionQueryPlanParserTests`: 13/13 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- `QueryPlanSqlParityTests`: 22/22 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
