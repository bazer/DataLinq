using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;

namespace DataLinq.Query;

public partial class Select<T>
{
    internal Task<object?> ExecuteScalarAsyncCore(CancellationToken cancellationToken = default)
    {
        var factory = IAsyncSqlScalarFactory.Require(query.DataSource.DatabaseAccess);
        var source = factory.BindScalar(CapturedSql.Capture(ToSql()));
        return AsyncScalarRead.ExecuteAsync(source, query.DataSource as Transaction, static value => value, cancellationToken);
    }

    internal Task<V> ExecuteScalarAsyncCore<V>(CancellationToken cancellationToken = default)
    {
        var factory = IAsyncSqlScalarFactory.Require(query.DataSource.DatabaseAccess);
        var invocation = factory.BindScalar<V>(CapturedSql.Capture(ToSql()));
        return AsyncScalarRead.ExecuteAsync(invocation.Source, query.DataSource as Transaction, invocation.Convert, cancellationToken);
    }
}
