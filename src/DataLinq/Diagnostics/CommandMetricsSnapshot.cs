using System.Collections.Generic;

namespace DataLinq.Diagnostics;

/// <summary>
/// Database command execution metrics for a provider instance or the full DataLinq runtime.
/// </summary>
/// <param name="ReaderExecutions">Total number of reader commands executed.</param>
/// <param name="ScalarExecutions">Total number of scalar commands executed.</param>
/// <param name="NonQueryExecutions">Total number of non-query commands executed.</param>
/// <param name="Failures">Total number of command execution failures.</param>
/// <param name="TotalDurationMicroseconds">Total measured command execution duration in microseconds.</param>
public readonly record struct CommandMetricsSnapshot(
    long ReaderExecutions,
    long ScalarExecutions,
    long NonQueryExecutions,
    long Failures,
    long TotalDurationMicroseconds)
{
    public long TotalExecutions => ReaderExecutions + ScalarExecutions + NonQueryExecutions;
    public double TotalDurationMilliseconds => TotalDurationMicroseconds / 1000d;

    internal static CommandMetricsSnapshot Sum(IEnumerable<CommandMetricsSnapshot> snapshots)
    {
        long readerExecutions = 0;
        long scalarExecutions = 0;
        long nonQueryExecutions = 0;
        long failures = 0;
        long totalDurationMicroseconds = 0;

        foreach (var snapshot in snapshots)
        {
            readerExecutions += snapshot.ReaderExecutions;
            scalarExecutions += snapshot.ScalarExecutions;
            nonQueryExecutions += snapshot.NonQueryExecutions;
            failures += snapshot.Failures;
            totalDurationMicroseconds += snapshot.TotalDurationMicroseconds;
        }

        return new CommandMetricsSnapshot(
            ReaderExecutions: readerExecutions,
            ScalarExecutions: scalarExecutions,
            NonQueryExecutions: nonQueryExecutions,
            Failures: failures,
            TotalDurationMicroseconds: totalDurationMicroseconds);
    }
}
