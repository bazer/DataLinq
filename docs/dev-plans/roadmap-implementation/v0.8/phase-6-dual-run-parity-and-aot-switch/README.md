> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 6: Dual-Run Parity and AOT Switch

**Status:** Complete.

## Execution Plan

- [Implementation Plan](Implementation%20Plan.md)

## Purpose

Phase 6 proves that the DataLinq parser is ready to own the supported path. This is where confidence is earned: normalize plans, compare SQL templates where useful, compare results, then switch generated/AOT smoke paths away from Remotion.

## Completion Notes

- `QueryPlanSqlParityTests` now compares SQL and entity/scalar results from expression-parser-produced plans against the Remotion adapter path across the active provider matrix, including terminal scalar aggregates and the current explicit join SQL baseline.
- `ExpressionQueryPlanProvider.ForExecution(...)` provides an internal executable route that enumerates entity queries and scalar terminal results through `ExpressionQueryPlanParser` and `QueryPlanSqlBuilder`.
- `PlatformSmokeRunner` executes a generated-model query through the expression parser route and still keeps the strict scalar projection stage from Phase 5.
- constrained smoke projects no longer explicitly root `DataLinq` or `Remotion.Linq`; `DataLinq.AotSmoke` and `DataLinq.TrimSmoke` root only `DataLinq.SQLite`.
- constructed row-local projection execution remains outside the temporary expression-parser execution harness. The current explicit join baseline is covered by plan and SQL parity, while the executable route is deliberately limited to entity queries and scalar terminal results until generated projector support exists.

## Closeout Evidence

Focused parser and SQL parity:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
```

Results:

- `QueryPlanSqlParityTests`: 20/20 passed per batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- `ExpressionQueryPlanParserTests`: 12/12 passed per batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.

Broad quick gates:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

Results:

- unit quick: 733/733 passed.
- compliance quick: 516/516 passed for the configured quick batch.

Constrained compatibility reports:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets trim --format summary
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --targets aot --format summary
```

Results:

- trimmed publish fails with `RemotionDependency`; the failing assembly is `Remotion.Linq.dll` from the main `DataLinq` package reference, not a constrained-smoke trimmer root. Report: `artifacts/dev/compat-size-report/20260627-225112317/report.md`.
- native AOT publish fails with `SdkOrWebAssemblyToolchain`; the local machine lacks the native AOT platform linker / Visual Studio Desktop Development for C++ workload. Report: `artifacts/dev/compat-size-report/20260627-225325293/report.md`.

## Phase 7 Handoff

Phase 7 owns the remaining dependency-removal work:

- remove `Remotion.Linq` from `src/DataLinq/DataLinq.csproj`
- remove the central `Remotion.Linq` version from `src/Directory.Packages.props` once no main product reference needs it
- replace or retire active tests and inspection helpers that instantiate Remotion parser APIs
- rerun package and constrained compatibility reports until the main runtime package and smoke publishes no longer include Remotion

## Scope

In scope:

- add dual-run parser parity tests
- compare normalized `DataLinqQueryPlan` output
- compare SQL templates for stable SQL shapes
- compare query results across SQLite and available MySQL/MariaDB lanes
- record intentional differences
- route generated SQLite Native AOT, trimmed, and WASM AOT smoke projects through the DataLinq parser
- remove Remotion roots from constrained-platform smoke projects when the path no longer needs them

Out of scope:

- deleting Remotion from the main package
- broadening the query support matrix
- treating browser no-AOT as supported

## Exit Criteria

- dual-run parity is green for enabled supported shapes
- intentional differences are documented
- constrained smoke projects no longer root Remotion directly
- compatibility reports classify remaining publish blockers as Phase 7 dependency-removal or local toolchain work
