using MySqlConnector;
using Slim.Mutation;
using System;
using System.Data;
using System.Data.Common;

namespace Slim.MySql
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
                if (dbConnection == null || dbTransaction == null || !IsTransactionPending)
                {
                    Status = DatabaseTransactionStatus.Open;
                    IsTransactionPending = true;
                    dbConnection = new MySqlConnection(ConnectionString);
                    dbConnection.Open();
                    dbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);
                }

                return dbConnection;
            }
        }

        public override int ExecuteNonQuery(string query)
        {
            var command = new MySqlCommand(query);

            return ExecuteNonQuery(command);
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

        public override DbDataReader ExecuteReader(string query)
        {
            return ExecuteReader(new MySqlCommand(query));
        }

        /// <summary>
        /// Close this reader when done! (or use a using-statement)
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        public override DbDataReader ExecuteReader(IDbCommand command)
        {
            try
            {
                command.Connection = DbConnection;
                command.Transaction = dbTransaction;

                return command.ExecuteReader() as DbDataReader;
            }
            catch (Exception)
            {
                //Rollback();
                throw;
            }
        }

        public override void Commit()
        {
            if (IsTransactionPending)
            {
                dbTransaction.Commit();
                IsTransactionPending = false;
                Status = DatabaseTransactionStatus.Committed;
            }

            Dispose();
        }

        public override void Rollback()
        {
            if (IsTransactionPending)
            {
                IsTransactionPending = false;
                Status = DatabaseTransactionStatus.RolledBack;
                dbTransaction?.Rollback();
            }

            Dispose();
        }

        private void Close()
        {
            if (IsTransactionPending)
            {
                IsTransactionPending = false;
                Status = DatabaseTransactionStatus.RolledBack;
                dbTransaction?.Rollback();
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