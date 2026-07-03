> [!WARNING]
> This document is roadmap implementation material for the DataLinq 0.9 development line. It is not normative product documentation and should not be treated as a shipped support claim.

# 0.9 In-Memory Database Implementation Plan

**Status:** Draft.

**Created:** 2026-07-03.

## Purpose

This document keeps the immediate 0.9 implementation plan for the planned memory backend.

The durable architecture lives in [Memory Backend Architecture](../../backends/memory/Architecture.md). Keep broad design discussion there. Keep this page focused on sequencing, exit criteria, release boundaries, and what must be true before 0.9 can honestly claim a memory backend.

## 0.9 Goal

The 0.9 in-memory work should prove two things:

1. `DataLinqQueryPlan` can be executed by a non-SQL backend.
2. A generated-model memory backend can run in browser/WebAssembly AOT without native SQLite, OPFS, browser file APIs, `Expression.Compile()`, runtime code generation, or broad reflection fallback.

The first release claim should stay narrow:

> DataLinq 0.9 introduces a backend-neutral query execution boundary and a generated-model memory backend that executes a documented query subset directly from DataLinq query plans.

Strengthen that wording only if evidence earns it.

## Phase 4A: Backend Execution Contract

This can be part of v0.9 Phase 1, but it is the hard prerequisite for a non-embarrassing memory backend.

Work:

- introduce a backend execution boundary over `DataLinqQueryPlan`
- adapt SQL execution through that boundary
- add backend capability metadata
- add backend capability validation before execution
- preserve existing SQL provider behavior
- keep diagnostics DataLinq-owned when a backend cannot execute a supported plan node

Exit signal:

- `ExpressionQueryPlanExecutor` no longer assumes SQL as the only real executor
- existing SQLite, MySQL, and MariaDB query tests stay green
- unsupported backend/query combinations fail with clear DataLinq diagnostics
- public docs still describe only shipped SQL provider behavior

## Phase 4B: Memory Store Foundation

Work:

- create `DataLinq.Memory`
- create `MemoryDatabase<TDatabase>` and `MemoryDatabaseStore<TDatabase>` or equivalent
- create store root, table state, row buffer, and primary-key index primitives
- start the provider from generated metadata
- support seed loading
- support primary-key lookup
- support cache-aware materialization through existing runtime paths
- keep raw SQL unsupported with a clear provider capability error

Exit signal:

- generated model rows can be seeded and fetched without SQL
- direct primary-key lookup works under ordinary runtime
- direct primary-key lookup works under an AOT or strict compatibility smoke
- the provider does not imply SQL compatibility

## Phase 4C: Memory Query Subset

Work:

- implement plan predicate evaluation over row buffers
- implement equality/comparison predicates over scalar columns
- implement boolean predicate composition
- implement local scalar `Contains(...)` membership
- implement `OrderBy`, `ThenBy`, `Skip`, and `Take`
- implement `Any`, `Count`, `First`, `Single`, and `...OrDefault`
- implement direct scalar and anonymous projection from one source
- add parity tests against SQLite for the supported slice
- add backend capability diagnostics for unsupported shapes

Exit signal:

- a small documented query subset passes under memory and SQLite
- unsupported shapes fail clearly
- browser WebAssembly smoke runs the memory provider
- no unsupported query shape falls back to LINQ-to-Objects by accident

## Phase 5A: Memory Mutation

Work:

- insert generated mutable rows
- update generated mutable rows
- delete rows
- stage writes inside explicit transactions
- commit atomically
- rollback staged changes
- validate primary-key uniqueness
- validate required/null constraints where metadata can prove them
- validate foreign keys in strict mode where relation metadata is available
- invalidate cache/index state after commit

Exit signal:

- common generated-model mutation workflows pass
- transaction behavior is documented honestly
- browser smoke can prove mutation and read-back if included in the release claim
- the provider claims atomic and isolated in-process mutation only, not durability

## Phase 5B: Test And Snapshot Utility

Work:

- seed builders
- store fork/reset/snapshot APIs
- deterministic clock and generated-key services
- optional relaxed fixture mode if strict mode blocks useful tests
- optional failure injection if it stays cleanly testing-scoped

Exit signal:

- users can replace a meaningful subset of database-backed tests with memory-backed tests
- fixture APIs do not leak into core provider complexity
- browser/demo scenarios can start from deterministic seed data

## 0.9 Verification Gates

The memory backend should not be called supported until these are green:

- unit tests for row buffers, key normalization, indexes, constraints, and snapshots
- memory-provider tests for seed, lookup, mutation, rollback, commit, and conflict behavior
- compliance tests shared with SQLite for the supported query slice
- AOT strict smoke for generated models
- browser WebAssembly smoke with `MemoryDatabase<TDatabase>` as the backing store
- unsupported-query diagnostics tests
- allocation benchmarks for primary-key lookup and repeated simple query shapes

The browser smoke should prove:

- provider startup
- seed load
- primary-key lookup
- filtered query
- ordered/paged query
- direct projection
- mutation and read-back if mutation is in the 0.9 claim
- unsupported query diagnostic

## Release Boundary

The 0.9 release can claim an in-memory backend only when:

- the provider starts from generated metadata without runtime schema discovery
- the provider runs under Native AOT or a strict AOT smoke
- the provider runs in browser WebAssembly smoke
- primary-key lookup and a documented LINQ subset execute without SQL
- common seed and query workflows are documented
- unsupported shapes fail with DataLinq-owned diagnostics
- mutation semantics are either implemented and documented or explicitly out of scope
- public docs do not blur `DataLinq.Memory`, SQLite in-memory, and JSON persistence

Possible stronger claims, if earned:

- memory backend supports common generated-model read and mutation workflows
- memory backend is the default browser backing store for generated-model scenarios
- repeated simple query shapes allocate less through backend-plan execution and future template reuse

Claims to avoid unless proven:

- "SQL-compatible in-memory database"
- "drop-in replacement for every provider"
- "full ACID database"
- "all LINQ works in memory"
- "cache-backed database"
- "browser persistence"

## Links

- [Memory Backend Design Notes](../../backends/memory/README.md)
- [Memory Backend Architecture](../../backends/memory/Architecture.md)
- [DataLinq 0.9 Rough Roadmap](README.md)
