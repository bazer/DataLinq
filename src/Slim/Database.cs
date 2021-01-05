using Slim.Interfaces;
using Slim.Metadata;
using Slim.Mutation;
using Slim.Query;
using System;
using System.Linq;

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

        public T Query()
        {
            return Transaction(TransactionType.NoTransaction).From();
        }

        public SqlQuery From(string tableName, string alias = null)
        {
            if (alias == null)
                (tableName, alias) = QueryUtils.ParseTableNameAndAlias(tableName);

            var transaction = Transaction(TransactionType.NoTransaction);
            var table = transaction.Provider.Metadata.Tables.Single(x => x.DbName == tableName);

            return new SqlQuery(table, transaction, alias);
        }

        public SqlQuery From(Table table, string alias = null)
        {
            var transaction = Transaction(TransactionType.NoTransaction);

            return new SqlQuery(table, transaction, alias);
        }

        public SqlQuery<V> From<V>() where V: IModel
        {
            var transaction = Transaction(TransactionType.NoTransaction);

            return transaction.From<V>();
        }
    }
}
