> [!WARNING]
> W2 is in progress. Internal native-provider implementation is not public async support, W0-F1 closure or release approval.

# W2 Native Provider Async Execution

**Started:** 2026-09-24, from `v0.10` commit `5d5def9a` after [W1 closeout](W1%20Closeout.md).

**Workflow:** one branch, `codex/0.10-w2`, and [draft PR #229, Implement W2: native provider async execution](https://github.com/bazer/DataLinq/pull/229), targeting `v0.10`. The [accepted workflow exception](Branch%20PR%20and%20Benchmark%20Workflow.md#w2-single-pr-exception) preserves coherent commits, incremental reviews and final merge-commit integration. No merge or publication is authorized by opening the PR.

## Scope And Acceptance

Bind W1's internal capabilities to the native providers, retain direct synchronous execution, and prove actual operation outcomes and owned-resource cleanup. MySQL and MariaDB share the implementation but require evidence against both server families. SQLite file and in-memory paths require explicit blocking, locking, cancellation and setup evidence; awaitable signatures do not establish interruptible native calls.

The [W1 I/O handoff](W1%20Closeout.md#f21-requirement-and-io-reconciliation), [W0 I/O map](W0%20IO%20Execution%20Map.md), [AAPI decisions](Async%20Public%20API%20Decisions.md) and [W2 exit gate](Implementation%20Order%20and%20Integration%20Plan.md#w2-native-provider-async-execution) remain authoritative. This plan breaks down that scope without reopening accepted policies.

## Milestones And Evidence Matrix

Each cell progresses separately through implementation and verification. A passing test with a controllable adapter cannot be recorded as native-provider proof.

| Milestone / required behavior | MySQL | MariaDB | SQLite file / memory |
| --- | --- | --- | --- |
| W2.1 Native standalone open, scalar/non-query dispatch, reader advancement and owned cleanup; validation/pre-cancellation; borrowed command ownership | Implemented internally; bounded native tests on 8.4/9.7 below | Implemented internally; bounded native tests on 10.11/11.4/11.8/12.3 below | Pending |
| W2.2 Lazy first-use transaction initialization, sync/async admission, private publication, partial-initialization cleanup and attachment | Implemented; native initialization checks below | Implemented; native initialization checks below | Pending; preserve deferred Serializable begin |
| W2.3 Captured query/key/relation/fluent integration, actual row conversion, complete/invalidation-safe cache publication and early reader cleanup | Basic integration verified; conversion/capture/invalidation matrix pending | Basic integration verified; conversion/capture/invalidation matrix pending | Pending |
| W2.4 Tracked mutations/private hydration, commit/rollback certainty, independent recovery budget, disposal and mixed execution | Basic mutation/completion/disposal bound; generated values and failure matrix pending | Basic mutation/completion/disposal bound; generated values and failure matrix pending | Pending |
| W2.5 Metadata parsers, existence/availability, per-command timeout, provisioning, journal mode, keeper and owning-root lifetimes | Bound; native metadata parity, timeout, cancellation and identity checks below | Bound; native metadata parity, timeout, cancellation and identity checks below | Pending |
| W2.6 Native interruption, soft/hard cancellation, timeout/connection trust and no-dispatch/cleanup classification; final parity and performance review | Pending | Pending | Pending official fix adoption and affected reruns |

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
