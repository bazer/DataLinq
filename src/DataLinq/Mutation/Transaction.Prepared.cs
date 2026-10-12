using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Linq;

namespace DataLinq.Mutation;

public partial class Transaction<T>
{
    /// <summary>Executes a prepared scalar or row query through this transaction.</summary>
    public TResult Execute<TArgument, TResult>(PreparedQuery<T, TArgument, TResult> prepared, TArgument argument, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        return prepared.Execute(this, argument, cancellationToken);
    }

    /// <summary>Executes a prepared scalar or row query asynchronously through this transaction.</summary>
    public Task<TResult> ExecuteAsync<TArgument, TResult>(PreparedQuery<T, TArgument, TResult> prepared, TArgument argument, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        return prepared.ExecuteAsync(this, argument, cancellationToken);
    }

    /// <summary>Captures invocation values for a deferred prepared sequence on this transaction.</summary>
    public IEnumerable<TElement> Execute<TArgument, TElement>(PreparedSequenceQuery<T, TArgument, TElement> prepared, TArgument argument, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        return prepared.Execute(this, argument, cancellationToken);
    }

    /// <summary>Captures invocation values for a deferred prepared async sequence on this transaction.</summary>
    public IAsyncEnumerable<TElement> ExecuteAsync<TArgument, TElement>(PreparedSequenceQuery<T, TArgument, TElement> prepared, TArgument argument, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        return prepared.ExecuteAsync(this, argument, cancellationToken);
    }
}
