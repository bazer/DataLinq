using System;
using System.Collections.Concurrent;
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

        var entry = KeepAliveConnections.GetOrAdd(builder.ConnectionString, static cs => new KeepAliveEntry(cs));
        lock (entry.SyncRoot)
        {
            entry.EnsureOpen();

            if (entry.ReferenceCount == 0)
                entry.HasFallbackOwner = true;
        }
    }

    public static IDisposable? AcquireKeepAliveConnectionIfInMemory(string normalizedConnectionString)
    {
        var builder = new SqliteConnectionStringBuilder(normalizedConnectionString);
        if (!IsInMemory(builder))
            return null;

        var entry = KeepAliveConnections.GetOrAdd(builder.ConnectionString, static cs => new KeepAliveEntry(cs));
        lock (entry.SyncRoot)
        {
            entry.EnsureOpen();
            entry.ReferenceCount++;
            entry.HasFallbackOwner = false;
        }

        return new KeepAliveLease(builder.ConnectionString);
    }

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

    private static void ReleaseKeepAliveConnection(string normalizedConnectionString)
    {
        if (!KeepAliveConnections.TryGetValue(normalizedConnectionString, out var entry))
            return;

        SqliteConnection? connectionToDispose = null;
        var shouldRemove = false;

        lock (entry.SyncRoot)
        {
            if (entry.ReferenceCount > 0)
                entry.ReferenceCount--;

            if (entry.ReferenceCount == 0 && !entry.HasFallbackOwner)
            {
                connectionToDispose = entry.Connection;
                entry.Connection = null;
                shouldRemove = true;
            }
        }

        if (shouldRemove)
        {
            KeepAliveConnections.TryRemove(normalizedConnectionString, out _);
            connectionToDispose?.Dispose();
        }
    }

    private sealed class KeepAliveEntry(string connectionString)
    {
        public string ConnectionString { get; } = connectionString;
        public object SyncRoot { get; } = new();
        public SqliteConnection? Connection { get; set; }
        public int ReferenceCount { get; set; }
        public bool HasFallbackOwner { get; set; }

        public void EnsureOpen()
        {
            if (Connection?.State == System.Data.ConnectionState.Open)
                return;

            Connection?.Dispose();
            Connection = new SqliteConnection(ConnectionString);
            Connection.Open();
        }
    }

    private sealed class KeepAliveLease(string normalizedConnectionString) : IDisposable
    {
        private readonly string normalizedConnectionString = normalizedConnectionString;
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            ReleaseKeepAliveConnection(normalizedConnectionString);
        }
    }
}
