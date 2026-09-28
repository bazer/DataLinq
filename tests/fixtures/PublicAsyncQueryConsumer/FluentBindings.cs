using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

internal static class FluentBindings
{
    internal static void Bind<T, V>(Select<T> select, SqlQuery<T> query, DataSourceAccess source,
        IDbCommand command, ColumnIndex index, CancellationToken token)
    {
        IAsyncEnumerable<IDataLinqAsyncDataReader> readers = select.ReadReaderAsync(cancellationToken: token);
        IAsyncEnumerable<RowData> rows = select.ReadRowsAsync();
        Task<RowData?> first = select.ReadFirstRowAsync();
        IAsyncEnumerable<DataLinqKey> keys = select.ReadKeysAsync();
        IAsyncEnumerable<(DataLinqKey fk, DataLinqKey[] pks)> grouped = select.ReadPrimaryAndForeignKeysAsync(foreignKeyIndex: index);
        IAsyncEnumerable<IImmutableInstance> models = select.ExecuteAsync();
        IAsyncEnumerable<V> casts = select.ExecuteAsAsync<V>();
        Task<V> typed = select.ExecuteScalarAsync<V>(cancellationToken: token);
        Task<object?> scalar = select.ExecuteScalarAsync();
        IAsyncEnumerable<T> selected = query.SelectAsync(cancellationToken: token);
        IAsyncEnumerable<IModel> raw = source.GetFromQueryAsync<IModel>(query: "sql", cancellationToken: token);
        IAsyncEnumerable<IModel> borrowed = source.GetFromCommandAsync<IModel>(dbCommand: command);
    }
}
