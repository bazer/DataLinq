# Archived Roadmap Implementation Notes

This folder contains historical roadmap implementation checkpoints that are no longer active execution guidance.

## Archived Documents

- `phase-1-benchmarking-and-observability/`
  Implemented benchmark and observability foundation.
- `phase-2-metadata-generator-and-diagnostics-hardening/`
  Implemented metadata, generator, and diagnostics hardening foundation.
- `phase-3-query-and-runtime-hot-path-optimization/`
  Implemented query/runtime allocation reduction slice.
- `phase-4-provider-metadata-roundtrip-fidelity/`
  Implemented provider metadata roundtrip support boundary for validation.
- `phase-4b-provider-fidelity-hardening/`
  Implemented provider fidelity follow-up for referential actions, defaults, generated columns, views, and related guardrails.
- `phase-5-product-trust-features/`
  Implemented validation, conservative diffing, and snapshot-scoping boundary. Full migration execution remains future work.
- `phase-6-linq-translation-coverage-and-query-composition/`
  Implemented LINQ translation coverage and query-composition boundary.
- `phase-7-linq-feature-expansion/`
  Implemented scalar aggregate, projection, nullable predicate, join, and relation predicate expansion boundary.
- `phase-8-native-aot-and-webassembly-readiness/`
  Implemented generated SQLite Native AOT, trimming, and WebAssembly AOT smoke boundary.
- `phase-8b-practical-aot-and-package-graph-hardening/`
  Implemented generated-contract and immutable metadata foundation.
- `phase-8c-practical-aot-package-graph-and-generated-runtime-hardening/`
  Implemented package graph, complete generated metadata startup, generated indexed access, and public compatibility wording cleanup.
- `phase-9a-release-hardening-benchmarks-allocation-cache-invalidation/`
  Implemented release-hardening, benchmark-history, allocation, and conservative cache-invalidation cleanup.
- `phase-10-key-and-allocation-foundation/`
  Implemented metadata collection cleanup, frozen lookups, provider-key row stores, generated relation access, scalar-converter seams, and allocation closeout evidence.
- `phase-11-cache-clearing-and-external-invalidation/`
  Implemented explicit database/table/provider-key invalidation APIs, relation/index invalidation, invalidation envelopes, freshness vocabulary, and cache telemetry.
- `phase-12-memory-pressure-cleanup-and-measured-deduplication/`
  Implemented estimated cache memory accounting, estimated-footprint byte limits, memory-pressure-aware cleanup, cleanup telemetry, and benchmark-led rejection of production value/key deduplication.
- `Phase 4 and 5 Status Review.md`
  Historical closeout review from 2026-05-02. It explains the provider-fidelity and product-trust boundary that later phases consumed, but its "start Phase 6 next" recommendation is no longer current.

Use `../../roadmap-implementation/README.md` and `../../../Roadmap.md` for the current roadmap position.
