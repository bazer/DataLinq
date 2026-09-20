using System.Diagnostics;
using DataLinq.Diagnostics;
using DataLinq.Mutation;

namespace DataLinq.Execution;

/// <summary>One admitted captured mutation, including hydration and owned cleanup.</summary>
internal struct MutationExecutionTelemetry(DataLinqTelemetryContext context, string tableName,
    TransactionChangeType mutationType, TransactionType transactionType, ExecutionOperationKind operation)
{
    private Activity? activity;
    private long startedAt;
    private bool started;
    private bool completed;

    internal void Start()
    {
        started = true;
        startedAt = Stopwatch.GetTimestamp();
        // Capture ownership before Start invokes fallible observers.
        activity = DataLinqTelemetry.CreateMutationActivity(context, tableName, mutationType, transactionType);
        activity?.Start();
    }

    internal void Complete(ref ExecutionFailures? failures, bool succeeded, int affectedRows)
    {
        if (!started || completed) return;
        completed = true;
        DataLinqTelemetry.RecordMutationExecution(context, tableName, mutationType, transactionType,
            succeeded && failures?.Primary is null, affectedRows, Stopwatch.GetElapsedTime(startedAt), operation, ref failures);
        activity?.SetTag("db.operation.rows_affected", affectedRows);
        ExecutionActivity.Complete(ref activity, ref failures, succeeded, operation);
    }
}
