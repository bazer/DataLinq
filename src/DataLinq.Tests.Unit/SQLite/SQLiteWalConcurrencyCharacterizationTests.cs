using System;
using System.Diagnostics;
using System.IO;
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
    public async Task FileBackedWal_PrivateCacheOutsideRead_SeesLastCommittedValueDuringPendingWrite()
    {
        using var database = WalDatabaseFixture.Create(SqliteCacheMode.Private);
        var outsideAccess = new SQLiteDbAccess(database.ConnectionString, NullLogging);
        using var transaction = new SQLiteDatabaseTransaction(
            database.ConnectionString,
            TransactionType.ReadAndWrite,
            NullLogging);

        await Assert.That(database.JournalMode).IsEqualTo("wal");
        await Assert.That(transaction.ExecuteNonQuery("UPDATE wal_rows SET value = 'pending' WHERE id = 1")).IsEqualTo(1);
        await Assert.That(transaction.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 1")).IsEqualTo("pending");

        // SQLiteDbAccess opens a separate connection and bypasses DataLinq's row and relation caches.
        // The committed value here is therefore database-isolation evidence, not cache-scoping evidence.
        await Assert.That(outsideAccess.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 1")).IsEqualTo("committed");

        transaction.Commit();

        await Assert.That(outsideAccess.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 1")).IsEqualTo("pending");
    }

    [Test]
    public async Task FileBackedWal_SharedCacheOwnedAccess_CurrentlyExposesPendingWriteAtDirectSqlBoundary()
    {
        using var database = WalDatabaseFixture.Create(SqliteCacheMode.Shared);
        var outsideAccess = new SQLiteDbAccess(database.ConnectionString, NullLogging);
        using var transaction = new SQLiteDatabaseTransaction(
            database.ConnectionString,
            TransactionType.ReadAndWrite,
            NullLogging);

        await Assert.That(database.JournalMode).IsEqualTo("wal");
        await Assert.That(transaction.ExecuteNonQuery("UPDATE wal_rows SET value = 'pending' WHERE id = 1")).IsEqualTo(1);

        // W1 defect baseline: both owned paths currently enable read_uncommitted, and the explicit
        // shared cache supplies SQLite's other prerequisite for a dirty read. SQ-1 must invert this
        // outside assertion and rename the test; this is explicitly not the desired 0.9 contract.
        // Direct SQLiteDbAccess proves the pending value did not leak through the ORM cache overlay.
        await Assert.That(outsideAccess.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 1")).IsEqualTo("pending");

        transaction.Rollback();

        await Assert.That(outsideAccess.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 1")).IsEqualTo("committed");
    }

    [Test]
    public async Task FileBackedWal_PrivateCacheSecondWriter_SurfacesBusyWithinConfiguredTimeout()
    {
        using var database = WalDatabaseFixture.Create(SqliteCacheMode.Private, defaultTimeoutSeconds: 1);
        var competingAccess = new SQLiteDbAccess(database.ConnectionString, NullLogging);
        using var transaction = new SQLiteDatabaseTransaction(
            database.ConnectionString,
            TransactionType.ReadAndWrite,
            NullLogging);

        await Assert.That(database.JournalMode).IsEqualTo("wal");
        await Assert.That(transaction.ExecuteNonQuery("UPDATE wal_rows SET value = 'first-writer' WHERE id = 1")).IsEqualTo(1);

        SqliteException? busyException = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            competingAccess.ExecuteNonQuery("UPDATE wal_rows SET value = 'second-writer' WHERE id = 1");
        }
        catch (SqliteException exception)
        {
            busyException = exception;
        }

        stopwatch.Stop();

        await Assert.That(busyException).IsNotNull();
        await Assert.That(busyException!.SqliteErrorCode).IsEqualTo(5);
        await Assert.That(busyException.Message.Contains("locked", StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));

        transaction.Rollback();

        await Assert.That(competingAccess.ExecuteScalar<string>("SELECT value FROM wal_rows WHERE id = 1")).IsEqualTo("committed");
    }

    private sealed class WalDatabaseFixture : IDisposable
    {
        private WalDatabaseFixture(string databasePath, string connectionString, string journalMode)
        {
            DatabasePath = databasePath;
            ConnectionString = connectionString;
            JournalMode = journalMode;
        }

        private string DatabasePath { get; }
        public string ConnectionString { get; }
        public string JournalMode { get; }

        public static WalDatabaseFixture Create(SqliteCacheMode cacheMode, int defaultTimeoutSeconds = 2)
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"datalinq-sqlite-wal-characterization-{Guid.NewGuid():N}.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = cacheMode,
                Pooling = false,
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
                "INSERT INTO wal_rows (id, value) VALUES (1, 'committed');";
            schemaCommand.ExecuteNonQuery();

            return new WalDatabaseFixture(databasePath, connectionString, journalMode);
        }

        public void Dispose()
        {
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
