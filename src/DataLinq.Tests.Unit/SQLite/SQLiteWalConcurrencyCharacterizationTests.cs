using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Logging;
using DataLinq.Mutation;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.Unit.SQLite;

public sealed class SQLiteWalConcurrencyCharacterizationTests
{
    private static readonly DataLinqLoggingConfiguration NullLogging = DataLinqLoggingConfiguration.NullConfiguration;

    [Test]
    public async Task FileBackedWal_PrivateCacheOutsideRead_SeesCommittedInsertUpdateDeleteState()
    {
        using var database = WalDatabaseFixture.Create(SqliteCacheMode.Private);
        var outsideAccess = new SQLiteDbAccess(database.ConnectionString, NullLogging);
        using var transaction = new SQLiteDatabaseTransaction(
            database.ConnectionString,
            TransactionType.ReadAndWrite,
            NullLogging);

        await Assert.That(database.JournalMode).IsEqualTo("wal");
        await Assert.That(transaction.ExecuteNonQuery(
            "UPDATE wal_rows SET value = 'pending' WHERE id = 1; " +
            "DELETE FROM wal_rows WHERE id = 2; " +
            "INSERT INTO wal_rows (id, value) VALUES (3, 'inserted');"))
            .IsEqualTo(3);
        await Assert.That(transaction.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 1")).IsEqualTo("pending");
        await Assert.That(transaction.ExecuteScalar<long>("SELECT COUNT(*) FROM wal_rows WHERE id = 2")).IsEqualTo(0L);
        await Assert.That(transaction.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 3")).IsEqualTo("inserted");

        // SQLiteDbAccess opens a separate connection and bypasses DataLinq's row and relation caches.
        // The committed value here is therefore database-isolation evidence, not cache-scoping evidence.
        await Assert.That(outsideAccess.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 1")).IsEqualTo("committed");
        await Assert.That(outsideAccess.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 2")).IsEqualTo("delete-me");
        await Assert.That(outsideAccess.ExecuteScalar<long>("SELECT COUNT(*) FROM wal_rows WHERE id = 3")).IsEqualTo(0L);

        transaction.Commit();

        await Assert.That(outsideAccess.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 1")).IsEqualTo("pending");
        await Assert.That(outsideAccess.ExecuteScalar<long>("SELECT COUNT(*) FROM wal_rows WHERE id = 2")).IsEqualTo(0L);
        await Assert.That(outsideAccess.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 3")).IsEqualTo("inserted");
    }

    [Test]
    public async Task FileBackedWal_ExplicitSharedCacheOutsideRead_NeverReturnsPendingValue()
    {
        using var database = WalDatabaseFixture.Create(SqliteCacheMode.Shared);
        var outsideAccess = new SQLiteDbAccess(database.ConnectionString, NullLogging);
        using var transaction = new SQLiteDatabaseTransaction(
            database.ConnectionString,
            TransactionType.ReadAndWrite,
            NullLogging);

        await Assert.That(database.JournalMode).IsEqualTo("wal");
        await Assert.That(transaction.ExecuteNonQuery("UPDATE wal_rows SET value = 'pending' WHERE id = 1")).IsEqualTo(1);

        // Shared cache uses table locks when read_uncommitted is disabled. Depending on the
        // SQLite build, the outside reader can observe the last committed row or receive
        // SQLITE_LOCKED, but it must never receive the writer's pending value.
        string? outsideValue = null;
        SqliteException? lockFailure = null;
        try
        {
            outsideValue = outsideAccess.ExecuteScalar<string>(
                "SELECT value FROM wal_rows WHERE id = 1");
        }
        catch (SqliteException exception)
        {
            lockFailure = exception;
        }

        await Assert.That(outsideValue).IsNotEqualTo("pending");
        if (lockFailure is null)
        {
            await Assert.That(outsideValue).IsEqualTo("committed");
        }
        else
        {
            await Assert.That(lockFailure.SqliteErrorCode).IsEqualTo(6);
            await Assert.That(lockFailure.Message.Contains(
                "locked",
                StringComparison.OrdinalIgnoreCase)).IsTrue();
        }

        transaction.Rollback();

        await Assert.That(outsideAccess.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 1")).IsEqualTo("committed");
    }

    [Test]
    [NotInParallel]
    public async Task OwnedAccessPaths_ResetPooledReadUncommittedState()
    {
        using var database = WalDatabaseFixture.Create(
            SqliteCacheMode.Private,
            pooling: true);
        var access = new SQLiteDbAccess(database.ConnectionString, NullLogging);

        access.ExecuteNonQuery(
            "CREATE TABLE policy_observations (value INTEGER NOT NULL);");
        SetPooledReadUncommitted(database.ConnectionString, enabled: true);
        access.ExecuteNonQuery(
            "INSERT INTO policy_observations (value) " +
            "SELECT read_uncommitted FROM pragma_read_uncommitted;");

        await Assert.That(access.ExecuteScalar<long>(
            "SELECT value FROM policy_observations")).IsEqualTo(0L);

        SetPooledReadUncommitted(database.ConnectionString, enabled: true);
        await Assert.That(access.ExecuteScalar<long>(
            "PRAGMA read_uncommitted;")).IsEqualTo(0L);

        SetPooledReadUncommitted(database.ConnectionString, enabled: true);
        var readerValue = access
            .ReadReader("PRAGMA read_uncommitted;")
            .Select(reader => Convert.ToInt64(reader.GetValue(0)))
            .Single();
        await Assert.That(readerValue).IsEqualTo(0L);
    }

    [Test]
    [NotInParallel]
    public async Task OwnedTransaction_UsesSerializableCommittedVisibilityPolicy()
    {
        using var database = WalDatabaseFixture.Create(
            SqliteCacheMode.Private,
            pooling: true);
        SetPooledReadUncommitted(database.ConnectionString, enabled: true);

        using var transaction = new SQLiteDatabaseTransaction(
            database.ConnectionString,
            TransactionType.ReadAndWrite,
            NullLogging);

        await Assert.That(transaction.ExecuteScalar<long>(
            "PRAGMA read_uncommitted;")).IsEqualTo(0L);
        await Assert.That(transaction.DbTransaction).IsNotNull();
        await Assert.That(transaction.DbTransaction!.IsolationLevel)
            .IsEqualTo(System.Data.IsolationLevel.Serializable);

        transaction.Rollback();
    }

    [Test]
    public async Task AttachedTransaction_PreservesCallerReadUncommittedPolicy()
    {
        using var database = WalDatabaseFixture.Create(SqliteCacheMode.Private);
        using var connection = new SqliteConnection(database.ConnectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA read_uncommitted = true;";
            command.ExecuteNonQuery();
        }

        using var providerTransaction = connection.BeginTransaction();
        using var transaction = new SQLiteDatabaseTransaction(
            providerTransaction,
            TransactionType.ReadAndWrite,
            NullLogging);

        await Assert.That(transaction.ExecuteScalar<long>(
            "PRAGMA read_uncommitted;")).IsEqualTo(1L);

        transaction.Rollback();
    }

    [Test]
    [NotInParallel]
    public async Task FileBackedWal_PrivateCacheSecondWriter_PreservesTimeoutsAndFailureTelemetry()
    {
        using var database = WalDatabaseFixture.Create(SqliteCacheMode.Private, defaultTimeoutSeconds: 1);
        var competingAccess = new SQLiteDbAccess(database.ConnectionString, NullLogging);
        using var transaction = new SQLiteDatabaseTransaction(
            database.ConnectionString,
            TransactionType.ReadAndWrite,
            NullLogging);

        await Assert.That(database.JournalMode).IsEqualTo("wal");
        await Assert.That(new SqliteConnectionStringBuilder(database.ConnectionString).DefaultTimeout)
            .IsEqualTo(1);
        await Assert.That(transaction.ExecuteNonQuery("UPDATE wal_rows SET value = 'first-writer' WHERE id = 1")).IsEqualTo(1);

        var stoppedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DataLinq",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stoppedActivities.Add
        };
        ActivitySource.AddActivityListener(listener);

        var defaultTimeoutAttempt = CaptureBusyAttempt(() =>
            competingAccess.ExecuteNonQuery(
                "UPDATE wal_rows SET value = 'default-timeout' WHERE id = 1"));

        using var explicitTimeoutCommand = new SqliteCommand(
            "UPDATE wal_rows SET value = 'explicit-timeout' WHERE id = 1")
        {
            CommandTimeout = 2
        };
        var explicitTimeoutAttempt = CaptureBusyAttempt(() =>
            competingAccess.ExecuteNonQuery(explicitTimeoutCommand));

        foreach (var attempt in new[]
                 {
                     defaultTimeoutAttempt,
                     explicitTimeoutAttempt
                 })
        {
            await Assert.That(attempt.Exception.SqliteErrorCode).IsEqualTo(5);
            await Assert.That(attempt.Exception.SqliteExtendedErrorCode).IsEqualTo(5);
            await Assert.That(attempt.Exception.Message.Contains(
                "locked",
                StringComparison.OrdinalIgnoreCase)).IsTrue();
        }

        await Assert.That(defaultTimeoutAttempt.Elapsed)
            .IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500));
        await Assert.That(defaultTimeoutAttempt.Elapsed)
            .IsLessThan(TimeSpan.FromSeconds(5));
        await Assert.That(explicitTimeoutCommand.CommandTimeout).IsEqualTo(2);
        await Assert.That(explicitTimeoutAttempt.Elapsed)
            .IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1500));
        await Assert.That(explicitTimeoutAttempt.Elapsed)
            .IsLessThan(TimeSpan.FromSeconds(6));

        var failedUpdateActivities = stoppedActivities
            .Where(activity =>
                activity.OperationName == "datalinq.db.command" &&
                Equals(activity.GetTagItem("db.operation.name"), "update"))
            .ToArray();

        await Assert.That(failedUpdateActivities.Length).IsEqualTo(2);
        foreach (var activity in failedUpdateActivities)
        {
            await Assert.That(activity.Kind).IsEqualTo(ActivityKind.Client);
            await Assert.That(activity.Status).IsEqualTo(ActivityStatusCode.Error);
            await Assert.That(activity.GetTagItem("datalinq.command.kind"))
                .IsEqualTo("non_query");
            await Assert.That((bool)activity.GetTagItem("datalinq.transactional")!)
                .IsFalse();
            await Assert.That(activity.GetTagItem("error.type"))
                .IsEqualTo(typeof(SqliteException).FullName);
            await Assert.That(activity.StatusDescription!.Contains(
                "locked",
                StringComparison.OrdinalIgnoreCase)).IsTrue();
        }

        transaction.Rollback();

        await Assert.That(competingAccess.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 1")).IsEqualTo("committed");
    }

    private static (SqliteException Exception, TimeSpan Elapsed) CaptureBusyAttempt(
        Func<int> execute)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _ = execute();
        }
        catch (SqliteException exception)
        {
            stopwatch.Stop();
            return (exception, stopwatch.Elapsed);
        }

        stopwatch.Stop();
        throw new InvalidOperationException(
            "Expected the competing SQLite writer to surface SQLITE_BUSY.");
    }

    private static void SetPooledReadUncommitted(
        string connectionString,
        bool enabled)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = enabled
            ? "PRAGMA read_uncommitted = true;"
            : "PRAGMA read_uncommitted = false;";
        command.ExecuteNonQuery();

        command.CommandText = "PRAGMA read_uncommitted;";
        var observed = Convert.ToInt64(command.ExecuteScalar());
        if (observed != (enabled ? 1L : 0L))
        {
            throw new InvalidOperationException(
                $"Expected pooled SQLite read_uncommitted={(enabled ? 1 : 0)}, observed {observed}.");
        }
    }

    private sealed class WalDatabaseFixture : IDisposable
    {
        private WalDatabaseFixture(
            string databasePath,
            string connectionString,
            string journalMode,
            bool pooling)
        {
            DatabasePath = databasePath;
            ConnectionString = connectionString;
            JournalMode = journalMode;
            Pooling = pooling;
        }

        private string DatabasePath { get; }
        private bool Pooling { get; }
        public string ConnectionString { get; }
        public string JournalMode { get; }

        public static WalDatabaseFixture Create(
            SqliteCacheMode cacheMode,
            int defaultTimeoutSeconds = 2,
            bool pooling = false)
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"datalinq-sqlite-wal-characterization-{Guid.NewGuid():N}.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = cacheMode,
                Pooling = pooling,
                DefaultTimeout = defaultTimeoutSeconds
            }.ConnectionString;

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var journalCommand = connection.CreateCommand();
            journalCommand.CommandText = "PRAGMA journal_mode = WAL";
            var journalMode = Convert.ToString(journalCommand.ExecuteScalar())
                ?? throw new InvalidOperationException("SQLite did not return a journal mode.");

            using var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText =
                "CREATE TABLE wal_rows (id INTEGER PRIMARY KEY, value TEXT NOT NULL); " +
                "INSERT INTO wal_rows (id, value) VALUES " +
                "(1, 'committed'), (2, 'delete-me');";
            schemaCommand.ExecuteNonQuery();

            return new WalDatabaseFixture(
                databasePath,
                connectionString,
                journalMode,
                pooling);
        }

        public void Dispose()
        {
            if (Pooling)
                SqliteConnection.ClearAllPools();

            TryDelete(DatabasePath);
            TryDelete($"{DatabasePath}-wal");
            TryDelete($"{DatabasePath}-shm");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
