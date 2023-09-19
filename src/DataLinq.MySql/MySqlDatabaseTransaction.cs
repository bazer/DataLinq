using DataLinq.Mutation;
using MySqlConnector;
using System;
using System.Data;

namespace DataLinq.MySql
{
    public class MySqlDatabaseTransaction : DatabaseTransaction
    {
        private IDbConnection dbConnection;
        //private MySqlTransaction dbTransaction;

        public MySqlDatabaseTransaction(string connectionString, TransactionType type) : base(connectionString, type)
        {
        }

        public MySqlDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type) : base(dbTransaction, type)
        {
            if (dbTransaction.Connection == null) throw new ArgumentNullException("dbTransaction.Connection", "The transaction connection is null");
            if (dbTransaction.Connection is not MySqlConnection) throw new ArgumentException("The transaction connection must be an MySqlConnection", "dbTransaction.Connection");
            if (dbTransaction.Connection.State != ConnectionState.Open) throw new Exception("The transaction connection is not open");

            Status = DatabaseTransactionStatus.Open;
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
                    Status = DatabaseTransactionStatus.Open;
                    dbConnection = new MySqlConnection(ConnectionString);
                    dbConnection.Open();
                    DbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);
                }

                return dbConnection;
            }
        }

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
            try
            {
                command.Connection = DbConnection;
                command.Transaction = DbTransaction;
                return command.ExecuteScalar();
            }
            catch (Exception)
            {
                throw;
            }
        }

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

        public override void Commit()
        {
            if (Status == DatabaseTransactionStatus.Open)
            {
                DbTransaction.Commit();
            }

            Status = DatabaseTransactionStatus.Committed;

            Dispose();
        }

        public override void Rollback()
        {
            if (Status == DatabaseTransactionStatus.Open)
            {
                DbTransaction?.Rollback();
            }

            Status = DatabaseTransactionStatus.RolledBack;

            Dispose();
        }

        private void Close()
        {
            if (Status == DatabaseTransactionStatus.Open)
            {
                DbTransaction?.Rollback();
                Status = DatabaseTransactionStatus.RolledBack;
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
}