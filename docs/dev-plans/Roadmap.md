> [!WARNING]
> This is an internal planning document. It describes intended work, not shipped behavior. Use the public docs, support matrices, and changelog for current product claims.

# DataLinq Development Roadmap

**Status:** Active.

**Last reviewed:** 2026-07-13.

## Purpose

This page answers three questions:

1. What is the next release trying to prove?
2. Which work is required, optional, or deliberately later?
3. Which detailed plan owns each decision?

Completed phase history does not belong here. The 0.8 implementation record lives under [`roadmap-implementation/v0.8/`](roadmap-implementation/v0.8/README.md), and release-level behavior lives in the changelog.

## Current Baseline

The 0.8 release established the foundation that 0.9 consumes:

- a DataLinq-owned expression parser and `DataLinqQueryPlan`
- SQL generation from the plan for the documented LINQ subset
- immutable plan bindings with indexed lookup
- source slots, SQL-backed projection rows, grouped aggregate rows, and bounded joins
- provider-key cache identity and generated key accessors
- generated metadata startup and constrained SQLite AOT/browser evidence
- schema validation and conservative diff scripts
- explicit cache invalidation, freshness vocabulary, telemetry, and memory-pressure cleanup

The important current limitations are equally real:

- retained expression-query result families execute behind the selected SQL backend, and bounded neutral routes now cover integral/scalar-UUID primary keys plus exact single-column integral relation indices; broader primary/joined keys, cache/relation families, and legacy reader routes are not yet backend-neutral
- projection recipes are self-contained after parsing, but only the SQL adapter implements their execution; there is no memory capability profile or backend yet
- model values, canonical provider values, and provider physical/wire values are not separate first-class contracts
- DataLinq has no native async database I/O surface
- managed mutable baselines now have explicit rollback/cross-transaction provenance, but raw-handle and full-concurrency boundaries remain unresolved
- there is no memory backend, JSON memory persistence package, or migration execution engine

## Roadmap Principles

1. Correct existing provider behavior before multiplying backends.
2. Make one architecture boundary real before making it public and extensible.
3. Prefer a narrow vertical proof over a broad collection of interfaces.
4. Treat provider capabilities as explicit contracts, not runtime surprises.
5. Separate model values, canonical provider values, and physical provider encodings.
6. Keep memory semantics explicit; memory is not SQL emulation.
7. Measure before adding caches or claiming performance wins.
8. Select at most one optional stretch after required release evidence is green.

## 0.9 Decision

The 0.9 release is an architecture-first release with a deliberately small product proof:

> Make query execution backend-selectable, make provider values explicit, and prove both with typed IDs, correct UUID storage, and a read-only generated-model memory backend.

The earlier plan combined backend extraction, plan caching, broad SQL query expansion, a transactional memory database, JSON durability, commit logs, replay, browser adapters, and CLI tooling. That was not a credible minor-release boundary. The trimmed plan keeps the differentiating architecture and moves the second transaction/persistence project out.

## 0.9 Dependency Order

The authoritative start-to-release sequence is [0.9 Implementation Order And Integration Plan](roadmap-implementation/v0.9/Implementation%20Order%20and%20Integration%20Plan.md). It replaces the earlier conflicting linear summaries with explicit waves, safe parallel lanes, merge gates, and one ownership map.

The condensed order is:

1. record the clean 0.8 behavior, package, compatibility, and performance baseline
2. characterize query execution, transaction/cache behavior, mutable lifecycle, and scalar/UUID values in parallel
3. land scalar metadata plus self-contained template/invocation and projection recipes
4. satisfy the applicable per-family SQL transaction/cache fault and terminal-state gate before each neutral cache/relation routing slice moves
5. land the shared canonical row/source/materializer boundary, scalar runtime conversion, capability validation, and SQL adapter
6. complete typed-ID query/key/schema behavior; run the granular UUID physical-codec lane and separate-project primitive memory spike where their dependencies allow
7. promote memory to a read-only preview package only if the spike gate passes, then complete its explicit semantics while UUID remains an independent required release gate
8. run the provisional baseline evidence gate
9. choose zero or one stretch
10. rerun the frozen-candidate release closeout

The detailed release-evidence work is owned by [Release Evidence And Closeout Implementation Plan](roadmap-implementation/v0.9/Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md). The existing SQL correctness lane is owned by [SQL Transaction And Mutable Lifecycle Implementation Plan](roadmap-implementation/v0.9/SQL%20Transaction%20and%20Mutable%20Lifecycle%20Implementation%20Plan.md).

Memory mutation and durable persistence require later transaction/mutation foundations and are not part of this sequence.

## Required 0.9 Workstreams

### Execution Foundation

Owner:

- [`roadmap-implementation/v0.9/Query Backend and Execution Foundation Implementation Plan.md`](roadmap-implementation/v0.9/Query%20Backend%20and%20Execution%20Foundation%20Implementation%20Plan.md)

Required outcomes:

- `DataLinqQueryPlan` execution no longer assumes that every backend is an ADO.NET SQL provider.
- Supported execution does not need to rediscover projection behavior from the original expression tree.
- cold primary-key, relation, and ordinary query row loading can go through a backend-neutral row source.
- SQL-specific APIs remain on a SQL-facing surface rather than becoming throwing members on memory providers.
- capability failures identify the unsupported plan node, operation, source, and backend.
- the internal boundary is async/cancellation-ready without pretending that native async I/O ships in 0.9.

Structural template/invocation separation belongs here only to the extent required for a self-contained, reusable execution request. Production plan caching, eviction, cache lifetime, and public cache claims require separate benchmark evidence and remain later work.

### Scalar Converters And Typed IDs

Owners:

- [`roadmap-implementation/v0.9/Scalar Converters and Typed IDs Implementation Plan.md`](roadmap-implementation/v0.9/Scalar%20Converters%20and%20Typed%20IDs%20Implementation%20Plan.md)
- [`metadata-and-generation/Scalar Converter Support.md`](metadata-and-generation/Scalar%20Converter%20Support.md)

Required outcomes:

- metadata distinguishes model CLR type from canonical provider CLR type
- converters are resolved once rather than discovered on hot paths
- reads, writes, query constants, local membership, keys, relations, and validation use one conversion contract
- typed `int`, `long`, `Guid`, and `string` IDs are test-covered at the supported boundary
- unsupported member-level value-object queries fail clearly
- existing public model-facing row/indexer behavior remains model-valued

Third-party typed-ID adapters, convention plugins, generated typed-key classes, multi-column value objects, and arbitrary value-object member translation remain later work.

### UUID Storage Correctness

Owner:

- [`providers-and-features/UUID Storage Format Support.md`](providers-and-features/UUID%20Storage%20Format%20Support.md)

Required outcomes:

- UUID physical representation is column-specific metadata, not an accidental connection-string convention
- reads, writes, query parameters, local membership, keys, relations, defaults, validation, and diffing use the same codec
- MySQL/MariaDB binary/native/text UUID behavior is explicit and test-backed
- SQLite text/blob behavior is explicit and test-backed where supported
- canonical memory values remain `Guid`; MySQL byte order does not leak into memory or cache semantics

UUID codecs consume the scalar/provider-value pipeline but are not scalar converters. The layers are:

```text
model value <-> canonical provider CLR value <-> provider physical/wire value
```

### Read-Only Memory Preview

Owners:

- [`roadmap-implementation/v0.9/In-Memory Database Implementation Plan.md`](roadmap-implementation/v0.9/In-Memory%20Database%20Implementation%20Plan.md) (read-only preview)
- [`backends/memory/Architecture.md`](backends/memory/Architecture.md)

Required first slice:

- generated models only
- deterministic seed loading
- primary-key lookup
- scalar equality/comparison and boolean predicates
- local scalar membership
- ordering and paging
- `Any`, `Count`, and supported single-row results
- direct scalar and direct SQL-row-style projection shapes
- explicit unsupported-capability diagnostics
- ordinary runtime, strict AOT, and browser WebAssembly execution

The preview does not include:

- insert/update/delete
- transactions, rollback, conflict resolution, or atomic root swapping
- forks or replayable commit batches
- raw SQL
- SQL-provider parity claims
- a promise that every supported SQL query is supported in memory

Provider-backed suites remain authoritative for SQL translation and provider behavior. Memory proves the DataLinq plan and memory capability contract.

### Existing-Provider Correctness

Owners:

- [`roadmap-implementation/v0.9/SQL Transaction and Mutable Lifecycle Implementation Plan.md`](roadmap-implementation/v0.9/SQL%20Transaction%20and%20Mutable%20Lifecycle%20Implementation%20Plan.md)
- [`providers-and-features/SQLite Transaction Isolation Alignment.md`](providers-and-features/SQLite%20Transaction%20Isolation%20Alignment.md)
- [`query-and-runtime/Mutable Instance Lifecycle.md`](query-and-runtime/Mutable%20Instance%20Lifecycle.md)

Required outcomes:

- SQLite no longer relies on dirty reads to provide same-transaction visibility
- pending transaction state is not published as committed global state
- mutable instances record enough provider/transaction provenance to reject untrustworthy reuse
- rollback, disposal of an open transaction, failed writes, cross-provider reuse, and cross-transaction reuse have tested behavior

These gates protect the current SQL product. They also prevent a later memory mutation implementation from copying ambiguous semantics.

### Release Evidence

Owner:

- [`roadmap-implementation/v0.9/Release Evidence and Closeout Implementation Plan.md`](roadmap-implementation/v0.9/Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md)

The 0.9 plan must end with the same kind of evidence discipline used for the 0.8 closeout:

- focused unit and compliance suites
- capability-filtered memory tests rather than blindly adding memory to every SQL suite
- full SQL-provider regression coverage
- package-report and publish-package integration for every new package
- trim, Native AOT, WebAssembly, and WebAssembly AOT browser execution where claimed
- allocation benchmarks for repeated supported reads and memory primary-key lookup
- public docs and support matrices updated only after evidence is green

## Optional 0.9 Stretch

At most one stretch may enter 0.9 after the required workstreams are green. Shipping neither is acceptable.

### Option A: Bounded SQL Join Continuation

Owner:

- [`roadmap-implementation/v0.9/Join and Grouping Continuation Implementation Plan.md`](roadmap-implementation/v0.9/Join%20and%20Grouping%20Continuation%20Implementation%20Plan.md)

Preferred order:

1. multiple explicit inner joins over direct source-slot projection members
2. composite direct-member join keys
3. supported filtering/ordering/paging/results over those rows

Narrow .NET 10 `Queryable.LeftJoin(...)`, grouped multi-join continuation, and relation-aware fluent join sugar remain later work.

### Option B: Snapshot-Only JSON Prototype

Owners:

- [`roadmap-implementation/v0.9/Memory JSON Persistence Implementation Plan.md`](roadmap-implementation/v0.9/Memory%20JSON%20Persistence%20Implementation%20Plan.md)
- [`backends/memory/persistence/json/JSON Persistence Store Architecture.md`](backends/memory/persistence/json/JSON%20Persistence%20Store%20Architecture.md)

Allowed 0.9 boundary:

- manual import/export only
- one canonical, versioned snapshot document
- deterministic table/row/column ordering
- schema digest with test vectors
- canonical provider-value encoding
- actionable malformed-data diagnostics

Excluded:

- flush-on-commit durability
- filesystem/browser storage adapters as a product contract
- commit logs, replay, compaction, retention, and recovery claims
- CLI command families
- production browser persistence

## Explicitly Out Of 0.9

- native async provider execution as a shipped feature
- public query-plan or backend plugin APIs
- production query-plan caching
- memory mutation and transactions
- durable JSON persistence and commit logs
- broad join/grouping expansion
- `JoinBy(...)`, `JoinMany(...)`, and relation-aware left-join APIs
- generated typed-key output
- full migration execution
- SQL JSON-path querying
- dependency-tracked result caching
- DataLinq.Store execution
- distributed cache coordination and CDC

## Post-0.9 Direction

### Adoption Release

The strongest immediate follow-up is a deliberately boring application-adoption release:

1. native async query, relation, mutation, and transaction execution
2. cancellation propagation to provider commands
3. DI/provider registration
4. explicit unit-of-work factory and host lifetimes
5. startup schema-validation integration
6. model/relation graph builders and memory-backed testing registration

Owners:

- [`query-and-runtime/Async and Lazy Loading.md`](query-and-runtime/Async%20and%20Lazy%20Loading.md)
- [`architecture/Dependency Injection and Hosting Integration.md`](architecture/Dependency%20Injection%20and%20Hosting%20Integration.md)
- [`providers-and-features/Schema Validation Hooks.md`](providers-and-features/Schema%20Validation%20Hooks.md)
- [`testing/Model Testing and Mocking Support.md`](testing/Model%20Testing%20and%20Mocking%20Support.md)

Async provider I/O and explicit loading come first. Awaitable entities, ambient sessions, and sync property access that hides I/O remain experiments, not default API direction.

### Write-Path Release

The mutation backlog forms one dependency chain:

1. mutable lifecycle and provider-neutral mutation planning
2. set-based update/delete
3. explicit relation-aware mutation
4. call-scoped batching
5. provider bulk execution where evidence supports it
6. structured post-commit audit events

Owners:

- [`query-and-runtime/Set-based mutations.md`](query-and-runtime/Set-based%20mutations.md)
- [`query-and-runtime/Relation-Aware Mutation API.md`](query-and-runtime/Relation-Aware%20Mutation%20API.md)
- [`query-and-runtime/Batched mutations.md`](query-and-runtime/Batched%20mutations.md)
- [`query-and-runtime/Mutation Audit Events.md`](query-and-runtime/Mutation%20Audit%20Events.md)

When this work starts, define one internal canonical committed-change receipt and adapt it into memory persistence, audit, invalidation/CDC, and Store-specific contracts. Do not publish one giant DTO that pretends those consumers have identical security and compatibility needs.

### Later And Incubating

Keep these separate until their prerequisites and product demand are concrete:

- full migration execution
- generated typed-key output and third-party adapters
- SQL JSON-path translation and partial JSON updates
- production JSON/browser persistence
- dependency-tracked result/module caching
- distributed coordination and CDC
- DataLinq.Store modules, sync, authorization, and generated bindings

## Plan Governance

Every active plan should state:

- status
- target release or `Unscheduled`
- last reviewed date
- prerequisites
- required exit evidence
- explicit non-goals

Use release-local workstream names rather than globally reusing phase numbers. Completed implementation records belong in `roadmap-implementation/<version>/` or `archive/`, not in this active roadmap.

If a durable design note and a release implementation plan disagree, update the durable design or explicitly record the release-specific deviation. Do not allow two files to silently own the same work.

## Review Triggers

Revisit this order if:

- the vertical memory spike proves that the provider/source split is materially larger than the release can absorb
- scalar conversion requires a public model break rather than an additive internal boundary
- SQLite committed-visibility work exposes a broader transaction redesign
- benchmark evidence shows plan parsing/rendering is a more urgent bottleneck than assumed
- a concrete adoption requirement makes native async more urgent than the architecture proof
- package/AOT/browser evidence cannot support the intended memory preview claim
