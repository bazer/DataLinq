# 0.8 Phase 18 Review Findings: Advanced GroupBy Keys and Joined Grouping

**Review date:** 2026-06-28.

**Reviewed scope:** planned Phase 18 README, v0.8 roadmap index links, public grouped-query support wording, current explicit/implicit join source-slot support, and current grouped aggregate parser/SQL/test boundary in the `v0.8` branch.

**Implementation plan:** not created yet. The current phase source is [README.md](./README.md).

**Current status:** No implementation review finding. This phase is still planned, so advanced keys and joined grouping have not shipped.

## Findings

No actionable findings.

The planned scope is sequenced behind Phase 16 and Phase 17 and does not claim current support for composite keys, computed keys, grouping over joined rows, or grouping over implicit relation joins. That is consistent with the current parser, tests, public docs, and support matrix.

## Review Notes

- The README correctly requires first-class key-member structure instead of expression-text binding for `group.Key.Member`.
- The joined-grouping plan is appropriately source-slot based. Grouping over explicit or implicit joins should bind to source-slot values, not relation loading or anonymous projection reflection.
- The phase keeps collection relation grouping, arbitrary client-computed keys, broad nested subqueries, and materialized `IGrouping<TKey,TElement>` out of scope.
- Current unsupported-shape tests already reject composite grouped keys, computed grouped keys, grouped joins, and broad grouped shapes; that is the right starting boundary for this future phase.

## Verification

Focused inspection:

```powershell
rg -n "composite key|computed key|joined grouping|group.Key|ExplicitInnerJoin|ImplicitRelation|UnsupportedGroupedProjectionShapes|GroupBy key selector|IGrouping" docs src\DataLinq src\DataLinq.Tests.Compliance
rg -n "phase-18|Advanced GroupBy|Joined Grouping" docs/dev-plans -g "*.md"
```

Recommended verification when implementation begins:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*|/*/*/EmployeesJoinTranslationTests/*|/*/*/EmployeesImplicitRelationJoinTests/*|/*/*/QueryPlanSnapshotTests/*Grouped*|/*/*/EmployeesUnsupportedQueryDiagnosticsTests/*Grouped*" --output failures --build
```
