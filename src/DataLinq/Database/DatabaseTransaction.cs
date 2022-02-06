using DataLinq.Mutation;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;

namespace DataLinq
{
    public enum DatabaseTransactionStatus
    {
        Closed,
        Open,
        Committed,
        RolledBack
    }

    public abstract class DatabaseTransaction : IDisposable
    {
        public DatabaseTransactionStatus Status { get; protected set; } = DatabaseTransactionStatus.Closed;
        public bool IsTransactionPending { get; protected set; }
        public string ConnectionString { get; }

        public TransactionType Type { get; protected set; }

        protected DatabaseTransaction(string connectionString, TransactionType type)
        {
            ConnectionString = connectionString;
            Type = type;
        }

        public abstract IDataLinqDataReader ExecuteReader(IDbCommand command);
        public abstract IDataLinqDataReader ExecuteReader(string query);
        public abstract object ExecuteScalar(IDbCommand command);
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
