using System.Collections.Generic;

namespace DataLinq.Diagnostics;

/// <summary>
/// Cache maintenance and cleanup metrics for a table, provider, or the full DataLinq runtime.
/// </summary>
/// <param name="Operations">Total number of cache maintenance operations recorded.</param>
/// <param name="RowsRemoved">Total number of rows removed from caches by maintenance operations.</param>
/// <param name="TotalDurationMicroseconds">Total measured maintenance duration in microseconds.</param>
public readonly record struct CacheCleanupMetricsSnapshot(
    long Operations,
    long RowsRemoved,
    long TotalDurationMicroseconds)
{
    public double TotalDurationMilliseconds => TotalDurationMicroseconds / 1000d;

    internal static CacheCleanupMetricsSnapshot Sum(IEnumerable<CacheCleanupMetricsSnapshot> snapshots)
    {
        long operations = 0;
        long rowsRemoved = 0;
        long totalDurationMicroseconds = 0;

        foreach (var snapshot in snapshots)
        {
            operations += snapshot.Operations;
            rowsRemoved += snapshot.RowsRemoved;
            totalDurationMicroseconds += snapshot.TotalDurationMicroseconds;
        }

        return new CacheCleanupMetricsSnapshot(
            Operations: operations,
            RowsRemoved: rowsRemoved,
            TotalDurationMicroseconds: totalDurationMicroseconds);
    }
}
