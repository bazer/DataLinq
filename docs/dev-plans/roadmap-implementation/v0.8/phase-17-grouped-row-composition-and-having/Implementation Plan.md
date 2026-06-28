> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 17 Implementation Plan

**Status:** Complete.

## Objective

Make SQL-backed grouped aggregate rows composable for the narrow shapes that can preserve SQL semantics: grouped `Where(...)` before projection as `HAVING`, and ordering, paging, filtering, `Any()`, and `Count()` over grouped projection rows when the later operators bind to aggregate-row members.

## Implemented Shape

Phase 17 keeps grouped rows SQL-backed and deliberately narrow:

- `Where(group => ...)` immediately after `GroupBy(...)` binds `group.Key`, `group.Count()`, and direct numeric grouped `Sum`/`Min`/`Max`/`Average` comparisons to `HAVING`.
- `Where(row => ...)` after grouped `Select(...)` binds projected key and aggregate members back to the same grouped plan values and renders them through `HAVING`.
- `OrderBy`, `OrderByDescending`, `ThenBy`, and `ThenByDescending` over grouped projection members render raw group-key or aggregate SQL expressions.
- `Skip` and `Take` apply to grouped projection rows after grouped ordering/filtering.
- `Count()` and `Any()` over grouped projection rows execute through an explicit derived grouped subquery, so paged grouped reductions do not fall back to cache-backed entity materialization.

## Work Items

- [x] Add first-class grouped predicate support for `group.Key` and supported grouped aggregate comparisons.
- [x] Render grouped `Where(...)` as SQL `HAVING`, not as a row-level `WHERE`.
- [x] Bind grouped projection members for post-`Select(...)` ordering and filtering.
- [x] Support `OrderBy`, `ThenBy`, `Skip`, and `Take` over grouped aggregate projection members.
- [x] Support `Any()` and `Count()` over grouped projection rows using explicit SQL shapes.
- [x] Add SQL-shape tests for `HAVING`, grouped ordering/paging, and grouped scalar reductions.
- [x] Add behavior and transaction-root tests across active providers.
- [x] Keep grouped element enumeration, client-computed grouped values, joined grouping, composite keys, and unsupported post-group composition rejected.
- [x] Update public docs, support matrix, and roadmap pages only for the tested grouped-row composition shapes.

## Guardrails

- No materialized `IGrouping<TKey,TElement>` support.
- No grouped element enumeration.
- No computed or composite keys.
- No grouping over joins.
- No client-side sorting, filtering, or scalar fallback for grouped rows.
- No broad derived-table engine beyond grouped scalar reductions.

## Verification Plan

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*" --output failures --build`
