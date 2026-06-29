> [!WARNING]
> This folder contains roadmap execution material. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a document explicitly says so.
# Roadmap Implementation

## Purpose

`docs/dev-plans/roadmap-implementation` is where DataLinq turns the high-level roadmap into concrete execution plans.

`Roadmap.md` answers:

What should happen next, and why?

This folder answers:

What are we actually going to do, in what order, and how will we know it worked?

## Active Release Roadmap

| Release | Status | Directory |
| --- | --- | --- |
| 0.8 | Parser-removal track complete through Phase 7; AOT/browser tooling implemented through Phase 12 with browser support still evidence-blocked; query-runtime slices implemented through Phase 21; final hardening is planned as Phases 22-24 | `v0.8/` |

The 0.8 sequence started over at [0.8 Phase 1](v0.8/phase-1-query-contract-and-plan-baseline/README.md) instead of continuing the old global phase numbering. That was intentional. Version-scoped phases are easier to execute, easier to close, and less confusing than saying the next release starts at "Phase 17". The parser-removal track is now closed through [0.8 Phase 7](v0.8/phase-7-remotion-dependency-removal/README.md). The release-critical AOT/browser tooling runs from [0.8 Phase 8](v0.8/phase-8-browser-aot-runtime-proof/README.md) through [0.8 Phase 12](v0.8/phase-12-aot-release-gates-and-support-contract/README.md). Query-runtime finish-line work then continues through [0.8 Phase 21](v0.8/phase-21-joined-post-paging-pushdown/README.md). The planned final release-hardening sequence is [0.8 Phase 22](v0.8/phase-22-linq-parser-plan-cleanup/README.md), [0.8 Phase 23](v0.8/phase-23-browser-aot-debugging/README.md), and [0.8 Phase 24](v0.8/phase-24-release-evidence-benchmarks-docs/README.md).

## Source Plans

These older plans are still useful design material, but they are not the active sequential roadmap:

| Source plan | Current role | Directory |
| --- | --- | --- |
| Phase 13: Explicit Multi-Join Composition | Follow-up source plan after the 0.8 query-plan work | `phase-13-explicit-multi-join-composition/` |
| Phase 14: Relation-Aware Joins, Implicit Joins, and Left Joins | Follow-up source plan after explicit joins are rebuilt on source slots | `phase-14-relation-aware-joins-and-left-joins/` |
| Phase 15: Scalar Converters and Typed-Key Ergonomics | Later conversion/key ergonomics source plan | `phase-15-scalar-converters-and-typed-key-ergonomics/` |
| Phase 16: Dependency-Tracked Result-Set Caching | Later semantic cache source plan | `phase-16-dependency-tracked-result-set-caching/` |
| Phase 17: Query Plan and Remotion Isolation | Superseded by the version-scoped 0.8 roadmap; still the detailed source plan | `phase-17-query-plan-and-remotion-isolation/` |

Completed execution records through Phase 12C live in [`../archive/roadmap-implementation/README.md`](../archive/roadmap-implementation/README.md).

## Current Roadmap Position

As of 2026-06-27, Phases 1 through 12C are closed execution history. Phase 12B completed generation-trust hardening, and Phase 12C completed the CLI, configuration, regeneration, schema, diagnostics, and secrets workflow. Both are archived with the earlier completed roadmap phases.

Phase 4 has the support matrix and provider roundtrip boundary that schema validation needed. Phase 5 has the comparer, validation CLI, conservative diff-script generator, and first snapshot migration contract. Full versioned migration execution is not a remaining Phase 5 cleanup task; it is a separate future product surface.

Phase 4B plugged the most useful Phase 4 metadata gaps before the roadmap moved on: referential actions, advanced index guardrails, generated-column guardrails, raw provider defaults, explicit MySQL/MariaDB column ordering, and view validation.

The Phase 6 plan completed the support-matrix audit, chained `Where(...)` correctness, projected local `Contains(...)`, equality-based local object-list `Any(predicate)` expansion, fixed-condition invariants, and unsupported-query diagnostics.

The Phase 7 plan completed scalar aggregates, projection expansion, nullable predicate polish, a narrow LINQ `Join(...)` baseline, and relation-aware predicate translation.

The Phase 8 plan closed with executable generated SQLite smoke coverage for Native AOT, trimmed publish, and Blazor WebAssembly AOT. Phase 8B closed the generated-contract and immutable metadata foundation. Phase 8C then completed the bounded package/generated-runtime cleanup: repeatable size reports, Roslyn removal from runtime dependency groups, complete generated metadata startup, runtime reflection metadata-discovery removal, generated indexed access, and package/public wording.

The query-plan, Remotion isolation, and supported-subset parser work became the 0.8 focus and is now closed through [DataLinq 0.8 Roadmap](v0.8/README.md) Phase 7: query contract baseline, Remotion plan adapter, SQL generation on the plan, supported-subset parser, projection/AOT cleanup, dual-run parity, production provider switch, and Remotion dependency removal. The query-runtime feature slices are now implemented through Phase 21 on the DataLinq-owned query plan. The remainder of 0.8 should stay focused on parser-plan cleanup, generated SQLite browser AOT debugging, and final release evidence/docs.

Phase 9A is complete for warning cleanup, benchmark-history and website trends, allocation reduction, and conservative cache invalidation hardening. Its benchmark closeout supports allocation and invalidation claims, not latency claims.

Phase 10 is complete for the key/allocation foundation: metadata collection and lookup cleanup, provider-key row stores, generated relation access, query/materialization provider-key reads, scalar-converter seams, and Phase 11 handoff artifacts. Phase 11 is complete for explicit cache clearing, external invalidation, relation/index invalidation, freshness vocabulary, and invalidation telemetry. Phase 12 is complete for estimated cache memory accounting, estimated-footprint byte limits, bounded memory-pressure cleanup, cleanup telemetry, and benchmark-led deduplication rejection.

Phase 12B completed generation trust before the runtime/query roadmap resumed: aggregate validation diagnostics, source-location fidelity, safe CLI generation, partial source-generator output, generated-file banners, optional header stamping, and nullable-reference-generation defaults. Phase 12C completed the pre-1.0 CLI cleanup and configuration workflow. The version-scoped 0.8 query-parser phases have now removed the Remotion dependency from the main runtime path, and the query-composition/join slices are implemented through Phase 21. The roadmap should finish Phase 22 parser-plan cleanup, Phase 23 browser AOT debugging, and Phase 24 release evidence/docs before moving to scalar converters and result/module caching on top of the DataLinq-owned query plan.

For the older Phase 4/5 checkpoint, see the archived [Phase 4 and 5 Status Review](../archive/roadmap-implementation/Phase%204%20and%205%20Status%20Review.md).

## Notes

- These documents should stay grounded in the current codebase and benchmark harness, not in idealized architecture sketches.
- If a phase plan disagrees with `Roadmap.md`, the roadmap should usually be updated rather than letting the two silently diverge.
- A phase plan should be implementation-oriented: workstreams, sequencing, risks, verification, and exit criteria.
