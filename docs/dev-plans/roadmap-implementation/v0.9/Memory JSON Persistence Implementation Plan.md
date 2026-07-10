> [!WARNING]
> This document is roadmap implementation material for the DataLinq 0.9 development line. It is not normative product documentation and should not be treated as a shipped support claim.

# 0.9 Memory JSON Snapshot Prototype

**Status:** Proposed.

**Target:** Optional 0.9 stretch candidate only; not part of the baseline.

**Created:** 2026-07-03.

**Reframed:** 2026-07-10.

## Decision

JSON is not part of the 0.9 baseline. If the backend/runtime, scalar conversion, UUID storage, and read-only memory preview are complete and well evidenced, 0.9 may take exactly one JSON stretch:

> An experimental, manual, snapshot-only JSON import/export prototype for read-only memory stores.

This is a codec and interchange proof. It is not automatic persistence, a durability feature, a browser storage system, or a mutation log.

If core 0.9 work slips, cut this entire stretch. Do not cut backend boundaries, conversion correctness, UUID fixes, memory materialization, or AOT/browser execution to keep JSON.

## Purpose

The prototype should answer three narrow questions:

1. Can a generated memory-store snapshot be represented in a deterministic, DataLinq-owned JSON format?
2. Can canonical provider values, including typed-ID conversions and `Guid`, round-trip without JSON-specific model conversion rules or SQL wire encodings?
3. Can a fresh read-only memory store import that snapshot and execute the already-supported query subset?

Arbitrary JSON document mapping, JSONPath execution, SQL JSON columns, and model generation from JSON remain unrelated features.

## Prerequisites And Dependency Direction

This plan begins only after the read-only memory preview has passed its baseline release gates:

- backend-neutral query/source/materialization boundaries are working
- the supported memory query subset is capability-gated
- canonical provider-value buffers materialize into model-valued `RowData`
- scalar converters, typed IDs, and canonical `Guid` values work through memory
- UUID physical codecs are owned and proven by the SQL-provider work rather than copied into memory
- strict AOT and browser execution smokes pass for memory without JSON

The dependency direction is:

```text
completed 0.9 core
       |
       v
read-only memory preview
       |
       v
optional manual JSON snapshot codec
```

JSON has no authority to change memory row representation, query behavior, conversion semantics, or the 0.9 memory release gate.

Memory mutation and transactions are not prerequisites because the prototype serializes a read-only store. Conversely, this prototype is not a prerequisite for later mutation.

## Scope

The optional prototype includes:

- one versioned, whole-store JSON snapshot document
- database and storage-relevant schema identity
- tables keyed by table `DbName`
- row properties keyed by column `DbName`
- canonical provider-value encoding through shared scalar-conversion metadata
- physical storage metadata only where required for strict schema identity
- deterministic output ordering
- strict format and schema validation
- manual export from a read-only memory store
- manual import into a fresh read-only memory store
- actionable format, table, column, row, and JSON-location diagnostics
- query proof over imported rows using only the existing memory capabilities

Manual means the caller explicitly supplies or consumes bytes/streams. The codec does not own files, URLs, browser storage, flush timing, or application lifecycle.

Illustrative shape:

```csharp
await MemoryJsonSnapshot.ExportAsync(store, output, cancellationToken);

var imported = await MemoryJsonSnapshot.ImportAsync<AppDb>(
    input,
    cancellationToken);

using var db = new MemoryDatabase<AppDb>(imported);
```

Exact names remain open. An async surface is appropriate for stream I/O; it does not imply asynchronous memory query execution.

## Explicitly Not In 0.9

- `FlushOnCommit` or `ExplicitFlush` lifecycle integration
- automatic open/load/save behavior
- filesystem persistence ownership or atomic file replacement
- browser storage adapters, IndexedDB, OPFS, or localStorage
- a general storage-adapter abstraction
- mutation persistence
- transaction integration
- canonical commit batches or committed-change receipts
- commit logs
- replay or replay-to-version
- snapshot-plus-log recovery
- compaction or retention policies
- schema migration or compatibility rewrites
- CLI commands
- arbitrary JSON import/mapping
- JSONPath queries
- a claim of durability

These are post-0.9 work and must not creep back in as “small” additions.

## Workstream Ownership

This plan uses local identifiers (`J0` through `J2`) instead of release-wide phase numbers.

| Concern | Owner |
| --- | --- |
| Memory row state, provider buffers, indexes, queries | Memory backend |
| Model-to-canonical-provider conversion, including typed IDs | Shared scalar-conversion pipeline |
| Canonical-provider-to-physical UUID encoding | SQL provider UUID work; never applied to snapshot row tokens |
| Snapshot manifest, JSON tokens, canonical ordering, diagnostics | JSON snapshot prototype |
| Stream/file/browser lifecycle | Caller in 0.9; future persistence layer later |
| Mutation receipts and replay semantics | Future provider-neutral mutation work |

## J0: Snapshot Contract

Define a small `datalinq-memory-snapshot/v1` contract with:

- required format/version
- generated database identity
- storage-relevant schema digest
- deterministic table ordering
- deterministic row ordering, preferably primary-key order where a table has a key
- deterministic column ordering from generated metadata
- strict handling of unknown/missing tables and columns

Candidate shape:

```json
{
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
    ]
  }
}
```

Do not add store versions, commit IDs, flush metadata, or log anchors until mutation/replay provides real semantics for them.

Exit signal:

- the V1 document contract is small, versioned, and documented
- schema digest inputs are storage-relevant and versioned
- the format does not imply support for arbitrary JSON

## J1: Provider-Value Reader And Writer

Work:

- write rows by generated table/column metadata rather than reflection-driven DTO discovery
- encode canonical provider values through the shared scalar-conversion boundary
- cover nulls, booleans, integers, decimals, strings, dates/times, `Guid`, byte arrays, enums, and typed IDs where those types are supported by 0.9
- decode JSON tokens back into canonical provider values
- validate duplicates, missing required values, malformed tokens, and unsupported conversions with metadata context
- avoid per-row runtime code generation and `Expression.Compile()`

The JSON representation is logical even when a SQL wire representation is binary. A canonical `Guid` should use the snapshot's invariant UUID token; the JSON layer must not call the MySQL UUID codec, hardcode a byte order, or otherwise serialize provider wire values. The configured physical format can participate in schema identity without changing the logical row token.

Exit signal:

- representative provider values round-trip deterministically
- typed IDs use shared scalar-conversion metadata
- canonical `Guid` values round-trip without leaking configured SQL text/binary layouts
- malformed values identify JSON path, table, column, row, expected type, and actual token
- model-facing values after import match ordinary memory materialization

## J2: Manual Import/Export Integration

Work:

- export an already-created read-only store to a caller-provided stream or buffer
- import a snapshot into a fresh store built from generated metadata
- perform strict format, database, and schema checks before publishing the store
- build primary-key indexes using the ordinary memory seed/import path
- run primary-key lookup and the supported memory query subset over imported state
- add cancellation and partial-read/write tests for stream APIs
- keep storage lifecycle entirely outside the codec

Exit signal:

- a small store exports and imports without a database connection
- imported and directly seeded stores produce equivalent model values and keys
- the snapshot is deterministic across repeated exports of identical state
- failed import never publishes a partially initialized store
- no query scans JSON text after import

## Verification Gates

The optional prototype may ship only with:

- canonical snapshot golden tests
- deterministic ordering tests
- strict database/schema mismatch tests
- provider-value round-trip tests
- typed-ID and canonical-`Guid` round-trip tests
- regression tests proving configured UUID physical formats do not become snapshot wire encodings
- malformed JSON and row diagnostic tests
- duplicate-key and missing-value import tests
- direct-seed versus imported-store query comparisons
- strict AOT-compatible reader/writer coverage
- explicit documentation that the caller owns transport and storage

Browser storage is not a gate. The baseline memory browser smoke must remain independent of JSON.

## Release Boundary And Cut Rule

If this stretch ships, the claim should be:

> DataLinq 0.9 includes an experimental manual JSON snapshot codec for importing and exporting read-only `DataLinq.Memory` state.

The claim must also say:

- snapshot-only
- manual invocation
- no mutation integration
- no automatic persistence or durability guarantee
- no browser storage integration
- no arbitrary JSON mapping

If implementation requires a persistence lifecycle, storage adapters, commit semantics, replay, or CLI support, stop and move that work to the post-0.9 plan.

## Post-0.9 Directions

After memory mutation and a provider-neutral committed-change receipt exist, a separate persistence plan may evaluate:

1. explicit save/load lifecycle
2. filesystem and browser storage adapters
3. atomic replacement and adapter consistency contracts
4. automatic or explicit flush policies
5. snapshot-plus-log formats
6. committed-change logs and deterministic replay
7. compaction and retention
8. schema import/migration tooling
9. focused CLI validation and operational commands

Those items should be owned by the persistence workstream, not folded into mutation implementation.

## Claims To Avoid

- "JSON persistence" without the `manual snapshot prototype` qualifier
- "durable memory database"
- "browser persistence"
- "JSON database" or "JSON backend"
- "query any JSON file"
- "event sourcing"
- "automatic model generation from JSON"
- "drop-in SQL replacement"

## Links

- [JSON Memory Persistence Design Notes](../../backends/memory/persistence/json/README.md)
- [JSON Persistence Store Architecture](../../backends/memory/persistence/json/JSON%20Persistence%20Store%20Architecture.md)
- [Memory Backend Architecture](../../backends/memory/Architecture.md)
- [0.9 Read-Only Memory Backend Implementation Plan](In-Memory%20Database%20Implementation%20Plan.md)
- [DataLinq 0.9 Roadmap](README.md)
