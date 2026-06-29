> [!WARNING]
> This document is roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.
# 0.8 Phase 6 Implementation Plan: Dual-Run Parity and AOT Switch

**Status:** Complete.

**Created:** 2026-06-27.

**Completed:** 2026-06-27.

## Purpose

Phase 6 proves that the DataLinq expression parser can take over the supported query path. Phase 4 proved plan-shape parity and Phase 5 made local/projection fallback usage explicit. Phase 6 has to move from "the plans look equivalent" to "the SQL and results stay equivalent when the new parser is the producer."

The phase should be boring by design. If a shape is already in the supported query contract, dual-run parity should either pass or expose a concrete bug. If a shape is intentionally unsupported, both paths should keep focused DataLinq diagnostics.

## Inputs

Phase 6 consumes:

- [Phase 5 README](../phase-5-projection-and-local-evaluation-aot-cleanup/README.md)
- [Phase 5 Dynamic Code Inventory](../phase-5-projection-and-local-evaluation-aot-cleanup/Dynamic%20Code%20Inventory.md)
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs`
- `src/DataLinq/Linq/Planning/Sql/QueryPlanSqlBuilder.cs`
- `src/DataLinq/Linq/QueryExecutor.cs`
- `src/DataLinq.PlatformCompatibility.Smoke/PlatformSmokeRunner.cs`
- `src/DataLinq.AotSmoke/`
- `src/DataLinq.TrimSmoke/`

## Scope

In scope:

- compare Remotion-adapter and expression-parser plans for supported shapes
- compare SQL text and parameter payloads produced from both plan producers
- compare execution results for representative supported shapes across active providers
- record intentional differences and unsupported shapes explicitly
- add a runtime switch or executable harness for the DataLinq parser path
- route generated AOT/trim smoke verification through the DataLinq parser once parity supports it
- remove constrained-smoke Remotion roots when the smoke path no longer depends on them

Out of scope:

- deleting Remotion from the main DataLinq package
- removing Remotion-backed tests before the switch has evidence
- broadening the LINQ support matrix
- general query optimization work
- browser no-AOT support claims

## Workstreams

### A. SQL Parity

Goal: prove that the new parser feeds the same SQL renderer with equivalent query plans.

Tasks:

1. Add helpers that build SQL from `ExpressionQueryPlanParser` output.
2. Compare SQL text after normal whitespace normalization.
3. Compare parameter payloads and counts.
4. Cover predicates, ordering, paging, local sequence membership, relation existence, aggregate selectors, and explicit joins where supported.

Exit criteria:

- focused SQL parity tests pass for representative supported sequence shapes
- failures report the exact SQL and parameter mismatch

### B. Result Parity

Goal: prove that expression-parser-produced SQL returns the same materialized rows or scalar values as the current production route.

Tasks:

1. Execute expression-parser-produced `Select<T>` queries directly for entity-returning shapes.
2. Compare result sets against current production queries.
3. Include relation `Any(...)`, local membership, nullable predicates, ordering/paging, scalar aggregates, and the current explicit join baseline.
4. Run across SQLite plus available MySQL/MariaDB providers for SQL-sensitive shapes.

Exit criteria:

- representative result parity passes across active providers
- intentional differences are documented before any route switch

### C. Parser Route Switch Harness

Goal: make the DataLinq parser path executable without deleting Remotion first.

Tasks:

1. Add an internal option or harness that routes supported queries through `ExpressionQueryPlanParser`.
2. Keep the default public provider behavior unchanged until parity gates are green.
3. Make unsupported-shape diagnostics remain DataLinq-owned.
4. Ensure strict projection/local fallback options can be used by constrained smoke verification.

Exit criteria:

- tests can execute supported queries through the DataLinq parser path without using Remotion's `QueryParser`
- fallback usage is visible when constrained verification enables strict mode

### D. Constrained Smoke Switch

Goal: start proving constrained-platform behavior against the new parser path.

Tasks:

1. Extend `PlatformSmokeRunner` from strict scalar parser/projection proof to actual query execution through the DataLinq parser route.
2. Remove `Remotion.Linq` trimmer roots from constrained smoke projects only after the smoke path no longer needs them.
3. Run native AOT and trimmed publish checks where the local machine has prerequisites.
4. Keep WebAssembly evidence separate because sandbox WebAssembly builds are unreliable in this repo.

Exit criteria:

- trimmed publish no longer fails because the smoke project roots Remotion
- native AOT publish evidence is captured on a machine with the required platform linker
- smoke output proves the DataLinq parser route executed

### E. Closeout

Goal: hand Phase 7 a parser route that has credible evidence.

Tasks:

1. Run focused SQL/result parity suites.
2. Run unit quick and compliance quick.
3. Run available constrained compatibility reports.
4. Update Phase 6 README with closeout evidence and known blockers.

Exit criteria:

- dual-run parity is green for enabled supported shapes
- intentional differences are documented
- generated SQLite AOT/trim smoke no longer depends on Remotion roots in the supported path
- Phase 7 receives a concrete dependency-removal checklist

## Recommended Implementation Order

1. Add this implementation plan and mark Phase 6 in progress.
2. Add expression-parser SQL parity helpers and focused sequence-shape coverage.
3. Add expression-parser result parity coverage across active providers.
4. Add scalar/aggregate result parity.
5. Add a parser route harness behind an internal option.
6. Switch constrained smoke execution to the DataLinq parser route.
7. Remove constrained smoke Remotion roots and run available compatibility reports.
8. Close Phase 6 with evidence and handoff to Phase 7.

## Verification

Focused verification:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSqlParityTests/*" --output failures --build
```

Broad verification:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

Constrained compatibility verification should use `DataLinq.Dev.CLI size-report` for `aot` and `trim` targets where local machine prerequisites exist. On this machine, native AOT publish currently lacks the platform linker / Visual Studio C++ workload, so native publish failure by itself is not product evidence.

## Risk Register

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Plan snapshots match but SQL differs | High | Add SQL parity before switching execution routes. |
| SQL matches but result materialization differs | High | Add result parity across active providers. |
| The switch silently falls back to Remotion | High | Add route evidence and remove Remotion roots only after the new path runs. |
| Strict projection verification blocks supported anonymous projection | Medium | Keep constructed result projection compatibility-only until generated projector support exists. |
| Native AOT publish fails for local toolchain reasons | Medium | Separate toolchain prerequisites from repo regressions in closeout evidence. |
| Phase 6 becomes Phase 7 prematurely | Medium | Do not remove the main Remotion dependency until the parser route has parity evidence. |

## Phase 7 Handoff

Phase 7 should receive:

- green dual-run SQL and result parity for supported shapes
- constrained smoke projects routed through the DataLinq parser path
- Remotion roots removed from constrained smoke projects
- a short list of remaining Remotion references that are dependency-removal work, not parser parity work

## Closeout Result

Phase 6 closed with the expression parser route executable for entity queries and scalar terminal results, and with the constrained smoke runner using that route for generated-model query execution.

Verification evidence:

- `QueryPlanSqlParityTests`: 20/20 passed per batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- `ExpressionQueryPlanParserTests`: 12/12 passed per batch across `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
- unit quick: 733/733 passed.
- compliance quick: 516/516 passed for the configured quick batch.
- trimmed size report: classified as `RemotionDependency` because `src/DataLinq/DataLinq.csproj` still references `Remotion.Linq`.
- native AOT size report: classified as `SdkOrWebAssemblyToolchain` because the local machine lacks the platform linker / Visual Studio C++ workload.

The remaining Remotion work is Phase 7 dependency removal, not Phase 6 parser parity. The constrained smoke projects root only `DataLinq.SQLite`; the Remotion trim failure now comes from the main product dependency graph.
