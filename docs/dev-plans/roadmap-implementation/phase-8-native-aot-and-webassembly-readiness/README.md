> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.
# Phase 8: Native AOT and WebAssembly Readiness

**Status:** Implemented and verified for the generated SQLite AOT/WASM boundary.

## Scope

This folder tracks the execution plan and compatibility evidence for the eighth roadmap phase described in [Roadmap.md](../../Roadmap.md).

Phase 8 is about proving that DataLinq can run in constrained deployment modes without pretending that ordinary desktop/server CLR behavior automatically carries over:

1. classify and remove hot-path `Expression.Compile()` usage
2. make generated metadata and instance construction the AOT-preferred path
3. audit trimming and reflection-sensitive code before claiming trim compatibility
4. prove the SQLite browser story with a small Blazor WebAssembly sample
5. replace or disable cache cleanup threading paths that do not fit browser execution

## Result

The phase now has executable proof for a deliberately narrow but useful boundary: generated SQLite models under Native AOT, trimmed runtime publish, and Blazor WebAssembly AOT.

The important product stance is blunt: generated hooks are the AOT path. Missing generated database hooks now fail instead of silently drifting into reflection or expression-compiled fallbacks. That is the right tradeoff for this phase because hidden fallback is exactly how a library lies to itself about AOT readiness.

The proof does not justify a broad "DataLinq supports all AOT/WASM scenarios" claim. No-AOT browser WebAssembly still fails in the Mono interpreter for the SQLite/DataLinq path, `Remotion.Linq` still emits Native AOT/trimming warnings, SQLitePCLRaw emits WebAssembly native varargs warnings, and the runtime package still drags Roslyn into constrained publishes.

## Implemented Surface

Relevant implementation files:

- `src/DataLinq/Linq/QueryExecutor.cs`
- `src/DataLinq/Linq/Visitors/WhereVisitor.cs`
- `src/DataLinq/Metadata/MetadataFromTypeFactory.cs`
- `src/DataLinq/Cache/DatabaseCache.cs`
- `src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs`
- `src/DataLinq.SharedCore/Metadata/GeneratedTableModelDeclaration.cs`
- `src/DataLinq.PlatformCompatibility.Smoke`
- `src/DataLinq.AotSmoke`
- `src/DataLinq.TrimSmoke`
- `src/DataLinq.BlazorWasm`

Relevant tests and verification lanes:

- `src/DataLinq.Generators.Tests`
- `src/DataLinq.Tests.Unit`
- `src/DataLinq.Tests.Compliance`
- Native AOT smoke publish/run
- trimmed smoke publish/run
- Blazor WebAssembly AOT publish/browser run

Relevant docs:

- [`../../platform-compatibility/AOT and WebAssembly Strategy.md`](../../platform-compatibility/AOT%20and%20WebAssembly%20Strategy.md)
- [`../../metadata-and-generation/Source Generator Optimizations.md`](../../metadata-and-generation/Source%20Generator%20Optimizations.md)
- [`../../query-and-runtime/Query Pipeline Abstraction.md`](../../query-and-runtime/Query%20Pipeline%20Abstraction.md)

## Documents

- `Implementation Plan.md`
- `Compatibility Results.md`
