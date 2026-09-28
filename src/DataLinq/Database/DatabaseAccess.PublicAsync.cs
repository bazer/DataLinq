using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq;

public abstract partial class DatabaseAccess
{
    /// <summary>Executes SQL with an owned command, without tracked-model cache invalidation.</summary>
    public virtual Task<int> ExecuteNonQueryAsync(string query, CancellationToken cancellationToken = default)
        => ExecuteNonQueryAsyncCore(query, cancellationToken);
    /// <summary>Executes a borrowed command, which must remain stable while active.</summary>
    public virtual Task<int> ExecuteNonQueryAsync(IDbCommand command, CancellationToken cancellationToken = default)
        => ExecuteNonQueryAsyncCore(command, cancellationToken);
    /// <summary>Executes scalar SQL with an owned command.</summary>
    public virtual Task<object?> ExecuteScalarAsync(string query, CancellationToken cancellationToken = default)
        => ExecuteScalarAsyncCore(query, cancellationToken);
    /// <summary>Executes a borrowed scalar command.</summary>
    public virtual Task<object?> ExecuteScalarAsync(IDbCommand command, CancellationToken cancellationToken = default)
        => ExecuteScalarAsyncCore(command, cancellationToken);
    /// <summary>Executes scalar SQL and converts its result using the provider's existing conversion.</summary>
    public virtual Task<T> ExecuteScalarAsync<T>(string query, CancellationToken cancellationToken = default)
        => ExecuteScalarAsyncCore<T>(query, cancellationToken);
    /// <summary>Executes a borrowed scalar command and converts its result.</summary>
    public virtual Task<T> ExecuteScalarAsync<T>(IDbCommand command, CancellationToken cancellationToken = default)
        => ExecuteScalarAsyncCore<T>(command, cancellationToken);
    /// <summary>Returns a reader that owns the created command; the caller must dispose the reader.</summary>
    public virtual Task<IDataLinqAsyncDataReader> ExecuteReaderAsync(string query, CancellationToken cancellationToken = default)
        => PublicReader(ExecuteReaderAsyncCore(query, cancellationToken));
    /// <summary>Returns a caller-owned reader over a borrowed command.</summary>
    public virtual Task<IDataLinqAsyncDataReader> ExecuteReaderAsync(IDbCommand command, CancellationToken cancellationToken = default)
        => PublicReader(ExecuteReaderAsyncCore(command, cancellationToken));
    /// <summary>Enumerates borrowed row views while owning reader and created-command cleanup.</summary>
    public virtual IAsyncEnumerable<IDataLinqDataReader> ReadReaderAsync(string query, CancellationToken cancellationToken = default)
        => ReadReaderAsyncCore(query, cancellationToken);
    /// <summary>Enumerates borrowed row views without disposing the caller's command or transaction.</summary>
    public virtual IAsyncEnumerable<IDataLinqDataReader> ReadReaderAsync(IDbCommand command, CancellationToken cancellationToken = default)
        => ReadReaderAsyncCore(command, cancellationToken);

    private static async Task<IDataLinqAsyncDataReader> PublicReader(Task<Execution.IAsyncDataReader> result)
        => await result.ConfigureAwait(false);
}
