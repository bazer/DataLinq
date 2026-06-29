> [!WARNING]
> This folder contains roadmap execution material for DataLinq 0.8. It is not normative product documentation, and it should not be treated as a shipped support claim.

# 0.8 Phase 22: LINQ Parser Plan Cleanup

**Status:** Implemented.

Execution plan: [Implementation Plan](Implementation%20Plan.md).

## Purpose

Phase 22 is the final parser implementation cleanup before release evidence work.

The parser replacement and follow-on query-runtime slices have landed. That is good, but the new architecture review identified one small, concrete weakness that is worth fixing before 0.8 ships: the logical plan looks mostly immutable, but binding state is still mutable by convention, and SQL rendering still does allocation-heavy binding lookup.

This phase tightens that seam without expanding the supported LINQ surface.

## Scope

In scope:

- freeze query-plan bindings into an immutable plan-owned snapshot
- replace render-time binding lookup with O(1), allocation-free lookup
- stop creating fresh read-only binding wrappers on repeated access
- reduce avoidable local-sequence binding copies where the caller only needs a read-only view
- add focused unit/compliance tests for binding immutability and rendering behavior
- run focused allocation benchmarks to make sure the cleanup does not regress recent primary-key and relation traversal wins

Out of scope:

- full query-plan template caching
- structural query-shape cache keys
- parser class decomposition
- broad SQL renderer rewrite
- `ValueStringBuilder` or query object struct conversion
- new LINQ operators or projection shapes
- backend abstraction work
- public documentation that claims new query behavior

## Design Requirements

The cleanup should preserve the current logical model:

- parser construction may stay mutable
- `DataLinqQueryPlan` should own immutable snapshots
- binding ids should stay stable for plan snapshots and diagnostics
- duplicate binding ids should be rejected during freeze rather than rediscovered repeatedly while rendering
- local sequence values should remain protected from caller mutation
- query debug output should continue to redact binding values and preserve shape
- SQL parameter rendering should preserve provider conversion behavior

The important distinction is builder versus plan:

```text
parser-time:
  mutable binding builder
  append-only captured scalar/local sequence records

plan-time:
  immutable binding snapshot
  stable list/span-style enumeration
  O(1) lookup by binding id
```

This is not a feature phase. A user should see the same supported query behavior before and after this work.

## Implementation Notes

Implemented in this phase:

- `QueryPlanBindingFrame` is now parser-time builder state with a stable read-only binding view and a `Freeze()` method.
- `DataLinqQueryPlan` now owns `QueryPlanBindings`, an immutable snapshot with copied binding storage and O(1) lookup by id.
- SQL rendering now resolves captured values through the immutable binding snapshot instead of LINQ enumeration plus `Take(...).ToArray()`.
- `QueryPlanNullSemanticsResolver` now uses a lookup contract, so parser-time null semantics no longer needs to allocate a fresh binding view.
- Debug output iterates the frozen plan bindings directly and still redacts captured scalar and local sequence values.
- Focused unit tests now cover stable binding views, plan-boundary freezing, local-sequence snapshot protection, and duplicate binding rejection.

One allocation remains deliberately: local sequence SQL rendering must still allocate an `object?[]` before creating `ValueOperand`, because `ValueOperand` is currently array-backed and exposes that mutable array. Removing that copy would require a query-layer operand contract change, not just a parser binding cleanup.

## Verification

Required verification:

- focused unit tests for query-plan binding immutability and lookup behavior
- focused compliance tests for captured scalar predicates and local sequence membership
- query-plan snapshot tests for representative shapes
- SQL parity tests for local sequence membership and captured values
- focused allocation benchmark check for the recently optimized primary-key and relation traversal scenarios

Recommended commands:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/QueryPlanNodeTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*|/*/*/QueryPlanSqlParityTests/*|/*/*/EmployeesContainsTranslationTests/*" --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile heavy --history-json artifacts\benchmarks\history\v0.8-phase22-phase2-watch.json
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile heavy --history-json artifacts\benchmarks\history\v0.8-phase22-query-hotpath.json
```

Executed verification:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/QueryPlanNodeTests/*" --output failures --build
# OK suite unit (12/12 passed)

$env:DATALINQ_TEST_DB_HOST='127.0.0.1'; .\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --filter "/*/*/QueryPlanSnapshotTests/*|/*/*/QueryPlanSqlParityTests/*|/*/*/EmployeesContainsTranslationTests/*|/*/*/ExpressionQueryPlanParserTests/*" --output failures --build
# OK suite compliance batch 1 [sqlite-file, sqlite-memory] (22/22 passed)
# OK suite compliance batch 2 [mysql-8.4, mariadb-11.8] (22/22 passed)

.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile heavy --history-json artifacts\benchmarks\history\v0.8-phase22-phase2-watch.json
# History JSON: artifacts\benchmarks\history\v0.8-phase22-phase2-watch.json
# Summary JSON: artifacts\benchmarks\results\20260629-192820482-965fb7565d6c40e09529d605584934a1-summary.json

.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile heavy --history-json artifacts\benchmarks\history\v0.8-phase22-query-hotpath.json
# History JSON: artifacts\benchmarks\history\v0.8-phase22-query-hotpath.json
# Summary JSON: artifacts\benchmarks\results\20260629-193213368-01a4b8948f5749929fc866a0bc1c1f0d-summary.json
```

## Exit Criteria

Phase 22 is done when:

- `DataLinqQueryPlan` no longer exposes mutable binding-frame state by convention
- SQL binding lookup no longer performs LINQ enumeration or `Take(...).ToArray()` per lookup
- local sequence rendering avoids avoidable copies while preserving immutability
- focused parser/SQL binding tests pass
- plan snapshots remain stable except for deliberately documented binding-frame debug shape changes
- focused allocation evidence shows no regression in the recently optimized benchmark lanes
- no public query support claims change
