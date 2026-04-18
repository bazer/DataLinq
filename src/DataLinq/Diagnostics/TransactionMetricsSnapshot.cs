using System.Collections.Generic;

namespace DataLinq.Diagnostics;

/// <summary>
/// Database transaction lifecycle metrics for a provider instance or the full DataLinq runtime.
/// </summary>
/// <param name="Starts">Total number of transactions started.</param>
/// <param name="Commits">Total number of transactions committed.</param>
/// <param name="Rollbacks">Total number of transactions rolled back.</param>
/// <param name="Failures">Total number of transaction completion failures.</param>
/// <param name="TotalDurationMicroseconds">Total measured transaction duration in microseconds.</param>
public readonly record struct TransactionMetricsSnapshot(
    long Starts,
    long Commits,
    long Rollbacks,
    long Failures,
    long TotalDurationMicroseconds)
{
    public double TotalDurationMilliseconds => TotalDurationMicroseconds / 1000d;

    internal static TransactionMetricsSnapshot Sum(IEnumerable<TransactionMetricsSnapshot> snapshots)
    {
        long starts = 0;
        long commits = 0;
        long rollbacks = 0;
        long failures = 0;
        long totalDurationMicroseconds = 0;

        foreach (var snapshot in snapshots)
        {
            starts += snapshot.Starts;
            commits += snapshot.Commits;
            rollbacks += snapshot.Rollbacks;
            failures += snapshot.Failures;
            totalDurationMicroseconds += snapshot.TotalDurationMicroseconds;
        }

        return new TransactionMetricsSnapshot(
            Starts: starts,
            Commits: commits,
            Rollbacks: rollbacks,
            Failures: failures,
            TotalDurationMicroseconds: totalDurationMicroseconds);
    }
}
