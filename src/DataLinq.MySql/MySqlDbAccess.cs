using System;
using System.Data;
using DataLinq.Mutation;
using DataLinq.Logging;
using MySqlConnector;

namespace DataLinq.MySql;

public class MySqlDbAccess : DatabaseTransaction
{
    private readonly string databaseName;
    private readonly MySqlDataSource dataSource;
    private readonly DataLinqLoggingConfiguration loggingConfiguration;

    public MySqlDbAccess(MySqlDataSource dataSource, string connectionString, TransactionType type, string databaseName, DataLinqLoggingConfiguration loggingConfiguration) : base(connectionString, type)
    {
        if (type != TransactionType.ReadOnly)
            throw new ArgumentException("Only 'TransactionType.ReadOnly' is allowed");

        this.dataSource = dataSource;
        this.databaseName = databaseName;
        this.loggingConfiguration = loggingConfiguration;
    }

    public override void Commit()
    {

    }

    public override void Dispose()
    {

    }

    public override int ExecuteNonQuery(IDbCommand command)
    {
        using (var connection = dataSource.OpenConnection())
        {
            connection.Open();
            command.Connection = connection;
            
            //if (databaseName != null)
            //    command.CommandText = $"USE `{databaseName}`;{command.CommandText}";

            Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

            int result = command.ExecuteNonQuery();
            connection.Close();

            return result;
        }
    }

    public override int ExecuteNonQuery(string query) =>
        ExecuteNonQuery(new MySqlCommand(query));

    public override object ExecuteScalar(string query) =>
        ExecuteScalar(new MySqlCommand(query));

    public override T ExecuteScalar<T>(string query) =>
        (T)ExecuteScalar(new MySqlCommand(query));

    public override T ExecuteScalar<T>(IDbCommand command) =>
        (T)ExecuteScalar(command);

    public override object ExecuteScalar(IDbCommand command)
    {
        using (var connection = dataSource.OpenConnection())
        {
            connection.Open();
            command.Connection = connection;

            //if (databaseName != null)
            //    command.CommandText = $"USE `{databaseName}`;{command.CommandText}";

            Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

            object result = command.ExecuteScalar();
            connection.Close();

            return result;
        }
    }

    public override IDataLinqDataReader ExecuteReader(IDbCommand command)
    {
        var connection = dataSource.CreateConnection();
        command.Connection = connection;
        connection.Open();

        //if (databaseName != null)
        //    command.CommandText = $"USE `{databaseName}`;{command.CommandText}";

        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

        return new MySqlDataLinqDataReader(command.ExecuteReader(CommandBehavior.CloseConnection) as MySqlDataReader);
    }

    public override IDataLinqDataReader ExecuteReader(string query) =>
        ExecuteReader(new MySqlCommand(query));

    public override void Rollback()
    {

    }
}
