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
| Phase 5: Product Trust Features | Substantially implemented; full migration execution deferred | `phase-5-product-trust-features/` |

## Current Roadmap Position

As of 2026-05-02, the active roadmap frontier is between Phase 5 and Phase 6.

Phase 4 has the support matrix and provider roundtrip boundary that schema validation needed. Phase 5 has the comparer, validation CLI, conservative diff-script generator, and first snapshot migration contract. The remaining Phase 5 gap is not drift detection; it is full versioned migration execution.

Unless migration execution becomes the immediate product priority, the next implementation plan should be Phase 6 LINQ translation coverage and query composition.

For the consolidated checkpoint, see [Phase 4 and 5 Status Review](Phase%204%20and%205%20Status%20Review.md).

## Notes

- These documents should stay grounded in the current codebase and benchmark harness, not in idealized architecture sketches.
- If a phase plan disagrees with `Roadmap.md`, the roadmap should usually be updated rather than letting the two silently diverge.
- A phase plan should be implementation-oriented: workstreams, sequencing, risks, verification, and exit criteria.
