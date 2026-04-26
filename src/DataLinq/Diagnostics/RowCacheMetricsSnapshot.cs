using System.Collections.Generic;

namespace DataLinq.Diagnostics;

/// <summary>
/// Row cache and row materialization metrics.
/// </summary>
/// <param name="Hits">Total number of row cache hits.</param>
/// <param name="Misses">Total number of row cache misses.</param>
/// <param name="DatabaseRowsLoaded">Total number of rows loaded from the underlying database.</param>
/// <param name="Materializations">Total number of row materializations into model instances.</param>
/// <param name="Stores">Total number of row cache store operations.</param>
public readonly record struct RowCacheMetricsSnapshot(
    long Hits,
    long Misses,
    long DatabaseRowsLoaded,
    long Materializations,
    long Stores)
{
    internal static RowCacheMetricsSnapshot Sum(IEnumerable<RowCacheMetricsSnapshot> snapshots)
    {
        long hits = 0;
        long misses = 0;
        long databaseRowsLoaded = 0;
        long materializations = 0;
        long stores = 0;

        foreach (var snapshot in snapshots)
        {
            hits += snapshot.Hits;
            misses += snapshot.Misses;
            databaseRowsLoaded += snapshot.DatabaseRowsLoaded;
            materializations += snapshot.Materializations;
            stores += snapshot.Stores;
        }

        return new RowCacheMetricsSnapshot(
            Hits: hits,
            Misses: misses,
            DatabaseRowsLoaded: databaseRowsLoaded,
            Materializations: materializations,
            Stores: stores);
    }
}
