using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq;

public abstract partial class DatabaseProvider
{
    internal virtual bool SupportsAsyncTransactions => false;

    internal static void EnsureAsyncTransactions(IDatabaseProvider provider)
    {
        if (provider is not DatabaseProvider { SupportsAsyncTransactions: true })
            throw new NotSupportedException("This provider does not support asynchronous managed transactions.");
    }

    /// <summary>Releases owned provider resources asynchronously.</summary>
    /// <remarks>There must be no active owned work. Does not silently fall back to synchronous disposal.</remarks>
    public virtual ValueTask DisposeAsync() => throw new NotSupportedException("This provider does not support asynchronous root disposal.");

    /// <summary>Runs a callback once in an owned transaction and returns after commit, finalization and cleanup.</summary>
    /// <remarks>The callback must await all of its work and leave completion to this helper. Recovery uses an independent rollback budget.</remarks>
    public virtual Task CommitAsync(Func<Transaction, Task> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return CommitAsync((transaction, _) => action(transaction), transactionType, cancellationToken);
    }

    /// <summary>Runs a callback once in an owned transaction and returns after commit, finalization and cleanup.</summary>
    /// <remarks>The callback must await all of its work and leave completion to this helper. Recovery uses an independent rollback budget.</remarks>
    public virtual Task CommitAsync(Func<Transaction, CancellationToken, Task> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return CommitAsync(async (transaction, token) =>
        {
            await (action(transaction, token) ?? throw new InvalidOperationException("The transaction callback returned a null task.")).ConfigureAwait(false);
            return true;
        }, transactionType, cancellationToken);
    }

    /// <summary>Runs a callback once in an owned transaction and returns after commit, finalization and cleanup.</summary>
    /// <remarks>The callback must await all of its work and leave completion to this helper. Recovery uses an independent rollback budget.</remarks>
    public virtual Task<TResult> CommitAsync<TResult>(Func<Transaction, Task<TResult>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return CommitAsync((transaction, _) => action(transaction), transactionType, cancellationToken);
    }

    /// <summary>Runs a callback once in an owned transaction and returns after commit, finalization and cleanup.</summary>
    /// <remarks>The callback must await all of its work and leave completion to this helper. Recovery uses an independent rollback budget.</remarks>
    public virtual Task<TResult> CommitAsync<TResult>(Func<Transaction, CancellationToken, Task<TResult>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureAsyncTransactions(this);
        var transaction = StartTransaction(transactionType);
        return transaction.RunOwnedCallbackAsyncCore(token => action(transaction, token), cancellationToken);
    }

}
