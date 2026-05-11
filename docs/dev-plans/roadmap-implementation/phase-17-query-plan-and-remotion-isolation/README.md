> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 17: Query Plan and Remotion Isolation

**Status:** Deferred. This is intentionally behind the key/cache, join, scalar-converter, and result-set caching roadmap work unless a concrete product need pulls AOT query support forward.

## Purpose

The query-plan and parser work is too large to hide inside Phase 8B or Phase 8C. It touches query semantics, SQL generation, unsupported-shape diagnostics, projection execution, and the AOT support boundary. That is a real phase, not a cleanup task.

Phase 17 owns the work needed to replace or isolate `Remotion.Linq` and to resolve the remaining WebAssembly SQLite warning story:

- introduce a DataLinq-owned query plan behind the current Remotion parser
- move SQL generation and diagnostics behind that plan
- build a supported-subset expression parser for generated/AOT mode
- dual-run parser parity against the current support matrix
- remove or isolate Remotion from the generated/AOT support boundary
- investigate SQLitePCLRaw WebAssembly warnings with call-path evidence

## Why Deferred

Key/cache and join work is more important right now. The query-boundary work is valuable, but it is also a high-blast-radius migration. It should not crowd out the nearer allocation, invalidation, cleanup, join, scalar-converter, and result-set caching phases unless constrained-platform query support becomes the dominant product priority.

## Source Plans

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
