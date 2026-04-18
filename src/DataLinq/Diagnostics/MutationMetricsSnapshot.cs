using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Diagnostics;

/// <summary>
/// Aggregated mutation metrics captured by DataLinq.
/// </summary>
/// <param name="Inserts">Number of insert mutations executed.</param>
/// <param name="Updates">Number of update mutations executed.</param>
/// <param name="Deletes">Number of delete mutations executed.</param>
/// <param name="Failures">Number of mutation executions that failed.</param>
/// <param name="AffectedRows">Total number of rows reported as affected by mutations.</param>
/// <param name="TotalDurationMicroseconds">Total mutation execution duration in microseconds.</param>
public readonly record struct MutationMetricsSnapshot(
    long Inserts,
    long Updates,
    long Deletes,
    long Failures,
    long AffectedRows,
    long TotalDurationMicroseconds)
{
    public long TotalExecutions => Inserts + Updates + Deletes;
    public double TotalDurationMilliseconds => TotalDurationMicroseconds / 1000d;

    internal static MutationMetricsSnapshot Sum(IEnumerable<MutationMetricsSnapshot> snapshots)
    {
        long inserts = 0;
        long updates = 0;
        long deletes = 0;
        long failures = 0;
        long affectedRows = 0;
        long totalDurationMicroseconds = 0;

        foreach (var snapshot in snapshots)
        {
            inserts += snapshot.Inserts;
            updates += snapshot.Updates;
            deletes += snapshot.Deletes;
            failures += snapshot.Failures;
            affectedRows += snapshot.AffectedRows;
            totalDurationMicroseconds += snapshot.TotalDurationMicroseconds;
        }

        return new MutationMetricsSnapshot(
            Inserts: inserts,
            Updates: updates,
            Deletes: deletes,
            Failures: failures,
            AffectedRows: affectedRows,
            TotalDurationMicroseconds: totalDurationMicroseconds);
    }
}
