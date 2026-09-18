using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Instances;

namespace DataLinq.Mutation;

public partial class Transaction
{
    // Internal mirrors of the existing local Action overloads. Public/generated
    // declarations and packed inference/ambiguity checks remain W3 work.
    internal Task<T> InsertAsyncCore<T>(Mutable<T> model, Action<Mutable<T>> edits, CancellationToken token = default)
        where T : class, IImmutableInstance
        => InsertAsyncCore<T, Mutable<T>>(model, edits, token);

    internal Task<T> InsertAsyncCore<T, TMutable>(TMutable model, Action<TMutable> edits, CancellationToken token = default)
        where T : class, IImmutableInstance where TMutable : Mutable<T>
        => MutateWithEditsAsyncCore<T, TMutable>(model, edits, TransactionChangeType.Insert, token);

    internal Task<T> UpdateAsyncCore<T>(Mutable<T> model, Action<Mutable<T>> edits, CancellationToken token = default)
        where T : class, IImmutableInstance
        => UpdateAsyncCore<T, Mutable<T>>(model, edits, token);

    internal Task<T> UpdateAsyncCore<T, TMutable>(TMutable model, Action<TMutable> edits, CancellationToken token = default)
        where T : class, IImmutableInstance where TMutable : Mutable<T>
        => MutateWithEditsAsyncCore<T, TMutable>(model, edits, TransactionChangeType.Update, token);

    internal Task<T> UpdateAsyncCore<T>(T model, Action<Mutable<T>> edits, CancellationToken token = default)
        where T : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(edits);
        return UpdateAsyncCore(new Mutable<T>(model), edits, token);
    }

    internal Task<T> SaveAsyncCore<T>(T? model, Action<Mutable<T>> edits, CancellationToken token = default)
        where T : class, IImmutableInstance
    {
        ArgumentNullException.ThrowIfNull(edits);
        return SaveAsyncCore(model is null ? new Mutable<T>() : new Mutable<T>(model), edits, token);
    }

    internal Task<T> SaveAsyncCore<T>(Mutable<T>? model, Action<Mutable<T>> edits, CancellationToken token = default)
        where T : class, IImmutableInstance
        => SaveAsyncCore<T, Mutable<T>>(model ?? new Mutable<T>(), edits, token);

    internal Task<T> SaveAsyncCore<T, TMutable>(TMutable model, Action<TMutable> edits, CancellationToken token = default)
        where T : class, IImmutableInstance where TMutable : Mutable<T>
        => MutateWithEditsAsyncCore<T, TMutable>(model, edits, null, token);
}
