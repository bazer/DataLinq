> [!WARNING]
> This folder contains roadmap and design material for the planned DataLinq JSON store backend. It is not normative product documentation and should not be treated as a shipped support claim.

# JSON Store Backend Design Notes

**Status:** Draft collection.

**Created:** 2026-07-03.

This folder collects durable design notes for the planned DataLinq JSON store backend.

Use this folder for design decisions that should outlive one implementation phase:

- DataLinq-owned JSON store format
- AOT and browser/WebAssembly constraints
- provider-value encoding and scalar conversion
- persistence lifecycle and atomic write behavior
- schema digest and compatibility behavior
- query-plan execution over loaded JSON state
- CLI import/export/validation surfaces

Use versioned roadmap folders such as `../../roadmap-implementation/v0.9/` for phase sequencing, immediate exit criteria, and release-claim boundaries.

## Documents

- [Store Backend Architecture](Store%20Backend%20Architecture.md): the current long-lived design for the JSON store backend.

## Current Position

The JSON store backend should be a DataLinq-owned persistence backend for generated model state.

It is not:

- arbitrary existing JSON document mapping
- model generation from random JSON samples
- JSON path querying over unknown document shapes
- SQL-provider JSON column support
- a replacement for the memory backend

The central design rule:

> JSON is the storage format. DataLinq metadata is the schema. `DataLinqQueryPlan` is the query contract.

The immediate 0.9 implementation plan lives in [0.9 JSON Store Implementation Plan](../../roadmap-implementation/v0.9/JSON%20Store%20Implementation%20Plan.md).
