using DataLinq.Mutation;
using Microsoft.Data.Sqlite;
using System;
using System.Data;

namespace DataLinq.SQLite
{
    public class SQLiteDatabaseTransaction : DatabaseTransaction
    {
        private IDbConnection dbConnection;
        //private SqliteTransaction dbTransaction;

        public SQLiteDatabaseTransaction(string connectionString, TransactionType type) : base(connectionString, type)
        {
        }

        public SQLiteDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type) : base(dbTransaction, type)
        {
            if (dbTransaction.Connection == null) throw new ArgumentNullException("dbTransaction.Connection", "The transaction connection is null");
            if (dbTransaction.Connection is not SqliteConnection) throw new ArgumentException("The transaction connection must be an SqliteConnection", "dbTransaction.Connection");
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
                    dbConnection = new SqliteConnection(ConnectionString);
                    dbConnection.Open();

                    using (var command = new SqliteCommand("PRAGMA journal_mode=WAL;", dbConnection as SqliteConnection))
                    {
                        command.ExecuteNonQuery();
                    }

                    DbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadUncommitted);
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
            ExecuteNonQuery(new SqliteCommand(query));

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
                command.Transaction = DbTransaction;

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