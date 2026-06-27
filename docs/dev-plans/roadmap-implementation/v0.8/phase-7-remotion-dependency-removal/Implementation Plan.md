> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 7 Implementation Plan: Remotion Dependency Removal

**Status:** In progress.

**Created:** 2026-06-27.

## Purpose

Phase 7 removes `Remotion.Linq` from the main DataLinq product. Phase 6 proved the DataLinq expression parser route for the supported surface; this phase makes that route the production query boundary and deletes the temporary Remotion scaffolding.

The required outcome is not "hide the warning." The main runtime package must stop depending on Remotion, constrained compatibility reports must stop seeing Remotion, and active tests must stop relying on Remotion parser APIs as their oracle.

## Current Inventory

Main product dependency graph:

- `src/DataLinq/DataLinq.csproj` still has `<PackageReference Include="Remotion.Linq" />`.
- `src/Directory.Packages.props` still carries `<PackageVersion Include="Remotion.Linq" Version="2.2.0" />`.

Runtime source:

- `DataLinq.Linq.Queryable<T>` inherits Remotion `QueryableBase<T>` and constructs a Remotion `QueryParser`.
- `DataLinq.Linq.QueryExecutor` executes Remotion `QueryModel` instances and still contains legacy Remotion clause/projection paths.
- `DataLinq.Linq.Planning.RemotionQueryPlanAdapter` remains as the temporary adapter/oracle.
- Legacy visitors and SQL helpers still accept Remotion clause types: `QueryBuilder`, `WhereVisitor`, `OrderByVisitor`, `LocalSequenceExtractor`, `Evaluator`, and the Remotion-clause overloads on `SqlQuery<T>`.
- `ProjectionExpressionEvaluator` still understands Remotion query-source expressions for the legacy projection route, even though the expression-parser route can evaluate parameter-based projection expressions.

Active tests and scaffolding:

- `CurrentQueryTranslationInspection`, `QueryPlanSnapshotTests`, `QueryPlanAdapterUnsupportedShapeTests`, `QueryPlanSqlParityTests`, and `ExpressionQueryPlanParserTests` still use Remotion as the migration oracle.
- Architecture guard tests still mention Remotion because they prove plan/parser types do not expose it.

Documentation:

- public and internal docs still describe Remotion as current runtime behavior.

## Workstream A: Production Query Provider Switch

Goal: make `Database.Query()` execute through the DataLinq expression parser without Remotion.

Tasks:

1. Replace `Queryable<T> : QueryableBase<T>` with a DataLinq-owned `IOrderedQueryable<T>` implementation.
2. Add a DataLinq-owned `IQueryProvider` that parses expression trees with `ExpressionQueryPlanParser`.
3. Extend the expression execution route from Phase 6 so production queries support:
   - entity sequences
   - scalar terminal results
   - single/first/last terminal results
   - scalar and anonymous row-local projections
   - the current explicit join projection baseline
4. Keep unsupported-shape diagnostics DataLinq-owned.

Exit criteria:

- no production query execution path constructs Remotion `QueryParser` or consumes `QueryModel`.
- focused query behavior and translation suites pass through the new provider.

## Workstream B: Delete Remotion Runtime Scaffolding

Goal: remove Remotion imports from the main runtime source.

Tasks:

1. Delete or retire `RemotionQueryPlanAdapter`.
2. Delete the Remotion-backed `QueryExecutor` path once production no longer calls it.
3. Remove Remotion clause overloads and visitors that only supported the old parser path.
4. Remove Remotion query-source support from `ProjectionExpressionEvaluator` once projection execution uses parameter mappings.
5. Remove the `Remotion.Linq` package reference and central package version.

Exit criteria:

- `rg "Remotion" src/DataLinq src/Directory.Packages.props` has no main-runtime dependency hits.
- `src/DataLinq/DataLinq.csproj` builds without `Remotion.Linq`.

## Workstream C: Test Ownership Cleanup

Goal: keep useful coverage while deleting the Remotion oracle from the active baseline.

Tasks:

1. Replace Remotion-vs-expression parity tests with direct DataLinq parser SQL/result assertions.
2. Delete adapter-only snapshot tests or rewrite them against `ExpressionQueryPlanParser`.
3. Keep focused unsupported-shape tests, but assert DataLinq parser diagnostics directly.
4. Remove `CurrentQueryTranslationInspection` Remotion paths or reduce it to DataLinq-only inspection helpers.
5. Keep architecture tests that assert the active runtime/parser has no Remotion type exposure.

Exit criteria:

- active unit/compliance tests no longer instantiate Remotion parser APIs.
- quick unit and compliance suites pass.

## Workstream D: Package and Compatibility Gates

Goal: prove the dependency is actually gone.

Tasks:

1. Run package inspection on freshly packed runtime packages and confirm `Remotion.Linq` is absent from runtime dependency groups.
2. Rerun trimmed size report and confirm the `RemotionDependency` publish blocker disappears.
3. Rerun native AOT report where toolchain prerequisites exist; on this machine, classify missing platform linker separately from product regressions.
4. Add or update a focused guard so `Remotion.Linq` cannot quietly re-enter the main runtime package.

Exit criteria:

- `DataLinq` package dependency groups do not include `Remotion.Linq`.
- trimmed constrained report no longer fails on `RemotionDependency`.
- native AOT status is either green or blocked only by local toolchain classification.

## Workstream E: Documentation Cleanup

Goal: align public and internal docs with the shipped query boundary.

Tasks:

1. Update `docs/Platform Compatibility.md`.
2. Update `docs/internals/Query Translator.md`.
3. Update `docs/internals/Data Flow.md`.
4. Update `docs/internals/Source Generator.md`.
5. Update support matrix and roadmap notes so Remotion is historical migration context, not current product behavior.

Exit criteria:

- public docs no longer state that the active query pipeline uses Remotion.
- remaining mentions are historical or roadmap/archive context.

## Recommended Order

1. Start Phase 7 and record this implementation plan.
2. Switch production queryable/provider execution to the DataLinq expression parser while Remotion scaffolding still exists for comparison.
3. Run focused query behavior, aggregate, relation, join, projection, and parser tests.
4. Delete Remotion runtime scaffolding and package references.
5. Rewrite tests away from the Remotion oracle.
6. Run unit quick, compliance quick, package report, and constrained compatibility reports.
7. Update docs and close Phase 7.

## Verification

Focused query gates:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesQueryBehaviorTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesProjectionTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesJoinTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesRelationPredicateTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesAggregateTranslationTests/*" --output failures --build
```

Broad gates:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets trim --format summary
```

Package verification should use the repo's NuGet pack/report workflow before final closeout.

## Risks

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Projection execution regresses while SQL parity stays green | High | Add direct result tests for scalar, anonymous, computed, and join projections before deleting Remotion. |
| Query support silently contracts | High | Run existing supported-surface compliance tests before deleting the oracle. |
| Tests keep Remotion alive as an active dependency | High | Rewrite tests to assert DataLinq parser behavior directly before removing package references. |
| Native AOT remains red for local toolchain reasons | Medium | Keep `SdkOrWebAssemblyToolchain` classification separate from `RemotionDependency`. |
| Documentation overclaims AOT/browser support | Medium | Keep WebAssembly and no-AOT caveats separate from Remotion removal. |
