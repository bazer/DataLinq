> [!WARNING]
> This document is roadmap implementation material for the DataLinq 0.9 development line. It is not normative product documentation and should not be treated as a shipped support claim.

# 0.9 Memory JSON Persistence Implementation Plan

**Status:** Draft.

**Created:** 2026-07-03.

**Reframed:** 2026-07-04.

## Purpose

This document keeps the immediate 0.9 implementation plan for JSON persistence over the planned memory backend.

The durable architecture lives in [JSON Persistence Store Architecture](../../backends/memory/persistence/json/JSON%20Persistence%20Store%20Architecture.md). Keep broad design discussion there. Keep this page focused on sequencing, exit criteria, release boundaries, and what must be true before 0.9 can honestly claim JSON persistence for memory stores.

Arbitrary existing JSON document mapping, JSONPath-backed table mapping, and CLI model generation from JSON are intentionally out of scope.

## 0.9 Goal

The 0.9 JSON persistence work should prove that `DataLinq.Memory` can persist generated model state in a deterministic, human-readable JSON format while keeping query and mutation semantics inside the memory backend.

The first release claim should stay narrow:

> DataLinq 0.9 introduces experimental JSON persistence for memory stores, using DataLinq-owned snapshot and optional commit-log formats.

Strengthen that wording only if evidence earns it.

## Prerequisites

JSON persistence work should start after:

- the backend execution contract exists
- the memory backend can execute the first query subset
- scalar/provider-value conversion is centralized enough to prevent JSON-specific one-off conversion rules, including typed-ID provider values
- memory snapshots have a stable enough internal shape to export/import

Commit-log persistence should start after:

- memory mutation exists
- successful memory commits produce a canonical committed operation batch in provider-value form
- replay rules for generated keys, defaults, clocks, and constraints are explicit

Starting before those prerequisites risks building a stringly JSON side path that will not rhyme with DataLinq.

## Phase 7A: Persistence Boundary And Storage Adapters

Work:

- define the memory-store persistence boundary
- keep persistence configuration on `MemoryDatabaseStore<TDatabase>` or equivalent
- define content modes:
  - `SnapshotOnly`
  - `CommitLogOnly`
  - `SnapshotWithCommitLog`
- define flush policies:
  - `FlushOnCommit`
  - `ExplicitFlush`
- add storage-adapter abstractions for filesystem, browser storage, and in-memory string/blob tests
- make storage-adapter consistency limits visible in diagnostics

Exit signal:

- memory stores can be constructed with no persistence, JSON snapshot persistence, or future persistence adapters without changing query execution
- public naming does not imply a standalone JSON backend
- storage failures do not masquerade as memory query failures

## Phase 7B: Snapshot Format And Reader/Writer

Work:

- define `datalinq-memory-snapshot/v1`
- choose the V1 physical shape, with single-document snapshot as the preferred baseline
- add manifest fields for format, database name, schema digest, store version, and tables
- use table `DbName` and column `DbName` as storage keys
- write deterministic JSON
- parse snapshot JSON into validated row payloads
- report malformed snapshot data with table, column, row index, and JSON location context

Exit signal:

- empty memory snapshots can be created from generated metadata
- small memory stores can round-trip through snapshots without runtime database access
- malformed JSON and malformed row values produce actionable diagnostics
- the format does not claim support for arbitrary JSON documents

## Phase 7C: Provider-Value Encoding

Work:

- route JSON encoding through the scalar/provider-value conversion boundary
- cover nulls, booleans, integral values, decimals, strings, dates/times, GUIDs, byte arrays, enums, and typed IDs where supported
- add explicit unsupported diagnostics for values the conversion boundary cannot encode
- add round-trip tests across representative column types

Exit signal:

- JSON persistence does not invent table-local conversion rules
- supported provider values round-trip deterministically
- unsupported conversion cases fail before data is silently corrupted

## Phase 7D: Snapshot Persistence Integration

Work:

- create `DataLinq.Memory.Json`, `DataLinq.Persistence.Json`, or equivalent
- load JSON snapshots into memory table state
- support primary-key lookup after load
- support strict schema digest validation
- support `SnapshotOnly`
- support `FlushOnCommit`
- support `ExplicitFlush`
- write canonical snapshots on flush
- use atomic filesystem replacement where available

Exit signal:

- generated model rows can be loaded from JSON into memory and fetched without SQL
- primary-key lookup works after reload
- schema mismatch errors are clear
- JSON snapshot support stays distinct from SQL JSON column support
- failed snapshot writes do not pretend commit durability succeeded

## Phase 8A: Memory Query Proof Over Persisted State

Work:

- run the memory-supported query subset against state loaded from JSON snapshots
- add parity tests against non-persisted memory and SQLite for the supported slice
- prove direct scalar and anonymous projection where the memory backend supports it
- prove unsupported shape diagnostics

Exit signal:

- JSON persistence proves the memory backend can execute persisted data
- no query path scans JSON text per execution
- unsupported query shapes do not fall back to LINQ-to-Objects by accident

## Phase 8B: Mutation Commit Batches

Work:

- make successful memory commits produce canonical committed operation batches
- capture insert/update/delete operations in provider-value form
- include from-version and to-version information
- include table and column `DbName` identities
- keep failed transactions and attempted object mutations out of the committed log shape
- add deterministic tests for committed batch capture

Exit signal:

- mutation improvements expose a clean operation-batch artifact
- commit batches can describe replayable store-state changes without JSON-specific hooks
- relation/index invalidation still follows the memory commit boundary

## Phase 8C: Commit Log And Replay

Work:

- define `datalinq-memory-commit-log/v1`
- write committed operation batches to JSON
- load and validate commit logs
- replay logs from an empty store, seed snapshot, or latest snapshot
- support replay to a target version
- report log/version/schema errors with actionable diagnostics
- keep `CommitLogOnly` experimental unless startup, compaction, and seed-state rules are proven

Exit signal:

- committed memory mutations can be replayed deterministically
- snapshot-plus-log recovery works for ordinary mutation flows
- replay fails clearly on schema mismatch or version gaps
- log persistence is visibly a memory persistence mode, not a separate backend

## Phase 8D: CLI And Browser Smoke

Work:

- add focused CLI commands if the persistence layer is ready for tooling:
  - `memory json init`
  - `memory json validate`
  - `memory json export-snapshot`
  - `memory json export-log`
  - `memory json replay`
  - `memory json compact`
  - `memory json rewrite`
- add browser/WebAssembly smoke through a storage adapter
- add strict AOT smoke
- add docs that clearly separate JSON memory persistence from arbitrary JSON documents and SQL JSON columns

Exit signal:

- a memory snapshot can be created and validated outside an application runtime
- browser smoke can load, query, mutate, flush, reload, and query again
- log mode can replay committed changes if log mode is included in the release claim
- public wording stays experimental unless durability, browser storage, replay, and schema behavior are proven strongly enough

## Verification Gates

JSON persistence should not be called supported until these are green:

- persistence-boundary unit tests
- storage-adapter failure tests
- snapshot reader/writer tests
- canonical snapshot output tests
- schema digest validation tests
- provider-value round-trip tests
- malformed JSON diagnostics tests
- strict constraint tests
- load/query parity tests against memory for the supported query subset
- mutation plus flush/reload tests
- commit-batch capture tests if mutation ships
- commit-log reader/writer tests if log mode ships
- snapshot-plus-log replay tests if log mode ships
- replay-to-version tests if log mode ships
- strict AOT smoke
- browser/WebAssembly smoke through a storage adapter
- CLI validation/replay tests if CLI commands ship

## Release Boundary

The 0.9 release can claim JSON memory persistence only when:

- persistence is configured through the memory store
- the store uses a DataLinq-owned, versioned JSON snapshot format
- values encode through the provider-value conversion boundary
- primary-key lookup and a documented query subset execute through `DataLinq.Memory`
- mutation persistence semantics are explicit
- strict schema mismatch diagnostics are actionable
- browser/WebAssembly smoke passes if browser support is claimed
- public docs do not blur JSON memory persistence, arbitrary JSON document mapping, SQL JSON column support, and standalone backends

The release can claim commit-log replay only when:

- successful commits produce canonical operation batches
- the log format is versioned
- snapshot-plus-log replay is deterministic
- version gaps and schema mismatches fail clearly
- compaction/retention rules are documented or explicitly out of scope

Claims to avoid unless proven:

- "query any JSON file"
- "JSON database"
- "JSON backend"
- "document database"
- "JSONPath LINQ provider"
- "automatic model generation from JSON"
- "drop-in SQL replacement"
- "production-grade multi-user persistence"
- "event sourcing framework"

## Links

- [JSON Memory Persistence Design Notes](../../backends/memory/persistence/json/README.md)
- [JSON Persistence Store Architecture](../../backends/memory/persistence/json/JSON%20Persistence%20Store%20Architecture.md)
- [Memory Backend Architecture](../../backends/memory/Architecture.md)
- [DataLinq 0.9 Rough Roadmap](README.md)
