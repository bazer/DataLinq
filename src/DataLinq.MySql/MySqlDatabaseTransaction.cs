using DataLinq.Mutation;
using MySqlConnector;
using System;
using System.Data;

namespace DataLinq.MySql
{
    /// <summary>
    /// Represents a transaction for a MySQL database, encapsulating the logic to execute commands with transactional support.
    /// </summary>
    public class MySqlDatabaseTransaction : DatabaseTransaction
    {
        private IDbConnection? dbConnection;

        /// <summary>
        /// Initializes a new instance of the MySqlDatabaseTransaction class with the specified connection string and transaction type.
        /// </summary>
        /// <param name="connectionString">The connection string to the MySQL database.</param>
        /// <param name="type">The type of transaction to be performed.</param>
        public MySqlDatabaseTransaction(string connectionString, TransactionType type) : base(connectionString, type)
        {
        }

        /// <summary>
        /// Initializes a new instance of the MySqlDatabaseTransaction class with the specified database transaction and transaction type.
        /// Ensures that the provided transaction is valid and the connection is open.
        /// </summary>
        /// <param name="dbTransaction">The existing database transaction.</param>
        /// <param name="type">The type of transaction to be performed.</param>
        public MySqlDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type) : base(dbTransaction, type)
        {
            if (dbTransaction.Connection == null) throw new ArgumentNullException("dbTransaction.Connection", "The transaction connection is null");
            if (dbTransaction.Connection is not MySqlConnection) throw new ArgumentException("The transaction connection must be an MySqlConnection", "dbTransaction.Connection");
            if (dbTransaction.Connection.State != ConnectionState.Open) throw new Exception("The transaction connection is not open");

            SetStatus(DatabaseTransactionStatus.Open);
            dbConnection = dbTransaction.Connection;
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
                    SetStatus(DatabaseTransactionStatus.Open);
                    dbConnection = new MySqlConnection(ConnectionString);
                    dbConnection.Open();
                    DbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);
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
            try
            {
                command.Connection = DbConnection;
                command.Transaction = DbTransaction;
                return command.ExecuteNonQuery();
            }
            catch (Exception)
            {
                throw;
            }
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
        public override T ExecuteScalar<T>(string query)
        {
            var result = ExecuteScalar(new MySqlCommand(query));
#pragma warning disable CS8603 // Possible null reference return.
            return result == null ? default : (T)result;
#pragma warning restore CS8603 // Possible null reference return.
        }

        /// <summary>
        /// Executes a SQL command that returns a single value of type T, using the provided IDbCommand.
        /// </summary>
        /// <typeparam name="T">The expected return type of the scalar result.</typeparam>
        /// <param name="command">The IDbCommand to execute.</param>
        /// <returns>The result cast to the type T, or default(T) if the result is DBNull or null.</returns>
        public override T ExecuteScalar<T>(IDbCommand command)
        {
            var result = ExecuteScalar(command);
#pragma warning disable CS8603 // Possible null reference return.
            return result == null ? default : (T)result;
#pragma warning restore CS8603 // Possible null reference return.
        }

        /// <summary>
        /// Executes a SQL command that returns a single value, using the provided IDbCommand.
        /// </summary>
        /// <param name="command">The IDbCommand to execute.</param>
        /// <returns>The first column of the first row in the result set returned by the command, or null if the result is DBNull.</returns>
        public override object? ExecuteScalar(IDbCommand command)
        {
            try
            {
                command.Connection = DbConnection;
                command.Transaction = DbTransaction;
                var result = command.ExecuteScalar();
                return result == DBNull.Value ? null : result;
            }
            catch (Exception)
            {
                // TODO: Implement specific exception handling or logging here.
                throw;
            }
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
            try
            {
                command.Connection = DbConnection;
                command.Transaction = DbTransaction;

                //return command.ExecuteReader() as IDataLinqDataReader;
                return new MySqlDataLinqDataReader(command.ExecuteReader() as MySqlDataReader);
            }
            catch (Exception)
            {
                //Rollback();
                throw;
            }
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
}