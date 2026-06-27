> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 17 Implementation Plan: Query Plan and Remotion Isolation

**Status:** Pulled forward for 0.8 planning after the 0.7.1 release. See [0.8 Query Parser Overview](0.8%20Query%20Parser%20Overview.md) for the current consolidation of the scattered query-parser and Remotion-isolation plans.

## Purpose

This phase owns the query-boundary work that used to live inside the oversized Phase 8B plan. The work is important, but it is not a small AOT cleanup. It is a query pipeline migration with AOT consequences.

The goal is to make DataLinq's supported query surface depend on DataLinq-owned plan nodes and diagnostics rather than on Remotion's `QueryModel` shape, then route generated/AOT execution through a supported-subset expression parser.

## Phase-Start Baseline

The current query path still depends on `Remotion.Linq`:

- `src/DataLinq.AotSmoke` and `src/DataLinq.TrimSmoke` root `Remotion.Linq` because the runtime query path depends on it
- Native AOT and trimmed publishes pass but still emit Remotion warnings
- SQL generation and diagnostics still consume Remotion body clauses and result operators directly
- the documented LINQ support matrix from Phases 6 and 7 is the parity contract

The WebAssembly browser path also has a separate warning question:

- `src/DataLinq.BlazorWasm` publishes and runs under WebAssembly AOT
- SQLitePCLRaw emits `WASM0001` warnings for `sqlite3_config` and `sqlite3_db_config`
- no-AOT browser WebAssembly remains unsupported for the SQLite/DataLinq path

## Goals

- introduce a DataLinq-owned query plan without changing parser behavior all at once
- move SQL generation and diagnostics away from Remotion clause types
- build a supported-subset expression parser over `System.Linq.Expressions`
- keep the parser scoped to the documented support matrix
- avoid silent client-side fallback for unsupported query shapes
- remove or isolate `Remotion.Linq` from the generated/AOT support path
- investigate SQLitePCLRaw WebAssembly warnings with exact call-path evidence

## Non-Goals

- broad LINQ provider replacement beyond the documented support matrix
- preserving every Remotion query shape
- client-side fallback for unsupported SQL query shapes
- no-AOT browser WebAssembly support unless it actually runs
- MySQL/MariaDB browser support
- OPFS/file-backed browser storage as part of the first query-boundary pass
- warning suppression as the final answer to Remotion or SQLitePCLRaw warnings

## Workstream A: DataLinq Query Plan Behind Remotion

Goals:

- introduce a DataLinq-owned query plan without changing parser behavior all at once
- move SQL generation and diagnostics away from Remotion clause types
- create the migration target for a supported-subset expression parser

Tasks:

1. Treat [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md) as the parity contract.
2. Add missing tests for high-risk support-matrix shapes before changing the query boundary.
3. Define immutable plan nodes for source slots, predicates, orderings, paging, projections, joins, local sequences, and result operators.
4. Represent operator-order-sensitive ordering and paging explicitly. `OrderBy(...).Take(...)` may render as one flat SQL query, but `Take(...).OrderBy(...)`, `Skip(...).OrderBy(...)`, and `OrderBy(...).Take(...).OrderBy(...)` must preserve LINQ order by pushing the already-limited/offset source into a subquery before applying the later ordering.
5. Add a `RemotionQueryPlanAdapter` that converts existing `QueryModel` output to the DataLinq plan.
6. Move `QueryExecutor` and SQL generation to consume the DataLinq plan while Remotion remains the producer.
7. Replace `SqlQuery<T>.Where(WhereClause)` and `OrderBy(OrderByClause)` as the main translation boundary.
8. Preserve fixed true/false condition behavior, local sequence semantics, nullable comparison semantics, scalar aggregate behavior, join behavior, relation `EXISTS` behavior, and EF-style ordering/paging operator order.
9. Add plan snapshot tests for representative queries.

Design stance:

- The plan should represent query intent, not SQL text.
- Source slots should be explicit so joins and relation subqueries do not rely on visitor-global assumptions.
- Captured values should be separated from query shape so plan caching and parameter rebinding remain possible later.
- Ordering and paging should preserve LINQ operator order instead of flattening to final SQL clause order. If a later `OrderBy` is applied after `Take` or `Skip`, SQL generation should use subquery pushdown, matching EF Core's relational behavior. A row-limiting operation without a preceding deterministic ordering should remain legal but documented as nondeterministic.

Exit criteria:

- Remotion still parses queries, but SQL generation consumes DataLinq plan nodes
- supported single-source queries generate equivalent SQL/results
- plan tests cover local collections, nullable predicates, projections, scalar aggregates, joins, relation predicates, and ordering/paging operator-order cases
- unsupported query diagnostics remain specific and DataLinq-owned

## Workstream B: Supported-Subset Expression Parser And AOT Boundary Switch

Goals:

- remove or isolate Remotion from the generated/AOT support path
- keep the parser scoped to the documented support matrix
- avoid silent client-side fallback for unsupported query shapes

Tasks:

1. Build a DataLinq expression parser over `System.Linq.Expressions` that emits the same DataLinq query plan.
2. Support the documented first slice:
   - `Where`
   - `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`
   - `Select`
   - `Skip`
   - `Take`
   - `Any`
   - `Count`
   - `Single`, `SingleOrDefault`
   - `First`, `FirstOrDefault`
   - `Last`, `LastOrDefault`
   - documented scalar aggregates
   - the narrow explicit `Join(...)` baseline if parity is practical in this phase
3. Rebuild local sequence handling against expression trees and plan bindings.
4. Add a projection interpreter or generated projection strategy for supported row-local projection shapes.
5. Inventory and remove reflection invocation from supported generated/AOT projection execution.
6. Add dual-run parity tests that parse with Remotion and with the DataLinq parser, then compare normalized plans, SQL templates, and results.
7. Route generated SQLite AOT, trimmed, and WASM AOT smoke projects through the DataLinq parser.
8. Remove `Remotion.Linq` roots from AOT and trim smoke projects.
9. Decide whether Remotion is deleted from the main runtime package or moved to a clearly named compatibility package.

Exit criteria:

- generated SQLite AOT smoke publishes without `Remotion.Linq` warnings
- trim smoke publishes without `Remotion.Linq` warnings
- WASM AOT browser smoke still passes
- the documented support matrix passes on the DataLinq parser for the enabled subset
- unsupported query shapes fail with `QueryTranslationException` or equivalent specific diagnostics
- main runtime package has no `Remotion.Linq` dependency, or Remotion is isolated outside the practical AOT support boundary

## Workstream C: SQLitePCLRaw WebAssembly Warning Disposition

Goals:

- understand whether `WASM0001` warnings are reachable in the supported browser path
- avoid library-level warning suppression without evidence
- keep no-AOT browser WebAssembly explicitly unsupported until it runs

Tasks:

1. Identify the managed SQLitePCLRaw methods that import `sqlite3_config` and `sqlite3_db_config`.
2. Determine whether `Microsoft.Data.Sqlite`, `SQLitePCLRaw.bundle_e_sqlite3`, or the selected provider calls those imports during:
   - provider registration
   - connection open
   - foreign-key configuration
   - schema creation
   - insert/query
   - relation loading
   - OPFS/file-backed configuration
3. Extend the WASM AOT smoke to cover provider registration, connection open, schema creation, foreign keys, insert, query, projection, and relation loading.
4. If the warning symbols are unreachable for the supported path, document the exact proof and keep any suppression local to the smoke/sample project with a comment.
5. If the symbols are reachable for realistic configuration, investigate a WebAssembly-safe provider or initialization path before claiming support.
6. Keep OPFS/file-backed browser storage as a separate experiment with separate warnings and behavior notes.
7. Keep no-AOT WebAssembly unsupported until the Mono interpreter failures are gone.

Exit criteria:

- SQLitePCLRaw warning disposition is documented with exact methods and call paths
- WASM AOT browser smoke still passes after the investigation
- any suppression is local, justified, and tied to call-path evidence
- no-AOT WebAssembly remains documented as unsupported unless it actually runs

## Recommended Order

1. Lock down query support-matrix parity gaps.
2. Introduce `DataLinqQueryPlan` with a Remotion adapter.
3. Add operator-order tests for `Take`/`Skip` before later `OrderBy` and verify the expected subquery shape.
4. Move SQL generation behind the DataLinq plan.
5. Build the supported-subset expression parser.
6. Add dual-run parser parity tests.
7. Move generated/AOT smoke projects to the DataLinq parser.
8. Remove or isolate Remotion from the generated/AOT support boundary.
9. Investigate SQLitePCLRaw WebAssembly warnings and document or eliminate them.

Removing Remotion before there is a plan boundary is a rewrite. Suppressing SQLitePCLRaw warnings before call-path analysis is pretending. Both are bad trades.

## Verification Plan

Routine verification after query work:

```powershell
dotnet run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
dotnet run --project src\DataLinq.Testing.CLI -- run --suite mysql --alias latest --output failures
```

Constrained-platform verification:

```powershell
.\scripts\dotnet-sandbox.ps1 publish src\DataLinq.AotSmoke\DataLinq.AotSmoke.csproj -f net10.0 -r win-x64 -c Release -v:minimal --self-contained true -p:PublishAot=true
.\scripts\dotnet-sandbox.ps1 publish src\DataLinq.TrimSmoke\DataLinq.TrimSmoke.csproj -f net10.0 -r win-x64 -c Release -v:minimal --self-contained true -p:PublishTrimmed=true
.\scripts\dotnet-sandbox.ps1 publish src\DataLinq.BlazorWasm\DataLinq.BlazorWasm.csproj -f net10.0 -c Release -v:minimal -p:RunAOTCompilation=true
```

Final phase verification:

- SQLite compliance quick suite
- MySQL/MariaDB provider lanes when SQL generation changed
- query plan snapshot tests
- dual-run parser parity tests
- Native AOT publish and executable run
- trimmed publish and executable run
- Blazor WebAssembly AOT publish and browser smoke
- compatibility size report after Remotion removal/isolation

Environment caveat:

Blazor WebAssembly builds are known to be unreliable inside the Codex sandbox on native Windows because the WebAssembly/MSBuild task host can fail there. Verify outside the sandbox before treating `DataLinq.BlazorWasm` build failures as product bugs.

## Risk Register

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Query plan becomes SQL-shaped by accident | Medium | Keep plan nodes backend-neutral and require SQL translation to be one consumer of the plan, not the plan itself. |
| Parser rewrite regresses supported LINQ behavior | High | Use the support matrix, plan snapshots, dual-run parity, and provider compliance tests before flipping defaults. |
| Ordering and paging are silently flattened into SQL clause order | High | Add explicit plan nodes and SQL tests for `Take(...).OrderBy(...)`, `Skip(...).OrderBy(...)`, and reordered limited subsets. Require subquery pushdown when later operators must apply outside an already-limited source. |
| Projection execution reintroduces reflection invocation debt | High | Inventory reflection invocation and keep unsupported projection shapes rejected in generated/AOT mode. |
| Remotion compatibility path becomes permanent | Medium | Define removal/isolation exit criteria and keep it outside the practical AOT support statement. |
| SQLitePCLRaw warning suppression hides a real browser failure | High | Suppress only after managed/native call-path proof and keep suppression local. |

## Exit Criteria

Phase 17 is complete when:

- Remotion still parses compatibility queries, but ordinary SQL generation consumes DataLinq plan nodes
- generated SQLite AOT and trim smokes run without `Remotion.Linq` roots or warnings
- the documented support matrix passes on the DataLinq parser for the enabled subset
- unsupported query shapes fail with DataLinq-owned diagnostics
- main runtime package has no `Remotion.Linq` dependency, or Remotion is isolated outside the practical AOT support boundary
- WASM AOT browser smoke still passes
- SQLitePCLRaw WebAssembly warning disposition is documented with call-path evidence
