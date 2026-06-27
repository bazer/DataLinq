# 0.8 Phase 4 Review Findings: Supported-Subset Expression Parser

**Review date:** 2026-06-27.

**Reviewed scope:** phase 4 changes from `a586605d` through `48a4d846`, with current follow-up state checked at `03fa1724`.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

## Findings

### P2: Filtered join inner sources are accepted as plain table sources

`ParseJoin(...)` sends the inner sequence to `TryParseRootSource(...)`, but `TryParseRootSource(...)` currently accepts any parameter-free `IQueryable<T>` expression whose element type maps to a table. That means a shape such as:

```csharp
departmentEmployees.Join(
    departments.Where(department => department.Name == "Sales"),
    departmentEmployee => departmentEmployee.dept_no,
    department => department.DeptNo,
    (departmentEmployee, department) => ...)
```

is treated as a direct `departments` source. The inner `Where(...)` predicate is not represented in the plan and is not rejected. That breaks the phase 4 contract for the narrow join baseline and diverges from the Remotion adapter, which rejects non-direct join inner sequences.

Evidence:

- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:199`
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:1095`

Expected fix: only accept true root query constants or `ExpressionPlanQueryable<T>` roots as root sources. Queryable method calls and captured/query-shaped `IQueryable<T>` sources should be rejected unless the parser explicitly parses their operations.

### P2: Captured `IQueryable` values can be enumerated as local sequences during parsing

`TryEvaluateLocalSequence(...)` evaluates parameter-free local sequence expressions, then `TryConvertToArray(...)` treats any `IEnumerable` as a local value list. Since `IQueryable<T>` is also `IEnumerable<T>`, a captured database query can be enumerated while merely constructing a plan:

```csharp
var ids = database.Query().Employees.Select(employee => employee.emp_no!.Value);
var query = departments.Where(department => ids.Contains(...));
```

That is the wrong failure mode for phase 4. Nested database subqueries are out of scope, and the expression parser should reject them, not run them during local membership extraction.

Evidence:

- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:1277`
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:1304`
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:1345`

Expected fix: reject `IQueryable` values before local-sequence enumeration, or make local-sequence extraction require known in-memory collection shapes rather than any `IEnumerable`.

## Resolved Follow-Up Observed

The phase 4 snapshot included a broad local `MethodInfo.Invoke(...)` fallback for local expression evaluation. Current phase 5 work has already replaced that path with `ExpressionLocalValueEvaluator`, and `ExpressionParser_LocalMethodEvaluationFailsWithoutInvokingMethod` covers the no-invocation behavior. I did not list that as an open finding.

## Verification

Focused verification run in the current worktree:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/QueryPlanNodeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
```

Result: all listed suites passed across their configured targets after rerunning the compliance builds sequentially. Parallel compliance builds initially hit a transient `VBCSCompiler` file lock on `DataLinq.Generators.dll`; reruns passed.
