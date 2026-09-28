using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace DataLinq;

public static partial class IModelExtensions
{
    /// <summary>Inserts this mutable through the supplied transaction without completing it.</summary>
    public static Task<M> InsertAsync<M>(this Mutable<M> model, Transaction transaction, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction.InsertAsync(model, cancellationToken);
    }

    /// <summary>Updates this mutable through the supplied transaction without completing it.</summary>
    public static Task<M> UpdateAsync<M>(this Mutable<M> model, Transaction transaction, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction.UpdateAsync(model, cancellationToken);
    }

    /// <summary>Saves this mutable through the supplied transaction without completing it.</summary>
    public static Task<M> SaveAsync<M>(this Mutable<M> model, Transaction transaction, CancellationToken cancellationToken = default)
        where M : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction.SaveAsync(model, cancellationToken);
    }

    /// <summary>Deletes this row using an independent transaction obtained from its validated SQL source.</summary>
    /// <remarks>Does not enlist in the row's originating transaction. Detached and Memory models cannot acquire SQL persistence.</remarks>
    public static Task DeleteAsync<M>(this M model, CancellationToken cancellationToken = default) where M : IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(model);
        var source = model.GetDataSource() ?? throw new InvalidOperationException("A detached model has no SQL source for asynchronous deletion.");
        DataSourceAccess.EnsureReadAllowed(source, "resolve the source for an asynchronous delete", operationKind: ExecutionOperationKind.Delete);
        return source.Provider.CommitAsync((transaction, token) => transaction.DeleteAsync(model, token),
            cancellationToken: cancellationToken);
    }

    /// <summary>Deletes this model through the supplied transaction without completing it.</summary>
    public static Task DeleteAsync<M>(this M model, Transaction transaction, CancellationToken cancellationToken = default) where M : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction.DeleteAsync(model, cancellationToken);
    }
}
