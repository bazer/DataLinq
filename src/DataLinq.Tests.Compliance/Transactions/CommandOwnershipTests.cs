using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Logging;
using DataLinq.Mutation;
using DataLinq.MySql;
using DataLinq.SQLite;
using DataLinq.Testing;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace DataLinq.Tests.Compliance;

public sealed class CommandOwnershipTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task StringCommandsHaveBoundedLifetimesAndCallerCommandsRemainReusable(TestProviderDescriptor descriptor)
    {
        using var scope = TemporaryModelTestDatabase<MultipleForeignKeyRelationDb>.Create(descriptor, "command_ownership");
        using var dataSource = scope.Connection.DatabaseType == DatabaseType.SQLite
            // This workload checks command lifetimes, not TCP connection churn.
            // Temporary database scopes otherwise disable server pooling.
            ? null : new MySqlDataSourceBuilder(new MySqlConnectionStringBuilder(scope.Connection.ConnectionString)
            {
                Pooling = true,
                MaximumPoolSize = 2
            }.ConnectionString).Build();
        foreach (var transactional in new[] { false, true })
        {
            var probe = new CommandProbe();
            DatabaseAccess access = scope.Connection.DatabaseType == DatabaseType.SQLite
                ? transactional
                    ? new TrackedSqliteTransaction(scope.Connection.ConnectionString, probe)
                    : new TrackedSqliteAccess(scope.Connection.ConnectionString, probe)
                : transactional
                    ? new TrackedSqlTransaction(dataSource!, scope.Connection.DataSourceName, probe)
                    : new TrackedSqlAccess(dataSource!, probe);
            using var transaction = access as DatabaseTransaction;
            access.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS command_ownership_values (value INTEGER)");
            await Assert.That(probe.LiveCount).IsEqualTo(0);

            using (var reader = access.ExecuteReader("SELECT 42 AS answer, 'text' AS word, X'010203' AS payload, NULL AS empty_value"))
            {
                await Assert.That(probe.LiveCount).IsEqualTo(1);
                await Assert.That(reader.ReadNextRow()).IsTrue();
                await Assert.That(reader.GetInt32(0)).IsEqualTo(42);
                await Assert.That(reader.GetOrdinal("word")).IsEqualTo(1);
                await Assert.That(reader.GetString(1)).IsEqualTo("text");
                await Assert.That(reader.GetBytes(2)).IsEquivalentTo(new byte[] { 1, 2, 3 });
                await Assert.That(reader.IsDbNull(3)).IsTrue();
            }
            await Assert.That(probe.LiveCount).IsEqualTo(0);
            _ = access.ReadReader("SELECT 1 UNION ALL SELECT 2").Take(1).ToArray();
            await Assert.That(probe.LiveCount).IsEqualTo(0);

            foreach (var failBeforeExecute in new[] { false, true })
            {
                probe.FailBeforeExecute = failBeforeExecute;
                foreach (var execute in new Action[]
                {
                    () => access.ExecuteNonQuery("INVALID SQL"),
                    () => access.ExecuteScalar("INVALID SQL"),
                    () => access.ExecuteScalar<object>("INVALID SQL"),
                    () => access.ExecuteReader("INVALID SQL")
                })
                {
                    var failed = false;
                    try { execute(); }
                    catch (Exception) { failed = true; }
                    await Assert.That(failed).IsTrue();
                    await Assert.That(probe.LiveCount).IsEqualTo(0);
                }
            }
            probe.FailBeforeExecute = false;

            using (IDbCommand callerCommand = scope.Connection.DatabaseType == DatabaseType.SQLite
                ? new SqliteCommand("SELECT 42") : new MySqlCommand("SELECT 42"))
            {
                using (var reader = access.ExecuteReader(callerCommand))
                    await Assert.That(reader.ReadNextRow()).IsTrue();
                // Microsoft.Data.Sqlite closes registered commands with its owned
                // connection. The same command remains reusable; open transactions
                // and MySQL do not perform that connection-level cleanup here.
                var expectedLive = descriptor.IsSQLite && !transactional ? 0 : 1;
                await Assert.That(probe.LiveCount).IsEqualTo(expectedLive);
                await Assert.That(Convert.ToInt32(access.ExecuteScalar(callerCommand))).IsEqualTo(42);
                await Assert.That(probe.LiveCount).IsEqualTo(expectedLive);
            }
            await Assert.That(probe.LiveCount).IsEqualTo(0);

            var startDisposed = probe.DisposedCount;
            var started = Stopwatch.GetTimestamp();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            const int iterations = 512;
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                access.ExecuteNonQuery("INSERT INTO command_ownership_values (value) VALUES (42)");
                _ = access.ExecuteScalar<object>("SELECT 42");
                using (var reader = access.ExecuteReader("SELECT 42"))
                    if (!reader.ReadNextRow() || reader.GetInt32(0) != 42)
                        throw new InvalidOperationException("Reader result changed during lifetime workload.");
                if (probe.LiveCount != 0)
                    throw new InvalidOperationException("An internally created command survived its operation.");
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var elapsed = Stopwatch.GetElapsedTime(started);
            await Assert.That(probe.DisposedCount - startDisposed).IsEqualTo(iterations * 3);
            Console.WriteLine($"Command lifetime workload: {descriptor.Name}, transaction={transactional}, commands={iterations * 3}, disposed={probe.DisposedCount - startDisposed}, peak-live={probe.PeakLive}, end-live={probe.LiveCount}, elapsed-ms={elapsed.TotalMilliseconds:F2}, thread-allocated-bytes={allocated} (includes probe overhead)");
            transaction?.Rollback();
        }
    }

    private sealed class CommandProbe
    {
        private readonly ConcurrentDictionary<IDbCommand, byte> live = new();
        private int disposed;
        internal bool FailBeforeExecute { get; set; }
        internal int LiveCount => live.Count;
        internal int DisposedCount => disposed;
        internal int PeakLive { get; private set; }
        internal void Track(IDbCommand command)
        {
            if (live.TryAdd(command, 0))
            {
                PeakLive = Math.Max(PeakLive, LiveCount);
                ((Component)command).Disposed += (_, _) =>
                {
                    if (live.TryRemove(command, out _))
                        Interlocked.Increment(ref disposed);
                };
            }
            if (FailBeforeExecute)
                throw new InvalidOperationException("Injected failure after owned command creation.");
        }
    }

    private sealed class TrackedSqliteAccess(string connection, CommandProbe probe)
        : SQLiteDbAccess(connection, DataLinqLoggingConfiguration.NullConfiguration)
    {
        public override int ExecuteNonQuery(IDbCommand command) { probe.Track(command); return base.ExecuteNonQuery(command); }
        public override object? ExecuteScalar(IDbCommand command) { probe.Track(command); return base.ExecuteScalar(command); }
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) { probe.Track(command); return base.ExecuteReader(command); }
    }

    private sealed class TrackedSqliteTransaction(string connection, CommandProbe probe)
        : SQLiteDatabaseTransaction(connection, TransactionType.ReadAndWrite, DataLinqLoggingConfiguration.NullConfiguration)
    {
        public override int ExecuteNonQuery(IDbCommand command) { probe.Track(command); return base.ExecuteNonQuery(command); }
        public override object ExecuteScalar(IDbCommand command) { probe.Track(command); return base.ExecuteScalar(command); }
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) { probe.Track(command); return base.ExecuteReader(command); }
    }

    private sealed class TrackedSqlAccess(MySqlDataSource source, CommandProbe probe)
        : SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration)
    {
        public override int ExecuteNonQuery(IDbCommand command) { probe.Track(command); return base.ExecuteNonQuery(command); }
        public override object? ExecuteScalar(IDbCommand command) { probe.Track(command); return base.ExecuteScalar(command); }
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) { probe.Track(command); return base.ExecuteReader(command); }
    }

    private sealed class TrackedSqlTransaction(MySqlDataSource source, string name, CommandProbe probe)
        : SqlDatabaseTransaction(source, TransactionType.ReadAndWrite, name, DataLinqLoggingConfiguration.NullConfiguration)
    {
        public override int ExecuteNonQuery(IDbCommand command) { probe.Track(command); return base.ExecuteNonQuery(command); }
        public override object? ExecuteScalar(IDbCommand command) { probe.Track(command); return base.ExecuteScalar(command); }
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) { probe.Track(command); return base.ExecuteReader(command); }
    }
}
