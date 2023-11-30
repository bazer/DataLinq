using System;
using System.Data;
using DataLinq.Mutation;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

public class SQLiteDbAccess : DatabaseTransaction
{
    public SQLiteDbAccess(string connectionString, TransactionType type) : base(connectionString, type)
    {
        //if (type != TransactionType.NoTransaction)
        throw new ArgumentException("Only 'TransactionType.NoTransaction' is allowed");
    }

    public override void Commit()
    {

    }

    public override void Dispose()
    {

    }

    public override int ExecuteNonQuery(IDbCommand command)
    {
        using (var connection = new SqliteConnection(ConnectionString))
        {
            connection.Open();
            command.Connection = connection;
            int result = command.ExecuteNonQuery();
            connection.Close();

            return result;
        }
    }

    public override int ExecuteNonQuery(string query) =>
        ExecuteNonQuery(new SqliteCommand(query));

    public override object ExecuteScalar(string query) =>
        ExecuteScalar(new SqliteCommand(query));

    public override T ExecuteScalar<T>(string query) =>
        (T)ExecuteScalar(new SqliteCommand(query));

    public override T ExecuteScalar<T>(IDbCommand command) =>
        (T)ExecuteScalar(command);

    public override object ExecuteScalar(IDbCommand command)
    {
        using (var connection = new SqliteConnection(ConnectionString))
        {
            connection.Open();
            command.Connection = connection;
            object result = command.ExecuteScalar();
            connection.Close();

            return result;
        }
    }

    public override IDataLinqDataReader ExecuteReader(IDbCommand command)
    {
        var connection = new SqliteConnection(ConnectionString);
        command.Connection = connection;
        connection.Open();

        using (var pragma = new SqliteCommand("PRAGMA journal_mode=WAL;", connection))
        {
            pragma.ExecuteNonQuery();
        }

        return new SQLiteDataLinqDataReader(command.ExecuteReader(CommandBehavior.CloseConnection) as SqliteDataReader);
    }

    public override IDataLinqDataReader ExecuteReader(string query) =>
        ExecuteReader(new SqliteCommand(query));

    public override void Rollback()
    {

    }
}
