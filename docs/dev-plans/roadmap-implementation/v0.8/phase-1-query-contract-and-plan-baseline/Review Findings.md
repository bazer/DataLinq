# 0.8 Phase 1 Review Findings: Query Contract and Plan Baseline

**Review date:** 2026-06-28.

**Reviewed scope:** phase 1 closeout docs and current query-contract consumers in the `v0.8` branch through `a978d85a`. Later phase implementation was reviewed only where it affects whether Phase 1 baseline wording is still accurate.

**Implementation plan:** [Implementation Plan.md](./Implementation%20Plan.md).

**Current status:** Resolved in the review-follow-up pass. No runtime query-regression finding was identified in this pass.

## Findings

### P2: Public unsupported-operator docs name the wrong exception type

`docs/Supported LINQ Queries.md:386` says the test suite explicitly expects `NotSupportedException` for `TakeLast(...)`, `SkipLast(...)`, `TakeWhile(...)`, and `SkipWhile(...)`. `docs/support-matrices/LINQ Translation Support Matrix.md:92` repeats the same `NotSupportedException` contract.

That is no longer true. `src/DataLinq.Tests.Compliance/Query/EmployeesQueryBehaviorTests.cs:642` asserts `QueryTranslationException` for all four operators, and the expression parser's unsupported `Queryable` operator path throws `QueryTranslationException` from `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs:124`.

This matters because unsupported-shape diagnostics are part of the parser migration contract. The implementation is reasonable: DataLinq-owned query translation failures should be `QueryTranslationException`. The docs are the stale part.

Expected fix: update the public LINQ docs and support matrix to say these operators fail with `QueryTranslationException`, not `NotSupportedException`.

### P3: The Phase 1 audit still calls superseded behavior "current"

`Query Contract Audit.md:88` introduces "Known current behavior decisions" and then says post-paging operators are outside the support contract and `GroupBy(...)` remains unsupported.

That was true at Phase 1 closeout. It is not true for the current branch:

- Phase 13 implements single-source post-paging pushdown.
- Phase 13B implements the narrow direct-key `group.Key` plus `group.Count()` grouped aggregate projection slice.

The audit is roadmap execution history, so this is not a product-doc bug. But the wording now reads as current state unless the reader already knows the later phases. That is exactly the kind of historical-plan ambiguity that causes bad follow-up work.

Expected fix: reword that section to "Known behavior decisions at Phase 1 closeout" and add a short note that Phase 13 and Phase 13B supersede the post-paging and narrow grouped aggregate entries.

## Resolution Notes

Resolved in the review-follow-up pass:

- `docs/Supported LINQ Queries.md` and `docs/support-matrices/LINQ Translation Support Matrix.md` now document `QueryTranslationException` for `TakeLast(...)`, `SkipLast(...)`, `TakeWhile(...)`, and `SkipWhile(...)`.
- `Query Contract Audit.md` now labels the stale behavior list as Phase 1 closeout history and explicitly notes that Phase 13 and Phase 13B supersede the post-paging and narrow grouped aggregate entries.

## Review Notes

- Phase 1 did the right kind of baseline work: support inventory, SQL-shape inspection, unsupported-shape coverage, and Remotion dependency inventory before plan/parser replacement.
- Later parser and SQL-renderer phases consumed the Phase 1 baseline rather than bypassing it.
- The current production parser still preserves the important Phase 1 contracts around local membership, nullable comparison semantics, relation `EXISTS`, scalar aggregates, projections, and explicit unsupported diagnostics.

## Verification

Focused inspection and delegated review checked:

```powershell
rg -n "NotSupportedException|QueryTranslationException|TakeLast|SkipLast|TakeWhile|SkipWhile" docs src\DataLinq src\DataLinq.Tests.Compliance
rg -n "post-paging|GroupBy|grouped aggregate|Phase 13" docs\dev-plans\roadmap-implementation\v0.8 docs\Supported* docs\support-matrices
```

Recommended focused regression checks before fixing the docs:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesQueryBehaviorTests/Query_UnsupportedTailAndWhileOperators_ThrowQueryTranslationException" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/ExpressionPlanSql_RendersPostPagingPushdownWithSeparateParameters" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesGroupedAggregateTranslationTests/*" --output failures --build
```
