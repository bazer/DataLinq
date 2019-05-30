using Slim.Mutation;
using System.Linq;
using System.Linq.Expressions;

namespace Slim
{
    public class DbRead<T> : Queryable<T>
    {
        public DbRead(Transaction transaction) : base(transaction, transaction.DatabaseProvider.Database.Tables.Single(x => x.Model.CsType == typeof(T)))
        {
        }

        public DbRead(IQueryProvider provider, Expression expression) : base(provider, expression)
        {
        }
    }
}