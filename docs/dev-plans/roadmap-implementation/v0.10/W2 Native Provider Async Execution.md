> [!WARNING]
> W2 is in progress. Internal native-provider implementation is not public async support, W0-F1 closure or release approval.

# W2 Native Provider Async Execution

**Started:** 2026-09-24, from `v0.10` commit `5d5def9a` after [W1 closeout](W1%20Closeout.md).

**Workflow:** one branch, `codex/0.10-w2`, and [draft PR #229, Implement W2: native provider async execution](https://github.com/bazer/DataLinq/pull/229), targeting `v0.10`. The [accepted workflow exception](Branch%20PR%20and%20Benchmark%20Workflow.md#w2-single-pr-exception) preserves coherent commits, incremental reviews and final merge-commit integration. No merge or publication is authorized by opening the PR.

## Scope And Acceptance

Bind W1's internal capabilities to the native providers, retain direct synchronous execution, and prove actual operation outcomes and owned-resource cleanup. MySQL and MariaDB share the implementation but require evidence against both server families. SQLite file and in-memory paths require explicit blocking, locking, cancellation and setup evidence; awaitable signatures do not establish interruptible native calls.

The [W1 I/O handoff](W1%20Closeout.md#f21-requirement-and-io-reconciliation), [W0 I/O map](W0%20IO%20Execution%20Map.md), [AAPI decisions](Async%20Public%20API%20Decisions.md) and [W2 exit gate](Implementation%20Order%20and%20Integration%20Plan.md#w2-native-provider-async-execution) remain authoritative. This plan breaks down that scope without reopening accepted policies.

The [native provider audit](W2%20Native%20Provider%20Audit.md) reconciles each operation and failure boundary against the implementation, identifies the driver limits and keeps the remaining acceptance gates explicit.

The [clean functional and performance checkpoint](W2%20Functional%20And%20Performance%20Checkpoint.md) records candidate `49176d7a`, passing individual-provider verification, all 90 canonical benchmark rows, bounded timing controls and the remaining performance questions.

## Milestones And Evidence Matrix

Each cell progresses separately through implementation and verification. A passing test with a controllable adapter cannot be recorded as native-provider proof.

| Milestone / required behavior | MySQL | MariaDB | SQLite file / memory |
| --- | --- | --- | --- |
| W2.1 Native standalone open, scalar/non-query dispatch, reader advancement and owned cleanup; validation/pre-cancellation; borrowed command ownership | Implemented internally; bounded native tests on 8.4/9.7 below | Implemented internally; bounded native tests on 10.11/11.4/11.8/12.3 below | Implemented internally; file/memory checks below, corrected-package acceptance pending |
| W2.2 Lazy first-use transaction initialization, sync/async admission, private publication, partial-initialization cleanup and attachment | Implemented; native initialization checks below | Implemented; native initialization checks below | Bound; native file/memory checks preserve deferred Serializable begin; final acceptance pending |
| W2.3 Captured query/key/relation/fluent integration, actual row conversion, complete/invalidation-safe cache publication and early reader cleanup | Native value/capture/cache checks and clean candidate parity recorded; performance disposition pending | Native value/capture/cache checks and clean candidate parity recorded; performance disposition pending | Native value/capture/cache checks and clean candidate parity recorded; performance and corrected-package acceptance pending |
| W2.4 Tracked mutations/private hydration, commit/rollback certainty, independent recovery budget, disposal and mixed execution | Native values, finite batches, completion loss, cooperative recovery and cleanup verified; clean candidate recorded, performance disposition pending | Native values, finite batches, completion loss, cooperative recovery and cleanup verified; clean candidate recorded, performance disposition pending | Native commit rejection and cleanup-error preservation verified; failed-rollback file-pool reuse, performance and corrected-package acceptance remain open |
| W2.5 Metadata parsers, existence/availability, per-command timeout, provisioning, journal mode, keeper and owning-root lifetimes | Bound; native metadata parity, timeout, cancellation and identity checks below | Bound; native metadata parity, timeout, cancellation and identity checks below | Bound; native metadata, administration, timeout and identity checks below; corrected-package and final acceptance pending |
| W2.6 Native interruption, soft/hard cancellation, timeout/connection trust and no-dispatch/cleanup classification; final parity and performance review | Native interruption, completion loss, recovery budget and cleanup verified; clean matrix recorded, performance disposition pending | Native interruption, completion loss, recovery budget and cleanup verified; clean matrix recorded, performance disposition pending | Between-row cancellation, blocking limits and native cleanup verified; failed-rollback pool integrity, official fix adoption and performance disposition pending |

The first slice starts on the shared MySQL/MariaDB standalone access path so native work can proceed while the SQLite servicing package is pending. It must not imply transaction support before W2.2/W2.4, or public API availability before W3. Deterministic W1 tests remain necessary for failure combinations that live drivers cannot reliably reproduce.

## Provider Rules

- Validate lifecycle and concrete native capability before pre-cancellation and I/O. Reject unsupported commands/sources explicitly. Do not use `Task.Run`, sync-over-async or inherited synchronous fallbacks as native async proof.
- Separate connection/open/setup failure from command dispatch. Preserve original primary errors and ordered cleanup failures, including failed reader handoff and observer failure.
- Keep command ownership explicit. Owned commands/readers/connections receive independent cleanup; borrowed commands remain usable by their caller after the operation's owned resources settle.
- A canceled ordinary read is reusable only after cleanup and verified provider/transaction integrity. Interrupted writes retain W1 poisoning regardless of socket survival. Confirmed commit remains committed after later failures; lost confirmation remains unknown.
- Preserve cancellation, command timeout, MySqlConnector cancellation escalation and recovery rollback timeout as separate settings. The recovery budget is cooperative and never permission to abandon active work into a pool.
- Preserve synchronous SQLite constructor keeper/WAL setup and MariaDB version probing. No new async construction promise, automatic migration, retry or destructive provisioning recovery is introduced.

## SQLite Dependency Gate

The [upstream fix is merged and issue #39008 targets 10.0.13](SQLite%20Pool%20Ownership%20Investigation.md). The user explicitly authorizes W2 development and native tests while waiting. DataLinq still pins 10.0.11; this is not corrected-package evidence. Adopt the corrected official package through `master`, merge forward, and rerun the affected ownership/concurrency/provider evidence before closing W0-F1 or SQLite acceptance. Public API freeze and release approval remain gated.

The later [failed-rollback pool reuse investigation](SQLite%20Failed%20Rollback%20Pool%20Reuse%20Investigation.md) identifies a separate native cleanup issue on the pinned driver. Its file-pool integrity disposition remains open; the ownership-order fix is not proof that this distinct failure path is corrected.

## Verification And Review

- Record focused TUnit commands, actual provider targets, results and source identities at each milestone. Use the Testing CLI and preserve runtime-state discovery across targeted server runs.
- Review each coherent slice before considering its milestone verified. Run broader provider regression tests at integration checkpoints; retain failures and classify infrastructure limitations explicitly.
- Complete the per-operation matrix, representative sync/async parity, pre-dispatch/in-flight/cleanup cancellation, diagnostics and existing cache/lifecycle assertions before W2 closeout.
- Preserve the frozen W0 baseline and W1 cost dispositions. Final candidate performance and telemetry evidence must identify the actual source commit; intermediate runs do not replace final acceptance.
- Public/generated signatures and packed consumers remain W3; hosting remains W4; startup comparison/policy remains W5. No scope expansion, package publication or merge is implied.

## Execution Record

- **2026-09-24, kickoff:** recorded the accepted single-PR workflow, updated upstream SQLite status and mapped W1's provider handoff. Native implementation and verification begin with W2.1. No W2 milestone is closed by this planning commit.

### W2.1 First MySQL/MariaDB Native Slice

[SqlDbAccess.Async](../../../../src/DataLinq.MySql/Shared/SqlDbAccess.Async.cs) binds W1's internal eager-command, captured-reader and borrowed-reader capabilities to MySqlConnector 2.6.2. Opening, scalar/non-query execution, reader acquisition/advancement and disposal use the concrete driver's async APIs. [SqlAsyncDataLinqDataReader](../../../../src/DataLinq.MySql/Shared/SqlAsyncDataLinqDataReader.cs) retains the existing scalar/UUID/binary decoding and owns its standalone connection explicitly. Synchronous execution remains direct; there are no new public async entry points.

Owned command cleanup composes with W1's existing coordinators. Borrowed commands stay caller-owned. Reader acquisition transfers connection ownership before completion observers run, so an observer failure cleans up an unreturned reader and its connection. Independent cleanup retains the primary exception and diagnostic context. Concrete command/parameter capability, nonempty SQL and attached-transaction rejection occur before cancellation and opening. Error evidence distinguishes initialization, actual dispatch, notification and cleanup without claiming standalone transaction recovery.

The [16 new TUnit methods](../../../../src/DataLinq.Tests.MySql/NativeAsyncCommandTests.cs) include two local validation cases and 14 cases parameterized across all six servers. They cover sync/async result parity, unchanged generic scalar casting, copied binary inputs/owned buffers, repeat enumeration, early sync/async disposal, canceled row advancement, failed authentication, provider identity, provider errors, logger and post-acquisition observer failure, native command timeout, live command cancellation and preservation of rejected caller-owned transactions. Their intended one-connection pool reuse and pool-wait coverage was invalid at this checkpoint; the correction below supersedes those claims. The live cancellation test observes its exact statement in the server process list while another connection holds a row lock before canceling it; it does not assume a timer proves dispatch.

Final working-source verification:

- **Release / .NET 10 provider-specific suite: 791/791 passed, zero failures/skips**, across MySQL 8.4/9.7 and MariaDB 10.11/11.4/11.8/12.3. The three batches pass 261, 265 and 265 cases. Independent TRX inspection finds all 90 new-case executions (30 per two-server batch, including the repeated local cases).
- Core/MySQL provider **Release builds pass on .NET 8/9/10 with zero warnings/errors**. The test project also builds cleanly on .NET 10.
- Relative file links in the five planning documents resolve; `git diff --check` passes. The Testing CLI state still lists all eight SQLite/server targets after targeted server execution.

The final local run is `20260923T222904705Z-b71c96f14a0c4117a611fa5411995f84`, recorded after midnight on 2026-09-24 in Europe/Madrid. It ran working changes based on kickoff `4f8550b0`, with unchanged checkout status during execution. The reused Testing CLI runner identifies `3bc97df2`; its provenance mismatch and dirty-source flags are retained. **ValidForEvidence remains false.** These are bounded developer checks, not a clean W2 candidate or full release-matrix receipt.

| Local summary under `artifacts/` | Result / scope | SHA-256 |
| --- | --- | --- |
| `w2-native-commands-focused.json` | Initial Debug probe: 13 passed / 8 failed; incorrect literal-cast/SLEEP test assumptions | `0a87dd4748a2b4eb1a19211b2b15bb9f2193e8f5538e3ecbd4c14fd05ca566c7` |
| `w2-native-commands-focused-release.json` | Release: 23 passed / 2 failed; SLEEP interruption assumptions | `ee84a00d9eb63e5d174d02a0499811fb8f93f5d4bd09e586ebc3927cac76440d` |
| `w2-native-commands-focused-release-locks.json` | Release: 25/25 passed after using observed lock contention | `b4f19ea28e4f1c9a876fc078ee9ceda3d211af3bdccb4a9f289b78e3544d5dbb` |
| `w2-native-commands-mysql-all.json` | Release: 788/788 passed before final foreign-parameter validation | `65ce508e758333a1909cbcef2a3d6bbfa3987f207006958b0c38b47d6a9e8de6` |
| `w2-native-commands-mysql-all-final.json` | Release: 791/791 passed with final parameter validation | `ecff84ad54911150dd441517e61f44a867a7ad58b97f9637c6f7873bfee39b99` |

The SLEEP experiments returned successful results on MySQL 9.7 where the initial test expected an exception; adding a second projection did not establish interruption failure either. Tests now use row-lock contention for an actual failing/canceled command. The implementation retains confirmed successful results rather than translating a late cancellation into failure. Literal scalar tests now preserve actual driver return types: DataLinq's existing generic cast is not silently widened to numeric conversion. An intermediate CLI build also failed with missing netstandard references after restore assets pointed outside the workspace cache; explicit sandboxed restore and builds succeeded. These failed probes/builds are retained, not counted as passes.

Reproduction after explicitly building the test project in Release:

```powershell
$env:DATALINQ_TEST_DB_HOST = '127.0.0.1'
.\scripts\dotnet-sandbox.ps1 build src/DataLinq.Tests.MySql/DataLinq.Tests.MySql.csproj -c Release -v minimal
.\scripts\dotnet-sandbox.ps1 exec src/DataLinq.Testing.CLI/bin/Debug/net10.0/DataLinq.Testing.CLI.dll run --suite mysql --alias all --configuration Release --no-build --output failures --summary-json artifacts/w2-native-commands-new-checkpoint.json
.\scripts\dotnet-sandbox.ps1 build src/DataLinq.MySql/DataLinq.MySql.csproj -c Release -v minimal
```

The direct CLI DLL invocation preserves the test configuration explicitly; the earlier `dotnet run` invocation's summary actually reported Debug. Rebuild the runner for a new clean-candidate receipt; do not relabel these working-source runs or overwrite retained evidence when collecting another checkpoint.

**Remaining at the standalone checkpoint:** SQLite native binding/adoption, managed transaction first-use and completion, full query/relation/mutation integration, administrative operations, hard-cancellation/connection-trust and combined-cleanup fault evidence, performance and final W2 acceptance. Transaction initialization follows below. Passing the standalone slice does not establish transaction reuse after interruption or complete W2.1 for SQLite.

### W2.2 MySQL/MariaDB Native Transaction Initialization

The [native resource bundle](../../../../src/DataLinq.MySql/Shared/SqlDatabaseTransaction.Resource.cs) binds W1's lazy initialization to direct synchronous and native asynchronous opening, ReadCommitted begin and quoted database selection. Native resources stay private until setup and startup reporting succeed. A failed open/setup/observer path is terminal and settles independent transaction/connection cleanup. Pre-cancellation leaves an unused wrapper reusable; cancellation after opening starts does not permit a new initialization attempt.

[Command dispatch](../../../../src/DataLinq.MySql/Shared/SqlDatabaseTransaction.Commands.cs) shares the managed operation owner across initialization and execution. Public synchronous raw commands and internal owned query/mutation dispatch use distinct admission paths, so a callback cannot borrow the active private owner. Async readers own their reader and any created command, while the transaction retains its connection. Attached native transactions are adopted without another begin, retain their isolation level and preserve consuming ownership. A ready resource can serve either sync or async execution regardless of the first-use mode.

Native commit/rollback and independent disposal now bind the existing managed completion coordinator. Successful native completion returns before managed notifications/finalization. Empty completion/disposal does not open a connection. These bindings establish the basic completion path, **not** the full W2.4 uncertain-outcome/recovery matrix. SQL NULL keeps the existing transaction scalar conversion, which differs from standalone raw scalar behavior.

[Ten TUnit methods](../../../../src/DataLinq.Tests.MySql/NativeAsyncTransactionTests.cs) cover nine server-parameterized scenarios plus targetless unused completion. The final focused run passes **57/57** across all six servers, including repeated targetless cases. It checks mixed first use/completion, validation before cancellation, reader admission through cleanup, connection retention, scalar-null parity, failed USE cleanup, observer reentrancy/failure and attached transaction rollback/ownership. The intended one-connection pool-wait proof was invalid at this checkpoint; see the correction below. The preceding full provider-specific run passes **848/848** before the final observer-attribution refinement. The final-source compliance run passes **3,728/3,728** across SQLite file/memory and all six servers. Release core/provider builds pass on .NET 8/9/10 without warnings/errors.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-native-transactions-initial.json` | First focused latest-server probe, 19/19 | `c6c9578d0ca36fa11bf9747f446a1244664285c7f5fb405e3f3710b1db71fb9b` |
| `w2-native-transactions-mysql-all-initial.json` | Provider regression before final refinement, 848/848 | `f34394da932a5f884f2fcad6178e0ee860b90e411a716e8c6914bc329fdf048d` |
| `w2-native-transactions-final-focused.json` | Final native initialization cases, 57/57 | `e76893d35c11968dbd1154b56446ee95ea50d305f654f7e849b1fa8223b9190f` |
| `w2-native-transactions-compliance-all.json` | Final-source compliance, 3,728/3,728 | `8f5d2cd39d5b17e0815443ea300f2c9b130683acb3f919e426ec36d8f1b56592` |

These Release/.NET 10 receipts identify working changes on `8dccdfab`, the same-commit reused Release runner and unchanged checkout status during each run; **ValidForEvidence remains false** because these are working-source development checks. They do not replace clean final W2 acceptance. Initial compile errors in new diagnostic argument names and a TUnit nullability inference were corrected before native test execution. An overlapping unit build encountered Windows locks on the active Testing CLI DLLs. After the runner exited, the serialized Release build succeeded without warnings/errors and the full unit suite passed 3,878/3,878; no source workaround was needed.

Driver-level cleanup limitations remain explicit: [MySqlConnector 2.6.2 transaction disposal](https://github.com/mysql-net/MySqlConnector/blob/2.6.2/src/MySqlConnector/MySqlTransaction.cs) can perform an implicit rollback. DataLinq does not issue a second explicit recovery rollback or infer confirmed rollback from disposal. W2.4/W2.6 must still prove failed-completion cleanup, recovery-budget behavior and connection trust under interruption; an open connection alone is not evidence of safe business-operation reuse. The adapter currently makes no ordinary-read integrity claim after dispatch.

**At this checkpoint:** captured scalar/query/key/relation integration and tracked mutations/private hydration remained next, followed by the full completion/recovery matrix. The following section records the first integration slice. SQLite bindings, administrative operations, interruption evidence and final parity/performance acceptance remain in scope.

The full-unit receipt is `artifacts/w2-native-transactions-unit.json`, SHA-256 `d6b74b19deb68806cd67b4d0a95c7ccb51a728a70cf7060321d431491dee3846`. It is another bounded working-source run, with `ValidForEvidence=false`.

### Native Pool Test Correction

[CI run #608](https://github.com/bazer/DataLinq/actions/runs/35932947539) on `1460b36b` failed `CanceledPoolWaitRejectsOverlapAndNeverPublishesPartialState` on MySQL 9.7. The temporary schema fixture disables pooling by default. Both native-test helpers set `MaximumPoolSize=1` but inherited `Pooling=false`, so the supposed pool wait was a race against a new physical connection. A quick connection could finish initialization before the overlap assertion. Earlier passing results therefore do **not** establish pool-capacity reuse or cancellation while waiting for a pooled connection.

Both helpers now explicitly enable pooling. Production connection settings and SQLite remain unchanged. The original [failed job](https://github.com/bazer/DataLinq/actions/runs/35932947539/job/107423490903) is retained in `artifacts/w2-transaction-ci-failure.zip`, SHA-256 `a1df9a008683c8b38512d857d9ce4a47f20e0d695bff02fc524e510baa97f0bd`. No unchanged retry was used to dismiss the failure.

With the corrected helpers and the following integration work in the working tree, the full Release provider suite passes **872/872**, zero failures/skips, across all six servers (batches 288/292/292). This includes every native command/transaction pool test. The receipt is `artifacts/w2-native-integration-pooled-final.json`, run `20260923T232708946Z-2cfa6b61c98d468b948c1497f88a3b70`, SHA-256 `e7110a5aecfacd669cfed0226a51b23c7207f754290ccbba7516e16acdd8b904`. It identifies working changes on `1460b36b` and a reused runner; **ValidForEvidence=false** remains explicit. This receipt covers the combined development tree, not a clean intermediate commit or final W2 acceptance.

### W2.3 / W2.4 First Query And Mutation Integration

Standalone and transaction adapters now bind captured scalar commands, and transactions bind captured mutation commands. This lets the existing internal W1 query-plan, prepared-scalar, key, relation, fluent-query and tracked-mutation paths reach native MySqlConnector execution. Initialization and reader lifetime keep the transaction's managed owner; hydration and cache publication retain the existing completion boundary.

[Four integration methods](../../../../src/DataLinq.Tests.MySql/NativeAsyncIntegrationTests.cs), parameterized across all six servers, exercise ordered entities/projections, prepared Count/Any, key hits/misses and instance identity, complete relation snapshots, transaction queries and fluent buffering. Insert/update/delete/Save checks establish private hydration, committed cache publication and rollback preservation of committed values. Rolled-back mutable baselines become invalid and cannot be saved again. Local property edits themselves remain allowed by the existing lifecycle contract.

All **24 new integration cases** pass in the 872-case receipt above, together with the corrected pool checks. Core/MySQL provider Release builds pass on .NET 8/9/10 with zero warnings/errors. This establishes a bounded native path, not completion of the full W2.3/W2.4 matrix: generated IDs/defaults, converter/binary/composite-key cases, capture and cache-generation races, interrupted mutations, uncertain completion, recovery-budget and combined-cleanup behavior still need their native evidence.

Two failed development probes remain recorded. `artifacts/w2-native-integration-initial.json` passed 2/8, SHA-256 `8c8b41745247f69d864df90847bd56c088c4c9b3c211fe4ad3fe53b74f238ab9`; tests incorrectly passed the database wrapper to an internal lookup requiring its row-service source. `artifacts/w2-native-integration-pooled.json` passed 165/171, SHA-256 `38365cd116767ea11204f5205bba3a16d4e8562be3c8830b01aa4c06f9043333`; the remaining failures incorrectly expected property editing after rollback to throw. Tests now use the actual read-only access and assert invalid baseline/save rejection. No production lifecycle policy was changed to satisfy those tests.

[CI run #609](https://github.com/bazer/DataLinq/actions/runs/35934233479) on integration commit `afae9816` passes every lane and the final required gate. It also verifies the corrected pool fixture on the MySQL 9.7 lane that failed at the prior checkpoint.

### W2.5 MySQL/MariaDB Probes, Provisioning And Root Disposal

[Administrative sessions](../../../../src/DataLinq.MySql/Shared/SqlAdministrativeSession.cs) own a native connection across sequential commands. Commands and readers borrow that connection; session cleanup releases it independently. Provider probes use the existing data source and effective database identity. They parameterize schema/table names, preserve INFORMATION_SCHEMA normalization and map expected native failures to false only for availability after clean settlement. Database/table failures and cancellation still escape. No probe borrows an application transaction or creates a missing database.

[Provisioning](../../../../src/DataLinq.MySql/Shared/SqlFromMetadataFactory.AsyncProvisioning.cs) captures the connection configuration and text-only CREATE DATABASE / USE / script batch before suspension. Execution opens and dispatches through concrete MySqlConnector async APIs. Partial DDL remains present after failure; cleanup does not retry, roll back or drop created objects. The initial database in the supplied connection string and generated foreign-key policy keep their synchronous semantics.

[Root disposal](../../../../src/DataLinq.MySql/Shared/SqlProvider.AsyncAdministration.cs) now shares W1's once-only coordinator between synchronous and internal asynchronous calls. Cache maintenance and the data source receive independent cleanup attempts. New provider-bound access rejects disposed roots before cancellation. Root disposal does not complete or drain application transactions; callers remain responsible for dependent lifetimes. MariaDB's existing synchronous constructor version probe remains unchanged.

The first native run exposed two binding defects: an owned raw command's factory validation missed disposed-root validation before cancellation, and low-level native failure reports hard-coded RawCommand, obscuring provisioning/query/mutation identity. The factory now carries the lifecycle check; native command/reader reports leave operation identity to their owning coordinator. Six additional integration executions verify real duplicate-insert and missing-table query errors retain requested operations, provider identity and native error classification. Existing raw-command diagnostics still pass.

The [eight administrative test methods](../../../../src/DataLinq.Tests.MySql/NativeAsyncAdministrationTests.cs) pass **45/45** across all six servers (seven parameterized methods plus one local method repeated in each batch). They cover names and fresh existence state, authentication failure policy, logger failure/cleanup, canceled pool waits, missing-schema creation, captured scripts, partial DDL preservation, sequential session connection ownership and mixed root disposal without implicit transaction completion. The full provider-specific regression passes **917/917**, zero failures/skips, before adding the final logger test and refining session-cleanup attribution. Session cleanup independently disposes its connection and any owned data source, preserving the enclosing administrative operation rather than borrowing root-disposal identity. The final focused run verifies this version. Release core/provider builds pass on .NET 8/9/10 without warnings/errors.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-native-administration-initial.json` | Initial latest-server probe: 10 passed / 3 failed; lifecycle ordering and operation attribution defects | `685a12bba8c5c171058de19bd494ea9c572c932fdfafbea67bdff7d8b1b19e78` |
| `w2-native-administration-corrected.json` | Corrected administrative paths before final logger case: 39/39 | `59d55cb96f875cfedbce68d3900213e6b12a5f42e85dfe54c7fe85d5dda2c79b` |
| `w2-native-administration-mysql-all.json` | Full provider suite and query/mutation diagnostic controls: 917/917 | `3bc7201b25b0d464ba0b0857e160b108a53ca3939c861887af6921bb924b7e97` |
| `w2-native-administration-final-focused.json` | Logger case included, before final cleanup refinement: 45/45 | `2c8c1588843d2f4b0daeb0932bd6c0b0046f941edf519a258dcfaa61f3889c11` |
| `w2-native-administration-cleanup-focused.json` | Final administrative source: 45/45 | `40157db15f7c4645fa21bbf60f332510495a3e1c0f3db67931c8242345bdd17c` |
| `w2-native-administration-compliance-all.json` | Compliance on all eight targets before the final session-cleanup refinement: 3,728/3,728 | `892aceec414acbbd7e9285d99278484a71cdce99d2ac53521051403ee7bd7003` |

The full provider run is `20260923T234434858Z-ba6f2df9e38841a4833b65c258817d6b`. These are working-source runs on `afae9816` with the reused `8dccdfab` runner; **ValidForEvidence=false**. Initial test compilation errors used an incorrect diagnostic property name and attempted direct access to an internal provider type; corrected tests use the existing internal capability boundary. Native metadata parsing, runtime identity/empty-schema behavior and per-command metadata timeout proof remain open, as do the broader SQLite, interruption/recovery and final parity/performance gates.

### Administrative Checkpoint CI Failures

[CI run #610](https://github.com/bazer/DataLinq/actions/runs/35935683263) on `495d45e9` failed two lanes. The unit lane's unchanged `HelperDrainedDisposal_IsAllocationFreeWithoutReopeningAdmission` observed 992 bytes instead of zero, recurring after the 65,304-byte observation in run #607. The SQLite-file lane's unchanged `Threading_ParallelTransactionCommits_PersistIndependentUpdates` reported two nested-transaction errors and a database-locked error. All other lanes passed. The SQLite symptoms are consistent with the open ownership dependency issue, but this observation alone does not establish its precise cause. No unchanged retry was used to dismiss either failure; W0-F1 and corrected-package acceptance stay open.

The original [unit job](https://github.com/bazer/DataLinq/actions/runs/35935683263/job/107432072147) is retained at `artifacts/w2-administration-ci-unit-failure.zip`, SHA-256 `09ba8acaaac0a37904b023aaf39b9f650482122c6802de4810686fc1077b03ed`. The original [SQLite job](https://github.com/bazer/DataLinq/actions/runs/35935683263/job/107432072059) is retained at `artifacts/w2-administration-ci-sqlite-failure.zip`, SHA-256 `ea1a854b74b2e3ddf57127951e24394c249112ecbbba6f1d012c5956d8dc66aa`. Neither failed test nor its product implementation differed from the W2 base at that checkpoint.

### W2.5 MySQL/MariaDB Native Metadata Reads

[Native metadata plans](../../../../src/DataLinq.MySql/Shared/MetadataFromSqlFactory.Async.cs) capture import settings, Include values, identity and connection configuration before suspension. They open an owned administrative session and execute every scalar and reader through W1's metadata context. Pure column/type/default parsing is reused; no generated information-schema database, runtime provider reconstruction or synchronous MariaDB version probe is involved. Runtime reads use the existing provider's effective schema and data source without borrowing an application transaction. Built-in import factories support this internal path; arbitrary derived factories reject it before cancellation/I/O because their synchronous overrides cannot safely be replaced by the captured built-in parser.

Schema existence is checked separately from object enumeration. An existing empty runtime schema produces a complete frozen definition, while an empty import or missing/inaccessible schema fails. Every call reads fresh metadata; no complete result is published after cancellation, command failure or logger failure. This is not a transactionally atomic DDL snapshot. Import filtering and unsupported-type failures remain distinct from runtime comparison scope. Views, composite foreign keys and actions, index filtering, checks, comments, enums, binary types and defaults are represented using the existing metadata model. A missing view definition triggers a quoted SHOW CREATE VIEW fallback; operational failures are not swallowed by the legacy CatchAll pattern. That fallback branch still lacks a forced native fixture.

The [nine native test methods](../../../../src/DataLinq.Tests.MySql/NativeAsyncMetadataTests.cs) include seven server-parameterized methods and two local validation cases. The final focused run passes **48/48** across all six servers. Tests compare complete metadata digests and generated-SQL roundtrips, inspect timeout values on every actual native metadata command (inherited default, zero and rounded positive timeout), verify scalar and reader timeout interruption under a real InnoDB row lock, and prove one-connection pool release. Other cases cover mutable Include capture, schema override, fresh/empty/missing schema state, validation precedence, cancellation while waiting for the pool and during parsing, native authentication failure and original logger-failure attribution. The timeout interruption test uses the real metadata session/context with a deliberately blocked row query; it does not claim information-schema itself was blocked.

Before adding the derived-factory guard, the full provider-specific Release suite passed **968/968**, zero failures/skips, in batches 320/324/324. The final focused run verifies the guard alongside all metadata cases. Core/provider Release builds pass on .NET 8/9/10 with zero warnings/errors. These runs use working changes on `495d45e9` and a runner built from that commit's dirty tree; checkout state remains stable within each run and **ValidForEvidence=false**. They are development checks, not clean final W2 acceptance.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-native-metadata-initial.json` | Initial latest-server probe, 13/13 | `bc3961c2f455fcad3b825413daa9cc9d03ed34e37760a8e5894946434a72cb2a` |
| `w2-native-metadata-focused-all.json` | 39 passed / 6 failed: test incorrectly required enclosing MetadataRead identity from an internal scalar context | `1c2248f9b092800711727d83f06ad7f63a67e4f233d7a875f16689bfeef3c810` |
| `w2-native-metadata-mysql-all.json` | Full provider regression before derived-factory guard, 968/968 | `84384a10aa4747de4cb7e2c0d85a5c3b91730d3e509d52d2a08bc91030477752` |
| `w2-native-metadata-final-focused.json` | Final metadata source, 48/48 | `2717e5f40d9009116da533a9d9060e0d4615b67b34ea2a5a38c0dd4c8de6f511` |

The full provider run is `20260924T000504921Z-146c516d9ada42ec8cc842d2b3f6e14f`; the final focused run is `20260924T001244237Z-21b735c93e824ccba225ee58b5db6a85`. The failed timeout probe already obtained the expected native timeout; removing the incorrect lower-layer operation assertion changed no production classification. Owning-coordinator metadata operation and logger attribution have separate passing tests. SQLite bindings, full query/conversion/cache-race coverage, interrupted mutation/completion/recovery and final parity/performance acceptance remain required.

### Allocation Measurement Isolation After Recurrence

The unchanged helper-drained iterator class passed a fresh local **6/6** control after run #610. A separate one-case JIT-disassembly probe showed that .NET 10.0.12 on Windows compiles the already-drained `Dispose` branch to a field read, branch and return, with no calls or allocation. This narrows the local product path; it does **not** establish which component allocated on Linux CI. In particular, the exact source of the observed 992 or 65,304 bytes remains unproven.

The [allocation test](../../../../src/DataLinq.Tests.Unit/Core/CompletedGuardedEnumerableTests.cs) now warms and measures the same synchronous helper outside the async test state machine. `NoInlining | NoOptimization` applies only to that measurement helper. The [.NET 10 runtime's tiering eligibility check](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/vm/method.cpp#L2672-L2705) excludes methods marked NoOptimization, so this removes tiering transitions in the measurement scaffold without changing production compilation. It is a controlled measurement change, not a demonstrated diagnosis of the prior CI allocation.

The assertion still requires **zero allocated bytes over all 10,000 disposals**, with no retry, minimum-of-runs or relaxed threshold. A new negative control disposes an intentionally allocating implementation through the same helper and requires at least its 640,000 payload bytes to be detected. Product `GuardedEnumerable` behavior and the finished-operation gate test are unchanged. The full Release unit suite passes **3,879/3,879** with this change; the new CI run must still establish its behavior on Linux.

| Local artifact under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-allocation-recurrence-control.json` | Unchanged class, 6/6 | `322cec303cf8b556b9cb1d33b09d12e5a33dab73146497236ca350ff6740c2f2` |
| `w2-allocation-jit-control.json` | Unchanged helper-drained case with local JIT output, 1/1 | `a3018aa2b8276236a453c0e764d4a1dcb12d0a8d7f605bcd1d2e801ce1ab5240` |
| `w2-guarded-disposal-jit.txt` | Windows native code for the product Dispose method | `cf3978cfe2a7e8c4438fe7cbb0a49729db0ec968e43d1c5735e7977103f7739c` |
| `w2-metadata-allocation-unit.json` | Full unit suite with isolated measurement and allocating control, 3,879/3,879 | `4c2400ba1763bc06b56f87ea5c2ffae7383276e33db659d932f8aa60a6cb7612` |

The full unit run is `20260924T000731577Z-8422b228b7b94457835ad9fc04411368`, using working changes on `495d45e9` and the same dirty-source runner. **ValidForEvidence=false**; none of these controls replaces the original failed CI artifacts or final W2 performance evidence.

[CI run #611](https://github.com/bazer/DataLinq/actions/runs/35937623881) on `46f9f2b1` passes every lane and the final required gate, including the isolated allocation test on Linux and both SQLite lanes. This is a new source revision, not an unchanged retry of #610. A passing SQLite lane on 10.0.11 does not close the corrected-package gate, and a passing allocation control does not identify the source of the earlier observations.

### W2.4 / W2.6 First Native Interruption And Recovery Matrix

The [native interruption tests](../../../../src/DataLinq.Tests.MySql/NativeAsyncInterruptionTests.cs) observe the actual blocked UPDATE in the server process list before canceling it. Caller cancellation, a soft command timeout and immediate hard command timeout each leave the tracked mutation unusable for subsequent reads or commit. Successful soft interruption retains rollback/disposal; hard interruption leaves the native connection Broken and permits disposal only. The mutable baseline is invalidated, committed cached values remain unchanged, and another operation acquires the one-slot pool only after settlement. Pre-canceled explicit rollback does not clear the poisoned state.

The fixture uses a two-second statement timeout and a separate 30-second cancellation window for the intended soft case; `CancellationTimeout=-1` selects the hard case. These are test connection settings, not changed production defaults or the recovery rollback budget. [MySqlConnector's cancellation contract](https://mysqlconnector.net/overview/command-cancellation/) and [2.6.2 timeout implementation](https://github.com/mysql-net/MySqlConnector/blob/2.6.2/src/MySqlConnector/Core/ICancellableCommand.cs) distinguish these mechanisms. Tests require the QueryInterrupted inner exception and an Open connection for soft timeout, and a Broken connection for hard timeout. They do not infer transaction reuse from an open socket.

A second case kills the connection immediately before a commit or rollback attempt. The original native exception escapes, completion remains Unknown through disposal, and another commit/read is rejected. This exposed a missing provider classification at the native completion boundary: [commit/rollback reporting](../../../../src/DataLinq.MySql/Shared/SqlDatabaseTransaction.Resource.cs) now preserves ProviderError, operation and provider identity for native failures, while the managed coordinator continues to determine completion certainty and permitted recovery. This fixture does not simulate a lost acknowledgement after a server-confirmed commit; that distinct case remains open.

Confirmed native commit/rollback survives a subsequent status-observer exception; the original exception, LocalFinalizationError classification and confirmed outcome remain intact, and independent reads verify persistence or rollback. A canceled callback uses an independent native rollback token under the existing default recovery settings, retains the caller's cancellation as primary, and retains an optional rollback-observer exception as a secondary failure. Resources are disposed before the callback escapes. This proves successful independent recovery, not expiry of the cooperative rollback budget or a deadline on driver disposal.

Four methods produce **24 executions** across six servers. The final full provider-specific Release suite passes **995/995**, zero failures/skips (329/333/333), including the previous native metadata, command, transaction and integration cases. Core/provider Release builds pass on .NET 8/9/10 with zero warnings/errors. Final receipt: `20260924T002627882Z-eea4797f4ff1416d91cddc0b37aba9df`, with stable working changes on `46f9f2b1` and the reused dirty `495d45e9` runner; **ValidForEvidence=false** remains explicit.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-native-interruption-initial.json` | 0/6: missing native completion cause; tests also incorrectly expected Closed instead of Broken and ApplicationError instead of LocalFinalizationError | `83e43631c2f00f1f31495d1502f9d23cc61c7457677c8ad6fc2363885931f2e8` |
| `w2-native-interruption-corrected.json` | First three corrected methods, 18/18 | `b5d78457194e9e993415eb6bc40f55553b59bd055492a0bbc0b93eb94f6a2175` |
| `w2-native-interruption-recovery.json` | 18 passed / 6 failed: fixture attempted caller completion inside a callback helper, which correctly owns completion | `d952e4336ed336284ec344483b790525f7bbb534384a1d44a2c8ba90462ea794` |
| `w2-native-interruption-mysql-all.json` | 993 passed / 2 failed: intended soft-timeout assertion failed on both MySQL targets with the fixture's two-second cancellation window | `64c80462092442bb28f3ca7429148316630975de121bf304047f2de5c416fafe` |
| `w2-native-interruption-final-mysql-all.json` | Final full provider regression, 995/995 | `519b2bdbd5ce038a14831faeb4f5402b41d4c4de1f2a8e7c8d13a96260472c4e` |

The last fixture revision separates the soft-cancellation confirmation window from the short statement timeout and retains all recovery assertions. It does not establish why the shorter window failed under the broader concurrent run. No product cancellation policy was weakened. Remaining W2 work still includes SQLite, generated/converter/binary/composite-key and capture/cache-generation evidence, ordinary-read integrity classification, post-commit acknowledgement loss, cooperative recovery-budget expiry, native cleanup-failure combinations, metadata fallback evidence and final parity/performance acceptance.

[CI run #612](https://github.com/bazer/DataLinq/actions/runs/35938635939) on `aa3e5df2` passes every lane and the final required gate, including the new interruption and callback-recovery cases.

### W2.1 SQLite Standalone Commands And Readers

[SQLite command binding](../../../../src/DataLinq.SQLite/SQLiteDbAccess.Async.cs) and the [native reader](../../../../src/DataLinq.SQLite/SQLiteAsyncDataLinqDataReader.cs) connect the internal W1 capabilities to Microsoft.Data.Sqlite. The driver's [async methods execute synchronously](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async); this implementation introduces no worker-thread dispatch or claim of interruptible SQLite I/O. Connection opening and committed-visibility setup are distinct from business-command dispatch. Cancellation and validation at boundaries prevent work where possible; a confirmed native success is not reversed by a late token request. BUSY/LOCKED remains a native provider error because the code alone does not establish whether a timeout elapsed.

Owned reader/connection cleanup is independent. SQLite connection closing disposes registered commands, so standalone cleanup detaches the borrowed command before releasing its owned connection. Failed opening leaves a pre-existing caller connection untouched. Post-acquisition observer failure releases the unreturned reader and connection. Native scalar casts, SQL NULL versus no row, copied binary values, independent returned buffers and the provider's canonical GUID text representation are preserved. Unsupported command subclasses and attached transactions reject before cancellation/I/O.

The [11 test methods](../../../../src/DataLinq.Tests.Unit/SQLite/SQLiteNativeAsyncCommandTests.cs) produce 22 executions, including file/shared-memory cases with pooling explicitly enabled. Tests establish early/canceled reader cleanup, reusable caller commands, actual native/logger/setup/completion-observer failures, setup cancellation without business dispatch, and failure to open a missing database. A SQLite user function cancels the token inside a successful UPDATE and records that it runs on the caller thread. A separate lock-holder and cancellation thread establish that a native lock wait continues blocking the calling thread and returns SQLite's error even after cancellation. These observations are specific driver limitations, not a general cancellation-success claim.

The full Release unit suite passed **3,901/3,901** and SQLite file/memory compliance passed **923/923**, zero failures/skips, before the final GUID-normalization parity refinement. Core/SQLite provider builds passed on .NET 8/9/10 without warnings/errors. Final focused verification follows that refinement. These are dirty-source checks on `aa3e5df2` with a runner built from that dirty commit, stable checkout within each run and **ValidForEvidence=false**. They use 10.0.11 and do not close the official 10.0.13 adoption gate.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-sqlite-native-commands-initial.json` | First nine methods, 17/17 | `33233215a309578c35af2c3eb364735f3328031bc6681568f768d78dbbd46c83` |
| `w2-sqlite-native-commands-unit.json` | Full unit suite, 3,901/3,901 | `3aa7e78aaf20542ed58ef6879a7de640b5b4d2a3daaedad5c1602bc290aa2019` |
| `w2-sqlite-native-commands-compliance.json` | SQLite file/memory compliance, 923/923 | `b8c90c3ea21e879e520b48baaf21b7116b34beb35556c6c5f1ba418112d80a59` |
| `w2-sqlite-native-commands-final.json` | Final focused source including GUID parity, 22/22 | `c5bd6c4df2cbfcf93e3e947da4d5cabed75f60d302c4ddb501bbfc988d8d2c25` |

The full unit run is `20260924T004405483Z-3691f4c276fb4ed4912c822e603daf37`. A TUnit nullable-return lambda compilation error was corrected before execution; an internal single-use command invocation is rebound for each repeated reader test. No failing native test was retried unchanged. SQLite transaction/administrative binding, the broader conversion/cache/recovery matrix and final candidate evidence remain in progress.

[CI run #613](https://github.com/bazer/DataLinq/actions/runs/35940093472) on standalone commit `d47de4d1` passed.

### W2.2 / W2.3 / W2.4 SQLite Transactions And Initial Integration

[SQLite's resource bundle](../../../../src/DataLinq.SQLite/SQLiteDatabaseTransaction.Resource.cs) now shares W1 lazy initialization between direct synchronous and internal asynchronous execution. Opening, committed-visibility setup and deferred Serializable begin finish before native handles are published. Observer or cancellation failure during started initialization is terminal and cleans the private resources; pre-cancellation leaves an unused wrapper reusable. Attachment adopts the existing transaction and preserves its isolation policy without opening or beginning again. Unused completion/disposal does not initialize resources.

Microsoft.Data.Sqlite 10.0.11's [concrete deferred begin](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteConnection.cs) must be used explicitly: the inherited asynchronous overload is synchronous but does not expose the deferred choice. A native fixture holds another writer's lock while both sync-first and async-first transactions execute SELECT 7 and read the visibility PRAGMA successfully. This distinguishes deferred BEGIN from BEGIN IMMEDIATE. Commit/rollback/disposal use the driver's awaitable methods with their synchronous limitations; disposal may implicitly roll back and cannot establish a confirmed outcome by itself.

[Transactional commands](../../../../src/DataLinq.SQLite/SQLiteDatabaseTransaction.Commands.cs) bind raw, captured query/scalar and tracked-mutation capabilities. Commands and readers borrow the transaction connection; readers retain admission through cleanup. Completed commands detach before SQLite connection cleanup can dispose caller-owned objects. Original native/logging errors and dispatch evidence reach the owning coordinator. SQLite retains its existing direct cast and DBNull behavior. Raw SQL has unknown effects; a cast failure after dispatch still restricts the transaction to recovery. No ordinary-read integrity exception is asserted here.

The [11 transaction test methods](../../../../src/DataLinq.Tests.Unit/SQLite/SQLiteNativeAsyncTransactionTests.cs) pass **22/22** in the full unit run. They cover mixed sync/async first use and completion, pre-cancellation/capability validation, reader ownership/admission, deferred locking, scalar parity, failed initialization/reentry, consuming attachment, disposal rollback and confirmed completion despite observer failure. Callback cancellation uses an independent recovery token, preserves the primary cancellation and optional secondary rollback-observer error, and disposes before returning. This does not prove a bounded duration for a blocked SQLite rollback.

The [five integration methods](../../../../src/DataLinq.Tests.Compliance/Query/SQLiteNativeAsyncIntegrationTests.cs) pass **10/10** across file and shared-memory databases in **933/933** SQLite compliance cases. They exercise captured query/projection/prepared scalar/key/relation/fluent paths, instance identity, Insert/Update/Delete/Save, private hydration, committed cache publication, invalidated rolled-back baselines and native query/mutation diagnostic identity. Shared-memory isolation is not mistaken for nonblocking outside reads: pre-commit publication is checked directly in the committed cache. Broader generated-value/converter/binary/composite-key and cache-generation races remain required.

The first focused run passed 18/20; two assertions incorrectly expected a raw scalar cast failure to leave business execution usable. Tests now require recovery without weakening production policy. A separate initial build invocation tried to rebuild the currently running Testing CLI and failed on Windows DLL locks. A serialized build and no-build run corrected that invocation. One initial compile error was a nullable resource-field assignment; the field now reflects its actual pre-initialization null state.

The full unit run passed **3,922/3,923**, with all new SQLite cases passing. Its one failure was the unchanged `QueryPlanCapabilityValidationTests.ExecutionCapabilityValidation_AllocatesNothingForSupportedInvocation`: expected zero, observed 8,176 bytes. The test and measured product code are unchanged from the W2 base. A fresh focused control passed 34/34 without source changes; this does not dismiss the original failure or identify its cause. The measurement follow-up is recorded separately below. These are working changes on `d47de4d1` with a dirty-source runner from that commit; **ValidForEvidence=false**.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-sqlite-native-transactions-initial.json` | Failed build: running Testing CLI DLLs were locked | `25f21ebba078261d697449b38dad40d3ac806484a9f7d2d66d0acd25b70a9c0e` |
| `w2-sqlite-native-transactions-focused.json` | Initial native cases, 18 passed / 2 incorrect recovery assertions | `ec8cfc7a4c1e0bd5a95e2870ca5c1fd36bf92e2e93bb276fbdcc3553eab165b4` |
| `w2-sqlite-native-transactions-unit.json` | Full unit suite, 3,922 passed / 1 allocation failure | `196ebfb8009b7040501fb4349fa5474256b4ef70dd47e78d37cf179b176ba9a9` |
| `w2-sqlite-native-transactions-compliance.json` | SQLite compliance including ten integration cases, 933/933 | `d8f8ac86116f48038d52fea301f310b912e9a7fed29a02b9978e0d6122d30f3b` |
| `w2-query-allocation-control.json` | Unchanged query-validation class, 34/34 | `25224c1f514645d7c71a522ebb9d9812c193bcc9be4be47db56ab073c95d95ea` |

SQLite native metadata/provisioning/probes/journal/owning-root binding and corrected-package adoption remain open. Completion uncertainty, interruption/recovery-budget/cleanup combinations, the broader query/mutation matrix and final parity/performance acceptance remain required across W2.

### Query And Key Allocation Measurement Follow-Up

After isolating the query-validation measurement scaffold, the next full unit run passed **3,923/3,924**. The query checks and new deliberately allocating control passed; a separate unchanged `KeyFactoryAndEqualityTests.SimpleKeyValueReads_DoNotAllocateSnapshotArrays` observed 4,544 bytes. Its product key implementation is unchanged from the W2 base. A fresh unchanged focused class control passed 27/27. Neither focused control identifies the origin of the earlier full-run allocations, and neither replaces the retained failures.

The [query-validation helper](../../../../src/DataLinq.Tests.Unit/Linq/QueryPlanCapabilityValidationTests.cs) and [key-read helper](../../../../src/DataLinq.Tests.Unit/Core/KeyFactoryAndEqualityTests.cs) now disable inlining/optimization only in the measurement scaffold, using the same isolation rationale as the helper-drained test above. Product getters and validators keep their normal JIT policy. Key tests warm the same scaffold before measuring. Query checks still require zero in the maximum of all five samples; key checks still require zero across all 10,000 reads. No threshold, minimum sample, production fallback or retry was introduced. Negative controls require detection of deliberately allocated arrays and the binary key getter's required defensive copies. This removes tiering/OSR transitions in the scaffold; it does not prove those transitions caused the exact 8,176- or 4,544-byte observations.

The resulting full Release unit run passes **3,925/3,925**, zero failures/skips, including all 44 SQLite standalone/transaction cases and both new allocation controls. The ten SQLite integration cases remain covered by the 933/933 compliance receipt above. Final core/SQLite provider builds pass on .NET 8/9/10 with zero warnings/errors. The combined working-source receipt is `20260924T010544869Z-10df970e95b74a3e9bb0abe5d08076ff` on `d47de4d1` with its dirty-source runner; **ValidForEvidence=false**. The receipt records the development tree containing both the transaction and measurement commits, not a clean final W2 candidate.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-sqlite-transactions-allocation-unit.json` | Query isolation/control included; 3,923 passed / 1 unchanged key-allocation failure | `b25d4f43cad0997e0d2f69da9f27840a6bd5ffa44af95cf75854fb26ec9d0143` |
| `w2-key-allocation-control.json` | Unchanged key class, 27/27 | `369436be197a2187e9b3a9a5427bf69bfc2f34e3fc24d00fa5fad0eb3e63b2a5` |
| `w2-sqlite-transactions-final-unit.json` | Final combined source, 3,925/3,925 | `aea6e29948f705d4e51db5d9488b732ac8c3b1139bf62aef46ea1d184c4f21dd` |

[CI run #614](https://github.com/bazer/DataLinq/actions/runs/35941607818) on `ef733949` passes, including the SQLite transaction and allocation-scaffold changes. Corrected-package and final W2 acceptance remain open.

### W2.5 SQLite Administration And Keeper Ownership

[Administrative sessions](../../../../src/DataLinq.SQLite/SQLiteAdministrativeSession.cs) own one execution connection across sequential commands. Native commands/readers borrow that connection and detach before session cleanup, preserving caller command ownership. [Probes and journal configuration](../../../../src/DataLinq.SQLite/SQLiteProvider.AsyncAdministration.cs) retain the provider's effective identity; SQLite's optional probe database name remains ignored. File table probes open read-only so a missing file is not recreated. Local file/database availability preserves the existing file-existence policy. Journal setters confirm successful PRAGMA execution, not that SQLite adopted the requested mode: shared-memory databases retain their memory journal.

[Provisioning](../../../../src/DataLinq.SQLite/SqlFromSQLiteFactory.AsyncProvisioning.cs) captures the text-only script and normalized connection configuration. A missing file is created without truncating a competing creator's file, including the existing ReadWrite-mode factory behavior. Native execution preserves partial DDL after failure. Memory provisioning retains a fallback keeper until a provider adopts ownership. No automatic drop, retry or rollback of provisioning side effects is introduced. All awaitable SQLite operations retain the driver's synchronous limitations.

Owning roots share once-only sync/async cleanup of cache state and their keeper lease. New provider-bound execution rejects a disposed root before cancellation; dependent transactions/readers remain independently owned. The [keeper registry](../../../../src/DataLinq.SQLite/SQLiteConnectionStringFactory.cs) uses one gate for acquisition and dictionary removal, and leases retain their actual entry. This fixes a code-level race where last-release removal could discard a reacquired entry. The native stress fixture exercises 32 release/reacquisition rounds and verifies generation retention and final release, but does not claim deterministic reproduction of the original interleaving or resolution of the upstream pool issue.

The [ten administrative methods](../../../../src/DataLinq.Tests.Unit/SQLite/SQLiteNativeAsyncAdministrationTests.cs) produce **17 passing cases** in the full Release unit run: **3,942/3,942**, zero failures/skips. Coverage includes fresh quoted-name probes, missing-file behavior, lifecycle/cancellation validation, journal-mode semantics, logger failures, caller-command/session ownership, independent dependent lifetimes, captured scripts, partial DDL and mixed keeper release. Core/SQLite provider Release builds pass on .NET 8/9/10 without warnings/errors.

The first run passed 12/16: four file cases failed only during fixture deletion because read-only probe pools use a different connection-string key. Fixture cleanup now clears both of its own pools while leaving pooling enabled. One intermediate compilation incorrectly referenced a provider-internal helper; the fixture now checks its known memory mode directly. The subsequent no-build invocation correctly refused a stale test graph and ran no tests. None of these failures was dismissed by an unchanged retry.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-sqlite-native-administration-initial.json` | Initial 12 passed / 4 read-only-pool fixture cleanup failures | `019ac22cb3e58c6d9768bf0ee5612d3a994caebdc3da3ecba04d9aca9de1ecf4` |
| `w2-sqlite-native-administration-unit.json` | Final full unit suite, 3,942/3,942 | `973deb409268fec4ba86afc333751a3c1dcb67b0ae4fad62914d92ee9c609274` |

The full run is `20260924T012438292Z-817b37a036e94e5e90df04723e1bb267`, using working changes on `ef733949` and a runner built from that dirty tree; **ValidForEvidence=false**. SQLite metadata, broader conversion/capture/cache and failure matrices, corrected-package adoption and final parity/performance evidence remain required.

[CI run #615](https://github.com/bazer/DataLinq/actions/runs/35942949608) on administrative commit `c84bc4fc` passed.

### W2.5 SQLite Native Metadata Reads

[Captured metadata plans](../../../../src/DataLinq.SQLite/MetadataFromSQLiteFactory.Async.cs) now bind import and runtime validation reads to the native session. Runtime reads preserve the existing provider's effective file/named-memory identity and borrow its keeper lifetime without reconstructing a provider or borrowing an application transaction. File sessions open read-only, so missing files fail without being created. An existing empty runtime database yields a frozen empty definition; empty imports and missing requested objects remain model failures. Operational failures retain their native exception and metadata diagnostic identity through W1's result contract; cancellation escapes without publishing a partial definition.

Every created command, including committed-visibility setup, passes through the metadata context. Native dispatch observations verify inherited connection defaults, zero and rounded positive timeouts after connection binding. Separate scalar and reader queries against an actual locked SQLite table exercise the one-second timeout in both file and shared-memory modes. BUSY/LOCKED remains ProviderError rather than being inferred to mean Timeout merely from the error code; the fixture's observed wait and explicit timeout provide this test's timing evidence. SQLite execution remains synchronous and these tests establish no interruptible-I/O guarantee or transactionally atomic DDL snapshot.

Sync and async paths reuse [catalog interpretation](../../../../src/DataLinq.SQLite/MetadataFromSQLiteFactory.Catalog.cs) and pure column/default parsing, retaining warnings for unsupported partial/expression/descending indexes and blob defaults, and preserving composite foreign-key actions. Captured settings include independent Include/name-policy values; arbitrary derived import factories reject before cancellation/connection parsing. Full import digests, warning sets, SQL generation/readback and generated-model-source parsing roundtrips agree.

The roundtrip exposed an existing shared-parser defect: quoted view SQL was escaped as if already inside a C# string, and CREATE VIEW then received invalid backslashes. The parser now retains SQL quotes; the existing model-source formatter performs C# escaping. Both synchronous and asynchronous imports receive the correction, verified through actual SQLite SQL recreation and parsed generated source. This is a concrete behavior correction beyond adding the internal binding.

The [eight new methods](../../../../src/DataLinq.Tests.Unit/SQLite/SQLiteNativeAsyncMetadataTests.cs) pass **14/14** in the full Release unit run, **3,956/3,956**, zero failures/skips. SQLite file/memory compliance passes **933/933**. Core/SQLite provider builds pass on .NET 8/9/10 without warnings/errors. The full unit run is `20260924T014001569Z-d2691521f3b343d08ee77e17f3c0bb3b`; compliance is `20260924T014138371Z-dd75d4ac40bb4b78a30d23eb4fbc6aa5`. They use working changes on `c84bc4fc` and a dirty-source runner; **ValidForEvidence=false**.

Failed development evidence is retained below. The first fixture used unsupported DATETIME, then used a TEXT timestamp without the `_at` naming convention needed by the existing importer. Correcting the fixture exposed the actual quoted-view defect. That run also failed an unchanged telemetry assertion with duplicate matching gauges after failed fixture construction left provider resources unclosed. Fixture construction now disposes on schema failure; the final full run passes the unchanged telemetry test. This observation does not independently prove the exact source of every duplicate gauge. An initial test compilation incorrectly used public `await using` before W3; the test now uses supported synchronous transaction disposal. One unquoted PowerShell target-list invocation failed before test execution; the quoted target list produced the compliance receipt.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-sqlite-native-metadata-initial.json` | 8 passed / 6 unsupported DATETIME fixture failures | `c949ce91301ca12d427c72a4ed2286a4a4f405af02cff362a6103b4dca0d8bc5` |
| `w2-sqlite-native-metadata-unit.json` | 3,950 passed / 6 timestamp naming/type fixture failures | `c38f787074cce5256c8bf3ac707e4d7435dbb3143021c333ae6cbfa737bee9a4` |
| `w2-sqlite-native-metadata-final-unit.json` | 3,953 passed / 2 quoted-view recreation failures / 1 duplicate-gauge failure | `59c2cc1d9b6d2c91da4cb0391e4de8b1e05f7b237b976844e5d9dcac2396a301` |
| `w2-sqlite-native-metadata-view-fix-unit.json` | Final metadata source and fixture cleanup, 3,956/3,956 | `17d895b2048887b419779bb37b64f0e6ae23378290af23fdfd6c0d2c36a5723f` |
| `w2-sqlite-native-metadata-compliance.json` | SQLite file/memory compliance, 933/933 | `158ff02fa27ce9c5c310162e50335d4e8c7016e76fa9f44ecddee049cc484592` |

Native bindings now cover the inventoried SQLite operation families, with bounded evidence. W2 still requires the broader conversion/capture/cache-generation and interruption/recovery matrices, forced MySQL view-fallback evidence, corrected SQLite dependency adoption and final parity/performance acceptance. None is closed by this checkpoint.

[CI run #616](https://github.com/bazer/DataLinq/actions/runs/35944223929) on metadata commit `b0ffd4fd` passed.

### W2.3 / W2.4 Native Values, Captured Inputs And Cache Generations

The [native value matrix](../../../../src/DataLinq.Tests.Compliance/Query/NativeAsyncValueIntegrationTests.cs) now runs against SQLite file/shared memory, MySQL 8.4/9.7 and MariaDB 10.11/11.4/11.8/12.3. Its [fixture](../../../../src/DataLinq.Tests.Compliance/Query/NativeAsyncTestDatabase.cs) explicitly enables pooling for execution roots, retains independent provisioning ownership and clears only its own SQLite pool during final cleanup. It does not inherit the provisioning fixture's disabled-pooling configuration.

The cases cover converted auto-increment keys and server defaults, explicit SQL NULL versus unset defaults, nullable/empty binary values, transaction-private hydration and rollback invalidation, converted plus binary composite-key identity, root-cache visibility across update/commit/delete, and ordinary/prepared query argument capture. An escaped mutation array cannot change captured SQL; its detected post-write inconsistency poisons the transaction and invalidates the mutable, as required by AAPI-27/28. Native-handle inspection establishes the actual written bytes before managed rollback. Finite batches enumerate and reserve their inputs once; clearing the source list or setting a reserved later mutable cannot alter execution. Cancellation at the second native command retains the first write until rollback and denies managed commit. Dedicated test threads reach deterministic barriers even when SQLite's awaitable call completes synchronously; production execution gains no worker-thread fallback.

The matrix exposed an existing generated-property ownership defect. `RowData` already returned a defensive binary copy, but the generated immutable getter memoized that public array and returned it again. A caller could consequently change the cached model's observed property without changing its underlying row. [Generated binary getters](../../../../src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs) now read through the defensive row accessor on every access; ordinary scalar memoization remains. Regression assertions cover nullable/non-null payloads and binary keys, synchronous access and native async materialization. This restores the documented binary ownership contract rather than weakening the failing assertion. Binary property reads now incur their required defensive copy; this correctness cost must remain visible in final performance comparisons.

The [native cache matrix](../../../../src/DataLinq.Tests.Compliance/State/NativeAsyncCachePublicationTests.cs) pauses actual model construction while another operation performs clear, precise invalidation or committed update. Key lookups and non-key expression queries run each invalidation both with and without a newer row being warmed before the old load resumes: **12 interleavings per target, 96 total**. Subsequent reads retain the current generation and any already-warmed identity. Cancellation during construction releases the observer/owner and permits subsequent loading; it does not assert that an already-complete reusable row must be discarded.

The initial native-values run passed **16/32**. Eight failures exposed the generated binary getter defect; eight incorrectly expected escaped-array modification to succeed instead of W1's established post-write failure. The latter expectations were corrected while preserving actual-SQL, poisoning, diagnostic, invalidation and rollback assertions. An intermediate unrun edit temporarily tested only independent materializations; it was discarded after checking the ownership contract, and final coverage retains the stronger repeated-getter assertion. No unchanged retry or reduced product guarantee closes these failures.

The final full Release suites pass **3,956/3,956 unit** and **3,824/3,824 compliance** cases, zero failures/skips. Compliance includes **56/56 new cases** across all eight targets. Core builds pass on .NET 8/9/10 with zero warnings/errors. Full unit run: `20260924T020533103Z-7aef5733f6094d4dbd9ac165b43d62d9`; full compliance: `20260924T020613820Z-639461950721427b83a515a954ddb445`. Both use stable working changes on `b0ffd4fd` with a matching dirty-source runner; **ValidForEvidence=false**. The earlier focused receipts used an older runner and are superseded for final-source coverage by the full suites.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-native-values-initial.json` | 16 passed / 8 binary getter failures / 8 incorrect escaped-array success expectations | `c1f80a674f269a51905dd6ad9f69ebc9243dc7eec861f7052fa4e7732b377e98` |
| `w2-native-values-cache-first.json` | Getter correction, initial values and cache cases, 48/48 | `89183f78a29afc6c30fd5078b8282f2c13fe6e6df84bf6a2b3b5722c43174495` |
| `w2-native-values-cache-batch.json` | Finite batch cases added, 56/56, before non-key query refinement | `3df34be7c7820ed7e86c54e837872c0a18c071d743cf3145684475ce950a5b5c` |
| `w2-native-values-cache-unit.json` | Final product source, full unit suite, 3,956/3,956 | `3b8e4f0088ef686197a805799d7b6853bf20bfe6ed46ece2f3c5c3f7b0c0f886` |
| `w2-native-values-cache-compliance.json` | Final source, all-target compliance, 3,824/3,824 | `0c8d7c59a8fb3a6c53464781d655fd4a2655834ec4d34f6e14ef1f96f4250850` |

Remaining W2 work includes ordinary-read interruption/integrity evidence, post-commit acknowledgement loss, cooperative recovery-budget expiry and native cleanup-failure combinations, forced MySQL view-fallback evidence, corrected SQLite dependency adoption, and final cross-provider parity/performance/telemetry and clean-candidate acceptance. This checkpoint does not close those requirements.

[CI run #617](https://github.com/bazer/DataLinq/actions/runs/35946294104) on values/cache commit `02559d89` passed.

### W2.5 Forced Native View Fallback

The [fallback fixture](../../../../src/DataLinq.Tests.MySql/NativeAsyncMetadataFallbackTests.cs) now exercises MySQL 8.4/9.7 and MariaDB 10.11/11.4/11.8/12.3. A test access decorator changes only the catalog projection to `NULL AS VIEW_DEFINITION`; that query still runs natively, and the subsequent `SHOW CREATE VIEW` statement is unchanged. This deliberately forces the branch. It is not evidence of a naturally occurring permission arrangement that hides the catalog definition while permitting SHOW CREATE VIEW.

Each target covers successful frozen metadata/digest parity, a server failure after another connection drops the actual view before fallback dispatch, and cancellation at the fallback driver's entry. The view name contains an embedded backtick; exact qualified identifier quoting and the rounded two-second command timeout are verified. Native exceptions retain identity and metadata diagnostics, cancellation retains its token, and failed reads do not return partial metadata. Each owned session settles before a fresh complete read reuses its one-slot pool. No production implementation change or synthetic reader/error substitute was needed.

Focused fallback cases pass **6/6** (three modes per target). The final full Release provider-specific suite passes **1,001/1,001**, zero failures/skips, also covering the generated getter correction from the prior checkpoint. Run `20260924T021656730Z-1f3d5147ba464f9f87ce0a3f5ba75648` uses stable working changes on `02559d89` and a matching dirty-source runner; **ValidForEvidence=false**. The earlier focused run used the older `b0ffd4fd` runner.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-native-metadata-fallback.json` | Forced native fallback, 6/6 | `beed979a3d89cba4ec69e8f720377ab526ca64d75fff42bd421af6fb41a98db6` |
| `w2-native-metadata-fallback-mysql.json` | Full provider-specific suite, 1,001/1,001 | `4c6dbdf5fd7f154c205feafcebb33176fe497e338eba1b5edafb99df343e988b` |

The forced fallback evidence gap is now covered. Ordinary-read interruption/integrity, post-commit acknowledgement loss, recovery-budget expiry, native cleanup-failure combinations, corrected SQLite adoption and final parity/performance/telemetry and clean-candidate acceptance remain open.

[CI run #618](https://github.com/bazer/DataLinq/actions/runs/35946763255) on fallback commit `4996e013` passed.

### W2.4 Native Commit Acknowledgement Loss

A [test-only TCP relay](../../../../src/DataLinq.Tests.MySql/NativeCompletionProxy.cs) now intercepts the real successful server response to one explicitly armed COMMIT. It forwards all other classic-protocol packets unchanged, including connection/authentication and command execution. The fixture uses local plaintext, uncompressed connections and handles the attribute-free COM_QUERY layouts exercised by MySqlConnector 2.6.2; it is not a general MySQL proxy or TLS/compression test. Packet framing and query/OK markers follow the [MySQL protocol specification](https://dev.mysql.com/doc/dev/mysql-server/8.4.9/page_protocol_basic_packets.html), and the tested [connector commit implementation](https://github.com/mysql-net/MySqlConnector/blob/2.6.2/src/MySqlConnector/MySqlTransaction.cs) sends COMMIT and waits for its native result.

The [managed transaction regression](../../../../src/DataLinq.Tests.MySql/NativeAsyncCommitAcknowledgementTests.cs) performs an actual tracked update, holds the server's OK before the driver receives it, and verifies the persisted value through an independent direct connection while managed commit is still pending. The relay then closes the sockets without forwarding the acknowledgement. No provider wrapper manufactures success or an exception. All six servers produce the expected native failure and `Completion.Unknown`; the transaction denies business reuse/commit, invalidates the mutable baseline, preserves uncertainty after disposal and evicts the old cached value. A new one-slot-pool lookup reads the actual committed value, with exactly one observed COMMIT and no replay.

Focused acknowledgement-loss tests pass **6/6**. The full provider-specific run passes **1,005/1,007**, including all six new cases, with two failures in the unchanged `NativeAsyncTransactionTests.FirstUseAndCompletionCanSwitchBetweenSyncAndAsync` on MySQL 8.4/9.7. Both failures occur during pooled-session reauthentication in `TryResetConnectionAsync` / `SwitchAuthenticationAsync` and exhaust the existing five-second connection budget. The unchanged transaction class then passes **19/19** in isolation on those targets. This is a diagnostic control, not a replacement for the failed broad run or proof of its cause. No connection timeout, pooling setting, product behavior or assertion was changed to dismiss it. The broad failure remains open for investigation and final-candidate verification.

The full run is `20260924T022513641Z-83888ef72b6e448db470ea28073b105f`; the focused transaction control is `20260924T022713033Z-16706a6b506847008a6c6d99dd7f7a9c`. Both use stable working changes on `4996e013` and a matching dirty-source runner; **ValidForEvidence=false**. The earlier six-case acknowledgement run used the older `02559d89` runner. The new fixture builds with zero warnings/errors and changes no production implementation.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-native-commit-acknowledgement.json` | Native post-commit acknowledgement loss, 6/6 | `2aa16e34e6e1886a235f1c6efa4e6a25b2c72fc6a3fd5f8028d2c8516079eff7` |
| `w2-native-commit-acknowledgement-mysql.json` | Full provider suite, 1,005 passed / 2 existing MySQL reauthentication timeouts | `926a95525d1d9acdd55e9f870d52f7c0e6d4dc2686503341ae0ebe606142d833` |
| `w2-native-commit-acknowledgement-transaction-control.json` | Unchanged transaction class on MySQL 8.4/9.7, 19/19 | `7cbb1318fb6817986185353bf16b9e7505a5acdd57dd414daa4ff0cbb7ee8888` |

Native server post-commit acknowledgement loss now has direct evidence. Ordinary-read interruption/integrity, recovery-budget expiry, native cleanup-failure combinations, the two broad-run reauthentication failures, corrected SQLite adoption and final parity/performance/telemetry and clean-candidate acceptance remain open.

[CI run #619](https://github.com/bazer/DataLinq/actions/runs/35947472343) on acknowledgement-loss commit `01e13bd6` passed.

### W2.4 Cooperative Native Recovery Budget

The [native recovery-budget fixture](../../../../src/DataLinq.Tests.MySql/NativeAsyncRecoveryBudgetTests.cs) now covers all six servers. The renamed [completion relay](../../../../src/DataLinq.Tests.MySql/NativeCompletionProxy.cs) can hold either COMMIT or ROLLBACK and then forward or lose the actual successful response. After a tracked update and callback cancellation, a controlled TimeProvider fires the real recovery CTS timer only after the server has completed rollback and its response is held. The SQL driver, cancellation handling and cleanup remain native. Each target runs four combinations: budget live/expired and response forwarded/lost.

Expiry does not release the private operation owner or its connection. Overlapping disposal rejects and an unrelated operation remains blocked on the explicitly enabled one-slot pool until canceled. Delivering the late OK preserves confirmed rollback; losing it preserves Unknown completion with the native rollback failure as a secondary. The original callback cancellation instance/token survives every combination, the separate recovery timer is disposed, mutable state is invalidated, and a subsequent pooled lookup reads the original value. This proves cooperative waiting, not a hard wall-clock cleanup deadline. It does not yet establish native cleanup-failure combinations or SQLite's corrected-package acceptance.

The focused fixture passes **6/6** (24 scenarios). The first full provider run passes **1,010/1,013**. Besides two pooled-session reset timeouts in the unchanged mixed sync/async transaction test on MariaDB 11.8/12.3, the MariaDB 12.3 fallback test times out reading the catalog before its intended SHOW CREATE VIEW failure, so its identity assertion correctly fails. These join the earlier MySQL reset failures; no product policy, timeout, pooling setting or assertion was relaxed.

A one-variable concurrency control runs the same full source/suite with `--maximum-parallel-tests 8`, matching the existing named full plan's server-suite limit, and passes **1,013/1,013**, zero skips. TRX inspection includes all six recovery cases and all six existing commit-acknowledgement cases. The failed run used automatic concurrency (its first batch reports effective concurrency 20.84); the control bounds concurrent test scheduling, without removing any target or case. This supports contention as a hypothesis, not a proven diagnosis of the reset/catalog timeouts. The failed receipts remain authoritative observations and final-candidate verification is still required.

The full failure run is `20260924T024010040Z-0681992fe2684ce5872b06a8834932be`; the concurrency control is `20260924T024223849Z-23f337fb22bc4d0db00c89e287f615ce`. Both use stable working changes on `01e13bd6` and a matching dirty-source runner; **ValidForEvidence=false**. The focused run used the older runner. Test and runner builds pass with zero warnings/errors. An initial CLI invocation omitted `--configuration Release` and was correctly rejected as stale Debug output before running tests.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-native-recovery-budget-initial.json` | Native cooperative recovery, 6/6 | `3804504905b5029f8fb8c5ba134d25c751d6c324b5099fe997156e48b01bcb15` |
| `w2-native-recovery-budget-mysql.json` | Automatic concurrency, 1,010 passed / 3 timeout-related failures | `b9353bfe7119b466e95d11224e4e588a26b32091bbe275b97cfcba554c5f81b6` |
| `w2-native-recovery-budget-mysql-concurrency-control.json` | Eight concurrent tests, same full suite, 1,013/1,013 | `7057547a218930dc638a0d27e1847dfd7f16d0d44490cb2b55b51cfeaee14414` |

Ordinary-read interruption/integrity, native cleanup-failure combinations, the unexplained broad-run timeouts, corrected SQLite adoption and final parity/performance/telemetry and clean-candidate acceptance remain open.

### W2.4 / W2.6 Native Cleanup Failure And Interrupted Reads

The [recovery fixture](../../../../src/DataLinq.Tests.MySql/NativeAsyncRecoveryBudgetTests.cs) now expires the recovery token before driver entry, after either an actual duplicate-key command error or callback cancellation. MySqlConnector's subsequent disposal performs its own implicit rollback with no request token. The relay holds that real response and then forwards or loses it: four additional scenarios per server. The helper retains the exact original error, then the independent recovery cancellation, then any native disposal error in occurrence order. While disposal is pending, its connection remains unavailable to the one-slot pool and overlapping disposal rejects. Independent connection cleanup closes the handle even after transaction disposal fails; subsequent pooled queries succeed and observe the rolled-back data. Successful implicit disposal does not retroactively establish managed rollback confirmation. The driver implementation and its implicit rollback are documented in [MySqlConnector 2.6.2](https://github.com/mysql-net/MySqlConnector/blob/2.6.2/src/MySqlConnector/MySqlTransaction.cs).

All six cases in the first run fail because the retained native cleanup exception has Cause.Unknown. [Native resource cleanup](../../../../src/DataLinq.MySql/Shared/SqlDatabaseTransaction.Resource.cs) now reports its provider/timeout cause at the disposal boundary, for synchronous and asynchronous transaction/connection cleanup. It rejects stale occurrence reports, preserves any more-specific current report and nested failures, and leaves completion/recovery decisions with the outer coordinator. Original exception identity, once-only attempts and independent cleanup remain intact. The corrected recovery class passes **12/12** (48 combined scenarios).

The [server read fixture](../../../../src/DataLinq.Tests.MySql/NativeAsyncReadInterruptionTests.cs) blocks an ordinary generated key-lookup SELECT using a separate connection's table lock. It observes that exact connection's SELECT in PROCESSLIST before caller cancellation or soft/hard command timeout. No locking clause, stored function or raw business query is substituted for the generated read. Pre-dispatch cancellation leaves the transaction reusable. After dispatch, soft interruption retains rollback/disposal and hard interruption leaves disposal only; a surviving open connection alone does not restore business access or commit. Cleanup releases the reader and a new pooled lookup returns a complete value. All six targets pass the three interruption modes.

The [reader-lifetime compliance fixture](../../../../src/DataLinq.Tests.Compliance/Query/NativeAsyncReadLifetimeTests.cs) cancels between two generated rows on all eight targets. It verifies original cancellation/token and Query identity, clean reader settlement, recovery-only transaction access, rollback and subsequent complete root lookups. SQLite file/memory use the same assertions; this proves between-row cancellation, not interruptibility inside SQLite's synchronous native call. The adapters continue to make no positive integrity claim for a dispatched interrupted read. These tests exercise the conservative accepted policy rather than enabling Continue based on socket state.

Final full Release provider-specific tests pass **1,025/1,025** and compliance passes **3,832/3,832**, zero skips. Both use the existing full plan's eight-concurrent-test limit. TRX inspection confirms all 24 server executions of recovery, cleanup, read interruption and commit-loss cases, plus all eight new reader-lifetime cases. The final source includes a reporting refinement to reuse the common cleanup collector, preserving nested reports; the earlier 1,025-case server run preceded that refinement and is retained separately. Core/MySQL provider builds pass on .NET 8/9/10 without warnings/errors.

Final provider run: `20260924T030519761Z-0884d58ab35f4ae681ba8e17a7d3c61e`; final compliance: `20260924T025936628Z-992a0be4bf4c45e9960c322cc7a81f21`. Both use stable working changes on `77428dd2` and a matching dirty-source runner; **ValidForEvidence=false**. Earlier focused runs used the older runner. These passing runs retain the prior timeout observations and do not establish their precise cause or final W2 acceptance.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-native-cleanup-initial.json` | Six failures exposing Unknown cleanup cause | `26ba127314aca81b5b8a3ef0ddf91bc8e8915c76d595db18aff3eb3ce9bc1d52` |
| `w2-native-cleanup-classification.json` | Corrected recovery/cleanup class, 12/12 | `925570b9876cf9faa3629645d91b6ba2256e133b13e5815f4c9a57e9af06c62d` |
| `w2-native-read-interruption.json` | Generated read interruption, 6/6 | `5d3530394c15b9c39193bd6ba7d195b1deed177e2cc54c6be623864dbfd57713` |
| `w2-native-read-lifetime.json` | Between-row cancellation, 8/8 | `ddcbced6847237f29d00a047b451015bd08cd470999b5e006b9803df704a06a5` |
| `w2-native-read-cleanup-mysql.json` | Before final collector refinement, 1,025/1,025 | `17c318aa45f14338b9e08a5b8237bcf9841cc417656c8e6d315c13f4b4e214d1` |
| `w2-native-read-cleanup-final-mysql.json` | Final native cleanup source, 1,025/1,025 | `c53c4cb244fe4e0dd9473f9b9c80e48da8eafe0f46bcfa21624bb54d49f478f9` |
| `w2-native-read-cleanup-final-compliance.json` | Final source, all-target compliance, 3,832/3,832 | `efcf0e48bdea53ce324d27c8d5b88f8316c5cf733c33e278154beb81d8c2943c` |

Remaining work includes SQLite native cleanup-failure/certainty checks, final per-operation reconciliation, the unexplained broad-run timeout observations, corrected official SQLite adoption and affected reruns, and final parity/performance/telemetry and clean-candidate acceptance. Native server evidence does not substitute for those requirements.

### Finished-Iterator Allocation Measurement After CI #620

[CI run #620](https://github.com/bazer/DataLinq/actions/runs/35948572848) on recovery-budget commit `77428dd2` passes every provider lane but fails the unit lane. The unchanged `CompletedGuardedEnumerableTests.FinishedCalls_DoNotAllocateDiagnosticScopes(false)` observes **5,856 bytes**, expected zero. The original [job](https://github.com/bazer/DataLinq/actions/runs/35948572848/job/107471939043) artifact is retained as `artifacts/w2-recovery-budget-ci-unit-failure.zip`, SHA-256 `93133a61e54ff0663c477cf39cdc4adf1bc9ec7071981dd9db60df1a6d53c4e1`. Its unchanged local class control passes **7/7**. That control does not identify or dismiss the CI allocation.

The [measurement](../../../../src/DataLinq.Tests.Unit/Core/CompletedGuardedEnumerableTests.cs) now warms and measures the same synchronous helper entirely outside the async test state machine. As with the earlier helper-drained disposal measurement, NoInlining/NoOptimization applies only to the scaffold; [runtime tiering eligibility](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/vm/method.cpp#L2672-L2705) excludes that helper. Production iterator/gate code and compilation are unchanged. Both exhausted and never-started disposed iterators still require **zero bytes across all 10,000 move/dispose pairs**, alongside unchanged call counts, scope restoration and overlap rejection. A new intentionally allocating iterator must produce at least its 640,000 payload bytes through the same helper. There is no minimum-of-retries measurement or relaxed threshold. The exact source of the original Linux allocation remains unproven; the next CI run must verify this changed scaffold there.

The full Release unit suite passes **3,957/3,957**, zero failures/skips, including all eight finished-iterator cases. Final run `20260924T030427716Z-dec98c511143403fbeb1e772eaed35ca` includes the final native-cleanup collector refinement from the preceding section; the earlier full run preceded that refinement. Both use stable working changes on `77428dd2` and matching dirty-source runners, **ValidForEvidence=false**. These checks do not replace final W2 performance evidence.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-finished-calls-allocation-control.json` | Unchanged class control, 7/7 | `68c9d26c69503673c412d6d694b3d1fadead79bf12ced97b2063c5bfe54b9352` |
| `w2-native-read-cleanup-unit.json` | Isolated scaffold and allocating control, 3,957/3,957 | `bd2b6482d6e5586265a2bd2f8f62d5ba84cee24885bb48d809fe471d9cbde55c` |
| `w2-native-read-cleanup-final-unit.json` | Final working product/test source, 3,957/3,957 | `531a00b168970a6a5fe462318005bfa8b95d19f9fba9e9b567971a5f6ad38bc6` |

### W2.4 / W2.6 SQLite Native Cleanup And Failed Rollback Reuse

[CI #621](https://github.com/bazer/DataLinq/actions/runs/35950311052) passes all lanes on `9dea07a2`, including the revised finished-iterator measurement. This verifies the changed scaffold on Linux; it does not establish the precise source of the earlier 5,856-byte observation.

[Four native SQLite TUnit cases](../../../../src/DataLinq.Tests.Unit/SQLite/SQLiteNativeAsyncRecoveryTests.cs) cover six file/memory scenarios. A real engine authorizer rejects only the selected transaction command. A rejected commit retains Unknown certainty after successful automatic rollback and closes without publishing the attempted write. The other cases preserve either the original duplicate-key error or exact caller cancellation through an expired independent recovery token, followed by a real native error in the driver's implicit disposal rollback. They check ordered recovery/cleanup failures, provider/transaction identity, terminal recovery, independent connection disposal and once-only attempts. The timer expires before native recovery entry; this is not evidence of interruptible SQLite I/O or a hard wall-clock deadline.

The first run passes both commit-rejection cases and fails both disposal cases because native cleanup Cause is Unknown. [SQLite resource disposal](../../../../src/DataLinq.SQLite/SQLiteDatabaseTransaction.Resource.cs) now reports its provider cause at the native boundary, using the same current-occurrence/nested-cleanup rules as the shared server resource. The corrected class passes **4/4**. Full Release unit passes **3,961/3,961** and SQLite file/memory compliance passes **949/949**, zero failures/skips. SQLite/core builds pass on .NET 8/9/10 without warnings/errors. A subsequent fixture-comment correction clarifies that the driver ignores `Pooling=true` for memory mode; executable test/product source is unchanged from these runs.

That diagnostic correction does not resolve the separate native pool finding. The standalone [published-driver probe and report](SQLite%20Failed%20Rollback%20Pool%20Reuse%20Investigation.md) reproduce an active transaction crossing file-pool checkouts after rollback rejection, for both synchronous and asynchronous disposal. It uses no DataLinq runtime. ADO Closed/null transaction state is therefore not treated as safe native reuse. Memory-mode controls physically close their handle because the driver disables pooling there. Test pool clearing occurs only after observations during isolated teardown. No production pooling change, retry, dependency replacement or upstream submission is included.

Local test receipts identify stable working changes on `9dea07a2`, matching dirty-source runner assemblies and **ValidForEvidence=false**. Focused run: `20260924T032422421Z-d4eae03794ac4954b684260e570e3ebc`; full unit: `20260924T032658525Z-b5119da6373d4202b961551b2a6096aa`; SQLite compliance: `20260924T032748865Z-8e113bce92914bb6a6a3e90d1d489eff`. Commands use the compiled Release Testing CLI with `run --configuration Release --no-build`, unit concurrency 16 and compliance concurrency 8. The focused filter is `/*/*/SQLiteNativeAsyncRecoveryTests/*`; compliance selects `sqlite-file,sqlite-memory`.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-sqlite-native-recovery-initial.json` | 2 passed / 2 expected classification failures; raw log also retains the initial pool-state observations | `07bce6540472c49d06aec40783006d90939d42454a312a72a40c9545d17ecb1c` |
| `w2-sqlite-native-recovery-classification.json` | Corrected native class, 4/4 | `3a808783b2d8a5b4c2598b73ad10230b0ff53ec772bd1c87e05d06c23ecbe67f` |
| `w2-sqlite-native-recovery-unit.json` | Full unit, 3,961/3,961 | `34a6949b2e9575d61f07c7b0cdda55b0e9819265bf926c36e1b0a8a111ab817b` |
| `w2-sqlite-native-recovery-compliance.json` | File/memory compliance, 949/949 | `834fe7250ba9baa128d32795844b2e7c5d46b331790d554a333d7c4721adb107` |

Final per-operation reconciliation, the retained unexplained broad-run timeouts, failed-rollback file-pool integrity, official dependency adoption and affected reruns, clean-candidate parity, performance and telemetry review remain open. Native cleanup diagnostics are verified within this bounded fault model; W2 is not complete.

### Operation Audit And Native Telemetry Parity

[CI #622](https://github.com/bazer/DataLinq/actions/runs/35951770320) passes all lanes on `fe70d481`. A clean local Release `run --plan full` passes **8,531/8,531**, zero failures/skips: generators 71, unit 3,961, Memory 221, compliance 3,403 and server-specific 875. Named-plan provider affinity runs invariant cases only in the anchor batch, so these counts differ from earlier ad hoc runs that repeated invariant cases. All eight targets are present, checkout/runner assemblies match `fe70d481`, and the checkout stays clean. **ValidForEvidence=false** remains explicit: default batch size two does not satisfy the report's individual-provider-total gate. The final receipt must use `--plan full --batch-size 1`, preserving the standard per-suite worker budgets.

The [operation audit](W2%20Native%20Provider%20Audit.md) accounts for each W0/W1 native handoff. It distinguishes actual driver failures from controllable W1 combinations, calls out native reader-drain error suppression, and retains the separate SQLite failed-rollback integrity gate. It does not close public/generated W3 work or corrected-package acceptance.

The new [native telemetry parity test](../../../../src/DataLinq.Tests.Compliance/Query/NativeAsyncTelemetryParityTests.cs) passes **8/8** across all targets. Identical sync/async workloads compare actual rows and cached identity, command/query/transaction/mutation/cache snapshot deltas, ten counter/histogram instruments and activity kinds/status/tags. The workload includes committed and rolled-back updates, convenience insert/delete and a real native SQL error. Explicit nonempty counts prevent two absent instrumentation paths from passing. Unique traces and database names isolate parallel observers without clearing global metrics; elapsed durations and unique database-name values are the only normalized differences.

Both focused probes pass 8/8. The final version explicitly starts a unique trace rather than inheriting a possibly shared test-runner trace. Its run is `20260924T034858386Z-7b7b6d0cc7ac449fae711d1331168c7f`; it uses stable working test changes on `fe70d481` and the reused same-commit clean runner, **ValidForEvidence=false**. Release compliance builds pass without warnings/errors. No production code changes are required by this parity check.

| Local summary under `artifacts/` | Scope | SHA-256 |
| --- | --- | --- |
| `w2-fe70d481-full-release.json` | Clean full plan, 8,531/8,531; paired provider batches | `2849b2d601207bba1ee5fc8ab31f2f3c999f45d843dbfb3561d115129b6ae7de` |
| `w2-native-telemetry-parity-initial.json` | Initial telemetry parity, 8/8 | `4548e5474beea36f759d72c6dbba746f411a9b7c6694aa36c76b15f432bda08e` |
| `w2-native-telemetry-parity-final.json` | Final isolated-trace parity, 8/8 | `23e9562677615d086c05dc8101f534ed8e6b23c41bbc6305a027bdca6cf2d513` |

Reproduction: build `src/DataLinq.Tests.Compliance/DataLinq.Tests.Compliance.csproj -c Release`, then use the compiled Release Testing CLI with `run --suite compliance --alias all --configuration Release --no-build --maximum-parallel-tests 8 --filter '/*/*/NativeAsyncTelemetryParityTests/*' --output failures --summary-json <fresh-artifact-path>`. Set `DATALINQ_TEST_DB_HOST=127.0.0.1` for sandboxed server execution.

Operation reconciliation and representative native telemetry parity are now recorded. Clean final candidate evidence, six-lane performance review, the retained unexplained observations, official SQLite ownership-fix adoption/reruns and the distinct failed-rollback pool-integrity disposition remain open.

### Clean Candidate And Performance Checkpoint

Candidate `49176d7a` passes **8,539/8,539** in the clean Release full plan with `--batch-size 1`, zero failures/skips, individual provider totals and **ValidForEvidence=true**. [CI #623](https://github.com/bazer/DataLinq/actions/runs/35953248630) passes every lane on the same commit. Independent TRX review confirms all eight native telemetry cases; the checkpoint records the summary hash and 69 artifact hashes.

All six canonical lanes are captured: **90 rows, 126 raw receipts**, clean matching source/runner/target provenance and valid comparisons against frozen W0. Operation counts and normalized telemetry also match W1. The initial comparison retains **13 allocation warnings, 20 latency warnings and 12 noisy latency rows**. The warning identities match W1, but synchronous mutation allocation has increased further: roughly 8 KB per CRUD workload and 2.8 KB per employee update. Those increments still need attribution/disposition under D10-6.

A fixed four-run timing control adds **30 rows and 54 verified raw receipts**, using the original W1 runtime and current W2 runtime in sequence for hot paths and Memory reads. Fresh W1 itself reports timing warnings against the original W1 capture. Fresh W2 Memory results are all stable against fresh W1; every fresh hot-path timing comparison is noisy and therefore inconclusive. These controls neither replace the initial results nor close unresolved timing review elsewhere.

The [standalone generated-getter probe](evidence/binary-getter/Program.cs) confirms the binary ownership correction's cost: 56, 4,120 and 65,560 bytes per read for nonempty 32-, 4,096- and 65,536-byte payloads. W1's warmed getter allocated zero by exposing the same mutable array; W2 returns independent arrays and preserves subsequent values after caller edits. This is supplemental thread-allocation evidence, not a canonical latency claim.

The [checkpoint](W2%20Functional%20And%20Performance%20Checkpoint.md) preserves every original row, hashes, controls, measurement limits and reproduction commands. **W2 remains in progress:** performance attribution/disposition, official SQLite ownership-fix adoption and affected reruns, the separate failed-rollback file-pool finding and retained unexplained native/CI observations remain explicit.
