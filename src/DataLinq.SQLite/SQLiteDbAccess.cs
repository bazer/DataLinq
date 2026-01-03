using System.Data;
using DataLinq.Logging;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

public class SQLiteDbAccess : DatabaseAccess
{
    private readonly string connectionString;
    private readonly DataLinqLoggingConfiguration loggingConfiguration;

    public SQLiteDbAccess(string connectionString, DataLinqLoggingConfiguration loggingConfiguration) : base()
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
                    command.ExecuteNonQuery();
                }
                break;
            case IsolationLevel.Serializable:
            // Serializable is the default mode in SQLite, but you can explicitly set it if needed.
            // Other isolation levels can be managed here if SQLite supports them in future versions.
            default:
                using (var command = new SqliteCommand("PRAGMA read_uncommitted = false;", connection))
                {
                    command.ExecuteNonQuery();
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
            int result = command.ExecuteNonQuery();
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
        (T)ExecuteScalar(command);

    public override object? ExecuteScalar(IDbCommand command)
    {
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            command.Connection = connection;
            SetIsolationLevel(connection, IsolationLevel.ReadUncommitted);
            Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);
            var result = command.ExecuteScalar();
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

        //using (var pragma = new SqliteCommand("PRAGMA journal_mode=WAL;", connection))
        //{
        //    pragma.ExecuteNonQuery();
        //}

        return new SQLiteDataLinqDataReader((command.ExecuteReader(CommandBehavior.CloseConnection) as SqliteDataReader)!);
    }

    public override IDataLinqDataReader ExecuteReader(string query) =>
        ExecuteReader(new SqliteCommand(query));
}
