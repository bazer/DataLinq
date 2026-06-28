> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 17: Query Plan and Remotion Isolation

**Status:** Source plan superseded and implemented by the version-scoped [DataLinq 0.8 Roadmap](../v0.8/README.md). Earlier roadmap text treated this as deferred behind join, converter, and result-set caching work; the `v0.8` branch deliberately made the query parser boundary the major theme and closed the Remotion-removal track through [0.8 Phase 7](../v0.8/phase-7-remotion-dependency-removal/README.md).

## Purpose

The query-plan and parser work is too large to hide inside Phase 8B or Phase 8C. It touches query semantics, SQL generation, unsupported-shape diagnostics, projection execution, and the AOT support boundary. That is a real phase, not a cleanup task.

Historically, Phase 17 owned the work needed to replace or isolate `Remotion.Linq` and to resolve the remaining WebAssembly SQLite warning story:

- introduce a DataLinq-owned query plan behind the then-current Remotion parser
- move SQL generation and diagnostics behind that plan
- build a supported-subset expression parser for generated/AOT mode
- dual-run parser parity against the current support matrix
- remove Remotion from the generated/AOT support boundary
- investigate SQLitePCLRaw WebAssembly warnings with call-path evidence

The query-parser and Remotion dependency work is now closed by the 0.8 Phase 1 through Phase 7 sequence. SQLitePCLRaw WebAssembly warning disposition remains separate compatibility work.

## Why This Was Deferred

Key/cache and join work was more important when this phase was originally parked. The query-boundary work is valuable, but it is also a high-blast-radius migration. The old roadmap therefore kept it behind nearer allocation, invalidation, cleanup, join, scalar-converter, and result-set caching phases unless constrained-platform query support became the dominant product priority.

For 0.8, the product priority changed: removing the Remotion dependency became the release theme. That means the older join plans should now be built around the source-slot-aware DataLinq query plan instead of extending the old Remotion-shaped boundary first.

## Source Plans

- [DataLinq 0.8 Roadmap](../v0.8/README.md)
- [0.8 Query Parser Overview](0.8%20Query%20Parser%20Overview.md)
- [Implementation Plan](Implementation%20Plan.md)
- [Remotion.Linq Replacement Plan](../../query-and-runtime/Remotion.Linq%20Replacement%20Plan.md)
- [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)
- [Practical AOT and Size Plan](../../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md)
- [Phase 8 Compatibility Results](../../archive/roadmap-implementation/phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md)

## Exit Criteria Status

The Remotion/query-parser part is done in the current 0.8 branch:

- Remotion is removed from the main product dependency graph
- SQL generation consumes DataLinq query-plan nodes rather than Remotion clause types
- the supported subset parser is the active production query provider for documented shapes
- generated SQLite AOT and trim smokes no longer root `Remotion.Linq`
- unsupported query shapes fail with DataLinq-owned diagnostics

Still separate compatibility work:

- SQLitePCLRaw WebAssembly warning disposition needs exact managed/native call-path evidence, provider/bundle changes, or explicit support caveats
