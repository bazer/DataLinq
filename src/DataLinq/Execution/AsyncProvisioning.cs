using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.Query;
using ThrowAway;

namespace DataLinq.Execution;

// Provisioning executes script text, not general parameterized SQL. Capturing
// only the inputs actually consumed preserves the existing factory contract.
internal sealed record ProvisioningRequest(string Script, string DatabaseName,
    string ConnectionString, bool ForeignKeyRestrict)
{
    // Neither a connection string nor a provisioning script belongs in incidental
    // record formatting (for example, diagnostics of a failed custom factory).
    public override string ToString() => "Captured provisioning request";
}

internal interface IAsyncSqlProvisioningFactory
{
    // I/O-free binding to this factory, its effective settings and immutable inputs. Native
    // provider adoption is W2; a synchronous factory supplies no async fallback.
    IAsyncProvisioningPlan CaptureProvisioning(ProvisioningRequest request);
}

internal interface IAsyncProvisioningPlan
{
    // Validate inputs, lifecycle and actual initialization/execution/cleanup
    // capability before cancellation and before any creation work.
    void Validate();

    // I/O-free construction of owned, unopened execution resources. On failure,
    // the implementation must clean any partial construction it cannot hand off.
    IAsyncProvisioningSession CreateSession();
}

internal interface IAsyncProvisioningSession : IAsyncDisposable
{
    IAsyncDatabaseAccess Access { get; }
    IAsyncOwnedCommandFactory CommandFactory { get; }

    // May create/open the destination and establish required keep-alive ownership.
    // A failed or canceled call can leave effects; DisposeAsync releases only this
    // session's execution resources, never drops objects or destroys a retained DB.
    Task InitializeAsync(CancellationToken cancellationToken);
}

internal static class AsyncProvisioning
{
    internal static Task<Option<int, IDLOptionFailure>> CreateDatabaseAsyncCore(
        this ISqlFromMetadataFactory factory, Sql sql, string databaseName,
        string connectionString, bool foreignKeyRestrict, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(databaseName);
        ArgumentNullException.ThrowIfNull(connectionString);
        var request = new ProvisioningRequest(sql.Text, databaseName, connectionString, foreignKeyRestrict);
        var capability = factory as IAsyncSqlProvisioningFactory
            ?? throw new NotSupportedException("This SQL factory does not support asynchronous provisioning.");
        var plan = capability.CaptureProvisioning(request)
            ?? throw new InvalidOperationException("Provisioning capture returned no execution plan.");
        plan.Validate();
        token.ThrowIfCancellationRequested();
        return ExecuteAsync(plan, token);
    }

    private static async Task<Option<int, IDLOptionFailure>> ExecuteAsync(IAsyncProvisioningPlan plan, CancellationToken token)
    {
        IAsyncProvisioningSession? session = null;
        ExecutionFailures? failures = null;
        var stage = ExecutionFailureStage.Validation;
        var result = 0;
        try
        {
            session = plan.CreateSession() ?? throw new InvalidOperationException("Provisioning created no execution session.");
            // Capture both collaborators before initialization suspends. The command
            // factory is already bound to the provider's captured script and settings.
            var execution = new OwnedCommandExecution(session.Access, session.CommandFactory);
            execution.Validate(AsyncCommandKind.NonQuery);
            token.ThrowIfCancellationRequested();
            stage = ExecutionFailureStage.Initialization;
            await session.InitializeAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            stage = ExecutionFailureStage.CommandExecution;
            result = await execution.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            // A confirmed non-query result is not retroactively canceled. Cleanup
            // must still settle successfully before returning that result.
        }
        catch (Exception failure)
        {
            (failures ??= new()).AddReported(failure, stage,
                failure is OperationCanceledException canceled && canceled.CancellationToken == token && token.IsCancellationRequested
                    ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown);
        }
        finally
        {
            try { if (session is not null) await session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { (failures ??= new()).AddCleanup(cleanup); }
        }
        if (failures?.Primary is { } primary)
        {
            // Provisioning is not a tracked transaction: no rollback/replay or
            // atomicity claim can follow from either success or cleanup.
            ExecutionFailureContexts.Attach(primary, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
                ExecutionRecoveryActions.None, transactionId: null));
            failures.ThrowIfAny();
        }
        return result;
    }
}
