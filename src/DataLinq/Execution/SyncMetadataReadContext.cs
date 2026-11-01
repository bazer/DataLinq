using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using DataLinq.Query;

namespace DataLinq.Execution;

/// <summary>Sequential, buffered native synchronous commands on one owned metadata session.</summary>
internal sealed class SyncMetadataReadContext
{
    private readonly object gate = new();
    private readonly ISyncMetadataSession session;
    private readonly int? timeout;
    private readonly string? providerInstanceId;
    private readonly List<ObservedExecutionFailure> observedFailures = [];
    private bool active;
    private bool closed;
    private long completedCommandOccurrence;

    internal SyncMetadataReadContext(ISyncMetadataSession session, int? timeout, string? providerInstanceId)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.timeout = timeout;
        this.providerInstanceId = providerInstanceId;
        session.Validate(timeout);
    }

    internal IReadOnlyList<T> Read<T>(Sql sql, Func<IDataLinqDataReader, T> materialize)
    {
        ArgumentNullException.ThrowIfNull(materialize);
        return Run<IReadOnlyList<T>>(sql, invocation =>
        {
            var reader = invocation.Reader = session.ExecuteReader(invocation.Command!)
                ?? throw new InvalidOperationException("Metadata reader acquisition returned no reader.");
            var rows = new List<T>();
            while (true)
            {
                invocation.Stage = ExecutionFailureStage.RowLoading;
                if (!reader.ReadNextRow()) break;
                invocation.Stage = ExecutionFailureStage.Materialization;
                rows.Add(materialize(reader));
            }
            return rows.AsReadOnly();
        });
    }

    internal object? ExecuteScalar(Sql sql) => Run(sql, invocation => session.ExecuteScalar(invocation.Command!));

    private T Run<T>(Sql sql, Func<Invocation, T> execute)
    {
        ArgumentNullException.ThrowIfNull(sql);
        lock (gate)
        {
            if (closed) throw new InvalidOperationException("This metadata read has ended.");
            if (active)
            {
                var failure = new InvalidOperationException("A metadata command is still active.");
                observedFailures.Add(new(failure, null));
                throw failure;
            }
            if (observedFailures.Count != 0) throw new InvalidOperationException("This metadata read has failed.");
            active = true;
        }

        using var diagnostics = ExecutionFailureScope.Begin();
        var failures = new ExecutionFailures();
        var invocation = new Invocation();
        var occurrence = ExecutionFailureContexts.CaptureOccurrence();
        var result = default(T)!;
        try
        {
            try
            {
                invocation.Command = session.CreateCommand(CapturedSql.Capture(sql))
                    ?? throw new InvalidOperationException("Metadata command creation returned no command.");
                if (timeout is { } seconds) invocation.Command.CommandTimeout = seconds;
                invocation.Stage = ExecutionFailureStage.CommandExecution;
                occurrence = ExecutionFailureContexts.CaptureOccurrence();
                result = execute(invocation);
            }
            catch (Exception failure)
            {
                ExecutionFailureContexts.DiscardEarlierReport(failure, occurrence);
                failures.AddReported(failure, invocation.Stage, invocation.Stage == ExecutionFailureStage.Materialization
                    ? ExecutionFailureCause.MaterializationError : ExecutionFailureCause.Unknown, ExecutionOperationKind.MetadataRead);
            }
            finally
            {
                Dispose(invocation.Reader, failures);
                Dispose(invocation.Command, failures);
            }

            if (failures.Primary is { } primary)
            {
                var evidence = new ReadFailureEvidence();
                if (session is IAsyncReadFailureEvidence classifier)
                {
                    try { evidence = classifier.GetReadFailureEvidence(primary) ?? throw new InvalidOperationException("Metadata failure classification returned no evidence."); }
                    catch (Exception classificationFailure)
                    { failures.AddReported(classificationFailure, ExecutionFailureStage.Validation, fallbackOperation: ExecutionOperationKind.MetadataRead); }
                }
                var context = failures.Snapshot(evidence, ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None,
                    null, ExecutionOperationKind.MetadataRead, providerInstanceId, providerIdentityIsAuthoritative: true);
                ExecutionFailureContexts.Attach(primary, context);
                lock (gate) observedFailures.Add(new(primary, context));
                failures.ThrowIfAny();
            }
            return result;
        }
        finally
        {
            lock (gate)
            {
                if (failures.Primary is null)
                    Volatile.Write(ref completedCommandOccurrence, ExecutionFailureContexts.CaptureOccurrence());
                active = false;
                Monitor.PulseAll(gate);
            }
        }
    }

    private static void Dispose(IDisposable? resource, ExecutionFailures failures)
    {
        if (resource is null) return;
        using var cleanup = ExecutionFailureScope.Begin();
        try { resource.Dispose(); }
        catch (Exception failure) { failures.AddCleanup(failure); }
    }

    internal void DiscardCompletedCommandReport(Exception failure) =>
        ExecutionFailureContexts.DiscardEarlierReport(failure, Volatile.Read(ref completedCommandOccurrence));

    internal void CopyFailuresTo(ExecutionFailures destination)
    {
        lock (gate)
            foreach (var failure in observedFailures)
                destination.AddObserved(failure, ExecutionFailureStage.Validation, ExecutionFailureCause.InvalidOperation,
                    ExecutionOperationKind.MetadataRead);
    }

    internal void Close(ExecutionFailures destination)
    {
        lock (gate)
        {
            closed = true;
            CopyFailuresTo(destination);
            if (active && destination.Primary is null)
                destination.Add(new InvalidOperationException("The metadata parser returned with an unfinished command."),
                    ExecutionFailureCause.InvalidOperation, ExecutionFailureStage.Materialization, ExecutionOperationKind.MetadataRead);
            // A malformed internal parser may have dispatched on another thread.
            // Never dispose its session or publish metadata before that work settles.
            while (active) Monitor.Wait(gate);
            CopyFailuresTo(destination);
        }
    }

    private sealed class Invocation
    {
        internal IDbCommand? Command;
        internal IDataLinqDataReader? Reader;
        internal ExecutionFailureStage Stage = ExecutionFailureStage.Validation;
    }
}
