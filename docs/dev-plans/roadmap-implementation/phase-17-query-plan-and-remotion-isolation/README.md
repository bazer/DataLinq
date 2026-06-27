> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 17: Query Plan and Remotion Isolation

**Status:** Source plan superseded by the version-scoped [DataLinq 0.8 Roadmap](../v0.8/README.md). Earlier roadmap text treated this as deferred behind join, converter, and result-set caching work; the `v0.8` branch deliberately makes the query parser boundary the major theme.

## Purpose

The query-plan and parser work is too large to hide inside Phase 8B or Phase 8C. It touches query semantics, SQL generation, unsupported-shape diagnostics, projection execution, and the AOT support boundary. That is a real phase, not a cleanup task.

Phase 17 owns the work needed to replace or isolate `Remotion.Linq` and to resolve the remaining WebAssembly SQLite warning story:

- introduce a DataLinq-owned query plan behind the current Remotion parser
- move SQL generation and diagnostics behind that plan
- build a supported-subset expression parser for generated/AOT mode
- dual-run parser parity against the current support matrix
- remove or isolate Remotion from the generated/AOT support boundary
- investigate SQLitePCLRaw WebAssembly warnings with call-path evidence

## Why This Was Deferred

Key/cache and join work was more important when this phase was originally parked. The query-boundary work is valuable, but it is also a high-blast-radius migration. The old roadmap therefore kept it behind nearer allocation, invalidation, cleanup, join, scalar-converter, and result-set caching phases unless constrained-platform query support became the dominant product priority.

For 0.8, the product priority has changed: removing or isolating the Remotion dependency is now the release theme. That means the older join plans should be rebased around a source-slot-aware DataLinq query plan instead of extending the old Remotion-shaped boundary first.

## Source Plans

- [DataLinq 0.8 Roadmap](../v0.8/README.md)
- [0.8 Query Parser Overview](0.8%20Query%20Parser%20Overview.md)
- [Implementation Plan](Implementation%20Plan.md)
- [Remotion.Linq Replacement Plan](../../query-and-runtime/Remotion.Linq%20Replacement%20Plan.md)
- [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)
- [Practical AOT and Size Plan](../../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md)
- [Phase 8 Compatibility Results](../../archive/roadmap-implementation/phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md)

## Exit Criteria

Phase 17 is done when:

- Remotion still works only as an explicit compatibility path, or is removed
- SQL generation consumes DataLinq query-plan nodes rather than Remotion clause types
- the supported subset parser passes the documented support matrix for enabled shapes
- generated SQLite AOT and trim smokes no longer root `Remotion.Linq`
- unsupported query shapes fail with DataLinq-owned diagnostics
- SQLitePCLRaw WebAssembly warning disposition is documented with exact managed/native call-path evidence
