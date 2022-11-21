using DataLinq.Mutation;
using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite
{
    public class SQLiteDatabaseTransaction : DatabaseTransaction
    {
        private SqliteConnection dbConnection;
        private SqliteTransaction dbTransaction;

        public SQLiteDatabaseTransaction(string connectionString, TransactionType type) : base(connectionString, type)
        {
        }

        private SqliteConnection DbConnection
        {
            get
            {
                if (Status == DatabaseTransactionStatus.Committed || Status == DatabaseTransactionStatus.RolledBack)
                    throw new Exception("Can't open a new connection on a committed or rolled back transaction");

                if (Status == DatabaseTransactionStatus.Closed)
                {
                    Status = DatabaseTransactionStatus.Open;
                    dbConnection = new SqliteConnection(ConnectionString);
                    dbConnection.Open();
                    dbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);
                }

                return dbConnection;
            }
        }

        public override int ExecuteNonQuery(string query)
        {
            var command = new SqliteCommand(query);

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

        public override object ExecuteScalar(string query) =>
            ExecuteScalar(new SqliteCommand(query));

        public override T ExecuteScalar<T>(string query) =>
            (T)ExecuteScalar(new SqliteCommand(query));

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
            return ExecuteReader(new SqliteCommand(query));
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
                return new SQLiteDataLinqDataReader(command.ExecuteReader() as SqliteDataReader);
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