> [!WARNING]
> This folder contains roadmap and design material for planned JSON persistence for the DataLinq memory backend. It is not normative product documentation and should not be treated as a shipped support claim.

# JSON Memory Persistence Design Notes

**Status:** Draft collection.

**Created:** 2026-07-03.

This folder collects durable design notes for planned JSON persistence stores for `DataLinq.Memory`.

Use this folder for design decisions that should outlive one implementation phase:

- DataLinq-owned JSON snapshot and commit-log formats
- AOT and browser/WebAssembly constraints
- provider-value encoding and scalar conversion
- persistence lifecycle, flush policy, and atomic write behavior
- schema digest and compatibility behavior
- replayability and commit-log compaction
- CLI import/export/validation surfaces

Use versioned roadmap folders such as `../../../../roadmap-implementation/v0.9/` for phase sequencing, immediate exit criteria, and release-claim boundaries.

## Documents

- [JSON Persistence Store Architecture](JSON%20Persistence%20Store%20Architecture.md): the current long-lived design for JSON persistence over memory stores.

## Current Position

JSON should be a DataLinq-owned persistence store for memory backend state, not its own query backend.

The memory backend owns:

- row buffers
- indexes
- constraints
- query-plan execution
- transactions
- canonical commit batches
- snapshots and replay

It is not:

- arbitrary existing JSON document mapping
- model generation from random JSON samples
- JSON path querying over unknown document shapes
- SQL-provider JSON column support
- a replacement for the memory backend
- a peer provider with separate query semantics

The central design rule:

> JSON is storage. `DataLinq.Memory` is the backend. DataLinq metadata is the schema. `DataLinqQueryPlan` is the query contract.

The immediate 0.9 implementation plan lives in [0.9 Memory JSON Persistence Implementation Plan](../../../../roadmap-implementation/v0.9/Memory%20JSON%20Persistence%20Implementation%20Plan.md).
