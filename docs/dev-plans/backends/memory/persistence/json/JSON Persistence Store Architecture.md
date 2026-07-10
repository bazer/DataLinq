> [!WARNING]
> This document is roadmap and design material for planned JSON serialization and later persistence for the DataLinq memory backend. It is not normative product documentation and should not be treated as a shipped support claim.

# JSON Persistence Store Architecture

**Status:** Proposed.

**Release scope:** Only the manual snapshot codec is an optional 0.9 stretch; persistence and replay remain later work.

**Created:** 2026-07-03.

**Reframed:** 2026-07-10.

## Purpose

JSON should provide an inspectable, DataLinq-owned representation of `DataLinq.Memory` state.

The immediate opportunity is a small snapshot codec, not a durable database. Automatic persistence, mutation logging, replay, storage adapters, and browser lifecycle integration all have additional failure semantics and dependencies. They should not hitchhike into 0.9 behind the word “JSON.”

The blunt rule:

> JSON serializes memory state. `DataLinq.Memory` executes queries. Neither layer owns the other's semantics.

## Release Horizons

| Horizon | Intended scope |
| --- | --- |
| Optional 0.9 stretch | Manual import/export of one versioned, whole-store snapshot for a read-only memory store |
| Post-0.9 persistence | Explicit open/save lifecycle, storage adapters, automatic/explicit flush policy, filesystem and browser consistency contracts |
| Later replay tooling | Provider-neutral committed-change receipts, JSON commit logs, deterministic replay, compaction, retention, CLI operations |

Only the first row is eligible for 0.9, and only after all core release gates pass.

## Design Thesis

JSON is a companion format, not a query backend.

The optional 0.9 flow is:

1. Build a read-only memory store from generated metadata and seed data.
2. Explicitly export its canonical provider-value rows to a caller-owned stream or buffer.
3. Explicitly import a snapshot into a fresh read-only memory store.
4. Build the ordinary memory indexes.
5. Query through the existing memory `DataLinqQueryPlan` executor.

The JSON codec owns:

- format and manifest parsing/writing
- stable table, row, and column ordering
- JSON-token encoding of canonical provider CLR values
- schema identity validation
- JSON-path-aware diagnostics

The memory backend owns:

- provider-value row buffers
- key normalization and indexes
- model-value materialization
- capabilities and query execution
- future mutation and transaction behavior

The caller owns transport and lifecycle in 0.9:

- files
- network streams
- packaged resources
- browser storage
- save timing
- replacement and backup policy

That last boundary is what keeps a manual codec from making accidental durability promises.

## Non-Goal: Arbitrary JSON Documents

This format does not map arbitrary existing JSON documents.

That separate feature would need:

- arrays-as-tables and path metadata
- schema inference
- sample- or schema-based model generation
- preservation of unrelated nodes and formatting
- partial write-back into nested structures
- polymorphic document rules

The snapshot codec owns its document shape. It does not execute JSONPath, preserve unknown application JSON, or generate models from samples.

## Non-Goal: SQL JSON Columns

SQL JSON columns are also separate. They concern a single model property stored as a JSON value inside a SQLite, MySQL, or MariaDB row and may eventually support provider-native JSON functions.

A memory snapshot describes the whole logical memory store. It must not borrow SQL JSON-column query semantics or merge a JSON-valued column into the snapshot manifest.

## Optional 0.9 Product Shape

The smallest honest API accepts caller-owned I/O:

```csharp
await MemoryJsonSnapshot.ExportAsync(
    store,
    outputStream,
    cancellationToken);

var importedStore = await MemoryJsonSnapshot.ImportAsync<AppDb>(
    inputStream,
    cancellationToken);
```

Possible package names include:

- `DataLinq.Memory.Json`
- `DataLinq.Persistence.Json`

For 0.9, `DataLinq.Memory.Json` is the clearer signal because the prototype encodes one backend's state and does not yet establish a general persistence abstraction. A top-level `DataLinq.JsonStore` name would wrongly suggest a peer provider or document database.

The codec API should not accept a file path, browser-storage key, flush option, or persistence policy in 0.9. Applications can compose stream/file APIs themselves while the format is experimental.

## Snapshot Format

V1 should use one canonical JSON document for the whole read-only store.

Candidate shape:

```json
{
  "$schema": "https://datalinq.org/schemas/datalinq-memory-snapshot.v1.schema.json",
  "format": "datalinq-memory-snapshot/v1",
  "database": "EmployeesDb",
  "schema": {
    "digest": "sha256:..."
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
        "last_name": "Facello"
      }
    ]
  }
}
```

V1 rules:

- `format` is required and versioned.
- `database` identifies the generated database metadata.
- `schema.digest` covers a versioned, storage-relevant metadata projection.
- `tables` uses table `DbName` keys.
- rows use column `DbName` keys.
- table and column order follows generated metadata.
- keyed rows use primary-key order for deterministic export where all key components have a defined canonical comparer.
- keyless table ordering must be explicitly defined or the table must be rejected by the prototype.
- strict import rejects unknown/missing tables and columns unless the V1 contract explicitly permits omitted empty tables.
- duplicate primary keys and invalid values fail before the store becomes visible.

The 0.9 document does not need:

- store versions
- commit IDs
- transaction timestamps
- log anchors
- flush state
- adapter metadata

Adding fields without real semantics produces future compatibility debt for no benefit.

## Canonical Provider-Value Encoding

The snapshot stores a logical memory-store representation. It serializes canonical provider CLR values, not provider-specific SQL wire values.

The layers remain distinct:

```text
model CLR value
    <-> canonical provider CLR value
    <-> provider physical/wire representation
```

Examples:

- a typed `CustomerId` may encode as its canonical provider `int`
- a `Guid` should normally encode as an invariant UUID string in the snapshot
- a MySQL `BINARY(16)` byte layout remains a SQL provider codec concern
- a MariaDB native UUID representation remains a provider concern

The snapshot schema digest may include UUID storage metadata because it is part of the model's storage contract, but the JSON writer must not emit MySQL connector bytes merely because the SQL column uses `BINARY(16)`. Import reconstructs canonical `Guid`; the SQL provider applies its physical codec later if that logical row is ever written to SQL.

Initial JSON token rules should be explicit and round-trippable:

| Canonical provider value | Candidate JSON representation |
| --- | --- |
| `null` | JSON `null` |
| `bool` | JSON boolean |
| bounded integral types | JSON number when exact round-trip is proven |
| `decimal` | invariant string unless a canonical numeric policy is proven |
| `string` | JSON string |
| `Guid` | lowercase dashed invariant string |
| `DateOnly`, `TimeOnly`, `DateTime`, `DateTimeOffset` | documented invariant strings |
| `byte[]` | base64 string |
| enum-backed values | their configured canonical provider value |
| typed IDs | their scalar converter's canonical provider value |

The codec must call shared scalar conversion metadata when the input is model-valued and must validate canonical provider types on import. It must not invent table-local conversions.

## Reader And Writer Shape

`System.Text.Json` is the default implementation choice.

Recommended shape:

- `Utf8JsonReader` for controlled import and useful token/path context
- `Utf8JsonWriter` for stable canonical output
- source-generated serializer contexts only for small stable manifest DTOs if they simplify code
- generated/runtime-owned table and column metadata for rows
- no reflection-based serialization of model instances
- no runtime code generation or compiled expressions

Import should stage all parsed state privately:

1. parse and validate the manifest
2. resolve generated metadata
3. parse each token into the expected canonical provider CLR type
4. build memory provider rows
5. validate primary keys and build indexes
6. publish the completed read-only store

Malformed input must never expose a partially initialized store.

Export should read the memory provider rows directly. It should not materialize every generated model merely to serialize it, and it should not re-run SQL physical codecs.

## Schema Identity

V1 should implement strict matching only.

The digest input needs a versioned definition that includes at least:

- database storage identity
- table storage names
- column storage names and ordinals
- canonical provider CLR types
- nullability
- primary-key shape and ordering
- scalar converter identity/version where required for stable interpretation
- physical storage metadata only where changing it changes the declared generated model contract

Safe additive loading, renames, provider-type changes, converter changes, and key-shape changes belong to explicit later migration/import tooling.

A clear schema-mismatch error is safer than a clever silent rewrite.

## Query Execution

JSON never executes queries.

After import:

1. the JSON reader is finished
2. the in-memory provider rows and indexes exist
3. the memory backend executes its normal documented plan subset
4. model materialization converts canonical provider values to model-valued `RowData`

No query scans JSON text, uses JSONPath, or depends on property ordering in the source document.

## AOT And Browser Position

The codec should be AOT-safe by construction:

- no reflection-based model serialization
- no `Expression.Compile()`
- no runtime code generation
- no native dependencies
- no filesystem assumption in the codec

That does not make browser persistence a 0.9 feature. A caller could theoretically pass a browser-provided stream or byte buffer, but IndexedDB/OPFS/localStorage integration, consistency, quota, lifecycle, and recovery are unclaimed.

The memory backend's browser execution smoke must pass without this package. The optional JSON prototype may have strict AOT codec tests, but it must not reopen the core browser release gate.

## Diagnostics

Import failures should identify both JSON and DataLinq metadata context where possible.

Examples:

- `tables.employees[12].emp_no`: duplicate primary key `10001`
- `tables.departments[4].dept_name`: required canonical provider value is null
- `tables.salaries[9].salary`: expected invariant decimal string, got JSON object
- `schema.digest`: snapshot targets a different generated storage schema
- `tables.orders[3].customer_id`: scalar provider value is outside the target integer range

Diagnostics should include:

- snapshot format/version
- JSON path-like location
- table and column names
- row index
- expected canonical provider type
- actual token type or value summary
- schema/database identity where relevant

Raw `JsonException` offsets alone are inadequate.

## 0.9 Verification

The optional prototype requires:

- canonical/golden snapshot tests
- deterministic output tests
- schema digest tests
- provider-value token round trips
- typed-ID round trips through canonical provider values
- UUID tests proving snapshots remain logical values rather than SQL byte-layout dumps
- malformed input diagnostics
- duplicate-key and required-value rejection
- partial stream and cancellation tests
- directly seeded versus imported-store lookup/query comparisons
- strict AOT-compatible reader/writer smoke

It does not require:

- browser storage tests
- filesystem atomicity tests
- flush/reload mutation tests
- transaction tests
- commit-log or replay tests
- CLI tests

## Post-0.9: Persistence Boundary

Automatic or lifecycle-aware persistence begins only after the snapshot codec and memory backend have stable ownership boundaries.

A future persistence abstraction may own:

- opening/loading a store
- explicit save/flush
- dirty-state integration after mutation exists
- transport adapters
- consistency tokens
- backup/replace policy
- diagnostics that distinguish memory commit failures from persistence failures

Transport adapters may later target:

- filesystem streams
- in-memory bytes/strings for tests
- IndexedDB or OPFS
- application-provided remote/object storage

Each adapter must document atomicity, consistency, overwrite, concurrency, quota, and recovery behavior. A generic `IStorageAdapter` with no meaningful guarantees would merely hide data-loss semantics behind an interface.

## Post-0.9: Flush And Durability Semantics

Flush policy is meaningless until memory mutation has a stable commit boundary.

Future designs may distinguish:

- explicit snapshot save: the caller requests serialization of current committed state
- flush after commit: persistence succeeds before the application receives a durable-commit result
- asynchronous/background flush: memory commit succeeds first and persistence is eventual

These modes have materially different failure behavior. They must not share vague “auto-save” wording.

`FlushOnCommit` is not automatically the safest default. Coordinating an in-memory root swap with an external write can require prepare/finalize or compensation semantics. That design should be made only after real mutation and adapter contracts exist.

## Post-0.9: Committed Changes, Logs, And Replay

Commit logging depends on a provider-neutral committed-change receipt owned by the mutation layer. JSON should serialize that receipt; it should not define what a successful DataLinq mutation means.

A future log may include:

- format and schema identity
- from/to committed version
- ordered insert/update/delete operations
- table and column storage names
- canonical provider values for keys and changes
- optional caller-provided timestamp/source metadata

It must exclude:

- failed transactions
- setter calls that never committed
- cache events
- arbitrary application commands
- timing-dependent re-execution of user code

Only after deterministic receipt replay works should the project choose among:

- JSON array logs
- JSON Lines
- segmented logs
- snapshot-plus-log checkpoints
- replay to a target version
- compaction and retention

Commit-log-only startup is especially risky because generated values, defaults, schema evolution, base-state identity, and unbounded replay cost all require answers. It should not be the default merely because event logs sound sophisticated.

## Post-0.9: CLI And Operational Tooling

CLI work follows a stable format and lifecycle. Possible later commands include validation, canonical rewrite, snapshot export/import, replay, and compaction.

Do not build 0.9 commands for:

- persistence initialization
- log export
- replay
- compaction
- browser storage management
- model generation from arbitrary JSON

Tooling before semantics would fossilize an unstable format and multiply compatibility obligations.

## Performance Position

JSON snapshots are not a high-throughput query or database engine.

Reasonable prototype goals:

- deterministic output
- predictable small/medium snapshot load and save
- no per-query JSON parsing
- no forced model materialization during export
- bounded diagnostics overhead on success paths

Rejected ambitions:

- streaming queries over large JSON files
- partial in-place updates
- multi-process coordination
- independent document-store indexes
- high-frequency flush-on-commit durability

Later measurements can justify segmented or per-table snapshots. V1 should not pre-design them.

## Documentation Shape

Public documentation must distinguish:

- `DataLinq.Memory`: the query backend
- manual JSON snapshot codec: optional logical state import/export
- future JSON persistence: unshipped lifecycle and durability layer
- SQLite in-memory: SQLite provider mode
- SQL JSON columns: JSON values inside provider rows
- arbitrary JSON document mapping: not this design

Good optional-0.9 wording:

> DataLinq includes an experimental manual codec for exporting and importing read-only memory-store snapshots in a versioned JSON format. The caller owns storage and lifecycle; the codec provides no automatic persistence or durability guarantee.

Avoid:

- "JSON database"
- "JSON backend"
- "browser persistence"
- "durable memory store"
- "query any JSON file"
- "event sourcing framework"
- "drop-in SQL replacement"

## Open Questions

- Is `DataLinq.Memory.Json` the right prototype package name?
- Which metadata fields form the versioned storage-schema digest?
- Must all V1 tables have primary keys to guarantee canonical row ordering?
- Should empty tables be emitted or may they be omitted under strict mode?
- Should decimals always use strings in V1?
- Should snapshot import accept only a fresh store, or can a later explicit API replace an existing read-only store atomically?
- What evidence would justify promoting manual serialization into lifecycle-aware persistence?
- Which committed-change primitive can later serve memory, audit events, CDC, and other backends without becoming memory-specific?

## Non-Goals

- standalone JSON query backend
- arbitrary JSON document mapping
- model generation from JSON or JSON Schema
- JSONPath query execution
- preserving unknown document nodes, comments, whitespace, or property order
- SQL JSON-column behavior
- per-query JSON scanning
- mutation integration in 0.9
- automatic load/save or flush policy in 0.9
- filesystem or browser storage adapters in 0.9
- commit logs, replay, or compaction in 0.9
- CLI tooling in 0.9
- production durability claims
- cross-process write coordination
- schema migration engine
