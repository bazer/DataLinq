> [!WARNING]
> This folder contains roadmap and design material for the planned DataLinq memory backend. It is not normative product documentation and should not be treated as a shipped support claim.

# Memory Backend Design Notes

**Status:** Draft collection.

**Created:** 2026-07-03.

This folder collects durable design notes for the planned `DataLinq.Memory` backend.

Use this folder for design decisions that should outlive one implementation phase:

- provider architecture
- AOT and browser/WebAssembly constraints
- store and transaction semantics
- query-plan execution semantics
- seeding, snapshots, and fixture behavior
- diagnostics, verification, and public wording

Use versioned roadmap folders such as `../roadmap-implementation/v0.9/` for phase sequencing, immediate exit criteria, and release-claim boundaries.

## Documents

- [Architecture](Architecture.md): the current long-lived design for the in-memory backend.

## Current Position

The memory backend should be a real backend over generated DataLinq metadata, not SQLite in-memory, not a cache promotion, and not a SQL parser wearing a provider costume.

The central design rule:

> `DataLinqQueryPlan` should be executable by a non-SQL backend. SQL providers are one backend implementation, not the center of the design.

The immediate 0.9 implementation plan lives in [0.9 In-Memory Database Implementation Plan](../../roadmap-implementation/v0.9/In-Memory%20Database%20Implementation%20Plan.md).
