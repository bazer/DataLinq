> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 9B Implementation Plan: Row Freshness, External Invalidation, and Adaptive Cache Policy

**Status:** Planned follow-up release after Phase 9A.

## Purpose

Phase 9B introduces broader cache semantics after Phase 9A has made the existing cache behavior measured and defensible.

The priority is high, but the order matters. Row freshness, external invalidation, and adaptive heuristics all change how users reason about cached data. They should be built on Phase 9A's warning baseline, benchmark history, allocation reductions, invalidation tests, and telemetry. Otherwise the product risks growing a sophisticated cache whose correctness story is weaker than the performance story.

## Phase-Start Preconditions

Phase 9B should not start seriously until Phase 9A has delivered:

- warning cleanup or a documented warning baseline
- benchmark history that can show profile, last-run date, trends, and telemetry deltas
- allocation baselines after metadata/key/cache cleanup
- cache invalidation characterization tests
- hardened transaction commit/rollback invalidation behavior
- telemetry that can identify cache invalidation and cleanup work

If any of those are missing, Phase 9B should start by filling that gap rather than adding new semantics.

## Goals

- define and implement explicit row freshness primitives
- support hash-based or version-based freshness checks where they are valuable
- expose external invalidation APIs that host applications can call directly
- keep external invalidation provider-neutral and message-bus-agnostic
- make cleanup react to memory pressure without surprising user-visible behavior
- add adaptive cache policy only where telemetry and benchmarks justify it
- evaluate scoped value/key deduplication with retention and contention evidence
- make cache telemetry identify invalidation source, scope, and cost

## Non-Goals

- dependency-tracked result-set caching
- automatic Kafka, Debezium, or database CDC integration
- transparent multi-process cache coherence
- startup database profiling by default
- automatic broad table scans for heuristics
- replacing the query parser or introducing a DataLinq query plan
- async provider pipeline work
- full migration execution

## Design Principles

1. **Freshness must be explicit.** If DataLinq cannot prove a row is fresh, the API should not imply it can.
2. **External invalidation should be boring.** The first API should accept table/key invalidation signals. Kafka, CDC, and service-bus adapters can come later.
3. **Adaptive policy must be overrideable.** A user who knows their workload should be able to turn the cleverness off.
4. **Memory wins must beat contention.** Global caches and interners are not automatically improvements. They can retain objects and add synchronization cost.
5. **Telemetry is part of the feature.** A cache that cannot explain why it invalidated data will be miserable to debug.

## Workstream A: Row Freshness Contract

Goals:

- decide what DataLinq means by row freshness
- define the API surface before choosing hash SQL
- preserve provider differences honestly

Tasks:

1. Define freshness states:
   - unknown
   - assumed fresh within current cache policy
   - externally invalidated
   - freshness checked
   - stale
2. Decide where freshness metadata lives:
   - `RowData`
   - row-cache entry wrapper
   - separate freshness registry
3. Define whether freshness is opt-in per database, table, or operation.
4. Define failure behavior:
   - throw on stale update/delete
   - evict and reload
   - report stale through diagnostics without mutation failure
5. Define telemetry events for freshness checks, stale detections, and freshness-check failures.
6. Add tests around current cache behavior before adding hash/version checks.

Exit criteria:

- freshness states are documented in planning and code comments where needed
- API behavior is clear for read, update, delete, reload, and cache eviction
- telemetry names and dimensions are chosen before provider SQL work starts

## Workstream B: Hash Or Version Freshness Implementation

Goals:

- implement provider-backed freshness checks without forcing every query to pay for them
- keep the design provider-extensible
- avoid pretending hashes are free or universally comparable

Tasks:

1. Define a provider abstraction for row freshness expression generation.
2. Start with `sqlite-memory` and SQLite file support if provider SQL is practical.
3. Evaluate MySQL/MariaDB hash expressions only after the SQLite path is clear.
4. Decide whether freshness tokens are:
   - provider SQL hashes
   - explicit version columns
   - generated synthetic projections
   - user-provided expressions
5. Add opt-in configuration for freshness checking.
6. Store freshness tokens with cached rows when enabled.
7. Check freshness before update/delete only when configured.
8. Decide whether ordinary reads include freshness tokens or whether checks are explicit.
9. Add provider tests for stale row detection.
10. Add benchmark coverage for freshness-enabled reads and mutations.

Exit criteria:

- freshness checking is opt-in and tested
- stale update/delete behavior is deterministic
- provider SQL differences are documented
- benchmark output shows the overhead of freshness checks

## Workstream C: External Invalidation API

Goals:

- let host applications invalidate cache entries when they know data changed elsewhere
- avoid taking a hard dependency on any queue, CDC, or distributed-cache technology
- make external invalidation compatible with later adapters

Candidate API shape:

```csharp
database.Cache.Invalidate(table, primaryKey);
database.Cache.Invalidate(table, primaryKeys);
database.Cache.InvalidateTable(table);
database.Cache.InvalidateDatabase();
```

The exact public shape can differ, but the first version should stay explicit and dull.

Tasks:

1. Define a public cache invalidation surface on the provider/database state.
2. Support table-level and primary-key invalidation.
3. Support relation/index invalidation using the same internals as mutation invalidation.
4. Add an invalidation event DTO that can be constructed by external adapters later:
   - database name
   - table name
   - primary key values
   - invalidation scope
   - source name
   - optional version/freshness token
5. Add telemetry source dimension values:
   - mutation
   - external
   - freshness
   - cleanup
   - memory-pressure
6. Add tests for invalidating one row, many rows, a table, and a database.
7. Add tests for unknown table/key behavior.

Exit criteria:

- applications can explicitly invalidate cached rows without reflection or internal access
- table-level invalidation is available as a conservative fallback
- invalidation telemetry records external source and scope
- no message-bus package is required

## Workstream D: Memory-Pressure-Aware Cleanup

Goals:

- make cleanup react to actual process pressure
- keep behavior predictable and observable
- avoid background work on unsupported runtimes

Tasks:

1. Use `GC.GetGCMemoryInfo()` to detect high memory load where supported.
2. Add configurable pressure thresholds.
3. Add a cleanup mode that can run more aggressively under pressure.
4. Record cleanup reason:
   - scheduled
   - row limit
   - size limit
   - age limit
   - memory pressure
   - external request
5. Ensure browser/runtime boundaries from Phase 8 remain respected.
6. Add tests for policy selection with injectable memory-pressure readers.
7. Add telemetry for pressure observations and cleanup outcomes.

Exit criteria:

- cleanup can react to memory pressure through a testable abstraction
- users can configure or disable memory-pressure cleanup
- telemetry identifies pressure-triggered cleanup
- browser/WASM paths do not start unsupported background workers

## Workstream E: Adaptive Cleanup Scheduling

Goals:

- reduce unnecessary cleanup work during idle periods
- react faster during high churn
- keep scheduling simple enough to reason about

Tasks:

1. Track mutation and invalidation churn since the last cleanup.
2. Define minimum and maximum cleanup intervals.
3. Adjust next cleanup interval based on churn and memory pressure.
4. Keep explicit configured cleanup intervals as the default until adaptive mode is opted in or proven safe.
5. Add tests with a fake clock and fake worker.
6. Add metrics for scheduled interval decisions.

Exit criteria:

- adaptive scheduling is deterministic under test
- users can inspect why cleanup ran
- idle workloads do not pay frequent cleanup cost
- high-churn workloads can clean sooner without unbounded background activity

## Workstream F: Adaptive Cache Policy

Goals:

- add smarter policy only where Phase 9A and 9B telemetry proves a need
- avoid startup database profiling by default
- provide clear override precedence

Tasks:

1. Define policy precedence:
   - explicit programmatic configuration
   - metadata attributes
   - opt-in adaptive policy
   - DataLinq defaults
2. Avoid automatic startup `COUNT(*)` or broad profiling by default.
3. Consider using observed telemetry instead of startup profiling for default decisions.
4. Add a programmatic configuration shape for cache policies if existing metadata attributes are insufficient.
5. Add table-level overrides for cache limits, cleanup, freshness checks, and external invalidation behavior.
6. Add benchmark comparisons for adaptive on/off behavior.

Exit criteria:

- adaptive policy is opt-in or conservative by default
- explicit user configuration wins over heuristics
- policy choices are visible through diagnostics or telemetry
- no hidden startup database scans are introduced

## Workstream G: Measured Key And Value Deduplication

Goals:

- reduce repeated object allocation only where measurements justify it
- avoid global caches that retain too much or add hot contention
- keep deduplication scoped where possible

Tasks:

1. Use Phase 9A allocation data to identify remaining key/value duplication.
2. Prototype scoped key interning for narrow scenarios.
3. Measure:
   - allocation reduction
   - throughput impact
   - lock/contention cost
   - object retention after cache cleanup
4. Prefer scoped caches or weak-reference strategies over global unbounded dictionaries.
5. Add stress tests for cleanup and retention.
6. Adopt only the deduplication strategies that clearly win.

Exit criteria:

- any deduplication change has benchmark evidence
- cleanup releases interned entries when expected
- no unbounded global key cache is introduced without a retention strategy
- concurrency tests do not show unacceptable contention

## Workstream H: Documentation, Diagnostics, And Closeout

Goals:

- make the shipped behavior understandable
- separate actual support claims from future distributed-cache ambitions
- leave result-set caching with a stronger foundation but still later

Tasks:

1. Update cache and diagnostics documentation for freshness and external invalidation.
2. Document policy precedence and opt-in behavior.
3. Document provider support boundaries for freshness tokens.
4. Add examples for explicit external invalidation.
5. Update roadmap docs with what shipped and what remains later.
6. Keep CDC/Kafka integrations as future adapter work unless actually implemented.

Exit criteria:

- docs match shipped behavior
- users can understand freshness cost and limitations
- result-set caching remains explicitly later
- Phase 12 can depend on tested freshness/invalidation foundations

## Suggested Implementation Order

1. Review Phase 9A baselines and invalidation tests.
2. Complete Workstream A freshness contract.
3. Implement Workstream C external invalidation API, because it reuses known invalidation mechanics and does not require hash SQL.
4. Implement Workstream D memory-pressure-aware cleanup.
5. Implement Workstream E adaptive cleanup scheduling.
6. Implement Workstream B hash/version freshness for the first supported provider path.
7. Implement Workstream F adaptive cache policy only after telemetry proves the right defaults.
8. Evaluate Workstream G deduplication last, with benchmark proof.
9. Complete Workstream H documentation and closeout.

This order is intentionally conservative. External invalidation and cleanup policy can use Phase 9A invalidation foundations. Hash/version freshness touches provider SQL and mutation semantics, so it deserves more design time.

## Verification

Baseline checks:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

Provider checks:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --targets sqlite-file,sqlite-memory --output failures --build
```

Benchmark checks:

```powershell
$env:DATALINQ_BENCHMARK_PROVIDERS = 'sqlite-memory'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --profile default --filter "*Crud*"
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile default
```

Stress checks should be added for:

- concurrent external invalidation
- concurrent relation/index invalidation
- cleanup during active readers
- memory-pressure cleanup policy decisions
- key/value deduplication retention

## Release Acceptance Criteria

Phase 9B can ship when:

- external invalidation works without optional bus/CDC dependencies
- row freshness support has a clear opt-in boundary and provider-specific tests
- adaptive cleanup behavior is observable and configurable
- memory-pressure cleanup is testable without relying on actual process pressure in tests
- any adaptive policy or deduplication feature has benchmark evidence
- docs explain what the cache can and cannot promise
- dependency-tracked result-set caching remains explicitly deferred
