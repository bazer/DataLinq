using System;
using System.Data;
using DataLinq.Execution;
using DataLinq.Metadata;

namespace DataLinq;

/// <summary>Retains the standalone transaction slot until the native reader has been disposed.</summary>
internal class StandaloneTransactionReader(IDataLinqDataReader reader, StandaloneTransactionOperation operation) : IDataLinqDataReader
{
    private readonly EnumeratorCallGate calls = new();
    private bool disposed;

    internal static IDataLinqDataReader Open(DatabaseTransaction transaction, IDbCommand command, bool ownsCommand,
        Func<TransactionOperationGate.Step, IDataLinqDataReader> open)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        StandaloneTransactionOperation? operation = null;
        try
        {
            operation = transaction.BeginStandaloneCommand();
            OwnedCommandLifetime.Retain(command, operation);
            var reader = open(operation.Step);
            // The command must be disposed before the transaction slot is released.
            if (ownsCommand) reader = OwnedCommandDataReader.Create(reader, command);
            return Create(reader, operation);
        }
        catch (Exception failure)
        {
            var failures = new ExecutionFailures();
            failures.AddReported(failure, ExecutionFailureStage.CommandExecution, fallbackOperation: ExecutionOperationKind.RawCommand);
            if (ownsCommand)
            {
                using var cleanup = ExecutionFailureScope.Begin();
                try { command.Dispose(); } catch (Exception error) { failures.AddCleanup(error); }
            }
            operation?.Dispose();
            if (failures.HasCleanupFailure)
                ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), transaction.SynchronousCompletion,
                    ExecutionRecoveryActions.Dispose, null, ExecutionOperationKind.RawCommand, transaction.DiagnosticProviderInstanceId));
            failures.ThrowIfAny();
            throw;
        }
    }

    internal static IDataLinqDataReader Create(IDataLinqDataReader reader, StandaloneTransactionOperation operation) =>
        reader is IDataLinqOwnedBinaryBufferReader binary
            ? new BinaryReader(reader, operation, binary) : new StandaloneTransactionReader(reader, operation);

    protected void Validate()
    {
        if (disposed) throw new ObjectDisposedException(nameof(StandaloneTransactionReader));
    }

    public object GetValue(int ordinal)
    {
        using var call = calls.Enter();
        Validate();
        return reader.GetValue(ordinal);
    }

    public int GetOrdinal(string name)
    {
        using var call = calls.Enter();
        Validate();
        return reader.GetOrdinal(name);
    }

    public string GetString(int ordinal)
    {
        using var call = calls.Enter();
        Validate();
        return reader.GetString(ordinal);
    }

    public bool GetBoolean(int ordinal)
    {
        using var call = calls.Enter();
        Validate();
        return reader.GetBoolean(ordinal);
    }

    public int GetInt32(int ordinal)
    {
        using var call = calls.Enter();
        Validate();
        return reader.GetInt32(ordinal);
    }

    public DateOnly GetDateOnly(int ordinal)
    {
        using var call = calls.Enter();
        Validate();
        return reader.GetDateOnly(ordinal);
    }

    public Guid GetGuid(int ordinal)
    {
        using var call = calls.Enter();
        Validate();
        return reader.GetGuid(ordinal);
    }

    public byte[]? GetBytes(int ordinal)
    {
        using var call = calls.Enter();
        Validate();
        return reader.GetBytes(ordinal);
    }

    public long GetBytes(int ordinal, Span<byte> buffer)
    {
        using var call = calls.Enter();
        Validate();
        return reader.GetBytes(ordinal, buffer);
    }

    public T? GetValue<T>(ColumnDefinition column)
    {
        using var call = calls.Enter();
        Validate();
        return reader.GetValue<T>(column);
    }

    public T? GetValue<T>(ColumnDefinition column, int ordinal)
    {
        using var call = calls.Enter();
        Validate();
        return reader.GetValue<T>(column, ordinal);
    }

    public bool ReadNextRow()
    {
        using var call = calls.Enter();
        Validate();
        return reader.ReadNextRow();
    }

    public bool IsDbNull(int ordinal)
    {
        using var call = calls.Enter();
        Validate();
        return reader.IsDbNull(ordinal);
    }

    public void Dispose()
    {
        using var call = calls.Enter();
        if (disposed) return;
        disposed = true;
        try
        {
            if (reader is OwnedCommandDataReader owned)
            {
                var failures = owned.DisposeWithFailures(null);
                if (failures?.Primary is { } failure)
                {
                    ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotAttempted,
                        ExecutionRecoveryActions.Dispose, null, ExecutionOperationKind.RawCommand, operation.Step.ProviderInstanceId));
                    failures.ThrowIfAny();
                }
            }
            else reader.Dispose();
        }
        finally { operation.Dispose(); }
    }

    private sealed class BinaryReader(IDataLinqDataReader reader, StandaloneTransactionOperation operation, IDataLinqOwnedBinaryBufferReader binary)
        : StandaloneTransactionReader(reader, operation), IDataLinqOwnedBinaryBufferReader
    {
        public byte[]? TakeOwnedBytes(int ordinal)
        {
            using var call = calls.Enter();
            Validate();
            return binary.TakeOwnedBytes(ordinal);
        }
    }
}
