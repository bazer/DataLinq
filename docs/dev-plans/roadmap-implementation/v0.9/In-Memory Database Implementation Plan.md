> [!WARNING]
> This document is roadmap implementation material for the DataLinq 0.9 development line. It is not normative product documentation and should not be treated as a shipped support claim.

# 0.9 Read-Only Memory Backend Implementation Plan

**Status:** Accepted. Aggregate M0, bounded M1-A exact non-null inequality, bounded M1-B exact Boolean composition, bounded M1-C exact non-null `Int32` relational comparison, W10 steps 1-2 / RE-1D package-tool integration, and W10 step 3 / RE-1A suite registration are complete. Aggregate M1-M3, RE-1C, RE-1E/F/G/H, W10 steps 4-9, aggregate RE-1/RE-4/W10/W11, packaged constrained-runtime evidence, consumer smoke, final release-candidate closeout, and publication remain open.

**Created:** 2026-07-03.

**Reframed:** 2026-07-10.

**Last reviewed:** 2026-08-04.

**Target:** DataLinq 0.9 experimental preview.

**Historical F7/W8 spike progress (2026-07-15 through D5-A):** The separate non-packable runtime and TUnit projects now exist. Dense `CanonicalProviderValueRow` instances sit behind per-table primary-key ordinal maps, while materialized immutable identities remain in separate existing `RowCache` instances. Direct neutral primary-key lookup and pass-through root entity plans execute without SQL. Repeated direct `Where` equality remains limited to an exact non-nullable `Int32` root column and exact non-null `Int32` scalar binding; one ascending or descending direct non-nullable converter-free `Int32` single-column-primary-key ordering may be followed by one final exact nonnegative `Int32` scalar-binding `Take`. The exhaustive profile remains 31 tokens and admits one final `ScalarMember` sequence over a direct non-nullable converter-free model/provider `Int32` root column with an exact `Int32` result; the selected column may be the primary key or a non-key column. Selectorless `Any` and `Count` reduce either root entities or that exact scalar projection through the same canonical-row cursor; predicate overloads remain bounded to the admitted root-column equality shape. Empty reductions return `false` and `0`, unordered `Any` short-circuits, and `Count` exhausts the selected cursor with checked `Int32` arithmetic. Ordered reductions inherit full-match buffering, and neither reduction materializes an entity, reads the projected cell, nor touches `RowCache`. Terminal operations after paging remain parser pushdowns and reject before row access. The scalar-projection shape still rejects string, nullable, widened, boxed, converter-backed, typed-ID, `Guid`, and non-root alternatives. Natural self-join, grouped-aggregate, and captured row-local projection plans freeze deterministic first capability failures as `SourceCount:Multiple` at `sources`, `Operation:GroupBy` at `operations[0]`, and `Projection:ComputedRowLocalExpression` at `projection`. All three reject before any store/cache diagnostic changes, and the captured value is absent from the error. One canonical-row cursor owns scan accounting, equality filtering, ordering, `Take`, cancellation, and disposal. Entity sequences materialize selected identities through `RowCache`; scalar sequences apply the shared scalar materializer/result adapter to the selected canonical cell and perform zero entity-cache work. Unordered projection streams lazily without a stable-order promise, while ordered projection retains full-match buffering and the existing total primary-key order. `Take(0)` performs no scan, `Take` limits above cardinality return all ordered rows, and public count arguments are snapshotted at query construction. Canonical seed publication and source-local identity remain proven, while one shared SQL/memory registry lock serializes resolution through generated binding; a gated unit test proves a concurrent caller cannot observe the winner before binding completes, and a 32-way cold start proves convergence on that graph. Canonical-provider and dense model-valued seed hooks remain internal; D5-A now adds public direct construction, `Query()`, and generated-mutable `Seed<TModel>(IEnumerable<Mutable<TModel>>)` without exposing those representation hooks. Bounded step-8 parity uses the same generated metadata and five adversarial exact-`Int32` rows in SQLite and memory. Every paired shape is parsed once into one `QueryPlanInvocation` and that same invocation executes through both sources. Hard-coded expected values cover every representative current 31-token dimension: unordered root and repeated equality, ascending/descending ordering, zero/bounded/over-cardinality `Take`, key/non-key scalar projection, composed and empty selections, and entity/projected `Any`/`Count`. Unordered outputs are normalized only after execution; ordered outputs are compared as returned. One separate invocation makes the SQLite-only post-`Take` terminal behavior explicit: SQLite succeeds, while memory rejects `Operation:Pushdown` before diagnostics change. Focused memory and capability evidence now passes `55/55` and `25/25`; the earlier integrated gates remain `1138/1138` unit, `57/57` generator, and `795/795` SQLite file/memory compliance tests. The parity test host intentionally carries SQLite and native assets; at that W8 checkpoint the memory runtime remained non-packable, built cleanly for net8/net9/net10 with zero warnings or errors, and its resolved runtime graphs remained free of SQL-provider and native-database packages. Pre-cancellation and cancellation between entity, filtered, ordered, and projected rows plus scalar-reduction pre-cancellation are proven through the internal spike surface; generated public LINQ supplies `CancellationToken.None`. This is an exact-`Int32` query-semantics, representative-diagnostics, focused parity, bounded model-seed/read, public-construction, and constrained-runtime proof, not support for broader typed-ID or `Guid` predicates, projections, membership, ordering, or relations. Ordered W8 steps 6 through 10 and bounded `F7` were complete at the end of this pre-D5-B checkpoint; `M0`, `M1`, `M2`, D5 package promotion, and W9 remained incomplete because package dependency evidence, the remaining query subset, wider semantics and parity, broader typed-ID/`Guid` query behavior, public typed primary-key lookup, and concurrent cache maintenance were still open.

The bounded W8 step-9 seed/read checkpoint adds internal `SeedModelValues<TModel>(object?[][])` for dense table-ordinal model rows without changing the older canonical-provider test surface. Each cell crosses the existing `ModelValueConverter` boundary before `CanonicalProviderValueRow` construction and key indexing, while a null model cell bypasses its converter. A full model type/nullability preflight runs across each row before its first converter. Completed reseeds and concurrent same-table attempts are rejected before their converter execution, while converter code itself runs outside the seed monitor. Invalid cells produce value-redacted top-level table, column, and row diagnostics; invalid rows and duplicate canonical keys leave the table unpublished. One generated fixture combines a Guid-backed typed-ID primary key, a direct `Guid`, another non-null typed-ID, and a nullable typed-ID. Its deliberately different SQLite, MySQL, and MariaDB `GuidStorage` declarations are never selected or encoded by memory: stored values and lookup identity remain canonical `Guid`, and generated immutable properties remain model-valued. Seed normalization calls `ToProvider` twice for the non-null converted cells. Cold materialization calls `FromProvider` twice and the existing generated immutable primary-key capture calls `ToProvider` once, producing cumulative counts of three and two. Warm canonical-key lookup and root enumeration reuse the same immutable instance without more conversion. Explicit test cache eviction rematerializes once, adding two `FromProvider` plus one key-capture `ToProvider` calls and producing cumulative counts of four and four. A differential canonical-seed case sends canonical `Guid` cells through `SeedCanonical` with zero seed-time `ToProvider`; cold materialization invokes `FromProvider` for all three converted fields plus one `ToProvider` for immutable primary-key capture. Focused evidence passes `7/7`; at that checkpoint the complete memory project passed `40/40`, and `DataLinq.Memory` built cleanly for net8, net9, and net10. This checkpoint does not admit typed-`Guid` predicates, projections, membership, ordering, relation navigation, or a public seed API/package.

The bounded W8 step-10 checkpoint adds a provider-free generated runner and separate Native AOT, full-trim, and Blazor WebAssembly hosts without changing the 31-token capability profile. All four modes execute canonical/model-valued seeding, primary-key hit/miss, equality, ordering plus `Take`, entity and scalar projection, `Any`/`Count`, self-join rejection before work, pre-cancellation, and canonical Guid-backed/direct-`Guid` storage. Native AOT and full-trim executables exit successfully; isolated browser no-AOT and AOT runs complete with no warning/error entries. Each final browser publish contains one fingerprinted copy of each DataLinq assembly, and all four final output roots scan clean for SQL-provider or native-database payload. Clean or isolated WebAssembly intermediates are required when switching AOT modes to avoid cross-mode stripped-IL contamination. At that checkpoint this completed bounded constrained-runtime evidence for the spike; D5-B and the broader M0/M1/M2 release surface still remained open.

The D5-A public-surface checkpoint resolves direct construction in favor of `MemoryDatabase<TDatabase>`, exposes the SQL-consistent `Query()` vocabulary, and accepts only generated mutable model rows through `Seed<TModel>(IEnumerable<Mutable<TModel>>)`. Seed rows are enumerated only after the per-table publication reservation, copied by frozen table ordinal, normalized through `ModelValueConverter`, and published atomically once. Completed or concurrent same-table attempts reject before source enumeration; empty publication seals the table; invalid values, lazy enumeration failures, and duplicate keys leave it retryable. Ordinary cleanup failure cannot mask an earlier seed failure, while cancellation or fatal cleanup exceptions still propagate without publication. Source mutation after return cannot alter stored rows, and separate memory databases share metadata while owning independent rows, read sources, and immutable identity. `MemorySeedException` and structured `QueryBackendCapabilityException` are public, while canonical rows/keys, dense arrays, metadata, read-source plumbing, diagnostics counters, explicit-token execution, and cache/test hooks remain internal. The provider-free smoke now uses public construction, generated mutable seeding, and `Query()` in Native AOT, full trim, WebAssembly no-AOT, and WebAssembly AOT; all modes pass and all output roots remain free of SQL-provider/native-database payload names. The memory suite passes `55/55`, and independent surface review is green. At the D5-A checkpoint, `DataLinq.Memory` deliberately remained non-packable until D5-B inspected actual package dependencies/assets and recorded an explicit promotion decision; W10 still owned release-tool registration and packaged compatibility reruns.

The historical bounded SC-6A canonical-`Guid` equality checkpoint superseded only the earlier blanket exclusion for `Guid` and Guid-backed typed-ID predicates above. At that checkpoint, the exhaustive memory profile contained 32 tokens and admitted exact default-null-semantics equality between one direct non-null root column and one non-null scalar when the column was either an identity-mapped `Guid` or a resolved scalar-converter-backed model type whose canonical provider CLR type was `Guid`. Both operand orders, selective hits and misses, invocation rebinding, and entity `Any`/`Count` reductions were proven. Each captured model scalar was normalized exactly once per predicate through the shared `ModelValueConverter` during execution-plan compilation; row evaluation then compared exact canonical `Guid` values. Memory did not inspect `GuidStorage`, invoke a SQL-provider codec, translate through bytes or text, or parse a `Guid`. A converter failure became a value-redacted public `QueryTranslationException` with no retained arbitrary inner exception graph, while `OperationCanceledException`, `OutOfMemoryException`, and `AccessViolationException` preserved exact exception identity. Nullable columns or bindings, inequality, local membership, `Guid` ordering, scalar projection, typed-ID member unwrapping, relations, and public typed primary-key lookup remained unsupported and rejected before memory row work. Construction, `Seed(...)`, and `Query()` were unchanged; SC-6A added no public lookup or representation surface. Same-invocation parity parsed each bounded expression once and executed that `QueryPlanInvocation` unchanged through memory and independently raw-seeded SQLite, proving the direct and converted `Guid` hit/miss and reduction observations without claiming general provider parity. The checkpoint gates passed `62/62` memory, `1214/1214` unit, and `60/60` generator tests; `DataLinq.Memory` built cleanly for net8, net9, and net10. Native AOT and full-trim executables passed, real-browser WebAssembly no-AOT and AOT runs passed without warning/error entries, and every published output remained clean of SQL-provider and native-database payloads. At the SC-6A checkpoint, D5-B package promotion, the remaining memory query surface, aggregate SC-6, and aggregate W6 remained open.

The D5-B package checkpoint promotes `DataLinq.Memory` to a packable experimental preview after inspecting real local core and Memory candidates at `0.9.0-preview.d5b.5`. Private MinVer aligns ordinary packing with core, SourceLink supplies repository provenance, and shared explicit overrides align the inspected candidate pair. The package embeds a dedicated Memory preview README with the bounded supported surface and explicit unsupported mutation, transaction, durability, persistence, raw SQL, relation, join/grouping, projection, and general-LINQ boundaries. Each Memory dependency group for net8, net9, and net10 contains only `DataLinq` at the candidate minimum with build/analyzer assets excluded. The runtime archive has exactly one managed Memory assembly per TFM and no analyzer, runtime, native, build, build-transitive, or tool folder; the symbol archive has exactly one PDB per TFM. Direct metadata, archive, and binary-token checks find no SQL-provider, native-database, Roslyn, Remotion, or generator payload, while the explicit two-package report has zero findings. At that checkpoint D5 and W9 step 1 were complete, `DataLinq.Tests.Memory` remained non-packable, and M0/M1/M2, D6, W10 integration, packaged constrained-runtime reruns, final public support wording, and publication remained open.

The bounded M0-A public lookup checkpoint adds `MemoryDatabase<TDatabase>.Find<TModel>(object)` for the non-null model-side value of exactly one generated primary-key column. Converter-backed keys normalize through the shared `ModelValueConverter` boundary and probe the existing canonical primary-key index without scanning; hits materialize the generated immutable model, misses and unseeded-table probes return `null`, warm probes preserve exact identity, and separate database instances retain separate stores, read sources, and identities. Wrong model values, raw canonical or numeric surrogates, composite metadata, and ordinary failures during initial `ToProvider` normalization, canonical-to-model `FromProvider` materialization, or generated immutable primary-key `ToProvider` identity capture produce value-redacted public `MemoryLookupException` diagnostics without arbitrary inner exception graphs; literal null remains `ArgumentNullException`, fatal and cancellation exceptions at all three conversion points preserve identity, and failed materialization or identity capture does not poison the cache. Focused lookup evidence passes `15/15`, the full Memory suite passes `77/77`, net8/net9/net10 builds remain clean with zero warnings and zero errors, Native AOT and full-trim executables pass, isolated WebAssembly no-AOT and AOT browser runs reach `passed`/`completed` with only expected logs, and banned-provider/native-payload scans remain clean. The LINQ capability profile remains 32 tokens. At that checkpoint this completed the exact single-column public-lookup slice only: no generated `Get(...)` overload whose source parameter was typed as `MemoryDatabase<TDatabase>` or `IDataLinqReadSource` alone was added, composite lookup remained unsupported, aggregate M0 still awaited Testing CLI registration and structural SQL-boundary work, and W10 remained open.

The later W10 step-3 / RE-1A registration checkpoint makes `DataLinq.Tests.Memory` the distinct Testing CLI `memory` suite. The suite is non-target-batched and runs once even when `--alias all` is supplied. Direct built execution with summary JSON passes `77/77`, emits one result with `Targets` `-`, and leaves the test-infrastructure state file's hash and timestamp unchanged. The composite `--suite all --alias quick --build` gate passes `2162/2162`, including generators `60`, unit `1214`, memory `77` exactly once, and compliance `811` across the two SQLite targets. The CLI list surface is correct. The test project intentionally references `DataLinq.SQLite` for bounded differential parity, so this is project-based suite-registration evidence rather than provider-free or package-consumer evidence. W10 step 3 and RE-1A registration are complete; compatibility-catalog, release-package, aggregate RE-1/W10, and final release-candidate work remain open.

The aggregate M0 structural-boundary checkpoint completes the generated store and seed foundation without adding a fake raw-SQL API. `MemoryDatabase<TDatabase>` exposes only `Find`, `Query`, and `Seed`; the complete public `IDataLinqReadSource` contract exposes only metadata and inherits no operational interface; and the Memory construction/query route supplies none of `IDataSourceAccess`, `IDatabaseProvider`, or `IDatabaseAccess` and exposes no Memory provider-style post-seed CRUD/commit/transaction service. Primitive and converter-backed fixtures freeze the existing shared generated `Get(...)` source parameters to exactly `IDataSourceAccess`, `Database<TDatabase>`, or `Transaction<TDatabase>`; none is typed as `IDataLinqReadSource` alone or `MemoryDatabase<TDatabase>`. The canonical primitive fixture proves its row, root, and query provider expose no SQL access interface, while consumer-authored partial members remain outside the Memory contract. The legacy inherited `GetDataSource()` member and parameterless `Delete()` extension reject with the same DataLinq-owned diagnostic without additional backend work, Memory diagnostics remain unchanged, and public lookup preserves stored identity. Focused public-boundary tests pass `14/14`, and the full targetless Memory suite passes `78/78`. Earlier M0-A net8/net9/net10 and constrained-runtime evidence remains applicable because production assemblies and the 32-token profile are unchanged. D5-B's dependency/archive conclusion remains relevant, but the package README changed and W10 still owns a fresh aligned package candidate. Aggregate M0 is complete; M1, M2, and the remaining W10 compatibility/package work remain open.

**M1-A exact non-null inequality checkpoint:** The Memory backend adds only `ComparisonOperator:NotEqual`; the 610-feature catalog and SQL's 352-supported/258-unsupported profile are unchanged, while the exhaustive Memory profile grows from 32 to 33 tokens. `!=` is admitted under default null semantics only for the two existing exact column/scalar shapes: direct non-nullable converter-free model/provider `Int32`, and non-nullable canonical `Guid` from either direct `Guid` or a resolved Guid-backed typed ID. Both operand orders, late invocation rebinding, mixed `==`/`!=`, entity and direct-`Int32` scalar projection, selectorless `Any`/`Count`, and the existing primary-key ordering plus final `Take` compositions are covered. Typed model scalars normalize once per predicate through `ModelValueConverter`; row comparison uses canonical values and never `GuidStorage`, SQL text, or provider byte codecs. Ordinary converter failures remain value-redacted without an inner exception graph, while cancellation and fatal exceptions retain identity. Strings, widened/boxed numerics, column-to-column and nullable comparisons, typed-ID member unwrapping, ordered predicates, compound boolean predicates, membership, `Skip`, `ThenBy`, element terminals, anonymous projections, joins, relation navigation, and grouping still reject before Memory row work. Focused and same-invocation differential tests prove the exact primitive, direct-`Guid`, and Guid-backed typed-ID inequality paths; the differential fixtures execute each parsed plan through Memory and independently raw-seeded SQLite. The targetless Memory suite passes `88/88`, and the runtime builds cleanly for net8/net9/net10. The constrained-runtime smoke exercises the representative primitive `Int32 !=` path, not canonical-`Guid` inequality; Native AOT and full-trim publishes execute successfully; isolated WebAssembly no-AOT and AOT publishes reach `passed` in real browser runs with zero warning/error entries; and recursive path/content scans of all four output roots find no SQL-provider/native-database payload. This advances only bounded M1 and D6 comparison semantics. At that checkpoint, aggregate M1/M2 and W10 package/compatibility reruns remained open, and the packed README change required a fresh later candidate.

**M1-B exact Boolean-composition checkpoint:** The Memory backend adds only `Predicate:And`, `Predicate:Or`, and `Predicate:Not`; the 610-feature catalog and SQL's 352-supported/258-unsupported profile are unchanged, while the exhaustive Memory profile grows from 33 to 36 tokens. `&&`, `||`, and `!` are admitted only as nested plan-tree composition over the existing exact default-null-semantics `==`/`!=` leaves: direct non-nullable converter-free model/provider `Int32`, and non-nullable canonical `Guid` from either direct `Guid` or a resolved Guid-backed typed ID. `And` and `Or` evaluate terms left-to-right with row-time short circuit, while `Not` evaluates and negates its child once. Every captured scalar is still normalized eagerly exactly once per comparison leaf while the invocation-local row plan is compiled before enumeration; branch short circuit does not defer or suppress conversion. The trees compose with repeated `Where`, entity and direct-`Int32` scalar projection, selectorless `Any`/`Count`, and the existing exact primary-key ordering plus final `Take`. Any unsupported predicate kind or comparison leaf still rejects at its exact nested capability location before store, cache, binding-conversion, or row work. Focused tests prove nested truth, precedence, negation, late rebinding, row-time short circuit, eager per-leaf Guid-backed normalization, unsupported-child zero-work rejection, and unchanged materialization boundaries. Same-invocation differential fixtures execute representative primitive and canonical-`Guid` trees through Memory and independently raw-seeded SQLite; this is bounded regression pressure, not general provider parity. The targetless Memory suite passes `96/96`, and the runtime builds cleanly for net8/net9/net10. The constrained-runtime smoke exercises one representative primitive tree containing all three operators, not canonical-`Guid` Boolean composition; Native AOT and full-trim publishes execute successfully, isolated WebAssembly no-AOT and AOT publishes reach `passed` in real browser runs with zero warning/error entries, and recursive path/content scans of all four output roots find no SQL-provider/native-database payload. This advances only bounded M1 and D6 predicate composition. At that checkpoint, aggregate M1/M2 and W10 package/compatibility reruns remained open, and the packed README change required a fresh later candidate.

**M1-C exact non-null Int32 relational checkpoint:** The Memory backend adds only `ComparisonOperator:GreaterThan`, `ComparisonOperator:GreaterThanOrEqual`, `ComparisonOperator:LessThan`, and `ComparisonOperator:LessThanOrEqual`; the 610-feature catalog and SQL's 352-supported/258-unsupported profile are unchanged, while the exhaustive Memory profile grows from 36 to 40 tokens. `<`, `<=`, `>`, and `>=` are admitted under default null semantics only between one direct non-nullable converter-free model/provider `Int32` root column and one exact non-null `Int32` scalar, in either operand order. Scalar-left forms invert the operator before the row predicate is constructed; row evaluation then uses the corresponding direct C# `int` comparison and never subtraction, so this slice introduces no comparison-arithmetic overflow path. Existing exact direct-`Guid` and resolved Guid-backed typed-ID `==`/`!=` leaves remain admitted, but relational canonical-`Guid` comparisons classify to `QueryPlanComparisonShape.DefaultNullSemantics` and reject before Memory store, binding-conversion, cache, or row work. The new leaves compose inside the bounded M1-B `And`/`Or`/`Not` trees and with repeated `Where`, entity and direct-`Int32` scalar projection, selectorless `Any`/`Count`, the exact primary-key ordering, and final `Take`. Focused evidence passes `6/6` `MemoryOrderedInt32ComparisonTests` and `25/25` `QueryPlanCapabilityValidationTests`; the full targetless Memory suite passes `103/103`. Capability contracts freeze the exact 40-token list, exact relational-`Int32` classification in both operand directions, canonical-`Guid` relational fallback, and the unchanged 610/352/258 catalog/SQL matrix. One same-invocation differential range fixture covers all four relational operators, both operand directions, and late rebinding through Memory and independently raw-seeded SQLite; this is bounded regression pressure, not general provider parity. `DataLinq` and `DataLinq.Memory` build cleanly for `net8.0`, `net9.0`, and `net10.0` with zero warnings and zero errors. Native AOT and full-trim publishes and executables pass with the exact range result and capability count (`range-filtered=[-5,17]`, `capabilities=40`). Isolated WebAssembly no-AOT and AOT publishes reach `passed` in real browser runs with the same exact range result and capability count, the expected `querying-relational-range` stage, and zero warning/error entries. Recursive filename and binary/text scans of the `aot`, `trim`, `wasm-noaot`, and `wasm-aot` output roots find none of `DataLinq.SQLite`, `DataLinq.MySql`, `Microsoft.Data.Sqlite`, `MySqlConnector`, `SQLitePCLRaw`, or `e_sqlite3`. This advances only bounded M1/D6 exact `Int32` relational semantics. At that checkpoint, aggregate M1/M2 and W10 package/compatibility reruns remained open, and the packed README change required a fresh later candidate.

**W10 steps 1-2 / RE-1D package-tooling checkpoint:** Commits `bdae5f5b` and follow-up version fix `39522ce376a2dddb4faa7dcaded80d470889abb2` make Memory one of six default public packages and one of four default runtime packages, reject non-empty pack output, and fail package inspection closed over aligned version/identity/metadata, independently inventoried symbols, exact dependencies/assets, managed assembly identity, and banned payloads. The first `0.9.0-preview.w10.1` probe exposed that `PackageVersion` did not override MinVer; the follow-up uses `MinVerVersionOverride`. The fresh final `0.9.0-preview.w10.2` candidate at `artifacts/nuget-release/0.9.0-preview.w10.2` contains six exact-version package/symbol pairs. Default schema `v0.9.package-inspection-report.v3` evidence at `artifacts/dev/package-report/20260804-075329094` records six packages, six symbol packages, six expected packages, four runtime packages, zero findings, and zero hard failures. Memory has exact net8/net9/net10 DLL/PDB sets, three valid CLI assemblies named `DataLinq.Memory`, exact same-version core-only dependency groups with `Build,Analyzers` excluded, clean metadata/root assets, and no provider, native, Roslyn, Remotion, or generator payload. Inspector/size tests pass `17/17` and `9/9`; unit passes `1231/1231`; integrated quick passes `2205/2205` (`60` generators + `1231` unit + `103` memory + `811` compliance); Dev CLI builds with zero warnings/errors; DocFX has zero errors and only the two known duplicate `AnalyzerReleases` warnings. No package was published. This completes only W10 steps 1-2 and RE-1D; all status-listed release work remains open. Aggregate M1/M2 remain unchanged at Memory `40`, catalog `610`, and SQL `352` supported / `258` unsupported.

## Decision

DataLinq 0.9 should keep the memory backend, but only as a read-only experimental preview.

The preview exists to prove that the runtime can start from generated metadata and execute a deliberately small `DataLinqQueryPlan` subset without SQL. It does not need mutation, transactions, durability, fixture-forking, or broad query parity to prove that point.

The 0.9 claim should be no stronger than:

> DataLinq 0.9 includes an experimental, read-only memory backend for generated models. It supports seeding, primary-key lookup, and a documented query subset through DataLinq query plans, including browser/WebAssembly and strict AOT smoke coverage.

Anything beyond that claim is a separate feature with separate evidence.

## Why This Boundary

The previous plan combined four architecture projects:

- a backend-neutral provider/source and execution boundary
- a read-only memory query engine
- an in-process transactional database
- a persistence and replay system

That is too much for one release and makes the dependency graph circular. A read-only provider is enough to expose whether query plans, materialization, provider values, capabilities, generated metadata, AOT, and browser execution are genuinely backend-neutral. Mutation can follow after those seams have survived real use.

## Ownership And Dependencies

The 0.9 workstreams use local identifiers (`M0` through `M3`) rather than reusing release-wide phase numbers.

| Concern | Owning workstream | Memory dependency |
| --- | --- | --- |
| Backend-neutral provider/source, row-reading, cache, and materialization boundaries | 0.9 query/runtime foundation | Must exist before `M0` is complete |
| Backend-neutral execution boundary, capabilities, and self-contained execution request | 0.9 query/runtime foundation | Must exist before `M1`; supported projection data must not depend on the original expression |
| Model-to-canonical-provider conversion, including typed IDs | Scalar-converter work | Must exist before `M0` is complete |
| Canonical-provider-to-physical UUID encoding | UUID work | Owned by SQL providers; memory must not copy it into row storage |
| Shared provider-value row-buffer type and materializer contract | 0.9 query/runtime foundation | Memory consumes these contracts; it does not define a second row representation |
| Memory tables, indexes, seeding, and execution over shared provider-value buffers | This plan | Owned here |
| Model-to-canonical and canonical-to-model scalar conversion | Scalar-converter work | Memory invokes the shared conversion boundary; it does not own conversion policy |
| Model-valued `RowData` materialization | 0.9 query/runtime foundation plus scalar conversion | Must be proven through the memory adapter in `M0` |
| JSON snapshot codec prototype | Optional JSON stretch plan | Starts only after `M3`; never blocks this plan |

The dependency direction is deliberately one way:

```text
query/runtime foundation + scalar/provider conversion
                         |
                         v
                 read-only memory preview
                         |
                         v
             optional JSON snapshot prototype
```

Memory must consume the shared conversion system. It must not invent a second conversion layer merely to unblock itself. JSON must consume an already-working memory store. It must not be a prerequisite for memory.

## Explicit 0.9 Scope

The preview includes:

- generated-metadata startup with no runtime schema discovery
- explicit seed loading
- canonical provider-value row storage by column ordinal
- primary-key indexes and direct primary-key lookup
- conversion from provider-value buffers to model-valued `RowData` before generated model materialization
- a small, capability-gated query subset
- clear diagnostics for unsupported operations and plan nodes
- no Memory-owned raw-SQL, command, connection, provider, or transaction entry point
- strict AOT and browser/WebAssembly proof

The preview does not include:

- insert, update, delete, or `Save`
- transactions, isolation, rollback, or conflict handling
- generated-key allocation or mutation-time defaults
- store forks, reset APIs, named snapshots, or failure injection
- canonical commit batches or change receipts
- persistence, automatic loading, automatic flushing, or durability
- commit logs, replay, or compaction
- broad relation, join, or grouping support
- a claim of SQL semantic equivalence
- a claim that memory is the default substitute for provider-backed tests

## M0: Generated Store And Seed Foundation

**Progress:** Complete. Bounded M0-A proves generated startup, public model-valued exact single-column lookup, canonical-index hit/miss, converter-backed typed-ID normalization, materialization, warm identity, store isolation, and constrained-runtime execution. W10 step 3 registers the distinct targetless `memory` suite. The final structural-boundary test freezes the `MemoryDatabase`/neutral-source SQL-service absence and rejects the legacy provider/implicit-delete routes without additional backend work.

Work:

- continue from the separate, initially non-packable `DataLinq.Memory` project created by the `F7` vertical spike
- **Complete (W10 step 3 / RE-1A registration):** continue using the separate TUnit `DataLinq.Tests.Memory` project as the distinct non-target-batched Testing CLI `memory` suite; keep `sqlite-memory` as the separate SQLite provider target
- after the spike passes, promote `DataLinq.Memory` to a preview NuGet package; if the spike fails, stop and re-scope rather than moving the backend into the core package
- start the store exclusively from generated/frozen DataLinq metadata
- store memory rows in the shared compact canonical-provider-value buffer defined by foundation workstream `F3`
- normalize seed model values through the shared scalar-converter pipeline to canonical provider CLR values
- build primary-key identities from canonical provider values
- reject duplicate primary keys and malformed seed values with table, column, and row context
- build the minimum primary-key index needed for direct lookup
- do not expose or emulate raw-SQL services on Memory, and do not implement SQL service interfaces with throwing stubs
- avoid native dependencies, runtime schema discovery, runtime code generation, and `Expression.Compile()`

The representation boundary is mandatory:

```text
seed/model value
    -> canonical provider CLR value
    -> memory provider-value buffer
    -> provider-to-model conversion
    -> model-valued RowData
    -> generated immutable model
```

Existing `RowData` and model indexer behavior must remain model-valued. Storing provider values internally does not authorize changing that public/runtime contract.

Exit signal:

- a generated database starts without a SQL provider or live database
- representative rows, typed IDs, and canonical `Guid` values seed successfully through shared conversion
- configured UUID physical formats do not change the canonical value stored by memory
- **Bounded M0-A complete:** public exact single-column primary-key lookup returns a correctly materialized generated model or `null` for a miss without exposing canonical provider values
- cache/materialization integration does not expose provider values through `RowData`
- raw SQL is structurally unavailable through `MemoryDatabase<TDatabase>` and the neutral source; the legacy inherited provider member and implicit-delete extension reject with a DataLinq-owned diagnostic without additional backend work

## M1: Capability-Gated Query Subset

Implement the smallest useful subset that exercises query-plan execution:

- direct entity enumeration from one table
- `Where` equality, inequality, and ordered comparisons over supported scalar columns
- boolean `&&`, `||`, and `!`
- local scalar `Contains(...)` membership
- `OrderBy`, `ThenBy`, `Skip`, and `Take`
- `Any`, `Count`, `First`, `FirstOrDefault`, `Single`, and `SingleOrDefault`
- direct scalar projection from one source
- direct anonymous projection from one source only when the plan contains all information needed to execute it without re-reading the original expression tree

Every supported node must be represented in explicit memory-backend capability metadata. Unsupported joins, relation traversals, grouping, aggregates, projection forms, methods, and result operators must fail capability validation with a diagnostic that names the unsupported shape.

The executor must not:

- compile expression trees
- silently switch to unrestricted LINQ-to-Objects
- generate or parse SQL
- accept a query merely because SQLite accepts it
- re-extract executable projection behavior from the original expression after planning

Exit signal:

- the documented subset executes directly over memory row buffers
- supported projection execution is driven by the execution request/plan, not a hidden copy of the source expression
- unsupported shapes fail predictably before partial execution
- repeated query invocation values do not leak into reusable backend state

Bounded W8 step-7 evidence freezes one parser-valid representative for every named diagnostic family without widening the profile: a self-join rejects as `SourceCount:Multiple` at `sources`, grouped aggregation rejects as `Operation:GroupBy` at `operations[0]`, and a captured computed row projection rejects as `Projection:ComputedRowLocalExpression` at `projection`. Each failure precedes store, cache, predicate, and materialization work, and the captured value is redacted. This completes the spike's representative diagnostic proof, not the broader M1 operator matrix.

## M2: Semantics And Materialization Contract

Before calling any operator supported, document and test its semantics:

- null equality and ordering
- string equality, ordering, and case sensitivity
- numeric comparison and coercion boundaries
- date/time comparison
- enum and typed-ID comparison through canonical provider values
- canonical `Guid` comparison without applying provider-specific UUID byte layouts
- membership with null and empty local sequences
- deterministic paging only when ordering is sufficient
- `First` and `Single` error/default behavior

These are DataLinq memory semantics, not proof of every SQL provider's semantics. Parity tests against SQLite are useful regression pressure, but a matching result for a small sample is not evidence of general SQL equivalence.

The bounded W8 step-8 matrix is intentionally stronger than merely comparing two independently written queries and intentionally weaker than a general parity claim. Each query is built from the generated memory model, parsed exactly once, and the resulting `QueryPlanInvocation` is sent unchanged to the memory and SQLite sources. Both stores receive the same five logical rows with adversarial `Int32` keys (`Int32.MinValue`, `-11`, `0`, `17`, and `Int32.MaxValue`), repeated group values, and distinct names. Hard-coded expected observations cover root and repeated-equality entity results, both key orders, `Take(0)`, bounded and over-cardinality `Take`, key and non-key scalar projections, composed filter/order/page/projection, empty selection, and entity/projected `Any` and `Count`. Unordered entity observations are normalized only after execution because neither backend promises their order; ordered observations are never normalized. The matrix also preserves the deliberate boundary instead of forcing false agreement: a shared post-`Take` terminal invocation returns `true` from SQLite but rejects as memory `Operation:Pushdown` before row or diagnostic work. SQLite/native dependencies belong to the test host only; the `DataLinq.Memory` runtime graph remains clean. These two tests close bounded W8 step 8 for the then-current 31-token exact-`Int32` island, while wider `M2`/D6 semantics, typed IDs, canonical `Guid`, and general SQL parity remain open.

The bounded `Int32` ordering/`Take` checkpoint intentionally proves only a total-order island. One direct root ordering is accepted when its column is non-nullable, converter-free, model/provider `Int32`, and the table's entire primary key; ascending and descending are both defined by canonical `Int32` order without subtraction. There can be no admitted key ties, so this slice defines no stable-tie or seed-order behavior. Zero or more admitted Boolean predicate trees over the exact equality and inequality leaves plus the exact direct-`Int32` relational leaves may appear before the ordering or between the ordering and final `Take`; the executor evaluates them before sorting because filtering preserves that ordered subsequence and the predicates cannot observe evaluation order. Nothing moves across `Take`, `Skip`, pushdown, a post-projection operator, or a future user-defined evaluation boundary.

`Take` is accepted only once, after that sufficient ordering, with a direct nonnegative `Int32` scalar binding. Unordered, repeated, negative, converted, null, overflowed, and other count shapes reject before store access. `Take(0)` returns empty without scanning; a count above cardinality returns every matching row in key order. `Queryable.Take(local)` receives the value when the query object is built, so re-enumerating the same query keeps that count and rebuilding the query is required to observe a changed local. Ordered execution buffers all canonical matches and is not a top-N or streaming-performance claim; only selected rows cross the materialization/cache boundary. Cancellation checks exist at bounded scan, merge, selection, and yield points only through the internal spike execution surface.

The bounded scalar-projection checkpoint admits only a final `ScalarMember` sequence over exactly one root source. The selected primary-key or non-key column, its model/provider types, and the declared sequence result must all be non-nullable converter-free `Int32`; string, nullable, widened, boxed, converter-backed, typed-ID, `Guid`, anonymous/constructed/local, joined, relation, grouped, aggregate, and element-terminal alternatives reject before store access. Projection composes with the admitted Boolean predicate trees over exact equality and inequality leaves plus exact direct-`Int32` relational leaves, optional exact primary-key ordering, and optional final `Take`; row selection always completes before the cell is read. The projection cursor applies shared scalar materialization and result adaptation directly to the canonical cell, never materializes an entity, and never touches `RowCache`. Unordered projection is lazy but defines no general stable-order contract; ordered projection retains full-match buffering, and `Take(0)` still performs no scan. Pre-cancellation and cancellation between projected rows are proven only through the internal token-aware execution surface.

Selectorless `Any` and `Count` reduce the admitted entity or exact scalar-projection row island directly through the canonical-row cursor. They intentionally do not materialize entities or projected cells: existence and cardinality depend on selected rows, not selected values. Empty input yields `false` or `0`; unordered `Any` stops after its first selected row, while `Count` exhausts the cursor with checked `Int32` arithmetic. Ordering remains semantically valid but currently buffers every match before reduction, so this is not a performance claim. A predicate overload is supported only when it becomes an admitted Boolean predicate tree over the exact root-column equality and inequality leaves plus the exact direct-`Int32` relational leaves. Terminal operations after `Take` remain unsupported because the parser represents them as `Pushdown`, and projected-value predicates, `LongCount`, other scalar reductions, element terminals, general SQL parity, and public cancellation remain open.

Cancellation for the bounded spike is checked before execution and at bounded scan, filter, buffered-ordering, materialization, projection, and reduction points. Focused tests prove the original token on pre-cancellation, cancellation between root and filtered entity rows, after buffered ordering between entity yields, between scalar-projection rows, and before `Any`/`Count`. This is internal execution-surface evidence only: generated public LINQ still supplies `CancellationToken.None`, and no general asynchronous or public cancellation API is claimed.

Materialization tests must prove:

- memory stores provider values internally
- provider values are converted back to model values exactly once at the materialization boundary
- `RowData`, model properties, keys, relations, and cache identity receive the representation they expect
- configured scalar converters do not leak provider values into model-facing APIs
- UUID physical codecs remain outside the memory row/materialization boundary

Exit signal:

- each advertised operator has an explicit semantics test matrix
- semantic differences from SQLite/MySQL/MariaDB are documented rather than hidden
- provider/model value separation is enforced by focused tests

## M3: Release Evidence

Required evidence:

- unit tests for row buffers, seed conversion, primary-key normalization, and duplicate-key diagnostics
- memory-provider tests for startup, seed, lookup, supported queries, materialization, and unsupported capabilities
- typed-ID and canonical-`Guid` tests through seed, lookup, predicate, membership, and projection paths where applicable
- regressions proving configured UUID physical formats do not leak byte arrays/text encodings into memory rows
- focused cross-provider tests against SQLite for the intentionally shared subset
- strict AOT smoke using generated models
- browser/WebAssembly smoke with no native SQLite or filesystem dependency
- package and target-framework verification for the new preview surface
- allocation measurements for startup, primary-key lookup, and repeated simple queries; these inform follow-up work and are not arbitrary release thresholds

The browser smoke should prove:

1. generated-provider startup
2. seed loading
3. primary-key lookup
4. one filtered query
5. one ordered/paged query
6. one supported projection
7. one unsupported-query diagnostic
8. typed-ID or canonical-`Guid` behavior when those features are part of the 0.9 claim

No browser persistence is required. Browser execution is the proof.

Bounded W8 step-10 evidence satisfies this browser/runtime checklist for the then-current internal 31-token spike through project references. W10 still owns compatibility-catalog registration, package-based reruns, size/report integration, the retained SQLite regression graph, and final release-candidate evidence.

## Release Boundary

The experimental preview may ship only when:

- generated startup, seed loading, and primary-key lookup work without SQL
- the provider-value-buffer-to-model-`RowData` boundary is correct
- the supported query matrix is small, explicit, and capability-gated
- raw SQL is structurally unavailable, while unsupported expression-query plans fail clearly before row work
- strict AOT and browser/WebAssembly smokes pass
- public wording says `experimental` and `read-only`
- documentation explicitly warns that memory is neither SQL semantic proof nor a general replacement for provider-backed integration tests

If any of those conditions fail, cut the preview rather than quietly weakening its architecture.

## Testing Position

The memory backend is useful for:

- testing DataLinq query-plan and materialization behavior inside its documented subset
- fast application tests whose assertions do not depend on provider-specific SQL, collation, type affinity, constraints, or transaction behavior
- examples, demos, and transient browser state

It is not sufficient for:

- SQL translation validation
- migration or schema validation
- provider collation/null/date behavior
- server constraint and concurrency behavior
- transaction, rollback, locking, or durability tests
- deciding that a query works on SQLite, MySQL, or MariaDB

Provider-backed compliance and integration suites remain authoritative for provider behavior.

## Deferred Until After 0.9

The next memory design stage may consider:

1. provider-neutral mutation and transaction boundaries
2. insert/update/delete with trustworthy mutable-instance lifecycle semantics
3. atomic root replacement, rollback, and documented conflict handling
4. constraints, generated values, and relation/index invalidation
5. provider-neutral committed-change receipts or canonical commit batches
6. store forks, reset helpers, and richer fixture APIs
7. persistence integration, commit logs, replay, and compaction
8. broader query capabilities based on demonstrated demand

Those items remain valid design directions. They are not hidden 0.9 stretch goals.

## Claims To Avoid

- "SQL-compatible in-memory database"
- "drop-in replacement for every provider"
- "all LINQ works in memory"
- "default database test replacement"
- "full ACID database"
- "transactional memory database"
- "browser persistence"
- "durable store"

## Links

- [0.9 Implementation Order And Integration Plan](Implementation%20Order%20and%20Integration%20Plan.md)
- [Release Evidence And Closeout Implementation Plan](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md)
- [Memory Backend Design Notes](../../backends/memory/README.md)
- [Memory Backend Architecture](../../backends/memory/Architecture.md)
- [0.9 Memory JSON Snapshot Prototype](Memory%20JSON%20Persistence%20Implementation%20Plan.md)
- [DataLinq 0.9 Roadmap](README.md)
