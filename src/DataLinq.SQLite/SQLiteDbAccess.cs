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

    private SqliteConnection OpenOwnedConnection()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            SQLiteConnectionPolicy.ApplyCommittedVisibility(
                connection,
                command => ExecuteCommandWithTelemetry(
                    command,
                    "non_query",
                    transactional: false,
                    transactionType: null,
                    command.ExecuteNonQuery));
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public override int ExecuteNonQuery(IDbCommand command)
    {
        using (var connection = OpenOwnedConnection())
        {
            command.Connection = connection;
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
        (T)ExecuteScalar(new SqliteCommand(query))!;

    public override T ExecuteScalar<T>(IDbCommand command) =>
        (T)ExecuteScalar(command)!;

    public override object? ExecuteScalar(IDbCommand command)
    {
        using (var connection = OpenOwnedConnection())
        {
            command.Connection = connection;
            Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);
            var result = ExecuteCommandWithTelemetry(command, "scalar", transactional: false, transactionType: null, command.ExecuteScalar);
            connection.Close();

            return result;
        }
    }

    public override IDataLinqDataReader ExecuteReader(IDbCommand command)
    {
        var connection = OpenOwnedConnection();
        try
        {
            command.Connection = connection;
            Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

            var reader = ExecuteCommandWithTelemetry(
                command,
                "reader",
                transactional: false,
                transactionType: null,
                () => command.ExecuteReader(CommandBehavior.CloseConnection) as SqliteDataReader);

            return new SQLiteDataLinqDataReader(reader!);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public override IDataLinqDataReader ExecuteReader(string query) =>
        ExecuteReader(new SqliteCommand(query));
}
