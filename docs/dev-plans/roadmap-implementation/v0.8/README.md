> [!WARNING]
> This folder contains roadmap execution material for the 0.8 development line. It is not normative product documentation, and it should not be treated as a shipped support claim.
# DataLinq 0.8 Roadmap

**Status:** Parser-removal track complete through Phase 7; AOT/browser release gate tooling implemented through Phase 12; current browser AOT evidence fails at generated SQLite startup; final 0.8 closeout requires fixing or excluding that browser claim, then refreshing compatibility, package, and benchmark evidence before query-composition hardening, a narrow grouped aggregate projection baseline, source-slot joins, relation-aware joins, and implicit join completion.

**Created:** 2026-06-27.

## Purpose

0.8 resets the execution numbering after the 0.7.1 release. The old roadmap phases are still useful historical and design material, but continuing with "Phase 17" as the active label makes the next release harder to reason about than it needs to be.

The original major 0.8 theme was:

> Replace the `Remotion.Linq` parser with a DataLinq-owned query parser and remove `Remotion.Linq` as a dependency of the main DataLinq product.

This is not a general LINQ provider rewrite. It is a controlled migration of the supported DataLinq query surface.

The wording matters. "Isolate Remotion" was a useful earlier fallback while the migration shape was unclear. It is not the 0.8 goal. A separate compatibility experiment can exist later if a real user need appears, but 0.8 should not ship with the main runtime package, constrained-platform smoke paths, or active test baseline depending on `Remotion.Linq`.

That parser-removal track is now complete. The release goal for the rest of 0.8 is:

> Make generated SQLite Native AOT, trimming, and Blazor WebAssembly AOT actually run with current automation, documented query coverage, and sensible deploy sizes.

That means browser AOT is release work, not a stretch note. Query-composition hardening, the first narrow `GroupBy(...)` aggregate slice, and source-slot join expansion move behind the AOT release gates, but they should remain in the 0.8 line rather than drifting into an indefinite 0.8.x follow-up.

## Release Shape

| 0.8 phase | Status | Directory | Release role |
| --- | --- | --- | --- |
| Phase 1: Query Contract and Plan Baseline | Complete | `phase-1-query-contract-and-plan-baseline/` | Lock down parity before changing internals. |
| Phase 2: Remotion Plan Adapter | Complete | `phase-2-remotion-plan-adapter/` | Make Remotion one producer of DataLinq plan nodes. |
| Phase 3: SQL Generation on Query Plan | Complete | `phase-3-sql-generation-on-query-plan/` | Move SQL generation and diagnostics off Remotion clauses. |
| Phase 4: Supported-Subset Expression Parser | Complete | `phase-4-supported-subset-expression-parser/` | Build the DataLinq parser over expression trees. |
| Phase 5: Projection and Local Evaluation AOT Cleanup | Complete | `phase-5-projection-and-local-evaluation-aot-cleanup/` | Keep supported generated/AOT projection paths honest. |
| Phase 6: Dual-Run Parity and AOT Switch | Complete | `phase-6-dual-run-parity-and-aot-switch/` | Prove the new parser before routing constrained-platform paths through it. |
| Phase 7: Remotion Dependency Removal | Complete | `phase-7-remotion-dependency-removal/` | Remove Remotion package references, roots, tests, and documentation assumptions from the main product path. |
| Phase 8: Browser AOT Runtime Proof | Implemented in tooling; current `wasm-aot` browser evidence fails at generated SQLite startup | `phase-8-browser-aot-runtime-proof/` | Automate browser execution evidence for the WebAssembly AOT smoke app. |
| Phase 9: WebAssembly Warning and no-AOT Disposition | Tooling implemented; current evidence keeps browser support blocked | `phase-9-webassembly-warning-and-no-aot-disposition/` | Resolve SQLitePCLRaw warning disposition and prove or reject no-AOT browser runtime support. |
| Phase 10: AOT Query Coverage and Fallback Fencing | Implemented for selected 0.8 constrained smoke subset | `phase-10-aot-query-coverage-and-fallback-fencing/` | Expand constrained-platform query coverage and keep AOT routes out of compatibility fallback. |
| Phase 11: Browser Payload and Deploy-Size Hardening | Implemented in compatibility reporting | `phase-11-browser-payload-and-deploy-size-hardening/` | Turn current good payload numbers into reproducible release thresholds and deployment guidance. |
| Phase 12: AOT Release Gates and Support Contract | Release gate wiring implemented; current evidence blocks the browser AOT support claim | `phase-12-aot-release-gates-and-support-contract/` | Promote only the narrow support statement backed by current evidence. |
| Phase 13: Query Composition and Subquery Pushdown | Implemented for the single-source Phase 13 slice | `phase-13-query-composition-and-subquery-pushdown/` | Preserve LINQ operator order for filtering, ordering, paging, and scalar result operators, using SQL subquery boundaries where needed. |
| Phase 13B: Grouped Aggregate Projection Baseline | Implemented for direct-key grouped `Count()` projection | `phase-13b-grouped-aggregate-projection-baseline/` | Adds the first honest `GroupBy(...)` support slice: single-source grouped aggregate projection, not materialized `IGrouping` support. |
| Phase 14: Source-Slot Join Composition | Implemented for explicit two-source join composition | `phase-14-source-slot-join-composition/` | Makes explicit joins useful on the DataLinq source-slot plan. |
| Phase 15: Relation-Aware and Implicit Joins | Planned 0.8 finish-line work after Phase 14 | `phase-15-relation-aware-and-implicit-joins/` | Add `JoinBy(...)`, `JoinMany(...)`, implicit singular relation joins, join-local predicates, and left-join behavior. |

Phases 1 through 7 are the coherent 0.8 parser-removal track. Phases 8 through 12 are the 0.8 AOT/browser release track. The release-track tooling is implemented, and the first fresh browser evidence found a real blocker: `wasm-aot` publishes on the host, then fails in Edge at `opening-generated-database` with `MONO_WASM: function signature mismatch`. Phase 13, Phase 13B, and Phases 14 through 15 should follow the fix-or-exclude decision because broad query expansion is less important than making the browser AOT story honest and release-grade, but query composition, narrow grouped aggregates, and join work are still part of the intended 0.8 completion story.

## Current Implementation State

Phase 7 closed the parser-removal track. The current branch has:

- production `Database.Query()` roots executing through DataLinq's `ExpressionQueryPlanProvider`
- supported expression trees parsed by `ExpressionQueryPlanParser` into `DataLinqQueryPlan`
- SQL generation and query diagnostics routed through `QueryPlanSqlBuilder` and DataLinq plan concepts
- active parser, SQL parity, unsupported-shape, and architecture tests no longer relying on Remotion parser APIs
- `Remotion.Linq` removed from `src/DataLinq/DataLinq.csproj`, `src/Directory.Packages.props`, constrained smoke roots, and public runtime package dependency groups
- trimmed constrained compatibility reporting no longer blocked by a Remotion dependency

That closes the 0.8 parser-removal goal. It does not make arbitrary LINQ supported, it does not make browser runtime support pass, and it does not resolve the separate SQLitePCLRaw WebAssembly warning story.

The current public architecture description is [LINQ Parser Architecture](../../../internals/LINQ%20Parser%20Architecture.md). Treat that page, [Supported LINQ Queries](../../../Supported%20LINQ%20Queries.md), and the [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md) as the current-state handoff from this execution plan. The files in this folder explain how the parser got here; the public docs explain what the parser is now.

## Sequential Rule

Each phase should leave the repo in a defensible state:

1. Tests describe the supported behavior before internals move.
2. Remotion becomes an adapter before it becomes optional.
3. SQL generation consumes DataLinq plan nodes before the new parser becomes default.
4. The new parser proves parity before generated/AOT paths switch over.
5. Remotion leaves the main product dependency graph only after the replacement path has evidence.
6. Browser support claims require browser execution evidence, not just publish output.
7. Payload claims use symbol-excluded and compressed deploy-size numbers, not whatever folder total is easiest to copy.

Skipping ahead is how parser rewrites turn into archaeology projects with passing demos and broken edge cases.

## Release Gates

0.8 should not claim the parser replacement is complete until:

- the supported LINQ matrix passes for the enabled DataLinq parser subset
- unsupported query shapes still fail with focused diagnostics
- plan snapshot tests cover representative supported shapes
- generated SQLite Native AOT and trim smoke paths no longer root `Remotion.Linq`
- `src/DataLinq/DataLinq.csproj` no longer references `Remotion.Linq`
- `src/Directory.Packages.props` no longer carries a `Remotion.Linq` version for the main product
- package inspection confirms `Remotion.Linq` is absent from DataLinq runtime dependency groups
- source and test cleanup removes Remotion-specific parser dependencies from the active baseline
- public docs describe only the behavior that actually shipped

0.8 should not claim browser AOT support until:

- WebAssembly AOT publish and browser smoke run from a repeatable repo command
- browser smoke covers generated SQLite startup, schema creation, insert, query, projection, relation loading, and parser route evidence
- browser failures report the failing stage instead of collapsing into generic startup failure
- SQLitePCLRaw WebAssembly warning disposition is documented with exact symbols and call paths
- no-AOT browser behavior is re-tested and either explicitly unsupported or narrowly supported with current evidence
- constrained-platform query coverage includes the documented subset selected for 0.8
- AOT strict paths do not depend on reflection-only projection/member fallback or `Expression.Compile()`
- Native AOT, trimmed, WASM publish, and WASM AOT publish reports have zero banned payloads
- deploy-size thresholds are documented and met, or the release notes explain a deliberate exception
- public docs state the exact support boundary: generated SQLite models, documented query subset, Native AOT, trimmed publish, and Blazor WebAssembly AOT

Current 2026-06-28 evidence does not satisfy this gate. The browser automation is in place, but `artifacts/dev/compat-size-report/20260628-163740998/` fails before schema creation while opening SQLite in WebAssembly AOT.

0.8 should not claim expanded query-composition support until:

- post-paging filters and orderings preserve the C# operator sequence instead of flattening into wrong SQL clause order
- `OrderBy(...).Take(...)`, `Take(...).OrderBy(...)`, `Skip(...).OrderBy(...)`, and `OrderBy(...).Take(...).OrderBy(...)` have SQL-shape tests for flat versus pushed-down forms
- supported scalar result operators work over pushed-down query sources
- aliases, parameters, projection binding, and diagnostics stay stable across nested source boundaries
- every supported query command works from both `db.Query()` and `transaction.Query()`, with transaction-rooted queries using the transaction data source for execution, materialization, relation lookup, and cache interaction
- public docs and the support matrix describe only the shapes actually implemented

0.8 should not claim `GroupBy(...)` support until:

- the public claim is explicitly scoped to grouped aggregate projection, not materialized `IGrouping<TKey,TElement>` sequences
- single-source `GroupBy(key).Select(...)` plans record group keys and aggregate projection members as first-class DataLinq plan nodes
- SQL rendering has explicit `GROUP BY` support instead of smuggling grouping text through raw selector strings
- execution reads grouped result rows directly and does not pretend aggregate rows are cache-backed entity rows
- at least `g.Key` plus `g.Count()` has provider-matrix behavior tests against SQLite, MySQL, and MariaDB
- direct numeric `Sum`, `Min`, `Max`, and `Average` over grouped rows are either implemented with tests or remain explicitly rejected with focused diagnostics
- bare `GroupBy(...).ToList()`, grouped element enumeration, `HAVING`, composite keys, computed keys, grouped joins, and post-group query composition remain rejected until deliberately designed
- public docs and the support matrix describe only the tested grouped aggregate shapes

The current Phase 13B implementation satisfies this gate for the direct mapped key plus `group.Key`/`group.Count()` shape. Grouped numeric aggregates, materialized `IGrouping<TKey,TElement>`, grouped joins, computed/composite keys, `HAVING`, and post-group composition remain outside the support boundary.

0.8 should not claim expanded join support until:

- standard C# query syntax supports practical multi-table inner joins
- explicit joined row shapes preserve Phase 13 filtering, ordering, paging, `Any`, and `Count` semantics
- relation metadata can drive `JoinBy(...)` and `JoinMany(...)` without duplicated key selectors
- singular generated relation traversal is SQL-backed for supported predicates, ordering, and simple projections
- collection relation traversal remains explicit except for documented `Any(...)` and existence-equivalent `Count(...)` patterns
- left joins preserve unmatched source rows and expose nullable joined values
- `net10.0` has a documented support decision for standard `Queryable.LeftJoin(...)`
- supported explicit, relation-aware, and implicit join shapes work from both `db.Query()` and `transaction.Query()`
- public docs and the support matrix describe only the tested join shapes

## Cross-Cutting Requirements

Some release work does not belong cleanly to one phase, but it is still required for a credible 0.8:

- **Dependency gates:** add or update package/size/dependency checks so `Remotion.Linq` cannot quietly re-enter the main runtime package or constrained publish outputs.
- **Browser runtime evidence:** publish output alone is not support evidence. WebAssembly AOT must run in a browser through a repeatable local command before release wording promotes it.
- **Test ownership cleanup:** rewrite active tests that instantiate Remotion's `QueryParser` or reflect into Remotion-shaped `ParseQueryModel` paths. Dual-run tests are temporary migration scaffolding, not a permanent dependency.
- **Documented support parity:** preserve the current public LINQ support surface on the DataLinq parser unless the release deliberately documents a breaking contraction. Relation `Any(...)`, existence-equivalent relation `Count()`, scalar aggregates, row-local projections, local collection membership, nullable predicates, and the current narrow explicit `Join(...)` baseline are not optional just because they are inconvenient.
- **Root parity:** query translation should be rooted in `IQueryable<T>`, not in `Database<T>` convenience methods. Any supported command should be tested from both `db.Query()` and `transaction.Query()` before it becomes a 0.8 support claim.
- **Diagnostics parity:** ensure unsupported query diagnostics use DataLinq concepts and do not leak Remotion type names such as result-operator or clause class names.
- **Provider matrix:** run SQLite plus available MySQL/MariaDB verification whenever SQL generation semantics move. SQLite-only green tests are not enough for a query translator change.
- **Performance baseline:** capture at least focused allocation/translation measurements before and after the parser switch. The new parser does not need to be magically faster on day one, but it should not accidentally make the hot path absurd.
- **Deploy-size discipline:** keep Native AOT symbols separate from runtime size, track Brotli/Gzip browser payloads, and reject Roslyn/compiler payload regressions.
- **Public docs and release notes:** update `Supported LINQ Queries`, the LINQ support matrix, query internals docs, platform compatibility docs, and changelog/release notes after behavior ships.
- **Upgrade notes:** call out any intentional query-shape difference, unsupported-shape diagnostic change, or removed compatibility behavior.

## Source Plans

The 0.8 roadmap consolidates these older plans rather than discarding them:

- [0.8 Query Parser Overview](../phase-17-query-plan-and-remotion-isolation/0.8%20Query%20Parser%20Overview.md)
- [Phase 17 Query Plan and Remotion Isolation](../phase-17-query-plan-and-remotion-isolation/Implementation%20Plan.md)
- [Remotion.Linq Replacement Plan](../../query-and-runtime/Remotion.Linq%20Replacement%20Plan.md)
- [Query Pipeline Abstraction](../../query-and-runtime/Query%20Pipeline%20Abstraction.md)
- [Relation-Aware Join API](../../query-and-runtime/Relation-Aware%20Join%20API.md)
- [0.8 Phase 13B Grouped Aggregate Projection Baseline](phase-13b-grouped-aggregate-projection-baseline/README.md)
- [Old Phase 13 Explicit Multi-Join Composition](../phase-13-explicit-multi-join-composition/README.md)
- [Old Phase 14 Relation-Aware Joins and Left Joins](../phase-14-relation-aware-joins-and-left-joins/README.md)
- [Practical AOT and Size Plan](../../platform-compatibility/Practical%20AOT%20and%20Size%20Plan.md)
- [LINQ Translation Support Matrix](../../../support-matrices/LINQ%20Translation%20Support%20Matrix.md)
- [Supported LINQ Queries](../../../Supported%20LINQ%20Queries.md)
- [LINQ Parser Architecture](../../../internals/LINQ%20Parser%20Architecture.md)
- [Query Translator internals](../../../internals/Query%20Translator.md)

## Explicit Non-Goals

- arbitrary LINQ provider behavior
- broad arbitrary database subqueries beyond the pushdown needed to preserve documented operator order
- silent client-side predicate fallback
- full `GroupBy(...)` materialization as `IGrouping<TKey,TElement>` sequences, grouped element enumeration, and broad grouped query composition beyond the Phase 13B grouped aggregate projection baseline
- broad join expansion before AOT/browser release evidence is captured
- implicit collection projection or hidden row multiplication
- DataLinq.Store query/module execution
- non-SQL backend execution as a 0.8 release requirement
- no-AOT browser WebAssembly support unless the current browser smoke proves it
- warning suppression as the final answer for Remotion
- shipping a Remotion-backed compatibility fallback inside the main DataLinq package
- OPFS/file-backed browser storage as part of the core 0.8 AOT claim

## After 0.8

Once the parser boundary, AOT/browser release gates, and join completion work are green, the next roadmap can resume feature work in a cleaner order:

1. scalar converters and typed keys
2. dependency-tracked result/module caching
3. non-SQL query executors if the plan proves stable enough
