using System;
using System.Data;
using DataLinq.Execution;
using DataLinq.Interfaces;

namespace DataLinq;

public abstract partial class DatabaseAccess
{
    // Managed callers already own admission through resource cleanup. Validate that
    // authority here, before a provider hook can dispatch; never infer it from ambient
    // state or tag caller-owned commands with a reusable admission bypass.
    internal IDataLinqDataReader ExecuteReaderOwned(IDbCommand command, TransactionOperationGate.Step owner)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandOwner(owner);
        return ExecuteReaderOwnedCore(command, owner);
    }

    internal IDataLinqDataReader ExecuteReaderOwned(string query, TransactionOperationGate.Step owner)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateCommandOwner(owner);
        return ExecuteReaderOwnedCore(query, owner);
    }

    internal object? ExecuteScalarOwned(IDbCommand command, TransactionOperationGate.Step owner)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandOwner(owner);
        return ExecuteScalarOwnedCore(command, owner);
    }

    internal T ExecuteScalarOwned<T>(IDbCommand command, TransactionOperationGate.Step owner)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandOwner(owner);
        return ExecuteScalarOwnedCore<T>(command, owner);
    }

    internal int ExecuteNonQueryOwned(IDbCommand command, TransactionOperationGate.Step owner)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandOwner(owner);
        return ExecuteNonQueryOwnedCore(command, owner);
    }

    private void ValidateCommandOwner(TransactionOperationGate.Step owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (managedTransaction is null)
            throw new InvalidOperationException("Owned command execution requires its managed transaction owner.");
        managedTransaction.EnsureCanRead("execute an owned command", owner);
    }

    // Keep existing custom provider overrides and their conversion/ownership policies.
    // Gate-aware adapters can override these private dispatch hooks separately from
    // public raw admission, without recursively entering their public gate.
    internal virtual IDataLinqDataReader ExecuteReaderOwnedCore(IDbCommand command, TransactionOperationGate.Step owner)
        => ExecuteReader(command);

    internal virtual IDataLinqDataReader ExecuteReaderOwnedCore(string query, TransactionOperationGate.Step owner)
        => ExecuteReader(query);

    internal virtual object? ExecuteScalarOwnedCore(IDbCommand command, TransactionOperationGate.Step owner)
        => ExecuteScalar(command);

    internal virtual T ExecuteScalarOwnedCore<T>(IDbCommand command, TransactionOperationGate.Step owner)
        => ExecuteScalar<T>(command);

    internal virtual int ExecuteNonQueryOwnedCore(IDbCommand command, TransactionOperationGate.Step owner)
        => ExecuteNonQuery(command);
}
