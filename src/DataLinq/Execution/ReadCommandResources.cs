using System;
using System.Data;
using DataLinq.Interfaces;

namespace DataLinq.Execution;

/// <summary>
/// Direct synchronous read cleanup. A value type keeps successful command/reader ownership
/// allocation-free; the failure collector is created only on an actual failure. Declare this
/// inside the transaction read scope so cleanup and diagnostic publication precede lease release.
/// </summary>
internal struct ReadCommandResources(uint? transactionId) : IDisposable
{
    private IDbCommand? command;
    private IDataLinqDataReader? reader;
    private ExecutionFailures? failures;
    private bool disposed;

    internal IDbCommand OwnCommand(IDbCommand value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ObjectDisposedException.ThrowIf(disposed, typeof(ReadCommandResources));
        if (command is not null)
            throw new InvalidOperationException("This read already owns a command.");
        return command = value;
    }

    internal IDataLinqDataReader OwnReader(IDataLinqDataReader value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ObjectDisposedException.ThrowIf(disposed, typeof(ReadCommandResources));
        if (reader is not null)
            throw new InvalidOperationException("This read already owns a reader.");
        return reader = value;
    }

    internal void RecordFailure(Exception failure, ExecutionFailureStage stage) =>
        (failures ??= new()).AddReported(failure, stage, stage == ExecutionFailureStage.Materialization
            ? ExecutionFailureCause.MaterializationError : ExecutionFailureCause.Unknown);

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        try { reader?.Dispose(); }
        catch (Exception failure) { (failures ??= new()).AddCleanup(failure); }
        finally { reader = null; }
        try { command?.Dispose(); }
        catch (Exception failure) { (failures ??= new()).AddCleanup(failure); }
        finally { command = null; }
        if (failures?.Primary is { } primary)
        {
            var context = failures.Snapshot(new(),
                transactionId is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted,
                transactionId is null ? ExecutionRecoveryActions.None : ExecutionRecoveryActions.Dispose, transactionId);
            ExecutionFailureContexts.Attach(primary, context);
            failures.ThrowIfAny();
        }
    }
}
