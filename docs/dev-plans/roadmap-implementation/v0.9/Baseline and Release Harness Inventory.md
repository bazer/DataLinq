> [!WARNING]
> This document is roadmap execution material for the DataLinq 0.9 development line. It records a before-state and does not describe shipped 0.9 behavior.

# 0.9 Baseline And Release Harness Inventory

**Status:** W0-W2 complete. This remains the 2026-07-10 before-state; later implementation checkpoints, including the W8 step-10 provider-free Memory constrained-runtime graph, SC-6A canonical-`Guid` equality island, D5-B local package promotion, aggregate M0, bounded M1-A exact non-null inequality, bounded M1-B exact Boolean composition, bounded M1-C exact non-null `Int32` relational comparison, bounded M1-D exact local `Int32` membership, bounded M1-E exact ordered final `Skip`, bounded M1-F exact `Single`/`SingleOrDefault`, W10 steps 1-2 / RE-1D package-tool integration, and W10 step 3 / RE-1A Testing CLI registration, are recorded separately below and do not rewrite the baseline.

**Baseline branch:** `v0.9`.

**Baseline source commit:** `8bcfc770246f960e27a91e3046f19a76c3736217`.

**Last reviewed:** 2026-08-04.

**Starting worktree:** Clean, on `v0.9`, before characterization-only test and documentation changes.

**Authority:** The release ordering and ownership rules remain in [Implementation Order And Integration Plan](Implementation%20Order%20and%20Integration%20Plan.md). This record freezes the observable before-state used by W2 and later work.

## Outcome

The first 0.9 slice changed no production runtime, public API, package, provider, or support claim.

It did four things:

1. catalogued every expression-query entry, SQL-builder construction, original-expression dependency, primary-key shortcut, cache-cold loader, and relation loader that later waves will move
2. resolved projection disposition D1 for every current `QueryPlanProjectionKind`
3. added focused query, parsed-plan binding, primary-key, reader-lifetime, transaction-cache, relation, mutable-reuse, provider-lifecycle fault, file-backed SQLite/WAL, scalar-value, typed-ID-fixture, canonical-key, and UUID-vector characterization
4. captured reproducible build, provider, package, compatibility, and benchmark evidence, including real baseline failures instead of laundering them into green claims

At W0 the production baseline was healthy across the complete SQL provider matrix. Native AOT and trimming were green. Both historical SQLite-shaped WebAssembly publish lanes were red under SDK 10.0.301 because the Blazor SDK requested a missing `ResolveWasmOutputs` target. That failure reproduced outside the sandbox and was therefore a real W0 release-harness gap, not sandbox noise. It remains baseline evidence; it does not describe the later, separate W8 memory-only browser graph.

## Reproduction Environment

| Field | Value |
| --- | --- |
| Host | Windows 10.0.26200, `win-x64` |
| .NET SDK | 10.0.301, MSBuild 18.6.4 |
| Installed workload | `wasm-tools` 10.0.108 |
| Core target matrix | `net8.0`, `net9.0`, `net10.0` |
| Local providers | SQLite file and named shared in-memory |
| Server providers | MySQL 8.4; MariaDB 10.11, 11.4, and 11.8 |
| Server host ports | 13307 through 13310 on `127.0.0.1` |
| Test artifact root | `artifacts/testdata/` and `artifacts/release/v0.9/w0-8bcfc770246f/` |
| Package artifact root | `artifacts/nuget-release/w0-8bcfc770246f/` |
| Compatibility artifact root | `artifacts/dev/compat-size-report/` |
| Benchmark artifact root | `artifacts/benchmarks/` |

The artifact tree is ignored by Git. This tracked record is the durable manifest; local JSON, logs, packages, and binaries are supporting evidence, not the only record that a run occurred.

## Query Execution Route Inventory

### Production roots and funnels

Generated database models expose `DbRead<T>` properties. All production expression queries enter through those properties and then converge on the DataLinq expression provider.

| Stage | Production location | Responsibility |
| --- | --- | --- |
| Generated query property | `DataLinq.SharedCore/Factories/Models/ModelFileFactory.cs:140` | Constructs `DbRead<T>` from the selected source access. |
| Query wrapper | `DataLinq/DbRead.cs:11-24` | Exposes the generated table root. |
| Root provider construction | `DataLinq/Linq/Queryable.cs:22-29` | Creates `ExpressionQueryPlanProvider.ForExecution(...)`. |
| Database root | `DataLinq/Database.cs:106-109` | `Database<T>.Query()`. |
| Read-only root | `DataLinq/Mutation/ReadOnlyAccess.cs:92` | `ReadOnlyAccess<T>.Query()`. |
| Transaction root | `DataLinq/Mutation/Transaction.cs:484` | `Transaction<T>.Query()`. |
| Composition | `ExpressionPlanQueryable.cs:46-47` | `CreateQuery<TElement>`. |
| Sequence execution | `Queryable.cs:37-46`; `ExpressionPlanQueryable.cs:68-72,102-106` | Enumerates a parsed sequence plan. |
| Terminal execution | `ExpressionPlanQueryable.cs:49-66` | Runs terminal result operators, including the pre-parse primary-key shortcut. |

`ExpressionQueryPlanProvider.CreateRoot<T>()` has no production caller. Parser-only constructors and test inspection helpers are not execution roots.

### `QueryPlanSqlBuilder` construction

Production construction is currently centralized in `ExpressionPlanQueryable.cs`, but split by result/projection family:

| Site | Current route |
| --- | --- |
| `:143` | Entity sequence |
| `:487` | Terminal entity sequence |
| `:495` | Scalar or aggregate result |
| `:541` | Joined row-local projection through joined primary keys |
| `:604` | Grouped-aggregate projection |
| `:626` | Direct scalar-member projection |
| `:641` | SQL-backed row projection |
| `:699` | Single-source local projection reprojected through entity rows |

Nested builders inside `QueryPlanSqlBuilder.cs` are deliberate renderer composition:

- `:108`: ordinary post-paging pushdown
- `:135`: joined post-paging pushdown
- `:405`: grouped `Count` or `Any` wrapper

Inspection-only construction remains in `CurrentQueryTranslationInspection.cs:17,40,63` and `PlatformSmokeRunner.cs:422`.

### Original-expression dependency

The original expression is still a hidden second execution plan:

- provider handoff: `ExpressionPlanQueryable.cs:52-72`
- executor APIs: `:133-181`
- terminal projection routing: `:470-510`
- projection recovery: `:704-739`
- recovered-lambda consumers: `:513-569`
- local recipe interpretation: `ProjectionExpressionEvaluator.cs:27-76`

`TryGetProjectionLambda` walks backwards through `Select`, `Join`, terminal operators, filtering, ordering, and paging. F2 must replace that walk with a self-contained recipe or an early unsupported disposition.

The primary-key shortcut performs a separate expression walk at `ExpressionPlanQueryable.cs:111-131,264-467`.

### Bypasses and neutral-read migration ownership

| Route | Current implementation | Later owner |
| --- | --- | --- |
| Terminal scalar-PK shortcut | `ExpressionPlanQueryable.cs:54-65,184-248,264-467` | W5 query foundation |
| Entity cache optimization | `Select.cs:294-325` | W5 query foundation |
| Single-row cold lookup | `TableCache.RowLookup.cs:40-87` | W5 query foundation |
| Key query and batch dispatch | `TableCache.RowLookup.cs:125-173` | W5 query foundation |
| Batched/ordered PK load | `TableCache.RowLoading.cs:16-58,178-214` | W5 query foundation |
| PK SQL construction | `TableCache.RowQueries.cs:17-67,116-127` | W5 query foundation |
| Scalar-column direct command | `TableCache.RowQueries.cs:177-247` | W5 query foundation |
| Relation lookup dispatch | `TableCache.RowLookup.cs:16-38` | W5 query foundation |
| Relation FK load | `TableCache.RowLoading.cs:60-176` | W5 query foundation |
| Lazy collection/reference load | `ImmutableRelation.cs:232-249`; `ImmutableForeignKey.cs:93-102` | W5 query foundation |
| Index preload | `TableCache.Indexes.cs:120-186` | W5 query foundation |
| Pending/committed publication | `Transaction.cs`, `TableCache.Invalidation.cs`, notifications | W3 transaction correctness; W5 must preserve it |

Direct generated and public `Get` callers ultimately feed the same cold path. They include `Database.cs:154-173`, `Transaction.cs:492-497`, `InstanceFactory.cs:46-58`, and generated static accessors in `GeneratorFileFactory.cs:1021,1027`.

## D1 Projection Disposition

| Projection kind | Disposition | 0.9 rule |
| --- | --- | --- |
| `Entity` | 1: direct plan value | Execute from source and plan metadata. |
| `ScalarMember` | 1: direct plan value | Execute from the explicit column value. |
| `SqlRow` | 1: SQL-backed row | Keep the explicit members and constructor contract. |
| `GroupedAggregate` | 1: SQL-backed row | Keep explicit group keys and aggregate members. |
| `ComputedRowLocalExpression` | 2: self-contained AOT-safe recipe | Replace the shape string plus recovered lambda with an interpreted recipe. |
| `Anonymous` | 3: SQL-only compatibility recipe initially | Preserve SQL behavior without claiming reflection-backed construction is AOT-safe. Promote only with real constrained-runtime evidence. |
| `JoinedRowLocal` | 3: SQL-only compatibility recipe | SQL may materialize joined source rows; memory rejects it in 0.9. |
| `TransparentIdentifier` | 4: unsupported as a final result | Retain only as parser-internal query-syntax binding. Reject it if it reaches executable-plan output. |

No kind may recover behavior from the original expression after F2. Memory support is intentionally narrower than SQL support.

## Characterization Evidence

### Query, bindings, projection, and ownership

| Invariant | Evidence |
| --- | --- |
| Entity, scalar, aggregate, SQL row, computed local, anonymous, joined local, grouped, join, paging, null, and result shapes | `QueryPlanSnapshotTests` |
| Captured values are absent from debug output | `QueryPlanSnapshotTests`; `QueryPlanNodeTests` |
| Scalar, null, and local sequences freeze independently per parsed plan | `CapturedBindings_AreFrozenAndIsolatedAcrossParsedPlans` |
| Local-sequence arrays are copied at plan freeze | `QueryPlan_FreezesLocalSequenceBindingValues` |
| Joined row-local function projection executes across providers | `ExplicitInnerJoin_RowLocalFunctionProjection_MatchesInMemory` |
| Terminal PK cold hit, warm hit, and absent-key telemetry | `EmployeesOptimizationTests` |
| Relation collection/reference cold-load command and cache telemetry | `Query_RelationTraversal_ColdCacheMiss_LoadsAndStoresRows` |
| Reader disposal on completion, early stop, and reader failure | `DatabaseAccessReaderLifetimeTests` |
| Read-only and transaction-root parity | Existing join, grouping, projection, implicit-relation, and post-paging provider tests |

Command disposal is not uniformly trustworthy today. `Select.ReadFirstRow` and the scalar-column row query explicitly own commands, but several inline `ToDbCommand(...)` calls in sequence/scalar routes do not. W5 owns explicit backend-result and command lifetime. The characterization deliberately does not assert that leaking ownership is desired behavior.

### Transaction and cache baseline

| Invariant | Evidence |
| --- | --- |
| Successful statements affect transaction-local rows before commit | `Cache_UpdateBeforeCommit_UsesTransactionLocalRowCache` |
| Outside cached identity/value stays committed before rollback or commit | Same test plus relation insertion tests |
| Provider commit precedes global publication | `Transaction.Commit` order plus commit invalidation/notification tests |
| Rollback preserves committed row identity and purges transaction rows | `Cache_Rollback_DoesNotInvalidateReadOnlyRowCacheForUncommittedMutation` |
| Open transaction disposal rolls back and purges transaction rows | `Cache_OpenTransactionDispose_RemovesTransactionRowsAndPreservesReadOnlyRowCache` |
| Managed-wrapper rollback/open disposal terminalizes touched ownership, drops exact transaction rows/subscriptions, preserves committed state, and publishes wrapper `RolledBack` only after finalization | deterministic `TransactionFaultInjectionCharacterizationTests`, `TransactionMutationFailureTests`, `MutableLifecycleTests`, and `CacheNotificationManagerTests`; active-provider `EmployeesMutableLifecycleTests` |
| Outside relation remains stable before commit and refreshes after commit | `Transaction_InsertRelations_PersistsAfterCommit` |
| Relation rollback remains scoped and does not notify outside subscribers | `Transaction_RelationInsertRollback_KeepsViewsScopedAndDoesNotNotifyOutsideSubscriber` |
| Same-transaction graph identity | `Transaction_InsertRelationsWithinTransaction_MaintainsGraphIdentity` |
| Commit clears transaction cache | `Transaction_InsertRelationsReadAfterCommit_ClearsTransactionCache` |
| Current repeated mutable reuse behavior | implicit and explicit repeated-save characterization tests |
| Provider commit, rollback, and disposal success/failure partitions | `TransactionFaultInjectionCharacterizationTests` |
| Owned-path committed policy, private-cache WAL committed visibility, explicit shared-cache locking, caller timeout preservation, failure telemetry, and bounded writer contention | `SQLiteWalConcurrencyCharacterizationTests` |

The following accepted behaviors were intentionally assigned to W3 rather than encoded as green W1 behavior:

- provider and transaction provenance
- cross-provider and cross-transaction mutable rejection
- reuse rejection after rollback, disposal, deletion, or uncertain failure
- primary-key mutation rejection before command creation
- read-only mutation rejection before SQL
- failed-statement poisoning and commit rejection

Subsequent W3 slices have closed the listed provenance, cross-owner, primary-key, read-only, mutation-poisoning, and bounded managed-wrapper rollback/open-disposal deficits. Bounded `TX-3` replaces the rollback/disposal defect expectations with provider-first completion attempts, accurate `RolledBack`/`RollbackOutcomeUnknown`/`OpenTransactionDisposed` ownership, touched invalidation and registry clearing, exact transaction row/subscription discard, committed-cache preservation, deferred finalized wrapper status, and an only-dispose gate after a failed rollback that remains open. The adjacent managed recovery now records permanent `CommitOutcomeUnknown` when the provider `Commit()` call throws, preserves the exact provider exception, invalidates/clears touched and transaction-local state, structurally evicts provider-wide committed rows and indices before recovery notifications, attaches recovery failures as secondary context, rejects further managed use, and permits only status-compatible rollback or disposal. Bounded native-provider evidence around an injected completion boundary proves both actual outcomes: pre-commit throw plus rollback retains the old row, while native commit plus throw exposes the new row after conservative cache recovery. `TX-5A` proves active attached wrapper-only commit promotion/reuse and rollback invalidation across every provider, and prevents wrapper commit after external commit/rollback from manufacturing success. Bounded `TX-5B` detects an inactive original handle before managed read/write/fallback/dispose, records `ExternalCompletionUnknown`, extends provider-wide recovery to externally completed wrapper rollback/disposal, and proves fresh rematerialization of the actual external outcome across every active provider. Preventing raw low-level escape, arbitrary local-cache primitive fault injection, connector-native/full provider commit-fault evidence, and full concurrency remain open; the W1 statements above remain historical baseline rather than present-tense runtime claims.

The temporary file-backed WAL lane proves `SQ-1`: every DataLinq-owned path resets `read_uncommitted=0`; private-cache readers retain committed insert/update/delete state during a pending write; an explicit shared-cache reader locks instead of receiving pending data; and attached transactions preserve caller policy. `SQ-2` is also green: CLI and test-harness file defaults omit `Cache`, generated paths open successfully, named memory retains shared cache, and explicit caller settings are not rewritten. Bounded `SQ-3` proves a competing writer honors both the connection default and an explicit command timeout, preserves `SQLITE_BUSY` codes/message, emits one failed `update` activity per attempted command, and triggers no DataLinq retry.

### Value, key, and UUID baseline

The W1-V tests approve independent values and current seams without pretending the future converter or codec already exists.

| Invariant | Evidence |
| --- | --- |
| Primitive `int`, `long`, `Guid`, and `string` metadata is model/provider identity with no converter handle | `PrimitiveKeyMetadata_UsesIdentityProviderRepresentation` |
| Typed-ID record-struct fixtures have value equality and equality-consistent hashing | `TypedIdFixtures_HaveValueEqualityAndHashSemantics` |
| Canonical provider keys preserve CLR type, value equality, hash, and composite boundaries | `CanonicalProviderKeys_*` |
| Canonical `Guid` is distinct from text and byte-array physical representations | `CanonicalGuidKey_IsDistinctFromPhysicalRepresentations` |
| Native `Guid`, text36, text32, legacy little-endian binary, and RFC-order binary use fixed known vectors | `GuidKnownVector_*` |
| Finalized direct-`Guid` defaults use the resolved codec for all 13 SQLite/MySQL/MariaDB text, native, and binary representations | `GuidStorageStaticDefaultTests`; `MetadataFromSqlFactoryDefaultParsingTests` |
| `DefaultNewUUID` Version4/Version7 remain distinct through direct parse, direct metadata-to-model regeneration, semantic comparison, snapshots, and digests; provider DDL fails closed; exact MySQL/MariaDB `UUID()` imports remain provider-scoped raw SQL | `SyntaxParserTests`; `ModelFileFactoryTests`; `SchemaComparerGuidStorageTests`; `SchemaMigrationSnapshotTests`; `GuidStorageStaticDefaultTests`; `MetadataFromSqlFactoryDefaultParsingTests` |
| Direct generated `DefaultNewUUID` Version4/Version7 client initialization works on net8/net9/net10, matches the RFC 9562 UUIDv7 vector, and evaluates each generated default once across parameterless and required-constructor paths | `GeneratedDefaultValueFactoryTests`; `GeneratorFileFactoryTests`; `SourceGeneratorTests`; `DataLinq.Tests.Models` multi-target build |
| Exact-D `[DefaultGuid("...")]` parses to the same real-`Guid` default meaning as an expression-free base `DefaultAttribute(Guid)`, initializes mutable models through `global::System.Guid.Parse(...)`, regenerates canonically, and remains normalized across comparison, snapshots, roundtrip, and digest fingerprints | `SyntaxParserTests`; `MetadataDefinitionFactoryTests`; `ModelFileFactoryTests`; `SchemaComparerGuidStorageTests`; `SourceGeneratorTests` |
| Finalized converter-backed canonical `Int32` accepts SQLite `INTEGER` or signed MySQL/MariaDB `INT`, rejects physically matching incompatible storage with one review-only canonical diagnostic, skips unresolved metadata, and invokes no converter | `SchemaComparerScalarStorageTests`; `SchemaComparerScalarStorageProviderTests`; `SchemaDiffScriptGeneratorTests` |
| Finalized converter-backed canonical `Int64` accepts SQLite `INTEGER` or signed MySQL/MariaDB `BIGINT`; the same exact canonical long also traverses bounded `F6-B` PK/FK relations and `JoinedRowLocal` key hydration without narrowing or converter calls at those dispatch/decode seams | `SchemaComparerScalarStorageTests`; `SchemaComparerScalarStorageProviderTests`; `DataSourceAccessSourceRowLoaderTests`; `SqlLocalProjectionExecutorTests`; `Int64TypedIdKeyBoundaryTests` |
| One representative explicit-inner `JoinedRowLocal` Guid-backed typed-ID binary key resolves concrete-provider storage, decodes the selected alias to canonical `Guid`, hydrates both sources through dynamic cache identity, and keeps projected entity properties model-valued without exposing raw bytes | `SqlLocalProjectionExecutorTests`; `JoinedGuidTypedIdKeyHydrationTests` |
| Internal memory model-valued seed normalizes direct and Guid-backed typed fields through the shared scalar boundary, stores canonical `Guid` rows/keys without applying provider `GuidStorage`, and rematerializes model-valued immutable identities across cold, warm, and test cache-eviction paths | `MemoryModelSeedTests` |
| Exact resolved canonical-`Guid` `F6-B` relation/index admission accepts only an already-canonical single component on a concrete built-in provider, preserves rollback isolation, warms one committed index on the cold collection load, and reuses exact child and reverse-parent identity without converter calls at the gate | `ProviderKeyComponentsTests`; `DataSourceAccessSourceRowLoaderTests`; `JoinedGuidTypedIdKeyHydrationTests` |

The approved vector is `00112233-4455-6677-8899-aabbccddeeff`. Its current DataLinq/MySQL compatibility bytes are `33221100554477668899AABBCCDDEEFF`; its RFC-order bytes are `00112233445566778899AABBCCDDEEFF`.

At W1, format-aware schema validation and diffing could not be characterized honestly before `GuidStorageDefinition`; that was a baseline limitation. UUID-1B introduced the metadata, and bounded UUID-4 now covers physical-type-gated format matching, unobservable-layout diagnostics, trusted same-type manual-migration differences, exact-D source representation/regeneration for ordinary fixed direct-`Guid` defaults, DDL encoding through the exact provider codec, UUID-version preservation, fail-closed `DefaultNewUUID` provider DDL, faithful raw `UUID()` import, and net8-safe single-evaluation direct client generation. The later bounded SC-5 checkpoint additionally covers finalized converter-backed canonical `Int32` compatibility after physical equality, including review-only incompatible-storage diagnostics, integer metadata normalization, physical-drift precedence, unresolved-metadata skipping, and zero converter calls. The latest integrated gates pass `60/60` generators, `1203/1203` unit tests, `803/803` SQLite file/memory tests, `1622/1622` in the four-server compliance batch, and `376/376` in the full MySQL/MariaDB provider-specific lane. Focused source-carrier evidence passes `61/61` parser, `190/190` metadata-definition, `22/22` model-file, `15/15` comparer, and `1/1` generator tests; focused canonical-compatibility evidence passes `13/13` scalar comparer, `15/15` neighboring UUID comparer, `6/6` diff-script, and `4/4` live-provider cases; focused client-generation evidence passes `5/5` runtime, `16/16` constructor-generation, `1/1` source-generator, and `6/6` fail-closed SQLite default tests. The fixed-`Guid` carrier is storage-neutral and normalizes to an expression-free base `DefaultAttribute(Guid)`; `[Default("guid-text")]` remains invalid, while the already-covered codecs render the canonical value across all 13 formats. The generated Version7 helper embeds UTC Unix milliseconds plus random remaining bits and does not promise same-millisecond monotonicity. Source-only typed-ID converter resolution, canonical compatibility beyond finalized converter-backed `Int32`, converter-backed defaults, static provider-default import, transformer precedence, SQLite expression/BLOB import, and verified provider-version/storage-aware automatic server generation remain later work.

The subsequent exact-`Int64` checkpoint requires SQLite `INTEGER` or signed MySQL/MariaDB `BIGINT` after normalized physical equality, rejects matching text, signed narrower server integers, and unsigned `BIGINT`, and invokes no converter. The same canonical long is admitted through the exact single-column `F6-B` relation/index route and decoded at the selected `JoinedRowLocal` alias ordinal before cache hydration. Dedicated values from `5_000_000_101` through `6_000_000_203` prove high-range identity rather than accidental `Int32` compatibility. Focused evidence is `2/2` for the joined-reader seam, including the `Int16` legacy-path exclusion, `19/19` for the scalar schema class, and `11/11` for the loader class; relation/joined compliance passes `4/4` on SQLite file/memory and `8/8` across four servers, while live schema comparison passes `8/8` across those servers. Current integrated gates pass `60/60` generator, `1211/1211` unit, `807/807` SQLite file/memory, `815/815` in each paired server batch (`1630/1630` total), and `189/189` plus `191/191` provider-specific executions (`380/380` total). Canonical compatibility beyond finalized `Int32` and `Int64`, other converted integral relation/joined keys, string/CHAR, UUID/`Guid`, composite/external/manual/memory routes, source-only resolution, converter-backed defaults, and aggregate SC-5/W6 remain open.

The next exact joined Guid-backed typed-ID checkpoint narrows only one explicit-inner `JoinedRowLocal` path. On concrete SQLite, MySQL, and MariaDB sources, any resolved active-provider `GuidStorage` makes the selected alias ordinal column-aware; the runtime gate is format-agnostic. This checkpoint's provider evidence and support claim use one representative binary mapping, where physical bytes decode to canonical `Guid`, become a dynamic `DataLinqKey`, and hydrate both joined sources through the existing cache. Non-symmetric raw vectors, repeated-parent identity, warm same-instance reuse, canonical cache keys, and model-valued public results prove that no `byte[]` leaks across the boundary. The reader seam passes `5/5` with neither converter direction invoked. Cold end-to-end immutable construction records the expected three `ToProvider` and five `FromProvider` calls; warm execution adds zero. Focused compliance passes `2/2` on SQLite file/memory and `4/4` across the four server targets. Current integrated gates pass `60/60` generator, `1214/1214` unit, `809/809` SQLite file/memory, `817/817` in each paired server batch (`1634/1634` total), and `189/189` plus `191/191` provider-specific executions (`380/380` total). Joined-key evidence for text/native UUID storage, other typed mappings/formats, composites, outer/missing-source joins, UUID relation/index/foreign-key routing, external/key-only/preload/manual/provider-less readers, source-only resolution, converter-backed defaults, memory/AOT, and aggregate W6/UUID completion remain open.

The bounded W8 step-9 memory seed/read checkpoint adds a separate generated fixture with a Guid-backed typed-ID primary key, a direct `Guid`, another non-null typed field, and a nullable typed field. The internal dense model seed path invokes the shared `ModelValueConverter`, publishes only fully validated canonical rows, and indexes the primary key as `Guid`; deliberately different SQL-provider `GuidStorage` definitions remain unapplied metadata. Seed normalization calls `ToProvider` twice, and nullable null bypasses conversion. Cold materialization adds two `FromProvider` calls plus one `ToProvider` for existing generated immutable primary-key capture (cumulative three and two). Warm lookup and root enumeration reuse the exact immutable instance with no new calls. Test cache eviction and rematerialization add two `FromProvider` plus one key-capture `ToProvider` call (cumulative four and four). A differential canonical-seed case sends canonical `Guid` cells through `SeedCanonical` with zero seed-time `ToProvider`; cold materialization invokes `FromProvider` for all three converted fields plus one `ToProvider` for immutable primary-key capture. A full model type/nullability preflight runs before each row's first converter, completed reseeds and concurrent same-table attempts reject before their converter execution, and invalid or duplicate rows leave the table unpublished; invalid-cell top-level diagnostics carry value-redacted row/column context. Converter code runs outside the seed monitor. At that checkpoint focused tests passed `7/7`, all `DataLinq.Tests.Memory` tests passed `40/40`, and `DataLinq.Memory` built cleanly for net8/net9/net10. The public seed/package shape, Guid-backed typed-ID predicates/projections/membership/relations, `M0`, and aggregate UUID completion remain open. The following step-10 checkpoint supersedes the AOT/browser, bounded `F7`, and ordered W8-step status only.

The bounded W8 step-10 memory constrained-runtime checkpoint adds a non-packable provider-free generated runner plus non-packable Native AOT, full-trim, and Blazor WebAssembly hosts. The unchanged 31-token profile executes canonical/model-valued seed, primary-key hit/miss, captured equality, ordering plus `Take`, entity and scalar materialization, `Any`/`Count`, deterministic unsupported self-join rejection before work, pre-cancellation, and canonical Guid-backed/direct-`Guid` storage. Native AOT and full-trim executables publish and exit successfully; isolated WebAssembly no-AOT and AOT publishes complete in a real browser with zero warning/error entries. Recursive scans of all four outputs find no SQL-provider or native-database payload names. The full memory suite passes `42/42`. At that checkpoint this completed bounded `F7` and W8 step 10, but did not promote a package, register a Testing CLI/compatibility target, complete D5/SC-6/M0-M2, or replace the historical SQLite graph.

The later D5-A memory public-surface checkpoint exposes only direct `MemoryDatabase<TDatabase>` construction, `Query()`, and generated-mutable `Seed<TModel>(IEnumerable<Mutable<TModel>>)`, plus public catchable seed and structured capability exceptions. It keeps canonical rows/keys, dense seed arrays, metadata/read-source plumbing, diagnostics, explicit-token execution, and cache/test hooks internal. Public-path tests cover value snapshots, Guid-backed typed conversion, store/read-source/identity isolation, atomic correction after malformed or lazy-enumerator failure, cleanup failure precedence, cancellation propagation, completed and empty-table reseed rejection before enumeration, and redacted seed plus structured capability diagnostics. The provider-free shared runner now uses that public construction/seed/query route; Native AOT and full trim exit successfully, isolated WebAssembly no-AOT and AOT reach `passed` in a real browser with no warning/error logs, and all four outputs still scan clean for SQL-provider/native-database payload names. The full memory suite passes `55/55`. Independent surface review is green. At the D5-A checkpoint this was still project-reference evidence: `DataLinq.Memory` remained non-packable pending D5-B inspection of actual package dependencies/assets and an explicit promotion decision.

The later exact resolved-canonical-`Guid` `F6-B` relation/index checkpoint supersedes only the UUID relation exclusion above. Neutral relation dispatch admits exactly one index column whose canonical provider CLR type is `Guid`, and only on a concrete SQLite, MySQL, or MariaDB source with resolved active-provider `GuidStorage`. Direct-`Guid` and converter-backed metadata are eligible only when the caller already supplies canonical `Guid`; the admission and exact-key seams invoke neither converter direction. Model wrappers, raw bytes, UUID text, composite indices, missing or unresolved active-provider storage, and `DatabaseType.Unknown` retain legacy routing. Provider end-to-end evidence is narrower than the format-agnostic gate: one representative converter-backed binary mapping uses RFC-order bytes on SQLite and MariaDB and little-endian bytes on MySQL. A rolled-back transaction-local delete leaves the committed relation index cold; the next committed collection load issues one reader, rematerializes both children, and warms one canonical index. Warm index access returns the same child instances, and reverse references resolve to the same parent. Cold counters are `ToProvider=3` and `FromProvider=4`; warm index access adds nothing, and two reverse references finish at `ToProvider=5`, `FromProvider=4`. Current integrated gates pass `60/60` generator, `1214/1214` unit, `811/811` SQLite file/memory compliance, `819/819` in each paired server batch (`1638/1638` total), and `189/189` plus `191/191` provider-specific executions (`380/380` total). Direct-`Guid` relation end-to-end evidence, text/native relation formats, composites, custom-provider/provider-less/external/key-only/preload/manual routes, memory relations, and aggregate `F6`/W6/UUID completion remain open.

The later bounded SC-6A memory-query checkpoint admits exactly non-null canonical-`Guid` column/scalar equality, in either operand order, for direct `Guid` columns and resolved Guid-backed typed-ID columns. Typed scalar bindings normalize once through the existing converter boundary; direct `Guid` bindings do not invoke it. Public-query evidence covers primary-key and non-key hits and misses, rebinding, repeated mixed equality, `Any`, and `Count`. Same-invocation differential tests parse each query once and execute the exact `QueryPlanInvocation` through both Memory and SQLite: Memory is seeded through the public model-valued surface, while SQLite is independently raw-seeded with a little-endian BLOB typed primary key, Text36 direct `Guid`, and RFC-order BLOB typed non-key value. Cross-wired non-byte-symmetric values produce the same selective results on both backends. Nullable typed-`Guid` equality, `NotEqual`, local-sequence membership, ordering, scalar projection, and unwrapped typed-ID member access remain unsupported or translation-rejected before store work; nearby shapes are not implied by this island. At that checkpoint, the capability catalog had `610` features, with SQL supporting `352` and rejecting `258`; the Memory profile had `32` supported tokens. The bounded gates at that checkpoint passed `62/62` Memory, `1214/1214` Unit, and `60/60` Generator tests, and `DataLinq.Memory` built for `net8.0`, `net9.0`, and `net10.0` with zero warnings and zero errors. Native AOT and full-trim hosts published and executed successfully; isolated WebAssembly no-AOT and AOT hosts reached `passed` in a real browser with zero browser warning/error entries; and banned-token scans of all four outputs remained clean. This was project-reference, non-packaged evidence only. At the SC-6A checkpoint, D5-B package inspection/promotion, W10 catalog/package integration, aggregate SC-6, and aggregate W6 remained open.

The D5-B package checkpoint promotes `DataLinq.Memory` as a packable experimental preview while leaving `DataLinq.Tests.Memory` non-packable. Local core and Memory candidates were built at `0.9.0-preview.d5b.5`; private MinVer makes the default package version track core instead of producing a stable Memory package over a prerelease dependency, SourceLink supplies repository provenance, and shared explicit overrides align the inspected candidate pair. The package embeds a dedicated Memory preview README with the bounded supported surface and explicit unsupported mutation, transaction, durability, persistence, raw SQL, relation, join/grouping, projection, and general-LINQ boundaries. The Memory nuspec has one same-candidate `DataLinq` minimum in each net8/net9/net10 group. Its runtime archive contains exactly three managed Memory assemblies and no analyzer/runtime/native/build/tool folder; its symbol archive contains exactly three PDBs. Direct metadata and binary-token inspection found no SQL-provider, native-database, Roslyn, Remotion, or generator payload, and the explicit two-package report had zero findings. This completes D5 and W9 step 1 only; the package/report presets, memory-specific inspection policy, Testing CLI suite, compatibility targets, package consumer smoke, packaged constrained-runtime reruns, and publication remain W10 work.

The later bounded M0-A checkpoint exposes `MemoryDatabase<TDatabase>.Find<TModel>(object)` for one non-null model-side value when generated metadata declares exactly one primary-key column. Converter-backed keys normalize through the shared canonical-value boundary and probe the existing index without scanning. Direct and Guid-backed typed-ID hits/misses, unseeded miss, warm same-instance reuse, model-valued rows, and separate store/read-source/identity ownership are proven. Wrong model values, raw canonical or numeric surrogates, composite metadata, and ordinary failures during initial `ToProvider` normalization, canonical-to-model `FromProvider` materialization, or generated immutable primary-key `ToProvider` identity capture produce value-redacted public `MemoryLookupException` diagnostics with no arbitrary inner exception graph; literal null produces `ArgumentNullException`, cancellation/fatal exceptions at all three conversion points preserve identity, and failed materialization or identity capture leaves the cache recoverable. Focused lookup evidence passes `15/15`, the full Memory suite passes `77/77`, `DataLinq.Memory` builds for net8/net9/net10 with zero warnings and zero errors, Native AOT and full-trim executables pass, isolated WebAssembly no-AOT and AOT browser runs reach `passed`/`completed` with only expected logs, and all four output roots remain free of SQL-provider/native-database payloads. The Memory LINQ profile stays at 32 tokens. At that checkpoint this completed only bounded public exact single-column lookup, not a generated `Get(...)` overload whose source parameter was typed as `MemoryDatabase<TDatabase>` or `IDataLinqReadSource` alone, composite lookup, aggregate M0, Testing CLI/structural SQL-boundary closeout, compatibility-catalog/package reruns, or W10.

The later W10 step-3 / RE-1A registration checkpoint adds `DataLinq.Tests.Memory` to `DataLinq.Testing.CLI` as the targetless `memory` suite and includes it exactly once in the composite `all` suite. Direct `--suite memory --build --summary-json` and explicit `--suite memory --alias all` executions each pass `77/77` with one summary result and `Targets` `-`; the direct run preserves the test-infrastructure state file's hash and timestamp. `--suite all --alias quick --build` passes `2162/2162`, comprising generators `60`, unit `1214`, memory `77` exactly once, and compliance `811` across `sqlite-file` and `sqlite-memory`; the `list` output is correct. The CLI test project deliberately references `DataLinq.SQLite` for bounded differential parity, so this checkpoint proves project-based, non-target-batched suite orchestration rather than provider-free or package-based execution. At that checkpoint it completed W10 step 3 and RE-1A registration only; structural SQL-boundary and aggregate M0 closeout, compatibility/report catalogs, package-consumer and packaged constrained-runtime evidence, aggregate RE-1/W10, and publication remained open.

The aggregate M0 structural-boundary checkpoint proves that the Memory route does not expose SQL or Memory provider-style post-seed CRUD/commit/transaction services. `MemoryDatabase<TDatabase>` remains limited to `Find`, `Query`, and `Seed`; the complete public `IDataLinqReadSource` contract remains metadata-only and inherits no operational interface; and the Memory construction/query route supplies none of `IDataSourceAccess`, `IDatabaseProvider`, or `IDatabaseAccess`. Primitive plus converter-backed fixtures freeze the existing shared generated `Get(...)` source parameters to exactly `IDataSourceAccess`, `Database<TDatabase>`, or `Transaction<TDatabase>`, never a parameter typed as `IDataLinqReadSource` alone or `MemoryDatabase<TDatabase>`. The canonical primitive fixture also proves its row, root, and query provider expose no SQL access interface; consumer-authored partial members remain outside the Memory contract. The legacy inherited `GetDataSource()` member and parameterless `Delete()` extension reject without additional backend work, Memory diagnostics remain unchanged, and public lookup preserves stored identity. Focused public-boundary tests pass `14/14`, and the complete targetless Memory suite passes `78/78`. Earlier M0-A net8/net9/net10 and constrained-runtime evidence remains applicable because production assemblies are unchanged. D5-B still establishes the dependency/archive boundary, but the embedded README changed and W10 must inspect a fresh aligned candidate. Aggregate M0 is complete; M1/M2 and the remaining compatibility/package/release work remain open.

The UUID-version snapshot fingerprint intentionally advances newly written schema migration snapshots to format version 2. Existing format-version-1 JSON remains readable because deserialization does not enforce a version gate. `DefaultGuid` normalization represents the existing fixed-`Guid` meaning and therefore does not bump the snapshot format again. This fingerprint evidence is not a source/database merge claim: `MetadataTransformer` precedence for source defaults remains open.

**M1-A exact non-null inequality checkpoint:** The Memory backend adds only `ComparisonOperator:NotEqual`; the 610-feature catalog and SQL's 352-supported/258-unsupported profile are unchanged, while the exhaustive Memory profile grows from 32 to 33 tokens. `!=` is admitted under default null semantics only for the two existing exact column/scalar shapes: direct non-nullable converter-free model/provider `Int32`, and non-nullable canonical `Guid` from either direct `Guid` or a resolved Guid-backed typed ID. Both operand orders, late invocation rebinding, mixed `==`/`!=`, entity and direct-`Int32` scalar projection, selectorless `Any`/`Count`, and the existing primary-key ordering plus final `Take` compositions are covered. Typed model scalars normalize once per predicate through `ModelValueConverter`; row comparison uses canonical values and never `GuidStorage`, SQL text, or provider byte codecs. Ordinary converter failures remain value-redacted without an inner exception graph, while cancellation and fatal exceptions retain identity. Strings, widened/boxed numerics, column-to-column and nullable comparisons, typed-ID member unwrapping, ordered predicates, compound boolean predicates, membership, `Skip`, `ThenBy`, element terminals, anonymous projections, joins, relation navigation, and grouping still reject before Memory row work. Focused and same-invocation differential tests prove the exact primitive, direct-`Guid`, and Guid-backed typed-ID inequality paths; the differential fixtures execute each parsed plan through Memory and independently raw-seeded SQLite. The targetless Memory suite passes `88/88`, and the runtime builds cleanly for net8/net9/net10. The constrained-runtime smoke exercises the representative primitive `Int32 !=` path, not canonical-`Guid` inequality; Native AOT and full-trim publishes execute successfully; isolated WebAssembly no-AOT and AOT publishes reach `passed` in real browser runs with zero warning/error entries; and recursive path/content scans of all four output roots find no SQL-provider/native-database payload. This advances only bounded M1 and D6 comparison semantics. At that checkpoint, aggregate M1/M2 and W10 package/compatibility reruns remained open, and the packed README change required a fresh later candidate.

**M1-B exact Boolean-composition checkpoint:** The Memory backend adds only `Predicate:And`, `Predicate:Or`, and `Predicate:Not`; the 610-feature catalog and SQL's 352-supported/258-unsupported profile are unchanged, while the exhaustive Memory profile grows from 33 to 36 tokens. `&&`, `||`, and `!` are admitted only as nested plan-tree composition over the existing exact default-null-semantics `==`/`!=` leaves: direct non-nullable converter-free model/provider `Int32`, and non-nullable canonical `Guid` from either direct `Guid` or a resolved Guid-backed typed ID. `And` and `Or` evaluate terms left-to-right with row-time short circuit, while `Not` evaluates and negates its child once. Every captured scalar is still normalized eagerly exactly once per comparison leaf while the invocation-local row plan is compiled before enumeration; branch short circuit does not defer or suppress conversion. The trees compose with repeated `Where`, entity and direct-`Int32` scalar projection, selectorless `Any`/`Count`, and the existing exact primary-key ordering plus final `Take`. Any unsupported predicate kind or comparison leaf still rejects at its exact nested capability location before store, cache, binding-conversion, or row work. Focused tests prove nested truth, precedence, negation, late rebinding, row-time short circuit, eager per-leaf Guid-backed normalization, unsupported-child zero-work rejection, and unchanged materialization boundaries. Same-invocation differential fixtures execute representative primitive and canonical-`Guid` trees through Memory and independently raw-seeded SQLite; this is bounded regression pressure, not general provider parity. The targetless Memory suite passes `96/96`, and the runtime builds cleanly for net8/net9/net10. The constrained-runtime smoke exercises one representative primitive tree containing all three operators, not canonical-`Guid` Boolean composition; Native AOT and full-trim publishes execute successfully, isolated WebAssembly no-AOT and AOT publishes reach `passed` in real browser runs with zero warning/error entries, and recursive path/content scans of all four output roots find no SQL-provider/native-database payload. This advances only bounded M1 and D6 predicate composition. At that checkpoint, aggregate M1/M2 and W10 package/compatibility reruns remained open, and the packed README change required a fresh later candidate.

**M1-C exact non-null Int32 relational checkpoint:** The Memory backend adds only `ComparisonOperator:GreaterThan`, `ComparisonOperator:GreaterThanOrEqual`, `ComparisonOperator:LessThan`, and `ComparisonOperator:LessThanOrEqual`; the 610-feature catalog and SQL's 352-supported/258-unsupported profile are unchanged, while the exhaustive Memory profile grows from 36 to 40 tokens. `<`, `<=`, `>`, and `>=` are admitted under default null semantics only between one direct non-nullable converter-free model/provider `Int32` root column and one exact non-null `Int32` scalar, in either operand order. Scalar-left forms invert the operator before the row predicate is constructed; row evaluation then uses the corresponding direct C# `int` comparison and never subtraction, so this slice introduces no comparison-arithmetic overflow path. Existing exact direct-`Guid` and resolved Guid-backed typed-ID `==`/`!=` leaves remain admitted, but relational canonical-`Guid` comparisons classify to `QueryPlanComparisonShape.DefaultNullSemantics` and reject before Memory store, binding-conversion, cache, or row work. The new leaves compose inside the bounded M1-B `And`/`Or`/`Not` trees and with repeated `Where`, entity and direct-`Int32` scalar projection, selectorless `Any`/`Count`, the exact primary-key ordering, and final `Take`. Focused evidence passes `6/6` `MemoryOrderedInt32ComparisonTests` and `25/25` `QueryPlanCapabilityValidationTests`; the full targetless Memory suite passes `103/103`. Capability contracts freeze the exact 40-token list, exact relational-`Int32` classification in both operand directions, canonical-`Guid` relational fallback, and the unchanged 610/352/258 catalog/SQL matrix. One same-invocation differential range fixture covers all four relational operators, both operand directions, and late rebinding through Memory and independently raw-seeded SQLite; this is bounded regression pressure, not general provider parity. `DataLinq` and `DataLinq.Memory` build cleanly for `net8.0`, `net9.0`, and `net10.0` with zero warnings and zero errors. Native AOT and full-trim publishes and executables pass with the exact range result and capability count (`range-filtered=[-5,17]`, `capabilities=40`). Isolated WebAssembly no-AOT and AOT publishes reach `passed` in real browser runs with the same exact range result and capability count, the expected `querying-relational-range` stage, and zero warning/error entries. Recursive filename and binary/text scans of the `aot`, `trim`, `wasm-noaot`, and `wasm-aot` output roots find none of `DataLinq.SQLite`, `DataLinq.MySql`, `Microsoft.Data.Sqlite`, `MySqlConnector`, `SQLitePCLRaw`, or `e_sqlite3`. This advances only bounded M1/D6 exact `Int32` relational semantics. At that checkpoint, aggregate M1/M2 and W10 package/compatibility reruns remained open, and the packed README change required a fresh later candidate.

**W10 steps 1-2 / RE-1D package-tooling checkpoint:** Commits `bdae5f5b` and follow-up version fix `39522ce376a2dddb4faa7dcaded80d470889abb2` add `DataLinq.Memory` to the default public pack set and default expected/runtime report sets, reject non-empty pack output, and make inspection fail closed over aligned version/identity/metadata, independently inventoried symbols, exact Memory dependencies/assets and assembly identity, and banned payloads. The first `0.9.0-preview.w10.1` probe exposed that `PackageVersion` did not override MinVer; the follow-up uses `MinVerVersionOverride`. The final fresh `0.9.0-preview.w10.2` candidate at `artifacts/nuget-release/0.9.0-preview.w10.2` contains six exact-version `.nupkg` files and six independently matched `.snupkg` files. Default schema `v0.9.package-inspection-report.v3` evidence at `artifacts/dev/package-report/20260804-075329094` records six packages, six symbol packages, six expected packages, four runtime packages, zero findings, and zero hard failures. Memory has exact net8/net9/net10 DLL/PDB sets, three valid CLI assemblies named `DataLinq.Memory`, exact same-version core-only dependency groups with `Build,Analyzers` excluded, clean metadata/root assets, and no provider, native, Roslyn, Remotion, or generator payload. Focused inspector/size tests pass `17/17` and `9/9`; unit passes `1231/1231`; integrated quick passes `2205/2205` (`60` generators + `1231` unit + `103` memory + `811` compliance); Dev CLI builds with zero warnings/errors; DocFX has zero errors and only two known duplicate `AnalyzerReleases` warnings. No package was published. This completes only W10 steps 1-2 and RE-1D. RE-1C, RE-1E/F/G/H, W10 steps 4-9, aggregate RE-1/RE-4/W10/W11, packaged constrained-runtime evidence, consumer smoke, final release-candidate closeout, and publication remain open. Aggregate M1/M2 remain unchanged at Memory `40`, catalog `610`, and SQL `352` supported / `258` unsupported.

**M1-D exact local Int32 membership checkpoint:** The capability catalog adds exactly two `MembershipShape` values and grows from 610 to 612 features. SQL supports both vocabulary values, so its exhaustive profile grows from 352 to 354 supported while 258 remain unsupported; this describes existing SQL behavior rather than adding a new SQL execution route. Memory grows from 40 to 49 tokens through exactly `Predicate:In`, both predicate polarities, the exact direct-`Int32` membership shape, the membership item and sequence value uses, local-sequence binding, and empty/non-empty-without-nulls sequence shapes. The only admitted item is a direct, non-nullable, converter-free model/provider `Int32` root column against an invocation-local exact `Int32` sequence. Positive and negated `Contains`, equivalent equality-shaped local `Any` in either operand order, empty and non-empty sequences, duplicates, reassigned captures, nested Boolean trees, ordering plus final `Take`, direct-`Int32` projection, `Any`, and `Count` are covered. The shared parser's existing contract normalizes a captured null collection reference to an empty sequence, so positive membership is false and negated membership true; this deliberately differs from LINQ-to-Objects' null-source exception. Execution constructs an invocation-local `HashSet<int>` with cancellation checks before Memory store access. Nullable or null-containing, string, widened, boxed, converter-backed, `Guid`, and typed-ID membership classifies as `MembershipShape:Other`; after shared parser capture, capability validation rejects those shapes before Memory store, cache, conversion, or row work. Focused evidence passes `6/6` `MemoryInt32MembershipTests` and `26/26` `QueryPlanCapabilityValidationTests`; full Unit and targetless Memory suites pass `1232/1232` and `110/110`. The integrated quick gate passes `2213/2213` (`60` generators + `1232` unit + `110` memory + `811` compliance). One same-invocation fixture proves positive, negated, empty, null-reference, rebound, and composed scalar results against independently raw-seeded SQLite; it is bounded regression pressure, not general provider parity. `DataLinq` and `DataLinq.Memory` build for `net8.0`, `net9.0`, and `net10.0` with zero warnings and errors. Fresh isolated Native AOT and full-trim executables and real-browser WebAssembly no-AOT/AOT runs under `artifacts/dev/memory-m1d-membership-20260804` pass with `membership-filtered=[-5,42]`, `capabilities=49`, the `querying-int32-membership` stage, and zero browser warning/error or page-error entries. Recursive path and binary/text scans of all four publish roots find zero SQL-provider or native-database payload hits. This completes only bounded M1-D; aggregate M1/M2 and broader membership remain open. No package was built or published for this checkpoint, so the earlier `0.9.0-preview.w10.2` candidate remains valid historical W10 evidence but does not contain the M1-D README.

**M1-E exact ordered final Skip checkpoint:** The capability catalog adds exactly `PagingCompositionShape:SingleSkipAfterSingleOrdering` and grows from 612 to 613 features. SQL supports that descriptive vocabulary value, so its exhaustive profile grows from 354 to 355 supported while 258 remain unsupported; this describes existing SQL behavior rather than adding a new SQL execution route. Memory grows from 49 to 51 tokens through exactly `Operation:Skip` and the new paging-composition shape, reusing the existing exact primary-key ordering, exact nonnegative `Int32` paging-count, scalar-binding, and paging-value tokens. The admitted shape is one final nonnegative exact `Int32` scalar-binding `Skip` after exactly one direct, non-nullable, converter-free model/provider `Int32` ordering over the table's entire single-column primary key; admitted `Where` predicates may appear before the ordering or between it and final `Skip`. It executes entity and direct-`Int32` scalar sequences, selects only the ordered suffix, and materializes no skipped rows. Bare, unordered, repeated, negative, or non-primary-key `Skip`, `Skip` plus `Take`, `Take` plus `Skip`, post-`Skip` composition, element terminals, and `ThenBy` reject before Memory store, cache, or materialization work. Focused evidence passes `6/6` `MemoryOrderedSkipTests` and `26/26` `QueryPlanCapabilityValidationTests`; one same-invocation SQLite parity fixture proves the bounded ordered suffix. Full Unit and targetless Memory suites pass `1232/1232` and `117/117`. The integrated quick gate passes `2220/2220` (`60` generators + `1232` unit + `117` memory + `811` compliance). `DataLinq` and `DataLinq.Memory` build for `net8.0`, `net9.0`, and `net10.0` with zero warnings and errors. Fresh isolated Native AOT and full-trim executables and real-browser WebAssembly no-AOT/AOT runs under `artifacts/dev/memory-m1e-skip-20260804` pass with `skipped=[17,42]`, `capabilities=51`, the `querying-ordered-skip` stage, and zero browser warning/error or page-error entries. Recursive path and binary/text scans of all four publish roots find zero SQL-provider or native-database payload hits. This completes only bounded M1-E; aggregate M1/M2 and broader ordering/paging remain open. No package was built or published for this checkpoint; the earlier `0.9.0-preview.w10.2` candidate and M1-D's 49/612/354/258 counts remain historical evidence, and no package contains the M1-E README.

**M1-F exact Single/SingleOrDefault checkpoint:** The capability catalog remains at 613 features and SQL remains at 355 supported / 258 unsupported dispositions. Memory grows from 51 to 53 tokens through exactly `Result:Single` and `Result:SingleOrDefault`. The admitted result family is the existing one-root, unpaged Memory island over a root entity or exact direct non-nullable converter-free `Int32` scalar projection; the existing admitted predicates, Boolean composition, local `Int32` membership, and exact primary-key `Int32` ordering compose, while predicate terminal overloads normalize through `Where`. `Single` returns the sole canonical match and throws the standard `InvalidOperationException` for empty or multiple results. `SingleOrDefault` returns the sole match, `null` for an empty entity result, or `0` for an empty scalar result, and throws the standard `InvalidOperationException` for multiple results. Execution establishes canonical-row cardinality before entity materialization or scalar conversion, so empty and multiple results perform zero partial cache or materialization work; an unordered multiplicity probe stops at the second matching row, while ordered execution retains the full buffer/sort boundary. A cold successful entity result materializes once; a warm result reuses the cached identity, while a scalar result performs no entity or cache work. Invocation rebinding and pre-cancellation retain their existing contracts. `First`, `FirstOrDefault`, `Last`, `LastOrDefault`, `Single` or `SingleOrDefault` after `Take` or `Skip`, string projection, non-primary-key ordering, and all previously unsupported shapes remain rejected; terminal-after-paging rejection is classified as `Operation:Pushdown`. Focused evidence passes `6/6` `MemorySingleResultTests` and `26/26` `QueryPlanCapabilityValidationTests`; full Unit and targetless Memory suites pass `1232/1232` and `124/124`. The integrated quick gate passes `2227/2227` (`60` generators + `1232` unit + `124` memory + `811` compliance). One same-invocation SQLite parity fixture passes `1/1` across entity/scalar success, default, empty, and multiplicity semantics. `DataLinq` and `DataLinq.Memory` build for `net8.0`, `net9.0`, and `net10.0` with zero warnings and errors. Fresh isolated Native AOT and full-trim executables and real-browser WebAssembly no-AOT/AOT runs under `artifacts/dev/memory-m1f-single-20260804` reach `status=passed` and `stage=completed` with `single-entity=17, single-entity-default-null=True, single-scalar=3, single-scalar-default=0, single-multiple-before-materialization=True`, `capabilities=53`, the `querying-single-results` stage, and zero browser warning/error entries or error state. Recursive path and binary/text scans of all four publish roots report `PathHits=0` and `ContentHits=0` for SQL-provider or native-database payloads. This completes only bounded M1-F; aggregate M1/M2 and broader element terminals remain open. No package was built or published for this checkpoint; the historical `0.9.0-preview.w10.2` candidate does not contain the M1-D, M1-E, or M1-F README.

## Baseline Evidence Manifest

### Commands and results

| Evidence | Command | Result |
| --- | --- | --- |
| Local clean before-state | `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI --no-build -- run --suite all --alias quick --output failures` | 39/39 generators, 740/740 unit, 626/626 SQLite compliance |
| Initial complete provider matrix | `$env:DATALINQ_TEST_DB_HOST='127.0.0.1'; dotnet run --project src\DataLinq.Testing.CLI --no-build -- run --suite all --alias all --batch-size 4 --output failures --summary-json artifacts\release\v0.9\w0-8bcfc770246f\tests\all.json` | 2,889/2,889 passed; zero failed/skipped |
| Final integrated provider matrix | `$env:DATALINQ_TEST_DB_HOST='127.0.0.1'; dotnet run --project src\DataLinq.Testing.CLI -- run --suite all --alias all --batch-size 4 --build --output failures --summary-json artifacts\release\v0.9\w0-8bcfc770246f\tests\all-integrated.json` | 2,910/2,910 passed; zero failed/skipped |
| CI-profile solution build | `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- build src\DataLinq.sln --profile ci --output errors` | Passed in 79.5 s with two MSBuild `WASM0001` warnings and zero errors; the CLI summarized 13 warning lines |
| Pack only | `.\publish-nuget.ps1 -PackOnly -PackageOutputPath artifacts\nuget-release\w0-8bcfc770246f` | Five packages and five symbol packages produced; push skipped |
| Package inspection | `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- package-report --package-dir artifacts\nuget-release\w0-8bcfc770246f --format markdown` | Passed for the current five-package graph |
| Compatibility, sandbox | `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- size-report --target phase8c --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown` | Native AOT/trim green; WebAssembly failed |
| Compatibility, outside sandbox | `dotnet run --project src\DataLinq.Dev.CLI -- size-report --target phase8c --clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload --format markdown` | Same result; failure is real, not sandbox-only |
| Query hot path | `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile heavy --history-json artifacts\benchmarks\history\v0.9-before-foundation-query-hotpath.json` | Completed with six measurements and multimodal/noise warning |
| Provider watch | `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile heavy --history-json artifacts\benchmarks\history\v0.9-before-foundation-provider-watch.json` | Completed with six measurements and multimodal/noise warning |

The complete provider run required raw `dotnet` outside the sandbox after the sandbox could not execute Podman and the sandbox wrapper's rewritten `LOCALAPPDATA` prevented Podman's CLI fallback from finding its SSH connection. Direct inspection proved all four containers were healthy before the external run.

Both benchmark commands ran with `DATALINQ_BENCHMARK_PROVIDERS=sqlite-file,sqlite-memory`.

### Concrete artifact locations

| Artifact | Path |
| --- | --- |
| Initial complete test summary | `artifacts/release/v0.9/w0-8bcfc770246f/tests/all.json` |
| Final integrated test summary | `artifacts/release/v0.9/w0-8bcfc770246f/tests/all-integrated.json` |
| Raw test logs | `artifacts/testdata/cli-logs/` |
| CI-profile build log/binlog | `artifacts/dev/build-20260710-131225479.log` and `artifacts/dev/build-20260710-131105497.binlog` |
| Packed packages | `artifacts/nuget-release/w0-8bcfc770246f/` |
| Package inspection | `artifacts/dev/package-report/20260710-131444310/report.json` and `artifacts/dev/package-report/20260710-131444310/report.md` |
| Outside-sandbox compatibility report | `artifacts/dev/compat-size-report/20260710-131804528/report.json` and `artifacts/dev/compat-size-report/20260710-131804528/report.md` |
| WebAssembly no-AOT failure log | `artifacts/dev/compat-size-report-wasm-publish-20260710-131949407.log` |
| WebAssembly AOT failure log | `artifacts/dev/compat-size-report-wasm-aot-publish-20260710-132025392.log` |
| Query benchmark history | `artifacts/benchmarks/history/v0.9-before-foundation-query-hotpath.json` |
| Query benchmark summary | `artifacts/benchmarks/results/20260710-132122767-32125f4be13a4bb09349d7c12d36b66b-summary.json` |
| Provider benchmark history | `artifacts/benchmarks/history/v0.9-before-foundation-provider-watch.json` |
| Provider benchmark summary | `artifacts/benchmarks/results/20260710-132429545-ebfda16e6fd94a1ea41848f6c538cb4f-summary.json` |

### Provider pass counts

| Suite/batch | Targets | Passed |
| --- | --- | ---: |
| Generators | once | 39 |
| Unit | once | 761 |
| Compliance batch 1 | SQLite file, SQLite memory, MySQL 8.4, MariaDB 10.11 | 1,203 |
| Compliance batch 2 | MariaDB 11.4, MariaDB 11.8 | 639 |
| MySQL-specific | all four server targets | 268 |
| **Total** | complete active matrix | **2,910** |

The final integrated matrix rebuilt all four test projects, includes the three reader-lifetime tests, eighteen value/UUID cases, and independent MySQL UUID-default vector assertions, and supersedes the initial 2,889-test artifact.

### Package graph

Pack-only produced version `0.8.1-alpha.0.9` for:

- `DataLinq`
- `DataLinq.SQLite`
- `DataLinq.MySql`
- `DataLinq.CLI`
- `DataLinq.Tools`

Core/provider libraries contain `net8.0`, `net9.0`, and `net10.0` assets. The package report found the expected provider dependencies and no unexpected runtime folders. This is a before-state inspection, not a 0.9 package promotion.

### Historical W0 compatibility baseline

| Target | Publish | Smoke | Symbol-excluded size | Banned payload |
| --- | --- | --- | ---: | ---: |
| Native AOT | Passed | Passed | 9.29 MB | 0 |
| Trimmed | Passed | Passed | 22.79 MB | 0 |
| WebAssembly no-AOT | Failed | Skipped | n/a | 0 observed |
| WebAssembly AOT | Failed | Skipped | n/a | 0 observed |

Both WebAssembly logs end with:

```text
error MSB4057: The target "ResolveWasmOutputs" does not exist in the project.
```

At W0 the failing project was `DataLinq.BlazorWasm.csproj::TargetFramework=net10.0`, reached through `Microsoft.NET.Sdk.BlazorWebAssembly.6_0.targets`. W10 still owns the accepted release-catalog disposition and final rerun of the existing SQLite graph. The separate W8 memory browser graph is now green, but it does not retroactively turn this historical SQLite baseline green; conversely, a green SQLite AOT executable is not memory-backend evidence.

### Performance baseline and D7 policy

Query-hotpath allocations:

| Scenario | SQLite file | SQLite memory |
| --- | ---: | ---: |
| Repeated scalar `Any` | 13.15 KB/op | 13.43 KB/op |
| Repeated non-PK equality fetch | 17.70 KB/op | 18.22 KB/op |
| Repeated `IN` predicate fetch | 23.35 KB/op | 24.08 KB/op |

Provider/watch allocations:

| Scenario | SQLite file | SQLite memory |
| --- | ---: | ---: |
| Warm PK | 1.77 KB/op | 1.77 KB/op |
| Startup PK | 66.84 KB/op | 67.81 KB/op |
| Provider initialization | 345.30 KB/op | 345.86 KB/op |

Timing noise ranges from 6.9% to 28.4%, with a multimodal-distribution warning. D7 therefore uses these rules:

- exact telemetry deltas must not regress
- an allocation increase must exceed both 10% and 1 KB/op in two repeat runs before it is treated as material
- a latency increase must exceed 15%, exceed the combined reported error/noise, and reproduce in two independent runs before it blocks the adapter
- isolated template/invocation measurements must be added before any plan-cache or invocation-allocation claim

## Release Harness Inventory

### Missing harness capabilities

- `api-report` is plan syntax, not an implemented command; RE-1F owns the API baseline.
- `package-smoke` is also a placeholder; RE-1E owns a real consumer smoke.
- test summary JSON omits schema identity, commit, command, and timestamps; the tracked manifest supplies that context for W0.
- the compatibility report still carries the historical `phase8c.compatibility-size-report.v1` identity; the package report now uses `v0.9.package-inspection-report.v3`.
- no isolated template/invocation benchmark exists; RE-1G owns it.
- the historical SQLite-shaped WebAssembly baseline was red under SDK 10.0.301; W10 still needs an accepted disposition and final rerun, while the separate W8 memory browser graph now provides credible bounded memory-specific evidence.

### Remaining release-harness assumptions after W10 steps 1-3 / RE-1A / RE-1D

D5-B deliberately changed only Memory's package metadata and locally inspected an explicit candidate. W10 step 3 later resolved the Testing CLI registration gap. W10 steps 1-2 / RE-1D now resolve the default pack and package-report gaps on the fresh `0.9.0-preview.w10.2` candidate without completing compatibility, package-consumer, packaged constrained-runtime, or release-candidate closeout. The following release-harness assumptions remain:

- compatibility accepts only `phase8c` and still registers only the historical SQLite-shaped target graph; the new memory hosts are not yet release-catalog targets.
- the historical shared smoke references `DataLinq.SQLite`; the separate provider-free memory shared runner and Native AOT/full-trim/browser hosts now coexist outside that catalog.
- compatibility identity models platform kind, not backend by platform.
- current release thresholds explicitly describe 0.8.
- the solution contains a packable Memory runtime plus non-packable test, shared-runner, Native AOT, full-trim, and browser projects; Memory is now a release-script/report target but is not yet a compatibility-catalog or packaged constrained-runtime target.
- no clean package-consumer smoke proves the six-package graph without project references.
- final RC evidence and publication remain deliberately separate from the completed preview tooling probe.
- benchmarks currently support only SQLite file and SQLite memory modes.

## Decision Register

| Decision | First-slice disposition | Latest consuming wave |
| --- | --- | --- |
| D0 current-behavior baseline | Resolved by this inventory, tests, and evidence manifest | W2 |
| D1 projection disposition | Resolved by the table above | W2/F2 |
| D2 scalar conversion contract | Accepted release decision; SC-1 implementation gate remains | W2 |
| D3 UUID public/metadata shape | Accepted release decision; known-vector evidence is tracked in the W1 value lane | W2/W7 |
| D4 transaction/cache contract | Current pending/committed overlay characterized; provenance/failure gaps assigned | W3 |
| D5 memory project/package boundary | Resolved by D5-A public-surface review plus D5-B aligned candidate dependency/archive inspection and explicit experimental-package promotion | W9 |
| D6 memory semantic matrix | Deferred until the spike establishes an honest executable subset | W9 |
| D7 performance policy | Resolved above; noisy single-run timing is not a release claim | W5/W10 |

## First-Slice Exit

- every production expression-query route and known bypass has a later owner
- projection disposition is explicit for all eight current kinds
- query shape, binding isolation, PK/cache/relation behavior, reader lifetime, and transaction overlay behavior have focused regression evidence
- deliberately missing 0.9 safeguards are named against W3/W5/RE work instead of being asserted as current behavior
- provider, package, constrained-runtime, and performance before-state artifacts are reproducible
- no production architecture or shipped support claim changed

The W1 follow-up gate is closed by the WAL and provider-lifecycle suites plus the [Mutation Lifecycle Expected-Failure And Ownership Matrix](Mutation%20Lifecycle%20Expected-Failure%20and%20Ownership%20Matrix.md). That matrix makes no red or amber runtime behavior green; W3 owns those changes. W2 may now begin, but it must not add a backend name above the current SQL-shaped runtime.
