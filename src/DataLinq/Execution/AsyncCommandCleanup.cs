using System;
using System.Threading.Tasks;

namespace DataLinq.Execution;

internal static class AsyncCommandCleanup
{
    internal static async ValueTask DisposeAsync(IAsyncDataReader? reader, IAsyncOwnedCommand? command,
        uint? transactionId, ExecutionFailures? failures = null)
    {
        try { if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { Add(ref failures, failure); }
        try { if (command is not null) await command.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { Add(ref failures, failure); }
        Report(failures, transactionId);
    }

    internal static void Dispose(IAsyncDataReader? reader, IAsyncOwnedCommand? command, uint? transactionId)
    {
        ExecutionFailures? failures = null;
        try { reader?.Dispose(); }
        catch (Exception failure) { Add(ref failures, failure); }
        try { command?.Dispose(); }
        catch (Exception failure) { Add(ref failures, failure); }
        Report(failures, transactionId);
    }

    private static void Add(ref ExecutionFailures? failures, Exception failure) =>
        (failures ??= new()).AddCleanup(failure);

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
