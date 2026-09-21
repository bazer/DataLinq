using System;
using System.Threading.Tasks;

namespace DataLinq.Execution;

internal static class AsyncCommandCleanup
{
    internal static async ValueTask DisposeAsync(IAsyncDataReader? reader, IAsyncOwnedCommand? command,
        uint? transactionId, ExecutionFailures? failures = null)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var occurrence = ExecutionFailureContexts.CaptureOccurrence();
        try { if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { Add(ref failures, failure, occurrence); }
        occurrence = ExecutionFailureContexts.CaptureOccurrence();
        try { if (command is not null) await command.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { Add(ref failures, failure, occurrence); }
        Report(failures, transactionId);
    }

    internal static void Dispose(IAsyncDataReader? reader, IAsyncOwnedCommand? command, uint? transactionId)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        ExecutionFailures? failures = null;
        var occurrence = ExecutionFailureContexts.CaptureOccurrence();
        try { reader?.Dispose(); }
        catch (Exception failure) { Add(ref failures, failure, occurrence); }
        occurrence = ExecutionFailureContexts.CaptureOccurrence();
        try { command?.Dispose(); }
        catch (Exception failure) { Add(ref failures, failure, occurrence); }
        Report(failures, transactionId);
    }

    private static void Add(ref ExecutionFailures? failures, Exception failure, long occurrence)
    {
        // A successfully settled reader can have handled this same exception.
        // Only a report made during the current resource's disposal may classify
        // its throw. Previously captured primary/secondary snapshots stay intact.
        ExecutionFailureContexts.DiscardEarlierReport(failure, occurrence);
        (failures ??= new()).AddCleanup(failure);
    }

    private static void Report(ExecutionFailures? failures, uint? transactionId)
    {
        if (failures?.Primary is not { } primary) return;
        // The surrounding managed operation assesses permitted recovery after cleanup.
        // A lower-level boundary cannot promise continued transaction use by itself.
        ExecutionFailureContexts.Attach(primary, failures.Snapshot(new(),
            transactionId is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted,
            transactionId is null ? ExecutionRecoveryActions.None : ExecutionRecoveryActions.Dispose, transactionId));
        failures.ThrowIfAny();
    }
}
