> [!WARNING]
> This folder contains roadmap execution material for the 0.8 development line. It is not normative product documentation, and it should not be treated as a shipped support claim.
# DataLinq 0.8 Roadmap

**Status:** Active 0.8 execution roadmap.

**Created:** 2026-06-27.

## Purpose

0.8 resets the execution numbering after the 0.7.1 release. The old roadmap phases are still useful historical and design material, but continuing with "Phase 17" as the active label makes the next release harder to reason about than it needs to be.

The major 0.8 theme is:

> Replace or isolate the `Remotion.Linq` dependency by moving query execution behind a DataLinq-owned query plan and a supported-subset expression parser.

This is not a general LINQ provider rewrite. It is a controlled migration of the supported DataLinq query surface.

## Release Shape

| 0.8 phase | Status | Directory | Release role |
| --- | --- | --- | --- |
| Phase 1: Query Contract and Plan Baseline | Next | `phase-1-query-contract-and-plan-baseline/` | Lock down parity before changing internals. |
| Phase 2: Remotion Plan Adapter | Planned | `phase-2-remotion-plan-adapter/` | Make Remotion one producer of DataLinq plan nodes. |
| Phase 3: SQL Generation on Query Plan | Planned | `phase-3-sql-generation-on-query-plan/` | Move SQL generation and diagnostics off Remotion clauses. |
| Phase 4: Supported-Subset Expression Parser | Planned | `phase-4-supported-subset-expression-parser/` | Build the DataLinq parser over expression trees. |
| Phase 5: Projection and Local Evaluation AOT Cleanup | Planned | `phase-5-projection-and-local-evaluation-aot-cleanup/` | Keep supported generated/AOT projection paths honest. |
| Phase 6: Dual-Run Parity and AOT Switch | Planned | `phase-6-dual-run-parity-and-aot-switch/` | Prove the new parser before routing constrained-platform paths through it. |
| Phase 7: Remotion Removal or Compatibility Isolation | Planned | `phase-7-remotion-removal-or-compatibility-isolation/` | Remove Remotion from the main runtime path, or isolate it explicitly. |
| Phase 8: Source-Slot Join Follow-Up | Stretch / 0.8.x | `phase-8-source-slot-join-follow-up/` | Resume join expansion after the query plan exists. |

Phases 1 through 7 are the coherent 0.8 parser-removal track. Phase 8 is deliberately listed after that track because broad join expansion should not be implemented on the old Remotion-shaped boundary first.

## Sequential Rule

Each phase should leave the repo in a defensible state:

1. Tests describe the supported behavior before internals move.
2. Remotion becomes an adapter before it becomes optional.
3. SQL generation consumes DataLinq plan nodes before the new parser becomes default.
4. The new parser proves parity before generated/AOT paths switch over.
5. Remotion leaves the main runtime package only after the replacement path has evidence.

Skipping ahead is how parser rewrites turn into archaeology projects with passing demos and broken edge cases.

## Release Gates

0.8 should not claim the parser replacement is complete until:

- the supported LINQ matrix passes for the enabled DataLinq parser subset
- unsupported query shapes still fail with focused diagnostics
- plan snapshot tests cover representative supported shapes
- dual-run parity is green for shapes supported by both parsers
- generated SQLite Native AOT and trim smoke paths no longer root `Remotion.Linq`
- package inspection confirms the main runtime dependency story
- public docs describe only the behavior that actually shipped

## Source Plans

The 0.8 roadmap consolidates these older plans rather than discarding them:

- [0.8 Query Parser Overview](../phase-17-query-plan-and-remotion-isolation/0.8%20Query%20Parser%20Overview.md)
- [Phase 17 Query Plan and Remotion Isolation](../phase-17-query-plan-and-remotion-isolation/Implementation%20Plan.md)
- [Remotion.Linq Replacement Plan](../../query-and-runtime/Remotion.Linq%20Replacement%20Plan.md)
- [Query Pipeline Abstraction](../../query-and-runtime/Query%20Pipeline%20Abstraction.md)
- [Practical AOT and Size Plan](../../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md)
- [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)
- [Supported LINQ Queries](../../../Supported%20LINQ%20Queries.md)
- [Query Translator internals](../../../internals/Query%20Translator.md)

## Explicit Non-Goals

- arbitrary LINQ provider behavior
- broad nested database subqueries
- silent client-side predicate fallback
- `GroupBy(...)`
- broad join expansion before source slots exist
- DataLinq.Store query/module execution
- non-SQL backend execution as a 0.8 release requirement
- no-AOT browser WebAssembly support
- warning suppression as the final answer for Remotion

## After 0.8

Once the parser boundary is owned by DataLinq, the next roadmap can resume feature work in a cleaner order:

1. explicit multi-join composition on source slots
2. relation-aware joins and left joins
3. scalar converters and typed keys
4. dependency-tracked result/module caching
5. non-SQL query executors if the plan proves stable enough
