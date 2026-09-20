using System;
using System.Diagnostics;
using DataLinq.Diagnostics;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Execution;

/// <summary>Captured logical-query dimensions; private command/row loads do not create another query.</summary>
internal readonly record struct QueryTelemetryContext(
    DataLinqTelemetryContext Provider, string? TableName, bool Scalar, bool Transactional)
{
    internal static QueryTelemetryContext Capture(IDataSourceAccess source, string tableName, bool scalar = false) =>
        new(DataLinqTelemetryContext.FromProvider(source.Provider), tableName, scalar, source is Transaction);
    internal string Kind => Scalar ? "scalar" : "entity";
}

/// <summary>
/// One admitted logical query, including local composition and owned cleanup. Listener failures
/// are ordered after the execution failure and never prevent other reporting or resource cleanup.
/// </summary>
internal struct QueryExecutionTelemetry(QueryTelemetryContext context)
{
    private Activity? activity;
    private long startedAt;
    private bool started;
    private bool completed;

    internal void Start()
    {
        if (context.TableName is null) return;
        started = true;
        startedAt = Stopwatch.GetTimestamp();
        DataLinqMetrics.RecordQueryExecution(context.Provider, context.Scalar);
        // Own the activity before invoking ActivityStarted: a throwing listener
        // must not lose the activity that has already become current.
        activity = DataLinqTelemetry.CreateQueryActivity(context.Provider, context.TableName, context.Kind, context.Transactional);
        activity?.Start();
    }

    internal void MakeCurrent()
    {
        if (activity is { IsStopped: false } && !ReferenceEquals(Activity.Current, activity)) Activity.Current = activity;
    }

    internal void Complete(ref ExecutionFailures? failures, bool succeeded)
    {
        if (!started || completed) return;
        completed = true;
        var duration = Stopwatch.GetElapsedTime(startedAt);
        DataLinqTelemetry.RecordQueryExecution(context.Provider, context.TableName!, context.Kind,
            context.Transactional, succeeded && failures?.Primary is null, duration, ref failures);
        ExecutionActivity.Complete(ref activity, ref failures, succeeded, ExecutionOperationKind.Query);
    }

    internal static void AddFailure(ExecutionFailures failures, Exception failure) =>
        ExecutionActivity.AddFailure(failures, failure, ExecutionOperationKind.Query);

    internal static void AddFailure(ref ExecutionFailures? failures, Exception failure) =>
        AddFailure(failures ??= new(), failure);
}
