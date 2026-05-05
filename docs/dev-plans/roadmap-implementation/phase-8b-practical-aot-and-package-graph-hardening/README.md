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

- [Practical AOT and Size Plan](../../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md)
- [Remotion.Linq Replacement Plan](../../query-and-runtime/Remotion.Linq%20Replacement%20Plan.md)
- [Phase 8 Compatibility Results](../phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md)
- [AOT and WebAssembly Strategy](../../platform-compatibility/AOT%20and%20WebAssembly%20Strategy.md)

## Recommended Order

1. Add automated size reports and banned-file checks.
2. Split runtime-safe metadata from Roslyn/generator code.
3. Remove Roslyn from the runtime package graph and verify publish-size improvement.
4. Introduce a DataLinq-owned query plan behind the current Remotion parser.
5. Move SQL generation behind that plan.
6. Build the supported-subset expression parser for generated/AOT mode.
7. Remove or isolate Remotion from the practical AOT support boundary.
8. Investigate SQLitePCLRaw WebAssembly warnings and document the exact call-path disposition.

## Exit Criteria

Phase 8B is done when:

- trimmed and WebAssembly outputs no longer contain Roslyn payloads
- size reports can be refreshed without manual folder inspection
- generated SQLite Native AOT and trimmed publishes run without DataLinq-owned warnings
- Remotion is removed from, or isolated outside, the supported generated/AOT path
- the WebAssembly SQLitePCLRaw warning disposition is documented with call-path evidence
- public docs can state a narrow AOT/WASM support boundary without footnote gymnastics

Until then, the accurate public statement remains: DataLinq has a proven generated SQLite AOT/WASM AOT smoke path, not broad practical AOT support.
