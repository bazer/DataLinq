using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using System;
using System.Linq;

namespace DataLinq
{
    public abstract class Database<T> : IDisposable
        where T : class, IDatabaseModel
    {
        //public static Database<T> GetDatabase(DatabaseType)

        public DatabaseType DatabaseType => Provider.DatabaseType;
        public DatabaseProvider<T> Provider { get; }

        public Database(DatabaseProvider<T> provider)
        {
            this.Provider = provider;
        }

        public bool FileOrServerExists()
        {
            return Provider.FileOrServerExists();
        }

        public bool Exists(string databaseName = null)
        {
            return Transaction(TransactionType.ReadOnly)
                .DbTransaction
                .ReadReader(Provider.GetExists(databaseName))
                .Any();
        }

        public Transaction<T> Transaction(TransactionType transactionType = TransactionType.ReadAndWrite)
        {
            return new Transaction<T>(this.Provider, transactionType);
        }

        public T Query()
        {
            return Transaction(TransactionType.ReadOnly).Query();
        }

        public SqlQuery From(string tableName, string alias = null)
        {
            if (alias == null)
                (tableName, alias) = QueryUtils.ParseTableNameAndAlias(tableName);

            var transaction = Transaction(TransactionType.ReadOnly);
            var table = transaction.Provider.Metadata.TableModels.Single(x => x.Table.DbName == tableName).Table;

            return new SqlQuery(table, transaction, alias);
        }

        public SqlQuery From(TableMetadata table, string alias = null)
        {
            return new SqlQuery(table, Transaction(TransactionType.ReadOnly), alias);
        }

        public SqlQuery<V> From<V>() where V: IModel
        {
            return Transaction(TransactionType.ReadOnly).From<V>();
        }

        public M Insert<M>(M model, TransactionType transactionType = TransactionType.ReadAndWrite) where M : IModel
        {
            return Commit(transaction => transaction.Insert(model), transactionType);
        }

        public M Update<M>(M model, TransactionType transactionType = TransactionType.ReadAndWrite) where M : IModel
        {
            return Commit(transaction => transaction.Update(model), transactionType);
        }

        public M Update<M>(M model, Action<M> changes, TransactionType transactionType = TransactionType.ReadAndWrite) where M : IModel
        {
            return Commit(transaction => transaction.Update(model, changes), transactionType);
        }

        public M InsertOrUpdate<M>(M model, TransactionType transactionType = TransactionType.ReadAndWrite) where M : IModel
        {
            return Commit(transaction => transaction.InsertOrUpdate(model), transactionType);
        }

        public M InsertOrUpdate<M>(M model, Action<M> changes, TransactionType transactionType = TransactionType.ReadAndWrite) where M : IModel, new()
        {
            return Commit(transaction => transaction.InsertOrUpdate(model, changes), transactionType);
        }

        public void Delete<M>(M model, TransactionType transactionType = TransactionType.ReadAndWrite) where M : IModel
        {
            Commit(transaction => transaction.Delete(model), transactionType);
        }

        public void Commit(Action<Transaction> func, TransactionType transactionType = TransactionType.ReadAndWrite)
        {
            using var transaction = Transaction(transactionType);
            func(transaction);
            transaction.Commit();
        }

        public M Commit<M>(Func<Transaction, M> func, TransactionType transactionType = TransactionType.ReadAndWrite) where M : IModel
        {
            using var transaction = Transaction(transactionType);
            var result = func(transaction);
            transaction.Commit();

            return result;
        }

        public void Dispose()
        {
            Provider.Dispose();
        }
    }
}
