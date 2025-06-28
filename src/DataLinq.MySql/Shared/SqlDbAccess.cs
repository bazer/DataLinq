using System.Data;
using DataLinq.Logging;
using MySqlConnector;

namespace DataLinq.MySql;

public class SqlDbAccess : DatabaseAccess
{
    private readonly MySqlDataSource dataSource;
    private readonly DataLinqLoggingConfiguration loggingConfiguration;

    public SqlDbAccess(MySqlDataSource dataSource, DataLinqLoggingConfiguration loggingConfiguration) : base()
    {
        this.dataSource = dataSource;
        this.loggingConfiguration = loggingConfiguration;
    }

    public override int ExecuteNonQuery(IDbCommand command)
    {
        using var connection = dataSource.OpenConnection();
        command.Connection = connection;

        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

        return command.ExecuteNonQuery();
    }

    public override int ExecuteNonQuery(string query) =>
        ExecuteNonQuery(new MySqlCommand(query));

    public override object? ExecuteScalar(string query) =>
        ExecuteScalar(new MySqlCommand(query));

    public override T ExecuteScalar<T>(string query) =>
        ExecuteScalar<T>(new MySqlCommand(query));

    public override T ExecuteScalar<T>(IDbCommand command) =>
        (T)(ExecuteScalar(command) ?? default(T)!);

    public override object? ExecuteScalar(IDbCommand command)
    {
        using var connection = dataSource.OpenConnection();
        command.Connection = connection;

        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

        return command.ExecuteScalar();
    }

    public override IDataLinqDataReader ExecuteReader(IDbCommand command)
    {
        var connection = dataSource.OpenConnection();
        command.Connection = connection;

        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

        return new SqlDataLinqDataReader((command.ExecuteReader(CommandBehavior.CloseConnection) as MySqlDataReader)!);
    }

    public override IDataLinqDataReader ExecuteReader(string query) =>
        ExecuteReader(new MySqlCommand(query));
}
