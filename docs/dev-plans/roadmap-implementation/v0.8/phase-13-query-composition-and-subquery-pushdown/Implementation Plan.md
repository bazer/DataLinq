> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 13 Implementation Plan: Query Composition and Subquery Pushdown

**Status:** In progress.

## Goal

Preserve C# LINQ operator order for the supported single-source query surface when flat SQL clause order would be wrong.

The key correctness problem is post-paging composition. A query such as:

```csharp
db.Query().Employees
    .OrderBy(employee => employee.LastName)
    .Take(10)
    .OrderBy(employee => employee.HireDate)
```

does not mean "replace the original order by." It means "order ten already-limited rows." If DataLinq cannot preserve that meaning, it must keep rejecting the shape.

Phase 13 should therefore add an explicit nested-source boundary to the query plan and SQL renderer, then remove only the unsupported-shape guards that the new boundary makes correct.

## Workstreams

### 1. Contract Tests First

- Add parser snapshot tests for operator-order-sensitive shapes:
  - `Take(...).OrderBy(...)`
  - `Skip(...).OrderBy(...)`
  - `OrderBy(...).Take(...).OrderBy(...)`
  - `OrderBy(...).Take(...).Where(...)`
  - `Where(...).Skip(...).Take(...).Count()`
  - `Where(...).Skip(...).Take(...).Any()`
- Add SQL-shape inspection tests that prove when a flat query is still correct and when a nested `FROM (...)` source is required.
- Add behavior tests over the employees fixture for row results, not only SQL text.
- Add transaction-root parity tests for every newly supported shape.

### 2. Plan Model

- Add a first-class query-plan operation for subquery pushdown.
- Preserve source-slot identity across nested boundaries. The outer source can alias a nested SQL source, but it must still map back to the root table metadata for materialization.
- Keep captured scalar and local sequence bindings in the same `QueryPlanBindingFrame`.
- Make debug output show the pushdown boundary and the operations that live inside it.

### 3. Parser

- Remove the blanket post-paging rejection for `Where(...)` and ordering only after the parser can insert an explicit pushdown boundary.
- Keep rejecting shapes that still need broader query semantics:
  - post-paging `Select(...)` when projection binding would become ambiguous
  - joins over pushed-down sources until Phase 14 handles joined row shapes
  - `GroupBy(...)` until Phase 13B
  - nested database subqueries in user projections
- Treat scalar result operators over pushed-down sources as SQL-backed when possible.

### 4. SQL Rendering

- Teach `QueryPlanSqlBuilder` to render a nested single-source query in the `FROM` clause.
- Ensure aliases are stable and provider quoting remains provider-owned.
- Render predicates, ordering, paging, and scalar result operators against the correct source boundary.
- Avoid using row-local projection machinery as a fake substitute for SQL pushdown.

### 5. Execution

- Keep entity execution cache-aware.
- Keep row-local projections running after SQL filtering/order/paging.
- Ensure transaction-rooted queries execute through the transaction `DataSourceAccess`, including materialization and cache interaction.
- Preserve existing `Count` and `Any` semantics over paged sources without client-side row counting where SQL can express the result.

### 6. Documentation

- Update the public docs only after tests pass for the shipped shapes.
- Update:
  - `docs/Supported LINQ Queries.md`
  - `docs/support-matrices/LINQ Translation Support Matrix.md`
  - `docs/internals/LINQ Parser Architecture.md`
  - `docs/internals/Query Translator.md`
  - the 0.8 roadmap pages

## Verification

Focused checks:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/Translation/*QueryPlan*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/Translation/*Employees*Query*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

Server-backed provider checks should run when SQL shape changes are stable:

```powershell
$env:DATALINQ_TEST_DB_HOST = '127.0.0.1'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --targets mysql-8.4,mariadb-11.8 --filter "/*/*/Translation/*" --output failures --build
Remove-Item Env:DATALINQ_TEST_DB_HOST
```

## Exit Criteria

- Post-paging filters and orderings translate only when SQL subquery pushdown preserves the C# operator sequence.
- SQL-shape tests distinguish flat and pushed-down forms.
- Supported scalar result operators work over pushed-down sources.
- Newly supported shapes work from both `db.Query()` and `transaction.Query()`.
- Unsupported composition still fails with focused `QueryTranslationException` diagnostics.
- Public docs and the support matrix describe only the tested shipped shapes.
