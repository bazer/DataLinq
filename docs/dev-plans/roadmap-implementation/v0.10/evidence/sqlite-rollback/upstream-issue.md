# Microsoft.Data.Sqlite can pool an active transaction after rollback failure

**Submitted:** [dotnet/efcore#39082](https://github.com/dotnet/efcore/issues/39082), 2026-09-24.

**Closed as completed, verified 2026-09-28:** closed on **2026-09-26 at 01:44:31 UTC**, following the merge of [PR #39083](https://github.com/dotnet/efcore/pull/39083) as [`7b0248934f14bbb2159310b224f669c76eb07946`](https://github.com/dotnet/efcore/commit/7b0248934f14bbb2159310b224f669c76eb07946). The issue targets [12.0-preview1](https://github.com/dotnet/efcore/milestone/238), not a confirmed 10.0.x backport. DataLinq adoption and affected verification remain open.

The original submitted body follows.

### Bug description

If SQLite rejects ROLLBACK during transaction disposal, Microsoft.Data.Sqlite completes and detaches its managed transaction wrapper anyway. Closing the outer connection can then return a native handle with an active transaction to the file pool. The next borrower sees the previous transaction's uncommitted row and cannot begin a new transaction.

Reproduced directly with published Microsoft.Data.Sqlite **10.0.11 and 10.0.12**, and with unchanged upstream main at `cca217cc2d529b8083e804f7334db552f744f536`. No EF Core/DataLinq runtime code, concurrent operations, reflection, forced GC or replacement driver binaries are used for the published-package reproduction.

The reproduction uses SQLite's authorizer to reject only `SQLITE_TRANSACTION` / `ROLLBACK` before execution. The native engine raises error 23. This deterministically exercises native rollback failure; it does not establish how often production I/O failures produce the same state.

### Reproduction

Create the following two files in an empty directory and run `dotnet run -c Release`. Each database starts with committed row 1; a transaction inserts row 2. The program permits or rejects its disposal rollback, disposes the outer connection, and inspects the next checkout using the same connection string. It runs all eight file/memory, permit/deny and sync/async disposal combinations.

<details>
<summary>Probe.csproj and complete Program.cs</summary>

`Probe.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <UseAppHost>false</UseAppHost>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.12" />
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.5" />
  </ItemGroup>
</Project>
```

`Program.cs`:

```csharp
using System;
using System.Data;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SQLitePCL;

foreach (var memory in new[] { false, true })
foreach (var denyRollback in new[] { false, true })
foreach (var asyncDispose in new[] { false, true })
{
    var name = "rollback_denial_" + Guid.NewGuid().ToString("N");
    var path = Path.Combine(Path.GetTempPath(), name + ".db");
    var connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = memory ? name : path,
        Mode = memory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
        Cache = memory ? SqliteCacheMode.Shared : SqliteCacheMode.Private,
        Pooling = true
    }.ConnectionString;
    using var keeper = new SqliteConnection(connectionString);
    using var connection = new SqliteConnection(connectionString);
    using var next = new SqliteConnection(connectionString);
    try
    {
        if (memory) keeper.Open();
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE items (id INTEGER PRIMARY KEY); INSERT INTO items VALUES (1)";
            command.ExecuteNonQuery();
        }
        var transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO items VALUES (2)";
            command.ExecuteNonQuery();
        }
        var handle = connection.Handle!;
        var rollbackAttempts = 0;
        strdelegate_authorizer authorizer = (_, action, first, _, _, _) =>
        {
            if (action != raw.SQLITE_TRANSACTION || first != "ROLLBACK") return raw.SQLITE_OK;
            rollbackAttempts++;
            return denyRollback ? raw.SQLITE_DENY : raw.SQLITE_OK;
        };
        SqliteException.ThrowExceptionForRC(raw.sqlite3_set_authorizer(handle, authorizer, null), handle);
        int? disposeErrorCode = null;
        try
        {
            if (asyncDispose) await transaction.DisposeAsync();
            else transaction.Dispose();
        }
        catch (SqliteException failure) { disposeErrorCode = failure.SqliteErrorCode; }
        var driverTransactionCompleted = transaction.Connection is null;
        if (asyncDispose) await connection.DisposeAsync();
        else connection.Dispose();
        var originalRollbackAttempts = rollbackAttempts;
        next.Open();
        var autocommit = raw.sqlite3_get_autocommit(next.Handle!);
        long count;
        using (var command = next.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM items";
            count = (long)command.ExecuteScalar()!;
        }
        string? beginFailure = null;
        try { using var freshTransaction = next.BeginTransaction(IsolationLevel.Serializable, deferred: true); }
        catch (SqliteException failure) { beginFailure = failure.Message; }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            memory, denyRollback, asyncDispose, driver = typeof(SqliteConnection).Assembly.FullName,
            raw = typeof(raw).Assembly.FullName, disposeErrorCode, driverTransactionCompleted,
            connectionState = connection.State.ToString(), sameHandle = ReferenceEquals(handle, next.Handle),
            autocommit, visibleRows = count, beginFailure, originalRollbackAttempts
        }));
    }
    finally
    {
        // Only test teardown. Observe the pooled checkout before clearing its pool.
        SqliteConnection.ClearPool(connection);
        next.Dispose();
        connection.Dispose();
        keeper.Dispose();
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
    }
}
```

</details>

### Actual result

Both disposal forms produce the same paired results:

| Database / rollback policy | Disposal error | Same handle on next checkout | Autocommit | Visible rows | New transaction |
| --- | --- | --- | ---: | ---: | --- |
| File / permit | None | Yes | 1 | 1 | Succeeds |
| File / deny | SQLite 23 | **Yes** | **0** | **2** | **Fails** |
| Shared memory / permit | None | No | 1 | 1 | Succeeds |
| Shared memory / deny | SQLite 23 | No | 1 | 1 | Succeeds |

After rejected file rollback, the next `BeginTransaction` fails with:

```text
SQLite Error 1: 'cannot start a transaction within a transaction'.
```

The original ADO.NET connection is Closed and `SqliteTransaction.Connection` is null, but `sqlite3_get_autocommit` on the next checkout is zero. Those managed states do not establish native transaction settlement.

Memory mode is a physical-close control: the driver disables pooling for it even when the connection string requests pooling. The reproduction clears its pool only during final teardown, after observing the next checkout.

### Expected result

A handle with an unsettled native transaction must not be lent to another pool owner. Preserve the original rollback exception, but establish a safe native state before pool return or discard that physical connection.

### Source observation and proposed correction

[RollbackInternal / Complete](https://github.com/dotnet/efcore/blob/v10.0.12/src/Microsoft.Data.Sqlite.Core/SqliteTransaction.cs) clears the managed transaction association in a finally block even when ROLLBACK fails. Subsequent [connection close](https://github.com/dotnet/efcore/blob/v10.0.12/src/Microsoft.Data.Sqlite.Core/SqliteConnection.cs) and [pool return](https://github.com/dotnet/efcore/blob/v10.0.12/src/Microsoft.Data.Sqlite.Core/SqliteConnectionPool.cs) allow the observed native transaction to cross checkouts.

PR #39083 contains the correction and regression tests.

The patch checks native autocommit during deactivation and marks an unsettled handle non-poolable. The existing pool-return path then disposes only that connection. Healthy connections, including one successfully recovered by the caller before close, remain reusable. It adds no public API, rollback retry or pool-wide clearing.

Validation against the upstream base above:

- Ten regression/control cases: five fail on unchanged production code; all ten pass with the correction using the identical test DLL.
- Full bundled Microsoft.Data.Sqlite.Tests application: **724 passed, seven existing skips, zero failures**, using the normal `category=failing` exclusion. Six skips reference #35585; one references ericsink/SQLitePCL.raw#421.
- The unchanged standalone eight-case probe confirms that corrected file/deny checkouts see only committed data and can begin a transaction, while preserving original error 23.
- Builds complete with zero warnings/errors. This is Windows x64 coverage for the bundled SQLite driver, not the entire EF Core or alternate-native-library matrix.

This differs from #39008 / #39009: there is only one application owner at a time, and the issue is native transaction state surviving pool return. The ownership publication correction does not resolve it.

### Environment

- Microsoft.Data.Sqlite: published **10.0.11 and 10.0.12**
- SQLitePCLRaw.bundle_e_sqlite3: **3.0.5**
- Standalone reproduction: .NET **10.0.12**, Windows x64
- Upstream validation: pinned SDK **11.0.100-rc.1.26425.128**, net11.0 test application, net10.0 driver

### AI disclosure

The issue was found and the fix, regression tests, and PR were developed with GPT-6 Astra on Extra High, with the repository owner's authorization.
