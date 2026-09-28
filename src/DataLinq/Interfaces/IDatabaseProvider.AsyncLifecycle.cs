using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Interfaces;

public partial interface IDatabaseProvider
{
    /// <summary>Legacy providers must explicitly implement asynchronous root disposal.</summary>
    ValueTask IAsyncDisposable.DisposeAsync() => throw new NotSupportedException("This provider does not support asynchronous root disposal.");

    /// <summary>Runs a callback once in an owned transaction and returns after commit, finalization and cleanup.</summary>
    /// <remarks>The callback must await all of its work and leave completion to this helper. Recovery uses an independent rollback budget.</remarks>
    Task CommitAsync(Func<Transaction, Task> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This provider does not support asynchronous transaction callbacks.");

    /// <summary>Runs a callback once in an owned transaction and returns after commit, finalization and cleanup.</summary>
    /// <remarks>The callback must await all of its work and leave completion to this helper. Recovery uses an independent rollback budget.</remarks>
    Task CommitAsync(Func<Transaction, CancellationToken, Task> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This provider does not support asynchronous transaction callbacks.");

    /// <summary>Runs a callback once in an owned transaction and returns after commit, finalization and cleanup.</summary>
    /// <remarks>The callback must await all of its work and leave completion to this helper. Recovery uses an independent rollback budget.</remarks>
    Task<TResult> CommitAsync<TResult>(Func<Transaction, Task<TResult>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This provider does not support asynchronous transaction callbacks.");

    /// <summary>Runs a callback once in an owned transaction and returns after commit, finalization and cleanup.</summary>
    /// <remarks>The callback must await all of its work and leave completion to this helper. Recovery uses an independent rollback budget.</remarks>
    Task<TResult> CommitAsync<TResult>(Func<Transaction, CancellationToken, Task<TResult>> action,
        TransactionType transactionType = TransactionType.ReadAndWrite, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This provider does not support asynchronous transaction callbacks.");

}
