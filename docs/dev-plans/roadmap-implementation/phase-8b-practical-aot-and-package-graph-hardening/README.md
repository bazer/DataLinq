> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 8B: Practical AOT and Package Graph Hardening

**Status:** Active recommended follow-up after Phase 8.

## Purpose

Phase 8 proved a narrow generated SQLite path under Native AOT, trimming, and Blazor WebAssembly AOT. That is real. It is not yet a practical support story.

Phase 8B exists to close the ugly parts that Phase 8 deliberately surfaced instead of hiding:

- Roslyn/compiler assemblies still appear in constrained runtime publishes.
- `Remotion.Linq` still produces Native AOT and trimming warnings.
- SQLitePCLRaw still emits WebAssembly native varargs warnings.
- no-AOT browser WebAssembly publishes but does not run the SQLite/DataLinq path.
- compatibility results are still manually measured instead of repeatable size and warning reports.

## Execution Boundary

This phase should stay focused on package graph, query-pipeline dependency, warning hygiene, and measured constrained-platform output.

In scope:

- making missing generated hooks an initialization failure instead of a runtime fallback path
- removing the stale generated table-model hook compatibility shim
- introducing builder-built immutable metadata definitions before complete generated metadata startup
- moving ordinary generated-model metadata startup away from reflection
- repeatable size reports for Native AOT, trimmed, and WebAssembly publishes
- banned-payload checks for Roslyn files in runtime outputs
- splitting runtime-safe metadata from Roslyn/generator code
- removing `Microsoft.CodeAnalysis.*` from `DataLinq.dll` runtime dependency groups
- introducing the DataLinq-owned query-plan boundary needed to replace or isolate `Remotion.Linq`
- moving the generated/AOT query path toward a supported-subset parser
- investigating SQLitePCLRaw WebAssembly warning reachability
- keeping public support wording narrow and evidence-backed

Out of scope:

- full cache, memory, and invalidation redesign
- full migration execution
- claiming no-AOT WebAssembly support
- MySQL/MariaDB browser support
- general LINQ provider replacement beyond the documented support matrix
- OPFS/file-backed browser storage as part of the first hardening pass

## Source Plans

This execution slice is intentionally split across focused design documents:

- [Implementation Plan](Implementation%20Plan.md)
- [Practical AOT and Size Plan](../../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md)
- [Generated Metadata Contract and Runtime Fallback Removal](../../metadata-and-generation/Generated%20Metadata%20Contract%20and%20Runtime%20Fallback%20Removal.md)
- [Immutable Metadata Definitions and Factory Plan](../../metadata-and-generation/Immutable%20Metadata%20Definitions%20and%20Factory%20Plan.md)
- [Remotion.Linq Replacement Plan](../../query-and-runtime/Remotion.Linq%20Replacement%20Plan.md)
- [Warning Cleanup Plan](../../tooling/Warning%20Cleanup%20Plan.md)
- [Phase 8 Compatibility Results](../phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md)
- [AOT and WebAssembly Strategy](../../platform-compatibility/AOT%20and%20WebAssembly%20Strategy.md)

## Recommended Order

1. Remove stale generated-hook compatibility and make malformed generated declarations fail during initialization.
2. Introduce builder-built immutable metadata definitions so generated complete metadata targets the right runtime shape.
3. Add automated size reports and banned-file checks.
4. Split runtime-safe metadata from Roslyn/generator code.
5. Remove Roslyn from the runtime package graph and verify publish-size improvement.
6. Generate complete runtime metadata for generated models and keep reflection paths compatibility-only.
7. Introduce a DataLinq-owned query plan behind the current Remotion parser.
8. Move SQL generation behind that plan.
9. Build the supported-subset expression parser for generated/AOT mode.
10. Remove or isolate Remotion from the practical AOT support boundary.
11. Investigate SQLitePCLRaw WebAssembly warnings and document the exact call-path disposition.

## Exit Criteria

Phase 8B is done when:

- trimmed and WebAssembly outputs no longer contain Roslyn payloads
- size reports can be refreshed without manual folder inspection
- missing generated hooks and malformed generated declarations fail before materialization
- runtime metadata definitions are immutable snapshots built through a dedicated factory path
- generated model startup no longer rediscovers ordinary metadata through reflection
- generated SQLite Native AOT and trimmed publishes run without DataLinq-owned warnings
- Remotion is removed from, or isolated outside, the supported generated/AOT path
- the WebAssembly SQLitePCLRaw warning disposition is documented with call-path evidence
- public docs can state a narrow AOT/WASM support boundary without footnote gymnastics

Until then, the accurate public statement remains: DataLinq has a proven generated SQLite AOT/WASM AOT smoke path, not broad practical AOT support.
