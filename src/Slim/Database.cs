using Slim.Interfaces;
using Slim.Metadata;
using Slim.Mutation;
using Slim.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Slim
{
    public abstract class Database<T> 
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

        public T Select()
        {
            return Transaction(TransactionType.NoTransaction).Select();
        }

        public QuerySelector Query(string tableName)
        {
            var transaction = Transaction(TransactionType.NoTransaction);
            var table = transaction.Provider.Metadata.Tables.Single(x => x.DbName == tableName);

            return new QuerySelector(table, transaction);
        }

        public QuerySelector Query(Table table)
        {
            var transaction = Transaction(TransactionType.NoTransaction);

            return new QuerySelector(table, transaction);
        }

        public QuerySelector<V> Query<V>() where V: IModel
        {
            var transaction = Transaction(TransactionType.NoTransaction);

            return new QuerySelector<V>(transaction);
        }
    }
}
