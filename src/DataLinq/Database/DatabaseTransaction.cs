using DataLinq.Mutation;
using System;
using System.Collections.Generic;
using System.Data;

namespace DataLinq
{
    public enum DatabaseTransactionStatus
    {
        Closed,
        Open,
        Committed,
        RolledBack
    }

    public class DatabaseTransactionStatusChangeEventArgs : EventArgs
    {
        public DatabaseTransactionStatus Status { get; set; }
    }

    public abstract class DatabaseTransaction : IDisposable
    {
        public DatabaseTransactionStatus Status { get; private set; } = DatabaseTransactionStatus.Closed;

        public event EventHandler<DatabaseTransactionStatusChangeEventArgs>? OnStatusChanged;
        public string ConnectionString { get; }
        public IDbTransaction DbTransaction { get; protected set; }
        public TransactionType Type { get; protected set; }

        protected DatabaseTransaction(string connectionString, TransactionType type)
        {
            ConnectionString = connectionString;
            Type = type;
        }

        protected DatabaseTransaction(IDbTransaction dbTransaction, TransactionType type)
        {
            DbTransaction = dbTransaction ?? throw new ArgumentNullException(nameof(dbTransaction));
            Type = type;
        }

        protected void SetStatus(DatabaseTransactionStatus status)
        {
            this.Status = status;
            OnStatusChanged?.Invoke(this, new DatabaseTransactionStatusChangeEventArgs { Status = status });
        }

        
        public abstract IDataLinqDataReader ExecuteReader(IDbCommand command);
        public abstract IDataLinqDataReader ExecuteReader(string query);
        public abstract object? ExecuteScalar(IDbCommand command);
        public abstract T ExecuteScalar<T>(IDbCommand command);
        public abstract object? ExecuteScalar(string query);
        public abstract T ExecuteScalar<T>(string query);
        public abstract int ExecuteNonQuery(IDbCommand command);
        public abstract int ExecuteNonQuery(string query);

        public IEnumerable<IDataLinqDataReader> ReadReader(IDbCommand command)
        {
            using (var reader = ExecuteReader(command))
            {
                while (reader.Read())
                    yield return reader;
            }
        }

        public IEnumerable<IDataLinqDataReader> ReadReader(string query)
        {
            using (var reader = ExecuteReader(query))
            {
                while (reader.Read())
                    yield return reader;
            }
        }

        public abstract void Rollback();
        public abstract void Commit();
        public abstract void Dispose();
    }
}
