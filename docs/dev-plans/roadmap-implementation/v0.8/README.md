> [!WARNING]
> This folder contains roadmap execution material for the 0.8 development line. It is not normative product documentation, and it should not be treated as a shipped support claim.
# DataLinq 0.8 Roadmap

**Status:** Active 0.8 execution roadmap.

**Created:** 2026-06-27.

## Purpose

0.8 resets the execution numbering after the 0.7.1 release. The old roadmap phases are still useful historical and design material, but continuing with "Phase 17" as the active label makes the next release harder to reason about than it needs to be.

The major 0.8 theme is:

> Replace the `Remotion.Linq` parser with a DataLinq-owned query parser and remove `Remotion.Linq` as a dependency of the main DataLinq product.

This is not a general LINQ provider rewrite. It is a controlled migration of the supported DataLinq query surface.

The wording matters. "Isolate Remotion" was a useful earlier fallback while the migration shape was unclear. It is not the 0.8 goal. A separate compatibility experiment can exist later if a real user need appears, but 0.8 should not ship with the main runtime package, constrained-platform smoke paths, or active test baseline depending on `Remotion.Linq`.

## Release Shape

| 0.8 phase | Status | Directory | Release role |
| --- | --- | --- | --- |
| Phase 1: Query Contract and Plan Baseline | Complete | `phase-1-query-contract-and-plan-baseline/` | Lock down parity before changing internals. |
| Phase 2: Remotion Plan Adapter | Complete | `phase-2-remotion-plan-adapter/` | Make Remotion one producer of DataLinq plan nodes. |
| Phase 3: SQL Generation on Query Plan | Complete | `phase-3-sql-generation-on-query-plan/` | Move SQL generation and diagnostics off Remotion clauses. |
| Phase 4: Supported-Subset Expression Parser | Complete | `phase-4-supported-subset-expression-parser/` | Build the DataLinq parser over expression trees. |
| Phase 5: Projection and Local Evaluation AOT Cleanup | Complete | `phase-5-projection-and-local-evaluation-aot-cleanup/` | Keep supported generated/AOT projection paths honest. |
| Phase 6: Dual-Run Parity and AOT Switch | Planned | `phase-6-dual-run-parity-and-aot-switch/` | Prove the new parser before routing constrained-platform paths through it. |
| Phase 7: Remotion Dependency Removal | Planned | `phase-7-remotion-dependency-removal/` | Remove Remotion package references, roots, tests, and documentation assumptions from the main product path. |
| Phase 8: Source-Slot Join Follow-Up | Stretch / 0.8.x | `phase-8-source-slot-join-follow-up/` | Resume join expansion after the query plan exists. |

Phases 1 through 7 are the coherent 0.8 parser-removal track. Phase 8 is deliberately listed after that track because broad join expansion should not be implemented on the old Remotion-shaped boundary first.

## Sequential Rule

Each phase should leave the repo in a defensible state:

1. Tests describe the supported behavior before internals move.
2. Remotion becomes an adapter before it becomes optional.
3. SQL generation consumes DataLinq plan nodes before the new parser becomes default.
4. The new parser proves parity before generated/AOT paths switch over.
5. Remotion leaves the main product dependency graph only after the replacement path has evidence.

Skipping ahead is how parser rewrites turn into archaeology projects with passing demos and broken edge cases.

## Release Gates

0.8 should not claim the parser replacement is complete until:

- the supported LINQ matrix passes for the enabled DataLinq parser subset
- unsupported query shapes still fail with focused diagnostics
- plan snapshot tests cover representative supported shapes
- dual-run parity is green for shapes supported by both parsers
- generated SQLite Native AOT and trim smoke paths no longer root `Remotion.Linq`
- `src/DataLinq/DataLinq.csproj` no longer references `Remotion.Linq`
- `src/Directory.Packages.props` no longer carries a `Remotion.Linq` version for the main product
- package inspection confirms `Remotion.Linq` is absent from DataLinq runtime dependency groups
- source and test cleanup removes Remotion-specific parser dependencies from the active baseline
- public docs describe only the behavior that actually shipped

## Cross-Cutting Requirements

Some release work does not belong cleanly to one phase, but it is still required for a credible 0.8:

- **Dependency gates:** add or update package/size/dependency checks so `Remotion.Linq` cannot quietly re-enter the main runtime package or constrained publish outputs.
- **Test ownership cleanup:** rewrite active tests that instantiate Remotion's `QueryParser` or reflect into Remotion-shaped `ParseQueryModel` paths. Dual-run tests are temporary migration scaffolding, not a permanent dependency.
- **Documented support parity:** preserve the current public LINQ support surface on the DataLinq parser unless the release deliberately documents a breaking contraction. Relation `Any(...)`, existence-equivalent relation `Count()`, scalar aggregates, row-local projections, local collection membership, nullable predicates, and the current narrow explicit `Join(...)` baseline are not optional just because they are inconvenient.
- **Diagnostics parity:** ensure unsupported query diagnostics use DataLinq concepts and do not leak Remotion type names such as result-operator or clause class names.
- **Provider matrix:** run SQLite plus available MySQL/MariaDB verification whenever SQL generation semantics move. SQLite-only green tests are not enough for a query translator change.
- **Performance baseline:** capture at least focused allocation/translation measurements before and after the parser switch. The new parser does not need to be magically faster on day one, but it should not accidentally make the hot path absurd.
- **Public docs and release notes:** update `Supported LINQ Queries`, the LINQ support matrix, query internals docs, platform compatibility docs, and changelog/release notes after behavior ships.
- **Upgrade notes:** call out any intentional query-shape difference, unsupported-shape diagnostic change, or removed compatibility behavior.

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
- shipping a Remotion-backed compatibility fallback inside the main DataLinq package

## After 0.8

Once the parser boundary is owned by DataLinq, the next roadmap can resume feature work in a cleaner order:

1. explicit multi-join composition on source slots
2. relation-aware joins and left joins
3. scalar converters and typed keys
4. dependency-tracked result/module caching
5. non-SQL query executors if the plan proves stable enough
