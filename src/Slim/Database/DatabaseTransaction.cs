using Slim.Mutation;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;

namespace Slim
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

        public abstract DbDataReader ExecuteReader(IDbCommand command);
        public abstract DbDataReader ExecuteReader(string query);
        public abstract int ExecuteNonQuery(IDbCommand command);
        public abstract int ExecuteNonQuery(string query);

        public IEnumerable<DbDataReader> ReadReader(IDbCommand command)
        {
            using (var reader = ExecuteReader(command))
            {
                while (reader.Read())
                    yield return reader;
            }
        }

        public IEnumerable<DbDataReader> ReadReader(string query)
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
