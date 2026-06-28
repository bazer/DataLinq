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

- `architecture/Aspirational Product Specification.md`
- `architecture/Applications patterns.md`
- `architecture/Dependency Injection and Hosting Integration.md`
- `architecture/Distributed Cache Coordination and CDC.md`

### Platform compatibility

- `platform-compatibility/Practical AOT and Size Plan.md`

### Incubating companion projects

- `DataLinq.Store/README.md`
- `DataLinq.Store/Accepted High-Level Decisions.md`
- `DataLinq.Store/API and Binding Generation.md`
- `DataLinq.Store/Identity Versioning and Protocol Compatibility.md`
- `DataLinq.Store/Module Paging Lifetimes and Retention.md`
- `DataLinq.Store/Mutation and Invalidation Loop.md`
- `DataLinq.Store/Security and Authorization Model.md`
- `DataLinq.Store/Server Subscription and Module Cache Architecture.md`
- `DataLinq.Store/State Modules and Graph Cache.md`
- `DataLinq.Store/State Sync Architecture.md`
- `DataLinq.Store/Store Contract IR and Module Authoring Model.md`
- `DataLinq.Store/WASM and Interop Strategy.md`

### Tooling

- `tooling/Build Environment and Output Control.md`

### Metadata and generation

- `metadata-and-generation/Generated File Headers and Stamping.md`
- `metadata-and-generation/Metadata Architecture.md`
- `metadata-and-generation/Nullable Reference Type Generation Defaults.md`
- `metadata-and-generation/Scalar Converter Support.md`
- `metadata-and-generation/Source Location Diagnostic Fidelity.md`
- `metadata-and-generation/Source Generator Optimizations.md`
- `metadata-and-generation/Validation Diagnostics and Partial Generation.md`

### Performance

- `performance/Cache Memory Accounting.md`
- `performance/Memory Optimization and Deduplication.md`
- `performance/Allocation Reduction Audit.md`
- `performance/Representative Benchmark Suite and Website Trends.md`

### Roadmap implementation

- `roadmap-implementation/README.md`
- `roadmap-implementation/v0.8/README.md`
- `roadmap-implementation/v0.8/phase-1-query-contract-and-plan-baseline/README.md`
- `roadmap-implementation/v0.8/phase-2-remotion-plan-adapter/README.md`
- `roadmap-implementation/v0.8/phase-3-sql-generation-on-query-plan/README.md`
- `roadmap-implementation/v0.8/phase-4-supported-subset-expression-parser/README.md`
- `roadmap-implementation/v0.8/phase-5-projection-and-local-evaluation-aot-cleanup/README.md`
- `roadmap-implementation/v0.8/phase-6-dual-run-parity-and-aot-switch/README.md`
- `roadmap-implementation/v0.8/phase-7-remotion-dependency-removal/README.md`
- `roadmap-implementation/v0.8/phase-8-browser-aot-runtime-proof/README.md`
- `roadmap-implementation/v0.8/phase-9-webassembly-warning-and-no-aot-disposition/README.md`
- `roadmap-implementation/v0.8/phase-10-aot-query-coverage-and-fallback-fencing/README.md`
- `roadmap-implementation/v0.8/phase-11-browser-payload-and-deploy-size-hardening/README.md`
- `roadmap-implementation/v0.8/phase-12-aot-release-gates-and-support-contract/README.md`
- `roadmap-implementation/v0.8/phase-13-query-composition-and-subquery-pushdown/README.md`
- `roadmap-implementation/v0.8/phase-13b-grouped-aggregate-projection-baseline/README.md`
- `roadmap-implementation/v0.8/phase-14-source-slot-join-composition/README.md`
- `roadmap-implementation/v0.8/phase-15-relation-aware-and-implicit-joins/README.md`
- `roadmap-implementation/phase-13-explicit-multi-join-composition/README.md`
- `roadmap-implementation/phase-14-relation-aware-joins-and-left-joins/README.md`
- `roadmap-implementation/phase-15-scalar-converters-and-typed-key-ergonomics/README.md`
- `roadmap-implementation/phase-16-dependency-tracked-result-set-caching/README.md`
- `roadmap-implementation/phase-17-query-plan-and-remotion-isolation/README.md`
- `roadmap-implementation/phase-17-query-plan-and-remotion-isolation/Implementation Plan.md`

### Providers and features

- `providers-and-features/In-Memory Provider.md`
- `providers-and-features/Generated Column Support.md`
- `providers-and-features/JSON Data Type Support.md`
- `providers-and-features/Check Constraint Metadata Design.md`
- `providers-and-features/Migrations and Validation.md`
- `providers-and-features/Provider Metadata Roundtrip Fidelity.md`
- `providers-and-features/Schema Validation Hooks.md`
- `providers-and-features/SQLite Transaction Isolation Alignment.md`
- `providers-and-features/UUID Storage Format Support.md`

### Query and runtime

- `query-and-runtime/Async and Lazy Loading.md`
- `query-and-runtime/Batched mutations.md`
- `query-and-runtime/Mutable Instance Lifecycle.md`
- `query-and-runtime/Mutation Audit Events.md`
- `query-and-runtime/Projections and Views.md`
- `query-and-runtime/Query Pipeline Abstraction.md`
- `query-and-runtime/Relation-Aware Join API.md`
- `query-and-runtime/Relation-Aware Mutation API.md`
- `query-and-runtime/Remotion.Linq Replacement Plan.md`
- `query-and-runtime/Result set caching.md`
- `query-and-runtime/Set-based mutations.md`
- `query-and-runtime/Sql Generation Optimization.md`

### Testing

- `testing/README.md`
- `testing/Model Testing and Mocking Support.md`

### Archive

- `archive/documentation/README.md`
- `archive/metadata-and-generation/README.md`
- `archive/performance/README.md`
- `archive/platform-compatibility/README.md`
- `archive/query-and-runtime/README.md`
- `archive/roadmap-implementation/README.md`
- `archive/testing/README.md`
- `archive/tooling/README.md`

## Notes

- Testing dev-plan material is currently historical. Current workflow documentation lives under `../contributing/`, while completed design records live under `archive/testing/`.
- Completed or superseded documentation and roadmap checkpoints now live under `archive/` so active planning pages do not point readers at old "next step" guidance.
- Some documents in this folder describe ideas that are still valid but not implemented. That is fine. The real mistake is presenting those ideas as current product behavior.
- The active release roadmap is `roadmap-implementation/v0.8/`. The 0.8 parser-removal track is complete through Phase 7; Phases 8 through 12 now own browser AOT runtime proof, WebAssembly warning/no-AOT disposition, query coverage, deploy-size hardening, and final release gates. Phase 13, Phase 13B, and Phases 14 through 15 now keep query-composition/subquery-pushdown hardening, a narrow grouped aggregate projection baseline, source-slot joins, relation-aware joins, and implicit join completion inside the 0.8 line after those AOT gates. Completed global phase execution records belong under `archive/roadmap-implementation/`.

## Current Stage Audit

As of the current 0.8 branch after the parser-removal closeout:

- Phase 1 benchmarking and observability is substantially implemented; benchmark-history evidence still matters for noisy scenarios.
- Phase 2 metadata/generator/diagnostics hardening is implemented as a narrow foundation, not as a full immutable metadata rewrite.
- Phase 3 query/runtime hot-path optimization is implemented; the honest performance claim is lower allocation pressure on measured repeated-query paths.
- Phase 4 provider metadata roundtrip fidelity is implemented for the validation support boundary: the matrix is explicit, ordinary indexes/relations/identifiers/checks/comments are covered where supported, and unsupported provider details are documented instead of implied.
- Phase 4B provider fidelity hardening is implemented: referential actions, MySQL/MariaDB column ordering, raw provider defaults, generated-column guardrails, advanced index guardrails, and view validation are now covered at their documented boundaries.
- Phase 5 product-trust work is implemented for the intended validation/diff/snapshot scope: schema validation, CLI validation output, conservative diff scripts, and the versioned snapshot DTO/design are in place.
- Phase 6 LINQ translation coverage and query composition is implemented: the support audit, chained `Where(...)` fix, projected local `Contains(...)`, equality-based local object-list `Any(predicate)` expansion, fixed-condition invariants, and unsupported-query diagnostics have landed.
- Phase 7 LINQ feature expansion is implemented: scalar aggregates, computed projections, nullable predicate polish, explicit joins, and relation-aware predicate translation have landed within their documented support boundaries.
- Phase 8 Native AOT and WebAssembly readiness is implemented historically for the generated SQLite Native AOT, trimmed runtime, and Blazor WebAssembly AOT smoke boundary. The 2026-06-28 compatibility refresh has green Native AOT and trimmed evidence, and the 0.8 release gate tooling now automates browser WebAssembly smoke. Current `wasm-aot` browser evidence is red at generated SQLite startup with `MONO_WASM: function signature mismatch`, so browser AOT support remains blocked.
- Phase 8B generated contract and immutable metadata foundation is complete for its foundation scope: stale generated hooks fail early, malformed generated declarations fail during initialization, and runtime metadata snapshots are factory-built and frozen against ordinary mutation.
- Phase 8C practical AOT package graph and generated runtime hardening is complete: size reports, Roslyn/runtime package split, complete generated metadata startup, runtime reflection metadata-discovery removal, generated indexed access, and packaging/public wording have landed.
- Phase 9A release hardening, benchmarks, allocation, and cache invalidation is complete: warning cleanup, benchmark-history and website trends, allocation reduction, conservative cache invalidation hardening, and benchmark closeout evidence have landed. The honest performance claim is allocation evidence, not latency improvement.
- Phase 10 key and allocation foundation is complete: metadata collection shape, frozen lookups, generated provider-key cache paths, relation lookup without lookup-only `IKey`, scalar-converter seams, and allocation closeout evidence have landed.
- Phase 11 cache clearing and external invalidation is complete: explicit database/table/provider-key invalidation APIs, relation/index invalidation, invalidation envelopes, freshness vocabulary, and cache telemetry have landed.
- Phase 12 memory-pressure cleanup and measured deduplication is complete: estimated cache memory accounting, estimated-footprint byte limits, memory-pressure-aware cleanup, coordinated cleanup scheduling, cleanup telemetry, and benchmark-led rejection of production value/key deduplication have landed.
- Phase 12B generation trust and diagnostics hardening is complete: aggregate validation diagnostics, source-location fidelity, safe CLI generation, partial source-generator output, generated-file preambles, and nullable-reference-generation defaults.
- Phase 12C CLI configuration and regeneration workflow is complete: nested CLI commands, config init/schema/validate, batch generation and validation, diagnostics output, and secret references have landed.
- The 0.8 parser-removal track is complete through Phase 7: query contract baseline, Remotion plan adapter, SQL generation on `DataLinqQueryPlan`, supported-subset expression parser, projection/local-evaluation cleanup, dual-run parity, production provider switch, and removal of `Remotion.Linq` from the main runtime package graph.
- The 0.8 AOT/browser release track is implemented through Phases 8 through 12 for tooling: browser AOT runtime proof, SQLitePCLRaw/no-AOT evidence capture, constrained query coverage, deploy-size thresholds, and support-contract gates. Current browser evidence blocks the WebAssembly AOT support claim until the SQLite/WebAssembly runtime failure is fixed or explicitly excluded.
- Phase 13 query composition and subquery pushdown is implemented for the single-source mapped-row slice: `Where(...)`, `OrderBy(...)`, `Skip(...)`, `Take(...)`, and supported scalar result operators preserve C# operator order with SQL subquery boundaries where flat SQL would be wrong.
- Phase 13B grouped aggregate projection baseline is in progress after Phase 13 and before broad join expansion: support the first honest `GroupBy(...)` slice as `GroupBy(key).Select(g => new { g.Key, Count = g.Count() })`, not materialized `IGrouping<TKey,TElement>` support.
- Phase 14 source-slot join composition is planned 0.8 finish-line work after Phase 13 and the single-source Phase 13B grouping slice: standard query-syntax joins, multiple explicit joins, and filtering/ordering/paging/counting over joined rows should build on the source-slot-aware plan instead of broadening the old Remotion boundary first.
- Phase 15 relation-aware and implicit joins is planned 0.8 finish-line work after Phase 14: `JoinBy(...)`, `JoinMany(...)`, implicit singular relation traversal, join-local `on:` predicates, left-join nullability semantics, and a `net10.0` `Queryable.LeftJoin(...)` support decision.
- The old global scalar-converter and typed-key ergonomics source plan owns provider/model value conversion after the provider-key cache design has room for it.
- Phase 16 dependency-tracked result and module caching remains deferred until cache invalidation, freshness vocabulary, joins, projection semantics, and the DataLinq.Store module contract are stronger.
- Phase 17 query plan and Remotion isolation has been superseded and implemented by the version-scoped 0.8 roadmap. It remains the detailed source plan and design record for DataLinq query plan, supported-subset parser, Remotion removal/isolation, and SQLitePCLRaw WebAssembly warning disposition.
- Completed phase records and superseded implementation plans have moved under `archive/`; active docs should describe future work or current strategy.

The next broad query-runtime feature work after the 0.8 AOT release gates is query-composition hardening, then the narrow grouped aggregate projection baseline, then source-slot join expansion: Phase 13 query composition and subquery pushdown, Phase 13B grouped aggregate projection, Phase 14 source-slot join composition, then Phase 15 relation-aware and implicit joins. The source-slot-aware query plan exists; the work is to preserve LINQ operator order, add the first SQL-shaped `GroupBy(...)` aggregate slice without pretending to support `IGrouping` materialization, and then build useful join composition on top of it without expanding the documented support matrix by wishful thinking.

For the current public parser architecture, use [LINQ Parser Architecture](../internals/LINQ%20Parser%20Architecture.md). The 0.8 dev-plan records explain migration sequence and evidence; the public architecture page explains the design that exists now: DataLinq-owned expression parsing, `DataLinqQueryPlan`, source slots, bindings, SQL rendering, cache-aware execution, row-local projection, and the explicit unsupported boundary.

The main thing not to blur at this stage is the boundary between implemented product-trust tooling and planned migration history. `validate` and `diff` are real. Full `add-migration`, `update-database`, runtime migration APIs, and applied-migration tracking are still future work.
