using MySqlConnector;
using DataLinq.Mutation;
using System;
using System.Data;
using System.Data.Common;

namespace DataLinq.MySql
{
    public class MySqlDatabaseTransaction : DatabaseTransaction
    {
        private MySqlConnection dbConnection;
        private MySqlTransaction dbTransaction;

        public MySqlDatabaseTransaction(string connectionString, TransactionType type) : base(connectionString, type)
        {
        }

        private MySqlConnection DbConnection
        {
            get
            {
                if (Status == DatabaseTransactionStatus.Committed || Status == DatabaseTransactionStatus.RolledBack)
                    throw new Exception("Can't open a new connection on a committed or rolled back transaction");

                if (Status == DatabaseTransactionStatus.Closed) //dbConnection == null || dbTransaction == null || !IsTransactionPending)
                {
                    Status = DatabaseTransactionStatus.Open;
                    //IsTransactionPending = true;
                    dbConnection = new MySqlConnection(ConnectionString);
                    dbConnection.Open();
                    dbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);
                }

                return dbConnection;
            }
        }

        public override int ExecuteNonQuery(IDbCommand command)
        {
            try
            {
                command.Connection = DbConnection;
                command.Transaction = dbTransaction;
                return command.ExecuteNonQuery();
            }
            catch (Exception)
            {
                //Rollback();
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
                command.Transaction = dbTransaction;
                return command.ExecuteScalar();
            }
            catch (Exception)
            {
                //Rollback();
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
                command.Transaction = dbTransaction;

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
                dbTransaction.Commit();
            }

            Status = DatabaseTransactionStatus.Committed;

            Dispose();
        }

        public override void Rollback()
        {
            if (Status == DatabaseTransactionStatus.Open)
            {
                dbTransaction?.Rollback();
            }

            Status = DatabaseTransactionStatus.RolledBack;

            Dispose();
        }

        private void Close()
        {
            if (Status == DatabaseTransactionStatus.Open)
            {
                dbTransaction?.Rollback();
                Status = DatabaseTransactionStatus.RolledBack;
            }

            dbConnection?.Close();
        }

        #region IDisposable Members

        public override void Dispose()
        {
            Close();

            dbConnection?.Dispose();
            dbTransaction?.Dispose();
        }

        #endregion IDisposable Members
    }
}