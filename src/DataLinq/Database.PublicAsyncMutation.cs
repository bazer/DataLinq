using System.Threading;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq;

public abstract partial class Database<T> where T : class, IDatabaseModel<T>
{
    /// <summary>Inserts a model in an owned transaction and returns after completion and cleanup.</summary>
    /// <remarks>Input is captured before suspension. Cancellation does not authorize retrying a write.</remarks>
    public Task<M> InsertAsync<M>(Mutable<M> model, TransactionType transactionType = TransactionType.ReadAndWrite,
        CancellationToken cancellationToken = default) where M : class, IImmutableInstance
        => InsertAsyncCore(model, transactionType, cancellationToken);

    /// <summary>Updates a model in an owned transaction and returns after completion and cleanup.</summary>
    /// <remarks>Input is captured before suspension. Cancellation does not authorize retrying a write.</remarks>
    public Task<M> UpdateAsync<M>(Mutable<M> model, TransactionType transactionType = TransactionType.ReadAndWrite,
        CancellationToken cancellationToken = default) where M : class, IImmutableInstance
        => UpdateAsyncCore(model, transactionType, cancellationToken);

    /// <summary>Saves a model in an owned transaction and returns after completion and cleanup.</summary>
    /// <remarks>Input is captured before suspension. Cancellation does not authorize retrying a write.</remarks>
    public Task<M> SaveAsync<M>(Mutable<M> model, TransactionType transactionType = TransactionType.ReadAndWrite,
        CancellationToken cancellationToken = default) where M : class, IImmutableInstance
        => SaveAsyncCore(model, transactionType, cancellationToken);

    /// <summary>Deletes a model in an owned transaction and returns after completion and cleanup.</summary>
    public Task DeleteAsync<M>(M model, TransactionType transactionType = TransactionType.ReadAndWrite,
        CancellationToken cancellationToken = default) where M : IModelInstance
        => DeleteAsyncCore(model, transactionType, cancellationToken);
}
