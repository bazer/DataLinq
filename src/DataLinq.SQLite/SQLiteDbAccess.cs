using System.Data;
using DataLinq.Interfaces;
using DataLinq.Logging;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

public class SQLiteDbAccess : DatabaseAccess
{
    private readonly string connectionString;
    private readonly DataLinqLoggingConfiguration loggingConfiguration;

    public SQLiteDbAccess(string connectionString, DataLinqLoggingConfiguration loggingConfiguration)
        : this(null, connectionString, loggingConfiguration)
    {
    }

    internal SQLiteDbAccess(IDatabaseProvider? databaseProvider, string connectionString, DataLinqLoggingConfiguration loggingConfiguration)
        : base(databaseProvider)
    {
        this.connectionString = connectionString;
        this.loggingConfiguration = loggingConfiguration;
    }

    private void SetIsolationLevel(SqliteConnection connection, IsolationLevel isolationLevel)
    {
        switch (isolationLevel)
        {
            case IsolationLevel.ReadUncommitted:
                using (var command = new SqliteCommand("PRAGMA read_uncommitted = true;", connection))
                {
                    ExecuteCommandWithTelemetry(command, "non_query", transactional: false, transactionType: null, command.ExecuteNonQuery);
                }
                break;
            case IsolationLevel.Serializable:
            default:
                using (var command = new SqliteCommand("PRAGMA read_uncommitted = false;", connection))
                {
                    ExecuteCommandWithTelemetry(command, "non_query", transactional: false, transactionType: null, command.ExecuteNonQuery);
                }
                break;
        }
    }

    public override int ExecuteNonQuery(IDbCommand command)
    {
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            command.Connection = connection;
            SetIsolationLevel(connection, IsolationLevel.ReadUncommitted);
            Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);
            int result = ExecuteCommandWithTelemetry(command, "non_query", transactional: false, transactionType: null, command.ExecuteNonQuery);
            connection.Close();

            return result;
        }
    }

    public override int ExecuteNonQuery(string query) =>
        ExecuteNonQuery(new SqliteCommand(query));

    public override object? ExecuteScalar(string query) =>
        ExecuteScalar(new SqliteCommand(query));

    public override T ExecuteScalar<T>(string query) =>
        (T)ExecuteScalar(new SqliteCommand(query));

    public override T ExecuteScalar<T>(IDbCommand command) =>
        (T)ExecuteScalar(command)!;

    public override object? ExecuteScalar(IDbCommand command)
    {
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            command.Connection = connection;
            SetIsolationLevel(connection, IsolationLevel.ReadUncommitted);
            Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);
            var result = ExecuteCommandWithTelemetry(command, "scalar", transactional: false, transactionType: null, command.ExecuteScalar);
            connection.Close();

            return result;
        }
    }

    public override IDataLinqDataReader ExecuteReader(IDbCommand command)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        command.Connection = connection;
        SetIsolationLevel(connection, IsolationLevel.ReadUncommitted);
        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

        var reader = ExecuteCommandWithTelemetry(
            command,
            "reader",
            transactional: false,
            transactionType: null,
            () => command.ExecuteReader(CommandBehavior.CloseConnection) as SqliteDataReader);

        return new SQLiteDataLinqDataReader(reader!);
    }

    public override IDataLinqDataReader ExecuteReader(string query) =>
        ExecuteReader(new SqliteCommand(query));
}
