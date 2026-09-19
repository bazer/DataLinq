using System;
using System.Data;
using System.Threading;
using DataLinq.Interfaces;

namespace DataLinq.Execution;

/// <summary>
/// Direct synchronous read cleanup uses a value-type owner;
/// the failure collector is created only on an actual failure. Diagnostic
/// invocation/cleanup scopes are separate successful-path costs. Declare this
/// inside the transaction read scope so cleanup and diagnostic publication precede lease release.
/// </summary>
internal struct ReadCommandResources(uint? transactionId, ReadExecutionIdentity identity = default,
    CancellationToken cancellationToken = default) : IDisposable
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
        (failures ??= new()).AddReported(failure, stage,
            failure is OperationCanceledException canceled && canceled.CancellationToken == cancellationToken && cancellationToken.IsCancellationRequested
                ? ExecutionFailureCause.Cancellation : stage == ExecutionFailureStage.Materialization
                    ? ExecutionFailureCause.MaterializationError : ExecutionFailureCause.Unknown, identity.Operation);

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        // Capture this call, not construction: an iterator's owner can survive many
        // MoveNext calls, whose diagnostic scopes must never remain ambient at yield.
        var reportingScope = ExecutionFailureScope.Current;
        if (reader is not null)
        {
            using var cleanupDiagnostics = ExecutionFailureScope.Begin();
            try { reader.Dispose(); }
            catch (Exception failure) { (failures ??= new(reportingScope)).AddCleanup(failure); }
            finally { reader = null; }
        }
        if (command is not null)
        {
            using var cleanupDiagnostics = ExecutionFailureScope.Begin();
            try { command.Dispose(); }
            catch (Exception failure) { (failures ??= new(reportingScope)).AddCleanup(failure); }
            finally { command = null; }
        }
        if (failures?.Primary is { } primary)
        {
            var context = failures.Snapshot(new(),
                transactionId is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted,
                transactionId is null ? ExecutionRecoveryActions.None : ExecutionRecoveryActions.Dispose, transactionId,
                identity.Operation, identity.ProviderInstanceId);
            ExecutionFailureContexts.Attach(primary, context);
            failures.ThrowIfAny();
        }
    }
}
