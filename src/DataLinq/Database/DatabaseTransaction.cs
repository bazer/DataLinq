using System;
using System.Data;
using System.Diagnostics;
using DataLinq.Diagnostics;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq;

public enum DatabaseTransactionStatus
{
    Closed,
    Open,
    Committed,
    RolledBack
}

public class DatabaseTransactionStatusChangeEventArgs : EventArgs
{
    public DatabaseTransactionStatus Status { get; set; }
}

public abstract class DatabaseTransaction : DatabaseAccess, IDisposable
{
    private Activity? transactionActivity;
    private long transactionStartedTimestamp;
    private bool transactionTelemetryStarted;
    private bool transactionTelemetryCompleted;

    public DatabaseTransactionStatus Status { get; private set; } = DatabaseTransactionStatus.Closed;

    public event EventHandler<DatabaseTransactionStatusChangeEventArgs>? OnStatusChanged;

    /// <summary>
    /// Gets the underlying provider transaction handle, when one is active.
    /// </summary>
    /// <remarks>
    /// This is a low-level escape hatch. Completing or disposing this handle directly bypasses
    /// <see cref="Transaction"/> cache publication, transaction-local cleanup, and mutable
    /// lifecycle finalization. Complete attached transactions through their DataLinq wrapper.
    /// </remarks>
    public IDbTransaction? DbTransaction { get; protected set; }
    public TransactionType Type { get; protected set; }

    protected DatabaseTransaction(TransactionType type)
        : this((IDatabaseProvider?)null, type)
    {
    }

    protected DatabaseTransaction(IDatabaseProvider? databaseProvider, TransactionType type)
        : base(databaseProvider)
    {
        Type = type;
    }

    protected DatabaseTransaction(IDbTransaction dbTransaction, TransactionType type)
        : this((IDatabaseProvider?)null, dbTransaction, type)
    {
    }

    protected DatabaseTransaction(IDatabaseProvider? databaseProvider, IDbTransaction dbTransaction, TransactionType type)
        : base(databaseProvider)
    {
        DbTransaction = dbTransaction ?? throw new ArgumentNullException(nameof(dbTransaction));
        Type = type;
    }

    protected void SetStatus(DatabaseTransactionStatus status)
    {
        this.Status = status;
        OnStatusChanged?.Invoke(this, new DatabaseTransactionStatusChangeEventArgs { Status = status });
    }

    // The async managed path records confirmed completion before any fallible observer.
    // Existing synchronous providers retain their original combined boundary.
    internal void RecordConfirmedAsyncCompletion(ExecutionCompletion completion) =>
        Status = completion == ExecutionCompletion.Committed
            ? DatabaseTransactionStatus.Committed : DatabaseTransactionStatus.RolledBack;

    internal void NotifyConfirmedAsyncCompletion(ExecutionFailures failures, bool completeTelemetry = true)
    {
        var operation = Status == DatabaseTransactionStatus.Committed ? ExecutionOperationKind.Commit : ExecutionOperationKind.Rollback;
        using (ExecutionFailureScope.Begin())
        {
            try { OnStatusChanged?.Invoke(this, new DatabaseTransactionStatusChangeEventArgs { Status = Status }); }
            catch (Exception failure) { ExecutionActivity.AddFailure(failures, failure, operation); }
        }
        if (completeTelemetry) CompleteAsyncTransactionTelemetry(Status == DatabaseTransactionStatus.Committed
            ? ExecutionCompletion.Committed : ExecutionCompletion.RolledBack, failures, operation);
    }

    // Provider first-use integration point: invoke only after native begin has settled,
    // under the existing initialization owner. This method performs no provider I/O.
    internal void BeginAsyncTransactionTelemetry(ExecutionOperationKind operation)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        if (transactionTelemetryStarted) return;
        transactionTelemetryStarted = true;
        transactionStartedTimestamp = Stopwatch.GetTimestamp();
        ExecutionFailures? failures = null;
        using (ExecutionFailureScope.Begin())
        {
            try
            {
                transactionActivity = DataLinqTelemetry.CreateTransactionActivity(TelemetryContext, Type);
                transactionActivity?.Start();
            }
            catch (Exception failure) { ExecutionActivity.AddFailure(ref failures, failure, operation); }
        }
        DataLinqTelemetry.RecordTransactionStarted(TelemetryContext, operation, ref failures);
        if (failures?.Primary is not { } primary) return;
        CompleteAsyncTransactionTelemetry(ExecutionCompletion.NotAttempted, failures, operation);
        ExecutionFailureContexts.Attach(primary, failures.Snapshot(new(Effects: ExecutionEffects.Initialization),
            ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, null, operation, TelemetryContext.ProviderInstanceId));
        failures.ThrowIfAny();
    }

    internal void CompleteAsyncTransactionTelemetry(ExecutionCompletion completion, ExecutionFailures failures,
        ExecutionOperationKind operation)
    {
        if (!transactionTelemetryStarted || transactionTelemetryCompleted) return;
        // Claim completion before any observer, so failure or later disposal cannot
        // repeat reporting or relabel an uncertain outcome as successful recovery.
        transactionTelemetryCompleted = true;
        var confirmed = completion is ExecutionCompletion.Committed or ExecutionCompletion.RolledBack;
        var outcome = completion == ExecutionCompletion.Committed ? DatabaseTransactionStatus.Committed : DatabaseTransactionStatus.RolledBack;
        ExecutionFailures? reportingFailures = failures;
        DataLinqTelemetry.RecordTransactionCompleted(TelemetryContext, Type, outcome, confirmed,
            Stopwatch.GetElapsedTime(transactionStartedTimestamp), operation, ref reportingFailures);
        if (confirmed && failures.Primary is null) transactionActivity?.SetStatus(ActivityStatusCode.Ok);
        var caller = Activity.Current;
        var activityWasCurrent = ReferenceEquals(caller, transactionActivity);
        ExecutionActivity.Complete(ref transactionActivity, ref reportingFailures, confirmed, operation,
            confirmed ? DataLinqTelemetry.GetTransactionOutcome(outcome) : "failure");
        // A transaction can outlive the query that started it. Activity.Stop restores
        // its original start context even when completion runs under another caller.
        // Preserve that later caller for helper cleanup in this same async invocation.
        if (!activityWasCurrent && !ReferenceEquals(Activity.Current, caller))
        {
            using var restoration = ExecutionFailureScope.Begin();
            try { Activity.Current = caller is { IsStopped: true } ? null : caller; }
            catch (Exception failure) { ExecutionActivity.AddFailure(failures, failure, operation); }
        }
    }

    protected void BeginTransactionTelemetry()
    {
        if (transactionTelemetryStarted)
            return;

        transactionStartedTimestamp = Stopwatch.GetTimestamp();
        transactionActivity = DataLinqTelemetry.StartTransactionActivity(TelemetryContext, Type);
        DataLinqTelemetry.RecordTransactionStarted(TelemetryContext);
        transactionTelemetryStarted = true;
    }

    protected void CompleteTransactionTelemetry(DatabaseTransactionStatus outcome)
    {
        if (!transactionTelemetryStarted || transactionTelemetryCompleted)
            return;

        var duration = Stopwatch.GetElapsedTime(transactionStartedTimestamp);
        DataLinqTelemetry.RecordTransactionCompleted(TelemetryContext, Type, outcome, succeeded: true, duration);
        transactionActivity?.SetTag("datalinq.outcome", DataLinqTelemetry.GetTransactionOutcome(outcome));
        transactionActivity?.SetStatus(ActivityStatusCode.Ok);
        transactionActivity?.Dispose();
        transactionActivity = null;
        transactionTelemetryCompleted = true;
    }

    protected void FailTransactionTelemetry(DatabaseTransactionStatus outcome, Exception ex)
    {
        if (transactionTelemetryCompleted)
            return;

        if (transactionTelemetryStarted)
        {
            var duration = Stopwatch.GetElapsedTime(transactionStartedTimestamp);
            DataLinqTelemetry.RecordTransactionCompleted(TelemetryContext, Type, outcome, succeeded: false, duration);
        }

        if (transactionActivity is not null)
        {
            transactionActivity.SetTag("datalinq.outcome", DataLinqTelemetry.GetTransactionOutcome(outcome));
            DataLinqTelemetry.RecordException(transactionActivity, ex);
            transactionActivity.Dispose();
            transactionActivity = null;
        }

        transactionTelemetryCompleted = true;
    }

    public abstract void Rollback();
    public abstract void Commit();
    public abstract void Dispose();
}
