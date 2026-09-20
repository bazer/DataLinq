using System.Data;
using System.Diagnostics;
using DataLinq.Diagnostics;
using DataLinq.Mutation;

namespace DataLinq.Execution;

/// <summary>One provider dispatch, ending at execution or reader acquisition, not later reader lifetime.</summary>
internal struct CommandExecutionTelemetry(DataLinqTelemetryContext context, string kind, bool transactional,
    TransactionType? transactionType)
{
    private Activity? activity;
    private string operation = "unknown";
    private long startedAt;
    private bool dispatched;
    internal bool Dispatched => dispatched;

    internal void Start(IDbCommand command)
    {
        operation = DataLinqTelemetry.GetCommandOperation(command);
        activity = DataLinqTelemetry.CreateCommandActivity(context, kind, operation, transactional, transactionType);
        activity?.Start();
    }

    internal void BeginDispatch()
    {
        startedAt = Stopwatch.GetTimestamp();
        dispatched = true;
    }

    internal void Complete(ref ExecutionFailures? failures, bool succeeded)
    {
        // Start observers and pre-dispatch cancellation do not count as executed commands.
        if (dispatched)
            DataLinqTelemetry.RecordCommand(context, kind, operation, transactional, transactionType,
                succeeded, Stopwatch.GetElapsedTime(startedAt), ref failures);
        if (succeeded && failures?.Primary is null) activity?.SetStatus(ActivityStatusCode.Ok);
        // The physical SQL operation is a telemetry dimension, not requested Save/Query/etc.
        // Leave diagnostic operation attribution to the explicit enclosing owner.
        ExecutionActivity.Complete(ref activity, ref failures, succeeded, ExecutionOperationKind.Unknown);
    }
}
