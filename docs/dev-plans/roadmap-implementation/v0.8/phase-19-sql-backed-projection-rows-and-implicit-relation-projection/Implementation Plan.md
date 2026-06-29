> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 19 Implementation Plan

**Status:** Complete.

## Objective

Add an explicit SQL-backed projection-row path for simple source-slot projections and supported implicit singular relation member projections, without turning provider `Select(...)` into hidden lazy relation loading or broad SQL expression translation.

## Implementation Shape

Phase 19 should introduce a projection row boundary instead of stretching row-local projection evaluation:

- represent bindable projected rows as planned members with names, CLR types, and `QueryPlanValue` bindings
- render those members directly in the SQL select list with stable aliases
- materialize scalar and constructor-backed projection results from `IDataLinqDataReader`
- reuse the Phase 15 implicit singular relation source-slot path when a projection member traverses a supported singular relation
- keep row-local computed projections on the existing post-materialization path
- reject relation objects, collection relations, nested provider queries, multi-hop relation traversal, and client expressions when the selector is supposed to be SQL-backed

## Work Items

- [x] Add a SQL-backed projection-row plan shape distinct from row-local anonymous/computed projections.
- [x] Parse scalar and anonymous projections whose members bind directly to source-slot values.
- [x] Parse scalar and anonymous projections over supported implicit singular relation members through planned joins.
- [x] Render SQL-backed projection members with stable aliases.
- [x] Materialize SQL-backed scalar and anonymous projection rows from the data reader.
- [x] Preserve existing row-local computed projection behavior for documented computed selectors.
- [x] Keep relation object projection, collection relation projection, nested database projection, multi-hop implicit traversal, and client projection expressions rejected with focused diagnostics.
- [x] Add provider behavior, SQL-shape, snapshot, transaction-root, and unsupported-diagnostics tests.
- [x] Update the phase README, roadmap pages, public LINQ docs, support matrix, and internals docs for the exact supported boundary.

## Guardrails

- No lazy relation loading inside provider projection.
- No collection relation projection or hidden row multiplication.
- No broad SQL expression translation for arbitrary computed projections.
- No client-side fallback when a projection member cannot bind to SQL.
- No multi-hop implicit relation traversal or nullable left-join semantics in this slice.
- No broad relation object/eager-loading claim.

## Verification Plan

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesProjectionTranslationTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesImplicitRelationJoinTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build`
