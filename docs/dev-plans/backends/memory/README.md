> [!WARNING]
> This folder contains roadmap and design material for the planned DataLinq memory backend. It is not normative product documentation and should not be treated as a shipped support claim.

# Memory Backend Design Notes

**Status:** Accepted.

**Created:** 2026-07-03.

**Reframed:** 2026-07-10.

This folder contains durable design notes for `DataLinq.Memory`.

## Current Direction

The immediate target is deliberately narrow:

> DataLinq 0.9 should ship, at most, an experimental read-only memory preview that starts from generated metadata, accepts explicit seed data, supports primary-key lookup and a small capability-gated query subset, and proves AOT/browser execution without SQL.

The preview is not a transactional database, a SQL emulator, or the default replacement for provider-backed tests. Its job is to prove the backend-neutral query and materialization architecture.

Post-0.9 design may add mutation, transactions, store forks, committed-change receipts, persistence, and replay. Those remain design directions, not part of the 0.9 release boundary.

## Durable Design Rules

- `DataLinqQueryPlan` execution must not require SQL generation or parsing.
- Unsupported query shapes fail through explicit backend capabilities.
- Memory rows store canonical provider CLR values by ordinal.
- Materialization converts those buffers into model-valued `RowData`; provider values must not leak into model-facing APIs.
- Generated metadata and accessors are the normal startup and hot path.
- No `Expression.Compile()`, runtime code generation, broad reflection fallback, or unrestricted LINQ-to-Objects escape hatch belongs in query execution.
- Memory semantics are documented in their own right. Matching a few SQLite results does not prove SQL semantic parity.
- Provider-backed tests remain necessary for SQL translation, schema, collation, constraints, concurrency, and transaction behavior.
- Persistence formats attach to memory state; they do not become peer query backends.

## Documents

- [Architecture](Architecture.md): the long-lived architecture, with the 0.9 preview separated from post-0.9 mutation and persistence directions.
- [JSON Memory Persistence](persistence/json/README.md): durable notes for JSON snapshot encoding and later persistence work.

The immediate implementation plans are:

- [0.9 Read-Only Memory Backend Implementation Plan](../../roadmap-implementation/v0.9/In-Memory%20Database%20Implementation%20Plan.md)
- [0.9 Memory JSON Snapshot Prototype](../../roadmap-implementation/v0.9/Memory%20JSON%20Persistence%20Implementation%20Plan.md)
