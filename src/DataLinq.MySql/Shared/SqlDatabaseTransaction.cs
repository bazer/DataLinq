using System;
using System.Data;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Mutation;
using MySqlConnector;

namespace DataLinq.MySql;

/// <summary>
/// Represents a transaction for a MySQL database, encapsulating the logic to execute commands with transactional support.
/// </summary>
public class SqlDatabaseTransaction : DatabaseTransaction
{
    private IDbConnection? dbConnection;
    private readonly string databaseName;
    private readonly MySqlDataSource? dataSource;
    private readonly DataLinqLoggingConfiguration loggingConfiguration;

    public SqlDatabaseTransaction(MySqlDataSource dataSource, TransactionType type, string databaseName, DataLinqLoggingConfiguration loggingConfiguration)
        : this(null, dataSource, type, databaseName, loggingConfiguration)
    {
    }

    internal SqlDatabaseTransaction(IDatabaseProvider? databaseProvider, MySqlDataSource dataSource, TransactionType type, string databaseName, DataLinqLoggingConfiguration loggingConfiguration)
        : base(databaseProvider, type)
    {
        this.dataSource = dataSource;
        this.databaseName = databaseName;
        this.loggingConfiguration = loggingConfiguration;
    }

    public SqlDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type, string databaseName, DataLinqLoggingConfiguration loggingConfiguration)
        : this(null, dbTransaction, type, databaseName, loggingConfiguration)
    {
    }

    internal SqlDatabaseTransaction(IDatabaseProvider? databaseProvider, IDbTransaction dbTransaction, TransactionType type, string databaseName, DataLinqLoggingConfiguration loggingConfiguration)
        : base(databaseProvider, dbTransaction, type)
    {
        if (dbTransaction.Connection == null) throw new ArgumentNullException("dbTransaction.Connection", "The transaction connection is null");
        if (dbTransaction.Connection is not MySqlConnection) throw new ArgumentException("The transaction connection must be an MySqlConnection", "dbTransaction.Connection");
        if (dbTransaction.Connection.State != ConnectionState.Open) throw new Exception("The transaction connection is not open");

        SetStatus(DatabaseTransactionStatus.Open);
        dbConnection = dbTransaction.Connection;
        this.databaseName = databaseName;
        this.loggingConfiguration = loggingConfiguration;
        BeginTransactionTelemetry();
    }

    private IDbConnection DbConnection
    {
        get
        {
            if (Status == DatabaseTransactionStatus.Committed || Status == DatabaseTransactionStatus.RolledBack)
                throw new Exception("Cannot open a new connection on a committed or rolled back transaction.");

            if (Status == DatabaseTransactionStatus.Closed)
            {
                if (dataSource == null)
                    throw new Exception("The data source is null");

                SetStatus(DatabaseTransactionStatus.Open);
                dbConnection = dataSource.OpenConnection();
                DbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);
                BeginTransactionTelemetry();

                if (databaseName != null)
                    ExecuteNonQuery($"USE `{databaseName}`;");
            }

            if (dbConnection == null)
                throw new Exception("The database connection is null");

            return dbConnection;
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
        ExecuteNonQuery(new MySqlCommand(query));

    public override object? ExecuteScalar(string query) =>
        ExecuteScalar(new MySqlCommand(query));

    public override T ExecuteScalar<T>(string query) =>
        ExecuteScalar<T>(new MySqlCommand(query));

    public override T ExecuteScalar<T>(IDbCommand command) =>
        (T)(ExecuteScalar(command) ?? default(T)!);

    public override object? ExecuteScalar(IDbCommand command)
    {
        command.Connection = DbConnection;
        command.Transaction = DbTransaction;
        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);
        var result = ExecuteCommandWithTelemetry(command, "scalar", transactional: true, Type, command.ExecuteScalar);
        return result == DBNull.Value ? null : result;
    }

    public override IDataLinqDataReader ExecuteReader(string query)
    {
        return ExecuteReader(new MySqlCommand(query));
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
            () => command.ExecuteReader() as MySqlDataReader);

        return new SqlDataLinqDataReader(reader!);
    }

    public override void Commit()
    {
        try
        {
            if (Status == DatabaseTransactionStatus.Open)
            {
                if (DbTransaction?.Connection?.State == ConnectionState.Open)
                    DbTransaction.Commit();

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
                if (DbTransaction?.Connection?.State == ConnectionState.Open)
                    DbTransaction.Rollback();

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
