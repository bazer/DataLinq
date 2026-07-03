> [!WARNING]
> This document is roadmap implementation material for the DataLinq 0.9 development line. It is not normative product documentation and should not be treated as a shipped support claim.

# 0.9 JSON Store Implementation Plan

**Status:** Draft.

**Created:** 2026-07-03.

## Purpose

This document keeps the immediate 0.9 implementation plan for the planned JSON store backend.

The durable architecture lives in [JSON Store Backend Architecture](../../backends/json/Store%20Backend%20Architecture.md). Keep broad design discussion there. Keep this page focused on sequencing, exit criteria, release boundaries, and what must be true before 0.9 can honestly claim a JSON store backend.

Arbitrary existing JSON document mapping and CLI model generation from JSON are intentionally out of scope.

## 0.9 Goal

The 0.9 JSON store work should prove that DataLinq can persist generated model state in a deterministic, human-readable JSON format while keeping query and mutation semantics inside DataLinq.

The first release claim should stay narrow:

> DataLinq 0.9 introduces an experimental JSON store backend for generated models, using a DataLinq-owned JSON format and the backend-neutral query execution path.

Strengthen that wording only if evidence earns it.

## Prerequisites

JSON store work should start after:

- the backend execution contract exists
- the memory backend can execute the first query subset
- scalar/provider-value conversion is centralized enough to prevent JSON-specific one-off conversion rules

Starting before those prerequisites risks building a stringly JSON side path that will not rhyme with DataLinq.

## Phase 7A: Store Format And Reader/Writer

Work:

- define `datalinq-json-store/v1`
- choose the V1 physical shape, with single-document store as the preferred baseline
- add manifest fields for format, database name, schema digest, and tables
- use table `DbName` and column `DbName` as storage keys
- write deterministic JSON
- parse store JSON into validated row payloads
- report malformed store data with table, column, row index, and JSON location context

Exit signal:

- empty stores can be created from generated metadata
- small stores can round-trip without runtime database access
- malformed JSON and malformed row values produce actionable diagnostics
- the format does not claim support for arbitrary JSON documents

## Phase 7B: Provider-Value Encoding

Work:

- route JSON encoding through the scalar/provider-value conversion boundary
- cover nulls, booleans, integral values, decimals, strings, dates/times, GUIDs, byte arrays, enums, and typed IDs where supported
- add explicit unsupported diagnostics for values the conversion boundary cannot encode
- add round-trip tests across representative column types

Exit signal:

- JSON persistence does not invent table-local conversion rules
- supported provider values round-trip deterministically
- unsupported conversion cases fail before data is silently corrupted

## Phase 7C: Store Backend Foundation

Work:

- create `DataLinq.JsonStore` or equivalent
- create `JsonStoreDatabase<TDatabase>` and `JsonStore<TDatabase>` or equivalent
- load JSON store data into memory-like table state
- reuse the backend-neutral query executor where possible
- support primary-key lookup after load
- support strict schema digest validation
- keep raw SQL unsupported with a clear provider capability error

Exit signal:

- generated model rows can be loaded from JSON and fetched without SQL
- primary-key lookup works after reload
- schema mismatch errors are clear
- JSON store support stays distinct from SQL JSON column support

## Phase 8A: JSON Query Proof

Work:

- run the memory-supported query subset against loaded JSON store state
- add parity tests against memory and SQLite for the supported slice
- prove direct scalar and anonymous projection where the memory backend supports it
- prove unsupported shape diagnostics

Exit signal:

- the JSON store backend proves the backend-neutral query path can execute persisted data
- no query path scans JSON text per execution
- unsupported query shapes do not fall back to LINQ-to-Objects by accident

## Phase 8B: JSON Mutation And Flush

Work:

- insert/update/delete generated mutable rows
- track dirty tables/state
- support `FlushOnCommit`
- support `ExplicitFlush`
- write canonical JSON on flush
- use atomic filesystem replacement where available
- expose storage-adapter consistency limits in diagnostics/docs

Exit signal:

- common mutation workflows persist and reload
- `FlushOnCommit` and `ExplicitFlush` have tested, documented durability behavior
- failed writes do not pretend commit durability succeeded
- browser storage can use the same lifecycle through an adapter

## Phase 8C: CLI And Browser Smoke

Work:

- add focused CLI commands if the backend is ready for tooling:
  - `json-store init`
  - `json-store validate`
  - `json-store export`
  - `json-store rewrite`
- add browser/WebAssembly smoke through a storage adapter
- add strict AOT smoke
- add docs that clearly separate JSON store from arbitrary JSON documents and SQL JSON columns

Exit signal:

- a JSON store can be created and validated outside an application runtime
- browser smoke can load, query, mutate, flush, reload, and query again
- public wording stays experimental unless durability, browser storage, and schema behavior are proven strongly enough

## Verification Gates

The JSON store backend should not be called supported until these are green:

- format reader/writer tests
- canonical output tests
- schema digest validation tests
- provider-value round-trip tests
- malformed JSON diagnostics tests
- strict constraint tests
- load/query parity tests against memory for the supported query subset
- mutation plus flush/reload tests
- strict AOT smoke
- browser/WebAssembly smoke through a storage adapter
- CLI validation tests if CLI commands ship

## Release Boundary

The 0.9 release can claim a JSON store backend only when:

- the store uses a DataLinq-owned, versioned JSON format
- the provider starts from generated metadata
- store values encode through the provider-value conversion boundary
- primary-key lookup and a documented query subset execute without SQL
- mutation persistence semantics are explicit
- strict schema mismatch diagnostics are actionable
- browser/WebAssembly smoke passes if browser support is claimed
- public docs do not blur JSON store, arbitrary JSON document mapping, and SQL JSON column support

Claims to avoid unless proven:

- "query any JSON file"
- "JSON database"
- "document database"
- "JSONPath LINQ provider"
- "automatic model generation from JSON"
- "drop-in SQL replacement"
- "production-grade multi-user persistence"

## Links

- [JSON Store Backend Design Notes](../../backends/json/README.md)
- [JSON Store Backend Architecture](../../backends/json/Store%20Backend%20Architecture.md)
- [DataLinq 0.9 Rough Roadmap](README.md)
