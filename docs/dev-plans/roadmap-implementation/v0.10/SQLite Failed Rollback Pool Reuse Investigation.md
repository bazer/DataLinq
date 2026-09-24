> [!WARNING]
> Open native cleanup finding from W2. The DataLinq diagnostic correction does not repair this driver behavior or establish safe pool reuse after a failed SQLite rollback.

# SQLite Failed Rollback Pool Reuse Investigation

**Recorded:** 2026-09-24, Microsoft.Data.Sqlite 10.0.11 and independently 10.0.12, with SQLitePCLRaw.bundle_e_sqlite3 3.0.5, Windows x64 / .NET 10.0.12.

## Finding And Boundaries

When SQLite rejects the rollback issued by transaction disposal, Microsoft.Data.Sqlite clears its managed transaction association anyway. Closing that outer connection can return a native handle with an active transaction to the file pool. The next checkout sees the previous transaction's uncommitted row and cannot start a new transaction. This is reproduced directly against the published driver, without DataLinq, concurrent operations, reflection, forced collection or replacement driver binaries.

The fault is controlled: an SQLite authorizer rejects only `SQLITE_TRANSACTION` / `ROLLBACK` at statement preparation. The callback observes the command and returns `SQLITE_DENY`; it never modifies its connection or manufactures an exception. This proves handling of that native rejection. It does not establish the frequency of such a failure in normal applications or prove that every SQLite I/O error has the same outcome. See SQLite's [authorizer contract](https://www.sqlite.org/c3ref/set_authorizer.html).

This differs from the [concurrent pool ownership finding](SQLite%20Pool%20Ownership%20Investigation.md): only one application connection uses the handle at a time, and the driver transaction has already been detached. The earlier activation-order correction is not evidence that this rollback path is corrected. Official-package adoption and retesting remain necessary; no new upstream issue or fix has been submitted by this investigation.

## Reproduction

The complete standalone [project](evidence/sqlite-rollback/Probe.csproj) and [program](evidence/sqlite-rollback/Program.cs) default to published 10.0.11. The driver version can be overridden explicitly for servicing checks; this does not change DataLinq's production pin. From the DataLinq repository root:

```powershell
.\scripts\dotnet-sandbox.ps1 restore 'docs/dev-plans/roadmap-implementation/v0.10/evidence/sqlite-rollback/Probe.csproj'
.\scripts\dotnet-sandbox.ps1 run --project 'docs/dev-plans/roadmap-implementation/v0.10/evidence/sqlite-rollback/Probe.csproj' -c Release
```

Each isolated database starts with committed row 1. A transaction inserts row 2, then either permits or denies its disposal rollback. After disposing the transaction and connection, a new connection opens with the same connection string. The probe records native handle identity, engine autocommit, visible rows and whether a fresh transaction can begin. Pool clearing occurs only after those observations, in test teardown.

All four rows below run once with synchronous disposal and once with asynchronous disposal, producing eight observations with identical paired results:

| Database / rollback policy | Disposal error | Same native handle | Autocommit | Rows on next checkout | New transaction |
| --- | --- | --- | ---: | ---: | --- |
| File / permit | None | Yes | 1 | 1 | Succeeds |
| File / deny | SQLite 23, authorization denied | Yes | **0** | **2** | **Fails: cannot start a transaction within a transaction** |
| Shared memory / permit | None | No | 1 | 1 | Succeeds |
| Shared memory / deny | SQLite 23, authorization denied | No | 1 | 1 | Succeeds |

In every case the original outer connection is Closed and `SqliteTransaction.Connection` is null. Those facts are insufficient to establish native transaction settlement. SQLite [defines zero autocommit as an active transaction](https://www.sqlite.org/c3ref/get_autocommit.html). Although the connection string sets `Pooling=true`, the driver's [factory disables pooling for memory mode](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteConnectionFactory.cs#L61-L65). The memory controls demonstrate physical connection closure, not safe reuse of a failed pooled handle.

## Source Mechanism

In the pinned driver's [SqliteTransaction](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteTransaction.cs#L197-L228), `RollbackInternal` executes `Complete` from a finally block. That clears the connection's transaction, clears the transaction's connection and marks it completed even when native rollback fails. [SqliteConnection.Close](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteConnection.cs#L301-L362) then has no attached transaction to dispose, and its deactivation work does not reset an engine transaction. [Pool return](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteConnectionPool.cs#L77-L92) can put the handle back in the warm pool. These source paths explain the observed result; the published-package repro is the behavioral evidence.

## DataLinq Disposition

[Native recovery tests](../../../../src/DataLinq.Tests.Unit/SQLite/SQLiteNativeAsyncRecoveryTests.cs) now verify file/memory commit rejection and original-error preservation through an expired independent recovery budget plus actual native disposal failure. A separate DataLinq defect classified the disposal exception as Unknown. The resource boundary now records ProviderError while retaining current nested diagnostics, original exceptions, cleanup ordering and the outer coordinator's Unknown outcome. Once-only cleanup and connection disposal are still attempted independently.

Those four TUnit cases cover six scenarios. They deliberately do not assert that a Closed connection proves safe file-pool reuse. Their isolated fixture clears its pool only at teardown. The diagnostic fix must not be described as resolution of this investigation.

Safe pooled cleanup after this failure still requires an explicit disposition. The [accepted continuation](SQLite%20Pool%20Ownership%20Investigation.md#accepted-w2-continuation) does not authorize a pooling workaround, unofficial driver or automatic retry. This slice therefore changes no production pool policy and submits no external report. An upstream correction must ensure a handle with an unsettled transaction cannot be lent to another owner, preserve the original failure, and have a regression test for both direct rollback and disposal. Any proposed DataLinq mitigation needs separate review of its pool-wide effects and cost. W2's native cleanup acceptance remains open.

## Evidence Identity

The raw probe is independent of DataLinq runtime source. It ran on a working tree based on `9dea07a2`; it is a bounded diagnostic, not final W2 or corrected-dependency evidence. Local output: `artifacts/w2-sqlite-rollback-denial-probe/final-output.jsonl`. The preceding four-row async-only exploratory capture remains in `output.jsonl` in that directory.

| Artifact | SHA-256 |
| --- | --- |
| Final eight-row JSONL | `27468335cb0c06608e5488991a701ebde7ddd1123099e5a7d9ca66ef1c7b0ea1` |
| Original Probe.csproj at the 10.0.11 capture, before adding the version override | `a28d90ad7ad13195bfa22fa1af3e763ed48a3bb719272ae1522f4185fc7eb596` |
| Tracked Program.cs | `0ed01ce4875e91c97e07bf26c72744928a9230287338c3fbcfcdeff702a9afbc` |
| Published Microsoft.Data.Sqlite.dll | `4abd9c2a61e580eb853e93ca8953a3cef2c05714ae28d2d1859d4dbc5e5700bc` |

The raw output is local only; the standalone source and this report are tracked. No upstream submission or external evidence backup is claimed.

## Published 10.0.12 Confirmation

The same eight cases were run against the published **Microsoft.Data.Sqlite 10.0.12** package. The program is byte-for-byte identical to the original probe. All eight expected results agree with the table above, including both file/deny cases: native rollback error 23, detached transaction wrapper, Closed outer connection, reused handle, autocommit zero, two visible rows and failure to begin a new transaction. Both memory/deny controls physically close the original handle and start a new transaction successfully on the next connection.

The isolated project under `artifacts/w2-sqlite-rollback-10.0.12` restored the official package and built Release with zero warnings/errors. Its initial sandbox NuGet failure is retained in `restore.log`; the same restore succeeded with escalation. No replacement driver binary was used. The tracked project now supports the equivalent commands (restore and build verified):

```powershell
.\scripts\dotnet-sandbox.ps1 restore 'docs/dev-plans/roadmap-implementation/v0.10/evidence/sqlite-rollback/Probe.csproj' -p:MicrosoftDataSqliteVersion=10.0.12
.\scripts\dotnet-sandbox.ps1 run --project 'docs/dev-plans/roadmap-implementation/v0.10/evidence/sqlite-rollback/Probe.csproj' -c Release -p:MicrosoftDataSqliteVersion=10.0.12
```

Local receipt `artifacts/w2-sqlite-rollback-10.0.12/verification.json` verifies eight unique combinations, the expected native findings, identical program source and these artifact hashes:

| Artifact | SHA-256 |
| --- | --- |
| Eight-row `output.jsonl` | `6e129837b54ca9c91e9100a7aa528c29ea372ecb923f4869b2a0bce887cef73e` |
| Isolated 10.0.12 Probe.csproj | `6607041990e048bc4ad1df17bd76ac95db7cbefc30cdec21eddf41f3a8220ef2` |
| Published Microsoft.Data.Sqlite.dll | `b98d957462be432895b00dccca012d3c127e118d3d4262418014b92c7bbd66cf` |
| Published SQLitePCLRaw.core.dll | `b72c9dfe1479a568d6b84e249eaf949e76c757888f8056fb6ddb9c0480e0dd25` |

The [NuGet version index](https://api.nuget.org/v3-flatcontainer/microsoft.data.sqlite/index.json) listed 10.0.12 and no 10.0.13 when checked on 2026-09-24. The [10.0.12 transaction source](https://github.com/dotnet/efcore/blob/v10.0.12/src/Microsoft.Data.Sqlite.Core/SqliteTransaction.cs) retains the finally/Complete mechanism. This native reproduction confirms the separate rollback defect on that published version; it neither adopts 10.0.12 into DataLinq nor tests the unreleased 10.0.13 ownership fix. The [upstream issue draft](evidence/sqlite-rollback/upstream-issue.md) is ready for review and has not been submitted.
