> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 6: Dual-Run Parity and AOT Switch

**Status:** In progress.

## Execution Plan

- [Implementation Plan](Implementation%20Plan.md)

## Purpose

Phase 6 proves that the DataLinq parser is ready to own the supported path. This is where confidence is earned: normalize plans, compare SQL templates where useful, compare results, then switch generated/AOT smoke paths away from Remotion.

## Current Progress

- `QueryPlanSqlParityTests` now compares SQL and entity/scalar results from expression-parser-produced plans against the Remotion adapter path across the active provider matrix, including terminal scalar aggregates and the current explicit join SQL baseline.
- `ExpressionQueryPlanProvider.ForExecution(...)` provides an internal executable route that enumerates entity queries and scalar terminal results through `ExpressionQueryPlanParser` and `QueryPlanSqlBuilder`.
- `PlatformSmokeRunner` executes a generated-model query through the expression parser route and still keeps the strict scalar projection stage from Phase 5.
- constrained smoke projects no longer explicitly root `DataLinq` or `Remotion.Linq`; trimmed publish still fails while the main `DataLinq` project references `Remotion.Linq`, which leaves the final dependency-removal work in Phase 7.

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
- generated SQLite Native AOT publish no longer emits Remotion warnings in the supported path
- trimmed publish no longer emits Remotion warnings in the supported path
- WASM AOT smoke still passes if it remains in the 0.8 release gate
- compatibility size/dependency reports reflect the new parser boundary
