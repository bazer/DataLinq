> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.
# Roadmap Implementation

## Purpose

`docs/dev-plans/roadmap-implementation` is where DataLinq turns the high-level roadmap into concrete execution plans.

`Roadmap.md` answers:

What should happen next, and why?

This folder answers:

What are we actually going to do, in what order, and how will we know it worked?

## Structure

| Phase | Status | Directory |
| --- | --- | --- |
| Phase 1: Benchmarking and Observability | Substantially implemented; benchmark-history review remains useful | `phase-1-benchmarking-and-observability/` |
| Phase 2: Metadata, Generator, and Diagnostics Hardening | Implemented | `phase-2-metadata-generator-and-diagnostics-hardening/` |
| Phase 3: Query and Runtime Hot Path Optimization | Implemented | `phase-3-query-and-runtime-hot-path-optimization/` |
| Phase 4: Provider Metadata Roundtrip Fidelity | Implemented for the validation support boundary | `phase-4-provider-metadata-roundtrip-fidelity/` |
| Phase 4B: Provider Fidelity Hardening | Implemented | `phase-4b-provider-fidelity-hardening/` |
| Phase 5: Product Trust Features | Implemented for validation/diff/snapshot scope; full migration execution deferred | `phase-5-product-trust-features/` |
| Phase 6: LINQ Translation Coverage and Query Composition | Implemented | `phase-6-linq-translation-coverage-and-query-composition/` |
| Phase 7: LINQ Feature Expansion | Implemented | `phase-7-linq-feature-expansion/` |
| Phase 8: Native AOT and WebAssembly Readiness | Implemented for generated SQLite AOT/WASM boundary | `phase-8-native-aot-and-webassembly-readiness/` |
| Phase 8B: Practical AOT and Package Graph Hardening | Active recommended follow-up before a broad public AOT support claim | `phase-8b-practical-aot-and-package-graph-hardening/` |

## Current Roadmap Position

As of the Phase 8 planning-doc review on 2026-05-05, Phase 8 is closed for its planned smoke boundary, but the active implementation frontier should not jump straight to a broad "AOT-compatible ORM" story.

Phase 4 has the support matrix and provider roundtrip boundary that schema validation needed. Phase 5 has the comparer, validation CLI, conservative diff-script generator, and first snapshot migration contract. Full versioned migration execution is not a remaining Phase 5 cleanup task; it is a separate future product surface.

Phase 4B plugged the most useful Phase 4 metadata gaps before the roadmap moved on: referential actions, advanced index guardrails, generated-column guardrails, raw provider defaults, explicit MySQL/MariaDB column ordering, and view validation.

The Phase 6 plan completed the support-matrix audit, chained `Where(...)` correctness, projected local `Contains(...)`, equality-based local object-list `Any(predicate)` expansion, fixed-condition invariants, and unsupported-query diagnostics.

The Phase 7 plan completed scalar aggregates, projection expansion, nullable predicate polish, a narrow LINQ `Join(...)` baseline, and relation-aware predicate translation.

The Phase 8 plan closed with executable generated SQLite smoke coverage for Native AOT, trimmed publish, and Blazor WebAssembly AOT. The result is intentionally scoped: generated hooks are now required for the AOT path, hot-path `Expression.Compile()` use has been removed from the checked LINQ/instance surface, and browser cache startup avoids the cleanup worker. Broad public compatibility still needs dependency cleanup around `Remotion.Linq`, SQLitePCLRaw WebAssembly varargs warnings, no-AOT interpreter failures, and Roslyn payload leakage.

The next recommended implementation slice is Phase 8B: practical AOT and package graph hardening. That means size reporting, removing Roslyn from the runtime graph, introducing the query-plan boundary needed to replace or isolate Remotion, and investigating SQLitePCLRaw WebAssembly warnings. Phase 9 cache, memory, and invalidation foundations remain the next broad runtime phase after that, unless full migration execution becomes the immediate product priority.

For the older Phase 4/5 checkpoint, see the archived [Phase 4 and 5 Status Review](../archive/roadmap-implementation/Phase%204%20and%205%20Status%20Review.md).

## Notes

- These documents should stay grounded in the current codebase and benchmark harness, not in idealized architecture sketches.
- If a phase plan disagrees with `Roadmap.md`, the roadmap should usually be updated rather than letting the two silently diverge.
- A phase plan should be implementation-oriented: workstreams, sequencing, risks, verification, and exit criteria.
