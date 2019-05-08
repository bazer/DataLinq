using Remotion.Linq;
using Remotion.Linq.Parsing.Structure;
using Slim.Linq;
using Slim.Metadata;
using System.Linq;
using System.Linq.Expressions;

namespace Slim
{
    public class Queryable<T> : QueryableBase<T>
    {
        protected Transaction DatabaseProvider { get; }
        protected Table Table { get; }

        public Queryable(IQueryProvider provider, Expression expression)
            : base(provider, expression)
        {
        }

        public Queryable(Transaction databaseProvider, Table table) : base(QueryParser.CreateDefault(), new QueryExecutor(databaseProvider, table))
        {
            this.DatabaseProvider = databaseProvider;
            this.Table = table;
        }
    }
}