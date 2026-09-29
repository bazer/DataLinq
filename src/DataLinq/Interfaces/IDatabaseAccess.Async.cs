using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Interfaces;

public partial interface IDatabaseAccess
{
    /// <summary>Executes SQL with an owned command, without tracked-model cache invalidation.</summary>
    Task<int> ExecuteNonQueryAsync(string query, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This access does not support asynchronous commands.");
    /// <summary>Executes a borrowed command, which must remain stable while active.</summary>
    Task<int> ExecuteNonQueryAsync(IDbCommand command, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This access does not support asynchronous commands.");
    /// <summary>Executes scalar SQL with an owned command.</summary>
    Task<object?> ExecuteScalarAsync(string query, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This access does not support asynchronous commands.");
    /// <summary>Executes a borrowed scalar command.</summary>
    Task<object?> ExecuteScalarAsync(IDbCommand command, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This access does not support asynchronous commands.");
    /// <summary>Executes scalar SQL and converts its result using the provider's existing conversion.</summary>
    Task<T> ExecuteScalarAsync<T>(string query, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This access does not support asynchronous commands.");
    /// <summary>Executes a borrowed scalar command and converts its result.</summary>
    Task<T> ExecuteScalarAsync<T>(IDbCommand command, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This access does not support asynchronous commands.");
    /// <summary>Returns a reader that owns the created command; the caller must dispose the reader.</summary>
    Task<IDataLinqAsyncDataReader> ExecuteReaderAsync(string query, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This access does not support asynchronous readers.");
    /// <summary>Returns a caller-owned reader over a borrowed command.</summary>
    Task<IDataLinqAsyncDataReader> ExecuteReaderAsync(IDbCommand command, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This access does not support asynchronous readers.");
    /// <summary>Enumerates borrowed row views while owning reader and created-command cleanup.</summary>
    IAsyncEnumerable<IDataLinqDataReader> ReadReaderAsync(string query, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This access does not support asynchronous readers.");
    /// <summary>Enumerates borrowed row views without disposing the caller's command or transaction.</summary>
    IAsyncEnumerable<IDataLinqDataReader> ReadReaderAsync(IDbCommand command, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This access does not support asynchronous readers.");
}
