> [!WARNING]
> This document is roadmap and design material for the planned DataLinq JSON store backend. It is not normative product documentation and should not be treated as a shipped support claim.

# JSON Store Backend Architecture

**Status:** Draft.

**Created:** 2026-07-03.

## Purpose

The JSON store backend should provide a simple, inspectable, DataLinq-owned persistence format for generated model data.

The most important use cases are:

- browser/WebAssembly applications that need a simple local backing store
- small single-user applications that want readable storage without a database server
- examples and demos that should persist state without SQLite or MySQL
- deterministic test fixtures and repro artifacts
- import/export of DataLinq database state for local workflows

The JSON store backend is deliberately narrower than "DataLinq over any JSON file." It owns the JSON shape. Existing arbitrary JSON document mapping is out of scope for this design.

The blunt rule:

> JSON is storage. DataLinq metadata is schema. Query and mutation semantics still belong to DataLinq.

## Design Thesis

The JSON store should be the persistence companion to the memory backend.

The runtime shape should be:

1. Load a DataLinq-owned JSON store document.
2. Validate its format, schema identity, and table payloads.
3. Materialize provider-value row buffers into memory-like table state.
4. Execute queries through the same backend-neutral `DataLinqQueryPlan` path as the memory backend.
5. Apply mutations to in-process table state.
6. Persist a deterministic JSON store document on commit or explicit flush, according to store options.

This keeps the architecture honest:

- JSON parsing and writing are not query execution
- JSON path expressions are not the query language
- JSON storage does not bypass generated metadata
- JSON persistence does not require SQL generation

## Non-Goal: Arbitrary Existing JSON Documents

Do not mix the JSON store backend with existing arbitrary JSON document mapping.

That separate idea has different problems:

- arrays-as-tables mapping
- path metadata
- schema inference
- sample-based model generation
- preserving unrelated document nodes
- preserving formatting and property order
- partial write-back into nested structures
- polymorphic document shapes

Those are legitimate problems, but they are not the important backend for DataLinq right now. The JSON store backend should get a boring, generated-model persistence story first.

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

The JSON store backend owns the whole persistence file. It should not borrow JSON column vocabulary unless the underlying scalar conversion rules are truly shared.

## AOT And Browser Are First-Class

The JSON store is likely to matter in browser/WebAssembly scenarios after the memory backend exists. It must therefore be designed for AOT from the beginning.

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
- use a persistence abstraction so the same store logic can target filesystem, browser storage, in-memory strings, or future packaged assets

The JSON library decision should be boring: start with `System.Text.Json`. Add another dependency only if a concrete requirement cannot be met cleanly.

## Product Shape

Likely package:

- `DataLinq.JsonStore`

That name is intentionally more specific than `DataLinq.Json`. The latter sounds like JSON column support, arbitrary document mapping, or serializer helpers.

Likely entry points:

```csharp
var db = new JsonStoreDatabase<AppDb>("appdb.datalinq.json");

var store = JsonStore.Open<AppDb>("appdb.datalinq.json", options => options
    .UseStrictSchema()
    .FlushOnCommit());

var db = new JsonStoreDatabase<AppDb>(store);
```

Browser-oriented shape:

```csharp
var store = JsonStore.Open<AppDb>(
    new BrowserJsonStoreStorage("appdb"),
    options => options.FlushOnCommit());

var db = new JsonStoreDatabase<AppDb>(store);
```

Testing/export shape:

```csharp
var snapshot = JsonStoreSnapshot.Create(db);
File.WriteAllText("repro.datalinq.json", snapshot.ToJson());
```

The exact names can change. The important API distinction is:

- database/provider: query and mutation surface
- store: persistence lifecycle, schema compatibility, flush policy, storage target
- storage adapter: filesystem/browser/string transport

## Store Format

V1 should start with one canonical JSON document for the whole DataLinq database state.

That is the right first tradeoff because:

- cross-table consistency is easier
- atomic filesystem replacement is easier
- browser storage is easier
- copying a repro is easier
- human inspection is still reasonable for the intended small-store use cases

Per-table files can come later if large stores, diff ergonomics, or partial-write performance justify the complexity.

Candidate V1 shape:

```json
{
  "$schema": "https://datalinq.org/schemas/datalinq-json-store.v1.schema.json",
  "format": "datalinq-json-store/v1",
  "database": "EmployeesDb",
  "schema": {
    "digest": "sha256:...",
    "mode": "strict"
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

Format rules:

- `format` is required and versioned.
- `database` is the DataLinq database metadata name.
- `schema.digest` identifies the generated metadata shape the store was written against.
- `tables` is keyed by table `DbName`.
- row properties are keyed by column `DbName`.
- missing tables mean empty tables only when the compatibility mode explicitly allows it.
- unknown tables or columns fail in strict mode.
- output ordering is deterministic: manifest fields, tables, rows, and columns should have stable order.

Use database and column `DbName`, not C# property names, as the default store keys. The storage file should survive C# property renames when the underlying DataLinq schema name remains stable.

## Provider Value Encoding

The JSON store must not invent one-off conversion rules.

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
- JSON columns, when supported, should store their raw provider value according to JSON column support rules, not be silently merged into the store document structure.

Because the generated metadata supplies column type, the row payload does not need type tags for every value. Type tags would make the format noisy and would weaken human readability.

## Schema Compatibility

The store should have explicit schema modes:

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

Do not build a full migration engine inside the JSON store. A clear "schema mismatch" error is better than a clever data rewrite nobody asked for.

## Persistence Lifecycle

The JSON store should support two persistence modes:

- `FlushOnCommit`: every committed transaction writes a new store document.
- `ExplicitFlush`: commits update in-process state and mark the store dirty; the caller flushes when appropriate.

Recommended defaults:

- `JsonStoreDatabase<T>` defaults to `FlushOnCommit`.
- fixture/test helpers can default to `ExplicitFlush`.
- browser adapters can choose based on storage cost, but must make the policy visible.

Filesystem atomic write strategy:

1. Serialize to a temporary file in the same directory.
2. Flush the temporary file.
3. Replace the target file atomically where the platform supports it.
4. Leave an actionable failure if replacement cannot be completed.

Browser storage strategy:

- write through a storage adapter
- require adapter-level consistency guarantees to be documented
- do not pretend browser storage has filesystem atomic rename semantics

Cross-process concurrency is not a V1 goal. The store can detect external changes through version, ETag, or last-write token where the storage adapter supports it, but multi-writer coordination should stay out of scope until there is a real use case.

## Query Execution

JSON should not execute queries by scanning JSON syntax on every query.

The expected runtime path:

1. Load JSON into validated row buffers.
2. Build memory-like table and index state.
3. Execute `DataLinqQueryPlan` through the backend-neutral execution boundary.
4. Reuse the memory backend executor where possible.
5. Persist dirty table state back to JSON according to the flush policy.

This makes the JSON backend closer to:

> memory backend plus DataLinq-owned persistence

than:

> document database with JSON path query execution

That is a good thing. The first version should be boring and correct.

## Mutation Semantics

Mutation should follow the memory backend's in-process transaction semantics, plus persistence policy.

Supported mutation behavior:

- insert/update/delete generated mutable rows
- explicit transaction staging
- primary-key uniqueness validation
- strict required/null validation
- strict foreign-key validation where relation metadata is available
- dirty-table tracking
- flush on commit or explicit flush

Durability claim:

- with `FlushOnCommit`, a commit is durable only after the JSON write succeeds
- with `ExplicitFlush`, a commit is not durable until flush succeeds

This distinction must be visible in docs and diagnostics. Hidden durability policy is how users lose data.

## CLI Surface

CLI support should be simple and operational.

Potential commands:

```text
datalinq json-store init
datalinq json-store validate
datalinq json-store export
datalinq json-store import
datalinq json-store rewrite
```

Useful behavior:

- create an empty store from generated metadata
- validate format/schema/row values without loading a runtime app
- export current provider-backed database state to JSON store format
- import JSON store data into a provider-backed database only through explicit command flow
- rewrite to canonical formatting for diffs

Do not add model generation from JSON samples or arbitrary JSON Schema in this command group. That belongs to the skipped JSON document-mapping idea, not the JSON store backend.

## Diagnostics

JSON store errors should point at both the JSON location and the DataLinq metadata location when possible.

Examples:

- `tables.employees[12].emp_no`: duplicate primary key `10001`
- `tables.departments[4].dept_name`: required column is null
- `tables.salaries[9].salary`: expected decimal provider value encoded as string
- `schema.digest`: store was written for a different generated model

Diagnostics should include:

- table name
- column name
- row index
- JSON path-like location
- expected provider type
- actual JSON token type
- strict/compatible mode context

Do not make users debug malformed store files from raw `JsonException` byte offsets alone.

## Performance Position

The JSON store is not a high-throughput database engine.

Expected V1 performance target:

- fast enough for small and medium browser/local stores
- predictable load and save behavior
- no accidental per-query JSON parsing
- reasonable allocation profile after load
- deterministic output

Rejected V1 ambitions:

- streaming query execution over huge JSON files
- partial in-place file updates
- multi-process concurrent writes
- document-store indexing independent of DataLinq metadata

Large-store work can revisit segmented/per-table storage after the simple store proves value.

## Verification

Test lanes:

- parser/writer tests for canonical store format
- schema digest and compatibility-mode tests
- scalar/provider-value round-trip tests
- malformed JSON diagnostics tests
- duplicate key and constraint tests
- load into memory-like state and query through backend execution
- mutation plus flush behavior
- strict AOT smoke
- browser/WebAssembly smoke through a storage adapter
- export/import CLI tests once CLI exists

The browser smoke should prove:

- open empty store
- seed or import JSON store data
- query loaded rows
- mutate rows
- flush data through the browser storage adapter
- reload and query again

## Documentation Shape

Public docs should draw clear lines:

- `DataLinq.Memory`: in-process generated-model backend
- `DataLinq.JsonStore`: DataLinq-owned JSON persistence backend
- SQLite in-memory: SQLite provider mode
- SQL JSON columns: JSON values inside SQL provider rows
- arbitrary JSON document mapping: not part of the JSON store backend

Good wording:

> DataLinq.JsonStore stores generated DataLinq model state in a deterministic JSON document. It loads that state into the DataLinq backend execution pipeline, executes the documented query subset through DataLinq query plans, and writes changes back according to an explicit flush policy.

Bad wording:

- "query any JSON file"
- "JSON database"
- "JSONPath LINQ provider"
- "drop-in replacement for SQL"
- "schema-less DataLinq"
- "automatic model generation from JSON"

## Open Questions

- Should the V1 physical format be single-file only, or should directory/per-table mode be hidden behind an experimental option?
- Should `FlushOnCommit` be the default for every `JsonStoreDatabase<T>` construction path?
- What is the exact schema digest input: full metadata, storage-relevant metadata, or a versioned subset?
- Should canonical output include empty tables?
- Should row order preserve insertion order, primary-key order, or source file order after mutation?
- How much compatibility loading should V1 attempt before explicit migration tooling exists?
- Should browser storage target localStorage, IndexedDB, or an abstraction only in V1?

## Non-Goals

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
- migration engine for JSON stores
