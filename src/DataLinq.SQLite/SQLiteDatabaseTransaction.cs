using System;
using System.Data;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Mutation;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

public class SQLiteDatabaseTransaction : DatabaseTransaction
{
    private IDbConnection dbConnection = null!;
    private readonly string? connectionString;
    private readonly DataLinqLoggingConfiguration loggingConfiguration;

    public SQLiteDatabaseTransaction(string connectionString, TransactionType type, DataLinqLoggingConfiguration loggingConfiguration)
        : this(null, connectionString, type, loggingConfiguration)
    {
    }

    internal SQLiteDatabaseTransaction(IDatabaseProvider? databaseProvider, string connectionString, TransactionType type, DataLinqLoggingConfiguration loggingConfiguration)
        : base(databaseProvider, type)
    {
        this.connectionString = connectionString;
        this.loggingConfiguration = loggingConfiguration;
    }

    public SQLiteDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type, DataLinqLoggingConfiguration loggingConfiguration)
        : this(null, dbTransaction, type, loggingConfiguration)
    {
    }

    internal SQLiteDatabaseTransaction(IDatabaseProvider? databaseProvider, IDbTransaction dbTransaction, TransactionType type, DataLinqLoggingConfiguration loggingConfiguration)
        : base(databaseProvider, dbTransaction, type)
    {
        if (dbTransaction.Connection == null) throw new ArgumentNullException("dbTransaction.Connection", "The transaction connection is null");
        if (dbTransaction.Connection is not SqliteConnection) throw new ArgumentException("The transaction connection must be an SqliteConnection", "dbTransaction.Connection");
        if (dbTransaction.Connection.State != ConnectionState.Open) throw new Exception("The transaction connection is not open");

        this.loggingConfiguration = loggingConfiguration;
        SetStatus(DatabaseTransactionStatus.Open);
        dbConnection = dbTransaction.Connection;
        BeginTransactionTelemetry();
    }

    private IDbConnection DbConnection
    {
        get
        {
            if (Status == DatabaseTransactionStatus.Committed || Status == DatabaseTransactionStatus.RolledBack)
                throw new Exception("Can't open a new connection on a committed or rolled back transaction");

            if (Status == DatabaseTransactionStatus.Closed)
            {
                if (connectionString == null)
                    throw new InvalidOperationException("Attached SQLite transactions cannot be reopened after they are closed because DataLinq does not own their connection string.");

                SetStatus(DatabaseTransactionStatus.Open);
                dbConnection = new SqliteConnection(connectionString);
                dbConnection.Open();
                SetIsolationLevel((dbConnection as SqliteConnection)!, IsolationLevel.ReadUncommitted);
                DbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadUncommitted);
                BeginTransactionTelemetry();
            }

            return dbConnection;
        }
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
        command.Connection = DbConnection;
        command.Transaction = DbTransaction;
        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);
        return ExecuteCommandWithTelemetry(command, "non_query", transactional: true, Type, command.ExecuteNonQuery);
    }

    public override int ExecuteNonQuery(string query) =>
        ExecuteNonQuery(new SqliteCommand(query));

    public override object ExecuteScalar(string query) =>
        ExecuteScalar(new SqliteCommand(query))!;

    public override T ExecuteScalar<T>(string query) =>
        (T)ExecuteScalar(new SqliteCommand(query))!;

    public override T ExecuteScalar<T>(IDbCommand command) =>
        (T)ExecuteScalar(command)!;

    public override object ExecuteScalar(IDbCommand command)
    {
        command.Connection = DbConnection;
        command.Transaction = DbTransaction;
        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);
        return ExecuteCommandWithTelemetry(command, "scalar", transactional: true, Type, command.ExecuteScalar)!;
    }

    public override IDataLinqDataReader ExecuteReader(string query)
    {
        return ExecuteReader(new SqliteCommand(query));
    }

    public override IDataLinqDataReader ExecuteReader(IDbCommand command)
    {
        command.Connection = DbConnection;
        command.Transaction = DbTransaction;
        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

        var reader = ExecuteCommandWithTelemetry(
            command,
            "reader",
            transactional: true,
            Type,
            () => command.ExecuteReader() as SqliteDataReader);

        return new SQLiteDataLinqDataReader(reader!);
    }

    private IDbTransaction GetActiveProviderTransaction(string operation)
    {
        var dbTransaction = DbTransaction ??
            throw new InvalidOperationException(
                $"Cannot {operation} because the provider transaction handle is unavailable. DataLinq cannot infer whether it committed or rolled back.");

        IDbConnection? connection;
        try
        {
            connection = dbTransaction.Connection;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} because the provider transaction handle is no longer readable. DataLinq cannot infer whether it committed or rolled back.",
                exception);
        }

        if (connection?.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} because the provider transaction is no longer active on an open connection. DataLinq cannot infer whether it committed or rolled back. " +
                "Complete attached transactions through the DataLinq wrapper instead of the original transaction handle.");
        }

        return dbTransaction;
    }

    public override void Commit()
    {
        try
        {
            if (Status == DatabaseTransactionStatus.Open)
            {
                GetActiveProviderTransaction("commit").Commit();

                CompleteTransactionTelemetry(DatabaseTransactionStatus.Committed);
            }

            SetStatus(DatabaseTransactionStatus.Committed);
            Dispose();
        }
        catch (Exception ex)
        {
            FailTransactionTelemetry(DatabaseTransactionStatus.Committed, ex);
            throw;
        }
    }

    public override void Rollback()
    {
        try
        {
            if (Status == DatabaseTransactionStatus.Open)
            {
                GetActiveProviderTransaction("roll back").Rollback();

                CompleteTransactionTelemetry(DatabaseTransactionStatus.RolledBack);
            }

            SetStatus(DatabaseTransactionStatus.RolledBack);
            Dispose();
        }
        catch (Exception ex)
        {
            FailTransactionTelemetry(DatabaseTransactionStatus.RolledBack, ex);
            throw;
        }
    }

    private void Close()
    {
        if (Status == DatabaseTransactionStatus.Open)
        {
            if (DbTransaction?.Connection?.State == ConnectionState.Open)
                DbTransaction.Rollback();

            CompleteTransactionTelemetry(DatabaseTransactionStatus.RolledBack);
            SetStatus(DatabaseTransactionStatus.RolledBack);
        }

        dbConnection?.Close();
    }

    public override void Dispose()
    {
        Close();

        dbConnection?.Dispose();
        DbTransaction?.Dispose();
    }
}
