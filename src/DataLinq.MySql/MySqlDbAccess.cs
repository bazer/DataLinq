using System;
using System.Data;
using DataLinq.Mutation;
using MySqlConnector;

namespace DataLinq.MySql;

public class MySqlDbAccess : DatabaseTransaction
{
    private readonly string databaseName;

    public MySqlDbAccess(string connectionString, TransactionType type, string databaseName) : base(connectionString, type)
    {
        if (type != TransactionType.ReadOnly)
            throw new ArgumentException("Only 'TransactionType.ReadOnly' is allowed");

        this.databaseName = databaseName;
    }

    public override void Commit()
    {

    }

    public override void Dispose()
    {

    }

    public override int ExecuteNonQuery(IDbCommand command)
    {
        using (var connection = new MySqlConnection(ConnectionString))
        {
            connection.Open();
            command.Connection = connection;
            
            if (databaseName != null)
                command.CommandText = $"USE `{databaseName}`;{command.CommandText}";

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
        using (var connection = new MySqlConnection(ConnectionString))
        {
            connection.Open();
            command.Connection = connection;

            if (databaseName != null)
                command.CommandText = $"USE `{databaseName}`;{command.CommandText}";

            object result = command.ExecuteScalar();
            connection.Close();

            return result;
        }
    }

    public override IDataLinqDataReader ExecuteReader(IDbCommand command)
    {
        var connection = new MySqlConnection(ConnectionString);
        command.Connection = connection;
        connection.Open();

        if (databaseName != null)
            command.CommandText = $"USE `{databaseName}`;{command.CommandText}";

        return new MySqlDataLinqDataReader(command.ExecuteReader(CommandBehavior.CloseConnection) as MySqlDataReader);
    }

    public override IDataLinqDataReader ExecuteReader(string query) =>
        ExecuteReader(new MySqlCommand(query));

    public override void Rollback()
    {

    }
}
