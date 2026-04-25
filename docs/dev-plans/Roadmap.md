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
- There is already a benchmark project in `src/DataLinq.Benchmark`, but it is still an experiment, not a trustworthy regression harness.
- `RowData` has already moved to dense indexed storage, so the performance roadmap should build on that rather than pretending memory optimization is still only theoretical.
- Metadata, source generation, SQL building, and instance materialization still contain meaningful runtime dynamism and avoidable allocation overhead.
- Product hardening features such as schema validation, migrations, and stronger diagnostics are still missing enough that DataLinq is easier to admire than to trust.

That last point matters. A fast ORM that is hard to validate or debug is still a risky tool.

## Roadmap Principles

The order below is opinionated on purpose.

1. Measure before optimizing.
2. Prefer foundational work that unlocks several later plans at once.
3. Prefer trust-building features over speculative capability expansion.
4. Treat clever APIs with suspicion when they hide I/O or increase magic.

## Priority Order

### Phase 1: Benchmarking and Observability

This is the immediate next phase.

Goals:

- turn `src/DataLinq.Benchmark` into a real BenchmarkDotNet harness
- establish deterministic benchmark datasets and baseline reports
- add lightweight observability for cache hits, cache misses, relation-cache hits, materializations, SQL generation, and cleanup activity
- create a nightly or CI lane for benchmark history and regression watching

Why first:

- performance plans without numbers are mostly storytelling
- observability is required to understand whether later optimizations help or merely feel sophisticated
- the current benchmark project is too ad hoc to support serious decisions

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

Key related plans:

- `metadata-and-generation/Metadata Architecture.md`
- `metadata-and-generation/Source Generator Optimizations.md`

### Phase 3: Query and Runtime Hot Path Optimization

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
- `query-and-runtime/Result set caching.md`

### Phase 4: Product Trust Features

Goals:

- implement schema validation and drift detection
- generate safe diff scripts
- define a migration/snapshot workflow

Why before broad feature expansion:

- this makes DataLinq safer to adopt in real projects
- it addresses a more important product weakness than adding one more clever capability

Key related plans:

- `providers-and-features/Migrations and Validation.md`

### Phase 5: Cache and Memory Optimization Phase 2

Goals:

- value deduplication and scoped interning
- key deduplication where it proves worthwhile
- cache compaction and better cleanup heuristics
- tighter cache invalidation and memory-pressure awareness

Why not first:

- memory work is easy to overdesign
- the repo already has part of the structural groundwork in place
- this should be driven by benchmark and observability evidence, not by aesthetic preference

Key related plans:

- `performance/Memory Optimization and Deduplication.md`
- `performance/Memory management.md`

### Phase 6: Async and Loading Semantics

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

### Phase 7: Capability Expansion

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

## What Should Happen Right Now

The next concrete stretch should be small and disciplined:

1. Rebuild `src/DataLinq.Benchmark` into a real harness instead of a local experiment.
2. Add a minimal observability layer with counters and debug summaries for the cache and query pipeline.
3. Use those numbers to decide whether the first runtime target is instance creation, SQL generation, cache behavior, or something else.

That is the right next move because it converts the roadmap from intuition into evidence.

## What Is Explicitly Not First

These may still be good ideas, but they should not lead the queue:

- broad provider expansion
- in-memory provider as a flagship initiative
- large documentation rewrites unrelated to immediate product clarity
- query abstraction for hypothetical future backends before the current SQL path is fully measured
- committing to a magical lazy-loading async API before sync/async boundaries are tested and defended

## Review Trigger

This roadmap should be revisited when any of the following happens:

- benchmark data contradicts the assumed hot paths
- a major product requirement appears that changes the order
- async support becomes urgent for a concrete target scenario
- validation or migration work proves more important than expected during adoption testing
