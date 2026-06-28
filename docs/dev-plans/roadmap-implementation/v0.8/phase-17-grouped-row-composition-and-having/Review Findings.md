# 0.8 Phase 17 Review Findings: Grouped Row Composition and HAVING

**Review date:** 2026-06-28.

**Reviewed scope:** planned Phase 17 README, v0.8 roadmap index links, public grouped-query support wording, and the current grouped aggregate parser/SQL/test boundary in the `v0.8` branch.

**Implementation plan:** not created yet. The current phase source is [README.md](./README.md).

**Current status:** No implementation review finding. This phase is still planned, so grouped-row composition and `HAVING` support have not shipped.

## Findings

No actionable findings.

The README correctly treats post-group ordering, paging, filtering, grouped-row scalar terminals, and `HAVING` as future work. It does not contradict the public docs, which still say filtering, ordering, paging, terminal operators after grouped projection, and `HAVING` are unsupported today.

## Review Notes

- The plan distinguishes pre-projection grouped predicates that should render as `HAVING` from post-projection grouped-row filters that may need a derived table.
- The no-client-fallback requirement is the right guardrail for this phase. Grouped rows are not entity rows, and pretending otherwise would recreate the exact hidden fallback class the 0.8 query-plan work has been removing.
- Current parser code explicitly rejects operators after grouped projections, which matches the planned status.
- Public docs and support matrix wording remain conservative enough for the current implementation.

## Verification

Focused inspection:

```powershell
rg -n "HAVING|grouped row|grouped projection|RejectGroupedOperator|RejectGroupedProjectionTerminal|GroupBy|IGrouping" docs src\DataLinq src\DataLinq.Tests.Compliance
rg -n "phase-17|Grouped Row Composition|HAVING" docs/dev-plans -g "*.md"
```

Recommended verification when implementation begins:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*|/*/*/QueryPlanSqlParityTests/*Grouped*|/*/*/QueryPlanSnapshotTests/*Grouped*|/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*Grouped*" --output failures --build
```
