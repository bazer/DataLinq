using System;
using System.Collections.Generic;
using System.Data;
using DataLinq.Interfaces;

namespace DataLinq.Execution;

/// <summary>
/// Carries existing transaction admission to synchronous provider dispatch. Root
/// and custom non-transaction sources retain their existing public execution path.
/// The caller, not this dispatcher, owns the step and command/reader lifetime.
/// </summary>
internal static class SyncCommandDispatch
{
    internal static IDataLinqDataReader ExecuteReader(IDatabaseAccess access, IDbCommand command, TransactionOperationGate.Step? owner)
        => owner is null ? access.ExecuteReader(command) : RequireManagedAccess(access).ExecuteReaderOwned(command, owner);

    internal static IDataLinqDataReader ExecuteReader(IDatabaseAccess access, string query, TransactionOperationGate.Step? owner)
        => owner is null ? access.ExecuteReader(query) : RequireManagedAccess(access).ExecuteReaderOwned(query, owner);

    internal static object? ExecuteScalar(IDatabaseAccess access, IDbCommand command, TransactionOperationGate.Step? owner)
        => owner is null ? access.ExecuteScalar(command) : RequireManagedAccess(access).ExecuteScalarOwned(command, owner);

    internal static T ExecuteScalar<T>(IDatabaseAccess access, IDbCommand command, TransactionOperationGate.Step? owner)
        => owner is null ? access.ExecuteScalar<T>(command) : RequireManagedAccess(access).ExecuteScalarOwned<T>(command, owner);

    internal static int ExecuteNonQuery(IDatabaseAccess access, IDbCommand command, TransactionOperationGate.Step? owner)
        => owner is null ? access.ExecuteNonQuery(command) : RequireManagedAccess(access).ExecuteNonQueryOwned(command, owner);

    internal static IEnumerable<IDataLinqDataReader> ReadReader(IDatabaseAccess access, IDbCommand command, TransactionOperationGate.Step? owner)
    {
        using var reader = ExecuteReader(access, command, owner);
        while (reader.ReadNextRow())
            yield return reader;
    }

    internal static IEnumerable<IDataLinqDataReader> ReadReader(IDatabaseAccess access, string query, TransactionOperationGate.Step? owner)
    {
        using var reader = ExecuteReader(access, query, owner);
        while (reader.ReadNextRow())
            yield return reader;
    }

    private static DatabaseAccess RequireManagedAccess(IDatabaseAccess access)
        => access as DatabaseAccess ?? throw new NotSupportedException(
            "Owned command execution requires a managed database access adapter.");
}
