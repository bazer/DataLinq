using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq;

public abstract partial class Database<T> : IAsyncDisposable where T : class, IDatabaseModel<T>
{
    /// <summary>Releases owned provider resources asynchronously.</summary>
    /// <remarks>There must be no active owned work. Does not silently fall back to synchronous disposal.</remarks>
    public ValueTask DisposeAsync() => ((IAsyncDisposable)Provider).DisposeAsync();

    /// <summary>Runs a callback once in an owned transaction and returns after commit, finalization and cleanup.</summary>
    /// <remarks>The callback must await all of its work and leave completion to this helper. Recovery uses an independent rollback budget.</remarks>
    public Task CommitAsync(Func<Transaction<T>, Task> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return CommitAsync((transaction, _) => action(transaction), transactionType, cancellationToken);
    }

    /// <summary>Runs a callback once in an owned transaction and returns after commit, finalization and cleanup.</summary>
    /// <remarks>The callback must await all of its work and leave completion to this helper. Recovery uses an independent rollback budget.</remarks>
    public Task CommitAsync(Func<Transaction<T>, CancellationToken, Task> action,
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
    public Task<TResult> CommitAsync<TResult>(Func<Transaction<T>, Task<TResult>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return CommitAsync((transaction, _) => action(transaction), transactionType, cancellationToken);
    }

    /// <summary>Runs a callback once in an owned transaction and returns after commit, finalization and cleanup.</summary>
    /// <remarks>The callback must await all of its work and leave completion to this helper. Recovery uses an independent rollback budget.</remarks>
    public Task<TResult> CommitAsync<TResult>(Func<Transaction<T>, CancellationToken, Task<TResult>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        DatabaseProvider.EnsureAsyncTransactions(Provider);
        var transaction = Transaction(transactionType);
        return transaction.RunOwnedCallbackAsyncCore(token => action(transaction, token), cancellationToken);
    }

}
