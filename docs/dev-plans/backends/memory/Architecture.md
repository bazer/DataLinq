> [!WARNING]
> This document is roadmap and design material for the planned DataLinq memory backend. It is not normative product documentation and should not be treated as a shipped support claim.

# Memory Backend Architecture

**Status:** Draft.

**Created:** 2026-07-03.

## Purpose

The memory backend should prove that DataLinq's query-plan architecture is real architecture, not just a nicer way to generate SQL.

The goal is a first-class, generated-model-backed, AOT-friendly in-process backend that executes `DataLinqQueryPlan` directly. It should be useful for browser/WebAssembly applications, fast tests, examples, semantic parity checks, and short-lived application state.

The blunt rule:

> If the memory backend has to generate SQL, parse SQL, compile expression trees, or reinterpret arbitrary LINQ to work, the design failed.

## Design Thesis

DataLinq should treat the memory backend as a backend, not as a cache trick and not as SQLite with a different connection string.

The architecture should preserve the same core DataLinq contract:

- generated metadata is the schema
- generated accessors and provider-key shapes are the hot path
- query parsing produces `DataLinqQueryPlan`
- backend execution consumes the plan
- immutable models are materialized from provider rows
- mutations go through explicit mutable wrappers and transactions
- unsupported query shapes fail with DataLinq diagnostics

This is an unusually good fit for DataLinq because the project has already paid for generated metadata, generated factories, generated key accessors, frozen metadata, source slots, query-plan values, projection nodes, and provider-key cache identity. A memory backend should spend that capital instead of creating a parallel mini-ORM.

## AOT And Browser Are First-Class

AOT is not a polish item for this feature. It is one of the reasons to build it.

The memory backend is likely to become the default browser backing store because it avoids the current SQLite/WebAssembly native dependency problem. It should therefore be designed as if WebAssembly AOT is a normal runtime, not a heroic special case.

Hard constraints:

- no `Expression.Compile()` in the query execution path
- no runtime code generation
- no broad reflection fallback for model shape, accessors, or serialization-like behavior
- no SQL parser
- no dependency on SQLitePCLRaw, native SQLite, OPFS, or browser file APIs for the baseline
- no background cleanup thread as a required correctness mechanism
- no dynamic provider discovery needed for generated models
- no lazy "just use LINQ-to-Objects" fallback for unsupported provider queries

Preferred implementation shape:

- generated metadata supplies table, column, relation, key, and factory information
- generated provider-key accessors normalize key identity
- row values are stored in provider-value form by column ordinal
- query-plan predicates are interpreted over row buffers using metadata and bindings
- projection rows are assembled from source slots without compiling selectors
- row-local computed projections use the existing supported projection evaluator only where the current support contract already allows it
- diagnostics name the unsupported plan node, backend capability, source slot, or value kind

The browser baseline should be:

```csharp
var db = new MemoryDatabase<AppDb>();

db.Seed(seed => seed
    .Table(x => x.Users)
    .Rows(users));

var rows = db.Query().Users
    .Where(x => x.IsActive)
    .OrderBy(x => x.UserId)
    .ToList();
```

No files. No native libraries. No browser storage permission story in the baseline. Persistence should be an explicit memory-store option, not a different query backend.

## Relationship To SQLite In-Memory

SQLite in-memory is a real and useful provider target, but it is not this feature.

SQLite in-memory still exercises:

- SQL rendering
- SQLite SQL semantics
- SQLite type affinity
- SQLite transaction behavior
- SQLite native/browser payload issues

The DataLinq memory backend should exercise:

- `DataLinqQueryPlan` execution without SQL
- metadata-driven storage
- provider-key materialization
- provider-neutral query semantics
- AOT-safe row access and projection
- deterministic in-process snapshots

Both are useful. They should not be confused.

## Relationship To The Existing Cache

The memory backend must not be "the cache, but promoted."

DataLinq's cache stores materialized immutable instances and relation/index lookup results for a provider. The memory backend is the provider. It owns durable-for-this-process row state. The cache can still sit above it and provide object identity reuse, materialized relation reuse, metrics, and invalidation behavior.

That split matters:

- provider store: normalized row buffers, indexes, transaction snapshots
- DataLinq cache: immutable model instances, row cache hits/misses, relation caches, invalidation metrics

Conflating those layers would make mutation semantics harder to reason about and would make cache eviction accidentally delete database state. That would be clever in the worst way.

## Public Product Shape

Likely package:

- `DataLinq.Memory`

Likely entry points:

```csharp
var db = new MemoryDatabase<AppDb>();
var db = new MemoryDatabase<AppDb>(store);
var store = MemoryDatabaseStore.Create<AppDb>();
var fork = store.Fork();
```

Persistence should be configured when the memory store is created:

```csharp
var db = MemoryDatabase.Open<AppDb>(options => options
    .UseJsonPersistence("appdb.datalinq.json", json => json
        .SnapshotOnly()
        .FlushOnCommit()));

var store = MemoryDatabaseStore.Create<AppDb>(options => options
    .UseJsonPersistence("appdb.datalinq.json", json => json
        .SnapshotWithCommitLog()
        .ExplicitFlush()));

var db = new MemoryDatabase<AppDb>(store);
```

Useful optional builders:

```csharp
var store = MemoryDatabaseStore.Create<AppDb>(options => options
    .UseStrictConstraints()
    .UseDeterministicClock(clock)
    .UseDeterministicIds(ids));

store.Seed(seed => seed
    .Table(db => db.Departments).Rows(departments)
    .Table(db => db.Employees).Rows(employees));
```

Naming should avoid pretending this is a generic fake database. `MemoryDatabase<TDatabase>` and `MemoryDatabaseStore<TDatabase>` are clear. `MockDatabase` is wrong. This should be real enough to run application logic, not a mocking framework.

The persistence package name is still open, but it should read as a companion to memory, for example `DataLinq.Memory.Json` or `DataLinq.Persistence.Json`. A top-level `DataLinq.JsonStore` package is probably the wrong signal because it sounds like a peer query backend or a document database.

## Store Model

The store should be an immutable root snapshot plus explicit mutation staging.

Conceptual shape:

```csharp
internal sealed class MemoryStoreRoot
{
    public long Version { get; init; }
    public IReadOnlyDictionary<TableDefinition, MemoryTableState> Tables { get; init; }
}

internal sealed class MemoryTableState
{
    public TableDefinition Table { get; init; }
    public IReadOnlyDictionary<DataLinqKey, MemoryRowBuffer> RowsByPrimaryKey { get; init; }
    public IReadOnlyDictionary<MemoryIndexKey, MemoryIndexState> Indexes { get; init; }
    public long NextGeneratedKey { get; init; }
}

internal sealed class MemoryRowBuffer
{
    public TableDefinition Table { get; init; }
    public object?[] ProviderValuesByOrdinal { get; init; }
}

internal sealed class MemoryCommitBatch
{
    public long FromVersion { get; init; }
    public long ToVersion { get; init; }
    public IReadOnlyList<MemoryCommitOperation> Operations { get; init; }
}
```

The exact data structures can change after benchmarking. The design intent should not:

- rows are stored as provider values, not immutable model instances
- column ordinal lookup is preferred over name lookup on hot paths
- primary keys use provider-key identity
- composite keys use generated/comparable key shapes where available
- secondary indexes are derived from metadata, not manually configured ad hoc
- table snapshots are replaceable as a unit
- successful commits can be represented as canonical provider-value operation batches

`System.Collections.Immutable` may be fine for the first implementation, but do not religiously commit to it before measurements. For small and medium browser data sets, allocation behavior may matter more than theoretical persistent-data-structure elegance. A copy-on-write table state with ordinary dictionaries plus versioned root swaps might be simpler and faster enough.

## Query Execution

The memory backend needs a backend-neutral execution seam before it becomes serious.

Candidate shape:

```csharp
internal interface IQueryPlanBackend
{
    QueryBackendCapabilities Capabilities { get; }

    IEnumerable<IImmutableInstance> ExecuteEntitySequence(
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan);

    object? ExecuteScalar(
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan);

    IEnumerable<object?> ExecuteProjectionSequence(
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan);
}
```

The interface above is only illustrative. The durable decision is that `ExpressionQueryPlanExecutor` should not directly instantiate `QueryPlanSqlBuilder` for every real path. SQL should become one backend implementation. Memory should become another.

The memory executor should run the plan in stages:

1. Validate backend capability support.
2. Resolve source slots to table snapshots.
3. Use primary-key or equality indexes where the predicate shape allows it.
4. Interpret predicates over row buffers.
5. Apply joins, grouping, ordering, paging, and result operators for supported shapes.
6. Materialize immutable rows through existing DataLinq materialization paths.
7. Apply SQL-backed-style projection rows or row-local computed projections only inside the documented support boundary.

The first implementation should not attempt every current SQL-supported shape. It should fail clearly until each shape is intentionally implemented and tested.

Good first query subset:

- direct primary-key lookup
- `Where` equality/comparison over scalar columns
- boolean `&&`, `||`, and `!`
- local `Contains(...)` membership over scalar values
- `OrderBy`, `ThenBy`, `Skip`, `Take`
- `Any`, `Count`, `First`, `Single`, and `...OrDefault` result shapes
- direct scalar projection
- direct anonymous projection from one source

Later query subset:

- relation existence predicates
- grouped aggregate rows
- explicit joins
- implicit singular relation traversal
- joined projection rows
- post-paging pushdown-equivalent behavior

The memory backend can be stricter than SQL at first. It must not be looser.

## Query Semantics

The memory backend should be a semantic pressure test for DataLinq, not a C# accident machine.

Rules:

- the supported LINQ matrix remains the public contract
- unsupported expression shapes still fail at translation or capability validation
- null semantics must be specified and tested
- string comparison rules must be explicit
- date/time member behavior must match documented DataLinq semantics
- local sequence values must be copied/frozen the same way query-plan bindings expect
- ordering is not stable unless the query asks for enough ordering

Important decision:

The memory backend should not blindly use ordinary LINQ-to-Objects comparison behavior if the support matrix says DataLinq semantics differ. It should implement DataLinq's provider-neutral semantics where those exist, and it should document provider-specific differences where they remain.

## Transactions And Mutation

The useful promise is not "ACID database." The useful promise is:

> Atomic, isolated, in-process mutations over generated model tables, with no durability beyond the store lifetime.

Initial transaction semantics:

- every transaction starts from a store root version
- reads inside a transaction see the starting snapshot plus the transaction's staged writes
- insert/update/delete stage new table states
- commit validates constraints, swaps the store root atomically, and emits a canonical commit batch
- rollback discards staged changes
- concurrent conflicting commits fail or retry according to a documented policy

For browser/WebAssembly, a single-writer lock or serialized commit gate is probably enough. Do not build a complicated lock-free database because the phrase sounds impressive. The important user behavior is deterministic snapshots and clear conflict handling.

Mutation support should include:

- insert generated mutable rows
- update generated mutable rows
- delete rows
- default-value application
- generated identity/key handling
- primary-key uniqueness validation
- required/null validation where metadata can prove it
- foreign-key validation in strict mode
- relation/index invalidation after commit

The existing synchronous `Insert`, `Update`, and `Save` APIs should remain honest and materializing. Async can be added later only if the runtime/provider boundary has real async work to do. For a memory backend, fake async is worse than no async.

## Commit Batches And Replayability

The mutation layer should treat a successful commit as a durable internal artifact, not just as "the root pointer changed."

A committed mutation should produce a `MemoryCommitBatch` or equivalent value:

- version before commit
- version after commit
- database and schema identity
- ordered insert/update/delete operations
- table `DbName`
- primary-key provider values
- changed provider values by column `DbName` or ordinal
- optional diagnostic metadata such as transaction label, source, or timestamp when supplied by the caller

The commit batch should record committed provider-value operations after validation, not every attempted object mutation. That distinction matters. Replayability should mean "rebuild the same DataLinq memory store state from a snapshot plus committed operations," not "replay arbitrary user code, failed transactions, relation-cache events, or timing-dependent behavior."

This gives DataLinq three useful persistence/export shapes:

- `SnapshotOnly`: write the canonical final state.
- `CommitLogOnly`: append committed batches and replay from an empty or seed snapshot.
- `SnapshotWithCommitLog`: write periodic snapshots plus the committed batches after the snapshot.

`SnapshotOnly` should be the default first implementation because it is easiest to inspect and hardest to corrupt. `SnapshotWithCommitLog` is the serious long-term mode because it gives startup checkpoints plus full committed-state replay. `CommitLogOnly` is useful for tests, debugging, and event-sourced workflows, but it should not be the default browser persistence story until startup cost, compaction, and schema evolution are proven.

Commit log support should be designed with mutation improvements, not bolted on afterward. If the transaction layer cannot naturally describe its commit as an ordered operation batch, persistence and diagnostics will both get weaker.

Open replay rules:

- replay must validate schema identity before applying operations
- replay must fail on unsupported schema changes unless an explicit migration/import path is provided
- replay must be deterministic for generated keys, defaults, and clocks
- replay should be able to stop at a target version for debugging
- compaction should be snapshot creation plus old-log retention policy, not silent deletion of history

## Constraints

Strict mode should be the default for application-like use:

- duplicate primary keys fail
- missing non-null values fail
- foreign-key violations fail when relation metadata is available
- unknown columns cannot be seeded
- invalid provider-value conversions fail with table/column context
- unsupported defaults fail unless an explicit value is supplied

Relaxed mode can be useful for tests and partial fixtures:

- allow missing unrelated tables
- allow FK gaps
- allow default-less generated keys only when the user supplies a deterministic key policy

The options should be explicit. A fixture that silently creates impossible data is a test bug factory.

## Indexes

Minimum indexes:

- primary-key index for every keyed table
- relation/FK indexes for metadata-backed relation traversal

Candidate secondary indexes:

- metadata-defined indexes
- unique indexes for constraint validation
- single-column equality indexes for common predicates

Index use should be diagnostic-visible. A user should be able to ask why a memory query scanned a table instead of using an index.

Possible diagnostic output:

```text
MemoryQueryPlan
  table: Employees
  candidate source: index Employees.emp_no
  predicate: emp_no IN p0
  rows scanned: 3
  rows matched: 3
  projection: entity
```

The first version can keep this internal or test-only. Long term, it would be useful as `Explain()` output across backends.

## Seeding, Snapshots, And Test Utility

This backend should make test setup boring.

Useful features:

- seed tables from immutable instances
- seed tables from mutable instances
- seed tables from anonymous/table-shaped records only if mapping is explicit
- import/export a deterministic snapshot object
- import/export a deterministic committed operation log
- replay a snapshot plus commit log to a target store version
- fork a store cheaply for scenario tests
- reset a store to a named seed snapshot
- deterministic clock and generated-key services
- opt-in failure injection for testing retry/error paths

Good test shape:

```csharp
using var baseline = MemoryDatabaseStore.Create<EmployeesDb>()
    .Seed(seed => seed
        .Table(db => db.Departments).Rows(StandardDepartments)
        .Table(db => db.Employees).Rows(StandardEmployees));

using var scenario = baseline.Fork();
using var db = new MemoryDatabase<EmployeesDb>(scenario);
```

The useful distinction:

- `Fork()` is for independent scenario mutation
- `Snapshot()` is for inspection/export/reuse
- `CommitLog()` is for replay/debug/audit-like workflows over committed store operations
- `Reset()` is for test lifecycle convenience

## Browser Use Cases

The browser story is not only tests.

Real uses:

- client-side demo data
- transient application state with generated model ergonomics
- offline-first prototypes before the persistence policy is chosen
- documentation samples that run without a server
- local-first workflows where sync/persistence is a later explicit layer
- replacing the current SQLite browser smoke dependency for query/runtime proof

The memory provider should be the simplest browser backer:

- no server
- no native SQLite
- no OPFS baseline
- no storage permissions
- no hidden serialization
- no browser-specific dependency for core behavior

Persistence should be layered through memory-store options. JSON snapshot/log persistence is the likely first persistence implementation. IndexedDB, OPFS, and DataLinq.Store synchronization can come later behind the same memory-store persistence boundary instead of becoming separate query backends.

## Compliance And Verification

The memory backend needs its own tests, but its highest value is cross-provider pressure.

Verification lanes:

- unit tests for row buffers, key normalization, indexes, constraint validation, and snapshots
- memory-provider tests for seeding, lookup, mutation, rollback, commit, and conflict handling
- commit-batch tests for deterministic insert/update/delete operation capture
- replay tests for snapshot-only, commit-log-only, and snapshot-with-commit-log flows when those modes ship
- compliance tests shared with SQLite for the supported query subset
- AOT strict smoke for generated models
- browser WebAssembly smoke with `MemoryDatabase<TDatabase>` as the backing store
- allocation benchmarks for primary-key lookup, repeated query shapes, and common fixture setup

The AOT/browser smoke should prove:

- provider startup
- seed load
- primary-key lookup
- filtered query
- ordered/paged query
- direct projection
- mutation and read-back if mutation is in scope
- unsupported query diagnostic

The compliance rule:

If a LINQ shape is not covered for memory, it is not supported for memory. Do not inherit SQL support claims by vibes.

## Documentation Shape

When this ships, public docs should separate:

- SQL providers: SQLite, MySQL, MariaDB
- memory provider: in-process generated-model backend
- SQLite in-memory: SQLite provider mode
- JSON memory persistence: snapshot/log storage for the memory provider
- other persistent local stores: experimental/future unless proven

Avoid phrases like:

- "SQL-compatible in-memory database"
- "drop-in replacement for any provider"
- "full ACID in memory"
- "cache-backed database"
- "all LINQ works in memory"

Better wording:

> DataLinq.Memory is an in-process backend for generated DataLinq models. It executes the documented query subset directly from DataLinq query plans and is designed for tests, browser scenarios, examples, and transient application state.

Persistence wording should stay attached to memory:

> DataLinq memory stores can optionally persist snapshots and committed mutation logs through configured persistence stores such as JSON. Query and mutation semantics still belong to `DataLinq.Memory`; JSON is a storage format, not a query backend.

## Open Questions

- Should `MemoryDatabase<TDatabase>` be constructed directly, or should all instances go through `MemoryDatabaseStore<TDatabase>`?
- Should relaxed fixture mode exist in the first release, or should strict mode ship first?
- Should memory query semantics define the provider-neutral truth when SQL providers differ, or should it mimic SQLite for parity convenience?
- How much of relation traversal should be in the first supported slice?
- Should `Explain()` be memory-only at first or part of the shared backend diagnostics story?
- Should the first persistence package be named `DataLinq.Memory.Json`, `DataLinq.Persistence.Json`, or something else that does not imply a JSON query backend?
- Should browser persistence start with JSON snapshot-only, JSON snapshot-plus-log, IndexedDB, or deliberately wait until the memory backend is stable?
- How much commit-log/replay support should land with the first mutation implementation?
- Which query shapes should be in the first WebAssembly smoke beyond PK lookup and simple filters?

## Non-Goals

- arbitrary LINQ execution
- raw SQL execution
- SQL compatibility
- SQLite compatibility quirks
- cross-process sharing
- durable persistence in the baseline memory package
- treating persistence stores as independent query backends
- distributed cache coordination
- OPFS or IndexedDB persistence
- DataLinq.Store integration
- full migration execution
- pretending every SQL-supported query shape is memory-supported on day one
