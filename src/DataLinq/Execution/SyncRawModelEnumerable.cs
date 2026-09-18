using System;
using System.Collections.Generic;
using DataLinq.Mutation;

namespace DataLinq.Execution;

/// <summary>One captured raw-model invocation. Reserve is I/O-free and claims cleanup ownership.</summary>
internal interface ISyncRawModelReaderSource
{
    ExecutionFailureStage Stage { get; }
    void Reserve();
    IDataLinqDataReader OpenReader(TransactionOperationGate.Step owner);
    ExecutionFailures? Dispose(IDataLinqDataReader? reader, ExecutionFailures? failures);
    ReadFailureEvidence GetFailureEvidence(Exception failure);
}

internal sealed record SyncRawModelPlan<T>(ISyncRawModelReaderSource Source, Func<IDataLinqDataReader, T> Materialize);

/// <summary>Materialization and cleanup form one raw operation, owned until recovery is published.</summary>
internal static class SyncRawModelEnumerable
{
    internal static IEnumerable<T> Create<T>(Transaction transaction,
        Func<TransactionOperationGate.Step, SyncRawModelPlan<T>> capture)
        => new GuardedEnumerable<T>(tracked => Read(transaction, capture, tracked));

    private static IEnumerable<T> Read<T>(Transaction transaction,
        Func<TransactionOperationGate.Step, SyncRawModelPlan<T>> capture, IHelperTrackedReader tracked)
    {
        const string operation = "read synchronous raw models";
        using var ownership = DataSourceAccess.BeginRead(transaction, operation)!;
        ownership.RegisterReader(tracked);
        ISyncRawModelReaderSource? source = null;
        IDataLinqDataReader? reader = null;
        ExecutionFailures? failures = null;
        var reserved = false;
        var stage = ExecutionFailureStage.Validation;
        try
        {
            SyncRawModelPlan<T> plan;
            try
            {
                plan = capture(ownership.Step);
                source = plan.Source ?? throw new InvalidOperationException("Raw model capture returned no reader source.");
                source.Reserve();
                reserved = true;
                reader = source.OpenReader(ownership.Step)
                    ?? throw new InvalidOperationException("Raw model acquisition returned no reader.");
            }
            catch (Exception failure)
            {
                Record(failure, reserved ? source!.Stage : ExecutionFailureStage.Validation);
                throw;
            }
            while (true)
            {
                bool hasRow;
                T model = default!;
                try
                {
                    stage = ExecutionFailureStage.RowLoading;
                    transaction.EnsureCanRead(operation, ownership.Step);
                    hasRow = reader.ReadNextRow();
                    if (hasRow)
                    {
                        stage = ExecutionFailureStage.Materialization;
                        model = plan.Materialize(reader);
                    }
                }
                catch (Exception failure) { Record(failure, stage); throw; }
                if (!hasRow) yield break;
                yield return model;
            }
        }
        finally
        {
            // A rejected shared invocation must not dispose a different caller's work.
            if (reserved)
            {
                try { failures = source!.Dispose(reader, failures); }
                catch (Exception cleanup) { (failures ??= new()).AddCleanup(cleanup); }
            }
            if (failures?.Primary is not null)
            {
                SyncRawExecution.PublishFailure(failures, transaction, ownership,
                    reserved ? source!.GetFailureEvidence : static _ => new(
                        Effects: ExecutionEffects.NoStatement, Integrity: TransactionIntegrity.Confirmed));
                failures.ThrowIfAny();
            }
        }

        void Record(Exception failure, ExecutionFailureStage failureStage) =>
            (failures ??= new()).AddReported(failure, failureStage,
                failureStage == ExecutionFailureStage.Materialization
                    ? ExecutionFailureCause.MaterializationError : ExecutionFailureCause.Unknown);
    }
}

internal static class SyncReaderCleanup
{
    internal static ExecutionFailures? Dispose(IDataLinqDataReader? reader, ExecutionFailures? failures)
    {
        // Keep known reader and command failures separate. Public legacy disposal keeps
        // its AggregateException contract; managed model execution can preserve its primary.
        if (reader is OwnedCommandDataReader owned) return owned.DisposeWithFailures(failures);
        try { reader?.Dispose(); }
        catch (Exception cleanup) { (failures ??= new()).AddCleanup(cleanup); }
        return failures;
    }
}
