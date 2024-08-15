using System.Linq;
using System.Linq.Expressions;
using DataLinq.Linq;
using DataLinq.Metadata;
using DataLinq.Mutation;
using Remotion.Linq;
using Remotion.Linq.Parsing.Structure;

namespace DataLinq;

public class Queryable<T> : QueryableBase<T>
{
    protected static readonly IQueryParser queryParser = QueryParser.CreateDefault();

    public Queryable(IQueryProvider provider, Expression expression) : base(provider, expression) { }

    public Queryable(DataSourceAccess dataSource, TableMetadata table) : base(queryParser, new QueryExecutor(dataSource, table)) { }
}