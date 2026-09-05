using System.Data;
using DataLinq.Interfaces;
using DataLinq.Logging;
using MySqlConnector;

namespace DataLinq.MySql;

public class SqlDbAccess : DatabaseAccess
{
    private readonly MySqlDataSource dataSource;
    private readonly DataLinqLoggingConfiguration loggingConfiguration;
    private readonly DatabaseType? databaseType;

    public SqlDbAccess(MySqlDataSource dataSource, DataLinqLoggingConfiguration loggingConfiguration)
        : this(null, dataSource, loggingConfiguration)
    {
    }

    internal SqlDbAccess(IDatabaseProvider? databaseProvider, MySqlDataSource dataSource, DataLinqLoggingConfiguration loggingConfiguration)
        : base(databaseProvider)
    {
        this.dataSource = dataSource;
        this.loggingConfiguration = loggingConfiguration;
        this.databaseType = databaseProvider?.DatabaseType;
    }

    public override int ExecuteNonQuery(IDbCommand command)
    {
        using var connection = dataSource.OpenConnection();
        command.Connection = connection;

        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

        return ExecuteCommandWithTelemetry(command, "non_query", transactional: false, transactionType: null, command.ExecuteNonQuery);
    }

    public override int ExecuteNonQuery(string query)
    {
        using var command = new MySqlCommand(query);
        return ExecuteNonQuery(command);
    }

    public override object? ExecuteScalar(string query)
    {
        using var command = new MySqlCommand(query);
        return ExecuteScalar(command);
    }

    public override T ExecuteScalar<T>(string query)
    {
        using var command = new MySqlCommand(query);
        return ExecuteScalar<T>(command);
    }

    public override T ExecuteScalar<T>(IDbCommand command) =>
        (T)(ExecuteScalar(command) ?? default(T)!);

    public override object? ExecuteScalar(IDbCommand command)
    {
        using var connection = dataSource.OpenConnection();
        command.Connection = connection;

        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

        return ExecuteCommandWithTelemetry(command, "scalar", transactional: false, transactionType: null, command.ExecuteScalar);
    }

    public override IDataLinqDataReader ExecuteReader(IDbCommand command)
    {
        var connection = dataSource.OpenConnection();
        try
        {
            command.Connection = connection;
            Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

            var reader = ExecuteCommandWithTelemetry(
                command,
                "reader",
                transactional: false,
                transactionType: null,
                () => command.ExecuteReader(CommandBehavior.CloseConnection) as MySqlDataReader);

            return new SqlDataLinqDataReader(reader!, databaseType);
        }
        catch
        {
            // CloseConnection only transfers ownership once a reader was created.
            // Logging, command setup, or execution can throw before that handoff.
            connection.Dispose();
            throw;
        }
    }

    public override IDataLinqDataReader ExecuteReader(string query) =>
        ExecuteOwnedReader(new MySqlCommand(query));
}
