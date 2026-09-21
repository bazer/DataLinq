using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;

namespace DataLinq.Execution;

// The mode type stays in its provider assembly. This internal binding does not
// add a core SQLite dependency or a second public journal-mode enum/protocol.
internal interface IAsyncJournalModeSource<TMode> where TMode : struct, Enum
{
    // I/O-free capture of the mode, effective provider identity and settings.
    IAsyncJournalModePlan CaptureJournalMode(TMode mode);
}

internal interface IAsyncJournalModePlan
{
    // Validate inputs/lifecycle and opening, command and cleanup capabilities
    // before pre-cancellation. Native/public mode binding remains W2/W3.
    void Validate();
    // I/O-free construction transfers owned, unopened resources on success.
    // The creator must clean partial construction it cannot hand off.
    IAsyncJournalModeSession CreateSession();
}

internal interface IAsyncJournalModeSession : IAsyncDisposable
{
    IAsyncDatabaseAccess Access { get; }
    IAsyncOwnedCommandFactory CommandFactory { get; }
    // Independent execution resources, never an application's transaction or
    // an existing database keeper. Do not reconstruct a setup-performing provider.
    Task OpenAsync(CancellationToken token);
}

internal static class AsyncJournalMode
{
    internal static Task SetJournalModeAsyncCore<TMode>(this IDatabaseProvider provider, TMode mode,
        CancellationToken token = default) where TMode : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(provider);
        var source = provider as IAsyncJournalModeSource<TMode>
            ?? throw new NotSupportedException("This provider does not support asynchronous journal-mode configuration for this mode type.");
        var plan = source.CaptureJournalMode(mode)
            ?? throw new InvalidOperationException("Journal-mode capture returned no plan.");
        plan.Validate();
        token.ThrowIfCancellationRequested();
        return ExecuteAsync(plan, token, provider.TelemetryInstanceId);
    }

    private static async Task ExecuteAsync(IAsyncJournalModePlan plan, CancellationToken token, string? providerInstanceId)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        IAsyncJournalModeSession? session = null;
        ExecutionFailures? failures = null;
        var stage = ExecutionFailureStage.Validation;
        var localCause = ExecutionFailureCause.Unknown;
        var occurrence = ExecutionFailureContexts.CaptureOccurrence();
        try
        {
            session = plan.CreateSession();
            if (session is null)
            {
                localCause = ExecutionFailureCause.InvalidOperation;
                throw new InvalidOperationException("Journal-mode capture created no session.");
            }
            occurrence = ExecutionFailureContexts.CaptureOccurrence();
            var execution = new OwnedCommandExecution(session.Access, session.CommandFactory);
            execution.Validate(AsyncCommandKind.NonQuery);
            token.ThrowIfCancellationRequested();
            stage = ExecutionFailureStage.Initialization;
            occurrence = ExecutionFailureContexts.CaptureOccurrence();
            await session.OpenAsync(token).ConfigureAwait(false);
            occurrence = ExecutionFailureContexts.CaptureOccurrence();
            token.ThrowIfCancellationRequested();
            stage = ExecutionFailureStage.CommandExecution;
            await execution.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            // The setter confirms command completion, not an effective mode or
            // row count. Late cancellation cannot undo a confirmed command.
        }
        catch (Exception failure)
        {
            // A later phase cannot borrow a report caught during completed setup.
            ExecutionFailureContexts.DiscardEarlierReport(failure, occurrence);
            (failures ??= new()).AddReported(failure, stage,
                failure is OperationCanceledException canceled && canceled.CancellationToken == token && token.IsCancellationRequested
                    ? ExecutionFailureCause.Cancellation : localCause, ExecutionOperationKind.ProviderConfiguration);
        }
        finally
        {
            if (session is not null)
            {
                using var cleanupScope = ExecutionFailureScope.Begin();
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { (failures ??= new()).AddCleanup(cleanup); }
            }
        }
        if (failures?.Primary is { } primary)
        {
            ExecutionFailureContexts.Attach(primary, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
                ExecutionRecoveryActions.None, transactionId: null, ExecutionOperationKind.ProviderConfiguration, providerInstanceId,
                providerIdentityIsAuthoritative: true));
            failures.ThrowIfAny();
        }
    }
}
