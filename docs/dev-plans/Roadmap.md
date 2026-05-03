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
- Provider metadata roundtrip fidelity now has an explicit support boundary for SQLite, MySQL, and MariaDB, including tested coverage for the ordinary table/column/index/relation subset and documented unsupported provider details.
- Schema validation and conservative diff-script tooling now exist for that supported subset; full versioned migration execution remains intentionally deferred.

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

Status: planned; implementation plan created.

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

Status: planned; implementation plan created.

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

Goals:

- remove hot-path `Expression.Compile()` usage where generated or interpreted alternatives are practical
- define generated materializer and projection paths for AOT-sensitive execution
- audit trimming compatibility and reflection-heavy discovery paths
- prove the SQLite/WebAssembly story with a small Blazor WASM sample
- review cache worker and threading behavior for browser/WASM environments

Why here:

- Phase 2 created some of the generator hooks this work needs, but it did not eliminate every AOT-hostile path
- Phase 3 made the query/runtime path cheaper first, so the AOT/WASM work starts from the cleaner runtime shape
- Phase 7 should clarify projection and relation-query execution paths before AOT locks down more runtime behavior
- platform compatibility is concrete enough to deserve a real phase, but not urgent enough to interrupt the current hot-path work

Key related plans:

- `platform-compatibility/AOT and WebAssembly Strategy.md`
- `metadata-and-generation/Source Generator Optimizations.md`

### Phase 9: Cache, Memory, and Invalidation Foundations

Goals:

- value deduplication and scoped interning
- key deduplication where it proves worthwhile
- cache compaction and better cleanup heuristics
- tighter cache invalidation and memory-pressure awareness
- row versioning or hash-based freshness primitives where they prove necessary

Why not first:

- memory work is easy to overdesign
- the repo already has part of the structural groundwork in place
- this should be driven by benchmark and observability evidence, not by aesthetic preference
- dependency-tracked result-set caching depends on this work and should not force these foundations into Phase 3

Key related plans:

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

## What Should Happen Right Now

Phase 4 is no longer the next concrete stretch. It has done its job: DataLinq now has a documented provider metadata support boundary that Phase 5 could consume.

Phase 5 is now closed for roadmap purposes as the product-trust groundwork phase:

1. `SchemaComparer` reports deterministic drift for the supported SQLite/MySQL/MariaDB metadata subset.
2. `datalinq validate` exposes that comparison through the public CLI.
3. `SchemaDiffScriptGenerator` and `datalinq diff` generate conservative SQL suggestions for additive changes and comment out destructive or ambiguous drift.
4. `SchemaMigrationSnapshot` and the snapshot design document define the next migration-history contract without pretending full migration execution exists.

The final closeout pass confirmed the generators, unit suite, SQLite compliance lane, and MariaDB validation/provider lanes.

Phase 6 LINQ translation coverage and query composition is now implemented for its planned support boundary: support-matrix audit, chained `Where(...)`, projected local `Contains(...)`, local object-list `Any(predicate)`, fixed true/false condition handling, and better unsupported-query diagnostics.

The next roadmap phase should therefore be Phase 7: LINQ feature expansion, unless we deliberately pause to prioritize full migration execution.

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
