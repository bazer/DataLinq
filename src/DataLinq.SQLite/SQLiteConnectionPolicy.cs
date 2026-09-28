using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

internal static class SQLiteConnectionPolicy
{
    internal const string CommittedVisibilitySql = "PRAGMA read_uncommitted = false;";
    internal const IsolationLevel OwnedTransactionIsolationLevel =
        IsolationLevel.Serializable;

    internal static void ApplyCommittedVisibility(
        SqliteConnection connection,
        Func<SqliteCommand, int> executeNonQuery)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(executeNonQuery);

        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "SQLite committed-visibility policy requires an open connection.");
        }

        using var command = new SqliteCommand(
            CommittedVisibilitySql,
            connection);
        _ = executeNonQuery(command);
    }
}
