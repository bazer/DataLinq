using System;
using System.Collections.Generic;
using System.Data;
using DataLinq.Execution;

namespace DataLinq;

public abstract partial class DatabaseAccess
{
    // Internal W1 counterparts. Native public adapters must explicitly bind direct
    // dispatch; calling their public virtual methods from these cores would recurse.
    internal int ExecuteNonQuerySyncCore(string sql) => ExecuteSyncNonQuery(BindSyncRaw(sql));
    internal int ExecuteNonQuerySyncCore(IDbCommand command) => ExecuteSyncNonQuery(BindSyncRaw(command));
    internal object? ExecuteScalarSyncCore(string sql) => ExecuteSyncScalar(BindSyncRaw(sql), static value => value);
    internal object? ExecuteScalarSyncCore(IDbCommand command) => ExecuteSyncScalar(BindSyncRaw(command), static value => value);
    internal T ExecuteScalarSyncCore<T>(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ValidateSyncRawOwner();
        var invocation = ISyncRawCommandFactory.Require(this).BindScalar<T>(sql);
        return ExecuteSyncScalar(invocation.Command, invocation.Convert);
    }
    internal T ExecuteScalarSyncCore<T>(IDbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateSyncRawOwner();
        var invocation = ISyncRawCommandFactory.Require(this).BindScalar<T>(command);
        return ExecuteSyncScalar(invocation.Command, invocation.Convert);
    }

    internal IDataLinqDataReader ExecuteReaderSyncCore(string sql) => SyncRawDataReader.Open(BindSyncRaw(sql), managedTransaction, DiagnosticProviderInstanceId);
    internal IDataLinqDataReader ExecuteReaderSyncCore(IDbCommand command) => SyncRawDataReader.Open(BindSyncRaw(command), managedTransaction, DiagnosticProviderInstanceId);

    internal IEnumerable<IDataLinqDataReader> ReadReaderSyncCore(string sql)
        => new GuardedEnumerable<IDataLinqDataReader>(ReadSyncRows(() => ExecuteReaderSyncCore(sql)));
    internal IEnumerable<IDataLinqDataReader> ReadReaderSyncCore(IDbCommand command)
        => new GuardedEnumerable<IDataLinqDataReader>(ReadSyncRows(() => ExecuteReaderSyncCore(command)));

    private static IEnumerable<IDataLinqDataReader> ReadSyncRows(Func<IDataLinqDataReader> open)
    {
        using var reader = open();
        while (reader.ReadNextRow()) yield return reader;
    }

    private int ExecuteSyncNonQuery(SyncRawCommand command)
        => SyncRawExecution.Execute(command, SyncCommandKind.NonQuery, managedTransaction,
            static (invocation, owner) => invocation.ExecuteNonQuery(owner), static value => value, DiagnosticProviderInstanceId);

    private T ExecuteSyncScalar<T>(SyncRawCommand command, Func<object?, T> convert)
        => SyncRawExecution.Execute(command, SyncCommandKind.Scalar, managedTransaction,
            static (invocation, owner) => invocation.ExecuteScalar(owner), convert, DiagnosticProviderInstanceId);

    private SyncRawCommand BindSyncRaw(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ValidateSyncRawOwner();
        return ISyncRawCommandFactory.Require(this).BindCommand(sql);
    }
    private SyncRawCommand BindSyncRaw(IDbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateSyncRawOwner();
        return ISyncRawCommandFactory.Require(this).BindCommand(command);
    }
    private void ValidateSyncRawOwner()
    {
        if (this is DatabaseTransaction && managedTransaction is null)
            throw new InvalidOperationException("Synchronous raw execution requires its managed transaction owner.");
        managedTransaction?.EnsureCanRead("execute a synchronous raw command", operationKind: ExecutionOperationKind.RawCommand);
    }
}
