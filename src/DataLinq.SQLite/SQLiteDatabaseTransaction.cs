using System;
using System.Data;
using DataLinq.Logging;
using DataLinq.Mutation;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

public class SQLiteDatabaseTransaction : DatabaseTransaction
{
    private IDbConnection dbConnection;
    private readonly string connectionString;
    readonly DataLinqLoggingConfiguration _loggingConfiguration;

    //private SqliteTransaction dbTransaction;

    public SQLiteDatabaseTransaction(string connectionString, TransactionType type, DataLinqLoggingConfiguration loggingConfiguration) : base(type)
    {
        this.connectionString = connectionString;
        _loggingConfiguration = loggingConfiguration;
    }

    public SQLiteDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type, DataLinqLoggingConfiguration loggingConfiguration) : base(dbTransaction, type)
    {
        if (dbTransaction.Connection == null) throw new ArgumentNullException("dbTransaction.Connection", "The transaction connection is null");
        if (dbTransaction.Connection is not SqliteConnection) throw new ArgumentException("The transaction connection must be an SqliteConnection", "dbTransaction.Connection");
        if (dbTransaction.Connection.State != ConnectionState.Open) throw new Exception("The transaction connection is not open");
        _loggingConfiguration = loggingConfiguration;

        SetStatus(DatabaseTransactionStatus.Open);
        dbConnection = dbTransaction.Connection;
    }

    private IDbConnection DbConnection
    {
        get
        {
            if (Status == DatabaseTransactionStatus.Committed || Status == DatabaseTransactionStatus.RolledBack)
                throw new Exception("Can't open a new connection on a committed or rolled back transaction");

            if (Status == DatabaseTransactionStatus.Closed)
            {
                SetStatus(DatabaseTransactionStatus.Open);
                dbConnection = new SqliteConnection(connectionString);
                dbConnection.Open();
                SetIsolationLevel((dbConnection as SqliteConnection)!, IsolationLevel.ReadUncommitted);

                DbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadUncommitted);
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
        command.Connection = DbConnection;
        command.Transaction = DbTransaction;
        Log.SqlCommand(_loggingConfiguration.SqlCommandLogger, command);
        return command.ExecuteNonQuery();
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
        command.Connection = DbConnection;
        command.Transaction = DbTransaction;
        Log.SqlCommand(_loggingConfiguration.SqlCommandLogger, command);
        return command.ExecuteScalar();
    }

    public override IDataLinqDataReader ExecuteReader(string query)
    {
        return ExecuteReader(new SqliteCommand(query));
    }

    /// <summary>
    /// Close this reader when done! (or use a using-statement)
    /// </summary>
    /// <param name="command"></param>
    /// <returns></returns>
    public override IDataLinqDataReader ExecuteReader(IDbCommand command)
    {
        command.Connection = DbConnection;
        command.Transaction = DbTransaction;
        Log.SqlCommand(_loggingConfiguration.SqlCommandLogger, command);

        //return command.ExecuteReader() as IDataLinqDataReader;
        return new SQLiteDataLinqDataReader(command.ExecuteReader() as SqliteDataReader);
    }

    public override void Commit()
    {
        if (Status == DatabaseTransactionStatus.Open)
        {
            if (DbTransaction?.Connection?.State == ConnectionState.Open)
                DbTransaction.Commit();
        }

        SetStatus(DatabaseTransactionStatus.Committed);

        Dispose();
    }

    public override void Rollback()
    {
        if (Status == DatabaseTransactionStatus.Open)
        {
            if (DbTransaction?.Connection?.State == ConnectionState.Open)
                DbTransaction?.Rollback();
        }

        SetStatus(DatabaseTransactionStatus.RolledBack);

        Dispose();
    }

    private void Close()
    {
        if (Status == DatabaseTransactionStatus.Open)
        {
            if (DbTransaction?.Connection?.State == ConnectionState.Open)
                DbTransaction?.Rollback();

            SetStatus(DatabaseTransactionStatus.RolledBack);
        }

        dbConnection?.Close();
    }

    #region IDisposable Members

    public override void Dispose()
    {
        Close();

        dbConnection?.Dispose();
        DbTransaction?.Dispose();
    }

    #endregion IDisposable Members
}