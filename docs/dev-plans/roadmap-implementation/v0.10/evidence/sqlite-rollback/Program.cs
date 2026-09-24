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
