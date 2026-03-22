using System;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

internal static class SQLiteConnectionStringFactory
{
    private static readonly ConcurrentDictionary<string, SqliteConnection> KeepAliveConnections = new(StringComparer.Ordinal);

    public static string NormalizeConnectionString(string connectionString, string? memoryDatabaseName = null)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (!IsInMemory(builder))
            return builder.ConnectionString;

        builder.DataSource = GetSharedMemoryDataSource(builder.DataSource, memoryDatabaseName);
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

        KeepAliveConnections.GetOrAdd(builder.ConnectionString, cs =>
        {
            var connection = new SqliteConnection(cs);
            connection.Open();
            return connection;
        });
    }

    private static string GetSharedMemoryDataSource(string source, string? memoryDatabaseName)
    {
        if (!string.IsNullOrWhiteSpace(memoryDatabaseName))
            return memoryDatabaseName;

        if (!string.IsNullOrWhiteSpace(source) &&
            source != ":memory:" &&
            !source.Equals("memory", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        return "datalinq_memory";
    }
}
