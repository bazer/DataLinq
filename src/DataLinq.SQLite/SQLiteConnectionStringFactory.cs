using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

internal static class SQLiteConnectionStringFactory
{
    private static readonly ConcurrentDictionary<string, KeepAliveEntry> KeepAliveConnections = new(StringComparer.Ordinal);

    public static string NormalizeConnectionString(string connectionString, string? memoryDatabaseName = null, string? anonymousInMemoryDatabaseName = null)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (!IsInMemory(builder))
            return builder.ConnectionString;

        builder.DataSource = GetSharedMemoryDataSource(builder.DataSource, memoryDatabaseName, anonymousInMemoryDatabaseName);
        builder.Mode = SqliteOpenMode.Memory;
        builder.Cache = SqliteCacheMode.Shared;

        return builder.ConnectionString;
    }

    public static bool IsInMemory(SqliteConnectionStringBuilder builder)
    {
        if (builder.Mode == SqliteOpenMode.Memory)
            return true;

        var source = builder.DataSource;
        return source == ":memory:" || source.Equals("memory", StringComparison.OrdinalIgnoreCase);
    }

    public static void EnsureKeepAliveIfInMemory(string normalizedConnectionString)
    {
        var builder = new SqliteConnectionStringBuilder(normalizedConnectionString);
        if (!IsInMemory(builder))
            return;

        while (true)
        {
            var entry = KeepAliveConnections.GetOrAdd(builder.ConnectionString, static cs => new KeepAliveEntry(cs));
            entry.Gate.Wait();
            try
            {
                if (!IsCurrent(entry)) continue;
                entry.EnsureOpen();
                if (entry.ReferenceCount == 0) entry.HasFallbackOwner = true;
                return;
            }
            finally { entry.Gate.Release(); }
        }
    }

    public static IDisposable? AcquireKeepAliveConnectionIfInMemory(string normalizedConnectionString)
    {
        var builder = new SqliteConnectionStringBuilder(normalizedConnectionString);
        if (!IsInMemory(builder))
            return null;

        while (true)
        {
            var entry = KeepAliveConnections.GetOrAdd(builder.ConnectionString, static cs => new KeepAliveEntry(cs));
            entry.Gate.Wait();
            try
            {
                if (!IsCurrent(entry)) continue;
                entry.EnsureOpen();
                entry.ReferenceCount++;
                entry.HasFallbackOwner = false;
                return new KeepAliveLease(entry);
            }
            finally { entry.Gate.Release(); }
        }
    }

    internal static async Task EnsureKeepAliveIfInMemoryAsync(string normalizedConnectionString, CancellationToken token)
    {
        var builder = new SqliteConnectionStringBuilder(normalizedConnectionString);
        token.ThrowIfCancellationRequested();
        if (!IsInMemory(builder)) return;
        while (true)
        {
            var entry = KeepAliveConnections.GetOrAdd(builder.ConnectionString, static cs => new KeepAliveEntry(cs));
            await entry.Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!IsCurrent(entry)) continue;
                await entry.EnsureOpenAsync(token).ConfigureAwait(false);
                if (entry.ReferenceCount == 0) entry.HasFallbackOwner = true;
                return;
            }
            finally { entry.Gate.Release(); }
        }
    }

    private static bool IsCurrent(KeepAliveEntry entry) =>
        KeepAliveConnections.TryGetValue(entry.ConnectionString, out var current) && ReferenceEquals(current, entry);

    private static string GetSharedMemoryDataSource(string source, string? memoryDatabaseName, string? anonymousInMemoryDatabaseName)
    {
        if (!string.IsNullOrWhiteSpace(memoryDatabaseName))
            return memoryDatabaseName;

        if (!string.IsNullOrWhiteSpace(source) &&
            source != ":memory:" &&
            !source.Equals("memory", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        return anonymousInMemoryDatabaseName ?? "datalinq_memory";
    }

    private static SqliteConnection? ReleaseKeepAliveConnection(KeepAliveEntry entry)
    {
        // Caller holds this entry's gate. Removal and reference release must be
        // atomic with acquisition; an acquirer holding a removed entry retries.
        if (entry.ReferenceCount > 0) entry.ReferenceCount--;
        if (entry.ReferenceCount == 0 && !entry.HasFallbackOwner && IsCurrent(entry))
        {
            KeepAliveConnections.TryRemove(entry.ConnectionString, out _);
            var connection = entry.Connection;
            entry.Connection = null;
            return connection;
        }
        return null;
    }

    private sealed class KeepAliveEntry(string connectionString)
    {
        public string ConnectionString { get; } = connectionString;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public SqliteConnection? Connection { get; set; }
        public int ReferenceCount { get; set; }
        public bool HasFallbackOwner { get; set; }

        public void EnsureOpen()
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            if (Connection?.State == System.Data.ConnectionState.Open)
                return;

            Connection?.Dispose();
            Connection = null;
            var connection = new SqliteConnection(ConnectionString);
            try { connection.Open(); Connection = connection; }
            catch (Exception failure)
            {
                var failures = new ExecutionFailures();
                failures.AddReported(failure, ExecutionFailureStage.Initialization);
                try { connection.Dispose(); } catch (Exception cleanup) { failures.AddCleanup(cleanup); }
                ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
                    ExecutionRecoveryActions.None, null));
                failures.ThrowIfAny();
                throw;
            }
        }

        public async Task EnsureOpenAsync(CancellationToken token)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            if (Connection?.State == System.Data.ConnectionState.Open) return;
            if (Connection is not null) await Connection.DisposeAsync().ConfigureAwait(false);
            Connection = null;
            var connection = new SqliteConnection(ConnectionString);
            try { await connection.OpenAsync(token).ConfigureAwait(false); Connection = connection; }
            catch (Exception failure)
            {
                var failures = new ExecutionFailures();
                failures.AddReported(failure, ExecutionFailureStage.Initialization);
                try { await connection.DisposeAsync().ConfigureAwait(false); } catch (Exception cleanup) { failures.AddCleanup(cleanup); }
                ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
                    ExecutionRecoveryActions.None, null));
                failures.ThrowIfAny();
                throw;
            }
        }
    }

    private sealed class KeepAliveLease(KeepAliveEntry entry) : IDisposable, IAsyncDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            SqliteConnection? connection;
            entry.Gate.Wait();
            try { connection = ReleaseKeepAliveConnection(entry); }
            finally { entry.Gate.Release(); }
            connection?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            SqliteConnection? connection;
            await entry.Gate.WaitAsync().ConfigureAwait(false);
            try { connection = ReleaseKeepAliveConnection(entry); }
            finally { entry.Gate.Release(); }
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
