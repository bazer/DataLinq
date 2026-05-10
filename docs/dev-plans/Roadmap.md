> [!WARNING]
> This document is roadmap material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# DataLinq Roadmap

**Status:** Active planning document

## Purpose

This roadmap exists to answer one practical question:

What should DataLinq do next, in what order, and why?

The answer should be grounded in the repo as it exists today, not in abstract ORM wishlists.

## Current Baseline

Several important things are already true:

- The test migration is complete. The active suite structure is TUnit-based, CI-backed, and the legacy xUnit projects are gone.
- There is now a benchmark and observability foundation to build on, including the Phase 3 query/runtime hot-path lane used to keep optimization claims honest.
- `RowData` has already moved to dense indexed storage, so the performance roadmap should build on that rather than pretending memory optimization is still only theoretical.
- Metadata and generator hardening have removed some avoidable runtime work, but SQL building, query translation, projection materialization, and compatibility-sensitive runtime paths still contain meaningful dynamism and allocation overhead.
- The runtime metadata graph now has a Phase 8B factory/freeze boundary for ordinary construction. A complete generated metadata switch should target that builder-built snapshot path rather than reviving reflection-heavy startup.
- Provider metadata roundtrip fidelity now has an explicit support boundary for SQLite, MySQL, and MariaDB, including tested coverage for the ordinary table/column/index/relation subset and documented unsupported provider details.
- Schema validation and conservative diff-script tooling now exist for that supported subset; full versioned migration execution remains intentionally deferred.
- Phase 8 proved generated SQLite models under Native AOT, trimming, and Blazor WebAssembly AOT, but it also exposed the practical compatibility debt: Roslyn still leaks into constrained publish payloads, `Remotion.Linq` still produces AOT/trimming warnings, SQLitePCLRaw still emits WebAssembly native varargs warnings, and no-AOT browser WebAssembly is not supportable for the SQLite/DataLinq path yet.

That last point matters. A fast ORM that is hard to validate or debug is still a risky tool.

## Roadmap Principles

The order below is opinionated on purpose.

1. Measure before optimizing.
2. Prefer foundational work that unlocks several later plans at once.
3. Prefer trust-building features over speculative capability expansion.
4. Treat clever APIs with suspicion when they hide I/O or increase magic.

## Priority Order

### Phase 1: Benchmarking and Observability

Status: mostly implemented.

Goals:

- turn `src/DataLinq.Benchmark` into a real BenchmarkDotNet harness
- establish deterministic benchmark datasets and baseline reports
- add lightweight observability for cache hits, cache misses, relation-cache hits, materializations, SQL generation, and cleanup activity
- create a nightly or CI lane for benchmark history and regression watching

Why first:

- performance plans without numbers are mostly storytelling
- observability is required to understand whether later optimizations help or merely feel sophisticated
- the original benchmark project was too ad hoc to support serious decisions, which is why this foundation had to come first

Exit criteria:

- a small set of trusted benchmark scenarios exists
- benchmark results are reproducible
- runtime counters expose the main hot-path behaviors

### Phase 2: Metadata, Generator, and Diagnostics Hardening

Status: implemented.

Goals:

- reduce runtime reflection and expression-based construction where code generation can do the work instead
- strengthen metadata architecture for structural equality and future indexed/runtime work
- improve generator diagnostics so failures point to useful source locations instead of collapsing into generic failures

Why second:

- this is high-leverage work that improves startup, AOT-friendliness, and developer experience at the same time
- it also prepares the ground for later runtime and memory optimizations

Important caveat:

- this phase improved AOT-readiness, but it did not complete Native AOT or WebAssembly support; runtime expression compilation and trimming-sensitive paths still need their own platform-readiness work

Key related plans:

- `metadata-and-generation/Metadata Architecture.md`
- `metadata-and-generation/Source Generator Optimizations.md`

### Phase 3: Query and Runtime Hot Path Optimization

Status: implemented.

Goals:

- reduce allocations in SQL generation
- move parameter handling closer to execution time
- separate reusable SQL templates from dynamic bindings where it is worth doing
- continue removing unnecessary hot-path object churn

Why here:

- after Phase 1, this should be measurable
- after Phase 2, this can build on stronger metadata and generation primitives instead of fighting them

Key related plans:

- `query-and-runtime/Sql Generation Optimization.md`
- `roadmap-implementation/phase-3-query-and-runtime-hot-path-optimization/Implementation Plan.md`

### Phase 4: Provider Metadata Roundtrip Fidelity

Status: implemented for the validation support boundary; broader provider DDL fidelity remains intentionally scoped.

Goals:

- audit MySQL, MariaDB, and SQLite metadata readers and SQL generators
- define the supported provider metadata roundtrip subset
- add create-read-generate-create-read tests for supported schema features
- fix ordinary metadata holes around indexes, relations, comments, checks, and quoted identifiers
- explicitly document advanced provider syntax that remains unsupported

Why before schema validation:

- validation can only compare metadata DataLinq actually preserves
- current relation/index metadata is good enough for basics but too ambiguous for full trust
- unsupported DDL features are acceptable only when they are visible, tested, and documented

Key related plans:

- `providers-and-features/Provider Metadata Roundtrip Fidelity.md`
- `roadmap-implementation/phase-4-provider-metadata-roundtrip-fidelity/Implementation Plan.md`

### Phase 4B: Provider Fidelity Hardening

Status: implemented as a focused follow-up to the Phase 4 matrix review.

Why this phase exists:

- the Phase 4 matrix still has several practical partials that are cheap enough to fix before moving on
- referential actions are expected DDL behavior and should not disappear during metadata roundtrips
- unsupported provider index and generated-column shapes should be explicit warnings, not misleading metadata
- raw provider default expressions need a provider-scoped representation rather than string-literal abuse
- schema validation should include views at the safe presence and column boundary
- MySQL/MariaDB column ordering should use provider ordinals, not incidental information-schema query order

Execution plan:

- `roadmap-implementation/phase-4b-provider-fidelity-hardening/Implementation Plan.md`

### Phase 5: Product Trust Features

Status: implemented for validation, conservative diffing, and snapshot scoping; full versioned migration execution is deferred with a concrete snapshot design.

Goals:

- implement schema validation and drift detection using the Phase 4 support boundary
- generate safe diff scripts
- define a migration/snapshot workflow
- avoid runtime auto-migration until validation, diffing, and migration history semantics are proven

Why before broad feature expansion:

- this makes DataLinq safer to adopt in real projects
- it addresses a more important product weakness than adding one more clever capability
- it needs provider metadata fidelity first, otherwise drift reports will be built on partial facts

Key related plans:

- `providers-and-features/Migrations and Validation.md`
- `roadmap-implementation/phase-5-product-trust-features/Implementation Plan.md`
- `roadmap-implementation/phase-5-product-trust-features/Snapshot Migration Design.md`

### Phase 6: LINQ Translation Coverage and Query Composition

Status: implemented.

Goals:

- document the real LINQ-to-SQL support matrix
- fix common local-collection predicate shapes such as projected `Contains` and object-list `Any(predicate)`
- make chained `Where` composition reliably preserve all predicates
- harden fixed true/false condition handling for empty collections and boolean grouping
- improve translation diagnostics for unsupported query shapes

Why here:

- these are ordinary application-query patterns, not speculative provider expansion
- the current parser already supports enough to justify tightening gaps instead of rewriting it
- AOT and broader query-pipeline work will be cleaner if supported and unsupported expression shapes are classified first

Key related plans:

- `roadmap-implementation/phase-6-linq-translation-coverage-and-query-composition/Implementation Plan.md`
- `query-and-runtime/LINQ Translation Support.md`
- `query-and-runtime/Query Pipeline Abstraction.md`

### Phase 7: LINQ Feature Expansion

Status: implemented.

Goals:

- add simple scalar aggregates: `Sum`, `Min`, `Max`, and `Average`
- expand projection support for computed but defensible selectors
- make nullable predicate support boring and explicitly documented
- add a narrow explicit LINQ `Join` baseline
- design and implement relation-aware query predicates over generated relation properties

Why here:

- Phase 6 classified the LINQ translator surface and made unsupported shapes fail clearly
- aggregates, projections, joins, and relation predicates are now the practical gaps users will hit next
- relation-aware translation is important enough to design deliberately instead of smuggling it into a cleanup phase
- these features are more immediately application-facing than platform compatibility work

Key related plans:

- `roadmap-implementation/phase-7-linq-feature-expansion/Implementation Plan.md`
- `query-and-runtime/LINQ Translation Support.md`
- `query-and-runtime/Query Pipeline Abstraction.md`

### Phase 8: Native AOT and WebAssembly Readiness

Status: implemented for the generated SQLite Native AOT, trimming, and WebAssembly AOT boundary.

Goals:

- remove hot-path `Expression.Compile()` usage where generated or interpreted alternatives are practical
- define generated materializer and projection paths for AOT-sensitive execution
- audit trimming compatibility and reflection-heavy discovery paths
- prove the SQLite/WebAssembly story with a small Blazor WASM sample
- review cache worker and threading behavior for browser/WASM environments

Why here:

- Phase 2 created some of the generator hooks this work needs, but it did not eliminate every AOT-hostile path
- Phase 3 made the query/runtime path cheaper first, so the AOT/WASM work starts from the cleaner runtime shape
- Phase 7 clarified projection and relation-query execution paths before AOT locks down more runtime behavior
- platform compatibility is concrete enough to deserve a real phase, but not urgent enough to interrupt the current hot-path work

Key related plans:

- `roadmap-implementation/phase-8-native-aot-and-webassembly-readiness/Implementation Plan.md`
- `roadmap-implementation/phase-8-native-aot-and-webassembly-readiness/Compatibility Results.md`
- `roadmap-implementation/phase-8b-practical-aot-and-package-graph-hardening/README.md`
- `roadmap-implementation/phase-8b-practical-aot-and-package-graph-hardening/Implementation Plan.md`
- `roadmap-implementation/phase-8c-practical-aot-package-graph-and-generated-runtime-hardening/README.md`
- `roadmap-implementation/phase-8c-practical-aot-package-graph-and-generated-runtime-hardening/Implementation Plan.md`
- `platform-compatibility/AOT and WebAssembly Strategy.md`
- `platform-compatibility/Practical AOT and Size Plan.md`
- `metadata-and-generation/Source Generator Optimizations.md`
- `query-and-runtime/Remotion.Linq Replacement Plan.md`

### Phase 8B: Generated Contract and Immutable Metadata Foundation

Status: complete for the generated-contract and immutable metadata foundation.

Goals:

- make generated hooks and generated metadata a strict fail-fast runtime contract
- remove stale generated-hook compatibility shims that hide broken or stale generated output
- introduce builder-built immutable runtime metadata definitions before switching generated startup to complete metadata
- move normal metadata production onto typed drafts and the factory path
- freeze factory-built runtime snapshots against ordinary mutation
- obsolete public mutable construction APIs where the product no longer needs them

Why before generated package/runtime work:

- Phase 8 produced real proof, but not a clean product support story
- silent generated-hook fallback is incompatible with a credible generated/AOT support boundary
- generated complete metadata should not target the current mutable definition graph because that would preserve the wrong construction model
- package graph cleanup and complete generated metadata startup needed a factory-owned snapshot foundation first

Key related plans:

- `roadmap-implementation/phase-8b-practical-aot-and-package-graph-hardening/README.md`
- `roadmap-implementation/phase-8b-practical-aot-and-package-graph-hardening/Implementation Plan.md`
- `metadata-and-generation/Generated Metadata Contract and Runtime Fallback Removal.md`
- `metadata-and-generation/Immutable Metadata Definitions and Factory Plan.md`

### Phase 8C: Practical AOT Package Graph and Generated Runtime Hardening

Status: planned follow-up, but not the current priority unless constrained-platform polish is chosen ahead of memory/cache work.

Goals:

- add repeatable size reports and banned-payload checks for AOT, trimmed, and WebAssembly publishes
- split Roslyn/compiler dependencies out of the runtime package graph
- remove `Microsoft.CodeAnalysis.*` from `DataLinq.dll` runtime dependency groups and constrained publish outputs
- switch generated-model startup to require complete generated metadata through the Phase 8B factory path
- remove runtime reflection metadata discovery instead of preserving it as a compatibility fallback
- generate indexed value access, relation handles, and mutable metadata handles
- inspect packed package assets, not only project references
- keep public compatibility wording narrow until the later query-boundary work is complete

Why separate from Phase 8B:

- Workstream C became a real implementation phase, not a small prerequisite
- runtime package cleanup should not be mixed with the historical metadata foundation log
- generated startup and indexed access are important, but they are not the same job as replacing the query parser
- this slice can improve package/runtime hygiene without blocking memory/cache work on the larger Remotion replacement

Key related plans:

- `roadmap-implementation/phase-8c-practical-aot-package-graph-and-generated-runtime-hardening/README.md`
- `roadmap-implementation/phase-8c-practical-aot-package-graph-and-generated-runtime-hardening/Implementation Plan.md`
- `platform-compatibility/Practical AOT and Size Plan.md`

### Phase 9A: Release Hardening, Benchmarks, Allocation, and Cache Invalidation

Status: complete as of 2026-05-10.

Goals:

- complete the warning cleanup plan and establish a credible warning baseline before deeper runtime changes
- upgrade the benchmark history and website trend surface so future performance work has visible long-term evidence
- implement the allocation-reduction audit workstreams: metadata collection shape, frozen metadata lookups, non-allocating key value access, generated metadata startup allocation, query temporary-array cleanup, and cache internals cleanup
- characterize cache invalidation behavior with tests before changing semantics
- harden cache invalidation around updates, deletes, changed relation/index columns, transaction commit/rollback boundaries, and cache notification subscribers
- clean low-risk cache internals such as lazy cache snapshots, `IndexCache` reverse-map concurrency, and `RowCache.TotalBytes`
- add benchmark and telemetry coverage that can prove whether cache invalidation became narrower, broader, or noisier

Closeout result:

- warning cleanup, benchmark-history website work, allocation reductions, cache invalidation characterization, and conservative cache internals hardening are complete
- final default-profile `sqlite-memory` benchmark closeout supports allocation and invalidation claims, but not latency claims because every timing comparison row was noisy
- Phase 9B should start from the new invalidation tests, cache-maintenance telemetry, and profile-aware benchmark history rather than revisiting Phase 9A cleanup

Why before the broader cache redesign:

- warning noise should not be carried into a cache-semantics phase
- allocation work is already measured and concrete enough to execute now
- benchmark/trend work makes the rest of Phase 9 falsifiable instead of anecdotal
- cache invalidation correctness is a foundation for row freshness, external invalidation, adaptive heuristics, and later result-set caching
- this phase is large enough already; row hashing, external invalidation, and adaptive cache policy should not be mixed into the same release

Key related plans:

- `roadmap-implementation/phase-9a-release-hardening-benchmarks-allocation-cache-invalidation/README.md`
- `roadmap-implementation/phase-9a-release-hardening-benchmarks-allocation-cache-invalidation/Implementation Plan.md`
- `tooling/Warning Cleanup Plan.md`
- `performance/Representative Benchmark Suite and Website Trends.md`
- `performance/Allocation Reduction Audit.md`
- `performance/Memory Optimization and Deduplication.md`
- `performance/Memory management.md`

### Phase 9B: Row Freshness, External Invalidation, and Adaptive Cache Policy

Status: next cache-semantics priority.

Goals:

- introduce row versioning or hash-based freshness primitives where they prove necessary
- add explicit external invalidation hooks for host applications and event-driven integrations
- implement adaptive cache heuristics only after Phase 9A telemetry can show whether they help
- add memory-pressure-aware cleanup behavior and better cleanup scheduling
- evaluate value/key deduplication and scoped interning with benchmark evidence before adopting global caches
- keep cache behavior observable enough that users can tell whether invalidation came from mutation, external signal, freshness check, cleanup, or memory pressure

Why after Phase 9A:

- row hashing and external invalidation are product semantics, not cleanup
- adaptive heuristics are dangerous without a measured baseline and clear override story
- global key/value deduplication can easily add contention or retention bugs if it is not benchmark-led
- dependency-tracked result-set caching depends on these foundations but remains a later semantic feature, not part of Phase 9B

Key related plans:

- `roadmap-implementation/phase-9b-row-freshness-external-invalidation-adaptive-cache-policy/README.md`
- `roadmap-implementation/phase-9b-row-freshness-external-invalidation-adaptive-cache-policy/Implementation Plan.md`
- `architecture/Distributed Cache Coordination and CDC.md`
- `performance/Memory Optimization and Deduplication.md`
- `performance/Memory management.md`

### Phase 10: Async and Loading Semantics

Goals:

- define a serious async provider pipeline
- decide how explicit or implicit lazy loading should be
- add strong sync/async boundary rules before widening the public API

Why later:

- async support matters, but it is also easy to make deceptively magical and difficult to reason about
- this work benefits from benchmark data, observability, and stronger runtime primitives
- DataLinq should not commit too early to a cute API that hides expensive I/O

Important stance:

- first-class async query and mutation APIs are good
- hidden sync fallback on property access is dangerous and must remain tightly controlled if it exists at all
- explicit preload/include mechanisms should remain the primary answer to N+1 issues

Key related plans:

- `query-and-runtime/Async and Lazy Loading.md`

### Phase 11: Capability Expansion

Goals:

- add features that expand scope after the core is measured and hardened
- examples include JSON columns, in-memory provider work, broader query pipeline abstraction, projections/views, and batched mutations

Why last:

- these features are easier to justify after the core product is more trustworthy
- several of them depend on earlier architectural work anyway

Key related plans:

- `metadata-and-generation/Scalar Converter Support.md`
- `providers-and-features/JSON Data Type Support.md`
- `providers-and-features/In-Memory Provider.md`
- `query-and-runtime/Query Pipeline Abstraction.md`
- `query-and-runtime/Projections and Views.md`
- `query-and-runtime/Batched mutations.md`

### Phase 12: Dependency-Tracked Result-Set Caching

Goals:

- support explicit cached computation scopes
- record dependency fingerprints for rows read during a computation
- validate stamped results against current row version markers
- integrate cached result invalidation with the cache/memory foundations rather than arbitrary TTLs

Why last:

- this is not SQL-generation optimization; it is a semantic caching feature
- it depends on row freshness/versioning, invalidation behavior, projection/view semantics, and observability
- shipping it too early would create a clever cache whose correctness story is harder to defend than the performance win

Key related plans:

- `query-and-runtime/Result set caching.md`
- `query-and-runtime/Projections and Views.md`
- `performance/Memory management.md`

### Phase 13: Query Plan and Remotion Isolation

Status: deferred to the back of the roadmap.

Goals:

- introduce a DataLinq-owned query plan behind the current Remotion parser
- move SQL generation and supported query diagnostics behind that plan
- build a supported-subset expression parser that can serve the generated/AOT path
- remove or isolate `Remotion.Linq` from the practical AOT support boundary
- investigate SQLitePCLRaw WebAssembly warnings with exact call-path evidence
- keep no-AOT browser WebAssembly unsupported unless it actually runs

Why last:

- this is a query-pipeline migration, not a cleanup task
- it has high regression risk across the LINQ support matrix
- memory/cache work is more important right now
- Phase 8C can clean the package/generated-runtime surface without forcing a parser rewrite

Key related plans:

- `roadmap-implementation/phase-13-query-plan-and-remotion-isolation/README.md`
- `roadmap-implementation/phase-13-query-plan-and-remotion-isolation/Implementation Plan.md`
- `query-and-runtime/Remotion.Linq Replacement Plan.md`
- `../support-matrices/LINQ Translation Support Matrix.md`

## What Should Happen Right Now

Phase 4 is no longer the next concrete stretch. It has done its job: DataLinq now has a documented provider metadata support boundary that Phase 5 could consume.

Phase 5 is now closed for roadmap purposes as the product-trust groundwork phase:

1. `SchemaComparer` reports deterministic drift for the supported SQLite/MySQL/MariaDB metadata subset.
2. `datalinq validate` exposes that comparison through the public CLI.
3. `SchemaDiffScriptGenerator` and `datalinq diff` generate conservative SQL suggestions for additive changes and comment out destructive or ambiguous drift.
4. `SchemaMigrationSnapshot` and the snapshot design document define the next migration-history contract without pretending full migration execution exists.

The final closeout pass confirmed the generators, unit suite, SQLite compliance lane, and MariaDB validation/provider lanes.

Phase 6 LINQ translation coverage and query composition is implemented for its planned support boundary: support-matrix audit, chained `Where(...)`, projected local `Contains(...)`, local object-list `Any(predicate)`, fixed true/false condition handling, and better unsupported-query diagnostics.

Phase 7 LINQ feature expansion is implemented for its planned support boundary: scalar aggregates, computed post-materialization projections, nullable predicate polish, a narrow explicit `Join(...)` baseline, and one-to-many relation existence predicates.

Phase 8 Native AOT and WebAssembly readiness is implemented for its planned generated SQLite boundary: Native AOT publish/run, trimmed publish/run, Blazor WebAssembly AOT publish/browser smoke, generated metadata/factory enforcement, hot-path projection compilation removal, and browser cache-worker avoidance. The remaining caveats are real and should not be hand-waved: no-AOT browser WebAssembly fails in the Mono interpreter, `Remotion.Linq` still produces AOT/trimming warnings, SQLitePCLRaw emits WebAssembly native varargs warnings, and Roslyn still leaks into constrained publish payloads.

Phase 8B is now the completed generated-contract and immutable metadata foundation. Phase 8C remains the bounded package/generated-runtime cleanup slice for constrained-platform polish, but it should not drag the Remotion/parser rewrite back into the critical path.

Phase 9A is now complete: warning cleanup, benchmark/history improvements, allocation reduction, conservative cache invalidation hardening, and benchmark closeout evidence have landed. The important caveat is performance wording: the closeout supports allocation and invalidation claims, not latency claims.

The next broad runtime priority should be Phase 9B: row freshness/hash primitives, external invalidation hooks, and adaptive cache policy. Those are high-value next steps, and they can now build on Phase 9A's tests, telemetry, and benchmark baselines. Dependency-tracked result-set caching should remain later on the roadmap.

Full `add-migration` / `update-database` work should remain a dedicated future feature. The migration foundation is now concrete enough to resume later without guessing, but folding execution into this phase would blur a useful boundary.

## What Is Explicitly Not First

These may still be good ideas, but they should not lead the queue:

- broad provider expansion
- in-memory provider as a flagship initiative
- dependency-tracked result-set caching
- large documentation rewrites unrelated to immediate product clarity
- query abstraction for hypothetical future backends before the current SQL path is fully measured
- committing to a magical lazy-loading async API before sync/async boundaries are tested and defended

## Review Trigger

This roadmap should be revisited when any of the following happens:

- benchmark data contradicts the assumed hot paths
- a major product requirement appears that changes the order
- async support becomes urgent for a concrete target scenario
- validation or migration work proves more important than expected during adoption testing
