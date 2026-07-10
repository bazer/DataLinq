> [!WARNING]
> This folder contains roadmap and design material for planned JSON serialization and later persistence for the DataLinq memory backend. It is not normative product documentation and should not be treated as a shipped support claim.

# JSON Memory Persistence Design Notes

**Status:** Proposed.

**Created:** 2026-07-03.

**Reframed:** 2026-07-10.

## Current Direction

JSON remains a companion to `DataLinq.Memory`, never a peer query backend.

The only possible 0.9 work is an optional manual snapshot-only import/export prototype after every core 0.9 gate is green. It would serialize a read-only memory store to a DataLinq-owned JSON document and import that document into a fresh read-only store.

It would not provide automatic persistence, durability, storage adapters, browser storage, mutation integration, commit logs, replay, compaction, or CLI tooling.

Those are post-0.9 design directions and depend on memory mutation plus a stable provider-neutral committed-change contract.

## Durable Design Rules

- JSON serializes memory state; it does not execute queries.
- DataLinq metadata defines the snapshot schema.
- Snapshots encode canonical provider CLR values through the shared scalar-conversion metadata, including typed IDs; provider-specific UUID wire codecs remain outside JSON row encoding.
- A manual snapshot codec does not own files, URLs, browser storage, or application lifecycle.
- Persistence policy must remain separate from snapshot encoding.
- Commit logs and replay begin only after successful mutations produce a stable committed-change receipt.
- Arbitrary JSON document mapping, JSONPath querying, model generation from samples, and SQL JSON columns are separate features.

## Documents

- [JSON Persistence Store Architecture](JSON%20Persistence%20Store%20Architecture.md): the long-lived design, split into the optional 0.9 snapshot codec and clearly deferred persistence/replay horizons.

The optional 0.9 implementation plan is [0.9 Memory JSON Snapshot Prototype](../../../../roadmap-implementation/v0.9/Memory%20JSON%20Persistence%20Implementation%20Plan.md).
