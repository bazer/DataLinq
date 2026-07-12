> [!WARNING]
> This document is roadmap execution material for the DataLinq 0.9 development line. It records a before-state and does not describe shipped 0.9 behavior.

# 0.9 Baseline And Release Harness Inventory

**Status:** W0-W2 complete. W3 and W4 implementation are active against this 2026-07-10 characterization baseline; bounded managed-wrapper `TX-3` rollback/open-disposal finalization is recorded below.

**Baseline branch:** `v0.9`.

**Baseline source commit:** `8bcfc770246f960e27a91e3046f19a76c3736217`.

**Starting worktree:** Clean, on `v0.9`, before characterization-only test and documentation changes.

**Authority:** The release ordering and ownership rules remain in [Implementation Order And Integration Plan](Implementation%20Order%20and%20Integration%20Plan.md). This record freezes the observable before-state used by W2 and later work.

## Outcome

The first 0.9 slice changed no production runtime, public API, package, provider, or support claim.

It did four things:

1. catalogued every expression-query entry, SQL-builder construction, original-expression dependency, primary-key shortcut, cache-cold loader, and relation loader that later waves will move
2. resolved projection disposition D1 for every current `QueryPlanProjectionKind`
3. added focused query, parsed-plan binding, primary-key, reader-lifetime, transaction-cache, relation, mutable-reuse, provider-lifecycle fault, file-backed SQLite/WAL, scalar-value, typed-ID-fixture, canonical-key, and UUID-vector characterization
4. captured reproducible build, provider, package, compatibility, and benchmark evidence, including real baseline failures instead of laundering them into green claims

The production baseline is healthy across the complete SQL provider matrix. Native AOT and trimming are green. Both WebAssembly publish lanes are currently red under SDK 10.0.301 because the Blazor SDK requests a missing `ResolveWasmOutputs` target. That failure reproduced outside the sandbox and is therefore a real release-harness gap, not sandbox noise.

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
| Owned-path committed policy, private-cache WAL committed visibility, explicit shared-cache locking, and bounded writer contention | `SQLiteWalConcurrencyCharacterizationTests` |

The following accepted behaviors were intentionally assigned to W3 rather than encoded as green W1 behavior:

- provider and transaction provenance
- cross-provider and cross-transaction mutable rejection
- reuse rejection after rollback, disposal, deletion, or uncertain failure
- primary-key mutation rejection before command creation
- read-only mutation rejection before SQL
- failed-statement poisoning and commit rejection

Subsequent W3 slices have closed the listed provenance, cross-owner, primary-key, read-only, mutation-poisoning, and bounded managed-wrapper rollback/open-disposal deficits. Bounded `TX-3` replaces the rollback/disposal defect expectations with provider-first completion attempts, accurate `RolledBack`/`RollbackOutcomeUnknown`/`OpenTransactionDisposed` ownership, touched invalidation and registry clearing, exact transaction row/subscription discard, committed-cache preservation, deferred finalized wrapper status, and an only-dispose gate after a failed rollback that remains open. The adjacent managed recovery now records permanent `CommitOutcomeUnknown` when the provider `Commit()` call throws, preserves the exact provider exception, invalidates/clears touched and transaction-local state, structurally evicts provider-wide committed rows and indices before recovery notifications, attaches recovery failures as secondary context, rejects further managed use, and permits only status-compatible rollback or disposal. `TX-5A` proves active attached wrapper-only commit promotion/reuse and rollback invalidation across every provider, and prevents wrapper commit after external commit/rollback from manufacturing success. Bounded `TX-5B` detects an inactive original handle before managed read/write/fallback/dispose, records `ExternalCompletionUnknown`, extends provider-wide recovery to externally completed wrapper rollback/disposal, and proves fresh rematerialization of the actual external outcome across every active provider. Determining the outcome of a throwing provider commit, preventing raw low-level escape, arbitrary local-cache primitive fault injection, full provider commit-fault evidence, and full concurrency remain open; the W1 statements above remain historical baseline rather than present-tense runtime claims.

The temporary file-backed WAL lane proves `SQ-1`: every DataLinq-owned path resets `read_uncommitted=0`; private-cache readers retain committed insert/update/delete state during a pending write; an explicit shared-cache reader locks instead of receiving pending data; attached transactions preserve caller policy; and a competing writer surfaces SQLite `BUSY` within the configured timeout. `SQ-2` is also green: CLI and test-harness file defaults omit `Cache`, generated paths open successfully, named memory retains shared cache, and explicit caller settings are not rewritten.

### Value, key, and UUID baseline

The W1-V tests approve independent values and current seams without pretending the future converter or codec already exists.

| Invariant | Evidence |
| --- | --- |
| Primitive `int`, `long`, `Guid`, and `string` metadata is model/provider identity with no converter handle | `PrimitiveKeyMetadata_UsesIdentityProviderRepresentation` |
| Typed-ID record-struct fixtures have value equality and equality-consistent hashing | `TypedIdFixtures_HaveValueEqualityAndHashSemantics` |
| Canonical provider keys preserve CLR type, value equality, hash, and composite boundaries | `CanonicalProviderKeys_*` |
| Canonical `Guid` is distinct from text and byte-array physical representations | `CanonicalGuidKey_IsDistinctFromPhysicalRepresentations` |
| Native `Guid`, text36, text32, legacy little-endian binary, and RFC-order binary use fixed known vectors | `GuidKnownVector_*` |
| Current MariaDB text and MySQL binary default generation matches independent vectors | `MetadataFromSqlFactoryDefaultParsingTests` |

The approved vector is `00112233-4455-6677-8899-aabbccddeeff`. Its current DataLinq/MySQL compatibility bytes are `33221100554477668899AABBCCDDEEFF`; its RFC-order bytes are `00112233445566778899AABBCCDDEEFF`.

Format-aware schema validation and diffing cannot be characterized honestly before `GuidStorageDefinition` exists. Typed-ID conversion remains SC-1 and later work. Provider-native MariaDB fixtures, raw legacy binary rows, and column-aware provider-codec evidence remain UUID-1 through UUID-5 work.

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

### Compatibility baseline

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

The failing project is `DataLinq.BlazorWasm.csproj::TargetFramework=net10.0`, reached through `Microsoft.NET.Sdk.BlazorWebAssembly.6_0.targets`. RE-1D owns repairing or replacing this harness before W10/W11. A green SQLite AOT executable is not memory-backend evidence.

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
- `package-smoke` is also a placeholder; RE-1C owns a real consumer smoke.
- test summary JSON omits schema identity, commit, command, and timestamps; the tracked manifest supplies that context for W0.
- compatibility and package report schemas still carry `phase8c.*.v1` identities.
- no isolated template/invocation benchmark exists; RE-1G owns it.
- the WebAssembly publish graph is red under SDK 10.0.301 and must be repaired before memory-specific browser evidence can be credible.

### Assumptions that later must learn about `DataLinq.Memory`

Do not change these before the W8 spike promotion gate:

- `publish-nuget.ps1` hardcodes the current five public packages.
- `PackageReportCommand` hardcodes five public and three runtime packages.
- package inspection has no memory-specific ban on SQLite, MySQL, SQLitePCLRaw, or native payload.
- Testing CLI has no `memory` suite and currently knows only generators, unit, compliance, and MySQL.
- compatibility accepts only `phase8c` and points all targets at the SQLite-shaped smoke dependency graph.
- the shared smoke references `DataLinq.SQLite`; AOT and trim root SQLite explicitly.
- compatibility identity models platform kind, not backend by platform.
- current release thresholds explicitly describe 0.8.
- the solution has no memory product, test, or smoke projects.
- benchmarks currently support only SQLite file and SQLite memory modes.

## Decision Register

| Decision | First-slice disposition | Latest consuming wave |
| --- | --- | --- |
| D0 current-behavior baseline | Resolved by this inventory, tests, and evidence manifest | W2 |
| D1 projection disposition | Resolved by the table above | W2/F2 |
| D2 scalar conversion contract | Accepted release decision; SC-1 implementation gate remains | W2 |
| D3 UUID public/metadata shape | Accepted release decision; known-vector evidence is tracked in the W1 value lane | W2/W7 |
| D4 transaction/cache contract | Current pending/committed overlay characterized; provenance/failure gaps assigned | W3 |
| D5 memory project/package boundary | Fixed: separate non-packable projects until promotion | W8 |
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
