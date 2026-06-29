> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 5: Projection and Local Evaluation AOT Cleanup

**Status:** Complete.

## Execution Plan

- [Implementation Plan](Implementation%20Plan.md)
- [Dynamic Code Inventory](Dynamic%20Code%20Inventory.md)

## Purpose

Phase 5 makes the supported generated/AOT path credible after the parser replacement. Query parsing can inspect expression metadata, but supported execution should avoid dynamic code and reflection invocation in hot projection/local-evaluation paths where practical.

## Scope

In scope:

- inventory reflection invocation in projection and local evaluation
- replace row-member reads with generated metadata/accessor paths where practical
- implement an interpreter or generated-projector strategy for supported row-local projections
- keep compatibility fallbacks separate from the generated/AOT support boundary
- make unsupported projection shapes fail clearly

Out of scope:

- arbitrary client method execution inside provider predicates
- SQL-backed projection expansion as a broad feature
- generated projectors for every expression shape

## Design Rule

Projection support should stay honest:

- SQL handles filtering, ordering, paging, scalar results, and join key selection.
- Row-local projection can run after materialization for supported shapes.
- Relation-property projection inside provider `Select(...)` remains unsupported unless explicitly designed.

## Exit Criteria

- supported generated/AOT projection execution avoids `Expression.Compile()`
- reflection invocation in supported projection/local paths is removed or explicitly isolated
- unsupported projection expressions fail with focused diagnostics
- constrained-platform smoke projects can use the new parser path without reintroducing dynamic-code debt

## Closeout Evidence

Phase 5 closed with the supported strict path separated from compatibility fallbacks:

- parser-local evaluation routes through `ExpressionLocalValueEvaluator`
- unsupported local method calls fail without invoking user code
- projection method fallback invocation is removed
- mapped model member projection reads use row data instead of reflected generated-property getters
- compatibility constructor invocation and non-model member reflection are isolated behind `ProjectionEvaluationOptions.Default`
- `ProjectionEvaluationOptions.AotStrict` rejects those compatibility fallbacks
- `PlatformSmokeRunner` now includes a strict parser/projection stage that parses a generated-model query with `ExpressionQueryPlanParserOptions.AotStrict`, renders SQL through `QueryPlanSqlBuilder`, and evaluates a scalar row projection with `ProjectionEvaluationOptions.AotStrict`

Verification run during closeout:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.PlatformCompatibility.Smoke\DataLinq.PlatformCompatibility.Smoke.csproj -v:minimal
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.AotSmoke\DataLinq.AotSmoke.csproj
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.TrimSmoke\DataLinq.TrimSmoke.csproj
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/ProjectionExpressionEvaluatorTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/EmployeesProjectionTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

Results:

- platform smoke library build passed
- AOT smoke runner passed and printed `strict-parser-projection="COMPILE GENERATED HOOKS"`
- trim smoke runner passed and printed `strict-parser-projection="COMPILE GENERATED HOOKS"`
- `ProjectionExpressionEvaluatorTests`: 4/4 passed
- `ExpressionQueryPlanParserTests`: 12/12 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`
- `EmployeesProjectionTranslationTests`: 8/8 passed per active provider batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`
- unit quick: 732/732 passed
- compliance quick: 504/504 passed for the configured quick batch

Compatibility publish probes were also attempted:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets "aot,trim" --format summary --stop-on-publish-failure
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets trim --format summary --stop-on-publish-failure
```

The native AOT publish is blocked on this machine by the missing NativeAOT platform linker / Visual Studio C++ workload. The trimmed publish is blocked by existing `Remotion.Linq` trim warnings because constrained smoke projects still root Remotion. Those are not Phase 5 projection/local-evaluation regressions; Phase 6 owns routing the full constrained smoke path through the new parser and removing the constrained smoke Remotion roots, and Phase 7 owns deleting Remotion from the main runtime dependency graph.

## Phase 6 Handoff

Phase 6 should use the strict smoke stage as the first executable proof point when switching constrained-platform smoke projects to the DataLinq parser. The remaining compatibility-only fallbacks are:

- anonymous/new-object projection construction uses `ConstructorInfo.Invoke(...)` in default mode
- non-model member reads use `FieldInfo.GetValue(...)` / `PropertyInfo.GetValue(...)` in default mode
- parser captured closure/member reads use `FieldInfo.GetValue(...)` / `PropertyInfo.GetValue(...)` in default mode

These fallbacks are mechanically distinguishable through the strict options added during Phase 5. Phase 6 should fail any constrained-platform route that depends on them.
