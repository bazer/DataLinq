> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.
# Phase 8: Native AOT and WebAssembly Readiness

**Status:** Draft implementation plan.

## Scope

This folder tracks the execution plan for the eighth roadmap phase described in [Roadmap.md](../../Roadmap.md).

Phase 8 is about proving that DataLinq can run in constrained deployment modes without pretending that ordinary desktop/server CLR behavior automatically carries over:

1. classify and remove hot-path `Expression.Compile()` usage
2. make generated metadata and instance construction the AOT-preferred path
3. audit trimming and reflection-sensitive code before claiming trim compatibility
4. prove the SQLite browser story with a small Blazor WebAssembly sample
5. replace or disable cache cleanup threading paths that do not fit browser execution

## Starting Stance

This phase should not become a query-provider rewrite.

The correct first move is to build smoke projects that fail loudly under Native AOT, trimming, and browser WebAssembly, then remove the runtime behaviors those probes prove are real blockers. Runtime compatibility fallbacks can remain, but they must be explicitly outside the AOT-safe contract.

## Current Baseline

Relevant implementation files:

- `src/DataLinq/Linq/QueryExecutor.cs`
- `src/DataLinq/Linq/LocalSequenceExtractor.cs`
- `src/DataLinq/Linq/Evaluator.cs`
- `src/DataLinq/Linq/Visitors/WhereVisitor.cs`
- `src/DataLinq/Instances/InstanceFactory.cs`
- `src/DataLinq/Metadata/MetadataFromTypeFactory.cs`
- `src/DataLinq/Workers/ThreadWorker.cs`
- `src/DataLinq/Cache/DatabaseCache.cs`
- `src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs`
- `src/DataLinq.SharedCore/Metadata/GeneratedTableModelDeclaration.cs`
- `src/DataLinq.SQLite/SQLiteConnectionStringFactory.cs`

Relevant tests and verification lanes:

- `src/DataLinq.Generators.Tests`
- `src/DataLinq.Tests.Unit`
- `src/DataLinq.Tests.Compliance`
- the sandboxed quick lane: `run --suite all --alias quick --output failures --build`

Relevant docs:

- [`../../platform-compatibility/AOT and WebAssembly Strategy.md`](../../platform-compatibility/AOT%20and%20WebAssembly%20Strategy.md)
- [`../../metadata-and-generation/Source Generator Optimizations.md`](../../metadata-and-generation/Source%20Generator%20Optimizations.md)
- [`../../query-and-runtime/Query Pipeline Abstraction.md`](../../query-and-runtime/Query%20Pipeline%20Abstraction.md)

## Documents

- `Implementation Plan.md`

