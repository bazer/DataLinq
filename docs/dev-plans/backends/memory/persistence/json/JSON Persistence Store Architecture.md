> [!WARNING]
> This document is roadmap and design material for planned JSON persistence for the DataLinq memory backend. It is not normative product documentation and should not be treated as a shipped support claim.

# JSON Persistence Store Architecture

**Status:** Draft.

**Created:** 2026-07-03.

**Reframed:** 2026-07-04.

## Purpose

JSON persistence should provide a simple, inspectable, DataLinq-owned storage format for `DataLinq.Memory` state.

The most important use cases are:

- browser/WebAssembly applications that need simple local persistence
- small single-user applications that want readable storage without a database server
- examples and demos that should persist state without SQLite or MySQL
- deterministic test fixtures and repro artifacts
- import/export of DataLinq memory-store state for local workflows
- replayable mutation traces for debugging and audit-like workflows

The JSON persistence store is deliberately narrower than "DataLinq over any JSON file." It owns the JSON shape. Existing arbitrary JSON document mapping is out of scope for this design.

The blunt rule:

> JSON is storage. `DataLinq.Memory` is the backend. DataLinq metadata is schema. Query and mutation semantics still belong to DataLinq.

## Design Thesis

JSON should be a persistence companion to the memory backend, not its own backend.

The runtime shape should be:

1. Configure a `MemoryDatabaseStore<TDatabase>` with a JSON persistence store.
2. Load a DataLinq-owned JSON snapshot, replay a commit log, or both.
3. Validate format, schema identity, and table payloads.
4. Materialize provider-value row buffers into memory table state.
5. Execute queries through the memory backend's `DataLinqQueryPlan` executor.
6. Apply mutations through the memory backend transaction layer.
7. Persist a deterministic snapshot, append committed operation batches, or both, according to store options.

This keeps the architecture honest:

- JSON parsing and writing are not query execution
- JSON path expressions are not the query language
- JSON storage does not bypass generated metadata
- JSON persistence does not require SQL generation
- commit logging belongs to the memory transaction layer, not to JSON-specific mutation code

## Relationship To Memory

The memory backend owns:

- row buffers
- primary-key and secondary indexes
- generated-key state
- constraints
- query-plan execution
- transactions
- canonical commit batches
- snapshots
- replay semantics

The JSON persistence store owns:

- snapshot serialization
- commit-log serialization
- storage adapters
- flush policy integration
- storage-format validation
- diagnostics over JSON locations and DataLinq metadata

If a JSON persistence feature needs a new query behavior, the work belongs in the memory backend first. JSON should never grow a separate query evaluator.

## Non-Goal: Arbitrary Existing JSON Documents

Do not mix JSON memory persistence with existing arbitrary JSON document mapping.

That separate idea has different problems:

- arrays-as-tables mapping
- path metadata
- schema inference
- sample-based model generation
- preserving unrelated document nodes
- preserving formatting and property order
- partial write-back into nested structures
- polymorphic document shapes

Those are legitimate problems, but they are not the important persistence story for DataLinq right now.

Explicitly out of scope:

- generating models from arbitrary JSON samples
- generating models from JSON Schema
- generic JSON tree navigation as a DataLinq backend
- JSONPath-backed table mapping
- preserving comments, whitespace, property order, or unknown nodes from an existing JSON file
- partial updates into arbitrary nested JSON documents

## Non-Goal: SQL JSON Columns

SQL-provider JSON column support is a different feature.

The existing SQL JSON column design covers JSON values stored inside MySQL, MariaDB, or SQLite columns. That feature needs attributes, provider SQL functions, JSON path predicates, and column-level serialization behavior.

JSON memory persistence owns the whole memory-store snapshot/log. It should not borrow JSON column vocabulary unless the underlying scalar conversion rules are truly shared.

## AOT And Browser Are First-Class

JSON persistence matters because the memory backend is the likely browser/WebAssembly backing store. It must therefore be designed for AOT from the beginning.

Hard constraints:

- use an existing JSON library; do not invent parsing or writing
- prefer `System.Text.Json`
- avoid reflection-based serialization for row payloads
- avoid `Expression.Compile()`
- avoid runtime code generation
- avoid raw JSONPath execution as a query path
- avoid native dependencies
- avoid filesystem-only assumptions in core storage abstractions

Recommended implementation shape:

- use `Utf8JsonReader` and `Utf8JsonWriter` for canonical store payloads where direct row-buffer reading/writing is cleaner than serializer DTOs
- use `System.Text.Json` source-generated contexts only for stable manifest/options DTOs if useful
- write provider values directly by metadata column ordinal and type
- use a storage abstraction so the same persistence logic can target filesystem, browser storage, in-memory strings, or future packaged assets

The JSON library decision should be boring: start with `System.Text.Json`. Add another dependency only if a concrete requirement cannot be met cleanly.

## Product Shape

Likely packages:

- `DataLinq.Memory`
- `DataLinq.Memory.Json` or `DataLinq.Persistence.Json`

Avoid `DataLinq.JsonStore` unless there is a strong reason. That name sounds like a standalone provider, JSON column support, arbitrary document mapping, or serializer helpers.

Likely entry points:

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

Browser-oriented shape:

```csharp
var store = MemoryDatabaseStore.Create<AppDb>(options => options
    .UseJsonPersistence(
        new BrowserJsonMemoryStorage("appdb"),
        json => json
            .SnapshotWithCommitLog()
            .FlushOnCommit()));

var db = new MemoryDatabase<AppDb>(store);
```

Testing/export shape:

```csharp
var snapshot = store.Snapshot();
var log = store.CommitLog();

File.WriteAllText("repro.datalinq.memory.json", JsonMemorySnapshot.Write(snapshot));
File.WriteAllText("repro.datalinq.memory.log.json", JsonMemoryCommitLog.Write(log));
```

The exact names can change. The important API distinction is:

- memory database/provider: query and mutation surface
- memory store: row state, indexes, transactions, snapshots, commit batches
- persistence store: storage format, flush policy, schema compatibility, storage target
- storage adapter: filesystem/browser/string transport

## Persistence Modes

Persistence content mode and flush policy should be separate choices.

Content modes:

- `SnapshotOnly`: write the canonical final state.
- `CommitLogOnly`: append committed batches and replay from an empty or seed snapshot.
- `SnapshotWithCommitLog`: write periodic snapshots plus the committed batches after the snapshot.

Flush policies:

- `FlushOnCommit`: every committed transaction updates durable storage before the commit is reported durable.
- `ExplicitFlush`: commits update in-process state and mark the persistence store dirty; the caller flushes when appropriate.

Recommended defaults:

- `SnapshotOnly` plus `FlushOnCommit` for simple application use.
- `SnapshotOnly` plus `ExplicitFlush` for fixtures and bulk test setup.
- `SnapshotWithCommitLog` for replay/debug workflows.
- `CommitLogOnly` only when startup cost, compaction, and seed-state rules are explicit.

The content mode answers "what do we write?" The flush policy answers "when is it durable?" Mixing those into one option would produce foot-guns.

## Snapshot Format

V1 should start with one canonical JSON document for a whole memory-store snapshot.

That is the right first tradeoff because:

- cross-table consistency is easier
- atomic filesystem replacement is easier
- browser storage is easier
- copying a repro is easier
- human inspection is still reasonable for the intended small-store use cases

Per-table files can come later if large stores, diff ergonomics, or partial-write performance justify the complexity.

Candidate V1 snapshot shape:

```json
{
  "$schema": "https://datalinq.org/schemas/datalinq-memory-snapshot.v1.schema.json",
  "format": "datalinq-memory-snapshot/v1",
  "database": "EmployeesDb",
  "schema": {
    "digest": "sha256:...",
    "mode": "strict"
  },
  "store": {
    "version": 42
  },
  "tables": {
    "departments": [
      {
        "dept_no": "d001",
        "dept_name": "Marketing"
      }
    ],
    "employees": [
      {
        "emp_no": 10001,
        "birth_date": "1953-09-02",
        "first_name": "Georgi",
        "last_name": "Facello",
        "gender": "M",
        "hire_date": "1986-06-26"
      }
    ]
  }
}
```

Snapshot rules:

- `format` is required and versioned.
- `database` is the DataLinq database metadata name.
- `schema.digest` identifies the generated metadata shape the snapshot was written against.
- `store.version` is the memory-store version represented by the snapshot.
- `tables` is keyed by table `DbName`.
- row properties are keyed by column `DbName`.
- missing tables mean empty tables only when the compatibility mode explicitly allows it.
- unknown tables or columns fail in strict mode.
- output ordering is deterministic: manifest fields, tables, rows, and columns should have stable order.

Use database and column `DbName`, not C# property names, as the default storage keys. The storage file should survive C# property renames when the underlying DataLinq schema name remains stable.

## Commit Log Format

The commit log should serialize memory commit batches after transaction validation.

It should not record every setter call, failed transaction, relation-cache event, or application command. The log is a replayable DataLinq store operation log, not an application event-sourcing framework.

Candidate V1 commit-log shape:

```json
{
  "$schema": "https://datalinq.org/schemas/datalinq-memory-commit-log.v1.schema.json",
  "format": "datalinq-memory-commit-log/v1",
  "database": "EmployeesDb",
  "schema": {
    "digest": "sha256:..."
  },
  "base": {
    "snapshotId": "snapshot-00042",
    "version": 42
  },
  "commits": [
    {
      "id": "commit-00043",
      "fromVersion": 42,
      "toVersion": 43,
      "utc": "2026-07-04T10:15:30Z",
      "operations": [
        {
          "op": "insert",
          "table": "employees",
          "values": {
            "emp_no": 10001,
            "first_name": "Georgi"
          }
        },
        {
          "op": "update",
          "table": "employees",
          "key": {
            "emp_no": 10001
          },
          "set": {
            "first_name": "George"
          }
        },
        {
          "op": "delete",
          "table": "employees",
          "key": {
            "emp_no": 10001
          }
        }
      ]
    }
  ]
}
```

Commit-log rules:

- log entries are ordered and versioned
- `fromVersion` must match the current replay store version
- `toVersion` must become the replay store version after applying the batch
- operations use table `DbName`
- keys and values use column `DbName`
- values use provider-value encoding
- replay validates constraints unless an explicit unsafe/import mode is selected
- replay can target a specific version for debugging

The first implementation can store one JSON array log if appending safely is too much work. Long term, append-only JSON Lines or segmented log files may be better for large logs and browser storage. The design should not depend on full-file rewrite being the only possible log strategy.

## Provider Value Encoding

JSON persistence must not invent one-off conversion rules.

All value encoding should flow through the same model-value/provider-value conversion boundary planned for v0.9. JSON persistence is where that boundary will be stress-tested.

Initial encoding principles:

- `null` is JSON `null`.
- booleans are JSON booleans.
- ordinary integral provider values can be JSON numbers when they round-trip through `System.Text.Json`.
- `decimal` should be encoded as a string unless the conversion boundary explicitly proves JSON-number round-tripping is safe enough for the support claim.
- `DateOnly`, `TimeOnly`, `DateTime`, `DateTimeOffset`, and `Guid` should use invariant strings.
- byte arrays should use base64 strings.
- enums should use their configured provider representation, not an automatic enum-name policy.
- typed IDs should use their provider representation after scalar conversion.
- SQL JSON columns, when supported, should store their raw provider value according to JSON column support rules, not be silently merged into the memory snapshot/log structure.

Because generated metadata supplies column type, row payloads do not need type tags for every value. Type tags would make the format noisy and would weaken human readability.

## Schema Compatibility

The persistence store should have explicit schema modes:

- `Strict`: schema digest must match and unknown/missing shape fails.
- `Compatible`: allow safe additive changes such as missing nullable/defaulted columns.
- `Manual`: load only through explicit migration/import tooling.

V1 should implement `Strict` first.

Compatibility work should be conservative:

- added nullable column: can load with `null`
- added column with DataLinq default: can load with default only when default evaluation is supported
- removed column: old value is ignored only in a migration/import path, not silent strict load
- renamed column: requires explicit migration metadata
- changed provider type: requires explicit migration metadata
- changed key shape: requires explicit migration metadata

Do not build a full migration engine inside JSON persistence. A clear "schema mismatch" error is better than a clever data rewrite nobody asked for.

## Storage Lifecycle

Filesystem snapshot write strategy:

1. Serialize to a temporary file in the same directory.
2. Flush the temporary file.
3. Replace the target file atomically where the platform supports it.
4. Leave an actionable failure if replacement cannot be completed.

Browser storage strategy:

- write through a storage adapter
- require adapter-level consistency guarantees to be documented
- do not pretend browser storage has filesystem atomic rename semantics
- expose whether snapshot and log writes are atomic together, best-effort, or recoverable through version checks

Cross-process concurrency is not a V1 goal. The persistence store can detect external changes through version, ETag, or last-write token where the storage adapter supports it, but multi-writer coordination should stay out of scope until there is a real use case.

## Query Execution

JSON should not execute queries.

The expected runtime path:

1. Load a JSON snapshot and/or replay a JSON commit log into memory-store row buffers.
2. Build memory table and index state.
3. Execute `DataLinqQueryPlan` through the memory backend.
4. Mutate through memory transactions.
5. Persist dirty memory state back to JSON snapshot/log storage according to the configured content mode and flush policy.

This makes the design:

> memory backend plus DataLinq-owned persistence

not:

> document database with JSON path query execution

That is a good thing. The first version should be boring and correct.

## Mutation Semantics

Mutation should follow the memory backend's in-process transaction semantics. JSON persistence observes successful commits.

Supported behavior:

- insert/update/delete generated mutable rows through memory transactions
- explicit transaction staging in memory
- primary-key uniqueness validation in memory
- strict required/null validation in memory
- strict foreign-key validation where relation metadata is available
- dirty-state tracking
- snapshot write on flush when configured
- commit-batch append on flush or commit when configured

Durability claim:

- with `FlushOnCommit`, a commit is durable only after the configured snapshot/log write succeeds
- with `ExplicitFlush`, a commit is not durable until flush succeeds
- with `CommitLogOnly`, replayability depends on the availability and schema compatibility of the base seed/snapshot
- with `SnapshotWithCommitLog`, recovery starts from the latest valid snapshot and replays subsequent committed batches

This distinction must be visible in docs and diagnostics. Hidden durability policy is how users lose data.

## CLI Surface

CLI support should be simple and operational.

Potential commands:

```text
datalinq memory json init
datalinq memory json validate
datalinq memory json export-snapshot
datalinq memory json export-log
datalinq memory json replay
datalinq memory json compact
datalinq memory json rewrite
```

Useful behavior:

- create an empty memory snapshot from generated metadata
- validate format/schema/row values without loading a runtime app
- export current provider-backed database state to memory snapshot format
- export committed memory operations when a log exists
- replay a snapshot plus log to a target version
- compact snapshot-plus-log storage by writing a new snapshot and retaining/discarding old log segments according to policy
- rewrite to canonical formatting for diffs

Do not add model generation from JSON samples or arbitrary JSON Schema in this command group. That belongs to the skipped JSON document-mapping idea, not JSON memory persistence.

## Diagnostics

JSON persistence errors should point at both the JSON location and the DataLinq metadata location when possible.

Examples:

- `tables.employees[12].emp_no`: duplicate primary key `10001`
- `tables.departments[4].dept_name`: required column is null
- `tables.salaries[9].salary`: expected decimal provider value encoded as string
- `schema.digest`: snapshot was written for a different generated model
- `commits[5].fromVersion`: expected version `47`, got `46`
- `commits[8].operations[2].set.salary`: unsupported provider-value conversion

Diagnostics should include:

- table name
- column name
- row index or commit index
- JSON path-like location
- expected provider type
- actual JSON token type
- strict/compatible mode context
- storage adapter context when a write fails

Do not make users debug malformed store files from raw `JsonException` byte offsets alone.

## Performance Position

JSON persistence is not a high-throughput database engine.

Expected V1 performance target:

- fast enough for small and medium browser/local stores
- predictable load and save behavior
- no accidental per-query JSON parsing
- reasonable allocation profile after load
- deterministic output
- commit-log replay cost visible in diagnostics or tooling

Rejected V1 ambitions:

- streaming query execution over huge JSON files
- partial in-place file updates
- multi-process concurrent writes
- document-store indexing independent of DataLinq metadata

Large-store work can revisit segmented/per-table snapshots and segmented logs after the simple store proves value.

## Verification

Test lanes:

- snapshot reader/writer tests for canonical format
- commit-log reader/writer tests for canonical format
- snapshot-plus-log replay tests
- schema digest and compatibility-mode tests
- scalar/provider-value round-trip tests
- malformed JSON diagnostics tests
- duplicate key and constraint tests
- load into memory state and query through the memory backend
- mutation plus flush behavior
- replay-to-version behavior
- compaction behavior if compaction ships
- strict AOT smoke
- browser/WebAssembly smoke through a storage adapter
- CLI validation/replay tests once CLI exists

The browser smoke should prove:

- open empty memory store with JSON persistence
- seed or import JSON snapshot data
- query loaded rows
- mutate rows
- flush snapshot/log data through the browser storage adapter
- reload and query again
- replay committed changes when log mode is enabled

## Documentation Shape

Public docs should draw clear lines:

- `DataLinq.Memory`: in-process generated-model backend
- JSON memory persistence: DataLinq-owned snapshot/log storage for memory stores
- SQLite in-memory: SQLite provider mode
- SQL JSON columns: JSON values inside SQL provider rows
- arbitrary JSON document mapping: not part of JSON memory persistence

Good wording:

> DataLinq memory stores can optionally persist generated model state to deterministic JSON snapshots and committed mutation logs. The memory backend still owns query execution, transactions, constraints, and replay semantics; JSON is the storage format.

Bad wording:

- "query any JSON file"
- "JSON database"
- "JSON backend"
- "JSONPath LINQ provider"
- "drop-in replacement for SQL"
- "schema-less DataLinq"
- "automatic model generation from JSON"

## Open Questions

- Should the first package be `DataLinq.Memory.Json`, `DataLinq.Persistence.Json`, or a different name?
- Should the V1 snapshot format be single-file only, or should directory/per-table mode be hidden behind an experimental option?
- Should `SnapshotOnly` plus `FlushOnCommit` be the default for all application-style construction paths?
- What is the exact schema digest input: full metadata, storage-relevant metadata, or a versioned subset?
- Should canonical snapshots include empty tables?
- Should row order preserve insertion order, primary-key order, or source file order after mutation?
- Should V1 commit logs be JSON arrays, JSON Lines, segmented files, or storage-adapter-specific?
- How much compatibility loading should V1 attempt before explicit migration tooling exists?
- Should browser storage target localStorage, IndexedDB, OPFS, or an abstraction only in V1?

## Non-Goals

- standalone JSON query backend
- arbitrary existing JSON document mapping
- CLI model generation from JSON or JSON Schema
- JSONPath query execution
- preserving unknown JSON nodes
- preserving comments or whitespace
- per-query JSON file scanning
- partial in-place JSON file updates
- cross-process write coordination
- production-grade document database behavior
- broad arbitrary LINQ support
- raw SQL support
- migration engine for JSON persistence
