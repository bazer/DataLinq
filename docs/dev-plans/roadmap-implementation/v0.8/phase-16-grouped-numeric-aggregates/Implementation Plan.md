> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# Phase 16 Implementation Plan

**Status:** Complete.

## Objective

Extend the existing SQL-backed grouped aggregate projection from direct-key `group.Count()` to direct numeric grouped `Sum`, `Min`, `Max`, and `Average`, while preserving the current non-goals around materialized `IGrouping<TKey,TElement>`, computed selectors, grouped composition, and joined grouping.

## Work Items

- [x] Extend `QueryPlanGroupedAggregateValue` so grouped aggregate members carry both aggregate kind and selector value when the aggregate reads an element column.
- [x] Parse `group.Sum(row => row.Column)`, `group.Min(...)`, `group.Max(...)`, and `group.Average(...)` in grouped aggregate projections.
- [x] Keep `group.Count()` selectorless and preserve the existing grouped count plan/debug shape.
- [x] Render grouped numeric aggregate SQL from plan values with stable projection aliases.
- [x] Reuse the existing grouped aggregate data-reader projection path, with tests documenting result CLR conversion.
- [x] Add behavior, SQL-shape, snapshot, parser, transaction-root, and unsupported-shape coverage.
- [x] Update the public LINQ docs, the support matrix, and the 0.8 roadmap phase status after the behavior is verified.

## Guardrails

- No computed grouped aggregate selectors in this phase.
- No relation-property selectors.
- No `HAVING`, post-group ordering, paging, or terminal operators.
- No composite or computed group keys.
- No grouping over joined rows.
- No client-side fallback for unsupported grouped aggregate shapes.

## Verification Plan

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj -v:minimal`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*" --output failures --build`
