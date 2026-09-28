using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Instances;

namespace DataLinq.Mutation;

public partial class Transaction
{
    /// <summary>Inserts a captured model without completing this transaction.</summary>
    public Task<M> InsertAsync<M>(Mutable<M> model, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance => InsertAsyncCore(model, cancellationToken);

    /// <summary>Applies local changes before suspension and inserts the captured model.</summary>
    /// <remarks>The local changes delegate is synchronous. This method does not complete the transaction.</remarks>
    public Task<M> InsertAsync<M>(Mutable<M> model, Action<Mutable<M>> changes, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(changes);
        return InsertAsyncCore<M>(model, changes, cancellationToken);
    }

    /// <summary>Applies local changes before suspension and inserts the captured model.</summary>
    /// <remarks>The local changes delegate is synchronous. This method does not complete the transaction.</remarks>
    public Task<M> InsertAsync<M, TMutable>(TMutable model, Action<TMutable> changes, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance where TMutable : Mutable<M>
    {
        ArgumentNullException.ThrowIfNull(changes);
        return InsertAsyncCore<M, TMutable>(model, changes, cancellationToken);
    }

    /// <summary>Updates a captured model without completing this transaction.</summary>
    public Task<M> UpdateAsync<M>(Mutable<M> model, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance => UpdateAsyncCore(model, cancellationToken);

    /// <summary>Applies local changes before suspension and updates the captured model.</summary>
    /// <remarks>The local changes delegate is synchronous. This method does not complete the transaction.</remarks>
    public Task<M> UpdateAsync<M>(Mutable<M> model, Action<Mutable<M>> changes, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(changes);
        return UpdateAsyncCore<M>(model, changes, cancellationToken);
    }

    /// <summary>Applies local changes before suspension and updates the captured model.</summary>
    /// <remarks>The local changes delegate is synchronous. This method does not complete the transaction.</remarks>
    public Task<M> UpdateAsync<M, TMutable>(TMutable model, Action<TMutable> changes, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance where TMutable : Mutable<M>
    {
        ArgumentNullException.ThrowIfNull(changes);
        return UpdateAsyncCore<M, TMutable>(model, changes, cancellationToken);
    }

    /// <summary>Applies local changes before suspension and updates the captured model.</summary>
    /// <remarks>The local changes delegate is synchronous. This method does not complete the transaction.</remarks>
    public Task<M> UpdateAsync<M>(M model, Action<Mutable<M>> changes, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(changes);
        return UpdateAsyncCore<M>(model, changes, cancellationToken);
    }

    /// <summary>Saves a captured model without completing this transaction.</summary>
    public Task<M> SaveAsync<M>(Mutable<M> model, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance => SaveAsyncCore(model, cancellationToken);

    /// <summary>Applies local changes before suspension and saves the captured model.</summary>
    /// <remarks>The local changes delegate is synchronous. This method does not complete the transaction. A null model starts a new mutable.</remarks>
    public Task<M> SaveAsync<M>(Mutable<M>? model, Action<Mutable<M>> changes, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(changes);
        return SaveAsyncCore<M>(model, changes, cancellationToken);
    }

    /// <summary>Applies local changes before suspension and saves the captured model.</summary>
    /// <remarks>The local changes delegate is synchronous. This method does not complete the transaction.</remarks>
    public Task<M> SaveAsync<M, TMutable>(TMutable model, Action<TMutable> changes, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance where TMutable : Mutable<M>
    {
        ArgumentNullException.ThrowIfNull(changes);
        return SaveAsyncCore<M, TMutable>(model, changes, cancellationToken);
    }

    /// <summary>Applies local changes before suspension and saves the captured model.</summary>
    /// <remarks>The local changes delegate is synchronous. This method does not complete the transaction. A null model starts a new mutable.</remarks>
    public Task<M> SaveAsync<M>(M? model, Action<Mutable<M>> changes, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(changes);
        return SaveAsyncCore<M>(model, changes, cancellationToken);
    }

    /// <summary>Captures a finite collection once and inserts its models without completing this transaction.</summary>
    /// <remarks>Repeated mutable object instances are rejected before any insert.</remarks>
    public Task<List<M>> InsertAsync<M>(IEnumerable<Mutable<M>> models, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance => InsertAsyncCore(models, cancellationToken);

    /// <summary>Deletes a captured model without completing this transaction.</summary>
    public Task DeleteAsync(IModelInstance model, CancellationToken cancellationToken = default)
        => DeleteAsyncCore(model, cancellationToken);
}
