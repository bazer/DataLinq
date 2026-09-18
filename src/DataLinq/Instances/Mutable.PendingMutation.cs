using System;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public partial class Mutable<T> : IMutableMutationReservation where T : class, IImmutableInstance
{
    private object? pendingMutation;

    private void EnsureNoPendingMutation()
    {
        if (pendingMutation is not null) throw MutationInputReservation.Conflict();
    }

    void IMutableMutationReservation.EnsureAvailable()
    {
        lock (rowDataMutationOwner) EnsureNoPendingMutation();
    }

    void IMutableMutationReservation.Reserve(object owner)
    {
        lock (rowDataMutationOwner)
        {
            EnsureNoPendingMutation();
            pendingMutation = owner;
        }
    }

    void IMutableMutationReservation.Release(object owner)
    {
        lock (rowDataMutationOwner)
        {
            if (!ReferenceEquals(pendingMutation, owner)) throw new InvalidOperationException("Invalid mutable reservation owner.");
            pendingMutation = null;
        }
    }

    void IMutableMutationReservation.SetGeneratedValue(ColumnDefinition column, object? value, object owner)
    {
        lock (rowDataMutationOwner)
        {
            if (!ReferenceEquals(pendingMutation, owner)) throw new InvalidOperationException("Invalid mutable reservation owner.");
            ValidateMappedColumn(column);
            SetColumnValue(column, value);
        }
    }

    private void SetColumnValue(ColumnDefinition column, object? value)
    {
        if (metadata.Table.PrimaryKeyColumns.Contains(column))
        {
            _isPkCached = false;
            _cachedPrimaryKey = null;
        }
        mutableRowData.SetValue(column, value, rowDataMutationOwner);
    }
}
