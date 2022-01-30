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
        public DatabaseProvider<T> Provider { get; }

        public Database(DatabaseProvider<T> provider)
        {
            this.Provider = provider;
        }

        public Transaction<T> Transaction(TransactionType transactionType = TransactionType.ReadAndWrite)
        {
            return new Transaction<T>(this.Provider, transactionType);
        }

        public void Transact(Action<Transaction<T>> action, TransactionType transactionType = TransactionType.ReadAndWrite)
        {
            using (var transaction = Transaction(transactionType))
            {
                action(transaction);
                transaction.Commit();
            }
        }

        public T Query()
        {
            return Transaction(TransactionType.NoTransaction).Query();
        }

        public SqlQuery From(string tableName, string alias = null)
        {
            if (alias == null)
                (tableName, alias) = QueryUtils.ParseTableNameAndAlias(tableName);

            var transaction = Transaction(TransactionType.NoTransaction);
            var table = transaction.Provider.Metadata.Tables.Single(x => x.DbName == tableName);

            return new SqlQuery(table, transaction, alias);
        }

        public SqlQuery From(TableMetadata table, string alias = null)
        {
            var transaction = Transaction(TransactionType.NoTransaction);

            return new SqlQuery(table, transaction, alias);
        }

        public SqlQuery<V> From<V>() where V: IModel
        {
            var transaction = Transaction(TransactionType.NoTransaction);

            return transaction.From<V>();
        }

        public M Insert<M>(M model) where M : IModel
        {
            using var transaction = Transaction();
            var newModel = transaction.Insert(model);
            transaction.Commit();

            return newModel;
        }

        public M Update<M>(M model) where M : IModel
        {
            using var transaction = Transaction();
            var newModel = transaction.Update(model);
            transaction.Commit();

            return newModel;
        }

        public M Update<M>(M model, Action<M> changes) where M : IModel
        {
            using var transaction = Transaction();
            var newModel = transaction.Update(model, changes);
            transaction.Commit();

            return newModel;
        }

        public void Delete<M>(M model) where M : IModel
        {
            using var transaction = Transaction();
            transaction.Delete(model);
            transaction.Commit();
        }

        public void Dispose()
        {
            Provider.Dispose();
        }
    }
}
