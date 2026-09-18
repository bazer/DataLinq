using System;
using System.Runtime.CompilerServices;
using System.Threading;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq.Mutation;

// Known mutables intercept local edits. Custom mutables retain submission exclusion
// and snapshot checks, but cannot promise interception of their own setter bodies.
internal interface IMutableMutationReservation
{
    void Reserve(object owner);
    void Release(object owner);
    void EnsureAvailable();
    void SetGeneratedValue(ColumnDefinition column, object? value, object owner);
}

internal sealed class MutationInputReservation : IDisposable
{
    private sealed class Slot { internal object? Owner; }
    private static readonly ConditionalWeakTable<IModelInstance, Slot> legacy = new();
    private readonly IModelInstance model;
    private int disposed;

    internal MutationInputReservation(IModelInstance model)
    {
        this.model = model;
        if (model is IMutableMutationReservation guarded) guarded.Reserve(this);
        else if (model is IMutableInstance)
        {
            var slot = legacy.GetValue(model, static _ => new());
            lock (slot)
            {
                if (slot.Owner is not null) throw Conflict();
                slot.Owner = this;
            }
        }
    }

    internal static InvalidOperationException Conflict() =>
        new("This mutable is reserved by a pending asynchronous mutation; edits, resets and conflicting mutations are not permitted.");

    internal static void EnsureAvailable(IModelInstance model)
    {
        if (model is IMutableMutationReservation guarded) guarded.EnsureAvailable();
        else if (legacy.TryGetValue(model, out var slot))
            lock (slot) { if (slot.Owner is not null) throw Conflict(); }
    }

    internal void SetGeneratedValue(ColumnDefinition column, object? value)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (model is IMutableMutationReservation guarded) guarded.SetGeneratedValue(column, value, this);
        else ((IMutableInstance)model)[column] = value;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (model is IMutableMutationReservation guarded) guarded.Release(this);
        else if (model is IMutableInstance && legacy.TryGetValue(model, out var slot))
            lock (slot) { if (ReferenceEquals(slot.Owner, this)) slot.Owner = null; }
    }
}
