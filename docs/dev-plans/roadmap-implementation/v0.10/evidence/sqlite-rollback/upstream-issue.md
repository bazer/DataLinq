# Microsoft.Data.Sqlite can return a connection with an active native transaction to the pool after rollback failure

Draft for review; not submitted.

## Reproduction

Verified with the published Microsoft.Data.Sqlite **10.0.11 and 10.0.12** packages and SQLitePCLRaw.bundle_e_sqlite3 **3.0.5**, on .NET 10.0.12 / Windows x64. The reproduction uses Microsoft.Data.Sqlite directly, without DataLinq runtime code.

The standalone [program](https://github.com/bazer/DataLinq/blob/codex/0.10-w2/docs/dev-plans/roadmap-implementation/v0.10/evidence/sqlite-rollback/Program.cs) and [project](https://github.com/bazer/DataLinq/blob/codex/0.10-w2/docs/dev-plans/roadmap-implementation/v0.10/evidence/sqlite-rollback/Probe.csproj) exercise eight cases: file versus named shared-memory databases, rollback permitted versus rejected by the native authorizer, and synchronous versus asynchronous disposal. The authorizer returns SQLITE_DENY only for the ROLLBACK transaction operation; the driver executes all actual statements and cleanup.

For the file case:

1. Open a connection with pooling enabled. Create a table and insert one committed row.
2. Begin a deferred Serializable transaction and insert a second row.
3. Set a native SQLite authorizer that rejects ROLLBACK.
4. Dispose the transaction and catch the resulting SQLite error 23.
5. Dispose the connection, then open another connection with the same connection string.
6. Inspect the native handle, `sqlite3_get_autocommit`, row count and ability to begin a new transaction.

## Actual result

Both disposal forms produce the same result. The old ADO.NET connection is Closed and the transaction's Connection property is null, yet the next checkout has the same native handle, autocommit is zero, the uncommitted second row is visible and a new transaction fails with "cannot start a transaction within a transaction".

| Database / native rollback policy | Same handle on next checkout | Autocommit | Visible rows | New transaction |
| --- | --- | ---: | ---: | --- |
| File / permit | yes | 1 | 1 | succeeds |
| File / deny | yes | 0 | 2 | fails |
| Shared memory / permit | no | 1 | 1 | succeeds |
| Shared memory / deny | no | 1 | 1 | succeeds |

Memory mode is a physical-close control: the driver disables pooling for it even when the connection string requests pooling. The reproduction calls ClearPool only at final teardown, after observing the next checkout.

## Expected result

A connection with an unsettled native transaction should not be lent to another pool owner. Preserve the original rollback exception, but either establish a safe native state before pool return or discard that particular native connection. Closed ADO.NET state and a detached transaction wrapper do not by themselves prove rollback settlement.

## Source observation and limits

In [RollbackInternal / Complete](https://github.com/dotnet/efcore/blob/v10.0.12/src/Microsoft.Data.Sqlite.Core/SqliteTransaction.cs), Complete executes from a finally block even when ROLLBACK fails. It clears the ADO.NET transaction association. Subsequent [connection close](https://github.com/dotnet/efcore/blob/v10.0.12/src/Microsoft.Data.Sqlite.Core/SqliteConnection.cs) and [pool return](https://github.com/dotnet/efcore/blob/v10.0.12/src/Microsoft.Data.Sqlite.Core/SqliteConnectionPool.cs) permit the observed native transaction to cross checkouts.

This is a deterministic native rollback-error injection. It does not establish how often production IO errors cause the same state. The existing pool-ownership correction scheduled for 10.0.13 concerns a different boundary and is not assumed to fix this finding.

The complete eight-case reproduction was also run against the published 10.0.12 package, with identical program source and all expected native observations confirmed. The 10.0.12 JSONL has SHA-256 `6e129837b54ca9c91e9100a7aa528c29ea372ecb923f4869b2a0bce887cef73e`; the loaded Microsoft.Data.Sqlite.dll has SHA-256 `b98d957462be432895b00dccca012d3c127e118d3d4262418014b92c7bbd66cf`. NuGet listed 10.0.12 and no 10.0.13 when checked on 2026-09-24. If submission occurs after another servicing release, rerun against that official version first.

The investigation and draft were prepared with AI assistance. The standalone reproduction and the recorded native observations are the evidence; no AI-generated explanation is offered as a substitute for those results.
