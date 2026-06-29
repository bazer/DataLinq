> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 20 Implementation Plan

**Status:** In progress.

## Objective

Support the compiler-lowered expression-tree shape for ordinary C# query-syntax inner joins, binding transparent identifiers back to DataLinq source slots and SQL-backed projection rows without adding client fallback or broad join semantics.

## Implementation Shape

Phase 20 should reuse the explicit join model:

- recognize query-syntax inner joins as `Queryable.Join(...)` followed by transparent-identifier `Where`, `OrderBy`, paging, result operators, and `Select(...)`
- bind transparent-identifier members such as `<>h__TransparentIdentifier0.departmentEmployee` and `.department` back to source slots or projection members
- preserve Phase 19 SQL-backed projection rows for `select new { ... }`
- keep unsupported `group join`, left-join `DefaultIfEmpty()`, composite keys, computed keys, and opaque transparent identifiers rejected with focused diagnostics
- avoid claiming multi-join support unless the compiler-lowered shape is explicitly covered by tests

## Work Items

- [ ] Inspect the compiler-lowered expression shape for single query-syntax inner joins.
- [ ] Extend projection/member binding so transparent identifiers map back to existing source slots.
- [ ] Parse query-syntax `where`, `orderby`, paging, `Any()`, and `Count()` over supported joined rows.
- [ ] Parse query-syntax `select new { ... }` as SQL-backed projection rows when every member binds to a source-slot value.
- [ ] Keep `group join`, left-join patterns, composite keys, computed keys, and unsupported transparent identifiers rejected.
- [ ] Add provider behavior, SQL-shape, snapshot, transaction-root, parser, and unsupported-diagnostics tests.
- [ ] Update the phase README, roadmap pages, public LINQ docs, support matrix, and internals docs for the tested boundary.

## Guardrails

- No C# source parsing; expression trees only.
- No `GroupJoin(...)` support in this slice.
- No left/outer join pattern support.
- No anonymous-object composite join keys.
- No computed join keys.
- No client fallback for transparent identifiers that cannot bind to source-slot values.
- No multi-join support claim without explicit tests.

## Verification Plan

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build`
