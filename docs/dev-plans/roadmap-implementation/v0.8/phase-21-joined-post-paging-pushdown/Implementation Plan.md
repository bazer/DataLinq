> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 21 Implementation Plan

**Status:** Complete.

## Objective

Phase 21 should extend Phase 13 pushdown semantics to the joined source-slot model introduced and hardened in Phases 14, 19, and 20.

The narrow shipped behavior should be:

- joined row shapes may be paged first and then filtered or ordered by SQL-backed projection members
- `Any()` and `Count()` over paged joined row shapes preserve the paging boundary
- SQL rendering uses a derived-source boundary instead of flattening the later operation into the pre-paging query
- joined projection aliases and joined source primary-key aliases remain available after pushdown

## Work Items

- [x] Inspect the current single-source `QueryPlanOperation.Pushdown` parser and SQL renderer.
- [x] Decide whether the existing pushdown node can safely model joined pushdown or whether a joined-specific path is required.
- [x] Preserve direct joined projection aliases through the inner derived source.
- [x] Preserve joined source primary-key aliases for row-local joined materialization.
- [x] Bind post-paging `Where(...)` and ordering to derived aliases instead of original table aliases.
- [x] Support `Any()` and `Count()` over paged joined SQL-backed projection rows.
- [x] Keep row-local computed joined projections, relation projection, grouped joins, and nested pushdown rejected.
- [x] Add provider behavior, SQL-shape, snapshot, transaction-root, parser, and unsupported-diagnostics tests.
- [x] Update the phase README, roadmap pages, public LINQ docs, support matrix, and internals docs for the tested boundary.

## Guardrails

- Do not flatten a post-paging joined predicate or ordering into the original join query. That changes LINQ semantics.
- Do not fall back to client-side filtering for a provider-backed query. That gives the right-looking result for the wrong reason and breaks paging size.
- Do not claim multi-join, left-join, grouped-join, or row-local computed projection support unless this phase explicitly tests it.
- Keep the derived-source shape visible in plan snapshots and SQL-shape tests.

## Verification Plan

- Build `src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj`.
- Run focused provider tests for joined pushdown behavior.
- Run expression parser tests for the new plan shape.
- Run plan snapshot tests for joined pushdown boundaries.
- Run unsupported-shape tests for rejected joined pushdown cases.
- Run SQL parity tests for derived-source SQL rendering.
- Run `git diff --check`.

Focused verification passed:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanUnsupportedShapeTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build`
