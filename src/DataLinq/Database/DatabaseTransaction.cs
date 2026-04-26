using System;
using System.Data;
using System.Diagnostics;
using DataLinq.Diagnostics;
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
