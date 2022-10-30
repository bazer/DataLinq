using DataLinq.Mutation;
using System.Linq;
using System.Linq.Expressions;

namespace DataLinq
{
    public class DbRead<T> : Queryable<T>
    {
        public DbRead(Transaction transaction) : base(transaction, transaction.Provider.Metadata.TableModels.Single(x => x.Model.CsType == typeof(T)).Table)
        {
        }

        public DbRead(IQueryProvider provider, Expression expression) : base(provider, expression)
        {
        }
    }
}