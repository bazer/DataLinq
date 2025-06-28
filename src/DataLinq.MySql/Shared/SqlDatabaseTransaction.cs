using System;
using System.Data;
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

    /// <summary>
    /// Initializes a new instance of the MySqlDatabaseTransaction class with the specified connection string and transaction type.
    /// </summary>
    /// <param name="connectionString">The connection string to the MySQL database.</param>
    /// <param name="type">The type of transaction to be performed.</param>
    public SqlDatabaseTransaction(MySqlDataSource dataSource, TransactionType type, string databaseName, DataLinqLoggingConfiguration loggingConfiguration) : base(type)
    {
        this.dataSource = dataSource;
        this.databaseName = databaseName;
        this.loggingConfiguration = loggingConfiguration;
    }

    /// <summary>
    /// Initializes a new instance of the MySqlDatabaseTransaction class with the specified database transaction and transaction type.
    /// Ensures that the provided transaction is valid and the connection is open.
    /// </summary>
    /// <param name="dbTransaction">The existing database transaction.</param>
    /// <param name="type">The type of transaction to be performed.</param>
    public SqlDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type, string databaseName, DataLinqLoggingConfiguration loggingConfiguration) : base(dbTransaction, type)
    {
        if (dbTransaction.Connection == null) throw new ArgumentNullException("dbTransaction.Connection", "The transaction connection is null");
        if (dbTransaction.Connection is not MySqlConnection) throw new ArgumentException("The transaction connection must be an MySqlConnection", "dbTransaction.Connection");
        if (dbTransaction.Connection.State != ConnectionState.Open) throw new Exception("The transaction connection is not open");

        SetStatus(DatabaseTransactionStatus.Open);
        dbConnection = dbTransaction.Connection;
        this.databaseName = databaseName;
        this.loggingConfiguration = loggingConfiguration;
    }

    /// <summary>
    /// Gets the underlying database connection for the transaction, ensuring it is open and valid.
    /// </summary>
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

                if (databaseName != null)
                    ExecuteNonQuery($"USE `{databaseName}`;");
            }

            if (dbConnection == null)
                throw new Exception("The database connection is null");

            return dbConnection;
        }
    }

    /// <summary>
    /// Executes a non-query SQL command within the context of the transaction.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <returns>The number of rows affected.</returns>
    public override int ExecuteNonQuery(IDbCommand command)
    {
        command.Connection = DbConnection;
        command.Transaction = DbTransaction;
        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);
        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// Executes a SQL command with a non-query statement such as INSERT, UPDATE, or DELETE.
    /// </summary>
    /// <param name="query">The SQL query string to execute.</param>
    /// <returns>The number of rows affected by the command.</returns>
    public override int ExecuteNonQuery(string query) =>
        ExecuteNonQuery(new MySqlCommand(query));

    /// <summary>
    /// Executes a SQL command that returns a single value, such as a COUNT or MAX.
    /// </summary>
    /// <param name="query">The SQL query string to execute.</param>
    /// <returns>The first column of the first row in the result set returned by the query.</returns>
    public override object? ExecuteScalar(string query) =>
        ExecuteScalar(new MySqlCommand(query));

    /// <summary>
    /// Executes a SQL command that returns a single value of type T.
    /// </summary>
    /// <typeparam name="T">The expected return type of the scalar result.</typeparam>
    /// <param name="query">The SQL query string to execute.</param>
    /// <returns>The result cast to the type T, or default(T) if the result is DBNull or null.</returns>
    public override T ExecuteScalar<T>(string query) =>
        ExecuteScalar<T>(new MySqlCommand(query));

    /// <summary>
    /// Executes a SQL command that returns a single value of type T, using the provided IDbCommand.
    /// </summary>
    /// <typeparam name="T">The expected return type of the scalar result.</typeparam>
    /// <param name="command">The IDbCommand to execute.</param>
    /// <returns>The result cast to the type T, or default(T) if the result is DBNull or null.</returns>
    public override T ExecuteScalar<T>(IDbCommand command) =>
        (T)(ExecuteScalar(command) ?? default(T)!);

    /// <summary>
    /// Executes a SQL command that returns a single value, using the provided IDbCommand.
    /// </summary>
    /// <param name="command">The IDbCommand to execute.</param>
    /// <returns>The first column of the first row in the result set returned by the command, or null if the result is DBNull.</returns>
    public override object? ExecuteScalar(IDbCommand command)
    {
        command.Connection = DbConnection;
        command.Transaction = DbTransaction;
        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);
        var result = command.ExecuteScalar();
        return result == DBNull.Value ? null : result;
    }


    /// <summary>
    /// Executes a SQL command that returns a result set, such as a SELECT query.
    /// </summary>
    /// <param name="query">The SQL query string to execute.</param>
    /// <returns>An IDataLinqDataReader that can be used to read the returned data.</returns>
    public override IDataLinqDataReader ExecuteReader(string query)
    {
        return ExecuteReader(new MySqlCommand(query));
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
        Log.SqlCommand(loggingConfiguration.SqlCommandLogger, command);

        return new SqlDataLinqDataReader((command.ExecuteReader() as MySqlDataReader)!);
    }

    /// <summary>
    /// Commits the transaction, ensuring it is open before attempting to commit.
    /// </summary>
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

    /// <summary>
    /// Rolls back the transaction, ensuring it is open before attempting to roll back.
    /// </summary>
    public override void Rollback()
    {
        if (Status == DatabaseTransactionStatus.Open)
        {
            if (DbTransaction?.Connection?.State == ConnectionState.Open)
                DbTransaction.Rollback();
        }

        SetStatus(DatabaseTransactionStatus.RolledBack);
        Dispose();
    }

    /// <summary>
    /// Closes the transaction and the underlying connection if open.
    /// </summary>
    private void Close()
    {
        if (Status == DatabaseTransactionStatus.Open)
        {
            if (DbTransaction?.Connection?.State == ConnectionState.Open)
                DbTransaction.Rollback();

            SetStatus(DatabaseTransactionStatus.RolledBack);
        }

        dbConnection?.Close();
    }

    #region IDisposable Members

    /// <summary>
    /// Releases all resources used by the MySqlDatabaseTransaction, rolling back the transaction if it is still open.
    /// </summary>
    public override void Dispose()
    {
        Close();
        dbConnection?.Dispose();
        DbTransaction?.Dispose();
    }

    #endregion IDisposable Members
}