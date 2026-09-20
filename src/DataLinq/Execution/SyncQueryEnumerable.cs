using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace DataLinq.Execution;

/// <summary>
/// Logical-query telemetry inside ReadSequence's admission and call gate. Each
/// call restores its own caller activity; no ambient scope survives a yielded row.
/// </summary>
internal sealed class SyncQueryEnumerable<T>(IEnumerable<T> rows, QueryTelemetryContext context,
    ReadExecutionIdentity identity, uint? transactionId) : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator() => new Enumerator(rows, context, identity, transactionId);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator(IEnumerable<T> rows, QueryTelemetryContext context,
        ReadExecutionIdentity identity, uint? transactionId) : IEnumerator<T>
    {
        private SyncQueryExecution execution = new(context, identity, transactionId);
        private IEnumerator<T>? iterator;
        private bool started;
        private bool completed;
        public T Current { get; private set; } = default!;
        object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (completed) return false;
            var caller = Activity.Current;
            var ready = true;
            if (!started)
            {
                started = true;
                ready = execution.Start();
            }
            else ready = execution.MakeCurrent();

            var hasRow = false;
            var succeeded = false;
            if (ready)
            {
                try
                {
                    iterator ??= rows.GetEnumerator();
                    hasRow = iterator.MoveNext();
                    if (hasRow) Current = iterator.Current;
                    else succeeded = true;
                }
                catch (Exception failure)
                {
                    hasRow = false;
                    execution.RecordFailure(failure, ExecutionFailureStage.Materialization, ExecutionFailureCause.MaterializationError);
                }
            }
            if (hasRow && execution.RestoreCurrent(caller)) return true;
            Finish(succeeded, caller);
            return false;
        }

        public void Dispose()
        {
            if (completed) return;
            if (!started) { completed = true; return; }
            Finish(false, Activity.Current);
        }

        private void Finish(bool succeeded, Activity? caller)
        {
            completed = true;
            execution.MakeCurrent();
            var reportingScope = ExecutionFailureScope.Current;
            using (ExecutionFailureScope.Begin())
            {
                try { iterator?.Dispose(); }
                catch (Exception failure) { execution.RecordCleanup(failure, reportingScope); }
            }
            iterator = null;
            Current = default!;
            execution.Complete(succeeded);
            execution.RestoreCurrent(caller);
            execution.ThrowIfAny();
        }

        public void Reset() => throw new NotSupportedException();
    }
}
