# Development Plans

> [!WARNING]
> This folder contains roadmap, design, migration, and audit material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.

## Purpose

`docs/dev-plans` is where DataLinq keeps internal planning notes, architectural drafts, migration records, and performance ideas that are useful to contributors but should stay clearly separate from user-facing docs.

The point of this folder is not to look tidy. The point is to stop roadmap material from leaking into documentation that claims to describe current behavior.

## Structure

### Cross-cutting

- `Roadmap.md`

### Architecture

- `architecture/Applications patterns.md`

### Documentation

- `documentation/Documentation Overhaul.md`

### Platform compatibility

- `platform-compatibility/AOT and WebAssembly Strategy.md`

### Tooling

- `tooling/Build Environment and Output Control.md`

### Metadata and generation

- `metadata-and-generation/Metadata Architecture.md`
- `metadata-and-generation/Source Generator Optimizations.md`

### Performance

- `performance/Memory management.md`
- `performance/Memory Optimization and Deduplication.md`
- `performance/Performance Benchmarking.md`

### Roadmap implementation

- `roadmap-implementation/README.md`
- `roadmap-implementation/phase-1-benchmarking-and-observability/README.md`
- `roadmap-implementation/phase-1-benchmarking-and-observability/Implementation Plan.md`
- `roadmap-implementation/phase-2-metadata-generator-and-diagnostics-hardening/README.md`
- `roadmap-implementation/phase-2-metadata-generator-and-diagnostics-hardening/Implementation Plan.md`
- `roadmap-implementation/phase-3-query-and-runtime-hot-path-optimization/README.md`
- `roadmap-implementation/phase-3-query-and-runtime-hot-path-optimization/Implementation Plan.md`
- `roadmap-implementation/phase-4-product-trust-features/README.md`
- `roadmap-implementation/phase-4-product-trust-features/Implementation Plan.md`

### Providers and features

- `providers-and-features/In-Memory Provider.md`
- `providers-and-features/JSON Data Type Support.md`
- `providers-and-features/Migrations and Validation.md`

### Query and runtime

- `query-and-runtime/Async and Lazy Loading.md`
- `query-and-runtime/Batched mutations.md`
- `query-and-runtime/LINQ Translation Support.md`
- `query-and-runtime/Projections and Views.md`
- `query-and-runtime/Query Pipeline Abstraction.md`
- `query-and-runtime/Result set caching.md`
- `query-and-runtime/Sql Generation Optimization.md`

### Testing

- `testing/README.md`
- `testing/Test Infrastructure CLI.md`

### Archive

- `archive/testing/README.md`

## Notes

- The active testing material now lives under `testing/`, while completed migration records live under `archive/testing/`.
- Some documents in this folder describe ideas that are still valid but not implemented. That is fine. The real mistake is presenting those ideas as current product behavior.
- The `roadmap-implementation/` folder is where high-level roadmap phases are turned into concrete execution plans. It should stay tightly linked to `Roadmap.md` rather than drifting into a second roadmap.

## Current Stage Audit

As of the Phase 3 closeout:

- Phase 1 benchmarking and observability is substantially implemented; benchmark-history evidence still matters for noisy scenarios.
- Phase 2 metadata/generator/diagnostics hardening is implemented as a narrow foundation, not as a full immutable metadata rewrite.
- Phase 3 query/runtime hot-path optimization is implemented; the honest performance claim is lower allocation pressure on measured repeated-query paths.
- The older benchmark, metadata, source-generator, and SQL-generation specs now have status notes explaining which parts landed and which remain future work.
- The next missing roadmap execution artifact was Phase 4 product trust work, so `roadmap-implementation/phase-4-product-trust-features/` now defines the validation/migration execution plan.

The main thing not to skip at this stage is schema validation. AOT, async, cache invalidation, projections, JSON columns, and result-set caching are still valid plans, but they should not jump ahead of drift detection and conservative schema tooling.
