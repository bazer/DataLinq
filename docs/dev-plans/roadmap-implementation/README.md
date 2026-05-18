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

Active or deferred execution plans:

| Phase | Status | Directory |
| --- | --- | --- |
| Phase 12B: Generation Trust and Diagnostics Hardening | Complete as of 2026-05-14 | `phase-12b-generation-trust-and-diagnostics-hardening/` |
| Phase 12C: CLI Configuration and Regeneration Workflow | Complete as of 2026-05-18 | `phase-12c-cli-configuration-and-regeneration-workflow/` |
| Phase 13: Explicit Multi-Join Composition | Next implementation priority | `phase-13-explicit-multi-join-composition/` |
| Phase 14: Relation-Aware Joins and Left Joins | Planned join API phase | `phase-14-relation-aware-joins-and-left-joins/` |
| Phase 15: Scalar Converters and Typed-Key Ergonomics | Planned conversion/key ergonomics phase | `phase-15-scalar-converters-and-typed-key-ergonomics/` |
| Phase 16: Dependency-Tracked Result-Set Caching | Deferred semantic cache phase | `phase-16-dependency-tracked-result-set-caching/` |
| Phase 17: Query Plan and Remotion Isolation | Deferred query-boundary and WebAssembly warning phase | `phase-17-query-plan-and-remotion-isolation/` |

Completed execution records for Phases 1 through 12 live in [`../archive/roadmap-implementation/README.md`](../archive/roadmap-implementation/README.md).

## Current Roadmap Position

As of 2026-05-18, Phases 1 through 12C are closed execution history. Phase 12B completed generation-trust hardening, and Phase 12C completed the CLI, configuration, regeneration, schema, diagnostics, and secrets workflow before returning to query API expansion. Phase 13 is now the next implementation priority.

Phase 4 has the support matrix and provider roundtrip boundary that schema validation needed. Phase 5 has the comparer, validation CLI, conservative diff-script generator, and first snapshot migration contract. Full versioned migration execution is not a remaining Phase 5 cleanup task; it is a separate future product surface.

Phase 4B plugged the most useful Phase 4 metadata gaps before the roadmap moved on: referential actions, advanced index guardrails, generated-column guardrails, raw provider defaults, explicit MySQL/MariaDB column ordering, and view validation.

The Phase 6 plan completed the support-matrix audit, chained `Where(...)` correctness, projected local `Contains(...)`, equality-based local object-list `Any(predicate)` expansion, fixed-condition invariants, and unsupported-query diagnostics.

The Phase 7 plan completed scalar aggregates, projection expansion, nullable predicate polish, a narrow LINQ `Join(...)` baseline, and relation-aware predicate translation.

The Phase 8 plan closed with executable generated SQLite smoke coverage for Native AOT, trimmed publish, and Blazor WebAssembly AOT. Phase 8B closed the generated-contract and immutable metadata foundation. Phase 8C then completed the bounded package/generated-runtime cleanup: repeatable size reports, Roslyn removal from runtime dependency groups, complete generated metadata startup, runtime reflection metadata-discovery removal, generated indexed access, and package/public wording.

The query-plan, Remotion isolation, supported-subset parser, and SQLitePCLRaw warning work remain Phase 17 at the back of the roadmap.

Phase 9A is complete for warning cleanup, benchmark-history and website trends, allocation reduction, and conservative cache invalidation hardening. Its benchmark closeout supports allocation and invalidation claims, not latency claims.

Phase 10 is complete for the key/allocation foundation: metadata collection and lookup cleanup, provider-key row stores, generated relation access, query/materialization provider-key reads, scalar-converter seams, and Phase 11 handoff artifacts. Phase 11 is complete for explicit cache clearing, external invalidation, relation/index invalidation, freshness vocabulary, and invalidation telemetry. Phase 12 is complete for estimated cache memory accounting, estimated-footprint byte limits, bounded memory-pressure cleanup, cleanup telemetry, and benchmark-led deduplication rejection.

Phase 12B completed generation trust before the runtime/query roadmap resumes: aggregate validation diagnostics, source-location fidelity, safe CLI generation, partial source-generator output, generated-file banners, optional header stamping, and nullable-reference-generation defaults. Phase 12C completed the pre-1.0 CLI cleanup and configuration workflow. The roadmap now continues with Phase 13 explicit multi-join composition, relation-aware joins, scalar converters, result-set caching, and Remotion isolation.

For the older Phase 4/5 checkpoint, see the archived [Phase 4 and 5 Status Review](../archive/roadmap-implementation/Phase%204%20and%205%20Status%20Review.md).

## Notes

- These documents should stay grounded in the current codebase and benchmark harness, not in idealized architecture sketches.
- If a phase plan disagrees with `Roadmap.md`, the roadmap should usually be updated rather than letting the two silently diverge.
- A phase plan should be implementation-oriented: workstreams, sequencing, risks, verification, and exit criteria.
