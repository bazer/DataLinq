using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq.Query;

public partial class Select<T>
{
    /// <summary>Enumerates borrowed current-row views from a privately captured selection.</summary>
    /// <remarks>Each enumerator captures the selection before I/O. Do not retain, advance or dispose its row views.</remarks>
    public IAsyncEnumerable<IDataLinqAsyncDataReader> ReadReaderAsync(CancellationToken cancellationToken = default)
        => ReadReaderAsyncCore(cancellationToken);

    /// <summary>Enumerates detached rows using the selection captured for each enumerator.</summary>
    public IAsyncEnumerable<RowData> ReadRowsAsync(CancellationToken cancellationToken = default)
        => ReadRowsAsyncCore(cancellationToken);

    /// <summary>Returns the first row, or null, after closing the owned reader.</summary>
    public Task<RowData?> ReadFirstRowAsync(CancellationToken cancellationToken = default)
        => ReadFirstRowAsyncCore(cancellationToken);

    /// <summary>Enumerates canonical primary keys from the captured selection.</summary>
    public IAsyncEnumerable<DataLinqKey> ReadKeysAsync(CancellationToken cancellationToken = default)
        => ReadKeysAsyncCore(cancellationToken);

    /// <summary>Reads and groups primary keys by the supplied foreign-key index.</summary>
    /// <remarks>Results may be buffered. The caller retains ownership of its transaction.</remarks>
    public IAsyncEnumerable<(DataLinqKey fk, DataLinqKey[] pks)> ReadPrimaryAndForeignKeysAsync(
        ColumnIndex foreignKeyIndex, CancellationToken cancellationToken = default)
        => ReadPrimaryAndForeignKeysAsyncCore(foreignKeyIndex, cancellationToken);

    /// <summary>Enumerates supported immutable models without editing the caller's selected columns.</summary>
    public IAsyncEnumerable<IImmutableInstance> ExecuteAsync(CancellationToken cancellationToken = default)
        => ExecuteAsyncCore(cancellationToken);

    /// <summary>Enumerates supported immutable models cast to the requested result type.</summary>
    /// <remarks>This is a model cast, not a DTO mapping operation.</remarks>
    public IAsyncEnumerable<V> ExecuteAsAsync<V>(CancellationToken cancellationToken = default)
        => new AsyncReaderEnumerable<V>(() => AsyncReaderTransform.Capture<IImmutableInstance, V>(CaptureModels(),
            static (rows, token) =>
            {
                var result = new List<V>(rows.Count);
                foreach (var row in rows)
                {
                    token.ThrowIfCancellationRequested();
                    result.Add((V)row);
                }
                return result;
            }), cancellationToken);

    /// <summary>Executes a scalar selection captured before suspension.</summary>
    public Task<V> ExecuteScalarAsync<V>(CancellationToken cancellationToken = default)
        => ExecuteScalarAsyncCore<V>(cancellationToken);

    /// <summary>Executes a scalar selection captured before suspension.</summary>
    public Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
        => ExecuteScalarAsyncCore(cancellationToken);
}
