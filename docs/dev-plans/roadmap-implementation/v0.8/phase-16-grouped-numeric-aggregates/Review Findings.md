# 0.8 Phase 16 Review Findings: Grouped Numeric Aggregates

**Review date:** 2026-06-28.

**Reviewed scope:** planned Phase 16 README, v0.8 roadmap index links, public grouped-query support wording, and the current grouped aggregate parser/SQL/test boundary in the `v0.8` branch.

**Implementation plan:** not created yet. The current phase source is [README.md](./README.md).

**Current status:** No implementation review finding. This phase is still planned, so there is no shipped Phase 16 code to accept or reject yet.

## Findings

No actionable findings.

The planned scope is consistent with the current product boundary: DataLinq supports only the Phase 13B direct-key grouped `Count()` projection today, while grouped `Sum`, `Min`, `Max`, `Average`, multiple aggregate members, and nullable aggregate semantics remain explicitly future work.

## Review Notes

- The README keeps grouped numeric aggregates SQL-shaped and rejects materialized `IGrouping<TKey,TElement>` behavior.
- The plan correctly calls out nullable aggregate semantics as test-defined work rather than something to infer from scalar aggregate behavior.
- Public docs and the support matrix still list grouped numeric aggregates as unsupported, which is the correct current claim.
- Current code has a `QueryPlanGroupedAggregateKind` shape that can grow, but SQL rendering currently supports only `COUNT(*)`; that matches the planned status.

## Verification

Focused inspection:

```powershell
rg -n "grouped Sum|grouped aggregate|GroupedAggregate|QueryPlanGroupedAggregateKind|grouped numeric|IGrouping|grouped Count|grouped Sum|Min|Max|Average" docs src\DataLinq src\DataLinq.Tests.Compliance
rg -n "phase-16|Grouped Numeric Aggregates" docs/dev-plans -g "*.md"
```

Recommended verification when implementation begins:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*|/*/*/QueryPlanSnapshotTests/*Grouped*|/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*Grouped*" --output failures --build
```
