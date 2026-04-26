using System.Collections.Generic;

namespace DataLinq.Diagnostics;

/// <summary>
/// Current cache occupancy metrics for a table, provider, or the full DataLinq runtime.
/// </summary>
/// <param name="Rows">Current number of rows stored in the row cache.</param>
/// <param name="TransactionRows">Current number of rows stored in transaction-local caches.</param>
/// <param name="Bytes">Current estimated row cache size in bytes.</param>
/// <param name="IndexEntries">Current number of cached index entries.</param>
public readonly record struct CacheOccupancyMetricsSnapshot(
    long Rows,
    long TransactionRows,
    long Bytes,
    long IndexEntries)
{
    internal static CacheOccupancyMetricsSnapshot Sum(IEnumerable<CacheOccupancyMetricsSnapshot> snapshots)
    {
        long rows = 0;
        long transactionRows = 0;
        long bytes = 0;
        long indexEntries = 0;

        foreach (var snapshot in snapshots)
        {
            rows += snapshot.Rows;
            transactionRows += snapshot.TransactionRows;
            bytes += snapshot.Bytes;
            indexEntries += snapshot.IndexEntries;
        }

        return new CacheOccupancyMetricsSnapshot(
            Rows: rows,
            TransactionRows: transactionRows,
            Bytes: bytes,
            IndexEntries: indexEntries);
    }
}
