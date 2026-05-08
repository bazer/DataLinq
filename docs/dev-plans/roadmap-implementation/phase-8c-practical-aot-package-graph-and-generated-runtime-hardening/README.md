> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 8C: Practical AOT Package Graph and Generated Runtime Hardening

**Status:** In progress after Phase 8B. Workstreams A and B are implemented: compatibility size reporting is repeatable, and Roslyn/compiler payloads are removed from the runtime package graph and constrained publish outputs.

## Purpose

Phase 8B got too large because the immutable metadata foundation turned into real implementation work instead of a small prerequisite. That was the right engineering move, but it means the rest of the practical AOT package-graph work deserves its own execution slice.

Phase 8C uses the Phase 8B generated-contract and immutable metadata foundation to clean up the runtime package graph and generated startup path:

- make constrained-platform measurements repeatable
- remove Roslyn/compiler payloads from runtime publishes
- switch generated startup to require complete generated metadata
- remove runtime reflection metadata discovery instead of keeping it as a compatibility fallback
- generate indexed metadata/value/relation access where it removes avoidable lookup
- keep public compatibility wording narrow and evidence-backed

## Execution Boundary

In scope:

- compatibility size reports and banned-payload gates
- runtime-safe metadata/package split work
- removal of `Microsoft.CodeAnalysis.*` from runtime dependency groups
- complete generated metadata startup
- removal of runtime reflection compatibility for generated metadata startup
- generated indexed value access, relation handles, and mutable metadata handles
- package inspection and public compatibility wording discipline

Out of scope:

- replacing or isolating `Remotion.Linq`
- introducing the DataLinq-owned query-plan boundary
- building the supported-subset expression parser
- deciding the SQLitePCLRaw WebAssembly warning disposition
- claiming no-AOT browser WebAssembly support
- MySQL/MariaDB browser support
- OPFS/file-backed browser storage
- cache, memory, and invalidation redesign

The query-plan/parser and SQLitePCLRaw warning work moved to the later query-boundary phase because it is big enough to distort this package/runtime slice.

## Source Plans

- [Implementation Plan](Implementation%20Plan.md)
- [Phase 8B Implementation Plan](../phase-8b-practical-aot-and-package-graph-hardening/Implementation%20Plan.md)
- [Practical AOT and Size Plan](../../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md)
- [Generated Metadata Contract and Runtime Fallback Removal](../../metadata-and-generation/Generated%20Metadata%20Contract%20and%20Runtime%20Fallback%20Removal.md)
- [Immutable Metadata Definitions and Factory Plan](../../metadata-and-generation/Immutable%20Metadata%20Definitions%20and%20Factory%20Plan.md)
- [Phase 8 Compatibility Results](../phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md)

## Recommended Order

1. Add repeatable compatibility size reports and banned-payload checks.
2. Split runtime-safe metadata from Roslyn/generator code.
3. Remove Roslyn from the runtime package graph and verify publish-size impact.
4. Generate complete runtime metadata for generated models.
5. Remove runtime reflection metadata discovery and make missing or unreadable generated metadata a descriptive startup failure.
6. Generate indexed value access, relation handles, and mutable metadata handles.
7. Inspect packed package assets and update compatibility wording without overclaiming.

## Exit Criteria

Phase 8C is done when:

- compatibility results can be refreshed without manual folder inspection
- trimmed and WebAssembly outputs do not contain Roslyn runtime payloads
- `DataLinq.dll` runtime dependency groups do not include `Microsoft.CodeAnalysis.*`
- generated model startup requires complete generated metadata and has no runtime reflection metadata-discovery fallback
- missing, stale, or unreadable generated metadata fails during startup with a descriptive `InvalidModel` diagnostic
- generated value, relation, and mutable access paths avoid avoidable name/global metadata lookup
- package inspection confirms analyzer payloads do not leak into runtime dependencies
- public docs avoid broad AOT claims and clearly leave Remotion/query-parser and SQLitePCLRaw warning disposition to the later query-boundary phase
