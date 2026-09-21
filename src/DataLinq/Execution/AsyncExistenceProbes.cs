using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;

namespace DataLinq.Execution;

internal enum ExistenceProbeKind { FileOrServer, Database, Table }

internal sealed record ExistenceProbeRequest(ExistenceProbeKind Kind, string? DatabaseName, string? TableName)
{
    public override string ToString() => "Captured existence probe request";
}

internal interface IAsyncExistenceProbeSource
{
    // I/O-free binding to this provider's effective identity and captured arguments.
    // Each invocation gets fresh read state. No application transaction, provider
    // reconstruction or database creation.
    ExistenceProbePlan CaptureExistenceProbe(ExistenceProbeRequest request);
}

internal interface IAsyncExistenceProbeSession : IAsyncDisposable
{
    IAsyncDatabaseAccess Access { get; }
    IAsyncOwnedCommandFactory CommandFactory { get; }
    // Open only this invocation's read resources. A missing SQLite file must not
    // be created; an existing named-memory keeper is not owned by this session.
    Task OpenAsync(CancellationToken token);
}

/// <summary>Captured local, scalar or first-row probe; no arbitrary parser can swallow command failures.</summary>
internal sealed class ExistenceProbePlan
{
    internal Action Validate { get; }
    internal Func<bool>? ReadLocal { get; }
    internal Func<IAsyncExistenceProbeSession>? CreateSession { get; }
    internal AsyncCommandKind CommandKind { get; }
    internal Func<object?, bool>? InterpretScalar { get; }
    internal Func<Exception, bool>? IsExpectedAvailabilityFailure { get; }

    private ExistenceProbePlan(Action validate, Func<bool>? readLocal,
        Func<IAsyncExistenceProbeSession>? createSession, AsyncCommandKind kind,
        Func<object?, bool>? interpretScalar, Func<Exception, bool>? expectedAvailabilityFailure)
    {
        Validate = validate;
        ReadLocal = readLocal;
        CreateSession = createSession;
        CommandKind = kind;
        InterpretScalar = interpretScalar;
        IsExpectedAvailabilityFailure = expectedAvailabilityFailure;
    }

    internal static ExistenceProbePlan Local(Action validate, Func<bool> read)
    {
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(read);
        return new(validate, read, null, default, null, null);
    }

    internal static ExistenceProbePlan Scalar(Action validate, Func<IAsyncExistenceProbeSession> createSession,
        Func<object?, bool> interpret, Func<Exception, bool>? expectedAvailabilityFailure = null)
    {
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(createSession);
        ArgumentNullException.ThrowIfNull(interpret);
        return new(validate, null, createSession, AsyncCommandKind.Scalar, interpret, expectedAvailabilityFailure);
    }

    internal static ExistenceProbePlan Reader(Action validate, Func<IAsyncExistenceProbeSession> createSession,
        Func<Exception, bool>? expectedAvailabilityFailure = null)
    {
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(createSession);
        return new(validate, null, createSession, AsyncCommandKind.Reader, null, expectedAvailabilityFailure);
    }
}

internal static class AsyncExistenceProbes
{
    internal static Task<bool> FileOrServerExistsAsyncCore(this IDatabaseProvider provider, CancellationToken token = default) =>
        Begin(provider, new(ExistenceProbeKind.FileOrServer, null, null), token);

    internal static Task<bool> DatabaseExistsAsyncCore(this IDatabaseProvider provider, string? databaseName = null,
        CancellationToken token = default) => Begin(provider, new(ExistenceProbeKind.Database, databaseName, null), token);

    internal static Task<bool> TableExistsAsyncCore(this IDatabaseProvider provider, string tableName, string? databaseName = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrEmpty(tableName)) throw new ArgumentNullException(nameof(tableName));
        return Begin(provider, new(ExistenceProbeKind.Table, databaseName, tableName), token);
    }

    private static Task<bool> Begin(IDatabaseProvider provider, ExistenceProbeRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var source = provider as IAsyncExistenceProbeSource
            ?? throw new NotSupportedException("This provider does not support asynchronous existence probes.");
        var plan = source.CaptureExistenceProbe(request)
            ?? throw new InvalidOperationException("Probe capture returned no plan.");
        plan.Validate();
        token.ThrowIfCancellationRequested();
        return ExecuteAsync(plan, request.Kind, token, provider.TelemetryInstanceId);
    }

    private static async Task<bool> ExecuteAsync(ExistenceProbePlan plan, ExistenceProbeKind kind, CancellationToken token,
        string? providerInstanceId)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        IAsyncExistenceProbeSession? session = null;
        var failures = new ExecutionFailures();
        var stage = ExecutionFailureStage.Validation;
        var result = false;
        try
        {
            if (plan.ReadLocal is { } local)
            {
                // File.Exists and named-memory state checks remain local, direct
                // operations. There is no thread-pool wrapper or native sync fallback.
                stage = ExecutionFailureStage.CommandExecution;
                result = local();
            }
            else
            {
                // Construction is I/O-free and transfers ownership only on success.
                // The creator must clean any partial construction it cannot hand off.
                session = plan.CreateSession!() ?? throw new InvalidOperationException("Probe capture created no session.");
                var execution = new OwnedCommandExecution(session.Access, session.CommandFactory);
                execution.Validate(plan.CommandKind);
                token.ThrowIfCancellationRequested();
                stage = ExecutionFailureStage.Initialization;
                await session.OpenAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                stage = ExecutionFailureStage.CommandExecution;
                if (plan.CommandKind == AsyncCommandKind.Reader)
                {
                    await foreach (var _ in new AsyncReaderEnumerable<bool>(() => execution, static _ => true,
                        cancellationToken: token, identity: new(ExecutionOperationKind.ExistenceCheck, providerInstanceId)).ConfigureAwait(false))
                    {
                        result = true;
                        break;
                    }
                }
                else
                {
                    var scalar = await execution.ExecuteScalarAsync(token).ConfigureAwait(false);
                    stage = ExecutionFailureStage.Materialization;
                    token.ThrowIfCancellationRequested();
                    result = plan.InterpretScalar!(scalar);
                }
            }
        }
        catch (Exception failure)
        {
            failures.AddReported(failure, stage,
                failure is OperationCanceledException canceled && canceled.CancellationToken == token && token.IsCancellationRequested
                    ? ExecutionFailureCause.Cancellation : stage == ExecutionFailureStage.Materialization
                        ? ExecutionFailureCause.MaterializationError : ExecutionFailureCause.Unknown, ExecutionOperationKind.ExistenceCheck);
        }
        finally
        {
            try { if (session is not null) await session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { failures.AddCleanup(cleanup); }
        }
        if (failures.Primary is null)
        {
            try { token.ThrowIfCancellationRequested(); }
            catch (OperationCanceledException canceled)
            { failures.Add(canceled, ExecutionFailureCause.Cancellation, ExecutionFailureStage.Materialization); }
        }
        if (failures.Primary is not { } primary) return result;
        var context = Snapshot(failures, providerInstanceId);
        // Only the availability probe can map an explicitly classified, settled
        // provider/open failure to false. Metadata-query failures are never absence.
        if (kind == ExistenceProbeKind.FileOrServer && !token.IsCancellationRequested &&
            primary is not (OperationCanceledException or ArgumentException or NotSupportedException or ObjectDisposedException) &&
            context.Cause != ExecutionFailureCause.Cancellation &&
            context.Stage is ExecutionFailureStage.Initialization or ExecutionFailureStage.CommandExecution or ExecutionFailureStage.RowLoading &&
            !context.HasCleanupFailure && context.SecondaryFailures.Count == 0 && plan.IsExpectedAvailabilityFailure is { } classify)
        {
            var expected = false;
            try { expected = classify(primary); }
            catch (Exception classificationFailure)
            { failures.AddReported(classificationFailure, ExecutionFailureStage.Validation, fallbackOperation: ExecutionOperationKind.ExistenceCheck); }
            if (expected && !token.IsCancellationRequested) return false;
        }
        ExecutionFailureContexts.Attach(primary, Snapshot(failures, providerInstanceId));
        failures.ThrowIfAny();
        return false; // ThrowIfAny always throws when Primary is present.
    }

    private static ExecutionFailureContext Snapshot(ExecutionFailures failures, string? providerInstanceId) =>
        failures.Snapshot(new(), ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, transactionId: null,
            ExecutionOperationKind.ExistenceCheck, providerInstanceId, providerIdentityIsAuthoritative: true);
}
