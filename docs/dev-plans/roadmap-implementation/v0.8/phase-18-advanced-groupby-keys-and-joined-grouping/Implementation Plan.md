> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 18 Implementation Plan

**Status:** Complete.

## Objective

Extend the Phase 13B/16/17 grouped aggregate row model from one direct source key to named SQL-renderable key members and supported joined source-slot inputs, without materialized `IGrouping<TKey,TElement>` support or client fallback.

## Implementation Shape

Phase 18 needs a grouping binding model, not more ad hoc expression checks:

- represent grouping keys as named plan members when the key selector is an anonymous-object key
- keep scalar direct keys as the existing single-key case
- bind `group.Key.Member` to the planned key member by name
- render every key member in both the select list and `GROUP BY`
- let SQL-renderable computed key values reuse existing `QueryPlanFunctionValue` support, such as date parts and string functions
- bind grouped aggregate selectors either to a root source or to a joined projection, depending on the grouped input shape
- allow grouping over explicit joined projections only when key members and aggregate selectors map back to source-slot values
- allow grouping over implicit singular relation traversal only through the existing SQL-backed implicit join path

## Work Items

- [x] Extend the grouping plan model with named key members and an element-binding context.
- [x] Parse direct, composite anonymous-object, and SQL-renderable computed group keys.
- [x] Bind `group.Key.Member` in grouped projections, grouped predicates, and grouped ordering.
- [x] Support grouped aggregate selectors over joined projection members when they map to source-slot numeric values.
- [x] Allow grouping after supported explicit joined-row projections and SQL-backed implicit singular relation traversal.
- [x] Keep whole composite `group.Key` projection, grouped element enumeration, client-computed keys, collection relation grouping, and non-bindable joined projection members rejected.
- [x] Add provider behavior tests for composite keys, computed keys, enum/nullable/string keys, explicit joined grouping, and implicit relation grouping.
- [x] Add plan snapshot and SQL-shape tests proving named key members and joined source-slot grouping.
- [x] Update the phase README, roadmap pages, public LINQ docs, support matrix, and internals docs for the exact supported boundary.

## Guardrails

- No materialized `IGrouping<TKey,TElement>` rows.
- No grouped element enumeration.
- No arbitrary client methods in group keys.
- No collection relation grouping or hidden row multiplication.
- No broad joined grouping over row-local computed projection members.
- No composite key projection as a whole object unless a later materialization design explicitly supports it.

## Verification Plan

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesImplicitRelationJoinTests/*" --output failures --build`
