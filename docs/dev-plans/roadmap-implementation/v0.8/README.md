> [!WARNING]
> This folder contains roadmap execution material for the 0.8 development line. It is not normative product documentation, and it should not be treated as a shipped support claim.
# DataLinq 0.8 Roadmap

**Status:** Parser-removal track complete through Phase 7; AOT/browser release gate tooling implemented through Phase 12; current browser AOT evidence fails at generated SQLite startup; query-composition hardening, grouped `Count()` and numeric aggregate projection, explicit two-source join composition, and implicit singular relation predicates/orderings have landed as later query-runtime slices. The remaining 0.8 roadmap now includes grouped-row composition, advanced GroupBy keys, SQL-backed projection rows, query-syntax joins, and joined post-paging pushdown before the next post-0.8 feature wave.

**Created:** 2026-06-27.

## Purpose

0.8 resets the execution numbering after the 0.7.1 release. The old roadmap phases are still useful historical and design material, but continuing with "Phase 17" as the active label makes the next release harder to reason about than it needs to be.

The original major 0.8 theme was:

> Replace the `Remotion.Linq` parser with a DataLinq-owned query parser and remove `Remotion.Linq` as a dependency of the main DataLinq product.

This is not a general LINQ provider rewrite. It is a controlled migration of the supported DataLinq query surface.

The wording matters. "Isolate Remotion" was a useful earlier fallback while the migration shape was unclear. It is not the 0.8 goal. A separate compatibility experiment can exist later if a real user need appears, but 0.8 should not ship with the main runtime package, constrained-platform smoke paths, or active test baseline depending on `Remotion.Linq`.

That parser-removal track is now complete. The release goal for the rest of 0.8 is:

> Make generated SQLite Native AOT, trimming, and Blazor WebAssembly AOT actually run with current automation, documented query coverage, and sensible deploy sizes.

That means browser AOT is release work, not a stretch note. Query-composition hardening, the first narrow `GroupBy(...)` aggregate slice, and source-slot join expansion moved behind the AOT release gates, and the implemented slices should stay documented as 0.8 work. The next GroupBy phases continue that same rule: they can broaden SQL-shaped grouped aggregate support, but they must not imply materialized `IGrouping<TKey,TElement>` support. The following projection and join phases should likewise broaden only the SQL-backed shapes they can prove, not paper over gaps with lazy relation loading or client-side fallback.

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
| Phase 15: Relation-Aware and Implicit Joins | Implemented for implicit singular relation predicates/orderings | `phase-15-relation-aware-and-implicit-joins/` | Adds the first SQL-backed implicit singular relation traversal slice and documents the deferred fluent/left-join APIs. |
| Phase 16: Grouped Numeric Aggregates | Implemented for direct numeric grouped aggregate selectors | `phase-16-grouped-numeric-aggregates/` | Adds grouped `Sum`, `Min`, `Max`, `Average`, and multiple aggregate members for direct numeric selectors. |
| Phase 17: Grouped Row Composition and HAVING | Planned after grouped numeric aggregates | `phase-17-grouped-row-composition-and-having/` | Makes grouped aggregate rows orderable, pageable, filterable, and able to express narrow SQL `HAVING` predicates. |
| Phase 18: Advanced GroupBy Keys and Joined Grouping | Planned after grouped-row composition and HAVING | `phase-18-advanced-groupby-keys-and-joined-grouping/` | Adds composite/computed SQL-renderable group keys and grouping over supported joined source-slot shapes. |
| Phase 19: SQL-Backed Projection Rows and Implicit Relation Projection | Planned after the SQL-style GroupBy completion track | `phase-19-sql-backed-projection-rows-and-implicit-relation-projection/` | Adds direct SQL-backed projection row materialization and singular relation member projection without hidden lazy relation loading. |
| Phase 20: Query-Syntax Join Support | Planned after SQL-backed projection rows | `phase-20-query-syntax-join-support/` | Makes C# query-syntax inner joins a documented and tested path over source-slot joins and transparent identifiers. |
| Phase 21: Joined Post-Paging Pushdown | Planned after query-syntax join support | `phase-21-joined-post-paging-pushdown/` | Extends Phase 13 operator-order pushdown to supported joined row shapes after `Skip(...)` or `Take(...)`. |

Phases 1 through 7 are the coherent 0.8 parser-removal track. Phases 8 through 12 are the 0.8 AOT/browser release track. The release-track tooling is implemented, and the first fresh browser evidence found a real blocker: `wasm-aot` publishes on the host, then fails in Edge at `opening-generated-database` with `MONO_WASM: function signature mismatch`. Phase 13, Phase 13B, Phase 16, and Phases 14 through 15 are implemented query-runtime slices that followed that release-gate work. Phases 17 through 18 are the remaining GroupBy completion slices, scoped to SQL-style grouped aggregate support rather than broad LINQ grouping semantics. Phases 19 through 21 then return to projection and join completion: SQL-backed projection rows, query-syntax joins, and joined post-paging pushdown.

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

The current Phase 13B and Phase 16 implementations satisfy this gate for the direct mapped key plus `group.Key`, `group.Count()`, and direct numeric grouped `Sum`/`Min`/`Max`/`Average` projection shape. Materialized `IGrouping<TKey,TElement>`, grouped joins, computed/composite keys, `HAVING`, and post-group composition remain outside the support boundary.

0.8 should not claim reasonably full SQL-style `GroupBy(...)` support until:

- grouped `Sum`, `Min`, `Max`, and `Average` work for direct numeric selectors with provider-matrix tests
- multiple aggregate members can be projected from one grouped query
- nullable grouped aggregate semantics are documented by tests instead of inferred from scalar aggregate behavior
- grouped aggregate rows can be ordered, paged, filtered, and counted without client fallback
- SQL `HAVING` and derived grouped-row filtering are represented deliberately and tested by SQL shape
- composite keys and SQL-renderable computed keys bind through first-class key members
- grouping over supported joined source-slot shapes works without relation loading or anonymous projection reflection
- null, enum, string, and collation-sensitive key grouping behavior is covered across active providers
- public docs keep materialized `IGrouping<TKey,TElement>` and grouped element enumeration explicitly out of the provider-backed support claim

0.8 should not claim expanded join support until:

- standard C# query syntax supports practical multi-table inner joins
- explicit joined row shapes preserve Phase 13 filtering, ordering, paging, `Any`, and `Count` semantics
- direct projection rows materialize from SQL result aliases when the projection is made of bindable source-slot values
- singular generated relation projection is SQL-backed and does not lazy-load relations inside provider `Select(...)`
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
- [0.8 Phase 16 Grouped Numeric Aggregates](phase-16-grouped-numeric-aggregates/README.md)
- [0.8 Phase 17 Grouped Row Composition and HAVING](phase-17-grouped-row-composition-and-having/README.md)
- [0.8 Phase 18 Advanced GroupBy Keys and Joined Grouping](phase-18-advanced-groupby-keys-and-joined-grouping/README.md)
- [0.8 Phase 19 SQL-Backed Projection Rows and Implicit Relation Projection](phase-19-sql-backed-projection-rows-and-implicit-relation-projection/README.md)
- [0.8 Phase 20 Query-Syntax Join Support](phase-20-query-syntax-join-support/README.md)
- [0.8 Phase 21 Joined Post-Paging Pushdown](phase-21-joined-post-paging-pushdown/README.md)
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
- full `GroupBy(...)` materialization as `IGrouping<TKey,TElement>` sequences, grouped element enumeration, and client-side fallback for unsupported grouped SQL shapes
- broad join expansion without source-slot, projection-row, and derived-source evidence
- implicit collection projection or hidden row multiplication
- DataLinq.Store query/module execution
- non-SQL backend execution as a 0.8 release requirement
- no-AOT browser WebAssembly support unless the current browser smoke proves it
- warning suppression as the final answer for Remotion
- shipping a Remotion-backed compatibility fallback inside the main DataLinq package
- OPFS/file-backed browser storage as part of the core 0.8 AOT claim

## After 0.8

Once the parser boundary, AOT/browser release gates, SQL-style GroupBy completion phases, and the planned projection/join completion phases are green, the next roadmap can resume feature work in a cleaner order:

1. scalar converters and typed keys
2. dependency-tracked result/module caching
3. non-SQL query executors if the plan proves stable enough
