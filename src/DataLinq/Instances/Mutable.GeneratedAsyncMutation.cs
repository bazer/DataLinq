using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public partial class Mutable<T>
{
    /// <summary>Executes a generated typed editing helper in an owned transaction.</summary>
    /// <remarks>
    /// Reserved for generated mutation helpers. Changes run synchronously before suspension,
    /// after validation and cancellation checks. The captured mutable remains reserved through
    /// transaction completion and cleanup. A null change type selects insert or update from
    /// the mutable lifecycle; only insert and update are otherwise supported.
    /// </remarks>
    protected static Task<T> ExecuteGeneratedMutationAsync<TMutable>(IDatabaseProvider provider,
        TMutable model, Action<TMutable> changes, TransactionChangeType? changeType,
        CancellationToken cancellationToken) where TMutable : Mutable<T>
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(changes);
        if (changeType is not null and not TransactionChangeType.Insert and not TransactionChangeType.Update)
            throw new ArgumentOutOfRangeException(nameof(changeType));
        DatabaseProvider.EnsureAsyncTransactions(provider);
        return provider.StartTransaction().RunMutationHelperWithEditsAsyncCore<T, TMutable>(
            model, changes, changeType, cancellationToken);
    }
}
