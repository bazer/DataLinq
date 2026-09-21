using System;
using System.Data;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Mutation;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

public class SQLiteDatabaseTransaction : DatabaseTransaction, ISyncTransactionCompletionResource
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
            EnsureSynchronousResourceUsable();
            if (Status == DatabaseTransactionStatus.Committed || Status == DatabaseTransactionStatus.RolledBack)
                throw new Exception("Can't open a new connection on a committed or rolled back transaction");

            if (Status == DatabaseTransactionStatus.Closed)
            {
                if (connectionString == null)
                    throw new InvalidOperationException("Attached SQLite transactions cannot be reopened after they are closed because DataLinq does not own their connection string.");

                SetStatus(DatabaseTransactionStatus.Open);
                dbConnection = new SqliteConnection(connectionString);
                dbConnection.Open();
                SQLiteConnectionPolicy.ApplyCommittedVisibility(
                    (SqliteConnection)dbConnection,
                    command => ExecuteCommandWithTelemetry(
                        command,
                        "non_query",
                        transactional: false,
                        transactionType: null,
                        command.ExecuteNonQuery));
                DbTransaction = ((SqliteConnection)dbConnection).BeginTransaction(
                    SQLiteConnectionPolicy.OwnedTransactionIsolationLevel,
                    deferred: true);
                BeginTransactionTelemetry();
            }

            return dbConnection;
        }
    }

    public override int ExecuteNonQuery(IDbCommand command)
    {
        command.Connection = DbConnection;
        command.Transaction = DbTransaction;
        Log.SqlCommand(loggingConfiguration, command);
        return ExecuteCommandWithTelemetry(command, "non_query", transactional: true, Type, command.ExecuteNonQuery);
    }

    public override int ExecuteNonQuery(string query)
    {
        using var command = new SqliteCommand(query);
        return ExecuteNonQuery(command);
    }

    public override object ExecuteScalar(string query)
    {
        using var command = new SqliteCommand(query);
        return ExecuteScalar(command)!;
    }

    public override T ExecuteScalar<T>(string query)
    {
        using var command = new SqliteCommand(query);
        return (T)ExecuteScalar(command)!;
    }

    public override T ExecuteScalar<T>(IDbCommand command) =>
        (T)ExecuteScalar(command)!;

    public override object ExecuteScalar(IDbCommand command)
    {
        command.Connection = DbConnection;
        command.Transaction = DbTransaction;
        Log.SqlCommand(loggingConfiguration, command);
        return ExecuteCommandWithTelemetry(command, "scalar", transactional: true, Type, command.ExecuteScalar)!;
    }

    public override IDataLinqDataReader ExecuteReader(string query)
    {
        return ExecuteOwnedReader(new SqliteCommand(query));
    }

    public override IDataLinqDataReader ExecuteReader(IDbCommand command)
    {
        command.Connection = DbConnection;
        command.Transaction = DbTransaction;
        Log.SqlCommand(loggingConfiguration, command);

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

    public override void Commit() => CompleteSynchronousTransaction(this, rollback: false);

    public override void Rollback() => CompleteSynchronousTransaction(this, rollback: true);

    public override void Dispose() => DisposeSynchronousTransaction(this);

    void ISyncTransactionCompletionResource.Complete(bool rollback)
    {
        var native = GetActiveProviderTransaction(rollback ? "roll back" : "commit");
        if (rollback) native.Rollback();
        else native.Commit();
    }

    bool ISyncTransactionCompletionResource.RollbackForDisposal()
    {
        if (DbTransaction?.Connection?.State != ConnectionState.Open) return false;
        DbTransaction.Rollback();
        return true;
    }

    void ISyncTransactionCompletionResource.CloseConnection() => dbConnection?.Close();
    void ISyncTransactionCompletionResource.DisposeConnection() => dbConnection?.Dispose();
    void ISyncTransactionCompletionResource.DisposeTransaction() => DbTransaction?.Dispose();
}
