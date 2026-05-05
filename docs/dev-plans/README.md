# Development Plans

> [!WARNING]
> This folder contains roadmap, design, migration, and audit material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.

## Purpose

`docs/dev-plans` is where DataLinq keeps internal planning notes, architectural drafts, migration records, and performance ideas that are useful to contributors but should stay clearly separate from user-facing docs.

The point of this folder is not to look tidy. The point is to stop roadmap material from leaking into documentation that claims to describe current behavior.

## Structure

### Cross-cutting

- `Roadmap.md`
- current support matrices live outside this folder in `../support-matrices/`

### Architecture

- `architecture/Applications patterns.md`
- `architecture/Distributed Cache Coordination and CDC.md`

### Platform compatibility

- `platform-compatibility/AOT and WebAssembly Strategy.md`
- `platform-compatibility/Practical AOT and Size Plan.md`

### Tooling

- `tooling/Build Environment and Output Control.md`

### Metadata and generation

- `metadata-and-generation/Generated Metadata Contract and Runtime Fallback Removal.md`
- `metadata-and-generation/Immutable Metadata Definitions and Factory Plan.md`
- `metadata-and-generation/Metadata Architecture.md`
- `metadata-and-generation/Scalar Converter Support.md`
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
- `roadmap-implementation/phase-4-provider-metadata-roundtrip-fidelity/README.md`
- `roadmap-implementation/phase-4-provider-metadata-roundtrip-fidelity/Implementation Plan.md`
- `roadmap-implementation/phase-4b-provider-fidelity-hardening/README.md`
- `roadmap-implementation/phase-4b-provider-fidelity-hardening/Implementation Plan.md`
- `roadmap-implementation/phase-5-product-trust-features/README.md`
- `roadmap-implementation/phase-5-product-trust-features/Implementation Plan.md`
- `roadmap-implementation/phase-5-product-trust-features/Snapshot Migration Design.md`
- `roadmap-implementation/phase-6-linq-translation-coverage-and-query-composition/README.md`
- `roadmap-implementation/phase-6-linq-translation-coverage-and-query-composition/Implementation Plan.md`
- `roadmap-implementation/phase-7-linq-feature-expansion/README.md`
- `roadmap-implementation/phase-7-linq-feature-expansion/Implementation Plan.md`
- `roadmap-implementation/phase-8-native-aot-and-webassembly-readiness/README.md`
- `roadmap-implementation/phase-8-native-aot-and-webassembly-readiness/Implementation Plan.md`
- `roadmap-implementation/phase-8-native-aot-and-webassembly-readiness/Compatibility Results.md`
- `roadmap-implementation/phase-8b-practical-aot-and-package-graph-hardening/README.md`

### Providers and features

- `providers-and-features/In-Memory Provider.md`
- `providers-and-features/JSON Data Type Support.md`
- `providers-and-features/Check Constraint Metadata Design.md`
- `providers-and-features/Migrations and Validation.md`
- `providers-and-features/Provider Metadata Roundtrip Fidelity.md`
- `providers-and-features/SQLite Transaction Isolation Alignment.md`

### Query and runtime

- `query-and-runtime/Async and Lazy Loading.md`
- `query-and-runtime/Batched mutations.md`
- `query-and-runtime/LINQ Translation Support.md`
- `query-and-runtime/Projections and Views.md`
- `query-and-runtime/Query Pipeline Abstraction.md`
- `query-and-runtime/Relation-Aware Join API.md`
- `query-and-runtime/Remotion.Linq Replacement Plan.md`
- `query-and-runtime/Result set caching.md`
- `query-and-runtime/Set-based mutations.md`
- `query-and-runtime/Sql Generation Optimization.md`

### Testing

- `testing/README.md`
- `testing/Test Infrastructure CLI.md`

### Archive

- `archive/documentation/README.md`
- `archive/roadmap-implementation/README.md`
- `archive/testing/README.md`

## Notes

- The active testing material now lives under `testing/`, while completed migration records live under `archive/testing/`.
- Completed or superseded documentation and roadmap checkpoints now live under `archive/` so active planning pages do not point readers at old "next step" guidance.
- Some documents in this folder describe ideas that are still valid but not implemented. That is fine. The real mistake is presenting those ideas as current product behavior.
- The `roadmap-implementation/` folder is where high-level roadmap phases are turned into concrete execution plans. It should stay tightly linked to `Roadmap.md` rather than drifting into a second roadmap.

## Current Stage Audit

As of the Phase 8 completion update on 2026-05-05:

- Phase 1 benchmarking and observability is substantially implemented; benchmark-history evidence still matters for noisy scenarios.
- Phase 2 metadata/generator/diagnostics hardening is implemented as a narrow foundation, not as a full immutable metadata rewrite.
- Phase 3 query/runtime hot-path optimization is implemented; the honest performance claim is lower allocation pressure on measured repeated-query paths.
- Phase 4 provider metadata roundtrip fidelity is implemented for the validation support boundary: the matrix is explicit, ordinary indexes/relations/identifiers/checks/comments are covered where supported, and unsupported provider details are documented instead of implied.
- Phase 4B provider fidelity hardening is implemented: referential actions, MySQL/MariaDB column ordering, raw provider defaults, generated-column guardrails, advanced index guardrails, and view validation are now covered at their documented boundaries.
- Phase 5 product-trust work is implemented for the intended validation/diff/snapshot scope: schema validation, CLI validation output, conservative diff scripts, and the versioned snapshot DTO/design are in place.
- Phase 6 LINQ translation coverage and query composition is implemented: the support audit, chained `Where(...)` fix, projected local `Contains(...)`, equality-based local object-list `Any(predicate)` expansion, fixed-condition invariants, and unsupported-query diagnostics have landed.
- Phase 7 LINQ feature expansion is implemented: scalar aggregates, computed projections, nullable predicate polish, explicit joins, and relation-aware predicate translation have landed within their documented support boundaries.
- Phase 8 Native AOT and WebAssembly readiness is implemented for the generated SQLite Native AOT, trimmed runtime, and Blazor WebAssembly AOT smoke boundary. The honest follow-up work is the practical AOT package graph: split Roslyn from the runtime package, replace or isolate `Remotion.Linq`, investigate SQLitePCLRaw WebAssembly warnings, and automate size reports.
- Phase 8B practical AOT and package graph hardening is the recommended next execution slice if constrained-platform support is the priority. It should convert the Phase 8 smoke proof into a cleaner generated-contract, immutable metadata snapshot, package, and warning story before public docs claim more than a narrow generated SQLite boundary.
- The older benchmark, metadata, source-generator, provider-fidelity, and migration specs now have status notes explaining which parts landed and which remain future work.

The next roadmap execution work should be Phase 8B practical AOT cleanup, starting with generated-hook fail-fast behavior, then the immutable metadata builder/factory foundation before full generated metadata startup, and then runtime/Roslyn separation and the Remotion replacement path, unless we deliberately pause to start the separate full migration-execution feature or move straight to Phase 9 cache/memory foundations.

The main thing not to blur at this stage is the boundary between implemented product-trust tooling and planned migration history. `validate` and `diff` are real. Full `add-migration`, `update-database`, runtime migration APIs, and applied-migration tracking are still future work.
