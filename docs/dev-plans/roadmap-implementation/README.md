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
| Phase 9B: Row Freshness, External Invalidation, and Adaptive Cache Policy | Planned follow-up cache-semantics release | `phase-9b-row-freshness-external-invalidation-adaptive-cache-policy/` |
| Phase 13: Query Plan and Remotion Isolation | Deferred query-boundary and WebAssembly warning phase | `phase-13-query-plan-and-remotion-isolation/` |

Completed execution records for Phases 1 through 9A live in [`../archive/roadmap-implementation/README.md`](../archive/roadmap-implementation/README.md).

## Current Roadmap Position

As of 2026-05-11, Phases 1 through 9A are closed execution history. The active implementation frontier should not jump straight to a broad "AOT-compatible ORM" story.

Phase 4 has the support matrix and provider roundtrip boundary that schema validation needed. Phase 5 has the comparer, validation CLI, conservative diff-script generator, and first snapshot migration contract. Full versioned migration execution is not a remaining Phase 5 cleanup task; it is a separate future product surface.

Phase 4B plugged the most useful Phase 4 metadata gaps before the roadmap moved on: referential actions, advanced index guardrails, generated-column guardrails, raw provider defaults, explicit MySQL/MariaDB column ordering, and view validation.

The Phase 6 plan completed the support-matrix audit, chained `Where(...)` correctness, projected local `Contains(...)`, equality-based local object-list `Any(predicate)` expansion, fixed-condition invariants, and unsupported-query diagnostics.

The Phase 7 plan completed scalar aggregates, projection expansion, nullable predicate polish, a narrow LINQ `Join(...)` baseline, and relation-aware predicate translation.

The Phase 8 plan closed with executable generated SQLite smoke coverage for Native AOT, trimmed publish, and Blazor WebAssembly AOT. Phase 8B closed the generated-contract and immutable metadata foundation. Phase 8C then completed the bounded package/generated-runtime cleanup: repeatable size reports, Roslyn removal from runtime dependency groups, complete generated metadata startup, runtime reflection metadata-discovery removal, generated indexed access, and package/public wording.

The query-plan, Remotion isolation, supported-subset parser, and SQLitePCLRaw warning work remain Phase 13 at the back of the roadmap.

Phase 9 is now split into two execution slices. Phase 9A is complete for warning cleanup, benchmark-history and website trends, allocation reduction, and conservative cache invalidation hardening. Its benchmark closeout supports allocation and invalidation claims, not latency claims. Phase 9B is the follow-up cache-semantics phase: row freshness, external invalidation hooks, adaptive cache policy, memory-pressure-aware cleanup, and measured deduplication.

For the older Phase 4/5 checkpoint, see the archived [Phase 4 and 5 Status Review](../archive/roadmap-implementation/Phase%204%20and%205%20Status%20Review.md).

## Notes

- These documents should stay grounded in the current codebase and benchmark harness, not in idealized architecture sketches.
- If a phase plan disagrees with `Roadmap.md`, the roadmap should usually be updated rather than letting the two silently diverge.
- A phase plan should be implementation-oriented: workstreams, sequencing, risks, verification, and exit criteria.
