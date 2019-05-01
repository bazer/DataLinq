using Modl.Db.Query;
using MySql.Data.MySqlClient;
using Slim;
using Slim.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Slim.MySql
{
    public class MySqlDatabaseTransaction : DatabaseTransaction
    {
        private MySqlConnection dbConnection;
        private MySqlTransaction dbTransaction;
        //private IsTransactionPending {get;} => dbTransaction != null;

        public MySqlDatabaseTransaction(string connectionString, TransactionType type) : base(connectionString, type)
        {
        }

        //public MySqlTransactionProvider(string connectionString): base(connectionString)
        //{
        //}

        //public bool IsTransactionPending { get; private set; }

        //private MySqlTransaction GetTransaction()
        //{
        //    if (!IsTransactionPending)
        //    {
        //        //dbTransaction = new CommittableTransaction(new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted });

        //        IsTransactionPending = true;
        //    }

        //    return dbTransaction;
        //}

        //private MySqlConnection GetConnection()
        //{
        //    if (Type == TransactionType.NoTransaction)
        //    {
        //        return 
        //    }

        //    var dbConnection = new MySqlConnection(ConnectionString);
        //    dbConnections.TryAdd(dbConnection.GetHashCode(), dbConnection);
        //    dbConnection.StateChange += DbConnection_StateChange;

        //    if (Type != TransactionType.NoTransaction)
        //        dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);

        //    //dbConnection.Open();
        //    //dbConnection.EnlistTransaction(GetTransaction());
            

        //    return dbConnection;
        //}

        //private void DbConnection_StateChange(object sender, StateChangeEventArgs e)
        //{
        //    var conn = sender as MySqlConnection;
        //    Debug.WriteLine($"{conn?.GetHashCode()} {e.OriginalState} -> {e.CurrentState}");

        //    if (Type == TransactionType.ReadOnly && conn.State == ConnectionState.Closed)
        //    {
        //        dbConnections.TryRemove(conn.GetHashCode(), out var temp);
        //        conn.Dispose();
        //    }

        //    if (Type == TransactionType.ReadOnly && dbConnections.Count == 0)
        //        Commit();


        //}

        private MySqlConnection DbConnection
        {
            get
            {
                if (dbConnection == null || dbTransaction == null || !IsTransactionPending)
                {
                    IsTransactionPending = true;
                    dbConnection = new MySqlConnection(ConnectionString);
                    dbConnection.Open();
                    dbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);
                }

                return dbConnection;
            }
        }

        //public static void Commit(Action<DbTransaction> action)
        //{
        //    using (var transaction = new DbTransaction())
        //    {
        //        action(transaction);
        //        transaction.Commit();
        //    }
        //}

        //public static T Commit<T>(Func<DbTransaction, T> func)
        //{
        //    using (var transaction = new DbTransaction())
        //    {
        //        var result = func(transaction);
        //        transaction.Commit();

        //        return result;
        //    }
        //}

        //public static int ExecuteNonQueryWithCommit(List<AdHocStatement> statements)
        //{
        //    var dbTrans = new DbTransaction();
        //    var result = dbTrans.ExecuteNonQuery(statements);
        //    dbTrans.Commit();
        //    return result;
        //}





        //public int ExecuteNonQuery(params AdHocStatement[] statements) =>
        //    ExecuteNonQuery(new List<AdHocStatement>(statements));

        //public int ExecuteNonQuery(List<AdHocStatement> statements)
        //{
        //    if (statements.Count == 0)
        //        return 0;

        //    return ExecuteNonQuery(AdHocStatement.GetSqlCommand(statements));
        //}

        public override int ExecuteNonQuery(string query)
        {
            var command = new MySqlCommand(query);

            return ExecuteNonQuery(command);
        }

        public override int ExecuteNonQuery(IDbCommand cmd)
        {
            try
            {
                cmd.Connection = DbConnection;
                cmd.Transaction = dbTransaction;
                return cmd.ExecuteNonQuery();
            }
            catch (Exception)
            {
                //Rollback();
                throw;
            }
        }

        //public MySqlDataReader ExecuteReader(params AdHocStatement[] statements)
        //    => ExecuteReader(new List<AdHocStatement>(statements));

        //public MySqlDataReader ExecuteReader(List<AdHocStatement> statements)
        //    => ExecuteReader(AdHocStatement.GetSqlCommand(statements));

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

                var reader = command.ExecuteReader() as DbDataReader;

                return reader;
            }
            catch (Exception)
            {
                //Rollback();
                throw;
            }
        }

        //public T ExecuteScalar<T>(params AdHocStatement[] statements)
        //    => ExecuteScalar<T>(new List<AdHocStatement>(statements));

        //public T ExecuteScalar<T>(List<AdHocStatement> statements)
        //    => ExecuteScalar<T>(AdHocStatement.GetSqlCommand(statements));

        //public T ExecuteScalar<T>(MySqlCommand command)
        //{
        //    try
        //    {
        //        command.Connection = DbConnection;
        //        command.Transaction = dbTransaction;
        //        var result = command.ExecuteScalar();
        //        return (T)Convert.ChangeType(result, typeof(T));
        //    }
        //    catch (Exception)
        //    {
        //        Rollback();
        //        throw;
        //    }
        //}

        public override void Commit()
        {
            if (IsTransactionPending)
            {
                dbTransaction.Commit();
                IsTransactionPending = false;
            }

            Dispose();
        }

        public override void Rollback()
        {
            if (IsTransactionPending)
            {
                IsTransactionPending = false;
                dbTransaction?.Rollback();
            }

            Dispose();
        }

        private void Close()
        {
            if (IsTransactionPending)
            {
                IsTransactionPending = false;
                dbTransaction?.Rollback();

                //if (dbConnections.Any(x => x.Value.State == ConnectionState.Open))
                //    dbTransaction?.Rollback();

                //throw (new Exception("TransactionalUpdate: Transaction pending, cannot close!"));
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

        #endregion
    }
}